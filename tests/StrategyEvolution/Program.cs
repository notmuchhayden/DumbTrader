using System.Globalization;
using System.IO;
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
var replayDatabase = args.Length is 2 or 3 && args[0] == "--replay-futures-db" ? Path.GetFullPath(args[1]) : null;
try
{
    Directory.SetCurrentDirectory(root);
    if (replayDatabase is null)
    {
        await RunAsync(root);
        Console.WriteLine("Strategy evolution checks passed.");
    }
    else await VerifyStoredFutureChartsAsync(replayDatabase, args.Length == 3 ? args[2] : null);
}
finally
{
    Directory.SetCurrentDirectory(originalDirectory);
    SqliteConnection.ClearAllPools();
    Directory.Delete(root, recursive: true);
}

static async Task VerifyStoredFutureChartsAsync(string path, string? symbol)
{
    var factory = new ReadOnlyReplayDbFactory(path);
    var service = new OFutureStrategyService(factory, new LoggingService());
    var simulator = new OFutureSimulationService(factory, service);
    var start = new DateTime(2000, 1, 1);
    await using var db = factory.CreateDbContext();
    var contracts = await db.OFutureContracts.AsNoTracking().Where(contract => symbol == null || contract.Symbol == symbol)
        .OrderBy(contract => contract.Symbol).ToListAsync();
    var tested = 0;
    foreach (var contract in contracts)
    {
        var interval = contract.Symbol == "HBIX26" ? 10 : 0;
        var script = $"{(interval == 0 ? "daily" : "minute10")}_{contract.Symbol}.cs";
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "strategy", "ofuture", "main", script))) continue;
        var assignment = new OFutureStrategyAssignment { Contract = contract, MainStrategyPath = script };
        var result = await simulator.RunAsync(assignment, 1_000_000, start, interval, 1);
        var bars = result.ChartBars.ToDictionary(bar => bar.Timestamp);
        Check(result.ChartBars.Count == result.BarsProcessed, $"{contract.Symbol}: snapshot covers every processed bar");
        var signals = result.ChartEvents.Where(e => e.Kind == OFutureChartEventKind.Signal).ToDictionary(e => e.Id);
        var fills = result.ChartEvents.Where(e => e.Kind != OFutureChartEventKind.Signal).ToArray();
        Check(signals.Values.All(signal => bars[signal.Timestamp].Close == signal.Price), $"{contract.Symbol}: signal uses decision-bar close");
        var nextBars = result.ChartBars.Zip(result.ChartBars.Skip(1))
            .ToDictionary(pair => pair.First.Timestamp, pair => pair.Second);
        Check(fills.All(fill => fill.SignalId is { } id && signals.TryGetValue(id, out var signal)
                && nextBars.TryGetValue(signal.Timestamp, out var next) && next.Timestamp == fill.Timestamp && next.Open == fill.Price),
            $"{contract.Symbol}: fill uses the next candle's open");
        Check(fills.Count(e => e.Kind == OFutureChartEventKind.Entry) == result.EntryCount
            && fills.Count(e => e.Kind == OFutureChartEventKind.Exit) == result.ExitCount,
            $"{contract.Symbol}: fill markers match ledger totals");
        Console.WriteLine($"{contract.Symbol} | period={interval} | candles={bars.Count} | signals={signals.Count} | fills={fills.Length}");
        tested++;
    }
    Check(tested > 0, "at least one stored futures contract was replayed");
    Console.WriteLine($"Stored futures replay checks passed ({tested} contracts).");
}

