using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;

namespace DumbTrader.ViewModels
{
    public class WatchlistViewModel : ViewModelBase
    {
        private readonly StockDataService _stockDataService;

        public ObservableCollection<StockInfo> _stocks;
        public ObservableCollection<StockInfo> Stocks
        {
            get => _stocks;
            set => SetProperty(ref _stocks, value);
        }

        public ICommand QueryStockListCommand { get; }

        public WatchlistViewModel(StockDataService stockDataService)
        {
            _stockDataService = stockDataService;
            _stockDataService.StockListUpdated += OnStockDataServicePropertyChanged;
            _stocks = new ObservableCollection<StockInfo>(_stockDataService.GetStockList());
            MapStockGubun();
            QueryStockListCommand = new RelayCommand(ExecuteQueryStockList);
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
