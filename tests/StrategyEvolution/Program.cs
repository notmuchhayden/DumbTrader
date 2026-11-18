using System.Globalization;
using System.Text.Json;
using DumbTrader.Models;
using DumbTrader.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

if (args.FirstOrDefault() == "exec")
{
    var mode = args[Array.IndexOf(args, "--model") + 1];
    var output = args[Array.IndexOf(args, "--output-last-message") + 1];
    await Console.In.ReadToEndAsync();
    if (mode == "timeout") await Task.Delay(TimeSpan.FromSeconds(30));
    if (mode == "auth")
    {
        Console.Error.WriteLine("401 auth rejected");
        Environment.ExitCode = 1;
        return;
    }
    var payload = mode == "invalid" ? "{" : JsonSerializer.Serialize(new GeneratedStrategy("1", "fixture CLI"));
    File.WriteAllText(output, payload);
    return;
}

var root = Directory.CreateTempSubdirectory("dumbtrader-evolution-").FullName;
var originalDirectory = Directory.GetCurrentDirectory();
try
{
    Directory.SetCurrentDirectory(root);
    await RunAsync(root);
    Console.WriteLine("Strategy evolution checks passed.");
}
finally
{
    Directory.SetCurrentDirectory(originalDirectory);
    SqliteConnection.ClearAllPools();
    Directory.Delete(root, recursive: true);
}

