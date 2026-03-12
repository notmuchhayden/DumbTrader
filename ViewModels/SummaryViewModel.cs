using DumbTrader.Core;
using DumbTrader.Services;
using System.ComponentModel;

namespace DumbTrader.ViewModels
{
    public class SummaryViewModel : ViewModelBase
    {
        private readonly AccountService? _accountService;

        // 계좌 번호
        private string _accountNumber = "00000000000";
        public string AccountNumber
        {
            get => _accountNumber;
            set => SetProperty(ref _accountNumber, value);
        }

        // 계좌 이름
        private string _accountName = "Hello";
        public string AccountName
        {
            get => _accountName;
            set => SetProperty(ref _accountName, value);
        }

        // 계좌 상세
        private string _accountDetailName = "World";
        public string AccountDetailName
        {
            get => _accountDetailName;
            set => SetProperty(ref _accountDetailName, value);
        }

        // 예수금
        private string _dps = "0000";
        public string Dps
        {
            get => _dps;
            set => SetProperty(ref _dps, value);
        }

        // 손익율
        private string _pnlRat = "0.00%";
        public string PnlRat
        {
            get => _pnlRat;
            set => SetProperty(ref _pnlRat, value);
        }

        // 투자원금
        private string _invstOrgAmt = "0000";
        public string InvstOrgAmt
        {
            get => _invstOrgAmt;
            set => SetProperty(ref _invstOrgAmt, value);
        }

        // 투자손익금액
        private string _invstPlAmt = "0000";
        public string InvstPlAmt
        {
            get => _invstPlAmt;
            set => SetProperty(ref _invstPlAmt, value);
        }

        public SummaryViewModel(AccountService accountService)
        {
            _accountService = accountService;

            if (_accountService.CurrentAccount == null)
            {
                // CurrentAccount가 null인 경우 기본값 설정
                AccountNumber = "00000000000";
                AccountName = "Hello";
                AccountDetailName = "World";
            }
            else
            {
                AccountNumber = _accountService.CurrentAccount.AccountNumber;
                AccountName = _accountService.CurrentAccount.AccountName;
                AccountDetailName = _accountService.CurrentAccount.AccountDetailName;
            }

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
