using System.Collections.ObjectModel;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;

namespace DumbTrader.ViewModels
{
    public class DashboardViewModel : ViewModelBase
    {
        private readonly StrategyService _strategyService;
        private readonly DumbTraderDbContext _dbContext;
        private readonly StockRealDataService _stockRealDataService;

        // 관심 종목 리스트
        public ObservableCollection<StrategyStockInfo> Watchlist { get; }

        public DashboardViewModel(StrategyService strategyService,
            DumbTraderDbContext dbContext, StockRealDataService stockRealDataService)
        {
            _strategyService = strategyService;
            _dbContext = dbContext;
            _stockRealDataService = stockRealDataService;

            // StockDetailViewModel과 동일하게 서비스 컬렉션 인스턴스 공유
            Watchlist = _strategyService.StrategyStocks;

            _stockRealDataService.RealDataUpdated += OnRealDataUpdated;
        }

        private void OnRealDataUpdated(object? sender, RealS3_K3_Data e)
        {
            // 실시간 데이터 업데이트 처리
        }
    }
}
