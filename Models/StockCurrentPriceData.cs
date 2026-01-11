using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace DumbTrader.Models
{
    // 주식 현재가 (t8407) 조회
    public class StockCurrentPriceData
    {
        public string shcode; // 종목코드
        public string hname; // 종목명
        public int price; //현재가
        public string sign; // 전일대비구분
        public int change; // 전일대비
        public float diff; // 등락율
        public long volume; // 누적거래량
        public int offerho; // 매도호가
        public int bidho; // 매수호가
        public int cvolume; // 체결수량
        public float chdegree; // 체결강도
        public int open; // 시가
        public int high; // 고가
        public int low; // 저가
        public long value; // 거래대금(백만)
        public long offerrem; // 우선매도잔량
        public long bidrem; // 우선매수잔량
        public long totofferrem; // 총매도잔량
        public long totbidrem; // 총매수잔량
        public int jnilclose; // 전일종가
        public int uplmtprice; // 상한가
        public int dnlmtprice; // 하한가
    }
}
