using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DumbTrader.Models
{
    // t1101 주식 현재가 호가 조회
    public class StockCurrentAskBidPriceData
    {
        public string hname; // 종목명
        public int price; // 현재가
        public string sign;//전일대비구분
        public int change; // 전일대비
        public float diff; // 등락율
        public long volume; // 누적거래량
        public int jnilclose; // 전일종가(기준가)

        public int[] offerho10 = new int[10]; // 매도호가1~10
        public int[] bidho10 = new int[10]; // 매수호가1~10
        public long[] offerrem10 = new long[10]; // 매도호가수량1~10
        public long[] bidrem10 = new long[10]; // 매수호가수량1~10
        public long[] preoffercha10 = new long[10]; // 직전매도대비수량1~10
        public long[] prebidcha10 = new long[10]; // 직전매수대비수량1~10

        public long offer; // 매도호가수량합
        public long bid; // 매수호가수량합
        public long preoffercha; // 직전매도대비수량합
        public long prebidcha; // 직전매수대비수량합
        public string hotime; // 수신시간
        public int yeprice; // 예상체결가격
        public long yevolume; //예상체결수량
        public string yesign; // 예상체결전일구분
        public int yechange; // 예상체결전일대비
        public float yediff; // 예상체결등락율
        public long tmoffer; // 시간외매도잔량
        public long tmbid; // 시간외매수잔량
        public string ho_status; // 동시구분
        public string shcode; // 단축코드
        public int uplmtprice; // 상한가
        public int dnlmtprice; // 하한가
        public int open; // 시가
        public int high; // 고가
        public int low; // 저가
        public int krx_midprice; // KRX중간가격
        public int krx_offermidsumrem; //  KRX매도중간가잔량합계수량
        public int krx_bidmidsumrem; // KRX매수중간가잔량합계수량
        public int krx_midsumrem; // KRX중간가잔량합계수량
        public string krx_midsumremgubun; // KRX중간가잔량구분
    }
}
