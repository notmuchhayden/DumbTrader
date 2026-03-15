using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
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
        private readonly LoggingService _loggingService;
        private bool _isReceivingRealData = false;

        // 관심 종목 리스트
        public ObservableCollection<StrategyStockInfo> Watchlist { get; }

        // 카드 뷰모델 컬렉션 (DashboardView에서 바인딩)
        public ObservableCollection<StockCardViewModel> StockCards { get; } = new ObservableCollection<StockCardViewModel>();

        // 종목 선택 명령 (예: 상세보기로 이동)
        public ICommand SelectStockCommand { get; }
        // 실시간 데이터 수신 제어 명령
        public ICommand StartReceivingCommand { get; }
        // 실시간 데이터 수신 중지 명령
        public ICommand StopReceivingCommand { get; }

        public DashboardViewModel(StrategyService strategyService,
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

            InitStockCards();
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

                // TODO : 전략 실행
                _strategyService.Run(e, false, 0);
            }
        }
    }
}
