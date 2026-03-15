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
        private readonly LoggingService _loggingService;

        // 계좌 목록
        private ObservableCollection<AccountInfo> _accounts;
        public ObservableCollection<AccountInfo> Accounts
        {
            get => _accounts;
            set => SetProperty(ref _accounts, value);
        }

        // 현재 선택된 계좌 정보
        private AccountInfo? _selectedAccount;
        public AccountInfo? SelectedAccount
        {
            get => _selectedAccount;
            set => SetProperty(ref _selectedAccount, value);
        }

        public ICommand QueryAccountsCommand { get; }
        public ICommand SelectAccountsCommand { get; }

        public AccountViewModel(IXASessionService sessionService,
            AccountService accountService,
            LoggingService loggingService)
        {
            _sessionService = sessionService;
            _accountService = accountService;
            _loggingService = loggingService;
            _accounts = new ObservableCollection<AccountInfo>(_accountService.GetAccounts());
            QueryAccountsCommand = new RelayCommand(QueryAccounts);
            SelectAccountsCommand = new RelayCommand(SelectAccount);
        }

        private void QueryAccounts(object? parameter)
        {
            var newAccounts = new ObservableCollection<AccountInfo>();
            int count = _sessionService.GetAccountListCount();

            _accountService.RemoveAllAccount();

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
                _accountService.AddAccount(accountInfo);
            }

            Accounts = newAccounts;
        }

        // 계좌 선택 처리
        private void SelectAccount(object? parameter)
        {
            if (parameter is AccountInfo account)
            {
                _accountService.CurrentAccount = account;
                _accountService.SaveConfig(); // 선택한 계좌 정보를 저장

                if (account.AccountDetailName == "종합매매")
                {
                    _accountService.RequestStockAccountInfo(account.AccountNumber);
                }
                else if (account.AccountDetailName == "선물옵션")
                {
                    _accountService.RequestKoreaFutureAccountInfo(account.AccountNumber);
                }
                else if (account.AccountDetailName == "해외선물")
                {
                    _accountService.RequestOverseasFutureAccountInfo(account.AccountNumber);
                }
                else
                {
                    // 기타 계좌 유형에 대한 처리 (필요 시 추가)
                    _loggingService.Log($"알 수 없는 계좌 유형: {account.AccountDetailName}");
                }
            }
        }
    }
}
