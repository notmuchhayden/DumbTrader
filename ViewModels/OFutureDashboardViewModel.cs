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
        private readonly ConcurrentDictionary<string, byte> _reconciliationSymbolsPending = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _reconciliationTokenSources = new(StringComparer.OrdinalIgnoreCase);
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

        public string ChartStatus { get => _chartStatus; private set => SetProperty(ref _chartStatus, value); }
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
            foreach (var source in _reconciliationTokenSources.Values)
            {
                source.Cancel();
                source.Dispose();
            }
            _reconciliationTokenSources.Clear();
            _reconciliationSymbolsPending.Clear();
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
            _tradeFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tradeFlushTimer.Tick += async (_, _) =>
            {
                _minuteBarAggregator.Prune();
                await FlushLiveMinuteBarsAsync();
                await FlushTradeSnapshotsAsync();
                OnPropertyChanged(nameof(DashboardStatusText));
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
                foreach (var symbol in _reconciliationTokenSources.Keys.Where(symbol => !currentSymbols.Contains(symbol)).ToArray())
                {
                    if (_reconciliationTokenSources.TryRemove(symbol, out var source))
                    {
                        source.Cancel();
                        source.Dispose();
                    }
                    _reconciliationSymbolsPending.TryRemove(symbol, out _);
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
            _minuteReconciliationTimer.Stop();
            _reconciliationCancellation?.Cancel();
            foreach (var source in _reconciliationTokenSources.Values)
            {
                source.Cancel();
                source.Dispose();
            }
            _reconciliationTokenSources.Clear();
            _reconciliationSymbolsPending.Clear();
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

        private void QueueMinuteReconciliation()
        {
            if (!_isActive || !_isReceivingOFutureData || !_sessionService.IsConnected || _reconciliationCancellation is null)
                return;
            if (!_reconciliationSymbolsPending.IsEmpty)
            {
                _loggingService.Log("해외선물 o3103 보정이 아직 진행 중이어서 이번 주기는 건너뜁니다.");
                return;
            }

            var token = _reconciliationCancellation.Token;
            var symbols = OFutureWatchlist.Select(contract => contract.Symbol)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var symbol in symbols)
            {
                if (!_reconciliationSymbolsPending.TryAdd(symbol, 0)) continue;
                var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                if (!_reconciliationTokenSources.TryAdd(symbol, requestCancellation))
                {
                    requestCancellation.Dispose();
                    _reconciliationSymbolsPending.TryRemove(symbol, out _);
                    continue;
                }
                var accepted = _oFutureDataService.RequestMinuteChartData(symbol, intervalMinutes: 1, count: 50,
                    cancellationToken: requestCancellation.Token, earliestDate: DateTime.Today, requestGeneration: 0,
                    maxPages: 1,
                    onStatusChanged: status => OnRequestSchedulerStatus(symbol, "o3103 보정", status));
                if (!accepted)
                {
                    CompleteMinuteReconciliation(symbol);
                    _loggingService.Log($"해외선물 o3103 보정 요청을 큐에 등록하지 못했습니다: {symbol}");
                }
            }
            if (symbols.Length == 0) _loggingService.Log("해외선물 o3103 보정 대상 관심계약이 없습니다.");
        }

        private void CompleteMinuteReconciliation(string symbol)
        {
            _reconciliationSymbolsPending.TryRemove(symbol, out _);
            if (_reconciliationTokenSources.TryRemove(symbol, out var source)) source.Dispose();
        }

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
                    UpdateMinutePlot(OneMinutePlot, _oneMinuteBars, 1);
                }
                if (tenMinute.Length > 0)
                {
                    _tenMinuteBars = MergeBars(_tenMinuteBars, tenMinute);
                    _tenMinuteRevision++;
                    UpdateMinutePlot(TenMinutePlot, _tenMinuteBars, 10);
                }
            });
        }

        private void OnStrategySignalUpdated(object? sender, OFutureStrategySignalEventArgs e)
        {
            OnUiThread(() =>
            {
                if (e.RequestGeneration != _selectionVersion
                    || !string.Equals(SelectedOFutureCard?.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase)
                    || _evolution.HasActiveVersion("OFuture", e.Symbol)) return;
                StrategySignalText = e.Action == "ERROR"
                    ? $"전략 실행 오류 · {e.Detail} · 주문 미전송"
                    : $"{e.Action} · {e.Count} · {e.ReceivedAtUtc.ToLocalTime():HH:mm:ss} · 신호 전용(주문 미전송)";
            });
        }

        private void OnStrategyConfigurationUpdated(object? sender, EventArgs e)
            => OnUiThread(RefreshStrategySignalStatus);

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
            ChartStatus = "DB 차트 읽기 중 · API 요청 대기열 등록 중";
            _ = LoadSavedChartsAsync(symbol, startDate, endDate, version);
            var dailyChartQueued = _oFutureDataService.RequestChartData(symbol, startDate, endDate,
                periodCode: "0", count: 2000, cancellationToken: cancellationToken,
                onStatusChanged: status => OnRequestSchedulerStatus(symbol, "o3108", status),
                requestGeneration: version);
            RequestStatus = dailyChartQueued ? "DB 분봉 표시 · o3103은 10분 주기 보정" : "일봉 요청을 대기열에 넣지 못함";
        }

        private async Task LoadSavedChartsAsync(string symbol, DateTime startDate, DateTime endDate, long version)
        {
            var dailyRevisionAtStart = _dailyChartRevision;
            var oneMinuteRevisionAtStart = _oneMinuteRevision;
            var tenMinuteRevisionAtStart = _tenMinuteRevision;
            try
            {
                var dailyTask = _chartStore.ReadDailyBarsAsync(symbol, startDate, endDate);
                var oneTask = _chartStore.ReadMinuteBarsAsync(symbol, 1, startDate, endDate);
                var tenTask = _chartStore.ReadMinuteBarsAsync(symbol, 10, startDate, endDate);
                await Task.WhenAll(dailyTask, oneTask, tenTask).ConfigureAwait(false);
                var savedDaily = dailyTask.Result.Select(bar => new OFutureChartData
                {
                    Symbol = bar.Symbol, Date = bar.TradeDate, Open = bar.Open, High = bar.High,
                    Low = bar.Low, Close = bar.Close, Volume = bar.Volume
                }).ToArray();
                var savedOneMinute = oneTask.Result.Select(ToMinuteData).ToArray();
                var savedTenMinute = tenTask.Result.Select(ToMinuteData).ToArray();
                var aggregatedTenMinute = BuildTenMinuteChartBars(savedOneMinute);
                var savedTenMinuteSource = aggregatedTenMinute.Length > 0 ? aggregatedTenMinute : savedTenMinute;
                OnUiThread(() =>
                {
                    if (version != _selectionVersion || !string.Equals(SelectedOFutureCard?.Symbol, symbol, StringComparison.OrdinalIgnoreCase)) return;
                    _selectedOFutureChartData = _dailyChartRevision == dailyRevisionAtStart
                        ? savedDaily : MergeDailyBars(savedDaily, _selectedOFutureChartData);
                    _oneMinuteBars = _oneMinuteRevision == oneMinuteRevisionAtStart
                        ? savedOneMinute : MergeBars(savedOneMinute, _oneMinuteBars);
                    _tenMinuteBars = _tenMinuteRevision == tenMinuteRevisionAtStart
                        ? savedTenMinuteSource : MergeBars(savedTenMinuteSource, _tenMinuteBars);
                    UpdatePlot(preserveView: false);
                    UpdateMinutePlot(OneMinutePlot, _oneMinuteBars, 1, preserveView: false);
                    UpdateMinutePlot(TenMinutePlot, _tenMinuteBars, 10, preserveView: false);
                    ChartStatus = "DB 차트 표시 중 · API 최신화 요청 중";
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
            catch (Exception ex)
            {
                _loggingService.Log($"해외선물 저장 차트 조회 실패: {symbol}, {ex.Message}");
                OnUiThread(() => { if (version == _selectionVersion) ChartStatus = "DB 차트 읽기 실패 · API 응답 대기"; });
            }
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
            if (e.RequestGeneration == 0 && e.IsComplete)
                CompleteMinuteReconciliation(e.Symbol);
            var bars = e.Data.Where(IsValidMinuteBar).ToArray();
            if (bars.Length == 0)
            {
                OnUiThread(() =>
                {
                    if (_isActive && (e.RequestGeneration == 0 || e.RequestGeneration == _selectionVersion)
                        && string.Equals(SelectedOFutureCard?.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase))
                        ChartStatus = $"{e.IntervalMinutes}분봉 · {e.StatusMessage}";
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
                if (!_isActive || (e.RequestGeneration != 0 && e.RequestGeneration != _selectionVersion)
                    || !string.Equals(SelectedOFutureCard?.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase)) return;
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
                ChartStatus = e.IsComplete
                    ? e.IsTruncated ? $"{e.IntervalMinutes}분봉 일부 조회 · {e.StatusMessage}" : $"{e.IntervalMinutes}분봉 조회 완료"
                    : $"{e.IntervalMinutes}분봉 {e.PageNumber}페이지 조회 중";
                RequestStatus = $"{e.IntervalMinutes}분봉 DB 저장 중";
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
            if (candles.Length > 0)
            {
                plot.Add.Candlestick(candles);
                plot.Axes.DateTimeTicksBottom();
                if (plot.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.DateTimeAutomatic ticks)
                    ticks.LabelFormatter = tick => FormatMinuteTick(tick, ordered, plotEpoch, interval);
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
                    var min = visible.Min(bar => bar.Low);
                    var max = visible.Max(bar => bar.High);
                    var padding = CalculateMinutePricePadding(min, max);
                    renderPack.Plot.Axes.SetLimitsY(min - padding, max + padding);
                };
            }
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
            if (string.Equals(e.TrCode, "o3103", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(e.Symbol))
                CompleteMinuteReconciliation(e.Symbol);
            OnUiThread(() =>
            {
                RequestStatus = $"{e.TrCode} 요청 실패: {e.Message}";
                if (string.IsNullOrWhiteSpace(e.Symbol)
                    || string.Equals(SelectedOFutureCard?.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase))
                    ChartStatus = "API 요청 오류 · 저장된 데이터는 유지됩니다";
            });
        }

        private void OnRequestSchedulerStatus(string symbol, string trCode, string status)
        {
            void UpdateStatus()
            {
                if (_isActive && string.Equals(SelectedOFutureCard?.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
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
                    if (e.RequestGeneration == _selectionVersion
                        && string.Equals(SelectedOFutureCard?.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase))
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
                var min = visible.Min(candle => candle.Low);
                var max = visible.Max(candle => candle.High);
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
