using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;
using ScottPlot;
using ScottPlot.WPF;

namespace DumbTrader.ViewModels
{
    public sealed class OFutureDashboardViewModel : ViewModelBase
    {
        private readonly OFutureDataService _oFutureDataService;
        private readonly OFutureRealDataService _oFutureRealDataService;
        private readonly LoggingService _loggingService;
        private readonly OFutureChartStoreService _chartStore;
        private readonly IXASessionService _sessionService;
        private readonly OFutureStrategyService _strategyService;
        private readonly StrategyEvolutionRuntime _evolution;
        private readonly ConcurrentDictionary<string, byte> _dashboardSubscriptions = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _tradeFlushGate = new(1, 1);
        private readonly ConcurrentDictionary<(string Symbol, int Interval, string Date, string Time), OFutureMinuteBar> _pendingMinuteBars = new();
        private readonly Dictionary<string, (long Generation, CancellationTokenSource Cancellation)> _minuteLookups = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _minuteLookupCompleted = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _dailyLookupCompleted = new(StringComparer.OrdinalIgnoreCase);
        private long _minuteLookupGeneration;
        private bool _checkingMinuteCache;
        private readonly OFutureMinuteBarAggregator _minuteBarAggregator = new();
        private IReadOnlyList<OFutureChartData> _selectedOFutureChartData = Array.Empty<OFutureChartData>();
        private OFutureRealData? _latestSelectedTrade;
        private double _dataMinOADate = double.NaN;
        private double _dataMaxOADate = double.NaN;
        private bool _dailyHasPlottedData;
        private bool _oneMinuteHasPlottedData;
        private bool _tenMinuteHasPlottedData;
        private volatile bool _isReceivingOFutureData;
        private volatile bool _isActive;
        private bool _hasActivated;
        private long _selectionVersion;
        private readonly OFutureChartEventHistory _chartEvents = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<OFutureChartEvent> _pendingPaperMarkers = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<OFutureStrategySignalEventArgs> _pendingManualMarkers = new();
        private OFutureStrategySignalEventArgs? _pendingManualStatus;
        private readonly Dictionary<WpfPlot, OFutureChartMarkers> _chartMarkers;
        private bool _chartMarkersDirty;
        private bool _oneMinutePlotDirty;
        private bool _tenMinutePlotDirty;
        private bool _flushingTradeBatch;
        private DateTime _receivingStartedUtc;
        private Guid _receivingSessionId;
        private readonly Dictionary<string, OFutureCompletedBarStrategy> _completedBarStrategies = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _dailyBarRequestDates = new(StringComparer.OrdinalIgnoreCase);
        private bool _evaluatingCompletedBars;
        private long _dailyChartRevision;
        private long _oneMinuteRevision;
        private long _tenMinuteRevision;
        private CancellationTokenSource? _selectionCancellation;
        private DateTime? _lastLiveTradeReceivedAtUtc;
        private DateTime? _lastStoredTradeAtUtc;
        private string _strategySignalText = "해외선물 계약을 선택하세요";
        private string _chartStatus = "선택한 계약의 차트 데이터를 불러오는 중";
        private string _requestStatus = "대기";
        private readonly ConcurrentDictionary<long, Task> _chartWriteTasks = new();
        private readonly object _writeTaskLock = new();
        private volatile bool _isShuttingDown;
        private long _nextWriteTaskId;
        private IReadOnlyList<OFutureMinuteChartData> _oneMinuteBars = Array.Empty<OFutureMinuteChartData>();
        private IReadOnlyList<OFutureMinuteChartData> _tenMinuteBars = Array.Empty<OFutureMinuteChartData>();
        private readonly ConcurrentDictionary<string, OFutureRealData> _pendingTrades = new(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer _tradeFlushTimer;
        private readonly DispatcherTimer _minuteReconciliationTimer;
        private CancellationTokenSource? _reconciliationCancellation;
        private string _oneMinuteRange = "Today";
        private string _minuteStatus = "분봉 DB 조회 대기";

        public string ChartStatus { get => _chartStatus; private set => SetProperty(ref _chartStatus, value); }
        public string MinuteStatus { get => _minuteStatus; private set => SetProperty(ref _minuteStatus, value); }
        public string RequestStatus { get => _requestStatus; private set { if (SetProperty(ref _requestStatus, value)) OnPropertyChanged(nameof(DashboardStatusText)); } }
        public string ConnectionStatus => _sessionService.IsConnected ? "Xing 연결" : "Xing 연결 끊김";
        public string ReceivingStatus => !_isActive ? "화면 비활성" : !_isReceivingOFutureData ? "수신 정지"
            : _dashboardSubscriptions.Count > 0 ? "OVC 구독 중" : "OVC 구독 대기";
        public string LastTradeReceivedText => _lastLiveTradeReceivedAtUtc?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "없음";
        public string LastTradeStoredText => _lastStoredTradeAtUtc?.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "없음";
        public bool IsTradeStale => !_isActive || !_isReceivingOFutureData || !_sessionService.IsConnected
            || _lastLiveTradeReceivedAtUtc is null || DateTime.UtcNow - _lastLiveTradeReceivedAtUtc.Value > TimeSpan.FromSeconds(30);
        public string DashboardStatusText => $"{ConnectionStatus} · {ReceivingStatus} · 실시간 {(IsTradeStale ? "오래됨/캐시" : "최신")} · 수신 {LastTradeReceivedText} · 저장 {LastTradeStoredText} · {RequestStatus}";

        public WpfPlot OneMinutePlot { get; } = new();
        public WpfPlot TenMinutePlot { get; } = new();
        public string OneMinuteRange { get => _oneMinuteRange; private set => SetProperty(ref _oneMinuteRange, value); }

        public string SelectedContractName => SelectedOFutureCard?.Name ?? string.Empty;
        public string SelectedContractSymbol => SelectedOFutureCard?.Symbol ?? string.Empty;
        public string TradePrice => _latestSelectedTrade?.Price.ToString(CultureInfo.InvariantCulture) ?? "—";
        public string TradeChange => _latestSelectedTrade is null ? "—" : $"{_latestSelectedTrade.ChangeSign}{_latestSelectedTrade.Change} ({_latestSelectedTrade.ChangeRate}%)";
        public string TradeOhl => _latestSelectedTrade is null ? "—" : $"{_latestSelectedTrade.Open} / {_latestSelectedTrade.High} / {_latestSelectedTrade.Low}";
        public string TradeVolume => _latestSelectedTrade is null ? "—" : $"{_latestSelectedTrade.TradeQuantity} / {_latestSelectedTrade.TotalQuantity}";
        public string TradeSideVolume => _latestSelectedTrade is null ? "—" : $"매수 {_latestSelectedTrade.BuyTradeQuantity} / 매도 {_latestSelectedTrade.SellTradeQuantity}";
        public string TradeTimes => _latestSelectedTrade is null ? "—" : $"현지 {_latestSelectedTrade.OverseasDate} {_latestSelectedTrade.TradeTime} / 한국 {_latestSelectedTrade.KoreaDate} {_latestSelectedTrade.KoreaTradeTime}";
        public string StrategySignalText { get => _strategySignalText; private set => SetProperty(ref _strategySignalText, value); }

        public OFutureContract? SelectedOFutureContract
        {
            get => OFutureWatchlist.FirstOrDefault(contract => string.Equals(contract.Symbol, SelectedOFutureCard?.Symbol, StringComparison.OrdinalIgnoreCase));
            set
            {
                var card = value is null ? null : OFutureCards.FirstOrDefault(item => string.Equals(item.Symbol, value.Symbol, StringComparison.OrdinalIgnoreCase));
                SelectedOFutureCard = card;
            }
        }

        public ObservableCollection<OFutureContract> OFutureWatchlist { get; }
        public ObservableCollection<OFutureCardViewModel> OFutureCards { get; } = new();
        public WpfPlot PlotControl { get; } = new();

        private OFutureCardViewModel? _selectedOFutureCard;
        public OFutureCardViewModel? SelectedOFutureCard
        {
            get => _selectedOFutureCard;
            set
            {
                if (SetProperty(ref _selectedOFutureCard, value))
                {
                    _selectionCancellation?.Cancel();
                    _selectionCancellation?.Dispose();
                    _selectionCancellation = _isActive ? new CancellationTokenSource() : null;
                    _latestSelectedTrade = value is null ? null : _oFutureRealDataService.GetLatestTrade(value.Symbol);
                    _lastLiveTradeReceivedAtUtc = null;
                    _selectionVersion++;
                    RefreshStrategySignalStatus();
                    if (_isActive)
                    {
                        if (value is not null) _ = LoadLatestTradeSnapshotAsync(value.Symbol, _selectionVersion);
                        LoadSelectedOFutureChartData();
                    }
                    UpdatePlot(preserveView: false);
                    NotifyTradeProperties();
                    OnPropertyChanged(nameof(SelectedOFutureContract));
                    if (_isActive && _isReceivingOFutureData) SyncOFutureSubscriptions();
                }
            }
        }

        public ICommand SelectOFutureCardCommand { get; }
        public ICommand SelectOneMinuteRangeCommand { get; }
        public ICommand StartOFutureReceivingCommand { get; }
        public ICommand StopOFutureReceivingCommand { get; }

        public void Activate()
        {
            if (_isActive) return;
            if (_isShuttingDown) return;
            _isActive = true;
            _selectionCancellation?.Dispose();
            _selectionCancellation = new CancellationTokenSource();
            if (!_hasActivated)
            {
                _isReceivingOFutureData = true;
                _receivingStartedUtc = DateTime.UtcNow;
                _receivingSessionId = Guid.NewGuid();
                _hasActivated = true;
            }
            _tradeFlushTimer.Start();
            if (_isReceivingOFutureData)
            {
                _reconciliationCancellation?.Cancel();
                _reconciliationCancellation?.Dispose();
                _reconciliationCancellation = new CancellationTokenSource();
                _minuteReconciliationTimer.Start();
            }
            NotifyTradeProperties();
            if (SelectedOFutureCard is not null)
            {
                _ = LoadLatestTradeSnapshotAsync(SelectedOFutureCard.Symbol, _selectionVersion);
                LoadSelectedOFutureChartData();
            }
            else LoadSelectedOFutureChartData();
            SyncOFutureSubscriptions();
            if (_isReceivingOFutureData) QueueMinuteReconciliation();
        }

        public void Deactivate()
        {
            if (!_isActive) return;
            foreach (var symbol in _dashboardSubscriptions.Keys.ToArray())
                _oFutureRealDataService.UnsubscribeTrade(symbol);
            _dashboardSubscriptions.Clear();
            _isActive = false;
            _tradeFlushTimer.Stop();
            _minuteReconciliationTimer.Stop();
            _reconciliationCancellation?.Cancel();
            foreach (var symbol in _minuteLookups.Keys.ToArray()) CancelMinuteLookup(symbol);
            _selectionCancellation?.Cancel();
            _ = FlushTradeSnapshotsAsync();
            _ = FlushLiveMinuteBarsAsync();
            NotifyTradeProperties();
        }

        public async Task ShutdownAsync()
        {
            lock (_writeTaskLock) _isShuttingDown = true;
            Deactivate();
            _selectionCancellation?.Dispose();
            _selectionCancellation = null;
            _reconciliationCancellation?.Dispose();
            _reconciliationCancellation = null;
            _oFutureDataService.WatchlistUpdated -= OnOFutureWatchlistUpdated;
            _oFutureDataService.ChartDataUpdated -= OnOFutureChartDataUpdated;
            _oFutureDataService.MinuteChartDataUpdated -= OnOFutureMinuteChartDataUpdated;
            _oFutureDataService.RequestFailed -= OnRequestFailed;
            _oFutureRealDataService.RealDataUpdated -= OnOFutureRealDataUpdated;
            _strategyService.StrategySignalUpdated -= OnStrategySignalUpdated;
            _evolution.ActiveFutureChartEvent -= OnPaperChartEvent;
            _strategyService.StrategyConfigurationUpdated -= OnStrategyConfigurationUpdated;
            _oFutureRealDataService.OrderBookUpdated -= OnOFutureOrderBookUpdated;
            await FlushTradeSnapshotsAsync().ConfigureAwait(false);
            await FlushLiveMinuteBarsAsync().ConfigureAwait(false);
            await DrainChartWritesAsync().ConfigureAwait(false);
        }

        public OFutureDashboardViewModel(
            OFutureDataService oFutureDataService,
            OFutureRealDataService oFutureRealDataService,
            LoggingService loggingService,
            OFutureChartStoreService chartStore,
            IXASessionService sessionService,
            OFutureStrategyService strategyService, StrategyEvolutionRuntime evolution)
        {
            _oFutureDataService = oFutureDataService;
            _oFutureRealDataService = oFutureRealDataService;
            _loggingService = loggingService;
            _chartStore = chartStore;
            _sessionService = sessionService;
            _strategyService = strategyService;
            _evolution = evolution;
            _chartMarkers = new()
            {
                [PlotControl] = new(PlotControl),
                [OneMinutePlot] = new(OneMinutePlot),
                [TenMinutePlot] = new(TenMinutePlot)
            };
            _tradeFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tradeFlushTimer.Tick += async (_, _) =>
            {
                if (_flushingTradeBatch || !_isActive) return;
                _flushingTradeBatch = true;
                try
                {
                    FlushManualStrategySignals();
                    while (_pendingPaperMarkers.TryDequeue(out var marker))
                        AddChartEvent(marker, marker.PublishedAtUtc);
                    _minuteBarAggregator.Prune();
                    var minuteFlush = FlushLiveMinuteBarsAsync();
                    RefreshLivePlots();
                    await minuteFlush;
                    _ = EvaluateCompletedBarsAsync();
                    await FlushTradeSnapshotsAsync();
                    if (_isActive) OnPropertyChanged(nameof(DashboardStatusText));
                }
                finally { _flushingTradeBatch = false; }
            };
            _minuteReconciliationTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
            _minuteReconciliationTimer.Tick += (_, _) => QueueMinuteReconciliation();

            OFutureWatchlist = new ObservableCollection<OFutureContract>(_oFutureDataService.Watchlist);
            _oFutureDataService.WatchlistUpdated += OnOFutureWatchlistUpdated;
            _oFutureDataService.ChartDataUpdated += OnOFutureChartDataUpdated;
            _oFutureDataService.MinuteChartDataUpdated += OnOFutureMinuteChartDataUpdated;
            _oFutureDataService.RequestFailed += OnRequestFailed;
            _oFutureRealDataService.RealDataUpdated += OnOFutureRealDataUpdated;
            _strategyService.StrategySignalUpdated += OnStrategySignalUpdated;
            _evolution.ActiveFutureChartEvent += OnPaperChartEvent;
            _strategyService.StrategyConfigurationUpdated += OnStrategyConfigurationUpdated;
            _oFutureRealDataService.OrderBookUpdated += OnOFutureOrderBookUpdated;

            SelectOFutureCardCommand = new RelayCommand(SelectOFutureCard);
            SelectOneMinuteRangeCommand = new RelayCommand(SelectOneMinuteRange);
            StartOFutureReceivingCommand = new RelayCommand(StartOFutureReceiving);
            StopOFutureReceivingCommand = new RelayCommand(StopOFutureReceiving);

            RebuildOFutureCards();
            SelectedOFutureCard = OFutureCards.FirstOrDefault();
            PlotControl.MouseUp += (_, _) => ClampXAxis();
            PlotControl.MouseWheel += (_, _) => ClampXAxis();
            ConfigureMinutePlotInteraction(OneMinutePlot);
            ConfigureMinutePlotInteraction(TenMinutePlot);
        }

        private void OnOFutureWatchlistUpdated(object? sender, EventArgs e)
        {
            OnUiThread(() =>
            {
                var selectedSymbol = SelectedOFutureCard?.Symbol;
                var previousSymbols = OFutureWatchlist.Select(contract => contract.Symbol).ToArray();
                OFutureWatchlist.Clear();
                foreach (var contract in _oFutureDataService.Watchlist)
                    OFutureWatchlist.Add(contract);

                var currentSymbols = OFutureWatchlist.Select(contract => contract.Symbol)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var symbol in previousSymbols.Where(symbol => !currentSymbols.Contains(symbol)))
                    _minuteBarAggregator.RemoveSymbol(symbol);
                foreach (var symbol in _minuteLookups.Keys.Where(symbol => !currentSymbols.Contains(symbol)).ToArray())
                {
                    CancelMinuteLookup(symbol);
                    _minuteLookupCompleted.Remove(symbol);
                    _dailyLookupCompleted.Remove(symbol);
                }

                RebuildOFutureCards(selectedSymbol);
                if (_isActive && _isReceivingOFutureData)
                    SyncOFutureSubscriptions();
            });
        }

