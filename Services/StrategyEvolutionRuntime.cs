using System.Globalization;
using System.Windows.Threading;
using DumbTrader.Models;

namespace DumbTrader.Services;

public sealed class StrategyEvolutionRuntime : IDisposable
{
    public event EventHandler<OFutureChartEvent> ActiveFutureChartEvent
    {
        add => _paper.ActiveFutureChartEvent += value;
        remove => _paper.ActiveFutureChartEvent -= value;
    }

    private const string SubscriptionOwner = "strategy-evolution";
    private static readonly TimeSpan KoreaOffset = TimeSpan.FromHours(9);
    private readonly StrategyCandidateRepository _repository;
    private readonly StrategyEvolutionCoordinator _coordinator;
    private readonly PaperTradingService _paper;
    private readonly OFutureRealDataService _futures;
    private readonly StockRealDataService _stocks;
    private readonly IXASessionService _session;
    private readonly LoggingService _log;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<(string Market, string Symbol), StrategyInstrument> _subscriptions = new();
    private readonly Dictionary<(string Market, string Symbol), VersionedStrategySignal> _signals = new();
    private readonly Dictionary<(string Market, string Symbol), StrategyInstrument> _paperTargets = new();
    private readonly CancellationTokenSource _shutdown = new();
    private bool _scheduledRunning;
    private DateTime _lastLogged;

    public StrategyEvolutionRuntime(StrategyCandidateRepository repository, StrategyEvolutionCoordinator coordinator,
        PaperTradingService paper, OFutureRealDataService futures, StockRealDataService stocks,
        IXASessionService session, LoggingService log)
    {
        _repository = repository;
        _coordinator = coordinator;
        _paper = paper;
        _futures = futures;
        _stocks = stocks;
        _session = session;
        _log = log;
        _futures.RealDataUpdated += OnFutureTrade;
        _stocks.RealDataUpdated += OnStockTrade;
        _paper.ActiveSignal += OnActiveSignal;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += OnTimer;
        _timer.Start();
    }

    public VersionedStrategySignal? Latest(StrategyInstrument instrument) =>
        _signals.GetValueOrDefault(Key(instrument));

    public string DescribeSignal(string market, string symbol)
    {
        if (ActiveVersionId(market, symbol) is not { } version) return string.Empty;
        var signal = _signals.GetValueOrDefault((market, symbol.ToUpperInvariant()));
        return signal?.CandidateId != version ? "활성 버전 · 봉 마감 신호 대기"
            : $"{signal.Signal?.Action.ToString() ?? signal.Error} · {signal.QuoteUtc.ToLocalTime():g} · {signal.CandidateId} · 신호 전용";
    }

    public bool HasActiveVersion(string market, string symbol) => ActiveVersionId(market, symbol) is not null;

    private Guid? ActiveVersionId(string market, string symbol)
    {
        try { return _paper.ActiveVersionId(market, symbol); }
        catch (Exception ex) { Log(ex); return null; }
    }

    public void StartPaper(Guid id)
    {
        if (!_session.IsConnected) throw new InvalidOperationException("실시간 시세 연결을 먼저 확인하세요.");
        var instrument = _repository.ReadSnapshot(id).Instrument;
        Subscribe(instrument);
        _paper.Start(id);
        _paperTargets[Key(instrument)] = instrument;
    }

    private void OnActiveSignal(object? sender, VersionedStrategySignal signal)
    {
        _signals[(signal.Market, signal.Symbol.ToUpperInvariant())] = signal;
        if (signal.Market != "OFuture") return;
        if (signal.Signal is { } result)
            _log.LogFutureStrategyResult(signal.Symbol, signal.CandidateId.ToString(), "활성 전략 · 페이퍼",
                result.Action.ToString().ToUpperInvariant(), result.QuantityHint);
        else if (signal.Error is { } error)
            _log.Log($"[해외선물 전략] {signal.Symbol} · 활성 전략 실행 중단 · {error}");
    }

    private void OnFutureTrade(object? sender, OFutureRealData trade)
    {
        var exchange = Date(trade.OverseasDate + trade.TradeTime);
        var korea = Date(trade.KoreaDate + trade.KoreaTradeTime);
        OnTrade("OFuture", trade.Symbol, exchange, KoreaUtc(korea), trade.Price, trade.TradeQuantity);
    }

