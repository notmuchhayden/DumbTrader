using System.Collections.ObjectModel;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;

namespace DumbTrader.ViewModels
{
    public class AccountViewModel : ViewModelBase
    {
        private readonly IXingSessionService _sessionService;
        public ObservableCollection<AccountInfo> Accounts { get; }

        public ICommand QueryAccountsCommand { get; }

        public AccountViewModel(IXingSessionService sessionService)
        {
            _sessionService = sessionService;
            Accounts = new ObservableCollection<AccountInfo>();
            QueryAccountsCommand = new RelayCommand(QueryAccounts);
        }

        private void QueryAccounts(object? parameter)
        {
            Accounts.Clear();
            int count = _sessionService.GetAccountListCount();
            for (int i = 0; i < count; i++)
            {
                var accountNumber = _sessionService.GetAccountList(i);
                var accountName = _sessionService.GetAccountName(accountNumber);
                Accounts.Add(new AccountInfo
                {
                    AccountNumber = accountNumber,
                    AccountName = accountName
                });
            }
        }
    }
}
