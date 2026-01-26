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
        private ObservableCollection<StockInfo> _watchlist;
        public ObservableCollection<StockInfo> Watchlist
        {
            get => _watchlist;
            set => SetProperty(ref _watchlist, value);
        }

        // QueryChartDataCommand
        public ICommand QueryChartDataCommand { get; }

        public StockDetailViewModel(StockDataService stockDataService, StrategyService strategyService)
        {
            _stockDataService = stockDataService;
            _strategyService = strategyService;

            QueryChartDataCommand = new RelayCommand(ExecuteQueryChartData);
        }

        private void ExecuteQueryChartData(object? parameter)
        {
            // TODO : 개별 종목 차트 데이터 조회 로직 구현
            _stockDataService.RequestStockChartData();
        }
    }
}
