namespace DumbTrader.Models
{
    // CSPAT00600: 현물주문
    // TR의 input/output block 구조를 그대로 반영한 주문 모델
    public sealed class CSPAT00600InBlock1
    {
        public string AcntNo { get; set; } = string.Empty;
        public string InptPwd { get; set; } = string.Empty;
        // IsuNo : 종목번호
        //  - 주식/ETF : 종목코드 or A+종목코드(모의투자는 A+종목코드)        //  - ELW : J+종목코드        //  - ETN : Q+종목코드
        public string IsuNo { get; set; } = string.Empty;
        public long OrdQty { get; set; }
        public double OrdPrc { get; set; }
        // BnsTpCode : 매매구분
        //  - 1:매도
        //  - 2:매수
        public string BnsTpCode { get; set; } = string.Empty;
        // OrdprcPtnCode : 주문가격유형
        //  - 00 : 지정가        //  - 03 : 시장가        //  - 05 : 조건부지정가        //  - 06 : 최유리지정가        //  - 07 : 최우선지정가        //  - 12 : 중간가        //  - 61 : 장개시전시간외종가        //  - 81 : 시간외종가        //  - 82 : 시간외단일가
        public string OrdprcPtnCode { get; set; } = string.Empty;
        // PrgmOrdprcPtnCode : 프로그램지정가유형
        //  - 000:보통
        //  - 003:유통/자기융자신규
        //  - 005:유통대주신규
        //  - 007:자기대주신규
        //  - 101:유통융자상환
        //  - 103:자기융자상환
        //  - 105:유통대주상환
        //  - 107:자기대주상환
        //  - 180:예탁담보대출상환(신용)
        public string MgntrnCode { get; set; } = string.Empty;
        public string LoanDt { get; set; } = string.Empty;
        // OrdCndiTpCode : 주문조건유형
        //  - 0:없음
        //  - 1:IOC
        //  - 2:FOK
        public string OrdCndiTpCode { get; set; } = string.Empty;
        // MbrNo : 회원번호 (공통적으로 입력하는 필드지만, 실제로는 사용되지 않는 경우가 많음)
        //  - KRX: KRX
        //  - NXT: NXT
        //  - 그외 입력값은 KRX로 처리
        public string MbrNo { get; set; } = string.Empty;
    }

    public sealed class CSPAT00600OutBlock1
    {
        public long RecCnt { get; set; }
        public string AcntNo { get; set; } = string.Empty;
        public string InptPwd { get; set; } = string.Empty;
        public string IsuNo { get; set; } = string.Empty;
        public long OrdQty { get; set; }
        public double OrdPrc { get; set; }
        public string BnsTpCode { get; set; } = string.Empty;
        public string OrdprcPtnCode { get; set; } = string.Empty;
        public string PrgmOrdprcPtnCode { get; set; } = string.Empty;
        public string StslAbleYn { get; set; } = string.Empty;
        public string StslOrdprcTpCode { get; set; } = string.Empty;
        public string CommdaCode { get; set; } = string.Empty;
        public string MgntrnCode { get; set; } = string.Empty;
        public string LoanDt { get; set; } = string.Empty;
        public string MbrNo { get; set; } = string.Empty;
        public string OrdCndiTpCode { get; set; } = string.Empty;
        public string StrtgCode { get; set; } = string.Empty;
        public string GrpId { get; set; } = string.Empty;
        public long OrdSeqNo { get; set; }
        public long PtflNo { get; set; }
        public long BskNo { get; set; }
        public long TrchNo { get; set; }
        public long ItemNo { get; set; }
        public string OpDrtnNo { get; set; } = string.Empty;
        public string LpYn { get; set; } = string.Empty;
        public string CvrgTpCode { get; set; } = string.Empty;
    }

    public sealed class CSPAT00600OutBlock2
    {
        public long RecCnt { get; set; }
        public long OrdNo { get; set; }
        public string OrdTime { get; set; } = string.Empty;
        public string OrdMktCode { get; set; } = string.Empty;
        public string OrdPtnCode { get; set; } = string.Empty;
        public string ShtnIsuNo { get; set; } = string.Empty;
        public string MgempNo { get; set; } = string.Empty;
        public long OrdAmt { get; set; }
        public long SpareOrdNo { get; set; }
        public long CvrgSeqno { get; set; }
        public long RsvOrdNo { get; set; }
        public long SpotOrdQty { get; set; }
        public long RuseOrdQty { get; set; }
        public long MnyOrdAmt { get; set; }
        public long SubstOrdAmt { get; set; }
        public long RuseOrdAmt { get; set; }
        public string AcntNm { get; set; } = string.Empty;
        public string IsuNm { get; set; } = string.Empty;
    }
}