static async Task RunAsync(string root)
{
    VerifyChartRefreshPolicy();
    var compiler = new StrategyCompiler();
    var input = new double[14];
    input[3] = 50;
    var signal = compiler.Compile("(Close > 42 ? (1 / 2) * 2 : 0, State + 1)")(input);
    Check(signal is { Action: StrategyAction.Buy, QuantityHint: 1, StrategyState: 1 }, "restricted signal contract");
    foreach (var code in new[] { """System.IO.File.ReadAllText("x")""", "1; return 2", "Close + true",
        "Close > 3", "#if true\n1\n#endif", "1e999", "new System.Random().Next()" })
        Throws<InvalidOperationException>(() => compiler.Compile(code), code);
    Throws<InvalidOperationException>(() => compiler.Compile("1 / 0")(input), "nonfinite runtime result");
    Throws<InvalidOperationException>(() => compiler.Compile("1.5")(input), "fractional quantity");

    var repository = new StrategyCandidateRepository(Path.Combine(root, "versions"));
    var options = new StrategyEvolutionOptions
    {
        Costs = new(Commission: 1, SlippageTicks: 1),
        MinPaperHours = 1, MinPaperTrades = 2, MinTradesPerWindow = 2,
        MaxQuoteAgeSeconds = 60, CliPath = Path.Combine(root, "missing.exe")
    };
    repository.SaveOptions(options);
    var factory = new TestDbFactory();
    var instrument = new StrategyInstrument("FIXTURE", "USD", 1, 1, 100);
    var start = new DateTime(2024, 1, 1, 9, 0, 0);
    await using (var db = factory.CreateDbContext())
    {
        db.EnsureCreatedAndExtended();
        for (var i = 0; i <= 300; i++)
        {
            var time = start.AddMinutes(i);
            var price = i % 2 == 0 ? 100 : 120;
            db.OFutureMinuteBars.Add(new OFutureMinuteBar
            {
                Symbol = instrument.Symbol, IntervalMinutes = 1,
                TradeDate = time.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                TradeTime = time.ToString("HHmmss", CultureInfo.InvariantCulture),
                Open = price, High = price, Low = price, Close = price, Volume = 1
            });
            if (i < 180)
                db.StockChartDatas.Add(new StockChartData
                {
                    shcode = "123456", date = start.AddDays(i).ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                    open = price, high = price, low = price, close = price, jdiff_vol = 1
                });
        }
        await db.SaveChangesAsync();
    }
    var store = new OFutureChartStoreService(factory);
    var latestMinute = await store.ReadLatestMinuteBarAsync(instrument.Symbol);
    Check(latestMinute?.TradeTime == "140000", "chart cache reads newest saved minute");
    var cached = new OFutureMinuteBar
    {
        Symbol = "CACHE", IntervalMinutes = 1, TradeDate = "20241117", TradeTime = "095900",
        Open = 100, High = 100, Low = 100, Close = 100, Volume = 1, ReceivedAtUtc = DateTime.UtcNow
    };
    await store.UpsertMinuteBarsAsync([cached]);
    cached.Close = cached.High = 102;
    cached.Volume = 3;
    cached.ReceivedAtUtc = cached.ReceivedAtUtc.AddSeconds(1);
    await store.UpsertMinuteBarsAsync([cached]);
    var cacheDay = new DateTime(2024, 11, 17);
    await store.RebuildTenMinuteBarsAsync(cached.Symbol, cacheDay, cacheDay);
    Check((await store.ReadMinuteBarsAsync(cached.Symbol, 1, cacheDay, cacheDay)).Single().Close == 102
        && (await store.ReadMinuteBarsAsync(cached.Symbol, 10, cacheDay, cacheDay)).Single() is
            { Close: 102, TradeTime: "095000", Volume: 3 }, "incremental minute upsert and local ten-minute rebuild");
    var snapshots = new MarketSnapshotBuilder(factory);
    var evaluation = new StrategyEvaluationService();
    var generator = new FixtureGenerator();
    using var coordinator = new StrategyEvolutionCoordinator(repository, snapshots, generator, compiler, evaluation);
    var candidate = (await coordinator.RunAsync(instrument, 1, start))!;
    Check(candidate.Status == CandidateStatus.Evaluated, candidate.FailureReason ?? "accepted candidate");
    var snapshot = repository.ReadSnapshot(candidate.Id);
    Check(snapshot.Bars.Length == 300 && snapshot.Bars[^1].Time == start.AddMinutes(299), "snapshot excludes latest bucket");
    var validationStart = snapshot.Bars[StrategyEvaluationService.TrainingEnd(snapshot.Bars.Length)].Time;
    Check(!generator.Prompt!.Contains(validationStart.ToString("s"), StringComparison.Ordinal), "holdout absent from prompt");
    Check(snapshot.Bars.Length > 150 && candidate.Evaluation!.Windows.Count == 4, "four chronological windows");
    var expected = evaluation.Evaluate(snapshot, compiler.Compile(repository.ReadCode(candidate)),
        compiler.Compile(snapshot.ChampionCode), CancellationToken.None);
    Check(JsonSerializer.Serialize(expected) == JsonSerializer.Serialize(candidate.Evaluation), "deterministic replay");
    Check(expected.Windows.All(window => window.Candidate.NetProfit < window.Candidate.GrossProfit
        && window.DoubleCostProfit <= window.Candidate.NetProfit), "commission and slippage applied");
    var duplicate = (await coordinator.RunAsync(instrument, 1, start))!;
    Check(duplicate.Status == CandidateStatus.Rejected && duplicate.Evaluation!.Windows.Count == 3, "fresh holdout required");
    generator.Code = "Close && Volume";
    var invalid = (await coordinator.RunAsync(instrument, 1, start))!;
    Check(invalid.Status == CandidateStatus.Rejected && invalid.FailureReason!.Contains("CS0019"), "compiler diagnostics recorded");
    generator.Code = "Close > 110 ? 1 : -1";

    var stock = MarketSnapshotBuilder.Stock(new StockInfo { shcode = "123456", gubun = "2" });
    var stockResult = (await coordinator.RunAsync(stock, 0, start))!;
    Check(stockResult.Evaluation?.Windows[0].Candidate.TotalCosts > 0, "stock fees and taxes");
    var missing = (await coordinator.RunAsync(instrument with { Symbol = "MISSING" }, 1, start))!;
    Check(missing.Status == CandidateStatus.Rejected && missing.FailureReason!.Contains("150"), "empty data rejects");
    var missingCli = new CodexStrategyGenerator(repository);
    await ThrowsAsync<System.ComponentModel.Win32Exception>(() =>
        missingCli.GenerateAsync(Guid.NewGuid(), "test", options, CancellationToken.None), "missing CLI");
    var cliOptions = options with { CliPath = Path.Combine(AppContext.BaseDirectory, "StrategyEvolution.Tests.exe"),
        Model = "ok", TimeoutSeconds = 10 };
    var cliId = Guid.NewGuid();
    var result = await missingCli.GenerateAsync(cliId, "test", cliOptions, CancellationToken.None);
    Check(result.Code == "1" && repository.ReadArtifact(cliId, "codex-run.json").Contains("ExitCode"), "CLI contract and log");
    await ThrowsAsync<InvalidOperationException>(() => missingCli.GenerateAsync(Guid.NewGuid(), "test",
        cliOptions with { Model = "invalid" }, CancellationToken.None), "CLI invalid output");
    await ThrowsAsync<InvalidOperationException>(() => missingCli.GenerateAsync(Guid.NewGuid(), "test",
        cliOptions with { Model = "auth" }, CancellationToken.None), "CLI authentication failure");
    await ThrowsAsync<TimeoutException>(() => missingCli.GenerateAsync(Guid.NewGuid(), "test",
        cliOptions with { Model = "timeout", TimeoutSeconds = 2 }, CancellationToken.None), "CLI deadline");

    generator.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
    using (var token = new CancellationTokenSource())
    {
        var pending = coordinator.RunAsync(instrument, 1, start, cancellationToken: token.Token);
        await generator.Hold.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(instrument, 1, start), "duplicate runs");
        token.Cancel();
        Check((await pending)!.Status == CandidateStatus.Rejected, "cancelled generation");
    }
    generator.Hold = null;

    var clock = new TestClock(new DateTimeOffset(2025, 4, 10, 9, 0, 0, TimeSpan.Zero));
    using (var paper = new PaperTradingService(repository, compiler, new RiskGate(), clock))
    {
        paper.Start(candidate.Id);
        Throws<InvalidOperationException>(() => paper.Approve(candidate.Id), "insufficient paper sample");
        Throws<InvalidOperationException>(() => paper.Activate(candidate.Id, false), "activation requires approval");
        using (var competing = new PaperTradingService(repository, compiler, new RiskGate(), clock))
            Throws<InvalidOperationException>(() => competing.Start(candidate.Id), "exclusive paper session");
        for (var i = 0; i <= 64; i++)
        {
            Feed(paper, clock, instrument.Symbol, i % 2 == 0 ? 100 : 120);
            var report = paper.Current(instrument.Market, instrument.Symbol);
            if (i >= 60 && report?.Candidate is { HasPosition: false, Trades: >= 2 }) break;
        }
        Check(paper.Current(instrument.Market, instrument.Symbol)?.Candidate.NetProfit > 0, "next-bucket paper fills");
        clock.Now = clock.Now.AddSeconds(-30);
        paper.Approve(candidate.Id);
        Check(paper.Activate(candidate.Id, false)?.Status == CandidateStatus.Active, "explicit signal activation");
        clock.Now = clock.Now.AddSeconds(30);
        Feed(paper, clock, instrument.Symbol, 100);
        Feed(paper, clock, instrument.Symbol, 120);
        Feed(paper, clock, instrument.Symbol, 100);
        Throws<InvalidOperationException>(() => paper.Activate(candidate.Id, true), "open virtual position blocks switching");
    }

    var reopened = new StrategyCandidateRepository(repository.Root);
    Check(reopened.Active(instrument.Market, instrument.Symbol)?.Id == candidate.Id, "active pointer persisted");
    using (var recovered = new PaperTradingService(reopened, compiler, new RiskGate(), clock))
    {
        Throws<InvalidOperationException>(() => recovered.Activate(candidate.Id, true), "open position restored after restart");
        var now = clock.GetUtcNow().UtcDateTime;
        recovered.OnTrade(instrument.Market, instrument.Symbol, now, now.AddMinutes(-5), 110, 1);
        Check(reopened.Active(instrument.Market, instrument.Symbol) is null
            && reopened.Get(candidate.Id).Status == CandidateStatus.RolledBack, "stale quote automatically rolls back");
    }

    File.AppendAllText(Path.Combine(repository.DirectoryFor(candidate.Id), "candidate.cs"), " + 1");
    Throws<InvalidOperationException>(() => repository.ReadCode(candidate), "code checksum");
    var risk = new RiskGate();
    Check(risk.CheckQuote(options with { KillSwitch = true }, clock.GetUtcNow().UtcDateTime, clock.GetUtcNow().UtcDateTime, 100) is not null,
        "kill switch");
    Check(risk.CheckLimits(instrument, options, 2, 0, 0) is not null
        && risk.CheckLimits(instrument, options, 1, -options.DailyLossLimit, 0) is not null, "position and loss limits");
    var sessionOptions = options with { FuturesSessionHoursKorea = new(new(9, 0), new(17, 0)) };
    var regular = new DateTime(2025, 4, 10, 3, 0, 0, DateTimeKind.Utc);
    Check(risk.FeedTimedOut(instrument, sessionOptions, regular, regular.AddMinutes(-2))
        && !risk.FeedTimedOut(instrument, sessionOptions, regular.AddHours(7), regular), "session-aware feed timeout");
}

