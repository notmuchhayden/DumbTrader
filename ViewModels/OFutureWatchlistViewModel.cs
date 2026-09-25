using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;

namespace DumbTrader.ViewModels
{
    public sealed class OFutureWatchlistViewModel : ViewModelBase
    {
        private readonly OFutureDataService _oFutureDataService;

        public ObservableCollection<OFutureContract> OFutureWatchlist { get; }

        private ObservableCollection<OFutureContract> _oFutureContracts;
        public ObservableCollection<OFutureContract> OFutureContracts
        {
            get => _oFutureContracts;
            private set => SetProperty(ref _oFutureContracts, value);
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        private OFutureContract? _selectedOFutureContract;
        public OFutureContract? SelectedOFutureContract
        {
            get => _selectedOFutureContract;
            set => SetProperty(ref _selectedOFutureContract, value);
        }

        private OFutureContract? _selectedWatchlistContract;
        public OFutureContract? SelectedWatchlistContract
        {
            get => _selectedWatchlistContract;
            set => SetProperty(ref _selectedWatchlistContract, value);
        }

        public ICommand QueryOFutureListCommand { get; }
        public ICommand SearchOFutureCommand { get; }
        public ICommand AddOFutureToWatchlistCommand { get; }
        public ICommand RemoveOFutureFromWatchlistCommand { get; }

        public OFutureWatchlistViewModel(OFutureDataService oFutureDataService)
        {
            _oFutureDataService = oFutureDataService;
            _oFutureDataService.ContractListUpdated += OnOFutureContractListUpdated;
            _oFutureDataService.WatchlistUpdated += OnOFutureWatchlistUpdated;

            OFutureWatchlist = new ObservableCollection<OFutureContract>(_oFutureDataService.Watchlist);
            OFutureContracts = new ObservableCollection<OFutureContract>(_oFutureDataService.Contracts);

            QueryOFutureListCommand = new RelayCommand(ExecuteQueryOFutureList);
            SearchOFutureCommand = new RelayCommand(ExecuteSearchOFuture);
            AddOFutureToWatchlistCommand = new RelayCommand(ExecuteAddOFutureToWatchlist);
            RemoveOFutureFromWatchlistCommand = new RelayCommand(ExecuteRemoveOFutureFromWatchlist);

            ExecuteQueryOFutureList(null);
        }

        private void ExecuteQueryOFutureList(object? parameter)
        {
            _oFutureDataService.RequestContractList();
        }

        private void ExecuteSearchOFuture(object? parameter)
        {
            var searchText = SearchText.Trim();
            var contracts = _oFutureDataService.Contracts;
            if (!string.IsNullOrEmpty(searchText))
            {
                contracts = contracts
                    .Where(contract => contract.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                        || contract.Symbol.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            OFutureContracts = new ObservableCollection<OFutureContract>(contracts);
        }

        private void ExecuteAddOFutureToWatchlist(object? parameter)
        {
            if (SelectedOFutureContract is not null)
                _oFutureDataService.AddToWatchlist(SelectedOFutureContract);
        }

        private void ExecuteRemoveOFutureFromWatchlist(object? parameter)
        {
            if (SelectedWatchlistContract is not null)
                _oFutureDataService.RemoveFromWatchlist(SelectedWatchlistContract.Symbol);
        }

        private void OnOFutureContractListUpdated(object? sender, EventArgs e)
        {
            OnUiThread(() => ExecuteSearchOFuture(null));
        }

        private void OnOFutureWatchlistUpdated(object? sender, EventArgs e)
        {
            OnUiThread(() =>
            {
                OFutureWatchlist.Clear();
                foreach (var contract in _oFutureDataService.Watchlist)
                    OFutureWatchlist.Add(contract);
            });
        }

        private static void OnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
                action();
            else
                _ = dispatcher.BeginInvoke(action);
        }
    }
}
