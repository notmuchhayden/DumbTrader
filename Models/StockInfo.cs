using System.ComponentModel.DataAnnotations;

namespace DumbTrader.Models
{
    // 주식 종목 정보 모델 (XingAPI t8430) 과 일치
    public class StockInfo
    {
        // 단축코드
        [Key]
        public string shcode { get; set; } = string.Empty;
        // 종목명
        public string hname { get; set; } = string.Empty;
        // 확장코드
        public string expcode { get; set; } = string.Empty;
        // ETF 구분
        public string etfgubun { get; set; } = string.Empty;
        // 구분
        public string gubun { get; set; } = string.Empty;
    }
}
