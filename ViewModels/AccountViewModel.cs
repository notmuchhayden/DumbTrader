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
            var newAccounts = new ObservableCollection<AccountInfo>();
            int count = _sessionService.GetAccountListCount();
            var dbAccounts = _accountService.GetAccounts();
            var dbAccountNumbers = dbAccounts.Select(a => a.AccountNumber).ToHashSet();

            for (int i = 0; i < count; i++)
            {
                var accountNumber = _sessionService.GetAccountList(i);
                var accountName = _sessionService.GetAccountName(accountNumber);
                var accountDetailName = _sessionService.GetAcctDetailName(accountNumber);
                var accountNickname = _sessionService.GetAcctNickname(accountNumber);
                var accountInfo = new AccountInfo
                {
                    AccountNumber = accountNumber,
                    AccountName = accountName,
                    AccountDetailName = accountDetailName,
                    AccountNickname = accountNickname
                };
                newAccounts.Add(accountInfo);

                // DB에 없으면 저장
                if (!dbAccountNumbers.Contains(accountNumber))
                {
                    _accountService.AddAccount(accountInfo);
                }
            }
            Accounts = newAccounts;
        }
    }
}
