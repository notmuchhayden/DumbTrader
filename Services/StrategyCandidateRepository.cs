using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DumbTrader.Models;
using Microsoft.Data.Sqlite;

namespace DumbTrader.Services;

public sealed class StrategyCandidateRepository
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _connection;
    private readonly Lazy<bool> _initialized;
    public string Root { get; }

    public StrategyCandidateRepository() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DumbTrader", "strategy-evolution")) { }

    public StrategyCandidateRepository(string root)
    {
        Root = Path.GetFullPath(root);
        _connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(Root, "candidates.db"), DefaultTimeout = 5 }.ToString();
        _initialized = new Lazy<bool>(Initialize);
    }

    private bool Initialize()
    {
        Directory.CreateDirectory(Root);
        using var db = Connect();
        using var cmd = Command(db, null, """
            CREATE TABLE IF NOT EXISTS Candidates (
                Id TEXT PRIMARY KEY, Market TEXT NOT NULL, Symbol TEXT NOT NULL, Record TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS CandidateSymbol ON Candidates (Market, Symbol);
            CREATE TABLE IF NOT EXISTS Champions (
                Market TEXT NOT NULL, Symbol TEXT NOT NULL, Id TEXT NOT NULL, PRIMARY KEY (Market, Symbol));
            CREATE TABLE IF NOT EXISTS Settings (
                Revision INTEGER PRIMARY KEY, ChangedAtUtc TEXT NOT NULL, Value TEXT NOT NULL);
            """);
        cmd.ExecuteNonQuery();
        return true;
    }

    public StrategyEvolutionOptions LoadOptions()
    {
        using var db = Open();
        using var cmd = Command(db, null, "SELECT Value FROM Settings ORDER BY Revision DESC LIMIT 1");
        var options = cmd.ExecuteScalar() is string record ? Read<StrategyEvolutionOptions>(record) : new();
        options.Validate();
        return options;
    }

    public void SaveOptions(StrategyEvolutionOptions options)
    {
        options.Validate();
        var record = JsonSerializer.Serialize(options, Json);
        using var db = Open();
        using var cmd = Command(db, null, """
            INSERT INTO Settings (ChangedAtUtc, Value)
            SELECT $at, $value WHERE $value IS NOT (SELECT Value FROM Settings ORDER BY Revision DESC LIMIT 1)
            """, ("$at", DateTime.UtcNow.ToString("O")), ("$value", record));
        cmd.ExecuteNonQuery();
    }

    public FileStream AcquireGenerationLock() => AcquireLock("generation.lock");
    internal FileStream AcquireOperationLock() => AcquireLock("operation.lock");

    private FileStream AcquireLock(string name)
    {
        Directory.CreateDirectory(Root);
        return new FileStream(Path.Combine(Root, name), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public IReadOnlyList<StrategyCandidate> List(string market, string symbol)
    {
        using var db = Open();
        using var cmd = Command(db, null, "SELECT Record FROM Candidates WHERE Market = $market AND Symbol = $symbol",
            ("$market", market), ("$symbol", Normalize(symbol)));
        using var reader = cmd.ExecuteReader();
        var candidates = new List<StrategyCandidate>();
        while (reader.Read()) candidates.Add(Read<StrategyCandidate>(reader.GetString(0)));
        return candidates.OrderByDescending(candidate => candidate.CreatedAtUtc).ToArray();
    }

    public StrategyCandidate Get(Guid id)
    {
        using var db = Open();
        return Get(db, null, id);
    }

    public StrategyCandidate? Active(string market, string symbol)
    {
        using var db = Open();
        return Active(db, null, market, symbol);
    }

    public IReadOnlyList<Guid> ActiveIds()
    {
        using var db = Open();
        using var cmd = Command(db, null, "SELECT Id FROM Champions");
        using var reader = cmd.ExecuteReader();
        var ids = new List<Guid>();
        while (reader.Read()) ids.Add(Guid.Parse(reader.GetString(0)));
        return ids;
    }

    public StrategyCandidate Create(StrategyInstrument instrument, Guid? parent, string? model)
    {
        var now = DateTime.UtcNow;
        var candidate = new StrategyCandidate(Guid.NewGuid(), instrument.Market, Normalize(instrument.Symbol), now,
            parent, null, model, CandidateStatus.Generating, null, null, null, null,
            [new(now, CandidateStatus.Generating, "후보 작업 시작")]);
        using var db = Open();
        using var cmd = Command(db, null, """
            INSERT INTO Candidates (Id, Market, Symbol, Record) VALUES ($id, $market, $symbol, $record)
            """, ("$id", candidate.Id.ToString()), ("$market", candidate.Market),
            ("$symbol", candidate.Symbol), ("$record", JsonSerializer.Serialize(candidate, Json)));
        cmd.ExecuteNonQuery();
        return candidate;
    }

    public StrategyCandidate Update(Guid id, CandidateStatus expected, CandidateStatus status, string reason,
        Func<StrategyCandidate, StrategyCandidate>? details = null)
    {
        if (!IsTransitionAllowed(expected, status))
            throw new InvalidOperationException($"허용되지 않는 상태 전환: {expected} → {status}");
        using var db = Open();
        using var tx = db.BeginTransaction();
        var current = Get(db, tx, id);
        if (current.Status != expected) throw new InvalidOperationException("후보 상태가 이미 변경되었습니다.");
        var next = Transition(details?.Invoke(current) ?? current, status, reason);
        Save(db, tx, next);
        tx.Commit();
        return next;
    }

    internal StrategyCandidate? SwitchActive(Guid id, bool rollback, string reason)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        var target = Get(db, tx, id);
        var active = Active(db, tx, target.Market, target.Symbol);
        if (rollback ? active?.Id != id : target.Status != CandidateStatus.Approved || target.ParentId != active?.Id)
            throw new InvalidOperationException("활성 후보 또는 승인 기준 버전이 변경되었습니다.");
        var next = rollback && target.ParentId is { } parent ? Get(db, tx, parent) : rollback ? null : target;
        if (next is not null)
        {
            _ = ReadCode(next);
            next = Transition(next, CandidateStatus.Active, reason);
            Save(db, tx, next);
        }
        if (active is not null) Save(db, tx, Transition(active, CandidateStatus.RolledBack, reason));

        using var cmd = next is null
            ? Command(db, tx, "DELETE FROM Champions WHERE Market = $market AND Symbol = $symbol",
                ("$market", target.Market), ("$symbol", target.Symbol))
            : Command(db, tx, "INSERT OR REPLACE INTO Champions (Market, Symbol, Id) VALUES ($market, $symbol, $id)",
                ("$market", target.Market), ("$symbol", target.Symbol), ("$id", next.Id.ToString()));
        cmd.ExecuteNonQuery();
        tx.Commit();
        return next;
    }

    public string DirectoryFor(Guid id) => Path.Combine(Root, "versions", id.ToString("N"));
    public void WriteArtifact(Guid id, string name, string content) => AtomicWrite(Path.Combine(DirectoryFor(id), name), content);
    public string ReadArtifact(Guid id, string name) => File.ReadAllText(Path.Combine(DirectoryFor(id), name));
    public void RecordPaperSample(Guid id, Guid session, StrategyPaperSample sample)
    {
        File.AppendAllText(Path.Combine(DirectoryFor(id), $"paper-{session:N}.jsonl"),
            JsonSerializer.Serialize(sample) + Environment.NewLine);
        AtomicWrite(PaperReportPath(id, session), JsonSerializer.Serialize(sample.Report), overwrite: true);
    }

    public StrategyPaperReport? LatestPaperReport(StrategyCandidate candidate)
    {
        if (candidate.Paper is not { StopReason: null } report) return candidate.Paper;
        var path = PaperReportPath(candidate.Id, report.SessionId);
        return File.Exists(path) ? Read<StrategyPaperReport>(File.ReadAllText(path)) : report;
    }

    private string PaperReportPath(Guid id, Guid session) => Path.Combine(DirectoryFor(id), $"paper-{session:N}-latest.json");
    public void SaveSnapshot(Guid id, StrategySnapshot snapshot)
    {
        var data = JsonSerializer.Serialize(snapshot, Json);
        WriteArtifact(id, "snapshot.json", data);
        WriteArtifact(id, "snapshot.sha256", Hash(data));
    }

    public StrategySnapshot ReadSnapshot(Guid id)
    {
        var data = ReadArtifact(id, "snapshot.json");
        if (Hash(data) != ReadArtifact(id, "snapshot.sha256"))
            throw new InvalidOperationException("평가 스냅샷 체크섬이 일치하지 않습니다.");
        return Read<StrategySnapshot>(data);
    }

    internal bool ClaimFinalWindow(Guid id, IEnumerable<StrategyCandidate> recent, int interval, DateTime from, DateTime through)
    {
        foreach (var previous in recent)
        {
            var path = Path.Combine(DirectoryFor(previous.Id), "final-window.json");
            if (!File.Exists(path)) continue;
            var claim = Read<FinalWindow>(File.ReadAllText(path));
            if (claim.Interval == interval && from <= claim.Through) return false;
        }
        WriteArtifact(id, "final-window.json", JsonSerializer.Serialize(new FinalWindow(interval, from, through), Json));
        return true;
    }

    private sealed record FinalWindow(int Interval, DateTime From, DateTime Through);

    internal void SaveCheckpoint<T>(StrategyCandidate candidate, T state)
        => AtomicWrite(CheckpointPath(candidate), JsonSerializer.Serialize(state), overwrite: true);

    internal T? ReadCheckpoint<T>(StrategyCandidate candidate) where T : class
    {
        var path = CheckpointPath(candidate);
        return File.Exists(path) ? Read<T>(File.ReadAllText(path)) : null;
    }

    private string CheckpointPath(StrategyCandidate candidate) =>
        Path.Combine(DirectoryFor(candidate.Id), $"active-{candidate.ActivatedAtUtc?.Ticks}.json");

    public string WriteCode(Guid id, string code)
    {
        WriteArtifact(id, "candidate.cs", code);
        return Hash(ReadArtifact(id, "candidate.cs"));
    }

    public string ReadCode(StrategyCandidate candidate)
    {
        var code = ReadArtifact(candidate.Id, "candidate.cs");
        if (Hash(code) != candidate.CodeHash) throw new InvalidOperationException("후보 코드 체크섬이 일치하지 않습니다.");
        return code;
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string Normalize(string symbol) => symbol.Trim().ToUpperInvariant();
    private static T Read<T>(string content) => JsonSerializer.Deserialize<T>(content, Json)
        ?? throw new InvalidOperationException("전략 기록이 비어 있습니다.");

    private static void AtomicWrite(string path, string content, bool overwrite = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Encoding.UTF8.GetBytes(content));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite);
        }
        finally { File.Delete(temp); }
    }

    private SqliteConnection Open()
    {
        _ = _initialized.Value;
        return Connect();
    }

    private SqliteConnection Connect()
    {
        var db = new SqliteConnection(_connection);
        try { db.Open(); return db; }
        catch { db.Dispose(); throw; }
    }

    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql,
        params (string Key, string Value)[] parameters)
    {
        var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = sql;
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value);
        return command;
    }

    private static StrategyCandidate Get(SqliteConnection db, SqliteTransaction? tx, Guid id)
    {
        using var cmd = Command(db, tx, "SELECT Record FROM Candidates WHERE Id = $id", ("$id", id.ToString()));
        return cmd.ExecuteScalar() is string record ? Read<StrategyCandidate>(record)
            : throw new InvalidOperationException("해당 후보를 찾을 수 없습니다.");
    }

    private static StrategyCandidate? Active(SqliteConnection db, SqliteTransaction? tx, string market, string symbol)
    {
        using var cmd = Command(db, tx, """
            SELECT c.Record FROM Champions a JOIN Candidates c ON c.Id = a.Id WHERE a.Market = $market AND a.Symbol = $symbol
            """, ("$market", market), ("$symbol", Normalize(symbol)));
        return cmd.ExecuteScalar() is string record ? Read<StrategyCandidate>(record) : null;
    }

    private static void Save(SqliteConnection db, SqliteTransaction tx, StrategyCandidate candidate)
    {
        using var cmd = Command(db, tx, "UPDATE Candidates SET Record = $record WHERE Id = $id",
            ("$record", JsonSerializer.Serialize(candidate, Json)), ("$id", candidate.Id.ToString()));
        cmd.ExecuteNonQuery();
    }

    private static StrategyCandidate Transition(StrategyCandidate candidate, CandidateStatus status, string reason)
    {
        var now = DateTime.UtcNow;
        return candidate with
        {
            Status = status,
            FailureReason = status == CandidateStatus.Rejected ? reason : candidate.FailureReason,
            ApprovedAtUtc = status == CandidateStatus.Approved ? now : candidate.ApprovedAtUtc,
            ActivatedAtUtc = status == CandidateStatus.Active ? now : candidate.ActivatedAtUtc,
            Audit = [.. candidate.Audit, new(now, status, reason)]
        };
    }

    private static bool IsTransitionAllowed(CandidateStatus from, CandidateStatus to) => (from, to) switch
    {
        (CandidateStatus.Generating, CandidateStatus.Generated) => true,
        (CandidateStatus.Generated, CandidateStatus.Compiled) => true,
        (CandidateStatus.Compiled, CandidateStatus.Evaluated) => true,
        (CandidateStatus.Evaluated or CandidateStatus.Paper, CandidateStatus.Paper) => true,
        (CandidateStatus.Paper, CandidateStatus.Approved) => true,
        (_, CandidateStatus.Rejected) => from is not (CandidateStatus.Active or CandidateStatus.RolledBack or CandidateStatus.Rejected),
        _ => false
    };
}