        private void RebuildOFutureCards(string? selectedSymbol = null)
        {
            OFutureCards.Clear();
            foreach (var contract in OFutureWatchlist)
            {
                var card = new OFutureCardViewModel(contract);
                card.Update(
                    _oFutureRealDataService.GetLatestTrade(contract.Symbol),
                    _oFutureRealDataService.GetLatestOrderBook(contract.Symbol));
                OFutureCards.Add(card);
            }

            SelectedOFutureCard = selectedSymbol is null
                ? OFutureCards.FirstOrDefault()
                : OFutureCards.FirstOrDefault(card => string.Equals(card.Symbol, selectedSymbol, StringComparison.OrdinalIgnoreCase))
                    ?? OFutureCards.FirstOrDefault();
            if (SelectedOFutureCard is null) LoadSelectedOFutureChartData();
        }

        private void StartOFutureReceiving(object? parameter)
        {
            if (!_isReceivingOFutureData)
            {
                _receivingStartedUtc = DateTime.UtcNow;
                _receivingSessionId = Guid.NewGuid();
                _chartEvents.StartSession(_receivingStartedUtc);
                _completedBarStrategies.Clear();
                _dailyBarRequestDates.Clear();
                _chartMarkersDirty = true;
            }
            _isReceivingOFutureData = true;
            _reconciliationCancellation?.Cancel();
            _reconciliationCancellation?.Dispose();
            _reconciliationCancellation = new CancellationTokenSource();
            _minuteReconciliationTimer.Start();
            SyncOFutureSubscriptions();
            QueueMinuteReconciliation();
            NotifyTradeProperties();
            _loggingService.Log("해외선물 실시간 데이터 수신 시작");
        }

