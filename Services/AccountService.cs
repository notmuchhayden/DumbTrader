// This file/class was renamed from AccountStoreService to AccountService
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services
{
    public class AccountService
    {
        private readonly DumbTraderDbContext _dbContext;
        private readonly LoggingService _loggingService;

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
                    if (_currentAccount != null)
                    {
                        CurrentAccountUpdated?.Invoke(this, _currentAccount); // 계좌 정보가 업데이트되었음을 알리는 이벤트 발생
                    }
                }
            }
        }

        public event EventHandler<AccountInfo>? CurrentAccountUpdated;
        public event EventHandler<AccountCSPAQ12300OutBlock2>? AccountDetailInfoUpdated; // (CSPAQ12300)

        public AccountService(DumbTraderDbContext dbContext, LoggingService loggingService)
        {
            _dbContext = dbContext;
            _loggingService = loggingService;

            _xaQueryServices = new Dictionary<string, IXAQueryService>();

            // 주식 계좌 기간별 수익률 상세 조회 TR 등록
            var CSPAQ12300 = new XAQueryService();
            CSPAQ12300.LoadFromResFile("Res\\CSPAQ12300.res");
            CSPAQ12300.AddReceiveDataEventHandler(CSPAQ12300ReceiveData);
            _xaQueryServices.Add("CSPAQ12300", CSPAQ12300);

            // 애플리케이션 시작 시 설정 파일에서 계좌 정보 로드
            LoadConfig();
        }

        //tcode로 IXAQueryService 가져오기
        public IXAQueryService? GetQueryService(string tcode)
        {
            _xaQueryServices.TryGetValue(tcode, out var service);
            return service;
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
        public bool RequestStockAccountInfo(string accountNumber)
        {
            var CSPAQ12300 = GetQueryService("CSPAQ12300");
            if (CSPAQ12300 == null)
            {
                _loggingService.Log("CSPAQ12300 TR 서비스가 등록되어 있지 않습니다.");
                return false;
            }
            // CSPAQ12300 TR의 입력값 설정
            CSPAQ12300.ClearBlockdata("CSPAQ12300InBlock1");
            CSPAQ12300.SetFieldData("CSPAQ12300InBlock1", "AcntNo", 0, accountNumber);
            
            int result = CSPAQ12300.Request(false);
            if (result < 0)
            {
                return false;
            }
            return true;
        }

        private void CSPAQ12300ReceiveData(string trcode)
        {
            var CSPAQ12300 = GetQueryService("CSPAQ12300");
            if (CSPAQ12300 == null)
                return;

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
}
