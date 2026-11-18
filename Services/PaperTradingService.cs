using System.IO;
using DumbTrader.Models;

namespace DumbTrader.Services;

public sealed class PaperTradingService(StrategyCandidateRepository repository, StrategyCompiler compiler, RiskGate risk,
    TimeProvider? timeProvider = null) : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Market, string Symbol), Session> _paper = new();
    private readonly Dictionary<(string Market, string Symbol), Session> _active = new();
    private readonly Dictionary<(string Market, string Symbol), StrategyCandidate?> _champions = new();
    private DateTime _readSettingsAt;
    private StrategyEvolutionOptions _settings = new();
    private FileStream? _ownership;
    private DateTime UtcNow => (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public event EventHandler<VersionedStrategySignal>? ActiveSignal;

    public Guid? ActiveVersionId(string market, string symbol)
    {
        lock (_gate)
        {
            var key = Key(market, symbol);
            if (!_champions.TryGetValue(key, out var candidate))
                _champions[key] = candidate = repository.Active(market, symbol);
            return candidate?.Id;
        }
    }

    public StrategyPaperReport? Current(string market, string symbol)
    {
        lock (_gate) return _paper.GetValueOrDefault(Key(market, symbol))?.Report;
    }

    public void Start(Guid id)
    {
        lock (_gate)
        {
            EnsureOwner();
            var candidate = repository.Get(id);
            if (candidate.Status is not (CandidateStatus.Evaluated or CandidateStatus.Paper)
                || candidate.Evaluation?.Failures.Count != 0)
                throw new InvalidOperationException("평가를 통과한 후보를 선택하세요.");
            var key = Key(candidate.Market, candidate.Symbol);
            if (_paper.ContainsKey(key)) throw new InvalidOperationException("이 종목에 실행 중인 모의 운용이 있습니다.");
            if (repository.LoadOptions().KillSwitch) throw new InvalidOperationException("킬 스위치가 켜져 있습니다.");
            var session = Create(candidate);
            repository.Update(id, candidate.Status, CandidateStatus.Paper, "모의 운용 시작", item => item with { Paper = session.Report });
            _paper.Add(key, session);
        }
    }

    public void Stop(string market, string symbol)
    {
        lock (_gate)
            if (_paper.TryGetValue(Key(market, symbol), out var session))
                StopPaper(Key(market, symbol), session, "사용자가 모의 운용을 중단했습니다.");
    }

    public void Approve(Guid id)
    {
        lock (_gate)
        {
            EnsureOwner();
            var candidate = repository.Get(id);
            var key = Key(candidate.Market, candidate.Symbol);
            if (!_paper.TryGetValue(key, out var session) || session.Candidate.Id != id)
                throw new InvalidOperationException("모의 운용을 시작하고 표본을 확보하세요.");
            var report = session.Report;
            var limits = repository.LoadOptions();
            if (limits.KillSwitch || report.Candidate.HasPosition || session.HasPending
                || report.Candidate.Trades < Math.Max(limits.MinPaperTrades, session.Options.MinPaperTrades)
                || report.LastQuoteUtc - report.StartedAtUtc < TimeSpan.FromHours(Math.Max(limits.MinPaperHours, session.Options.MinPaperHours))
                || report.Candidate.NetProfit <= 0 || report.Candidate.NetProfit < report.Champion.NetProfit
                || report.Candidate.MaxDrawdown > Math.Min(limits.MaxDrawdown, session.Options.MaxDrawdown)
                || risk.CheckQuote(limits, report.LastQuoteUtc, UtcNow, 0) is not null)
                throw new InvalidOperationException("모의 운용 기간·거래 수·성과·시세 및 FLAT 조건을 확인하세요.");
            repository.Update(id, CandidateStatus.Paper, CandidateStatus.Approved, "사용자가 신호 버전을 승인했습니다.",
                item => item with { Paper = report with { StopReason = "승인으로 모의 운용 종료" } });
            _paper.Remove(key);
        }
    }

    public StrategyCandidate? Activate(Guid id, bool rollback)
    {
        lock (_gate)
        {
            EnsureOwner();
            var candidate = repository.Get(id);
            var key = Key(candidate.Market, candidate.Symbol);
            var current = _active.GetValueOrDefault(key);
            if (current is null && repository.Active(candidate.Market, candidate.Symbol) is { } existing)
                _active[key] = current = Create(existing);
            if (_paper.ContainsKey(key) || current is { SafeToSwitch: false })
                throw new InvalidOperationException("가상 포지션과 미체결 신호가 없는 시점까지 전환을 보류합니다.");
            if (!rollback)
            {
                var limits = repository.LoadOptions();
                if (limits.KillSwitch || candidate.Paper is null
                    || candidate.Paper.Candidate.MaxDrawdown > limits.MaxDrawdown
                    || candidate.Paper.Candidate.Trades < limits.MinPaperTrades
                    || candidate.Paper.LastQuoteUtc - candidate.Paper.StartedAtUtc < TimeSpan.FromHours(limits.MinPaperHours)
                    || current is not null && risk.CheckQuote(limits, current.Report.LastQuoteUtc, UtcNow, 0) is not null)
                    throw new InvalidOperationException("현재 위험 설정 또는 활성 전략의 가상 포지션 상태를 확인하세요.");
                _ = Create(candidate);
            }
            var next = repository.SwitchActive(id, rollback, rollback ? "사용자가 이전 신호 버전으로 복구했습니다." : "사용자가 신호 버전을 활성화했습니다.");
            _active.Remove(key);
            _champions[key] = next;
            return next;
        }
    }

    public void OnTrade(string market, string symbol, DateTime exchangeTime, DateTime quoteUtc, double price, long volume)
    {
        VersionedStrategySignal? published = null;
        var key = Key(market, symbol);
        lock (_gate)
        {
            if (!_champions.TryGetValue(key, out var champion))
                _champions[key] = champion = repository.Active(market, symbol);
            if (!_paper.ContainsKey(key) && champion is null) return;
            if (EnsureOwner())
                _champions[key] = champion = repository.Active(market, symbol);
            var now = UtcNow;
            if (now - _readSettingsAt >= TimeSpan.FromSeconds(2))
            {
                _settings = repository.LoadOptions();
                _readSettingsAt = now;
            }
            var error = risk.CheckQuote(_settings, quoteUtc, now, price);
            if (market == "Stock" && (price <= 0 || volume <= 0)) return;
            if (market == "OFuture" && error is null && !risk.InConfiguredSession(_settings, quoteUtc)) return;
            if (_paper.TryGetValue(key, out var paper))
            {
                var step = Process(paper, exchangeTime, quoteUtc, now, price, volume, error);
                if (step.Stop is not null) StopPaper(key, paper, step.Stop);
            }
            if (champion is not null)
            {
                if (!_active.TryGetValue(key, out var active))
                {
                    try { _active[key] = active = Create(champion); }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Text.Json.JsonException)
                    {
                        const string reason = "활성 버전의 코드 또는 체크포인트를 읽을 수 없습니다.";
                        _champions[key] = repository.SwitchActive(champion.Id, rollback: true, reason);
                        published = new(champion.Id, market, symbol, quoteUtc, null, reason);
                    }
                }
                if (active is not null)
                {
                    var step = Process(active, exchangeTime, quoteUtc, now, price, volume, error);
                    if (step.Stop is not null)
                    {
                        // Block immediately, even if the database fails to commit the rollback.
                        if (!_settings.KillSwitch)
                        {
                            active.Blocked = true;
                            RollBack(key, active, step.Stop);
                        }
                        published = new(champion.Id, market, symbol, quoteUtc, null, step.Stop);
                    }
                    else if (step.Signal is not null)
                        published = new(champion.Id, market, symbol, quoteUtc, step.Signal, null);
                }
            }
        }
        if (published is not null) ActiveSignal?.Invoke(this, published);
    }

    public void Sweep()
    {
        lock (_gate)
        {
            var now = UtcNow;
            var limits = repository.LoadOptions();
            _settings = limits;
            _readSettingsAt = now;
            foreach (var (key, session) in _paper.ToArray())
            {
                var last = session.Report.LastQuoteUtc == default ? session.Report.StartedAtUtc : session.Report.LastQuoteUtc;
                if (limits.KillSwitch || risk.FeedTimedOut(session.Instrument, limits, now, last) || now - last > TimeSpan.FromDays(4))
                    StopPaper(key, session, limits.KillSwitch ? "킬 스위치" : "시세 수신 시간 초과");
            }
            if (limits.KillSwitch) return;
            foreach (var (key, session) in _active.ToArray())
            {
                var last = session.Report.LastQuoteUtc == default ? session.Report.StartedAtUtc : session.Report.LastQuoteUtc;
                if (!risk.FeedTimedOut(session.Instrument, limits, now, last)) continue;
                session.Blocked = true;
                const string reason = "활성 버전 시세 수신 시간 초과";
                RollBack(key, session, reason);
                ActiveSignal?.Invoke(this, new(session.Candidate.Id, key.Market, key.Symbol, last, null, reason));
            }
        }
    }

    private (StrategySignal? Signal, string? Stop) Process(Session session, DateTime exchangeTime, DateTime quoteUtc,
        DateTime now, double price, long volume, string? quoteError)
    {
        if (session.Blocked || quoteError is not null) return (null, quoteError ?? "차단된 신호 버전");
        if (exchangeTime == default) return (null, "유효하지 않은 거래 시각");
        try
        {
            var prefill = risk.CheckLimits(session.Instrument, _settings, session.CandidateLedger.Position,
                session.CandidateLedger.NetAt(price) - (exchangeTime.Date == session.Day ? session.DayStartNet : session.Report.Candidate.NetProfit),
                session.CandidateLedger.DrawdownAt(price));
            if (prefill is not null) return (null, prefill);
            var signal = session.Process(exchangeTime, quoteUtc, now, price, volume, _settings.MaxQuantity, repository);
            var limits = risk.CheckLimits(session.Instrument, _settings, session.CandidateLedger.Position,
                session.CandidateLedger.NetAt(price) - session.DayStartNet, session.Report.Candidate.MaxDrawdown);
            if (limits is not null) return (null, limits);
            if (signal is not null) session.Errors = 0;
            return (signal, null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArithmeticException)
        {
            session.ResetPending();
            session.Errors++;
            return (null, session.Errors >= _settings.MaxSignalErrors ? ex.Message : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            session.Blocked = true;
            return (null, "모의 체결 기록을 저장할 수 없습니다.");
        }
    }

    private Session Create(StrategyCandidate candidate)
    {
        var data = repository.ReadSnapshot(candidate.Id);
        var session = new Session(candidate, data, compiler.Compile(repository.ReadCode(candidate)), compiler.Compile(data.ChampionCode), UtcNow);
        if (candidate.Status == CandidateStatus.Active && repository.ReadCheckpoint<Session.Checkpoint>(candidate) is { } saved)
            session.Restore(saved);
        return session;
    }

    private void StopPaper((string Market, string Symbol) key, Session session, string reason)
    {
        _paper.Remove(key);
        repository.Update(session.Candidate.Id, CandidateStatus.Paper, CandidateStatus.Paper, reason,
            item => item with { Paper = session.Report with { StopReason = reason } });
    }

    private void RollBack((string Market, string Symbol) key, Session session, string reason)
    {
        _champions[key] = repository.SwitchActive(session.Candidate.Id, rollback: true, reason);
        _active.Remove(key);
    }

    private static (string Market, string Symbol) Key(string market, string symbol) => (market, symbol.ToUpperInvariant());

    private bool EnsureOwner()
    {
        if (_ownership is not null) return false;
        try { _ownership = repository.AcquireOperationLock(); }
        catch (IOException ex) { throw new InvalidOperationException("다른 앱 인스턴스가 모의 운용 또는 활성 신호를 관리하고 있습니다.", ex); }
        _champions.Clear();
        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var (key, session) in _paper.ToArray())
                try { StopPaper(key, session, "앱 종료"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                    Microsoft.Data.Sqlite.SqliteException or InvalidOperationException or System.Text.Json.JsonException) { }
            _ownership?.Dispose();
            _ownership = null;
        }
    }

    private sealed class Session
    {
        internal sealed record Checkpoint(Guid SessionId, DateTime Started, DateTime LastQuote, DateTime LastTrade,
            DateTime LastFilledBucket, int Samples, int Differences, long Lag, DateTime Day, double DayStartNet,
            StrategySignalRunner.Checkpoint Candidate, StrategySignalRunner.Checkpoint Champion,
            StrategyLedger.Checkpoint CandidateLedger, StrategyLedger.Checkpoint ChampionLedger);

        private readonly StrategySignalRunner _candidate;
        private readonly StrategySignalRunner _champion;
        private readonly StrategyLedger _championLedger;
        private Guid _sessionId = Guid.NewGuid();
        private DateTime _started;
        private readonly int _interval;
        private StrategyBar? _bar;
        private StrategySignal? _candidatePending;
        private StrategySignal? _championPending;
        private DateTime _lastQuote;
        private DateTime _lastTrade;
        private DateTime _lastFilledBucket;
        private DateTime _skipThroughBucket;
        private bool _firstBucket = true;
        private int _samples;
        private int _differences;
        private long _lag;
        public StrategyCandidate Candidate { get; }
        public StrategyInstrument Instrument { get; }
        public StrategyEvolutionOptions Options { get; }
        public StrategyLedger CandidateLedger { get; }
        public DateTime Day { get; private set; }
        public double DayStartNet { get; private set; }
        public int Errors { get; set; }
        public bool Blocked { get; set; }
        public bool HasPending => _candidatePending is { Action: not StrategyAction.Hold };
        public bool SafeToSwitch => CandidateLedger.Position == 0 && !HasPending;
        public StrategyPaperReport Report => new(_sessionId, _started, _lastQuote, CandidateLedger.Metrics,
            _championLedger.Metrics, _differences, _lag, _samples, null);

        public Session(StrategyCandidate candidate, StrategySnapshot snapshot,
            Func<double[], StrategySignal> strategy, Func<double[], StrategySignal> champion, DateTime now)
        {
            Candidate = candidate;
            _started = now;
            Instrument = snapshot.Instrument;
            Options = snapshot.Options;
            _interval = snapshot.IntervalMinutes;
            _candidate = new(strategy);
            _champion = new(champion);
            var costs = Instrument.Market == "Stock" ? Options.StockCosts : Options.Costs;
            var capital = Options.CapitalFor(Instrument);
            CandidateLedger = new(Instrument, costs, capital, Options.MaxQuantity);
            _championLedger = new(Instrument, costs, capital, Options.MaxQuantity);
            foreach (var bar in snapshot.Bars.TakeLast(21))
            {
                _candidate.Run(bar, 0);
                _champion.Run(bar, 0);
            }
        }

        public void Restore(Checkpoint state)
        {
            (_sessionId, _started, _lastQuote, _lastTrade, _lastFilledBucket, _samples, _differences, _lag, Day, DayStartNet)
                = (state.SessionId, state.Started, state.LastQuote, state.LastTrade, state.LastFilledBucket,
                    state.Samples, state.Differences, state.Lag, state.Day, state.DayStartNet);
            _candidate.Restore(state.Candidate);
            _champion.Restore(state.Champion);
            CandidateLedger.Restore(state.CandidateLedger);
            _championLedger.Restore(state.ChampionLedger);
            _skipThroughBucket = _lastFilledBucket;
        }

        private Checkpoint Save() => new(_sessionId, _started, _lastQuote, _lastTrade, _lastFilledBucket, _samples,
            _differences, _lag, Day, DayStartNet, _candidate.Save(), _champion.Save(), CandidateLedger.Save(), _championLedger.Save());

        public StrategySignal? Process(DateTime time, DateTime quote, DateTime now, double price, long volume, int maxQuantity,
            StrategyCandidateRepository repository)
        {
            if (time < _lastTrade || quote < _lastQuote) return null;
            _lastTrade = time;
            _lastQuote = quote;
            if (time.Date != Day)
            {
                Day = time.Date;
                DayStartNet = CandidateLedger.Metrics.NetProfit;
            }
            _lag = Math.Max(0, (long)(now - quote).TotalMilliseconds);
            var bucket = _interval == 0 ? time.Date : time.Date.AddMinutes((int)time.TimeOfDay.TotalMinutes / _interval * _interval);
            // Any bucket that was already in progress at restart lacks complete OHLCV.
            if (bucket <= _skipThroughBucket) return null;
            if (_bar is null)
            {
                _bar = new(bucket, price, price, price, price, Math.Max(0, volume));
                return null;
            }
            if (bucket == _bar.Time)
            {
                _bar = _bar with { High = Math.Max(_bar.High, price), Low = Math.Min(_bar.Low, price),
                    Close = price, Volume = checked(_bar.Volume + Math.Max(0, volume)) };
                return null;
            }
            var completed = _bar;
            _bar = new(bucket, price, price, price, price, Math.Max(0, volume));
            if (_firstBucket)
            {
                _firstBucket = false;
                return null;
            }
            var candidatePosition = CandidateLedger.Position;
            var championPosition = _championLedger.Position;
            try
            {
                CandidateLedger.Mark(completed.Close);
                _championLedger.Mark(completed.Close);
                _candidatePending = _candidate.Run(completed, CandidateLedger.Position);
                _championPending = _champion.Run(completed, _championLedger.Position);
                var signal = _candidatePending with { QuantityHint = Math.Min(_candidatePending.QuantityHint, maxQuantity) };
                var championSignal = _championPending with { QuantityHint = Math.Min(_championPending.QuantityHint, maxQuantity) };
                CandidateLedger.Fill(signal, price);
                _championLedger.Fill(championSignal, price);
                CandidateLedger.Mark(price);
                _championLedger.Mark(price);
                _samples++;
                if (signal.Action != championSignal.Action || signal.QuantityHint != championSignal.QuantityHint) _differences++;
                _lastFilledBucket = bucket;
                if (Candidate.Status == CandidateStatus.Active) repository.SaveCheckpoint(Candidate, Save());
                repository.RecordPaperSample(Candidate.Id, _sessionId, new(quote, completed, signal,
                    championSignal, CandidateLedger.Position, _championLedger.Position, price,
                    CandidateLedger.Position - candidatePosition, _championLedger.Position - championPosition, _lag, null, Report));
                return signal;
            }
            catch (InvalidOperationException ex)
            {
                repository.RecordPaperSample(Candidate.Id, _sessionId, new(quote, completed, null, null,
                    CandidateLedger.Position, _championLedger.Position, price,
                    CandidateLedger.Position - candidatePosition, _championLedger.Position - championPosition, _lag, ex.Message, Report));
                throw;
            }
            finally { ResetPending(); }
        }

        public void ResetPending()
        {
            _candidatePending = null;
            _championPending = null;
        }
    }
}

public sealed record VersionedStrategySignal(Guid CandidateId, string Market, string Symbol, DateTime QuoteUtc,
    StrategySignal? Signal, string? Error);
