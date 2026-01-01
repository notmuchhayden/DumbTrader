using System.Collections.ObjectModel;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;
using System.Linq;

namespace DumbTrader.ViewModels
{
    public class AccountViewModel : ViewModelBase
    {
        private readonly IXASessionService _sessionService;
        private readonly AccountService _accountService;
        private ObservableCollection<AccountInfo> _accounts;
        public ObservableCollection<AccountInfo> Accounts
        {
            get => _accounts;
            set => SetProperty(ref _accounts, value);
        }

        public ICommand QueryAccountsCommand { get; }

        public AccountViewModel(IXASessionService sessionService, AccountService accountService)
        {
            _sessionService = sessionService;
            _accountService = accountService;
            _accounts = new ObservableCollection<AccountInfo>(_accountService.GetAccounts());
            QueryAccountsCommand = new RelayCommand(QueryAccounts);
        }

        private void QueryAccounts(object? parameter)
        {
            Accounts = new ObservableCollection<AccountInfo>(_accountService.GetAccounts());
        }
    }
}
