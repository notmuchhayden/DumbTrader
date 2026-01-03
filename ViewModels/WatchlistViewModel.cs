using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Services;

namespace DumbTrader.ViewModels
{
    public class WatchlistViewModel : ViewModelBase
    {
        private readonly StockDataService _stockDataService;

        public ICommand QueryStockListCommand { get; }

        public WatchlistViewModel(StockDataService stockDataService)
        {
            _stockDataService = stockDataService;
            QueryStockListCommand = new RelayCommand(ExecuteQueryStockList);
        }

        private void ExecuteQueryStockList(object? parameter)
        {
            // TODO : StockDataService 에서 주식 종목 조회 기능을 구현한 후 호출
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
