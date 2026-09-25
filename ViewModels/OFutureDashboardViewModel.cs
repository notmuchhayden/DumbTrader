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
    public class OFutureDashboardViewModel : ViewModelBase
    {
        private readonly StrategyService _strategyService;
        private readonly DumbTraderDbContext _dbContext;
        private readonly StockRealDataService _stockRealDataService;
        private readonly LoggingService _loggingService;
        private bool _isReceivingRealData = false;

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
                    _latestSelectedRealData = null;
                    LoadSelectedChartData();
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

        public OFutureDashboardViewModel(StrategyService strategyService,
            DumbTraderDbContext dbContext,
            StockRealDataService stockRealDataService,
            LoggingService loggingService)
        {
            _strategyService = strategyService;
            _dbContext = dbContext;
            _stockRealDataService = stockRealDataService;
            _loggingService = loggingService;

            Watchlist = _strategyService.StrategyStocks;

            _stockRealDataService.RealDataUpdated += OnRealDataUpdated;

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

                if (_stockRealDataService.Subscriptions.ContainsKey(item.Stock.shcode))
                    continue;

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
            _stockRealDataService.UnsubscribeAll();
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
            // 해당 종목의 카드를 찾아서 업데이트
            var card = StockCards.FirstOrDefault(c => c.shcode == e.shcode);
            if (card != null)
            {
                card.UpdateFromRealData(e);

                _loggingService.Log($"[실시간] {e.shcode} 현재가: {e.price:N0} 등락율: {e.drate:+0.00;-0.00}% 체결량: {e.cvolume:N0}");

                // 해외선물 전용 실시간 데이터/전략 서비스가 연결되기 전까지
                // 주식 실시간 데이터로 기존 주식 전략을 실행하지 않는다.
            }

            if (SelectedStockCard?.shcode == e.shcode)
            {
                _latestSelectedRealData = e;
                UpdatePlot();
            }
        }

        private void SelectStock(object? parameter)
        {
            if (parameter is StockCardViewModel card)
            {
                SelectedStockCard = card;
            }
        }

        private void UpdatePlot()
        {
            var plot = PlotControl.Plot;
            plot.Clear();
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

            var latestRealData = _latestSelectedRealData?.shcode == SelectedStockCard.shcode
                ? _latestSelectedRealData
                : GetLatestRealData(SelectedStockCard.shcode);

            if (latestRealData != null)
            {
                ApplyLatestRealData(chartData, latestRealData);
            }

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
                if (!DateTime.TryParseExact(chartData[i].date, "yyyyMMdd", CultureInfo.InvariantCulture,
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

            plot.Axes.DateTimeTicksBottom();
            if (plot.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.DateTimeAutomatic ticks)
            {
                ticks.LabelFormatter = date =>
                {
                    var index = (int)Math.Round(date.Subtract(_chartBaseDate).TotalDays);
                    return index >= 0 && index < _chartDates.Count
                        ? _chartDates[index].ToString("yy/MM/dd", CultureInfo.InvariantCulture)
                        : string.Empty;
                };
            }

            _dataMinOADate = ohlcs.First().DateTime.ToOADate();
            _dataMaxOADate = ohlcs.Last().DateTime.ToOADate();
            var firstVisible = Math.Max(_dataMinOADate - 2, _dataMaxOADate - 60);
            plot.Axes.SetLimitsX(firstVisible, _dataMaxOADate + 2);

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

                var minPrice = visibleCandles.Min(x => x.Low);
                var maxPrice = visibleCandles.Max(x => x.High);
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

            var startDate = DateTime.Today.AddYears(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
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

        private RealS3_K3_Data? GetLatestRealData(string shcode)
        {
            var localData = _dbContext.RealS3K3Data.Local
                .Where(x => x.shcode == shcode)
                .OrderByDescending(x => x.chetime)
                .FirstOrDefault();

            return localData ?? _dbContext.RealS3K3Data
                .AsNoTracking()
                .Where(x => x.shcode == shcode)
                .OrderByDescending(x => x.chetime)
                .FirstOrDefault();
        }

        private static void ApplyLatestRealData(List<StockChartData> chartData, RealS3_K3_Data realData)
        {
            var today = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
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