static async Task RunAsync(string root)
{
    VerifyChartRefreshPolicy();
    VerifyChartEventHistory();
    await VerifyFutureChartReplayAsync();
    await VerifyDailyBarConfirmationAsync();
    await VerifyOneMinuteChartReplayAsync();
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
        var activatedMarkers = new List<OFutureChartEvent>();
        paper.ActiveFutureChartEvent += (_, marker) => activatedMarkers.Add(marker);
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
        Check(activatedMarkers.Count == 0, "candidate-only paper sessions do not emit active chart markers");
        clock.Now = clock.Now.AddSeconds(-30);
        paper.Approve(candidate.Id);
        Check(paper.Activate(candidate.Id, false)?.Status == CandidateStatus.Active, "explicit signal activation");
        clock.Now = clock.Now.AddSeconds(30);
        Feed(paper, clock, instrument.Symbol, 100);
        Feed(paper, clock, instrument.Symbol, 120);
        Feed(paper, clock, instrument.Symbol, 100);
        Check(activatedMarkers.Count == 2 && activatedMarkers[0].Kind == OFutureChartEventKind.Signal
            && activatedMarkers[1].Kind == OFutureChartEventKind.Entry
            && activatedMarkers[1].SignalId == activatedMarkers[0].Id
            && activatedMarkers[0].Timestamp < activatedMarkers[1].Timestamp
            && activatedMarkers.All(marker => marker.Source == "페이퍼" && marker.Strategy == candidate.Id.ToString()),
            "active candidate publishes one decision and its confirmed paper position change");
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

static void VerifyChartEventHistory()
{
    var night = new DateTime(2026, 11, 17, 23, 59, 59);
    var midnight = night.AddSeconds(1);
    Check(DumbTrader.ViewModels.OFutureChartMarkers.BucketStart(night, 10) == night.Date.AddHours(23).AddMinutes(50)
        && DumbTrader.ViewModels.OFutureChartMarkers.BucketStart(midnight, 10) == midnight
        && DumbTrader.ViewModels.OFutureChartMarkers.BucketStart(night, 1) == night.AddSeconds(-59),
        "marker mapping keeps exchange dates and minute buckets across midnight");
    var history = new OFutureChartEventHistory();
    var time = new DateTime(2026, 11, 17, 13, 0, 0, DateTimeKind.Utc);
    history.StartSession(time);
    var replay = Enumerable.Range(0, 4000).Select(index =>
        new OFutureChartEvent(Guid.NewGuid(), time, 100, 1, "BUY", OFutureChartEventKind.Signal,
            index % 2 == 0 ? "HBIX26" : "CUSZ26", "fixture", "실시간")).ToArray();
    foreach (var item in replay)
    {
        Check(history.Add(item, time), "distinct ticks in one second remain distinct");
        Check(!history.Add(item, time), "duplicate chart delivery is ignored");
    }
    Check(history.ForSymbol("hbix26").SequenceEqual(replay.Skip(2000).Where(e => e.Symbol == "HBIX26")),
        "chart history is bounded and restores only the selected contract in order");
    history.StartSession(time.AddSeconds(1));
    Check(!history.ForSymbol("HBIX26").Any() && !history.Add(replay[^2], time),
        "late chart deliveries cannot repopulate a new receiving session");
}

static async Task VerifyFutureChartReplayAsync()
{
    var factory = new TestDbFactory();
    var symbol = "HBIX26";
    var assignment = new OFutureStrategyAssignment
    {
        Contract = new OFutureContract
        {
            Symbol = symbol, Name = "Replay fixture", TickSize = 1, TickValue = 10,
            OpeningMargin = 100, CurrencyCode = "HKD"
        },
        MainStrategyPath = "minute10_HBIX26.cs"
    };
    var services = new OFutureStrategyService(factory, new LoggingService());
    var start = new DateTime(2026, 11, 17, 10, 0, 0);
    await using (var db = factory.CreateDbContext())
    {
        db.EnsureCreatedAndExtended();
        for (var i = -20; i <= 3; i++)
        {
            var timestamp = start.AddMinutes(i * 10);
            var price = i switch { 1 => 110, 2 => 90, 3 => 90, _ => 100 };
            db.OFutureMinuteBars.Add(new OFutureMinuteBar
            {
                Symbol = symbol, IntervalMinutes = 10,
                TradeDate = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                TradeTime = timestamp.ToString("HHmmss", CultureInfo.InvariantCulture),
                Open = price, High = price, Low = price, Close = price, Volume = 1
            });
        }
        await db.SaveChangesAsync();
    }

    OFutureRealData Quote(int minute, double price)
    {
        var timestamp = start.AddMinutes(minute);
        return new OFutureRealData
        {
            Symbol = symbol, OverseasDate = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            TradeTime = timestamp.ToString("HHmmss", CultureInfo.InvariantCulture),
            Price = price, TradeQuantity = 1, ReceivedAtUtc = DateTime.UtcNow
        };
    }

    var live = new OFutureCompletedBarStrategy(services, assignment, 10);
    Check((await live.EvaluateAsync(Quote(0, 100), default)).Count == 0, "no markers during bar warmup");
    Check((await live.EvaluateAsync(Quote(10, 110), default)).Count == 0, "first partial bucket is skipped");
    var buy = await live.EvaluateAsync(Quote(20, 90), default);
    Check((await live.EvaluateAsync(Quote(20, 90), default)).Count == 0, "repeated bucket emits no duplicate markers");
    var sell = await live.EvaluateAsync(Quote(30, 90), default);
    var paper = buy.Concat(sell).ToArray();
    var simulation = await new OFutureSimulationService(factory, services).RunAsync(assignment, 100_000, start.Date, 10, 1);
    Check(paper.Length == 4 && simulation.ChartEvents.Count == 4, "signal and actual position change markers in both paths");
    Check(paper.Select(e => (e.Timestamp, e.Price, e.Kind, e.Action))
        .SequenceEqual(simulation.ChartEvents.Select(e => (e.Timestamp, e.Price, e.Kind, e.Action))),
        "shared completed-bar script and signal/fill coordinates");
    Check(paper[1].SignalId == paper[0].Id && paper[3].SignalId == paper[2].Id
        && paper[1].Kind == OFutureChartEventKind.Entry && paper[3].Kind == OFutureChartEventKind.Exit,
        "paper fill markers link to the originating signal");
    Check(simulation.EntryCount == 1 && simulation.ExitCount == 1, "simulation marker counts match ledger");
    VerifyRenderedChartMarkers(simulation);

    var simulator = new OFutureSimulationService(factory, services);
    var insufficientMargin = await simulator.RunAsync(assignment, 50, start.Date, 10, 1);
    Check(insufficientMargin.EntryCount == 0 && insufficientMargin.ChartEvents.Count == 2
        && insufficientMargin.ChartEvents.All(e => e.Kind == OFutureChartEventKind.Signal
            && e.Detail?.Contains("증거금") == true),
        "insufficient margin keeps signal markers without creating simulated fills");

    await using (var db = factory.CreateDbContext())
    {
        var finalBar = await db.OFutureMinuteBars.SingleAsync(bar => bar.Symbol == symbol && bar.TradeTime == "103000");
        finalBar.Open = finalBar.High = finalBar.Low = finalBar.Close = 130;
        await db.SaveChangesAsync();
    }
    var finalSignal = await simulator.RunAsync(assignment, 100_000, start.Date, 10, 1);
    Check(finalSignal.EntryCount == 1 && finalSignal.ExitCount == 1 && finalSignal.ChartEvents.Count == 5
        && finalSignal.ChartEvents[^1] is { Kind: OFutureChartEventKind.Signal, Detail: "미체결 · 다음 봉 없음" },
        "the final bar retains its unfilled signal without a phantom entry");
}

static async Task VerifyDailyBarConfirmationAsync()
{
    var factory = new TestDbFactory();
    var service = new OFutureStrategyService(factory, new LoggingService());
    var assignment = new OFutureStrategyAssignment
    {
        Contract = new OFutureContract { Symbol = "CUSZ26", TickSize = 1, TickValue = 10, OpeningMargin = 100 },
        MainStrategyPath = "daily_CUSZ26.cs"
    };
    var date = new DateTime(2026, 11, 17);
    var clock = new TestClock(new DateTimeOffset(date, TimeSpan.Zero));
    OFutureRealData Quote(DateTime tradeDate) => new()
    {
        Symbol = assignment.Contract.Symbol, OverseasDate = tradeDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        TradeTime = "000001", Price = 110, TradeQuantity = 1, ReceivedAtUtc = clock.Now.UtcDateTime
    };

    await using var db = factory.CreateDbContext();
    for (var i = -20; i <= 1; i++)
    {
        var price = i >= 0 ? 110 : 100;
        db.OFutureDailyBars.Add(new OFutureDailyBar
        {
            Symbol = assignment.Contract.Symbol, TradeDate = date.AddDays(i).ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            SourceTrCode = "o3108", QueryType = "0", Open = price, High = price, Low = price, Close = price,
            Volume = 1, ReceivedAtUtc = clock.Now.UtcDateTime
        });
    }
    await db.SaveChangesAsync();
    var evaluator = new OFutureCompletedBarStrategy(service, assignment, 0, clock);
    Check((await evaluator.EvaluateAsync(Quote(date), default)).Count == 0, "daily bar stays open on its trading date");
    clock.Now = clock.Now.AddDays(1);
    Check((await evaluator.EvaluateAsync(Quote(date.AddDays(1)), default)).Count == 0,
        "rollover alone does not confirm a stored provisional daily bar");
    clock.Now = clock.Now.AddSeconds(16);
    var row = await db.OFutureDailyBars.SingleAsync(bar => bar.Symbol == assignment.Contract.Symbol && bar.TradeDate == "20261117");
    row.ReceivedAtUtc = clock.Now.UtcDateTime;
    await db.SaveChangesAsync();
    var events = await evaluator.EvaluateAsync(Quote(date.AddDays(1)), default);
    Check(events.Count == 2 && events[0].Timestamp == date && events[1].Timestamp.Date == date.AddDays(1),
        "confirmed prior-day signal and next-session paper fill have separate timestamps");
    clock.Now = clock.Now.AddSeconds(16);
    Check((await evaluator.EvaluateAsync(Quote(date.AddDays(1)), default)).Count == 0,
        "refetched confirmed daily bars cannot trigger again");
    var simulation = await new OFutureSimulationService(factory, service).RunAsync(assignment, 100_000, date, 0, 1);
    Check(simulation.HasOpenPosition && simulation.EntryCount == 1 && simulation.ExitCount == 0
        && simulation.ChartEvents.Count == 2, "an open position at simulation end has no exit marker");
    Check(simulation.ChartEvents.Select(e => (e.Timestamp.Date, e.Action, e.Kind, e.Price))
        .SequenceEqual(events.Select(e => (e.Timestamp.Date, e.Action, e.Kind, e.Price))),
        "daily paper and simulated signals and fills agree for the same bar and quote prices");
}

static async Task VerifyOneMinuteChartReplayAsync()
{
    var factory = new TestDbFactory();
    var contract = new OFutureContract { Symbol = "MINUTE-FIXTURE", TickSize = 1, TickValue = 10, OpeningMargin = 100 };
    var assignment = new OFutureStrategyAssignment { Contract = contract, MainStrategyPath = "main01.cs" };
    var start = new DateTime(2026, 11, 17, 23, 40, 0);
    await using var db = factory.CreateDbContext();
    for (var i = 0; i <= 22; i++)
    {
        var timestamp = start.AddMinutes(i);
        var price = i == 20 ? 120 : i > 20 ? 50 : 100;
        db.OFutureMinuteBars.Add(new OFutureMinuteBar
        {
            Symbol = contract.Symbol, IntervalMinutes = 1,
            TradeDate = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            TradeTime = timestamp.ToString("HHmmss", CultureInfo.InvariantCulture),
            Open = price, High = price, Low = price, Close = price, Volume = 1
        });
    }
    await db.SaveChangesAsync();
    var service = new OFutureStrategyService(factory, new LoggingService());
    var result = await new OFutureSimulationService(factory, service).RunAsync(assignment, 100_000, start.Date.AddDays(1), 1, 1);
    var events = result.ChartEvents;
    Check(result.ChartBars.Count == 3 && result.EntryCount == 1 && result.ExitCount == 1 && events.Count == 4,
        "one-minute replay warms up overnight without displaying pre-start candles or markers");
    Check(events.Select(e => e.Timestamp).SequenceEqual(new[] { 20, 21, 21, 22 }.Select(i => start.AddMinutes(i)))
        && events.Select(e => e.Kind).SequenceEqual(new[] { OFutureChartEventKind.Signal, OFutureChartEventKind.Entry,
            OFutureChartEventKind.Signal, OFutureChartEventKind.Exit }),
        "one-minute signals and fills land on the decision and execution candles across midnight");
}

static void VerifyRenderedChartMarkers(OFutureSimulationRunResult result)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var control = new ScottPlot.WPF.WpfPlot();
            var overlay = new DumbTrader.ViewModels.OFutureChartMarkers(control);
            var source = result.ChartEvents.ToArray();
            var slot = source[0].Timestamp.ToOADate();
            var sameLocation = source[1] with { Timestamp = source[0].Timestamp, Price = source[0].Price };
            overlay.Draw(source.Concat([sameLocation]), time => time == source[0].Timestamp ? slot : null, false);
            var rendered = control.Plot.GetPlottables().OfType<ScottPlot.Plottables.Text>().Single();
            Check(rendered.LabelText.Contains("△● 2"), "visible marker combines co-located records and skips missing bars");
            var (min, max) = overlay.IncludeVisiblePrices(slot - 1, slot + 1, 90, 100);
            Check(min == 90 && max == source[0].Price, "visible marker participates in the price axis");
            control.Plot.Axes.SetLimits(slot - 0.1, slot + 0.1, 89, 111);
            var imagePath = Path.Combine(Directory.GetCurrentDirectory(), "chart-markers.png");
            control.Plot.SavePng(imagePath, 640, 360);
            Check(new FileInfo(imagePath).Length > 0, "ScottPlot renders the marker overlay");

            control.Plot.Clear();
            var minute = TimeSpan.FromMinutes(1);
            var bars = Enumerable.Range(0, 500).Select(i =>
                new ScottPlot.OHLC(100, 106, 99, 102, source[0].Timestamp.AddMinutes(i), minute)).ToArray();
            var slots = bars.ToDictionary(bar => bar.DateTime, bar => bar.DateTime.ToOADate());
            var burst = bars.SelectMany(bar => Enumerable.Range(0, 4).Select(i =>
                source[i] with { Id = Guid.NewGuid(), Timestamp = bar.DateTime, Price = 100 + i })).ToArray();
            control.Plot.Add.Candlestick(bars);
            overlay.Draw(burst, time => slots.TryGetValue(time, out var x) ? x : null, false);
            Check(control.Plot.GetPlottables().OfType<ScottPlot.Plottables.Text>().Count() == 2000,
                "a full dashboard marker history renders without losing distinct coordinates");
            foreach (var first in new[] { 0, 125, 400 })
            {
                control.Plot.Axes.SetLimits(bars[first].DateTime.ToOADate(), bars[first + 90].DateTime.ToOADate(), 98, 107);
                control.Plot.SavePng(imagePath, 640, 360);
            }
        }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw new InvalidOperationException("Chart marker rendering failed.", failure);
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

sealed class ReadOnlyReplayDbFactory(string path) : IDbContextFactory<DumbTraderDbContext>
{
    public DumbTraderDbContext CreateDbContext() => new ReplayDbContext(path);

    private sealed class ReplayDbContext(string path) : DumbTraderDbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
    }
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
