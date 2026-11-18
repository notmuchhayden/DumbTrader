// This file/class was renamed from AccountStoreService to AccountService
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;
using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services
{
    public class AccountService
    {
        private readonly DumbTraderDbContext _dbContext;
        private readonly LoggingService _loggingService;
        private readonly XingTrQuerySchedulerService _queryScheduler;
        private readonly IXASessionService _session;
        private readonly StockLiveOrderLedger _stockLiveOrders;
        private readonly List<IXARealService> _stockOrderFeeds = new();
        private readonly DispatcherTimer _stockRefreshTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private AccountInfo? _pendingStockRefresh;
        private AccountInfo? _stockSummaryRequest;
        private const string ReturnTr = "FOCCQ33600";
        private sealed record ReturnSettings(AccountInfo Account, DateTime Start, DateTime End, string Password);
        private ReturnSettings? _returnSettings;
        private ReturnSettings? _activeReturnRequest;
        private IXAQueryService? _returnQuery;
        public event EventHandler<StockPeriodReturn>? StockPeriodReturnUpdated;
        public event EventHandler<string>? StockPeriodReturnStatus;

        private readonly Dictionary<string, IXAQueryService> _xaQueryServices;

        // 설정 파일 경로
        private readonly string _accountPath = "account.json";

        private AccountInfo? _currentAccount; // 현재 선택된 계좌 정보를 저장하는 필드
        public AccountInfo? CurrentAccount
        {
            get => _currentAccount;
            set
            {
                if (_currentAccount != value)
                {
                    _currentAccount = value;
                    _returnSettings = null;
                    if (_currentAccount != null)
                    {
                        CurrentAccountUpdated?.Invoke(this, _currentAccount); // 계좌 정보가 업데이트되었음을 알리는 이벤트 발생
                    }
                }
            }
        }

        public event EventHandler<AccountInfo>? CurrentAccountUpdated;
        public event EventHandler<AccountCSPAQ12300OutBlock2>? AccountDetailInfoUpdated; // (CSPAQ12300)
        public event EventHandler<AccountCSPAQ12300OutBlock2>? StockSummaryUpdated;

        public AccountService(DumbTraderDbContext dbContext, LoggingService loggingService,
            XingTrQuerySchedulerService queryScheduler, IXASessionService session, StockLiveOrderLedger stockLiveOrders)
        {
            _dbContext = dbContext;
            _loggingService = loggingService;
            _queryScheduler = queryScheduler;
            _session = session;
            _stockLiveOrders = stockLiveOrders;

            _xaQueryServices = new Dictionary<string, IXAQueryService>();

            _xaQueryServices.Add("CSPAQ12300", CreateAccountQuery());

            // 애플리케이션 시작 시 설정 파일에서 계좌 정보 로드
            LoadConfig();
            _stockRefreshTimer.Tick += (_, _) =>
            {
                _stockRefreshTimer.Stop();
                var account = _pendingStockRefresh;
                _pendingStockRefresh = null;
                if (_session.IsConnected && account is not null && ReferenceEquals(CurrentAccount, account))
                {
                    RequestStockAccountInfo(account.AccountNumber, account);
                    if (_returnSettings is { } settings && ReferenceEquals(settings.Account, account))
                        RequestPeriodReturn(settings);
                }
            };
            _session.AddLoginEventHandler((code, _) =>
            {
                if (code == "0000") SubscribeStockOrderEvents();
            });
            _session.AddLogoutEventHandler(ResetStockOrderEvents);
            _session.AddDisconnectEventHandler(ResetStockOrderEvents);
            if (_session.IsConnected) SubscribeStockOrderEvents();
        }

        public bool ConfigureStockPeriodReturn(DateTime start, DateTime end, string password)
        {
            if (CurrentAccount is not { } account || start.Date > end.Date || password.Length is < 1 or > 8)
                return false;
            _returnSettings = new(account, start.Date, end.Date, password);
            return true;
        }

        private void RequestPeriodReturn(ReturnSettings settings)
        {
            try
            {
                var query = _returnQuery ??= CreatePeriodReturnQuery();
                if (!_queryScheduler.Enqueue(ReturnTr, query, () =>
                {
                    if (!ReferenceEquals(_returnSettings, settings) || !_session.IsConnected) return -1;
                    _activeReturnRequest = settings;
                    const string input = ReturnTr + "InBlock1";
                    query.ClearBlockdata(input);
                    query.SetFieldData(input, "RecCnt", 0, "1");
                    query.SetFieldData(input, "AcntNo", 0, settings.Account.AccountNumber);
                    query.SetFieldData(input, "Pwd", 0, settings.Password);
                    query.SetFieldData(input, "QrySrtDt", 0, settings.Start.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                    query.SetFieldData(input, "QryEndDt", 0, settings.End.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                    query.SetFieldData(input, "TermTp", 0, "1");
                    StockPeriodReturnStatus?.Invoke(this, "기간 수익률 조회 중");
                    return query.Request(false);
                }, resetQuery: () =>
                {
                    if (!ReferenceEquals(_returnQuery, query)) return;
                    _activeReturnRequest = null;
                    _returnQuery = null;
                    if (query is IDisposable disposable) disposable.Dispose();
                }, onRejected: _ =>
                {
                    if (ReferenceEquals(_returnSettings, settings))
                        StockPeriodReturnStatus?.Invoke(this, "기간 수익률 조회 실패 · 계좌 정보와 접속 상태를 확인하세요.");
                }))
                    StockPeriodReturnStatus?.Invoke(this, "기간 수익률 조회를 예약하지 못했습니다.");
            }
            catch (Exception)
            {
                StockPeriodReturnStatus?.Invoke(this, "기간 수익률 조회를 시작하지 못했습니다.");
            }
        }

        private IXAQueryService CreatePeriodReturnQuery()
        {
            var query = new XAQueryService();
            if (!query.LoadFromResFile($"Res\\{ReturnTr}.res"))
                throw new InvalidOperationException("FOCCQ33600 리소스 로드 실패");
            query.AddReceiveDataEventHandler(_ =>
            {
                if (!ReferenceEquals(_returnQuery, query)) return;
                try
                {
                    if (_activeReturnRequest is not { } settings || !ReferenceEquals(_returnSettings, settings)) return;
                    const string output = ReturnTr + "OutBlock2";
                    string Field(string name) => query.GetFieldData(output, name, 0).Trim();
                    if (decimal.TryParse(Field("InvstErnrat"), NumberStyles.Number, CultureInfo.InvariantCulture, out var rate)
                        && long.TryParse(Field("InvstPlAmt"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var profit))
                        StockPeriodReturnUpdated?.Invoke(this, new(settings.Start, settings.End, rate, profit));
                    else StockPeriodReturnStatus?.Invoke(this, "조회된 기간 수익률이 없습니다.");
                }
                catch (Exception) { StockPeriodReturnStatus?.Invoke(this, "기간 수익률 응답을 읽지 못했습니다."); }
                finally
                {
                    _activeReturnRequest = null;
                    _queryScheduler.Complete(ReturnTr, query);
                }
            });
            query.AddReceiveMessageEventHandler((systemError, code, _) =>
            {
                if (systemError && ReferenceEquals(_returnQuery, query)) _queryScheduler.Fail(ReturnTr, query, code);
            });
            return query;
        }

        private void SubscribeStockOrderEvents()
        {
            if (_stockOrderFeeds.Count != 0) return;
            foreach (var code in new[] { "SC0", "SC1", "SC2", "SC3", "SC4" })
            {
                try
                {
                    var feed = new XARealService();
                    if (!feed.LoadFromResFile($"Res\\{code}.res"))
                    {
                        _loggingService.Log($"주식 주문 실시간 리소스 로드 실패 ({code})");
                        continue;
                    }
                    feed.AddReceiveRealDataEventHandler(_ =>
                    {
                        var number = feed.GetFieldData("OutBlock", "accno").Trim();
                        if (CurrentAccount is not { } account || number.Length == 0
                            || !string.Equals(number.Replace("-", ""), account.AccountNumber.Trim().Replace("-", ""), StringComparison.Ordinal))
                            return;
                        RecordStockBrokerEvent(feed, code, number.Replace("-", ""));
                        _pendingStockRefresh = account;
                        _stockRefreshTimer.Start();
                    });
                    feed.AdviseRealData();
                    _stockOrderFeeds.Add(feed);
                }
                catch (Exception ex) { _loggingService.Log($"주식 주문 실시간 구독 실패 ({code}): {ex.Message}"); }
            }
        }

        private void RecordStockBrokerEvent(IXARealService feed, string code, string account)
        {
            long Number(string name) => long.TryParse(feed.GetFieldData("OutBlock", name).Trim(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
            try
            {
                var update = new StockBrokerOrderEvent(account,
                    StockMinuteBarStore.MarketNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                    code, Number("ordno"), Number("orgordno"),
                    code == "SC1" ? Number("execno") : 0,
                    code == "SC1" ? Number("execqty") : code == "SC3" ? Number("orgordcancqty") : 0,
                    code == "SC1" ? Number("execprc") : 0,
                    feed.GetFieldData("OutBlock", code == "SC0" ? "expcode" : "shtnIsuno").Trim(),
                    feed.GetFieldData("OutBlock", "bnstp").Trim());
                _ = PersistStockBrokerEventAsync(update);
            }
            catch (Exception) { _loggingService.Log($"주식 주문 이벤트를 읽지 못했습니다 ({code})."); }
        }

        private async Task PersistStockBrokerEventAsync(StockBrokerOrderEvent update)
        {
            try { await _stockLiveOrders.RecordBrokerEventAsync(update).ConfigureAwait(false); }
            catch (Exception) { _loggingService.Log($"주식 주문 이벤트 저장 실패 ({update.Code})."); }
        }

        private void ResetStockOrderEvents()
        {
            _stockRefreshTimer.Stop();
            _pendingStockRefresh = null;
            _stockSummaryRequest = null;
            _returnSettings = null;
            _activeReturnRequest = null;
            StockPeriodReturnStatus?.Invoke(this, "연결 종료 · 기간 수익률 조회를 다시 설정하세요.");
            foreach (var feed in _stockOrderFeeds)
            {
                try { feed.UnadviseRealData(); }
                catch (Exception ex) { _loggingService.Log($"주식 주문 실시간 해제 실패: {ex.Message}"); }
            }
            _stockOrderFeeds.Clear();
        }

        //tcode로 IXAQueryService 가져오기
        public IXAQueryService? GetQueryService(string tcode)
        {
            _xaQueryServices.TryGetValue(tcode, out var service);
            return service;
        }

        private IXAQueryService CreateAccountQuery()
        {
            var query = new XAQueryService();
            if (!query.LoadFromResFile("Res\\CSPAQ12300.res"))
                throw new InvalidOperationException("CSPAQ12300 TR 리소스 로드 실패");
            query.AddReceiveDataEventHandler(trCode =>
            {
                if (!ReferenceEquals(GetQueryService("CSPAQ12300"), query)) return;
                try { CSPAQ12300ReceiveData(trCode); }
                finally { _queryScheduler.Complete("CSPAQ12300", query); }
            });
            query.AddReceiveMessageEventHandler((bool isSystemError, string messageCode, string message) =>
            {
                if (isSystemError && ReferenceEquals(GetQueryService("CSPAQ12300"), query))
                    _queryScheduler.Fail("CSPAQ12300", query, $"{messageCode}: {message}");
            });
            return query;
        }

        private void ReplaceAccountQuery(IXAQueryService expectedQuery)
        {
            if (!ReferenceEquals(GetQueryService("CSPAQ12300"), expectedQuery)) return;
            _xaQueryServices["CSPAQ12300"] = CreateAccountQuery();
            if (expectedQuery is IDisposable disposable) disposable.Dispose();
        }

        public void SaveConfig()
        {
            try
            {
                if (CurrentAccount != null)
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string jsonString = JsonSerializer.Serialize(CurrentAccount, options);
                    File.WriteAllText(_accountPath, jsonString);
                }
            }
            catch
            {
                // 저장 중 오류 발생 시 예외 처리 (필요시 로깅)
                _loggingService.Log("계정 정보 저장중 오류 발생.");
            }
        }

        public void LoadConfig()
        {
            try
            {
                if (File.Exists(_accountPath))
                {
                    string jsonString = File.ReadAllText(_accountPath);
                    CurrentAccount = JsonSerializer.Deserialize<AccountInfo>(jsonString);
                }
            }
            catch
            {
                // 로드 중 오류 발생 시 예외 처리 (필요시 로깅)
                _loggingService.Log("계정 정보 로드중 오류 발생.");
            }
        }

        public void AddAccount(AccountInfo account)
        {
            _dbContext.Accounts.Add(account);
            _dbContext.SaveChanges();
        }

        public List<AccountInfo> GetAccounts()
        {
            return _dbContext.Accounts.AsNoTracking().ToList();
        }

        public void RemoveAllAccount()
        {
            _dbContext.Accounts.RemoveRange(_dbContext.Accounts);
            _dbContext.SaveChanges();
        }

        // 계좌 상세 정보 요청
        public bool RequestStockAccountInfo(string accountNumber) => RequestStockAccountInfo(accountNumber, null);

        private bool RequestStockAccountInfo(string accountNumber, AccountInfo? summaryAccount)
        {
            var CSPAQ12300 = GetQueryService("CSPAQ12300");
            if (CSPAQ12300 == null)
            {
                _loggingService.Log("CSPAQ12300 TR 서비스가 등록되어 있지 않습니다.");
                return false;
            }
            return _queryScheduler.Enqueue("CSPAQ12300", CSPAQ12300, () =>
            {
                if (summaryAccount is not null && (!_session.IsConnected || !ReferenceEquals(CurrentAccount, summaryAccount)))
                    return -1;
                _stockSummaryRequest = summaryAccount;
                CSPAQ12300.ClearBlockdata("CSPAQ12300InBlock1");
                CSPAQ12300.SetFieldData("CSPAQ12300InBlock1", "AcntNo", 0, accountNumber);
                return CSPAQ12300.Request(false);
            }, resetQuery: () => ReplaceAccountQuery(CSPAQ12300),
                onRejected: reason => _loggingService.Log($"CSPAQ12300 요청 실패: {reason}"));
        }

        private void CSPAQ12300ReceiveData(string trcode)
        {
            var CSPAQ12300 = GetQueryService("CSPAQ12300");
            if (CSPAQ12300 == null)
                return;

            var summaryAccount = _stockSummaryRequest;
            _stockSummaryRequest = null;
            AccountCSPAQ12300OutBlock2 accountDetailInfo = new AccountCSPAQ12300OutBlock2
            {
                RecCnt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "RecCnt", 0)),
                BrnNm = CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "BrnNm", 0),
                AcntNm = CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "AcntNm", 0),
                MnyOrdAbleAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "MnyOrdAbleAmt", 0)),
                MnyoutAbleAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "MnyoutAbleAmt", 0)),
                SeOrdAbleAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "SeOrdAbleAmt", 0)),
                KdqOrdAbleAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "KdqOrdAbleAmt", 0)),
                HtsOrdAbleAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "HtsOrdAbleAmt", 0)),
                MgnRat100pctOrdAbleAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "MgnRat100pctOrdAbleAmt", 0)),
                BalEvalAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "BalEvalAmt", 0)),
                PchsAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PchsAmt", 0)),
                RcvblAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "RcvblAmt", 0)),
                PnlRat = double.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PnlRat", 0)),
                InvstOrgAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "InvstOrgAmt", 0)),
                InvstPlAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "InvstPlAmt", 0)),
                CrdtPldgOrdAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "CrdtPldgOrdAmt", 0)),
                Dps = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "Dps", 0)),
                D1Dps = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "D1Dps", 0)),
                D2Dps = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "D2Dps", 0)),
                OrdDt = CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "OrdDt", 0),
                MnyMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "MnyMgn", 0)),
                SubstMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "SubstMgn", 0)),
                SubstAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "SubstAmt", 0)),
                PrdayBuyExecAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PrdayBuyExecAmt", 0)),
                PrdaySellExecAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PrdaySellExecAmt", 0)),
                CrdayBuyExecAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "CrdayBuyExecAmt", 0)),
                CrdaySellExecAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "CrdaySellExecAmt", 0)),
                EvalPnlSum = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "EvalPnlSum", 0)),
                DpsastTotamt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "DpsastTotamt", 0)),
                Evrprc = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "Evrprc", 0)),
                RuseAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "RuseAmt", 0)),
                EtclndAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "EtclndAmt", 0)),
                PrcAdjstAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PrcAdjstAmt", 0)),
                D1CmsnAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "D1CmsnAmt", 0)),
                D2CmsnAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "D2CmsnAmt", 0)),
                D1EvrTax = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "D1EvrTax", 0)),
                D2EvrTax = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "D2EvrTax", 0)),
                D1SettPrergAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "D1SettPrergAmt", 0)),
                D2SettPrergAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "D2SettPrergAmt", 0)),
                PrdayKseMnyMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PrdayKseMnyMgn", 0)),
                PrdayKseSubstMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PrdayKseSubstMgn", 0)),
                PrdayKseCrdtMnyMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PrdayKseCrdtMnyMgn", 0)),
                PrdayKseCrdtSubstMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PrdayKseCrdtSubstMgn", 0)),
                CrdayKseMnyMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "CrdayKseMnyMgn", 0)),
                CrdayKseSubstMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "CrdayKseSubstMgn", 0)),
                CrdayKseCrdtMnyMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "CrdayKseCrdtMnyMgn", 0)),
                CrdayKseCrdtSubstMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "CrdayKseCrdtSubstMgn", 0)),
                PrdayKdqMnyMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PrdayKdqMnyMgn", 0)),
                PrdayKdqSubstMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PrdayKdqSubstMgn", 0)),
                PrdayKdqCrdtMnyMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PrdayKdqCrdtMnyMgn", 0)),
                PrdayKdqCrdtSubstMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PrdayKdqCrdtSubstMgn", 0)),
                CrdayKdqMnyMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "CrdayKdqMnyMgn", 0)),
                CrdayKdqSubstMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "CrdayKdqSubstMgn", 0)),
                CrdayKdqCrdtMnyMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "CrdayKdqCrdtMnyMgn", 0)),
                CrdayKdqCrdtSubstMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "CrdayKdqCrdtSubstMgn", 0)),
                PrdayFrbrdMnyMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PrdayFrbrdMnyMgn", 0)),
                PrdayFrbrdSubstMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PrdayFrbrdSubstMgn", 0)),
                CrdayFrbrdMnyMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "CrdayFrbrdMnyMgn", 0)),
                CrdayFrbrdSubstMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "CrdayFrbrdSubstMgn", 0)),
                PrdayCrbmkMnyMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PrdayCrbmkMnyMgn", 0)),
                PrdayCrbmkSubstMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "PrdayCrbmkSubstMgn", 0)),
                CrdayCrbmkMnyMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "CrdayCrbmkMnyMgn", 0)),
                CrdayCrbmkSubstMgn = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "CrdayCrbmkSubstMgn", 0)),
                DpspdgQty = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "DpspdgQty", 0)),
                BuyAdjstAmtD2 = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "BuyAdjstAmtD2", 0)),
                SellAdjstAmtD2 = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "SellAdjstAmtD2", 0)),
                RepayRqrdAmtD1 = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "RepayRqrdAmtD1", 0)),
                RepayRqrdAmtD2 = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "RepayRqrdAmtD2", 0)),
                LoanAmt = long.Parse(CSPAQ12300.GetFieldData("CSPAQ12300OutBlock2", "LoanAmt", 0))
            };

            AccountDetailInfoUpdated?.Invoke(this, accountDetailInfo);
            if (summaryAccount is not null && ReferenceEquals(CurrentAccount, summaryAccount) && _session.IsConnected)
                StockSummaryUpdated?.Invoke(this, accountDetailInfo);
        }

        // 한국 선물/옵션 계좌 상세 정보 요청 (예시, 실제 TR과 필드명은 다를 수 있음)
        public bool RequestKoreaFutureAccountInfo(string accountNumber)
        {
            return true;
        }

        // 해외 선물/옵션 계좌 상세 정보 요청 (예시, 실제 TR과 필드명은 다를 수 있음)
        public bool RequestOverseasFutureAccountInfo(string accountNumber)
        {
            return true;
        }
    }

    public sealed record StockPeriodReturn(DateTime Start, DateTime End, decimal Rate, long Profit);
}
