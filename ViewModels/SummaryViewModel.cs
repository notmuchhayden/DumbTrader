using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;
using System;
using System.ComponentModel;
using System.Windows.Threading;

namespace DumbTrader.ViewModels
{
    public class SummaryViewModel : ViewModelBase
    {
        private readonly AccountService? _accountService;
        private readonly LoggingService? _loggingService;
        private readonly DispatcherTimer _timer;

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
        private long _dps = 0;
        public long Dps
        {
            get => _dps;
            set => SetProperty(ref _dps, value);
        }

        // 손익율
        private float _pnlRat = 0.0f;
        public float PnlRat
        {
            get => _pnlRat;
            set => SetProperty(ref _pnlRat, value);
        }

        // 투자원금
        private long _invstOrgAmt = 0;
        public long InvstOrgAmt
        {
            get => _invstOrgAmt;
            set => SetProperty(ref _invstOrgAmt, value);
        }

        // 투자손익금액
        private long _invstPlAmt = 0;
        public long InvstPlAmt
        {
            get => _invstPlAmt;
            set => SetProperty(ref _invstPlAmt, value);
        }

        public SummaryViewModel(AccountService accountService, LoggingService loggingService)
        {
            _accountService = accountService;
            _loggingService = loggingService;

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
            _accountService.CurrentAccountUpdated += OnCurrentAccountUpdated;
            _accountService.AccountDetailInfoUpdated += OnAccountDetailInfoUpdated;

            // 10분에 한 번씩 계좌 정보 갱신
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(10)
            };
            _timer.Tick += RequestAccountInfoPeriodically;
            _timer.Start();

            // 처음 시작 시 한 번 호출
            RequestAccountInfo();
        }

        private void RequestAccountInfoPeriodically(object? sender, EventArgs e)
        {
            RequestAccountInfo();
        }

        private void RequestAccountInfo()
        {
            if (_accountService?.CurrentAccount != null)
            {
                _accountService.RequestStockAccountInfo(_accountService.CurrentAccount.AccountNumber);
                _loggingService?.Log($"Requested account info for: {_accountService.CurrentAccount.AccountNumber}");
            }
        }

        // Account 정보 변경 시 AccountNumber 속성 업데이트
        private void OnCurrentAccountUpdated(object? sender, AccountInfo e)
        {
            AccountNumber = e.AccountNumber;
            AccountName = e.AccountName;
            AccountDetailName = e.AccountDetailName;
            _loggingService?.Log($"Current account updated: {AccountNumber} - {AccountName} - {AccountDetailName}");
        }

        // 계좌 상세 정보 변경 시 관련 속성 업데이트
        private void OnAccountDetailInfoUpdated(object? sender, AccountCSPAQ12300OutBlock2 e)
        {
            Dps = e.Dps;
            PnlRat = (float)e.PnlRat;
            InvstOrgAmt = e.InvstOrgAmt;
            InvstPlAmt = e.InvstPlAmt;
            _loggingService?.Log($"Account detail info updated: Dps={Dps}, PnlRat={PnlRat}, InvstOrgAmt={InvstOrgAmt}, InvstPlAmt={InvstPlAmt}");
        }
    }
}
