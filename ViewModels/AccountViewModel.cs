using System.Collections.ObjectModel;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;

namespace DumbTrader.ViewModels
{
    public class AccountViewModel : ViewModelBase
    {
        private readonly IXASessionService _sessionService;
        public ObservableCollection<AccountInfo> Accounts { get; }

        public ICommand QueryAccountsCommand { get; }

        public AccountViewModel(IXASessionService sessionService)
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
                var accountDetailName = _sessionService.GetAcctDetailName(accountNumber);
                var accountNickname = _sessionService.GetAcctNickname(accountNumber);
                Accounts.Add(new AccountInfo
                {
                    AccountNumber = accountNumber,
                    AccountName = accountName,
                    AccountDetailName = accountDetailName,
                    AccountNickname = accountNickname
                });
            }
        }
    }
}