    private void OnStockTrade(object? sender, RealS3_K3_Data trade)
    {
        var date = DateTime.UtcNow.Add(KoreaOffset).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var exchange = Date(date + trade.chetime);
        if (exchange != default && (exchange.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            || exchange.TimeOfDay < TimeSpan.FromHours(9) || exchange.TimeOfDay > TimeSpan.FromHours(15.5)
            || trade.price <= 0 || trade.cvolume <= 0)) return;
        OnTrade("Stock", trade.shcode, exchange, KoreaUtc(exchange), trade.price, trade.cvolume);
    }

    private void OnTrade(string market, string symbol, DateTime exchange, DateTime utc, double price, long volume)
    {
        if (!_subscriptions.ContainsKey((market, symbol.ToUpperInvariant()))) return;
        try { _paper.OnTrade(market, symbol, exchange, utc, price, volume); }
        catch (Exception ex) { Log(ex); }
    }

    private void OnTimer(object? sender, EventArgs e)
    {
        try
        {
            _paper.Sweep();
            if (!_session.IsConnected) return;
            var desired = new Dictionary<(string Market, string Symbol), StrategyInstrument>();
            foreach (var id in _repository.ActiveIds())
            {
                var instrument = _repository.ReadSnapshot(id).Instrument;
                desired[Key(instrument)] = instrument;
            }
            foreach (var (key, instrument) in _paperTargets.ToArray())
            {
                if (_paper.Current(key.Market, key.Symbol) is null) _paperTargets.Remove(key);
                else desired[key] = instrument;
            }
            foreach (var instrument in desired.Values) Subscribe(instrument);
            foreach (var (key, instrument) in _subscriptions.ToArray())
                if (!desired.ContainsKey(key)) Unsubscribe(instrument);

            var settings = _repository.LoadOptions();
            if (!_scheduledRunning && DateTime.Now.Hour >= settings.ScheduledHourLocal
                && settings is { ScheduleEnabled: true, KillSwitch: false, ScheduledTarget: { } target })
                _ = RunScheduledAsync(target);
        }
        catch (Exception ex) { Log(ex); }
    }

    private async Task RunScheduledAsync(StrategyScheduleTarget target)
    {
        _scheduledRunning = true;
        try
        {
            await _coordinator.RunAsync(target.Instrument, target.IntervalMinutes,
                DateTime.Today.AddDays(-target.LookbackDays), scheduled: true, _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex) { Log(ex); }
        finally { _scheduledRunning = false; }
    }

    private void Subscribe(StrategyInstrument instrument)
    {
        var key = Key(instrument);
        if (_subscriptions.ContainsKey(key)) return;
        if (instrument.Market == "OFuture") _futures.SubscribeTrade(instrument.Symbol, SubscriptionOwner);
        else _stocks.SubscribeStockRealData(instrument.Symbol, StockMarket(instrument), SubscriptionOwner);
        _subscriptions[key] = instrument;
    }

    private void Unsubscribe(StrategyInstrument instrument)
    {
        if (instrument.Market == "OFuture") _futures.UnsubscribeTrade(instrument.Symbol, SubscriptionOwner);
        else _stocks.UnsubscribeStockRealData(instrument.Symbol, StockMarket(instrument), SubscriptionOwner);
        _subscriptions.Remove(Key(instrument));
    }

    private void Log(Exception ex)
    {
        var now = DateTime.UtcNow;
        if (now - _lastLogged < TimeSpan.FromSeconds(30)) return;
        _lastLogged = now;
        _log.Log($"전략 개선 서비스 오류: {ex.Message}");
    }

    private static MarketType StockMarket(StrategyInstrument instrument) => instrument.Kosdaq ? MarketType.KOSDAQ : MarketType.KOSPI;
    private static (string, string) Key(StrategyInstrument instrument) => (instrument.Market, instrument.Symbol.ToUpperInvariant());
    private static DateTime Date(string value) => DateTime.TryParseExact(value, "yyyyMMddHHmmss",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : default;
    private static DateTime KoreaUtc(DateTime time) => time == default ? default : new DateTimeOffset(time, KoreaOffset).UtcDateTime;

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimer;
        _shutdown.Cancel();
        _futures.RealDataUpdated -= OnFutureTrade;
        _stocks.RealDataUpdated -= OnStockTrade;
        _paper.ActiveSignal -= OnActiveSignal;
        foreach (var instrument in _subscriptions.Values.ToArray())
            try { Unsubscribe(instrument); } catch (Exception ex) { Log(ex); }
    }
}
