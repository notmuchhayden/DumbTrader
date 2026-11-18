using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;
using Microsoft.EntityFrameworkCore;
using ScottPlot;
using ScottPlot.WPF;

namespace DumbTrader.ViewModels
{
    public class StockDashboardViewModel : ViewModelBase
    {
        private readonly StrategyService _strategyService;
        private readonly StrategyEvolutionRuntime _evolution;
        private readonly DumbTraderDbContext _dbContext;
        private readonly StockRealDataService _stockRealDataService;
        private readonly LoggingService _loggingService;
        private readonly OFutureChartMarkers _markers;
        private readonly OFutureChartEventHistory _signals = new();
        private readonly Dictionary<string, IReadOnlyList<StockStrategyPriceLevel>> _levels = new();
        private readonly Dictionary<string, SortedDictionary<DateTime, StockMinuteBar>> _minutes = new();
        private readonly Dictionary<(string Symbol, string Date, string Time), StockMinuteBar> _pendingMinuteBars = new();
        private readonly Dictionary<string, DateTime> _minuteRequests = new();
        private IReadOnlyList<StockMinuteBar> _savedMinutes = [];
        private readonly StockMinuteBarStore _minuteStore;
        private readonly StockDataService _stockDataService;
        private DateTime _minuteSaveDue;
        private DateTime _minutePruneDue;
        private bool _minuteSaveRunning;
        private long _minuteLoadVersion;
        private readonly Dictionary<string, RealS3_K3_Data> _latestQuotes = new();
        private DateTime _sessionDate;
        private bool _chartDirty;
        private string _chartPeriod = "일봉";
        public IReadOnlyList<string> ChartPeriods { get; } = new[] { "1분봉", "일봉", "주봉", "월봉" };
        public string ChartPeriod
        {
            get => _chartPeriod;
            set
            {
                if (SetProperty(ref _chartPeriod, value))
                    UpdatePlot();
            }
        }
        private bool _isReceivingRealData = false;
        private string _strategySignalText = string.Empty;
        public string StrategySignalText { get => _strategySignalText; private set => SetProperty(ref _strategySignalText, value); }

        // 관심 종목 리스트
        public ObservableCollection<StrategyStockInfo> Watchlist { get; }

        // 카드 뷰모델 컬렉션 (DashboardView에서 바인딩)
        public ObservableCollection<StockCardViewModel> StockCards { get; } = new ObservableCollection<StockCardViewModel>();

        private StockCardViewModel? _selectedStockCard;
        public StockCardViewModel? SelectedStockCard
        {
            get => _selectedStockCard;
            set
            {
                if (SetProperty(ref _selectedStockCard, value))
                {
                    _latestSelectedRealData = value is not null ? _latestQuotes.GetValueOrDefault(value.shcode) : null;
                    StrategySignalText = value is null ? string.Empty : _evolution.DescribeSignal("Stock", value.shcode);
                    LoadSelectedChartData();
                    _savedMinutes = [];
                    _ = LoadSelectedMinutesAsync(value?.shcode);
                    UpdatePlot();
                }
            }
        }

        // 선택 종목의 일봉 차트
        public WpfPlot PlotControl { get; } = new WpfPlot();
        private readonly DateTime _chartBaseDate = new DateTime(2000, 1, 1);
        private readonly List<DateTime> _chartDates = new();
        private List<StockChartData> _selectedChartData = new();
        private RealS3_K3_Data? _latestSelectedRealData;
        private double _dataMinOADate = double.NaN;
        private double _dataMaxOADate = double.NaN;

        // 종목 선택 명령 (예: 상세보기로 이동)
        public ICommand SelectStockCommand { get; }
        // 실시간 데이터 수신 제어 명령
        public ICommand StartReceivingCommand { get; }
        // 실시간 데이터 수신 중지 명령
        public ICommand StopReceivingCommand { get; }