        private void StopOFutureReceiving(object? parameter)
        {
            foreach (var symbol in _dashboardSubscriptions.Keys.ToArray())
            {
                _oFutureRealDataService.UnsubscribeTrade(symbol);
                _dashboardSubscriptions.TryRemove(symbol, out _);
            }
            _isReceivingOFutureData = false;
            _completedBarStrategies.Clear();
            _minuteReconciliationTimer.Stop();
            _reconciliationCancellation?.Cancel();
            foreach (var symbol in _minuteLookups.Keys.ToArray()) CancelMinuteLookup(symbol);
            NotifyTradeProperties();
            _loggingService.Log("해외선물 실시간 데이터 수신 중지");
        }

        private void SyncOFutureSubscriptions()
        {
            if (!_isActive || !_isReceivingOFutureData) return;
            var watchlistSymbols = OFutureWatchlist.Select(contract => contract.Symbol)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var symbol in _dashboardSubscriptions.Keys.Where(symbol => !watchlistSymbols.Contains(symbol)).ToArray())
            {
                _oFutureRealDataService.UnsubscribeTrade(symbol);
                _dashboardSubscriptions.TryRemove(symbol, out _);
            }

            foreach (var symbol in watchlistSymbols)
            {
                if (_dashboardSubscriptions.TryAdd(symbol, 0) && !_oFutureRealDataService.SubscribeTrade(symbol))
                    _dashboardSubscriptions.TryRemove(symbol, out _);
            }
        }

        private void SelectOFutureCard(object? parameter)
        {
            if (parameter is OFutureCardViewModel card)
                SelectedOFutureCard = card;
        }

        private void OnOFutureRealDataUpdated(object? sender, OFutureRealData e)
        {
            if (_isShuttingDown) return;
            foreach (var bar in _minuteBarAggregator.Add(e))
                _pendingMinuteBars[(bar.Symbol, bar.IntervalMinutes, bar.TradeDate, bar.TradeTime)] = bar;
            _pendingTrades[e.Symbol] = e;
            if (_isActive && SelectedOFutureContract is { } selected
                && string.Equals(selected.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase))
            {
                if (_evolution.HasActiveVersion("OFuture", e.Symbol))
                {
                    var description = _evolution.DescribeSignal("OFuture", e.Symbol);
                    OnUiThread(() =>
                    {
                        if (SelectedOFutureContract?.Symbol != selected.Symbol) return;
                        StrategySignalText = description;
                    });
                }
                else _ = _strategyService.RunOnLiveDataAsync(e, _selectionVersion);
            }
            if (!_isActive)
            {
                _ = FlushLiveMinuteBarsAsync();
                _ = FlushTradeSnapshotsAsync();
            }
            OnUiThread(() =>
            {
                if (!_isActive) return;
                if (string.Equals(SelectedOFutureCard?.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase))
                {
                    _latestSelectedTrade = e;
                    _lastLiveTradeReceivedAtUtc = e.ReceivedAtUtc;
                    NotifyTradeProperties();
                }
            });
        }

