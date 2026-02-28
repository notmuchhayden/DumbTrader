namespace DumbTrader.Models
{
    // t8410OutBlock1 에서 나오는 연속 데이터
    public class StockChartData
    {
        public string shcode { get; set; } = string.Empty; // 종목코드
        public string date { get; set; } = string.Empty; // 날짜
        public long open { get; set; } = 0; // 시가
        public long high { get; set; } = 0; // 고가
        public long low { get; set; } = 0; // 저가
        public long close { get; set; } = 0; // 종가
        public long jdiff_vol { get; set; } = 0; // 거래량
        public long value { get; set; } = 0; // 거래대금
        public long jongchk { get; set; } = 0; // 수정구분
        public float rate { get; set; } = 0.0f; // 수정비율
        public long pricechk { get; set; } = 0; // 수정주가반영항목
        public long ratevalue { get; set; } = 0; // 수정비율반영거래대금
        public string sign { get; set; } = string.Empty; // 종가등락구분 (1:상한 2:상승 3:보합)
    }

    // t8410OutBlock 에서 나오는 데이터
    public class StockChartDataInfo
    {
        public string shcode { get; set; } = string.Empty; // 종목코드
        public int jisiga { get; set; } = 0; // 전일시가
        public int jihigh { get; set; } = 0; // 전일고가
        public int jilow { get; set; } = 0; // 전일저가
        public int jiclose { get; set; } = 0; // 전일종가
        public long jivolume { get; set; } = 0; // 전일거래량
        public int disiga { get; set; } = 0; // 당일시가
        public int dihigh { get; set; } = 0; // 당일고가
        public int dilow { get; set; } = 0; // 당일저가
        public int diclose { get; set; } = 0; // 당일종가
        public int highend { get; set; } = 0; // 상한가
        public int lowend { get; set; } = 0; // 하한가
        public string cts_date { get; set; } = string.Empty; // 연속일자
        public string s_time { get; set; } = string.Empty; // 장시작시간(HHMMSS)
        public string e_time { get; set; } = string.Empty; // 장종료시간(HHMMSS)
        public string dshmin { get; set; } = string.Empty; // 동시호가처리시간(MM:분)
        public int rec_count { get; set; } = 0; // 레코드카운트
        public int svi_uplmtprice { get; set; } = 0; // 정적VI상한가
        public int svi_dnlmtprice { get; set; } = 0; // 정적VI하한가
    }
}