        public StockDashboardViewModel(StrategyService strategyService,
            DumbTraderDbContext dbContext,
            StockRealDataService stockRealDataService,
            LoggingService loggingService, StrategyEvolutionRuntime evolution, StockDataService stockDataService,
            StockMinuteBarStore minuteStore)
        {
            _strategyService = strategyService;
            _dbContext = dbContext;
            _stockRealDataService = stockRealDataService;
            _loggingService = loggingService;
            _evolution = evolution;
            _minuteStore = minuteStore;
            _stockDataService = stockDataService;
            stockDataService.StockMinuteBarsUpdated += (_, bars) =>
                _ = PlotControl.Dispatcher.InvokeAsync(() => ApplyServerMinuteBars(bars));
            _markers = new(PlotControl);
            _evolution.StockChartEvent += (_, marker) =>
            {
                if (PlotControl.Dispatcher.CheckAccess()) AddSignal(marker);
                else _ = PlotControl.Dispatcher.InvokeAsync(() => AddSignal(marker));
            };
            var chartTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            chartTimer.Tick += (_, _) =>
            {
                MaintainMinutes();
                if (!_chartDirty) return;
                _chartDirty = false;
                UpdatePlot(preserveView: true);
            };
            chartTimer.Start();

            Watchlist = _strategyService.StrategyStocks;

            _stockRealDataService.RealDataUpdated += OnRealDataUpdated;
            stockDataService.StockChartDataInfoUpdated += (_, chart) => _ = PlotControl.Dispatcher.InvokeAsync(() =>
            {
                if (SelectedStockCard?.shcode != chart.shcode) return;
                LoadSelectedChartData();
                _chartDirty = true;
            });

            // Watchlist 변경 감지
            Watchlist.CollectionChanged += OnWatchlistChanged;
            StartReceivingCommand = new RelayCommand(StartRealData);
            StopReceivingCommand = new RelayCommand(StopRealData);
            SelectStockCommand = new RelayCommand(SelectStock);

            InitStockCards();

            SelectedStockCard = StockCards.FirstOrDefault();

            PlotControl.MouseUp += (_, _) => ClampXAxis();
            PlotControl.MouseWheel += (_, _) => ClampXAxis();
        }

