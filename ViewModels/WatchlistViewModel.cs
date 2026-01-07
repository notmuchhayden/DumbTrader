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

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        private StockInfo? _selectedStock;
        public StockInfo? SelectedStock
        {
            get => _selectedStock;
            set => SetProperty(ref _selectedStock, value);
        }

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
            _watchlist = new ObservableCollection<StockInfo>(_strategyService.StrategyStocks.Select(s => s.Stock));
            MapStockGubun();

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
                Watchlist.Remove(SelectedWatchlist);
                _strategyService.RemoveStock(SelectedWatchlist);
            }
        }

        private void ExecuteSelectStock(object? parameter)
        {
            if (SelectedStock != null && !Watchlist.Any(s => s.shcode == SelectedStock.shcode))
            {
                Watchlist.Add(SelectedStock);
                _strategyService.AddStock(SelectedStock);
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
            MapStockGubun();
        }

        private void MapStockGubun()
        {
            // Stocks.gubun 이 1 이면 코스피, 2 면 코스닥 으로 변경
            foreach (var stock in _stocks)
            {
                if (stock.gubun == "1")
                {
                    stock.gubun = "코스피";
                }
                else if (stock.gubun == "2")
                {
                    stock.gubun = "코스닥";
                }
                else
                {
                    stock.gubun = "기타";
                }
            }
        }
    }
}
