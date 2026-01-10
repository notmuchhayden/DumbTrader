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
        // TODO 
    }
}