        private void OnWatchlistChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_isReceivingRealData)
            {
                StartRealData(null);
            }
        }

        // Watchlist의 변경에 따라 실시간 데이터 구독을 관리하는 메서드
        private void StartRealData(object? parameter)
        {
            // 새로 추가된 종목 구독
            foreach (var item in Watchlist)
            {
                MarketType marketType = item.Stock.gubun == "2" ? MarketType.KOSDAQ : MarketType.KOSPI;

                _stockRealDataService.SubscribeStockRealData(item.Stock.shcode, marketType);
            }

            // Watchlist에서 제거된 종목 구독 해제
            var watchlistCodes = Watchlist.Select(item => item.Stock.shcode).ToHashSet();
            foreach (var (shcode, marketType) in _stockRealDataService.Subscriptions.ToList())
            {
                if (!watchlistCodes.Contains(shcode))
                {
                    _stockRealDataService.UnsubscribeStockRealData(shcode, marketType);
                }
            }

            _isReceivingRealData = true;

            _loggingService.Log("실시간 데이터 수신 시작");
        }

        private void StopRealData(object? parameter)
        {
            _stockRealDataService.UnsubscribeDashboard();
            _isReceivingRealData = false;

            _loggingService.Log("실시간 데이터 수신 중지");
        }


        /// <summary>
        /// Watchlist의 각 종목에 대해 StockCardViewModel을 생성하고,
        /// DB에서 최신 차트 데이터를 조회하여 초기값을 설정합니다.
        /// </summary>
        private void InitStockCards()
        {
            StockCards.Clear();

            foreach (var item in Watchlist)
            {
                var card = new StockCardViewModel
                {
                    hname = item.Stock.hname,
                    shcode = item.Stock.shcode
                };

                // latestData를 실시간 데이터 포맷(RealS3_K3_Data)으로 변환
                var realData = new RealS3_K3_Data();
                card.UpdateFromRealData(realData);
                StockCards.Add(card);
            }
        }

        private void OnRealDataUpdated(object? sender, RealS3_K3_Data e)
        {
            if (!PlotControl.Dispatcher.CheckAccess())
            {
                _ = PlotControl.Dispatcher.InvokeAsync(() => OnRealDataUpdated(sender, e));
                return;
            }
            if (e.price > 0 && DateTime.TryParseExact(e.chetime, "HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time))
            {
                var timestamp = StockMinuteBarStore.MarketNow.Date.Add(time.TimeOfDay);
                EnsureSession(timestamp.Date);
                _latestQuotes[e.shcode] = e;
                RecordMinute(e.shcode, timestamp, e.price, e.cvolume);
            }
            // 해당 종목의 카드를 찾아서 업데이트
            var card = StockCards.FirstOrDefault(c => c.shcode == e.shcode);
            if (card != null)
            {
                card.UpdateFromRealData(e);

                _loggingService.Log($"[실시간] {e.shcode} 현재가: {e.price:N0} 등락율: {e.drate:+0.00;-0.00}% 체결량: {e.cvolume:N0}");

                if (_isReceivingRealData && !_evolution.HasActiveVersion("Stock", e.shcode))
                {
                    var result = _strategyService.RunWithResult(e, false, 0);
                    if (result.MainResult is { Success: true } signal)
                    {
                        _levels[e.shcode] = signal.StockPriceLevels;
                        if (signal.Message is "BUY" or "SELL" && DateTime.TryParseExact(e.chetime, "HHmmss",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var signalTime))
                            AddSignal(new(Guid.NewGuid(), StockMinuteBarStore.MarketNow.Date.Add(signalTime.TimeOfDay), e.price,
                                signal.Count, signal.Message, OFutureChartEventKind.Signal, e.shcode,
                                Watchlist.FirstOrDefault(item => item.Stock?.shcode == e.shcode)?.Strategy?.MainStrategyPath ?? "",
                                "실시간", Detail: "전략 신호"));
                    }
                }
            }

            if (SelectedStockCard?.shcode == e.shcode)
            {
                _latestSelectedRealData = e;
                StrategySignalText = _evolution.DescribeSignal("Stock", e.shcode);
                _chartDirty = true;
            }
        }

        private void SelectStock(object? parameter)
        {
            if (parameter is StockCardViewModel card)
            {
                SelectedStockCard = card;
            }
        }

        private void UpdatePlot(bool preserveView = false)
        {
            var plot = PlotControl.Plot;
            var previousMin = plot.Axes.Bottom.Min;
            var previousMax = plot.Axes.Bottom.Max;
            var previousDataMax = _dataMaxOADate;
            var wasAtEnd = previousMax >= previousDataMax;
            plot.Clear();
            plot.HideLegend();
            _markers.Draw([], _ => null, false);
            _chartDates.Clear();

            if (SelectedStockCard == null)
            {
                _dataMinOADate = double.NaN;
                _dataMaxOADate = double.NaN;
                PlotControl.Refresh();
                return;
            }

            var chartData = _selectedChartData
                .Select(CloneChartData)
                .ToList();

            var latestRealData = _latestSelectedRealData;

            if (latestRealData != null)
            {
                ApplyLatestRealData(chartData, latestRealData);
            }
            var minute = ChartPeriod == "1분봉";
            if (minute)
                chartData = GetMinuteChart(SelectedStockCard.shcode);
            else if (ChartPeriod is "주봉" or "월봉")
                chartData = AggregateChartBars(chartData);

            if (chartData.Count == 0)
            {
                _dataMinOADate = double.NaN;
                _dataMaxOADate = double.NaN;
                PlotControl.Refresh();
                return;
            }

            var ohlcs = new List<OHLC>(chartData.Count);
            for (var i = 0; i < chartData.Count; i++)
            {
                if (!DateTime.TryParseExact(chartData[i].date, minute ? "yyyyMMddHHmm" : "yyyyMMdd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date))
                {
                    continue;
                }

                _chartDates.Add(date);
                ohlcs.Add(new OHLC(
                    chartData[i].open,
                    chartData[i].high,
                    chartData[i].low,
                    chartData[i].close,
                    _chartBaseDate.AddDays(ohlcs.Count),
                    TimeSpan.FromDays(1)));
            }

            if (ohlcs.Count == 0)
            {
                _dataMinOADate = double.NaN;
                _dataMaxOADate = double.NaN;
                PlotControl.Refresh();
                return;
            }

            var candles = plot.Add.Candlestick(ohlcs);
            candles.FallingColor = new ScottPlot.Color(0, 0, 255);
            candles.RisingColor = new ScottPlot.Color(255, 0, 0);
            var slots = _chartDates.Select((date, index) => (date, index))
                .ToDictionary(item => item.date, item => _chartBaseDate.AddDays(item.index).ToOADate());
            _markers.Draw(_signals.ForSymbol(SelectedStockCard.shcode),
                time => slots.TryGetValue(ChartBucket(time), out var x) ? x : null, false);
            if (!_evolution.HasActiveVersion("Stock", SelectedStockCard.shcode)
                && _levels.TryGetValue(SelectedStockCard.shcode, out var levels))
            {
                foreach (var level in levels.Where(level => double.IsFinite(level.Price) && level.Price > 0))
                {
                    var line = plot.Add.HorizontalLine(level.Price, color: Colors.DarkGreen);
                    line.LinePattern = LinePattern.Dashed;
                    line.LegendText = $"{level.Label} {level.Price:G10}";
                }
                if (levels.Count > 0)
                {
                    plot.Legend.BackgroundColor = Colors.White.WithOpacity(0.55);
                    plot.Legend.ShadowColor = Colors.Transparent;
                    plot.ShowLegend();
                }
            }

            plot.Axes.DateTimeTicksBottom();
            if (plot.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.DateTimeAutomatic ticks)
            {
                ticks.LabelFormatter = date =>
                {
                    var index = (int)Math.Round(date.Subtract(_chartBaseDate).TotalDays);
                    return index >= 0 && index < _chartDates.Count
                        ? _chartDates[index].ToString(minute ? "HH:mm" : "yy/MM/dd", CultureInfo.InvariantCulture)
                        : string.Empty;
                };
            }

            _dataMinOADate = ohlcs.First().DateTime.ToOADate();
            _dataMaxOADate = ohlcs.Last().DateTime.ToOADate();
            var firstVisible = Math.Max(_dataMinOADate - 2, _dataMaxOADate - 60);
            plot.Axes.SetLimitsX(firstVisible, _dataMaxOADate + 2);
            if (preserveView && double.IsFinite(previousDataMax) && previousMax > previousMin)
            {
                var shift = wasAtEnd ? _dataMaxOADate - previousDataMax : 0;
                plot.Axes.SetLimitsX(previousMin + shift, previousMax + shift);
            }

            // 컨트롤 크기나 현재 보이는 X축 범위가 바뀔 때마다 해당 구간에 맞게 Y축을 조정한다.
            plot.Axes.ContinuouslyAutoscale = true;
            plot.Axes.ContinuousAutoscaleAction = renderPack =>
            {
                var visibleStart = DateTime.FromOADate(renderPack.Plot.Axes.Bottom.Min);
                var visibleEnd = DateTime.FromOADate(renderPack.Plot.Axes.Bottom.Max);
                var visibleCandles = ohlcs
                    .Where(x => x.DateTime >= visibleStart && x.DateTime <= visibleEnd)
                    .ToList();

                if (visibleCandles.Count == 0)
                {
                    return;
                }

                var (minPrice, maxPrice) = _markers.IncludeVisiblePrices(visibleStart.ToOADate(), visibleEnd.ToOADate(),
                    visibleCandles.Min(x => x.Low), visibleCandles.Max(x => x.High));
                var priceMargin = Math.Max(1, (maxPrice - minPrice) * 0.05);
                renderPack.Plot.Axes.SetLimitsY(minPrice - priceMargin, maxPrice + priceMargin);
            };

            PlotControl.Refresh();
            ClampXAxis();
        }

        private void ClampXAxis()
        {
            if (double.IsNaN(_dataMinOADate) || double.IsNaN(_dataMaxOADate))
            {
                return;
            }

            const double margin = 2.0;
            var allowedMin = _dataMinOADate - margin;
            var allowedMax = _dataMaxOADate + margin;
            var currentMin = PlotControl.Plot.Axes.Bottom.Min;
            var currentMax = PlotControl.Plot.Axes.Bottom.Max;
            var currentWidth = currentMax - currentMin;
            var allowedWidth = allowedMax - allowedMin;

            if (currentWidth >= allowedWidth)
            {
                PlotControl.Plot.Axes.SetLimitsX(allowedMin, allowedMax);
            }
            else if (currentMin < allowedMin)
            {
                PlotControl.Plot.Axes.SetLimitsX(allowedMin, allowedMin + currentWidth);
            }
            else if (currentMax > allowedMax)
            {
                PlotControl.Plot.Axes.SetLimitsX(allowedMax - currentWidth, allowedMax);
            }
            else
            {
                return;
            }

            PlotControl.Refresh();
        }

        private void LoadSelectedChartData()
        {
            if (SelectedStockCard == null)
            {
                _selectedChartData = new List<StockChartData>();
                return;
            }

            var startDate = DateTime.Today.AddYears(-8).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            _selectedChartData = _dbContext.StockChartDatas
                .AsNoTracking()
                .Where(x => x.shcode == SelectedStockCard.shcode && string.Compare(x.date, startDate) >= 0)
                .OrderBy(x => x.date)
                .ToList();
        }

        private static StockChartData CloneChartData(StockChartData source)
        {
            return new StockChartData
            {
                shcode = source.shcode,
                date = source.date,
                open = source.open,
                high = source.high,
                low = source.low,
                close = source.close,
                jdiff_vol = source.jdiff_vol,
                value = source.value,
                jongchk = source.jongchk,
                rate = source.rate,
                pricechk = source.pricechk,
                ratevalue = source.ratevalue,
                sign = source.sign
            };
        }

        private void EnsureSession(DateTime date)
        {
            if (_sessionDate == date) return;
            _sessionDate = date;
            _minutes.Clear();
            _minuteRequests.Clear();
            _latestQuotes.Clear();
            _levels.Clear();
            _latestSelectedRealData = null;
            _signals.StartSession(DateTime.UtcNow);
            _ = LoadSelectedMinutesAsync(SelectedStockCard?.shcode);
        }

        private void AddSignal(OFutureChartEvent marker)
        {
            EnsureSession(StockMinuteBarStore.MarketNow.Date);
            _signals.Add(marker, DateTime.UtcNow);
            if (SelectedStockCard?.shcode == marker.Symbol) _chartDirty = true;
        }

        private void RecordMinute(string symbol, DateTime timestamp, long price, long volume)
        {
            var minute = timestamp.AddTicks(-(timestamp.Ticks % TimeSpan.TicksPerMinute));
            if (!_minutes.TryGetValue(symbol, out var bars))
                _minutes[symbol] = bars = new();
            if (!bars.TryGetValue(minute, out var bar))
            {
                bar = new StockMinuteBar(symbol, minute.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                    minute.ToString("HHmm", CultureInfo.InvariantCulture), price, price, price, price, 0, false);
            }
            if (bar.IsServer) return;
            bar = bar with { High = Math.Max(bar.High, price), Low = Math.Min(bar.Low, price),
                Close = price, Volume = bar.Volume + Math.Abs(volume) };
            bars[minute] = bar;
            QueueMinute(bar);
        }

        private void ApplyServerMinuteBars(IReadOnlyList<StockMinuteBar> bars)
        {
            foreach (var bar in bars)
            {
                QueueMinute(bar);
                if (!DateTime.TryParseExact(bar.TradeDate + bar.TradeTime, "yyyyMMddHHmm",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) || timestamp.Date != _sessionDate) continue;
                if (!_minutes.TryGetValue(bar.Symbol, out var symbolBars)) _minutes[bar.Symbol] = symbolBars = new();
                symbolBars[timestamp] = bar;
            }
            _chartDirty = true;
        }

        private void QueueMinute(StockMinuteBar bar)
        {
            var key = (bar.Symbol, bar.TradeDate, bar.TradeTime);
            if (bar.IsServer || !_pendingMinuteBars.TryGetValue(key, out var existing) || !existing.IsServer)
                _pendingMinuteBars[key] = bar;
        }

        private void MaintainMinutes()
        {
            var marketTime = StockMinuteBarStore.MarketNow;
            if (_isReceivingRealData && marketTime.Date == _sessionDate)
            {
                foreach (var item in Watchlist)
                {
                    if (item.Stock?.shcode is not { Length: > 0 } symbol
                        || !_latestQuotes.TryGetValue(symbol, out var latest)
                        || !DateTime.TryParseExact(latest.chetime, "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                        || marketTime - marketTime.Date.Add(time.TimeOfDay) > TimeSpan.FromMinutes(35)
                        || (_minuteRequests.TryGetValue(symbol, out var last) && marketTime - last < TimeSpan.FromMinutes(30))) continue;
                    if (_stockDataService.RequestLatestStockMinuteBars(symbol, marketTime.Date)) _minuteRequests[symbol] = marketTime;
                }
            }
            if (!_minuteSaveRunning && marketTime >= _minuteSaveDue)
                _ = FlushMinutesAsync(marketTime);
        }

        private async Task FlushMinutesAsync(DateTime marketTime)
        {
            _minuteSaveDue = marketTime.AddSeconds(5);
            var prune = marketTime >= _minutePruneDue;
            if (_pendingMinuteBars.Count == 0 && !prune) return;
            _minuteSaveRunning = true;
            var batch = _pendingMinuteBars.Values.ToArray();
            _pendingMinuteBars.Clear();
            try
            {
                await _minuteStore.SaveAsync(batch, marketTime.Date, prune);
                if (prune) _minutePruneDue = marketTime.Date.AddDays(1);
            }
            catch (Exception ex)
            {
                foreach (var bar in batch)
                    if (!_pendingMinuteBars.ContainsKey((bar.Symbol, bar.TradeDate, bar.TradeTime))) QueueMinute(bar);
                _loggingService.Log($"주식 분봉 저장 실패: {ex.Message}");
            }
            finally { _minuteSaveRunning = false; }
        }

        private async Task LoadSelectedMinutesAsync(string? symbol)
        {
            var version = ++_minuteLoadVersion;
            if (symbol is null) return;
            try
            {
                var bars = await _minuteStore.ReadAsync(symbol, StockMinuteBarStore.MarketNow.Date);
                if (version != _minuteLoadVersion) return;
                _savedMinutes = bars;
                if (ChartPeriod == "1분봉") _chartDirty = true;
            }
            catch (Exception ex) { _loggingService.Log($"주식 분봉 조회 실패 ({symbol}): {ex.Message}"); }
        }

        private List<StockChartData> GetMinuteChart(string symbol)
        {
            var bars = _minutes.TryGetValue(symbol, out var current) ? _savedMinutes.Concat(current.Values) : _savedMinutes;
            var oldest = StockMinuteBarStore.MarketNow.Date.AddDays(-6).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            return bars.Where(bar => string.CompareOrdinal(bar.TradeDate, oldest) >= 0)
                .GroupBy(bar => bar.TradeDate + bar.TradeTime).OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group =>
                {
                    var bar = group.OrderBy(item => item.IsServer).Last();
                    return new StockChartData { date = group.Key, open = bar.Open, high = bar.High,
                        low = bar.Low, close = bar.Close, jdiff_vol = bar.Volume };
                }).ToList();
        }

        private DateTime ChartBucket(DateTime time) => ChartPeriod switch
        {
            "1분봉" => time.AddTicks(-(time.Ticks % TimeSpan.TicksPerMinute)),
            "주봉" => time.Date.AddDays(-(((int)time.DayOfWeek + 6) % 7)),
            "월봉" => new DateTime(time.Year, time.Month, 1),
            _ => time.Date
        };

        private List<StockChartData> AggregateChartBars(IEnumerable<StockChartData> bars) =>
            bars.Select(bar => (Bar: bar, Date: DateTime.TryParseExact(bar.date, "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? (DateTime?)date : null))
                .Where(item => item.Date.HasValue).OrderBy(item => item.Date)
                .GroupBy(item => ChartBucket(item.Date!.Value))
                .Select(group => new StockChartData
                {
                    date = group.Key.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                    open = group.First().Bar.open, high = group.Max(item => item.Bar.high),
                    low = group.Min(item => item.Bar.low), close = group.Last().Bar.close
                }).ToList();

        private static void ApplyLatestRealData(List<StockChartData> chartData, RealS3_K3_Data realData)
        {
            var today = StockMinuteBarStore.MarketNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var liveCandle = new StockChartData
            {
                shcode = realData.shcode,
                date = today,
                open = realData.open != 0 ? realData.open : realData.price,
                high = realData.high != 0 ? realData.high : realData.price,
                low = realData.low != 0 ? realData.low : realData.price,
                close = realData.price,
                jdiff_vol = realData.volume,
                value = realData.value,
                sign = realData.sign
            };

            var todayIndex = chartData.FindIndex(x => x.date == today);
            if (todayIndex >= 0)
            {
                chartData[todayIndex] = liveCandle;
            }
            else
            {
                chartData.Add(liveCandle);
            }
        }
    }
}
