using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;
using System;

namespace DumbTrader.ViewModels
{
    public class SummaryViewModel : ViewModelBase
    {
        private readonly OFutureBalanceService _futureBalances;
        private readonly AccountService _accountService;
        private string _stockReturnText = "기간 수익률 조회를 설정하세요.";
        public string StockReturnText
        {
            get => _stockReturnText;
            private set => SetProperty(ref _stockReturnText, value);
        }

        public DateTime ReturnEnd { get; } = DateTime.UtcNow.AddHours(9).Date;
        public DateTime ReturnStart => ReturnEnd.AddDays(1 - ReturnEnd.Day);

        public void ConfigureStockReturn(DateTime? start, DateTime? end, string password)
        {
            if (start is null || end is null || end.Value.Date > DateTime.UtcNow.AddHours(9).Date
                || !_accountService.ConfigureStockPeriodReturn(start.Value, end.Value, password))
            {
                StockReturnText = "계좌, 조회 기간과 비밀번호를 확인하세요.";
                return;
            }
            StockReturnText = "기간 수익률 · 다음 주문 이벤트 대기";
        }
        private IReadOnlyList<OFutureBalanceRow> _futureRows = Array.Empty<OFutureBalanceRow>();
        private string _futureStatus = "해외선물 계좌를 조회하세요.";
        private bool _isFutureLoading;

        public IReadOnlyList<OFutureBalanceRow> FutureRows
        {
            get => _futureRows;
            private set => SetProperty(ref _futureRows, value);
        }

        public string FutureStatus
        {
            get => _futureStatus;
            private set => SetProperty(ref _futureStatus, value);
        }

        public async Task RefreshFutureBalanceAsync(string type, string account, string password)
        {
            if (_isFutureLoading) return;
            if (type.Trim().Length != 1 || string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
            {
                FutureStatus = "계좌 구분, 계좌번호, 비밀번호를 입력하세요.";
                return;
            }

            _isFutureLoading = true;
            FutureRows = Array.Empty<OFutureBalanceRow>();
            FutureStatus = "해외선물 잔고 조회 중";
            try
            {
                var result = await _futureBalances.QueryAsync(new(type.Trim(), account.Trim(), password));
                if (result.Success) FutureRows = result.Rows;
                FutureStatus = result.Success
                    ? result.Rows.Count == 0 ? "조회된 해외선물 잔고가 없습니다." : string.Empty
                    : $"해외선물 잔고 조회 실패 · {result.Message}";
            }
            finally { _isFutureLoading = false; }
        }

        // 계좌 번호
        private string _accountNumber = "—";
        public string AccountNumber
        {
            get => _accountNumber;
            set => SetProperty(ref _accountNumber, value);
        }

        // 계좌 이름
        private string _accountName = "";
        public string AccountName
        {
            get => _accountName;
            set => SetProperty(ref _accountName, value);
        }

        // 계좌 상세
        private string _accountDetailName = "";
        public string AccountDetailName
        {
            get => _accountDetailName;
            set => SetProperty(ref _accountDetailName, value);
        }

        // 예수금
        private long? _dps;
        public long? Dps
        {
            get => _dps;
            set => SetProperty(ref _dps, value);
        }

        // 손익율
        private float? _pnlRat;
        public float? PnlRat
        {
            get => _pnlRat;
            set => SetProperty(ref _pnlRat, value);
        }

        // 투자원금
        private long? _invstOrgAmt;
        public long? InvstOrgAmt
        {
            get => _invstOrgAmt;
            set => SetProperty(ref _invstOrgAmt, value);
        }

        // 투자손익금액
        private long? _invstPlAmt;
        public long? InvstPlAmt
        {
            get => _invstPlAmt;
            set => SetProperty(ref _invstPlAmt, value);
        }

        public SummaryViewModel(AccountService accountService, OFutureBalanceService futureBalances)
        {
            _accountService = accountService;
            _futureBalances = futureBalances;
            accountService.CurrentAccountUpdated += OnCurrentAccountUpdated;
            accountService.StockSummaryUpdated += OnAccountDetailInfoUpdated;
            accountService.StockPeriodReturnStatus += (_, status) => StockReturnText = status;
            accountService.StockPeriodReturnUpdated += (_, result) => StockReturnText =
                $"기간 수익률 {result.Rate:N2}% · 투자손익 {result.Profit:N0}원 · {result.Start:yyyy-MM-dd} ~ {result.End:yyyy-MM-dd}";
            if (accountService.CurrentAccount is { } account) OnCurrentAccountUpdated(accountService, account);
        }

        // Account 정보 변경 시 AccountNumber 속성 업데이트
        private void OnCurrentAccountUpdated(object? sender, AccountInfo e)
        {
            AccountNumber = e.AccountNumber;
            AccountName = e.AccountName;
            AccountDetailName = e.AccountDetailName;
            Dps = null;
            PnlRat = null;
            InvstOrgAmt = null;
            InvstPlAmt = null;
            StockReturnText = "기간 수익률 조회를 설정하세요.";
        }

        // 계좌 상세 정보 변경 시 관련 속성 업데이트
        private void OnAccountDetailInfoUpdated(object? sender, AccountCSPAQ12300OutBlock2 e)
        {
            Dps = e.Dps;
            PnlRat = (float)e.PnlRat;
            InvstOrgAmt = e.InvstOrgAmt;
            InvstPlAmt = e.InvstPlAmt;
        }
    }
}
