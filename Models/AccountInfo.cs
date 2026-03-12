using System.ComponentModel.DataAnnotations;

namespace DumbTrader.Models
{
    // 계좌정보 모델
    public class AccountInfo
    {
        [Key]
        public string AccountNumber { get; set; } = string.Empty; // 계좌번호. Primary Key
        // Account name (e.g., institution or account type)
        public string AccountName { get; set; } = string.Empty;
        // Detailed account name (e.g., specific account description)
        public string AccountDetailName { get; set; } = string.Empty;
        // Account nickname (user-defined or system-assigned)
        public string AccountNickname { get; set; } = string.Empty;
    }

    // 계좌 상세 정보 모델 (CSPAQ12300 TR의 출력 블록 2)
    public class AccountCSPAQ12300OutBlock2
    {
        public long RecCnt { get; set; } // 레코드갯수
		public string BrnNm { get; set; } = string.Empty; // 지점명
		public string AcntNm { get; set; } = string.Empty; // 계좌명
		public long MnyOrdAbleAmt { get; set; } // 현금주문가능금액
		public long MnyoutAbleAmt { get; set; } // 출금가능금액
		public long SeOrdAbleAmt { get; set; } // 거래소금액
		public long KdqOrdAbleAmt { get; set; } // 코스닥금액
		public long HtsOrdAbleAmt { get; set; } // HTS주문가능금액
		public long MgnRat100pctOrdAbleAmt { get; set; } // 증거금률100퍼센트주문가능금액
		public long BalEvalAmt { get; set; } // 잔고평가금액
		public long PchsAmt { get; set; } // 매입금액
		public long RcvblAmt { get; set; } // 미수금액
		public double PnlRat { get; set; } // 손익율
		public long InvstOrgAmt { get; set; } // 투자원금
		public long InvstPlAmt { get; set; } // 투자손익금액
		public long CrdtPldgOrdAmt { get; set; } // 신용담보주문금액
		public long Dps { get; set; } // 예수금
		public long D1Dps { get; set; } // D1예수금
		public long D2Dps { get; set; } // D2예수금
		public string OrdDt { get; set; } = string.Empty; // 주문일
		public long MnyMgn { get; set; } // 현금증거금액
		public long SubstMgn { get; set; } // 대용증거금액
		public long SubstAmt { get; set; } // 대용금액
		public long PrdayBuyExecAmt { get; set; } // 전일매수체결금액
		public long PrdaySellExecAmt { get; set; } // 전일매도체결금액
		public long CrdayBuyExecAmt { get; set; } // 금일매수체결금액
		public long CrdaySellExecAmt { get; set; } // 금일매도체결금액
		public long EvalPnlSum { get; set; } // 평가손익합계
		public long DpsastTotamt { get; set; } // 예탁자산총액
		public long Evrprc { get; set; } // 제비용
		public long RuseAmt { get; set; } // 재사용금액
		public long EtclndAmt { get; set; } // 기타대여금액
		public long PrcAdjstAmt { get; set; } // 가정산금액
		public long D1CmsnAmt { get; set; } // D1수수료
		public long D2CmsnAmt { get; set; } // D2수수료
		public long D1EvrTax { get; set; } // D1제세금
		public long D2EvrTax { get; set; } // D2제세금
		public long D1SettPrergAmt { get; set; } // D1결제예정금액
		public long D2SettPrergAmt { get; set; } // D2결제예정금액
		public long PrdayKseMnyMgn { get; set; } // 전일KSE현금증거금
		public long PrdayKseSubstMgn { get; set; } // 전일KSE대용증거금
		public long PrdayKseCrdtMnyMgn { get; set; } // 전일KSE신용현금증거금
		public long PrdayKseCrdtSubstMgn { get; set; } // 전일KSE신용대용증거금
		public long CrdayKseMnyMgn { get; set; } // 금일KSE현금증거금
		public long CrdayKseSubstMgn { get; set; } // 금일KSE대용증거금
		public long CrdayKseCrdtMnyMgn { get; set; } // 금일KSE신용현금증거금
		public long CrdayKseCrdtSubstMgn { get; set; } // 금일KSE신용대용증거금
		public long PrdayKdqMnyMgn { get; set; } // 전일코스닥현금증거금
		public long PrdayKdqSubstMgn { get; set; } // 전일코스닥대용증거금
		public long PrdayKdqCrdtMnyMgn { get; set; } // 전일코스닥신용현금증거금
		public long PrdayKdqCrdtSubstMgn { get; set; } // 전일코스닥신용대용증거금
		public long CrdayKdqMnyMgn { get; set; } // 금일코스닥현금증거금
		public long CrdayKdqSubstMgn { get; set; } // 금일코스닥대용증거금
		public long CrdayKdqCrdtMnyMgn { get; set; } // 금일코스닥신용현금증거금
		public long CrdayKdqCrdtSubstMgn { get; set; } // 금일코스닥신용대용증거금
		public long PrdayFrbrdMnyMgn { get; set; } // 전일프리보드현금증거금
		public long PrdayFrbrdSubstMgn { get; set; } // 전일프리보드대용증거금
		public long CrdayFrbrdMnyMgn { get; set; } // 금일프리보드현금증거금
		public long CrdayFrbrdSubstMgn { get; set; } // 금일프리보드대용증거금
		public long PrdayCrbmkMnyMgn { get; set; } // 전일장외현금증거금
		public long PrdayCrbmkSubstMgn { get; set; } // 전일장외대용증거금
		public long CrdayCrbmkMnyMgn { get; set; } // 금일장외현금증거금
		public long CrdayCrbmkSubstMgn { get; set; } // 금일장외대용증거금
		public long DpspdgQty { get; set; } // 예탁담보수량
		public long BuyAdjstAmtD2 { get; set; } // 매수정산금(D+2)
		public long SellAdjstAmtD2 { get; set; } // 매도정산금(D+2)
		public long RepayRqrdAmtD1 { get; set; } // 변제소요금(D+1)
		public long RepayRqrdAmtD2 { get; set; } // 변제소요금(D+2)
		public long LoanAmt { get; set; } // 대출금액
    }

    
}
