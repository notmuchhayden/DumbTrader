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

        // 관심 종목 리스트
        private ObservableCollection<StrategyStockInfo> _watchlist;
        public ObservableCollection<StrategyStockInfo> Watchlist
        {
            get => _watchlist;
            set => SetProperty(ref _watchlist, value);
        }

        // 선택된 관심종목
        private StrategyStockInfo? _selectedWatchlist;
        public StrategyStockInfo? SelectedWatchlist
        {
            get => _selectedWatchlist;
            set => SetProperty(ref _selectedWatchlist, value);
        }

        

        // QueryChartDataCommand
        public ICommand QueryChartDataCommand { get; }

        public StockDetailViewModel(StockDataService stockDataService, StrategyService strategyService)
        {
            _stockDataService = stockDataService;
            _strategyService = strategyService;

            // ViewModel 이 생성될 때 관심 종목 불러오기
            _watchlist = new ObservableCollection<StrategyStockInfo>(_strategyService.StrategyStocks);

            QueryChartDataCommand = new RelayCommand(ExecuteQueryChartData);
        }

        private void ExecuteQueryChartData(object? parameter)
        {
            // TODO : 개별 종목 차트 데이터 조회 로직 구현
            //_stockDataService.RequestStockChartData();
        }
    }
}
