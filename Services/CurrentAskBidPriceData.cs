using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DumbTrader.Services
{
    // t1101 개별종목 현재가
    public class CurrentAskBidPriceData
    {
        string hname; // 종목명
        int price; // 현재가
        string sign;//전일대비구분
        int change; // 전일대비
        float diff; // 등락율
        int volume; // 누적거래량
        int jnilclose; // 전일종가(기준가)

        int[] offerho10 = new int[10]; // 매도호가1~10
        int[] bidho10 = new int[10]; // 매수호가1~10
        int[] offerrem10 = new int[10]; // 매도호가수량1~10
        int[] bidrem10 = new int[10]; // 매수호가수량1~10
        int[] preoffercha10 = new int[10]; // 직전매도대비수량1~10
        int[] prebidcha10 = new int[10]; // 직전매수대비수량1~10

        int offer; // 매도호가수량합
        int bid; // 매수호가수량합
        int preoffercha; // 직전매도대비수량합
        int prebidcha; // 직전매수대비수량합
        string hotime; // 수신시간
        int yeprice; // 예상체결가격
		int yevolume; //예상체결수량
        string yesign; // 예상체결전일구분
        int yechange; // 예상체결전일대비
        float yediff; // 예상체결등락율
        int tmoffer; // 시간외매도잔량
        int tmbid; // 시간외매수잔량
        string ho_status; // 동시구분
        string shcode; // 단축코드
        int uplmtprice; // 상한가
        int dnlmtprice; // 하한가
        int open; // 시가
        int high; // 고가
        int low; // 저가
        int krx_midprice; // KRX중간가격
        int krx_offermidsumrem; //  KRX매도중간가잔량합계수량
        int krx_bidmidsumrem; // KRX매수중간가잔량합계수량
        int krx_midsumrem; // KRX중간가잔량합계수량
        string krx_midsumremgubun; // KRX중간가잔량구분
    }
}
