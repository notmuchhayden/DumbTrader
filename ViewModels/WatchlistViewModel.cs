using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Services;

namespace DumbTrader.ViewModels
{
    public class WatchlistViewModel : ViewModelBase
    {
        private readonly IXAQueryService _xaQueryService;

        public ICommand QueryStockListCommand { get; }

        public WatchlistViewModel(IXAQueryService xaQueryService)
        {
            _xaQueryService = xaQueryService;
            QueryStockListCommand = new RelayCommand(ExecuteQueryStockList);
        }

        private void ExecuteQueryStockList(object? parameter)
        {
            // 주식 리스트 조회
            //_xaQueryService

        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
