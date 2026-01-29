using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;

namespace DumbTrader.ViewModels
{
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
                        // 최신100개 데이터 조회 (날짜 내림차순)
                        var data = _dbContext.StockChartDatas
                            .Where(x => x.shcode == value.Stock.shcode)
                            .OrderByDescending(x => x.date)
                            .Take(100)
                            .ToList();

                        // 날짜 오름차순으로 정렬하여 차트에 표시
                        ChartData = new ObservableCollection<StockChartData>(
                            data.OrderBy(x => x.date)
                        );
                    }
                    else
                    {
                        ChartData = new ObservableCollection<StockChartData>();
                    }
                }
            }
        }

        // 차트 데이터. ChartData 는 대량으로 변경될 수 있으므로 SetProperty 사용
        private ObservableCollection<StockChartData> _chartData;
        public ObservableCollection<StockChartData> ChartData
        {
            get => _chartData;
            set => SetProperty(ref _chartData, value);
        }

        // QueryChartDataCommand
        public ICommand QueryChartDataCommand { get; }

        public StockDetailViewModel(StockDataService stockDataService, StrategyService strategyService, LoggingService loggingService, DumbTraderDbContext dbContext)
        {
            _stockDataService = stockDataService;
            _strategyService = strategyService;
            _loggingService = loggingService;
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
        }
    }
}
