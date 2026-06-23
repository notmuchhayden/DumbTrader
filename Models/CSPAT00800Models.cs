namespace DumbTrader.Models
{
    // CSPAT00800: 현물취소주문
    public sealed class CSPAT00800InBlock1
    {
        public long OrgOrdNo { get; set; }
        public string AcntNo { get; set; } = string.Empty;
        public string InptPwd { get; set; } = string.Empty;
        // IsuNo : 종목번호
        //  - 주식/ETF : 종목코드 or A+종목코드(모의투자는 A+종목코드)
        //  - ELW : J+종목코드
        //  - ETN : Q+종목코드
        public string IsuNo { get; set; } = string.Empty;
        public long OrdQty { get; set; }
    }

    public sealed class CSPAT00800OutBlock1
    {
        public long RecCnt { get; set; }
        public long OrgOrdNo { get; set; }
        public string AcntNo { get; set; } = string.Empty;
        public string InptPwd { get; set; } = string.Empty;
        public string IsuNo { get; set; } = string.Empty;
        public long OrdQty { get; set; }
        public string CommdaCode { get; set; } = string.Empty;
        public string GrpId { get; set; } = string.Empty;
        public string StrtgCode { get; set; } = string.Empty;
        public long OrdSeqNo { get; set; }
        public long PtflNo { get; set; }
        public long BskNo { get; set; }
        public long TrchNo { get; set; }
        public long ItemNo { get; set; }
    }

    public sealed class CSPAT00800OutBlock2
    {
        public long RecCnt { get; set; }
        public long OrdNo { get; set; }
        public long PrntOrdNo { get; set; }
        public string OrdTime { get; set; } = string.Empty;
        public string OrdMktCode { get; set; } = string.Empty;
        public string OrdPtnCode { get; set; } = string.Empty;
        public string ShtnIsuNo { get; set; } = string.Empty;
        public string PrgmOrdprcPtnCode { get; set; } = string.Empty;
        public string StslOrdprcTpCode { get; set; } = string.Empty;
        public string StslAbleYn { get; set; } = string.Empty;
        public string MgntrnCode { get; set; } = string.Empty;
        public string LoanDt { get; set; } = string.Empty;
        public string CvrgOrdTp { get; set; } = string.Empty;
        public string LpYn { get; set; } = string.Empty;
        public string MgempNo { get; set; } = string.Empty;
        public string BnsTpCode { get; set; } = string.Empty;
        public long SpareOrdNo { get; set; }
        public long CvrgSeqno { get; set; }
        public long RsvOrdNo { get; set; }
        public string AcntNm { get; set; } = string.Empty;
        public string IsuNm { get; set; } = string.Empty;
    }
}
