using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;
using ScottPlot;
using ScottPlot.WPF;

namespace DumbTrader.ViewModels
{
    public enum ChartQueryPeriod
    {
        OneDay,
        AWeek,
        AMonth,
        ThreeMonths,
        SixMonths,
        AYear,
        ThreeYears,
        FiveYears,
        All
    }

    public class StockDetailViewModel : ViewModelBase
    {
        // 서버, DB 에 접근하는 서비스들
        private readonly StockDataService _stockDataService;
        // 전략 서비스
        private readonly StrategyService _strategyService;
        private readonly LoggingService _loggingService;
        private readonly DumbTraderDbContext _dbContext;

        // 관심 종목 리스트
        public ObservableCollection<StrategyStockInfo> Watchlist { get; set; }

        // 선택된 관심종목
        private StrategyStockInfo? _selectedWatchlist;
        public StrategyStockInfo? SelectedWatchlist
        {
            get => _selectedWatchlist;
            set
            {
                if (SetProperty(ref _selectedWatchlist, value))
                {
                    if (value != null)
                    {
                        QueryChartData(ChartQueryPeriod.AYear);
                    }
                    else
                    {
                        ChartData = new ObservableCollection<StockChartData>();
                    }
                }
            }
        }

        // 차트 데이터. ChartData 는 대량으로 변경될 수 있으므로 SetProperty 사용
        private ObservableCollection<StockChartData> _chartData = new ObservableCollection<StockChartData>();
        public ObservableCollection<StockChartData> ChartData
        {
            get => _chartData;
            set
            {
                if (SetProperty(ref _chartData, value))
                {
                    UpdatePlot();
                }
            }
        }

        public WpfPlot PlotControl { get; } = new WpfPlot();

        // QueryChartDataCommand
        public ICommand QueryChartDataCommand { get; }

        public StockDetailViewModel(StockDataService stockDataService, StrategyService strategyService, LoggingService loggingService, DumbTraderDbContext dbContext)
        {
            _stockDataService = stockDataService;
            _strategyService = strategyService;
            _loggingService = loggingService; // keep consistent name used elsewhere
            _dbContext = dbContext;

            // 주식 차트 데이터 업데이트 이벤트 구독
            _stockDataService.StockChartDataInfoUpdated += OnStockChartDataInfoUpdated;

            // ViewModel 이 생성될 때 관심 종목 불러오기
            Watchlist = new ObservableCollection<StrategyStockInfo>(_strategyService.StrategyStocks);

            QueryChartDataCommand = new RelayCommand(ExecuteQueryChartData);
        }

        private void ExecuteQueryChartData(object? parameter)
        {
            if (SelectedWatchlist != null)
            {
                _stockDataService.RequestStockChartData(
                    SelectedWatchlist.Stock.shcode,
                    DateTime.Today.AddYears(-8),
                    DateTime.Today
                );
            }
        }

        private void OnStockChartDataInfoUpdated(object? sender, StockChartDataInfo e)
        {
            // 차트 데이터가 업데이트 되었을 때 주요 정보 로그 기록
            _loggingService.Log($"차트 데이터 업데이트: 종목코드={e.shcode}, 날짜={e.cts_date}, 종가={e.diclose}");
            QueryChartData(ChartQueryPeriod.AYear);
        }

        private void QueryChartData(ChartQueryPeriod period)
        {
            if (SelectedWatchlist == null)
            {
                ChartData = new ObservableCollection<StockChartData>();
                return;
            }

            DateTime endDate = DateTime.Today;
            DateTime startDate = endDate;
            switch (period)
            {
                case ChartQueryPeriod.OneDay:
                    startDate = endDate.AddDays(-1);
                    break;
                case ChartQueryPeriod.AWeek:
                    startDate = endDate.AddDays(-7);
                    break;
                case ChartQueryPeriod.AMonth:
                    startDate = endDate.AddMonths(-1);
                    break;
                case ChartQueryPeriod.ThreeMonths:
                    startDate = endDate.AddMonths(-3);
                    break;
                case ChartQueryPeriod.SixMonths:
                    startDate = endDate.AddMonths(-6);
                    break;
                case ChartQueryPeriod.AYear:
                    startDate = endDate.AddYears(-1);
                    break;
                case ChartQueryPeriod.ThreeYears:
                    startDate = endDate.AddYears(-3);
                    break;
                case ChartQueryPeriod.FiveYears:
                    startDate = endDate.AddYears(-5);
                    break;
                case ChartQueryPeriod.All:
                    startDate = DateTime.MinValue;
                    break;
            }

            // StockChartData.date는 string이므로, 날짜 비교를 위해 yyyyMMdd로 변환
            string startDateStr = startDate.ToString("yyyyMMdd");
            string endDateStr = endDate.ToString("yyyyMMdd");

            var data = _dbContext.StockChartDatas
                .Where(x => x.shcode == SelectedWatchlist.Stock.shcode && string.Compare(x.date, startDateStr) >=0 && string.Compare(x.date, endDateStr) <=0)
                .OrderBy(x => x.date)
                .ToList();

            ChartData = new ObservableCollection<StockChartData>(data);
        }

        private void UpdatePlot()
        {
            var data = ChartData;

            var plt = PlotControl.Plot;
            plt.Clear();

            if (data == null || data.Count == 0)
            {
                PlotControl.Refresh();
                return;
            }

            List<OHLC> ohlcs = new();
            for (int i = 0; i < data.Count; i++)
            {
                var dateTime = DateTime.ParseExact(data[i].date, "yyyyMMdd", CultureInfo.InvariantCulture);
                ohlcs.Add(new OHLC(
                    data[i].open,
                    data[i].high,
                    data[i].low,
                    data[i].close,
                    dateTime,
                    TimeSpan.FromDays(1)));
            }

            // add candlesticks
            var candlePlot = plt.Add.Candlestick(ohlcs);
            candlePlot.FallingColor = new ScottPlot.Color(0,0,255); // 파란색
            candlePlot.RisingColor = new ScottPlot.Color(255,0,0); // 빨간색
            plt.Axes.DateTimeTicksBottom();

            // 자동 Y축 스케일링 설정
            plt.Axes.ContinuouslyAutoscale = true;
            plt.Axes.ContinuousAutoscaleAction = (RenderPack rp) =>
            {
                // 현재 보이는 날짜 범위
                DateTime start = DateTime.FromOADate(plt.Axes.Bottom.Min);
                DateTime end = DateTime.FromOADate(plt.Axes.Bottom.Max);

                // 해당 범위에 속하는 데이터만 필터링
                var visibleData = ohlcs
                    .Where(d => d.DateTime >= start && d.DateTime <= end)
                    .ToList();

                int min = 0;
                int max = 0;
                foreach (var ohlc in visibleData)
                {
                    if (ohlc.Low < min || min == 0)
                        min = (int)ohlc.Low;
                    if (ohlc.High > max)
                        max = (int)ohlc.High;
                }
                min = (int)(min * 0.999);
                max = (int)(max * 1.001);
                // set vertical axis limits to that range
                rp.Plot.Axes.SetLimitsY(min, max);
            };


            // refresh view
            PlotControl.Refresh();
        }
    }
}
