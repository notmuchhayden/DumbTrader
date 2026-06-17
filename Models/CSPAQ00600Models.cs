namespace DumbTrader.Models
{
    // CSPAQ00600: 계좌별신용한도조회
    // In/Out 블록을 그대로 옮긴 데이터 모델
    public sealed class CSPAQ00600InBlock1
    {
        public long RecCnt { get; set; }
        public string AcntNo { get; set; } = string.Empty;
        public string InptPwd { get; set; } = string.Empty;
        public string LoanDtlClssCode { get; set; } = string.Empty;
        public string IsuNo { get; set; } = string.Empty;
        public double OrdPrc { get; set; }
        public string CommdaCode { get; set; } = string.Empty;
    }

    public sealed class CSPAQ00600OutBlock1
    {
        public long RecCnt { get; set; }
        public string AcntNo { get; set; } = string.Empty;
        public string InptPwd { get; set; } = string.Empty;
        public string LoanDtlClssCode { get; set; } = string.Empty;
        public string IsuNo { get; set; } = string.Empty;
        public double OrdPrc { get; set; }
        public string CommdaCode { get; set; } = string.Empty;
    }

    public sealed class CSPAQ00600OutBlock2
    {
        public long RecCnt { get; set; }
        public string AcntNm { get; set; } = string.Empty;
        public double OrdPrc { get; set; }
        public long SloanLmtAmt { get; set; }
        public long SloanAmtSum { get; set; }
        public long SloanNewAmt { get; set; }
        public long SloanRfundAmt { get; set; }
        public long MktcplMloanLmtAmt { get; set; }
        public long MktcplMloanAmtSum { get; set; }
        public long MktcplMloanNewAmt { get; set; }
        public long MktcplMloanRfundAmt { get; set; }
        public long SfaccMloanLmtAmt { get; set; }
        public long SfaccMloanAmtSum { get; set; }
        public long SfaccMloanNewAmt { get; set; }
        public long SfaccMloanRfundAmt { get; set; }
        public long BrnMktcplMloanLmtAmt { get; set; }
        public long BrnMktcplMloanNewAmt { get; set; }
        public long BrnMktcplMloanRfundAmt { get; set; }
        public long BrnMktcplMloanUseAmt { get; set; }
        public long BrnSfaccMloanLmtAmt { get; set; }
        public long BrnSfaccMloanNewAmt { get; set; }
        public long BrnSfaccMloanRfundAmt { get; set; }
        public long BrnSfaccMloanUseAmt { get; set; }
        public string FirmMloanLmtMgmtYn { get; set; } = string.Empty;
        public string FirmCrdtIsuRestrcTp { get; set; } = string.Empty;
        public double PldgMaintRat { get; set; }
        public string FirmNm { get; set; } = string.Empty;
        public double PldgRat { get; set; }
        public long DpsastSum { get; set; }
        public long LmtChgAbleAmt { get; set; }
        public long OrdAbleAmt { get; set; }
        public long OrdAbleQty { get; set; }
        public long RcvblUablOrdAbleQty { get; set; }
    }
}
