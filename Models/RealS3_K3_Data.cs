namespace DumbTrader.Models
{
    // 실시간 체결 데이터 - S3_ (KOSPI), K3_ (KOSDAQ) 공통 필드
    public class RealS3_K3_Data
    {
        public string chetime { get; set; } = string.Empty; // 체결시간
        public string sign { get; set; } = string.Empty; // 전일대비구분
        public long change { get; set; } = 0; // 전일대비
        public float drate { get; set; } = 0.0f; // 등락율
        public long price { get; set; } = 0; // 현재가
        public string opentime { get; set; } = string.Empty; // 시가시간
        public long open { get; set; } = 0; // 시가
        public string hightime { get; set; } = string.Empty; // 고가시간
        public long high { get; set; } = 0; // 고가
        public string lowtime { get; set; } = string.Empty; // 저가시간
        public long low { get; set; } = 0; // 저가
        public string cgubun { get; set; } = string.Empty; // 체결구분
        public long cvolume { get; set; } = 0; // 체결량
        public long volume { get; set; } = 0; // 누적거래량
        public long value { get; set; } = 0; // 누적거래대금
        public long mdvolume { get; set; } = 0; // 매도누적체결량
        public long mdchecnt { get; set; } = 0; // 매도누적체결건수
        public long msvolume { get; set; } = 0; // 매수누적체결량
        public long mschecnt { get; set; } = 0; // 매수누적체결건수
        public float cpower { get; set; } = 0.0f; // 체결강도
        public long w_avrg { get; set; } = 0; // 가중평균가
        public long offerho { get; set; } = 0; // 매도호가
        public long bidho { get; set; } = 0; // 매수호가
        public string status { get; set; } = string.Empty; // 장정보
        public long jnilvolume { get; set; } = 0; // 전일동시간대거래량
        public string shcode { get; set; } = string.Empty; // 종목코드
        public string exchname { get; set; } = string.Empty; // 거래소명
    }
}
