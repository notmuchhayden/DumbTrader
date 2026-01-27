using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;
using System.Runtime.CompilerServices;

namespace DumbTrader.ViewModels
{
    public class WatchlistViewModel : ViewModelBase
    {
        // 서버, DB 에 접근하는 서비스들
        private readonly StockDataService _stockDataService;
        private readonly StrategyService _strategyService;

        // 관심 종목 리스트
        private ObservableCollection<StockInfo> _watchlist;
        public ObservableCollection<StockInfo> Watchlist
        {
            get => _watchlist;
            set => SetProperty(ref _watchlist, value);
        }

        // 전체 종목 리스트
        private ObservableCollection<StockInfo> _stocks;
        public ObservableCollection<StockInfo> Stocks
        {
            get => _stocks;
            set => SetProperty(ref _stocks, value);
        }

        // 검색어
        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        // 전체 종목 리스트에서 선택된 종목
        private StockInfo? _selectedStock;
        public StockInfo? SelectedStock
        {
            get => _selectedStock;
            set => SetProperty(ref _selectedStock, value);
        }

        // 선택된 관심종목
        private StockInfo? _selectedWatchlist;
        public StockInfo? SelectedWatchlist
        {
            get => _selectedWatchlist;
            set => SetProperty(ref _selectedWatchlist, value);
        }

        public ICommand QueryStockListCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand SelectStockCommand { get; }
        public ICommand RemoveWatchlistCommand { get; }

        public WatchlistViewModel(StockDataService stockDataService, StrategyService strategyService)
        {
            _stockDataService = stockDataService;
            _strategyService = strategyService;

            // 종목 데이터 서비스의 이벤트 구독
            _stockDataService.StockListUpdated += OnStockDataServicePropertyChanged;
            // 초기 데이터 로드
            _stocks = new ObservableCollection<StockInfo>(_stockDataService.GetStockList());
            // 관심 종목 초기화
            _watchlist = new ObservableCollection<StockInfo>(_strategyService.StrategyStocks.Select(s => s.Stock));

            // 명령어 초기화
            QueryStockListCommand = new RelayCommand(ExecuteQueryStockList);
            SearchCommand = new RelayCommand(ExecuteSearch);
            SelectStockCommand = new RelayCommand(ExecuteSelectStock);
            RemoveWatchlistCommand = new RelayCommand(ExecuteRemoveWatchlist);
        }

        private void ExecuteRemoveWatchlist(object? parameter)
        {
            if (SelectedWatchlist != null)
            {
                // DataGrid 에서 값이 삭제 되면 자동으로 SelectedWatchlist 가 null 이 되므로
                // 먼저 StrategyService 에서 삭제 처리 후 Watchlist 컬렉션에서 제거
                _strategyService.RemoveStock(SelectedWatchlist);
                Watchlist.Remove(SelectedWatchlist);
            }
        }

        private void ExecuteSelectStock(object? parameter)
        {
            if (SelectedStock != null && !Watchlist.Any(s => s.shcode == SelectedStock.shcode))
            {
                _strategyService.AddStock(SelectedStock);
                Watchlist.Add(SelectedStock);
            }
        }

        private void ExecuteSearch(object? parameter)
        {
            // SearchText 프로퍼티 사용
            string searchText = SearchText;
            if (!string.IsNullOrEmpty(searchText))
            {
                var filteredStocks = _stockDataService.GetStockList()
                    .Where(stock => stock.hname.Contains(searchText, StringComparison.OrdinalIgnoreCase));
                Stocks = new ObservableCollection<StockInfo>(filteredStocks);
            }
            else
            {
                Stocks = new ObservableCollection<StockInfo>(_stockDataService.GetStockList());
            }
        }

        private void ExecuteQueryStockList(object? parameter)
        {
            bool success = _stockDataService.RequestStockList();
            if (!success)
            {
                // TODO: 요청 실패 처리
            }
        }

        private void OnStockDataServicePropertyChanged(object? sender, EventArgs e)
        {
            Stocks = new ObservableCollection<StockInfo>(_stockDataService.GetStockList());
        }
    }
}