static void VerifyChartRefreshPolicy()
{
    var today = new DateTime(2026, 11, 18);
    var oldest = today.AddYears(-1);
    var now = new DateTime(2026, 11, 18, 1, 0, 0, DateTimeKind.Utc);
    Check(OFutureChartCachePolicy.DailyRequestStart(today, oldest, today) is null, "latest daily cache skips TR");
    Check(OFutureChartCachePolicy.DailyRequestStart(today.AddDays(-1), oldest, today) == today.AddDays(-1),
        "daily TR starts at the last cached bar");
    Check(OFutureChartCachePolicy.DailyRequestStart(null, oldest, today) == oldest, "empty daily cache loads the requested range");
    Check(OFutureChartCachePolicy.DailyRequestStart(today.AddDays(2), oldest, today.AddDays(4)) is null,
        "Friday daily cache skips TR on Sunday");

    var latest = new OFutureMinuteBar { TradeDate = "20261117", TradeTime = "195400", ReceivedAtUtc = now.AddMinutes(-3) };
    var trade = new OFutureRealData { OverseasDate = "20261117", TradeTime = "195500", ReceivedAtUtc = now };
    Check(OFutureChartCachePolicy.MinuteRequest(null, null, now, oldest) == (oldest, 2000), "first minute load also accepts prior dates");
    Check(OFutureChartCachePolicy.MinuteRequest(latest, trade, now, oldest) is null, "fresh exchange minute skips TR");
    trade.TradeTime = "195900";
    Check(OFutureChartCachePolicy.MinuteRequest(latest, trade, now, oldest) == (today.AddDays(-1).AddHours(19).AddMinutes(54), 50),
        "short gap requests a small page from the cached minute");
    trade.OverseasDate = "20261118";
    trade.TradeTime = "030000";
    Check(OFutureChartCachePolicy.MinuteRequest(latest, trade, now, oldest)?.Count == 2000, "cross-session gap requests a larger page");
    Check(OFutureChartCachePolicy.MinuteRequest(latest, null, now, oldest) is null, "recent cache stays usable without a live quote");
    Check(OFutureChartCachePolicy.MinuteRequest(latest, null, now.AddHours(1), oldest)?.Count == 2000, "older cache refreshes once without a live quote");
}

static void Feed(PaperTradingService paper, TestClock clock, string symbol, double price)
{
    var now = clock.GetUtcNow().UtcDateTime;
    paper.OnTrade("OFuture", symbol, now, now, price, 1);
    clock.Now = clock.Now.AddMinutes(1);
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Throws<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException(message);
}

static async Task ThrowsAsync<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException(message);
}

sealed class TestDbFactory : IDbContextFactory<DumbTraderDbContext>
{
    public DumbTraderDbContext CreateDbContext() => new();
}

sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
}

sealed class FixtureGenerator : ICodexStrategyGenerator
{
    public string? Prompt { get; private set; }
    public TaskCompletionSource? Hold { get; set; }
    public string Code { get; set; } = "Close > 110 ? 1 : -1";

    public async Task<GeneratedStrategy> GenerateAsync(Guid id, string prompt, StrategyEvolutionOptions options, CancellationToken token)
    {
        Prompt = prompt;
        if (Hold is { } signal)
        {
            signal.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        return new(Code, "fixture alternating prices");
    }
}
