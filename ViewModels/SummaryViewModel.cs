using DumbTrader.Core;
using DumbTrader.Services;
using System.ComponentModel;

namespace DumbTrader.ViewModels
{
    public class SummaryViewModel : ViewModelBase
    {
        private readonly AccountService _accountService;

        private string _accountNumber = "00000000000";
        public string AccountNumber
        {
            get => _accountNumber;
            set => SetProperty(ref _accountNumber, value);
        }

        private string _accountName = "HelloWorld";
        public string AccountName
        {
            get => _accountName;
            set => SetProperty(ref _accountName, value);
        }

        private string _totalBalanceDisplay = "$10,250.00 +$325.50";
        public string TotalBalanceDisplay
        {
            get => _totalBalanceDisplay;
            set => SetProperty(ref _totalBalanceDisplay, value);
        }

        public SummaryViewModel(AccountService accountService)
        {
            _accountService = accountService;

            // AccountService.CurrentAccount 변경 감지
            _accountService.PropertyChanged += OnAccountServicePropertyChanged;
        }

        // Account 정보 변경 시 AccountNumber 속성 업데이트
        private void OnAccountServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AccountService.CurrentAccount))
            {
                AccountNumber = _accountService.CurrentAccount.AccountNumber;
                AccountName = _accountService.CurrentAccount.AccountName;
            }
        }
    }
}
