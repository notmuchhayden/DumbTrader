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

        // 카드 뷰모델 컬렉션 (DashboardView에서 바인딩)
        public ObservableCollection<StockCardViewModel> StockCards { get; } = new ObservableCollection<StockCardViewModel>();

        public DashboardViewModel(StrategyService strategyService,
            DumbTraderDbContext dbContext, StockRealDataService stockRealDataService)
        {
            _strategyService = strategyService;
            _dbContext = dbContext;
            _stockRealDataService = stockRealDataService;

            // StockDetailViewModel과 동일하게 서비스 컬렉션 인스턴스 공유
            Watchlist = _strategyService.StrategyStocks;

            _stockRealDataService.RealDataUpdated += OnRealDataUpdated;

            LoadStockCards();
        }

        /// <summary>
        /// Watchlist의 각 종목에 대해 StockCardViewModel을 생성하고,
        /// DB에서 최신 실시간 데이터를 조회하여 초기값을 설정합니다.
        /// </summary>
        private void LoadStockCards()
        {
            StockCards.Clear();

            foreach (var item in Watchlist)
            {
                var card = new StockCardViewModel
                {
                    hname = item.Stock.hname,
                    shcode = item.Stock.shcode
                };

                // DB에서 해당 종목의 최신 실시간 데이터 조회
                var latestData = _dbContext.RealS3K3Data
                    .Where(r => r.shcode == item.Stock.shcode)
                    .OrderByDescending(r => r.chetime)
                    .FirstOrDefault();

                if (latestData != null)
                {
                    card.UpdateFromRealData(latestData);
                }

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
            }
        }
    }
}