        private async void QueueMinuteReconciliation()
        {
            if (!_isActive || !_isReceivingOFutureData || !_sessionService.IsConnected
                || _reconciliationCancellation is null || _checkingMinuteCache)
                return;
            var token = _reconciliationCancellation.Token;
            _checkingMinuteCache = true;
            try
            {
                var symbols = OFutureWatchlist.Select(contract => contract.Symbol)
                    .Where(symbol => !string.IsNullOrWhiteSpace(symbol)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (var symbol in symbols)
                {
                    if (token.IsCancellationRequested) break;
                    if (_minuteLookups.ContainsKey(symbol) || CheckedRecently(_minuteLookupCompleted, symbol)) continue;
                    var latest = await _chartStore.ReadLatestMinuteBarAsync(symbol, token);
                    if (!token.IsCancellationRequested && OFutureWatchlist.Any(item =>
                        string.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
                        RequestMinutesIfMissing(symbol, latest);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { _loggingService.Log($"해외선물 분봉 DB 조회 실패: {ex.Message}"); }
            finally { _checkingMinuteCache = false; }
        }

        private void RequestMinutesIfMissing(string symbol, OFutureMinuteBar? latest)
        {
            if (!_isActive || !_sessionService.IsConnected || _minuteLookups.ContainsKey(symbol)
                || CheckedRecently(_minuteLookupCompleted, symbol)) return;
            if (latest is not null && !IsValidMinuteBar(ToMinuteData(latest))) latest = null;
            var request = OFutureChartCachePolicy.MinuteRequest(latest, _oFutureRealDataService.GetLatestTrade(symbol),
                DateTime.UtcNow, DateTime.Today.AddYears(-1));
            if (request is not { } missing) return;

            var generation = ++_minuteLookupGeneration;
            var cancellation = new CancellationTokenSource();
            _minuteLookups.Add(symbol, (generation, cancellation));
            if (IsSelected(symbol)) MinuteStatus = "누락된 최신 1분봉을 Xing에서 조회 중";
            if (!_oFutureDataService.RequestMinuteChartData(symbol, intervalMinutes: 1, count: missing.Count,
                cancellationToken: cancellation.Token, earliestDate: missing.EarliestMinute, requestGeneration: generation,
                maxPages: 1,
                onStatusChanged: status => OnUiThread(() =>
                {
                    if (IsSelected(symbol) && _minuteLookups.TryGetValue(symbol, out var active) && active.Generation == generation)
                        MinuteStatus = $"최신 1분봉 · {status}";
                })))
            {
                CompleteMinuteLookup(symbol, generation);
                if (IsSelected(symbol)) MinuteStatus = "1분봉 요청을 대기열에 넣지 못했습니다";
            }
        }

        private static bool CheckedRecently(Dictionary<string, DateTime> completed, string symbol)
            => completed.TryGetValue(symbol, out var checkedAt) && DateTime.UtcNow - checkedAt < OFutureChartCachePolicy.RetryInterval;

        private void CancelMinuteLookup(string symbol)
        {
            if (!_minuteLookups.Remove(symbol, out var lookup)) return;
            lookup.Cancellation.Cancel();
            lookup.Cancellation.Dispose();
        }

        private bool CompleteMinuteLookup(string symbol, long generation)
        {
            if (!_minuteLookups.TryGetValue(symbol, out var lookup) || lookup.Generation != generation) return false;
            _minuteLookups.Remove(symbol);
            lookup.Cancellation.Dispose();
            _minuteLookupCompleted[symbol] = DateTime.UtcNow;
            return true;
        }

        private bool IsSelected(string symbol) => _isActive
            && string.Equals(SelectedOFutureCard?.Symbol, symbol, StringComparison.OrdinalIgnoreCase);

        private async Task FlushLiveMinuteBarsAsync()
        {
            if (_pendingMinuteBars.IsEmpty) return;
            var pending = new List<OFutureMinuteBar>();
            foreach (var entry in _pendingMinuteBars.ToArray())
                if (_pendingMinuteBars.TryRemove(entry.Key, out var bar)) pending.Add(bar);
            if (pending.Count == 0) return;

            var chartBars = pending.Select(bar => new OFutureMinuteChartData
            {
                Symbol = bar.Symbol, IntervalMinutes = bar.IntervalMinutes, Date = bar.TradeDate, Time = bar.TradeTime,
                Open = bar.Open, High = bar.High, Low = bar.Low, Close = bar.Close, Volume = bar.Volume
            }).ToArray();
            ApplyLiveMinuteBarsToSelectedChart(chartBars.Where(IsValidMinuteBar).ToArray());

            try
            {
                await _chartStore.UpsertMinuteBarsAsync(pending).ConfigureAwait(false);
                var storedAtUtc = DateTime.UtcNow;
                OnUiThread(() =>
                {
                    _lastStoredTradeAtUtc = storedAtUtc;
                    OnPropertyChanged(nameof(LastTradeStoredText));
                    OnPropertyChanged(nameof(DashboardStatusText));
                });
            }
            catch (Exception ex)
            {
                foreach (var bar in pending)
                    _pendingMinuteBars.AddOrUpdate((bar.Symbol, bar.IntervalMinutes, bar.TradeDate, bar.TradeTime), bar,
                        (_, current) => current.ReceivedAtUtc >= bar.ReceivedAtUtc ? current : bar);
                _loggingService.Log($"해외선물 실시간 분봉 저장 실패: {ex.Message}");
            }
        }

        private void ApplyLiveMinuteBarsToSelectedChart(IReadOnlyCollection<OFutureMinuteChartData> bars)
        {
            if (bars.Count == 0) return;
            OnUiThread(() =>
            {
                if (!_isActive) return;
                var symbol = SelectedOFutureCard?.Symbol;
                if (string.IsNullOrWhiteSpace(symbol)) return;
                var selectedBars = bars.Where(bar => string.Equals(bar.Symbol, symbol, StringComparison.OrdinalIgnoreCase)).ToArray();
                var oneMinute = selectedBars.Where(bar => bar.IntervalMinutes == 1).ToArray();
                var tenMinute = selectedBars.Where(bar => bar.IntervalMinutes == 10).ToArray();
                if (oneMinute.Length > 0)
                {
                    _oneMinuteBars = MergeBars(_oneMinuteBars, oneMinute);
                    _oneMinuteRevision++;
                    _oneMinutePlotDirty = true;
                }
                if (tenMinute.Length > 0)
                {
                    _tenMinuteBars = MergeBars(_tenMinuteBars, tenMinute);
                    _tenMinuteRevision++;
                    _tenMinutePlotDirty = true;
                }
            });
        }

        private void RefreshLivePlots()
        {
            if (_chartMarkersDirty) UpdatePlot();
            if (_chartMarkersDirty || _oneMinutePlotDirty) UpdateMinutePlot(OneMinutePlot, _oneMinuteBars, 1);
            if (_chartMarkersDirty || _tenMinutePlotDirty) UpdateMinutePlot(TenMinutePlot, _tenMinuteBars, 10);
            _chartMarkersDirty = _oneMinutePlotDirty = _tenMinutePlotDirty = false;
        }

        private void OnStrategySignalUpdated(object? sender, OFutureStrategySignalEventArgs e)
        {
            if (e.RequestGeneration != Volatile.Read(ref _selectionVersion)) return;
            Interlocked.Exchange(ref _pendingManualStatus, e);
            if (e.ChartEvent is null) return;
            _pendingManualMarkers.Enqueue(e);
            while (_pendingManualMarkers.Count > 2000) _pendingManualMarkers.TryDequeue(out _);
        }

        private void FlushManualStrategySignals()
        {
            bool IsCurrent(OFutureStrategySignalEventArgs e) =>
                _isReceivingOFutureData && e.ReceivedAtUtc >= _receivingStartedUtc
                && e.RequestGeneration == _selectionVersion && IsSelected(e.Symbol)
                && !_evolution.HasActiveVersion("OFuture", e.Symbol);

            var status = Interlocked.Exchange(ref _pendingManualStatus, null);
            if (status is not null && IsCurrent(status))
                StrategySignalText = status.Action == "ERROR"
                    ? $"전략 실행 오류 · {status.Detail} · 주문 미전송"
                    : $"{status.Action} · {status.Count} · {status.ReceivedAtUtc.ToLocalTime():HH:mm:ss} · 신호 전용(주문 미전송)";

            while (_pendingManualMarkers.TryDequeue(out var signal))
            {
                if (IsCurrent(signal) && signal.ChartEvent is { } marker &&
                    _strategyService.FindAssignment(signal.Symbol)?.MainStrategyPath == marker.Strategy)
                    AddChartEvent(marker with { RunId = _receivingSessionId, PublishedAtUtc = signal.ReceivedAtUtc },
                        signal.ReceivedAtUtc);
            }
        }

        private void OnPaperChartEvent(object? sender, OFutureChartEvent marker)
        {
            if (!_isActive || !_isReceivingOFutureData || _isShuttingDown) return;
            _pendingPaperMarkers.Enqueue(marker);
            while (_pendingPaperMarkers.Count > 2000) _pendingPaperMarkers.TryDequeue(out _);
        }

        private void AddChartEvent(OFutureChartEvent marker, DateTime receivedAt)
        {
            if (_isReceivingOFutureData && receivedAt >= _receivingStartedUtc && _chartEvents.Add(marker, receivedAt))
                _chartMarkersDirty = true;
        }

        private void OnStrategyConfigurationUpdated(object? sender, EventArgs e)
            => OnUiThread(() =>
            {
                foreach (var (symbol, evaluator) in _completedBarStrategies.ToArray())
                    if (_strategyService.FindAssignment(symbol) is not { } assignment || !evaluator.Matches(assignment))
                        _completedBarStrategies.Remove(symbol);
                RefreshStrategySignalStatus();
            });

        private async Task EvaluateCompletedBarsAsync()
        {
            if (_evaluatingCompletedBars || !_isActive || !_isReceivingOFutureData
                || SelectedOFutureCard?.Symbol is not { } symbol
                || _evolution.HasActiveVersion("OFuture", symbol)
                || _strategyService.FindAssignment(symbol) is not { } assignment
                || OFutureStrategyService.CompletedBarInterval(assignment) is not { } interval
                || _oFutureRealDataService.GetLatestTrade(symbol) is not { } quote
                || quote.ReceivedAtUtc < _receivingStartedUtc
                || quote.ReceivedAtUtc < DateTime.UtcNow.AddMinutes(-1)
                || _selectionCancellation is not { } selection)
                return;

            if (!_completedBarStrategies.TryGetValue(symbol, out var evaluator) || !evaluator.Matches(assignment))
                _completedBarStrategies[symbol] = evaluator = new(_strategyService, assignment, interval);

            var generation = _selectionVersion;
            var session = _receivingStartedUtc;
            var cancellationToken = selection.Token;
            _evaluatingCompletedBars = true;
            try
            {
                if (interval == 0 && OFutureChartCachePolicy.ParseDate(quote.OverseasDate) is { } tradingDate
                    && evaluator.Bucket != default && tradingDate > evaluator.Bucket)
                {
                    _dailyLookupCompleted.Remove(symbol);
                    _dailyBarRequestDates.Remove(symbol);
                }

                if (interval == 0 && OFutureChartCachePolicy.ParseDate(quote.OverseasDate) is { } date
                    && (!_dailyBarRequestDates.TryGetValue(symbol, out var requestedAt)
                        || DateTime.UtcNow - requestedAt >= OFutureChartCachePolicy.RetryInterval))
                {
                    _dailyBarRequestDates[symbol] = DateTime.UtcNow;
                    RequestDailyIfMissing(symbol, date.AddDays(-7), date, generation,
                        date.AddDays(-1), cancellationToken);
                }

                var markers = await evaluator.EvaluateAsync(quote, cancellationToken);
                if (generation != _selectionVersion || session != _receivingStartedUtc
                    || !_completedBarStrategies.TryGetValue(symbol, out var current)
                    || !ReferenceEquals(evaluator, current) || _evolution.HasActiveVersion("OFuture", symbol))
                    return;
                foreach (var marker in markers) AddChartEvent(marker, quote.ReceivedAtUtc);
                if (markers.LastOrDefault() is { } latest)
                    StrategySignalText = $"{latest.Action} · {latest.Quantity} · 완성 봉 페이퍼 · 주문 미전송";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (generation == _selectionVersion && session == _receivingStartedUtc && _isReceivingOFutureData)
                    StrategySignalText = $"완성 봉 전략 실행 오류 · {ex.Message}";
                _loggingService.Log($"해외선물 완성 봉 전략 실행 오류 ({symbol}): {ex.Message}");
            }
            finally { _evaluatingCompletedBars = false; }
        }

        private void RefreshStrategySignalStatus()
        {
            if (SelectedOFutureCard is null)
            {
                StrategySignalText = "해외선물 계약을 선택하세요";
                return;
            }
            var symbol = SelectedOFutureCard.Symbol;
            var description = _evolution.DescribeSignal("OFuture", symbol);
            StrategySignalText = description.Length > 0 ? description : _strategyService.GetLiveStrategyStatus(symbol);
        }

        private void OnOFutureOrderBookUpdated(object? sender, OFutureOrderBook e)
        {
            // Dashboard now subscribes to OVC only.
        }

        private void LoadSelectedOFutureChartData()
        {
            var card = SelectedOFutureCard;
            if (card is null)
            {
                _selectedOFutureChartData = Array.Empty<OFutureChartData>();
                _oneMinuteBars = Array.Empty<OFutureMinuteChartData>();
                _tenMinuteBars = Array.Empty<OFutureMinuteChartData>();
                UpdateMinutePlot(OneMinutePlot, _oneMinuteBars, 1, preserveView: false);
                UpdateMinutePlot(TenMinutePlot, _tenMinuteBars, 10, preserveView: false);
                ChartStatus = "관심계약을 선택하세요";
                MinuteStatus = "관심계약을 선택하세요";
                RequestStatus = "대기";
                return;
            }

            var endDate = DateTime.Today;
            var startDate = endDate.AddYears(-1);
            var symbol = card.Symbol;
            var version = _selectionVersion;
            var cancellationToken = _selectionCancellation?.Token ?? CancellationToken.None;
            _selectedOFutureChartData = Array.Empty<OFutureChartData>();
            _oneMinuteBars = Array.Empty<OFutureMinuteChartData>();
            _tenMinuteBars = Array.Empty<OFutureMinuteChartData>();
            UpdateMinutePlot(OneMinutePlot, _oneMinuteBars, 1, preserveView: false);
            UpdateMinutePlot(TenMinutePlot, _tenMinuteBars, 10, preserveView: false);
            ChartStatus = "저장 일봉 조회 중";
            MinuteStatus = "분봉 DB 조회 중";
            RequestStatus = "저장 차트 조회 중";
            _ = LoadSavedChartsAsync(symbol, startDate, endDate, version, cancellationToken);
        }

        private async Task LoadSavedChartsAsync(string symbol, DateTime startDate, DateTime endDate, long version,
            CancellationToken cancellationToken)
        {
            var dailyRevisionAtStart = _dailyChartRevision;
            var oneMinuteRevisionAtStart = _oneMinuteRevision;
            var tenMinuteRevisionAtStart = _tenMinuteRevision;
            try
            {
                var dailyTask = _chartStore.ReadDailyBarsAsync(symbol, startDate, endDate, cancellationToken);
                var oneTask = _chartStore.ReadMinuteBarsAsync(symbol, 1, startDate, endDate, cancellationToken);
                var tenTask = _chartStore.ReadMinuteBarsAsync(symbol, 10, startDate, endDate, cancellationToken);
                await Task.WhenAll(dailyTask, oneTask, tenTask).ConfigureAwait(false);
                var savedDaily = dailyTask.Result.Select(bar => new OFutureChartData
                {
                    Symbol = bar.Symbol, Date = bar.TradeDate, Open = bar.Open, High = bar.High,
                    Low = bar.Low, Close = bar.Close, Volume = bar.Volume
                }).Where(IsValidDailyBar).ToArray();
                var savedOneMinute = oneTask.Result.Select(ToMinuteData).ToArray();
                var savedTenMinute = tenTask.Result.Select(ToMinuteData).ToArray();
                var aggregatedTenMinute = BuildTenMinuteChartBars(savedOneMinute);
                var savedTenMinuteSource = MergeBars(savedTenMinute, aggregatedTenMinute);
                var latestMinute = oneTask.Result.LastOrDefault(bar => IsValidMinuteBar(ToMinuteData(bar)));
                OnUiThread(() =>
                {
                    if (cancellationToken.IsCancellationRequested || version != _selectionVersion || !IsSelected(symbol)) return;
                    _selectedOFutureChartData = _dailyChartRevision == dailyRevisionAtStart
                        ? savedDaily : MergeDailyBars(savedDaily, _selectedOFutureChartData);
                    _oneMinuteBars = _oneMinuteRevision == oneMinuteRevisionAtStart
                        ? savedOneMinute : MergeBars(savedOneMinute, _oneMinuteBars);
                    _tenMinuteBars = _tenMinuteRevision == tenMinuteRevisionAtStart
                        ? savedTenMinuteSource : MergeBars(savedTenMinuteSource, _tenMinuteBars);
                    UpdatePlot(preserveView: false);
                    UpdateMinutePlot(OneMinutePlot, _oneMinuteBars, 1, preserveView: false);
                    UpdateMinutePlot(TenMinutePlot, _tenMinuteBars, 10, preserveView: false);
                    ChartStatus = savedDaily.Length > 0 ? $"저장 일봉 {savedDaily.Length}개 표시" : "저장된 일봉이 없습니다";
                    RequestStatus = "저장 차트 조회 완료";
                    ShowCachedMinuteStatus();
                    RequestMinutesIfMissing(symbol, latestMinute);
                    RequestDailyIfMissing(symbol, startDate, endDate, version,
                        OFutureChartCachePolicy.ParseDate(savedDaily.LastOrDefault()?.Date), cancellationToken);
                });

                if (oneTask.Result.Count > 0)
                {
                    try
                    {
                        await _chartStore.RebuildTenMinuteBarsAsync(symbol, startDate, endDate);
                    }
                    catch (Exception ex)
                    {
                        _loggingService.Log($"해외선물 저장 1분봉에서 10분봉 재생성 실패: {symbol}, {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _loggingService.Log($"해외선물 저장 차트 조회 실패: {symbol}, {ex.Message}");
                OnUiThread(() =>
                {
                    if (cancellationToken.IsCancellationRequested || version != _selectionVersion || !IsSelected(symbol)) return;
                    ChartStatus = "DB 차트 조회 실패 · Xing 조회 시도";
                    RequestMinutesIfMissing(symbol, latest: null);
                    RequestDailyIfMissing(symbol, startDate, endDate, version, latest: null, cancellationToken);
                });
            }
        }

        private void RequestDailyIfMissing(string symbol, DateTime startDate, DateTime endDate, long version,
            DateTime? latest, CancellationToken cancellationToken)
        {
            if (!_sessionService.IsConnected || CheckedRecently(_dailyLookupCompleted, symbol)
                || OFutureChartCachePolicy.DailyRequestStart(latest, startDate, endDate) is not { } missingFrom) return;
            var count = Math.Min(2000, (endDate - missingFrom).Days + 1);
            var accepted = _oFutureDataService.RequestChartData(symbol, missingFrom, endDate,
                periodCode: "0", count: count, cancellationToken: cancellationToken,
                onStatusChanged: status => OnRequestSchedulerStatus(symbol, "o3108", status, version),
                requestGeneration: version);
            RequestStatus = accepted ? "누락된 최신 일봉 요청" : "일봉 요청을 대기열에 넣지 못했습니다";
        }

        private static OFutureMinuteChartData ToMinuteData(OFutureMinuteBar bar) => new()
        {
            Symbol = bar.Symbol, IntervalMinutes = bar.IntervalMinutes, Date = bar.TradeDate, Time = bar.TradeTime,
            Open = bar.Open, High = bar.High, Low = bar.Low, Close = bar.Close, Volume = bar.Volume
        };

        private static OFutureMinuteChartData[] BuildTenMinuteChartBars(
            IEnumerable<OFutureMinuteChartData> oneMinuteBars,
            IEnumerable<OFutureMinuteChartData>? changedMinuteBars = null)
        {
            var source = oneMinuteBars;
            if (changedMinuteBars is not null)
            {
                var changedBuckets = changedMinuteBars.Select(GetTenMinuteBucket).Where(bucket => bucket.HasValue)
                    .Select(bucket => bucket!.Value).ToHashSet();
                if (changedBuckets.Count == 0) return Array.Empty<OFutureMinuteChartData>();
                source = source.Where(bar => GetTenMinuteBucket(bar) is { } bucket && changedBuckets.Contains(bucket));
            }

            return OFutureMinuteBarAggregator.AggregateTenMinuteBars(source.Select(bar => new OFutureMinuteBar
            {
                Symbol = bar.Symbol,
                IntervalMinutes = 1,
                TradeDate = bar.Date,
                TradeTime = bar.Time,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                Volume = bar.Volume
            })).Select(ToMinuteData).ToArray();
        }

        private static (string Date, string Time)? GetTenMinuteBucket(OFutureMinuteChartData bar)
        {
            if (!DateTime.TryParseExact(bar.Date + bar.Time, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var tradeTime))
                return null;
            var start = new DateTime(tradeTime.Year, tradeTime.Month, tradeTime.Day,
                tradeTime.Hour, tradeTime.Minute - tradeTime.Minute % 10, 0);
            return (start.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                start.ToString("HHmmss", CultureInfo.InvariantCulture));
        }

        private void OnOFutureMinuteChartDataUpdated(object? sender, OFutureMinuteChartDataEventArgs e)
        {
            if (_isShuttingDown) return;
            var bars = e.Data.Where(IsValidMinuteBar).ToArray();
            if (bars.Length == 0)
            {
                OnUiThread(() =>
                {
                    var handled = e.IsComplete && CompleteMinuteLookup(e.Symbol, e.RequestGeneration);
                    if (handled && IsSelected(e.Symbol))
                        MinuteStatus = "Xing에서 새로 저장할 1분봉을 받지 못했습니다";
                });
                return;
            }
            var received = DateTime.UtcNow;
            var stored = bars.Where(IsValidMinuteBar).Select(bar => new OFutureMinuteBar
            {
                Symbol = bar.Symbol, IntervalMinutes = bar.IntervalMinutes, TradeDate = bar.Date, TradeTime = bar.Time,
                Open = bar.Open, High = bar.High, Low = bar.Low, Close = bar.Close, Volume = bar.Volume, ReceivedAtUtc = received
            }).ToArray();
            TrackChartWrite(() => PersistMinuteBarsAsync(stored, e.Symbol));
            OnUiThread(() =>
            {
                var handled = e.IsComplete && CompleteMinuteLookup(e.Symbol, e.RequestGeneration);
                if (!IsSelected(e.Symbol)) return;
                if (e.IntervalMinutes == 1) _oneMinuteRevision++;
                else if (e.IntervalMinutes == 10) _tenMinuteRevision++;
                if (e.IntervalMinutes == 1)
                {
                    _oneMinuteBars = MergeBars(_oneMinuteBars, bars);
                    var changedTenMinute = BuildTenMinuteChartBars(_oneMinuteBars, bars);
                    if (changedTenMinute.Length > 0)
                    {
                        _tenMinuteBars = MergeBars(_tenMinuteBars, changedTenMinute);
                        _tenMinuteRevision++;
                    }
                    UpdateMinutePlot(OneMinutePlot, _oneMinuteBars, 1);
                    UpdateMinutePlot(TenMinutePlot, _tenMinuteBars, 10);
                }
                else if (e.IntervalMinutes == 10)
                {
                    _tenMinuteBars = MergeBars(_tenMinuteBars, bars);
                    UpdateMinutePlot(TenMinutePlot, _tenMinuteBars, 10);
                }
                if (handled)
                    MinuteStatus = GetMinuteBarsForPlot(OneMinutePlot, _oneMinuteBars).Length > 0
                        ? $"최신 1분봉 {bars.Length}개 반영" : "조회한 분봉이 선택 기간에 없습니다";
            });
        }

        private async Task PersistMinuteBarsAsync(IReadOnlyCollection<OFutureMinuteBar> bars, string symbol)
        {
            try
            {
                await _chartStore.UpsertMinuteBarsAsync(bars).ConfigureAwait(false);
                var oneMinuteDates = bars.Where(bar => bar.IntervalMinutes == 1)
                    .Select(bar => DateTime.TryParseExact(bar.TradeDate, "yyyyMMdd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date) ? date.Date : (DateTime?)null)
                    .Where(date => date.HasValue).Select(date => date!.Value).ToArray();
                if (oneMinuteDates.Length > 0)
                    await _chartStore.RebuildTenMinuteBarsAsync(symbol, oneMinuteDates.Min(), oneMinuteDates.Max())
                        .ConfigureAwait(false);
                OnUiThread(() =>
                {
                    if (string.Equals(SelectedOFutureCard?.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                        RequestStatus = $"분봉 {bars.Count}개 저장 완료";
                });
            }
            catch (Exception ex)
            {
                _loggingService.Log($"해외선물 분봉 저장 실패: {ex.Message}");
                OnUiThread(() =>
                {
                    if (string.Equals(SelectedOFutureCard?.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                        RequestStatus = "분봉 저장 실패 · 기존 DB 데이터 유지";
                });
            }
        }

        private void TrackChartWrite(Func<Task> write)
        {
            TaskCompletionSource completion;
            long id;
            lock (_writeTaskLock)
            {
                if (_isShuttingDown) return;
                id = ++_nextWriteTaskId;
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _chartWriteTasks[id] = completion.Task;
            }
            _ = RunWriteAsync();

            async Task RunWriteAsync()
            {
                try { await write().ConfigureAwait(false); }
                finally
                {
                    _chartWriteTasks.TryRemove(id, out _);
                    completion.TrySetResult();
                }
            }
        }

        private async Task DrainChartWritesAsync()
        {
            while (!_chartWriteTasks.IsEmpty)
                await Task.WhenAll(_chartWriteTasks.Values).ConfigureAwait(false);
        }

        private static bool IsValidMinuteBar(OFutureMinuteChartData bar)
            => DateTime.TryParseExact(bar.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                && DateTime.TryParseExact(bar.Time, "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                && bar.Volume >= 0 && double.IsFinite(bar.Open) && double.IsFinite(bar.High) && double.IsFinite(bar.Low) && double.IsFinite(bar.Close)
                && bar.High >= Math.Max(bar.Open, bar.Close) && bar.Low <= Math.Min(bar.Open, bar.Close);

        private static IReadOnlyList<OFutureMinuteChartData> MergeBars(IEnumerable<OFutureMinuteChartData> existing, IEnumerable<OFutureMinuteChartData> incoming)
            => existing.Concat(incoming).GroupBy(bar => (bar.Date, bar.Time))
                .Select(group => group.Last()).OrderBy(bar => bar.Date, StringComparer.Ordinal).ThenBy(bar => bar.Time, StringComparer.Ordinal).ToArray();

        private async Task PersistDailyBarsAsync(IReadOnlyCollection<OFutureDailyBar> bars, string symbol)
        {
            try
            {
                await _chartStore.UpsertDailyBarsAsync(bars).ConfigureAwait(false);
                OnUiThread(() =>
                {
                    if (string.Equals(SelectedOFutureCard?.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                        RequestStatus = $"일봉 {bars.Count}개 저장 완료";
                });
            }
            catch (Exception ex)
            {
                _loggingService.Log($"해외선물 일봉 저장 실패: {ex.Message}");
                OnUiThread(() =>
                {
                    if (string.Equals(SelectedOFutureCard?.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                        RequestStatus = "일봉 저장 실패 · 기존 DB 데이터 유지";
                });
            }
        }

        private void UpdateMinutePlot(WpfPlot control, IReadOnlyList<OFutureMinuteChartData> data, int interval, bool preserveView = true)
        {
            var plot = control.Plot;
            var previousMin = plot.Axes.Bottom.Min;
            var previousMax = plot.Axes.Bottom.Max;
            var hadPlottedData = ReferenceEquals(control, OneMinutePlot) ? _oneMinuteHasPlottedData : _tenMinuteHasPlottedData;
            var hasPreviousView = preserveView && hadPlottedData && double.IsFinite(previousMin) && double.IsFinite(previousMax) && previousMax > previousMin;
            plot.Clear();
            var ordered = GetMinuteBarsForPlot(control, data);
            // Use consecutive plotting slots so missing minutes/session breaks do not create empty horizontal space.
            var plotEpoch = new DateTime(2000, 1, 1);
            var slotWidth = TimeSpan.FromMinutes(interval);
            var candles = ordered.Select((bar, index) => new OHLC(bar.Open, bar.High, bar.Low, bar.Close,
                plotEpoch + TimeSpan.FromTicks(slotWidth.Ticks * index), slotWidth)).ToArray();
            var slots = new Dictionary<(string Date, string Time), double>();
            for (var i = 0; i < ordered.Length; i++)
                slots[(ordered[i].Date, ordered[i].Time)] = candles[i].DateTime.ToOADate();
            if (candles.Length > 0)
            {
                plot.Add.Candlestick(candles);
                plot.Axes.DateTimeTicksBottom();
                if (plot.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.DateTimeAutomatic ticks)
                    ticks.LabelFormatter = tick => FormatMinuteTick(tick, ordered, plotEpoch, interval);
                _chartMarkers[control].Draw(SelectedChartEvents(), timestamp =>
                {
                    var bucket = OFutureChartMarkers.BucketStart(timestamp, interval);
                    var key = (bucket.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                        bucket.ToString("HHmmss", CultureInfo.InvariantCulture));
                    return slots.TryGetValue(key, out var x) ? x : null;
                }, false);
                plot.Axes.AutoScale();
                if (hasPreviousView) plot.Axes.SetLimitsX(previousMin, previousMax);

                plot.Axes.ContinuouslyAutoscale = true;
                plot.Axes.ContinuousAutoscaleAction = renderPack =>
                {
                    var minX = renderPack.Plot.Axes.Bottom.Min;
                    var maxX = renderPack.Plot.Axes.Bottom.Max;
                    var firstIndex = Math.Clamp((int)Math.Floor((minX - plotEpoch.ToOADate()) * 1440 / interval) - 1, 0, ordered.Length - 1);
                    var lastIndex = Math.Clamp((int)Math.Ceiling((maxX - plotEpoch.ToOADate()) * 1440 / interval) + 1, 0, ordered.Length - 1);
                    var visible = ordered.Skip(firstIndex).Take(lastIndex - firstIndex + 1).ToArray();
                    if (visible.Length == 0) return;
                    var (min, max) = _chartMarkers[control].IncludeVisiblePrices(minX, maxX,
                        visible.Min(bar => bar.Low), visible.Max(bar => bar.High));
                    var padding = CalculateMinutePricePadding(min, max);
                    renderPack.Plot.Axes.SetLimitsY(min - padding, max + padding);
                };
            }
            else _chartMarkers[control].Draw([], _ => null, false);
            if (ReferenceEquals(control, OneMinutePlot)) _oneMinuteHasPlottedData = candles.Length > 0;
            else _tenMinuteHasPlottedData = candles.Length > 0;
            control.Refresh();
        }

        private double CalculateMinutePricePadding(double min, double max)
        {
            var range = max - min;
            if (double.IsFinite(range) && range > 0) return range * 0.05;

            var tickSize = SelectedOFutureContract?.TickSize ?? 0;
            if (double.IsFinite(tickSize) && tickSize > 0) return tickSize * 2;

            return Math.Max(Math.Max(Math.Abs(min), Math.Abs(max)) * 0.00001, 0.00000001);
        }

        private IEnumerable<OFutureChartEvent> SelectedChartEvents() =>
            _chartEvents.ForSymbol(_isActive ? SelectedOFutureCard?.Symbol : null);

        private static string FormatMinuteTick(DateTime tick, IReadOnlyList<OFutureMinuteChartData> bars, DateTime plotEpoch, int interval)
        {
            var index = (int)Math.Round((tick - plotEpoch).TotalMinutes / interval);
            if (index < 0 || index >= bars.Count) return string.Empty;
            var bar = bars[index];
            return DateTime.TryParseExact(bar.Date + bar.Time, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var tradeTime)
                ? tradeTime.ToString("MM/dd HH:mm", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private void ConfigureMinutePlotInteraction(WpfPlot control)
        {
            var pan = control.UserInputProcessor.UserActionResponses
                .OfType<ScottPlot.Interactivity.UserActionResponses.MouseDragPan>().FirstOrDefault();
            if (pan is not null) pan.LockY = true;
            control.MouseUp += (_, _) => ClampMinuteXAxis(control);
            control.MouseWheel += (_, _) => ClampMinuteXAxis(control);
        }

        private void ClampMinuteXAxis(WpfPlot control)
        {
            var isOneMinute = ReferenceEquals(control, OneMinutePlot);
            var data = isOneMinute ? _oneMinuteBars : _tenMinuteBars;
            var interval = isOneMinute ? 1 : 10;
            var count = GetMinuteBarsForPlot(control, data).Length;
            if (count == 0) return;
            var axis = control.Plot.Axes.Bottom;
            var slotDays = interval / 1440.0;
            var epoch = new DateTime(2000, 1, 1).ToOADate();
            var allowedMin = epoch - slotDays / 2;
            var allowedMax = epoch + (count - 1) * slotDays + slotDays / 2;
            var currentMin = axis.Min;
            var currentMax = axis.Max;
            var width = currentMax - currentMin;
            var allowedWidth = allowedMax - allowedMin;
            if (width >= allowedWidth) control.Plot.Axes.SetLimitsX(allowedMin, allowedMax);
            else if (currentMin < allowedMin) control.Plot.Axes.SetLimitsX(allowedMin, allowedMin + width);
            else if (currentMax > allowedMax) control.Plot.Axes.SetLimitsX(allowedMax - width, allowedMax);
        }

        private OFutureMinuteChartData[] GetMinuteBarsForPlot(
            WpfPlot control, IReadOnlyList<OFutureMinuteChartData> data)
        {
            IEnumerable<OFutureMinuteChartData> bars = data.Where(IsValidMinuteBar)
                .OrderBy(bar => bar.Date, StringComparer.Ordinal)
                .ThenBy(bar => bar.Time, StringComparer.Ordinal);
            if (ReferenceEquals(control, OneMinutePlot) || ReferenceEquals(control, TenMinutePlot))
            {
                var today = DateTime.Today;
                var firstDate = OneMinuteRange switch
                {
                    "Today" => today,
                    "Week" => today.AddDays(-6),
                    "Month" => today.AddMonths(-1),
                    _ => DateTime.MinValue
                };
                if (firstDate != DateTime.MinValue)
                    bars = bars.Where(bar => DateTime.TryParseExact(bar.Date, "yyyyMMdd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var barDate) && barDate.Date >= firstDate && barDate.Date <= today);
            }
            return bars.ToArray();
        }

        private void SelectOneMinuteRange(object? parameter)
        {
            if (parameter is not string range || range is not ("Today" or "Week" or "Month" or "All")
                || string.Equals(OneMinuteRange, range, StringComparison.Ordinal)) return;
            OneMinuteRange = range;
            UpdateMinutePlot(OneMinutePlot, _oneMinuteBars, 1, preserveView: false);
            UpdateMinutePlot(TenMinutePlot, _tenMinuteBars, 10, preserveView: false);
            ShowCachedMinuteStatus();
        }

        private void ShowCachedMinuteStatus()
        {
            if (SelectedOFutureCard is { } card && _minuteLookups.ContainsKey(card.Symbol))
                MinuteStatus = "누락된 최신 1분봉을 Xing에서 조회 중";
            else if (GetMinuteBarsForPlot(OneMinutePlot, _oneMinuteBars).Length > 0
                || GetMinuteBarsForPlot(TenMinutePlot, _tenMinuteBars).Length > 0)
                MinuteStatus = "선택 기간의 저장 분봉 표시";
            else
                MinuteStatus = _sessionService.IsConnected ? "선택 기간에 저장된 분봉이 없습니다"
                    : "선택 기간에 저장된 분봉이 없고 Xing에 연결되어 있지 않습니다";
        }

        private void NotifyTradeProperties()
        {
            OnPropertyChanged(nameof(SelectedContractName));
            OnPropertyChanged(nameof(SelectedContractSymbol));
            OnPropertyChanged(nameof(TradePrice));
            OnPropertyChanged(nameof(TradeChange));
            OnPropertyChanged(nameof(TradeOhl));
            OnPropertyChanged(nameof(TradeVolume));
            OnPropertyChanged(nameof(TradeSideVolume));
            OnPropertyChanged(nameof(TradeTimes));
            OnPropertyChanged(nameof(ConnectionStatus));
            OnPropertyChanged(nameof(ReceivingStatus));
            OnPropertyChanged(nameof(LastTradeReceivedText));
            OnPropertyChanged(nameof(LastTradeStoredText));
            OnPropertyChanged(nameof(IsTradeStale));
            OnPropertyChanged(nameof(DashboardStatusText));
        }

        private void OnRequestFailed(object? sender, OFutureRequestErrorEventArgs e)
        {
            OnUiThread(() =>
            {
                if (string.Equals(e.TrCode, "o3103", StringComparison.OrdinalIgnoreCase))
                {
                    if (CompleteMinuteLookup(e.Symbol, e.RequestGeneration) && IsSelected(e.Symbol))
                        MinuteStatus = $"최신 분봉 조회 · {e.Message}";
                    return;
                }
                if (string.Equals(e.TrCode, "o3108", StringComparison.OrdinalIgnoreCase))
                {
                    if (e.RequestGeneration != _selectionVersion || !IsSelected(e.Symbol)) return;
                    _dailyLookupCompleted[e.Symbol] = DateTime.UtcNow;
                }
                RequestStatus = $"{e.TrCode} 요청 실패: {e.Message}";
                if (string.IsNullOrWhiteSpace(e.Symbol)
                    || string.Equals(SelectedOFutureCard?.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase))
                    ChartStatus = "API 요청 오류 · 저장된 데이터는 유지됩니다";
            });
        }

        private void OnRequestSchedulerStatus(string symbol, string trCode, string status, long version)
        {
            void UpdateStatus()
            {
                if (version == _selectionVersion && IsSelected(symbol))
                    RequestStatus = $"{trCode} {status}";
            }

            // Keep scheduler status transitions ordered relative to the actual Query send.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) UpdateStatus();
            else
            {
                try { dispatcher.Invoke(UpdateStatus); }
                catch (InvalidOperationException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) { }
            }
        }

        private async Task LoadLatestTradeSnapshotAsync(string symbol, long version)
        {
            try
            {
                var snapshot = await _chartStore.ReadLatestTradeAsync(symbol);
                if (snapshot is null) return;
                OnUiThread(() =>
                {
                    if (version != _selectionVersion || !string.Equals(SelectedOFutureCard?.Symbol, symbol, StringComparison.OrdinalIgnoreCase)) return;
                    if (_latestSelectedTrade is not null && _latestSelectedTrade.ReceivedAtUtc >= snapshot.ReceivedAtUtc) return;
                    _latestSelectedTrade = new OFutureRealData
                    {
                        Symbol = snapshot.Symbol, OverseasDate = snapshot.OverseasDate, KoreaDate = snapshot.KoreaDate,
                        TradeTime = snapshot.TradeTime, KoreaTradeTime = snapshot.KoreaTradeTime, Price = snapshot.Price,
                        Change = snapshot.Change, ChangeSign = snapshot.ChangeSign, ChangeRate = snapshot.ChangeRate,
                        Open = snapshot.Open, High = snapshot.High, Low = snapshot.Low, TradeQuantity = snapshot.TradeQuantity,
                        TotalQuantity = snapshot.TotalQuantity, TradeDirection = snapshot.TradeDirection,
                        SellTradeQuantity = snapshot.SellTradeQuantity, BuyTradeQuantity = snapshot.BuyTradeQuantity,
                        ReceivedAtUtc = snapshot.ReceivedAtUtc
                    };
                    _lastStoredTradeAtUtc = snapshot.StoredAtUtc;
                    NotifyTradeProperties();
                });
            }
            catch (Exception ex) { _loggingService.Log($"해외선물 OVC 스냅샷 조회 실패: {symbol}, {ex.Message}"); }
        }

        private async Task FlushTradeSnapshotsAsync()
        {
            await _tradeFlushGate.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (var entry in _pendingTrades.ToArray())
                {
                    if (!_pendingTrades.TryRemove(entry.Key, out var trade)) continue;
                    var snapshot = new OFutureLatestTradeSnapshot
                    {
                        Symbol = trade.Symbol, OverseasDate = trade.OverseasDate, KoreaDate = trade.KoreaDate,
                        TradeTime = trade.TradeTime, KoreaTradeTime = trade.KoreaTradeTime, Price = trade.Price,
                        Change = trade.Change, ChangeSign = trade.ChangeSign, ChangeRate = trade.ChangeRate,
                        Open = trade.Open, High = trade.High, Low = trade.Low, TradeQuantity = trade.TradeQuantity,
                        TotalQuantity = trade.TotalQuantity, TradeDirection = trade.TradeDirection,
                        SellTradeQuantity = trade.SellTradeQuantity, BuyTradeQuantity = trade.BuyTradeQuantity,
                        ReceivedAtUtc = trade.ReceivedAtUtc
                    };
                    try
                    {
                        await _chartStore.UpsertLatestTradeAsync(snapshot).ConfigureAwait(false);
                        var storedAtUtc = DateTime.UtcNow;
                        OnUiThread(() => _lastStoredTradeAtUtc = storedAtUtc);
                    }
                    catch (Exception ex)
                    {
                        _pendingTrades.AddOrUpdate(entry.Key, trade,
                            (_, current) => current.ReceivedAtUtc > trade.ReceivedAtUtc ? current : trade);
                        _loggingService.Log($"해외선물 OVC 스냅샷 저장 실패: {entry.Key}, {ex.Message}");
                    }
                }
            }
            finally { _tradeFlushGate.Release(); }
            OnUiThread(() =>
            {
                OnPropertyChanged(nameof(LastTradeStoredText));
                OnPropertyChanged(nameof(DashboardStatusText));
            });
        }

        private void OnOFutureChartDataUpdated(object? sender, OFutureChartDataEventArgs e)
        {
            if (_isShuttingDown || e.QueryType != "0") return;
            var startDate = DateTime.Today.AddYears(-1);
            var endDate = DateTime.Today;
            var incoming = FilterChartData(e.Data, e.Symbol, startDate, endDate)
                .Where(IsValidDailyBar).ToArray();
            if (incoming.Length == 0)
            {
                OnUiThread(() =>
                {
                    if (e.RequestGeneration != 0) _dailyLookupCompleted[e.Symbol] = DateTime.UtcNow;
                    if (e.RequestGeneration == _selectionVersion && IsSelected(e.Symbol))
                    {
                        ChartStatus = "o3108 응답에 유효한 일봉이 없습니다.";
                        RequestStatus = "일봉 데이터 없음";
                    }
                });
                return;
            }

            var receivedAt = DateTime.UtcNow;
            var dailyBars = incoming.Select(item => new OFutureDailyBar
            {
                Symbol = item.Symbol, TradeDate = item.Date, SourceTrCode = "o3108", QueryType = "0",
                Open = item.Open, High = item.High, Low = item.Low, Close = item.Close, SourcePrice = item.Close,
                Volume = item.Volume, ReceivedAtUtc = receivedAt
            }).ToArray();
            TrackChartWrite(() => PersistDailyBarsAsync(dailyBars, e.Symbol));
            OnUiThread(() =>
            {
                if (e.RequestGeneration != 0) _dailyLookupCompleted[e.Symbol] = DateTime.UtcNow;
                if (!_isActive || e.RequestGeneration != _selectionVersion
                    || !string.Equals(SelectedOFutureCard?.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase)) return;
                _dailyChartRevision++;
                _selectedOFutureChartData = MergeDailyBars(_selectedOFutureChartData, incoming);
                UpdatePlot();
                ChartStatus = $"o3108 일봉 최신화 완료 · {incoming.Length}개 봉";
                RequestStatus = "일봉 API 응답 수신";
            });
        }

        private static IReadOnlyList<OFutureChartData> FilterChartData(
            IEnumerable<OFutureChartData> data, string symbol, DateTime startDate, DateTime endDate)
            => data.Where(item => string.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                    && DateTime.TryParseExact(item.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    && date.Date >= startDate.Date && date.Date <= endDate.Date)
                .OrderBy(item => item.Date, StringComparer.Ordinal)
                .ToArray();

        private static bool IsValidDailyBar(OFutureChartData item)
            => DateTime.TryParseExact(item.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                && item.Volume >= 0 && double.IsFinite(item.Open) && double.IsFinite(item.High)
                && double.IsFinite(item.Low) && double.IsFinite(item.Close)
                && item.High >= Math.Max(item.Open, item.Close) && item.Low <= Math.Min(item.Open, item.Close);

        private static IReadOnlyList<OFutureChartData> MergeDailyBars(
            IEnumerable<OFutureChartData> existing, IEnumerable<OFutureChartData> incoming)
            => existing.Concat(incoming).GroupBy(item => item.Date, StringComparer.Ordinal)
                .Select(group => group.Last()).OrderBy(item => item.Date, StringComparer.Ordinal).ToArray();

        private void UpdatePlot(bool preserveView = true)
        {
            var plot = PlotControl.Plot;
            var previousMin = plot.Axes.Bottom.Min;
            var previousMax = plot.Axes.Bottom.Max;
            var hasPreviousView = preserveView && _dailyHasPlottedData
                && double.IsFinite(previousMin) && double.IsFinite(previousMax) && previousMax > previousMin;
            plot.Clear();
            _chartMarkers[PlotControl].Draw([], _ => null, true);

            if (SelectedOFutureCard is null)
            {
                _dataMinOADate = double.NaN;
                _dataMaxOADate = double.NaN;
                _dailyHasPlottedData = false;
                PlotControl.Refresh();
                return;
            }

            var chartData = _selectedOFutureChartData.Where(IsValidDailyBar).ToList();

            var candles = new List<OHLC>(chartData.Count);
            foreach (var item in chartData.OrderBy(item => item.Date, StringComparer.Ordinal))
            {
                if (!DateTime.TryParseExact(item.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;
                candles.Add(new OHLC(item.Open, item.High, item.Low, item.Close, date, TimeSpan.FromDays(1)));
            }

            if (candles.Count == 0)
            {
                _dataMinOADate = double.NaN;
                _dataMaxOADate = double.NaN;
                _dailyHasPlottedData = false;
                PlotControl.Refresh();
                return;
            }

            var candlePlot = plot.Add.Candlestick(candles);
            var dates = candles.Select(candle => candle.DateTime.Date).ToHashSet();
            _chartMarkers[PlotControl].Draw(SelectedChartEvents(),
                timestamp => dates.Contains(timestamp.Date) ? timestamp.Date.ToOADate() : null, false);
            candlePlot.FallingColor = new ScottPlot.Color(0, 0, 255);
            candlePlot.RisingColor = new ScottPlot.Color(255, 0, 0);
            plot.Axes.DateTimeTicksBottom();
            if (plot.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.DateTimeAutomatic ticks)
            {
                ticks.LabelFormatter = tickDate => tickDate.ToString("yy/MM/dd", CultureInfo.InvariantCulture);
            }

            _dataMinOADate = candles[0].DateTime.ToOADate();
            _dataMaxOADate = candles[^1].DateTime.ToOADate();
            _dailyHasPlottedData = true;
            if (hasPreviousView) plot.Axes.SetLimitsX(previousMin, previousMax);
            else plot.Axes.SetLimitsX(Math.Max(_dataMinOADate - 2, _dataMaxOADate - 60), _dataMaxOADate + 2);
            plot.Axes.ContinuouslyAutoscale = true;
            plot.Axes.ContinuousAutoscaleAction = renderPack =>
            {
                var start = DateTime.FromOADate(renderPack.Plot.Axes.Bottom.Min);
                var end = DateTime.FromOADate(renderPack.Plot.Axes.Bottom.Max);
                var visible = candles.Where(candle => candle.DateTime >= start && candle.DateTime <= end).ToList();
                if (visible.Count == 0) return;
                var (min, max) = _chartMarkers[PlotControl].IncludeVisiblePrices(start.ToOADate(), end.ToOADate(),
                    visible.Min(candle => candle.Low), visible.Max(candle => candle.High));
                var padding = Math.Max((max - min) * 0.05, Math.Max(Math.Abs(min), Math.Abs(max)) * 0.001);
                if (padding == 0) padding = 0.01;
                renderPack.Plot.Axes.SetLimitsY(min - padding, max + padding);
            };

            PlotControl.Refresh();
            ClampXAxis();
        }

        private void ClampXAxis()
        {
            if (double.IsNaN(_dataMinOADate) || double.IsNaN(_dataMaxOADate)) return;
            const double margin = 2;
            var allowedMin = _dataMinOADate - margin;
            var allowedMax = _dataMaxOADate + margin;
            var currentMin = PlotControl.Plot.Axes.Bottom.Min;
            var currentMax = PlotControl.Plot.Axes.Bottom.Max;
            var width = currentMax - currentMin;
            var allowedWidth = allowedMax - allowedMin;

            if (width >= allowedWidth)
                PlotControl.Plot.Axes.SetLimitsX(allowedMin, allowedMax);
            else if (currentMin < allowedMin)
                PlotControl.Plot.Axes.SetLimitsX(allowedMin, allowedMin + width);
            else if (currentMax > allowedMax)
                PlotControl.Plot.Axes.SetLimitsX(allowedMax - width, allowedMax);
            else return;

            PlotControl.Refresh();
        }

        private static void OnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) action();
            else _ = dispatcher.BeginInvoke(action);
        }
    }
}
