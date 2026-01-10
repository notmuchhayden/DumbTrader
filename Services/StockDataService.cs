using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Threading.Tasks;
using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services
{
    /// <summary>
    /// 증권(주식) 데이터 요청을 담당하는 서비스. IXAQueryService를 래핑하여 더 직관적인 API 제공
    /// </summary>
    public class StockDataService
    {
        private readonly Dictionary<string, IXAQueryService> _xaQueryServices;
        private readonly DumbTraderDbContext _dbContext;

        private StockCurrentAskBidPriceData _currentAskBidPriceData;
        public StockCurrentAskBidPriceData CurrentAskBidPriceData
        {
            get { return _currentAskBidPriceData; }
        }

        // 종목 리스트 업데이트 알림용 속성
        public event EventHandler? StockListUpdated;
        public event EventHandler<StockCurrentAskBidPriceData>? CurrentAskBidPriceDataUpdated;

        public StockDataService(DumbTraderDbContext dbContext)
        {
            _dbContext = dbContext;

            // t8430 초기화
            _xaQueryServices = new Dictionary<string, IXAQueryService>();

            // 주식 현재가 호가 조회
            var t1101 = new XAQueryService();
            //t1101.ResFileName = "Res\\t1101.res";
            t1101.LoadFromResFile("Res\\t1101.res");
            t1101.AddReceiveDataEventHandler(t1101ReceiveData);
            _xaQueryServices.Add("t1101", t1101);

            // 주식차트(년월일)
            var t8410 = new XAQueryService();
            t8410.LoadFromResFile("Res\\t8410.res");
            t8410.AddReceiveDataEventHandler(t8410ReceiveData);
            _xaQueryServices.Add("t8410", t8410);

            // 주식종목조회
            var t8430 = new XAQueryService();
            //t8430.ResFileName = "Res\\t8430.res";
            t8430.LoadFromResFile("Res\\t8430.res");
            t8430.AddReceiveDataEventHandler(t8430ReceiveData);
            _xaQueryServices.Add("t8430", t8430);
        }

        //tcode로 IXAQueryService 가져오기
        public IXAQueryService? GetQueryService(string tcode)
        {
            _xaQueryServices.TryGetValue(tcode, out var service);
            return service;
        }

        // 주식 현재가 호가 조회
        public bool RequestCurrentAskBidPrice(string shcode)
        {
            var t1101 = GetQueryService("t1101");
            if (t1101 == null)
                return false;
            t1101.ClearBlockdata("t1101InBlock");
            t1101.SetFieldData("t1101InBlock", "shcode", 0, shcode);
            int result = t1101.Request(false);
            if (result < 0)
            {
                // TODO : 요청 실패 처리 
                return false;
            }
            return true;
        }
        // 주식 현재가 호가 데이터 수신 처리
        private void t1101ReceiveData(string trcode)
        {
            var t1101 = GetQueryService("t1101");
            if (t1101 == null)
                return;
            var currentStockPrice = new StockCurrentAskBidPriceData
            {
                hname = t1101.GetFieldData("t1101OutBlock", "hname", 0).Trim(),
                price = int.Parse(t1101.GetFieldData("t1101OutBlock", "price", 0).Trim()),
                sign = t1101.GetFieldData("t1101OutBlock", "sign", 0).Trim(),
                change = int.Parse(t1101.GetFieldData("t1101OutBlock", "change", 0).Trim()),
                diff = float.Parse(t1101.GetFieldData("t1101OutBlock", "diff", 0).Trim()),
                volume = long.Parse(t1101.GetFieldData("t1101OutBlock", "volume", 0).Trim()),
                jnilclose = int.Parse(t1101.GetFieldData("t1101OutBlock", "jnilclose", 0).Trim()),

                offer = long.Parse(t1101.GetFieldData("t1101OutBlock", "offer", 0).Trim()),
                bid = long.Parse(t1101.GetFieldData("t1101OutBlock", "bid", 0).Trim()),
                preoffercha = long.Parse(t1101.GetFieldData("t1101OutBlock", "preoffercha", 0).Trim()),
                prebidcha = long.Parse(t1101.GetFieldData("t1101OutBlock", "prebidcha", 0).Trim()),
                hotime = t1101.GetFieldData("t1101OutBlock", "hotime", 0).Trim(),
                yeprice = int.Parse(t1101.GetFieldData("t1101OutBlock", "yeprice", 0).Trim()),
                yevolume = long.Parse(t1101.GetFieldData("t1101OutBlock", "yevolume", 0).Trim()),
                yesign = t1101.GetFieldData("t1101OutBlock", "yesign", 0).Trim(),
                yechange = int.Parse(t1101.GetFieldData("t1101OutBlock", "yechange", 0).Trim()),
                yediff = float.Parse(t1101.GetFieldData("t1101OutBlock", "yediff", 0).Trim()),
                tmoffer = long.Parse(t1101.GetFieldData("t1101OutBlock", "tmoffer", 0).Trim()),
                tmbid = long.Parse(t1101.GetFieldData("t1101OutBlock", "tmbid", 0).Trim()),
                ho_status = t1101.GetFieldData("t1101OutBlock", "ho_status", 0).Trim(),
                shcode = t1101.GetFieldData("t1101OutBlock", "shcode", 0).Trim(),
                uplmtprice = int.Parse(t1101.GetFieldData("t1101OutBlock", "uplmtprice", 0).Trim()),
                dnlmtprice = int.Parse(t1101.GetFieldData("t1101OutBlock", "dnlmtprice", 0).Trim()),
                open = int.Parse(t1101.GetFieldData("t1101OutBlock", "open", 0).Trim()),
                high = int.Parse(t1101.GetFieldData("t1101OutBlock", "high", 0).Trim()),
                low = int.Parse(t1101.GetFieldData("t1101OutBlock", "low", 0).Trim()),
                krx_midprice = int.Parse(t1101.GetFieldData("t1101OutBlock", "krx_midprice", 0).Trim()),
                krx_offermidsumrem = int.Parse(t1101.GetFieldData("t1101OutBlock", "krx_offermidsumrem", 0).Trim()),
                krx_bidmidsumrem = int.Parse(t1101.GetFieldData("t1101OutBlock", "krx_bidmidsumrem", 0).Trim()),
                krx_midsumrem = int.Parse(t1101.GetFieldData("t1101OutBlock", "krx_midsumrem", 0).Trim()),
                krx_midsumremgubun = t1101.GetFieldData("t1101OutBlock", "krx_midsumremgubun", 0).Trim()
            };

            // 호가 1~10
            for (int i = 0; i < 10; i++)
            {
                currentStockPrice.offerho10[i] = int.Parse(t1101.GetFieldData("t1101OutBlock", $"offerho{i + 1}", 0).Trim());
                currentStockPrice.bidho10[i] = int.Parse(t1101.GetFieldData("t1101OutBlock", $"bidho{i + 1}", 0).Trim());
                currentStockPrice.offerrem10[i] = long.Parse(t1101.GetFieldData("t1101OutBlock", $"offerrem{i + 1}", 0).Trim());
                currentStockPrice.bidrem10[i] = long.Parse(t1101.GetFieldData("t1101OutBlock", $"bidrem{i + 1}", 0).Trim());
                currentStockPrice.preoffercha10[i] = long.Parse(t1101.GetFieldData("t1101OutBlock", $"preoffercha{i + 1}", 0).Trim());
                currentStockPrice.prebidcha10[i] = long.Parse(t1101.GetFieldData("t1101OutBlock", $"prebidcha{i + 1}", 0).Trim());
            }

            CurrentAskBidPriceDataUpdated?.Invoke(this, _currentAskBidPriceData);
        }

        // 주식차트(년월일) 데이터 요청 (t8410)
        public bool RequestStockChartData(string shcode, DateTime sdate, DateTime edate)
        {
            var t8410 = GetQueryService("t8410");
            if (t8410 == null)
                return false;
            t8410.ClearBlockdata("t8410InBlock");

            t8410.SetFieldData("t8410InBlock", "shcode", 0, shcode);
            t8410.SetFieldData("t8410InBlock", "gubun", 0, "2"); // 주기구분 (2:일 3:주 4:월 5:년)
            t8410.SetFieldData("t8410InBlock", "qrycnt", 0, "2000"); // 요청건수 (압축 최대:2000, 비압축 최대:500)
            t8410.SetFieldData("t8410InBlock", "sdate",0, sdate.ToString("yyyyMMdd")); // 시작일자 YYYYMMDD 형식
            t8410.SetFieldData("t8410InBlock", "edate",0, edate.ToString("yyyyMMdd")); // 종료일자 YYYYMMDD 형식
            t8410.SetFieldData("t8410InBlock", "cts_date", 0, ""); // 연속일자 YYYYMMDD 형식
            t8410.SetFieldData("t8410InBlock", "comp_yn", 0, "Y"); // 압축여부 Y/N
            t8410.SetFieldData("t8410InBlock", "sujung", 0, "Y"); // 수정주가여부 Y/N

            int result = t8410.Request(false);
            if (result < 0)
            {
                // TODO : 요청 실패 처리 
                return false;
            }
            return true;
        }

        private void t8410ReceiveData(string trcode)
        {
            // TODO: t8410 데이터 수신 처리 구현
            // Decompress() 로 압축 해제할 것
        }


        // 종목 리스트 요청 (t8430)
        public bool RequestStockList()
        {
            var t8430 = GetQueryService("t8430");
            if (t8430 == null)
                return false;
            t8430.ClearBlockdata("t8430OutBlock");
            t8430.SetFieldData("t8430InBlock", "gubun", 0, "0"); // 0:전체, 1:코스피, 2:코스닥
            int result = t8430.Request(false);
            if (result < 0)
            {
                // TODO : 요청 실패 처리 
                return false;
            }

            return true;
        }
        // 종목 리스트 가져오기
        private void t8430ReceiveData(string trcode)
        {
            var t8430 = GetQueryService("t8430");
            if (t8430 == null)
                return;
            int count = t8430.GetBlockCount("t8430OutBlock");
            for (int i = 0; i < count; i++)
            {
                var stock = new StockInfo
                {
                    shcode = t8430.GetFieldData("t8430OutBlock", "shcode", i).Trim(),
                    hname = t8430.GetFieldData("t8430OutBlock", "hname", i).Trim(),
                    expcode = t8430.GetFieldData("t8430OutBlock", "expcode", i).Trim(),
                    etfgubun = t8430.GetFieldData("t8430OutBlock", "etfgubun", i).Trim(),
                    gubun = t8430.GetFieldData("t8430OutBlock", "gubun", i).Trim()
                };
                // DB에 저장
                var existingStock = _dbContext.Stocks.Find(stock.shcode);
                if (existingStock == null)
                {
                    _dbContext.Stocks.Add(stock);
                }
                else
                {
                    _dbContext.Stocks.Update(stock);
                }
            }
            _dbContext.SaveChanges();
            
            StockListUpdated?.Invoke(this, EventArgs.Empty);
        }

        public async Task<List<StockInfo>> GetStockListAsync()
        {
            return await _dbContext.Stocks.ToListAsync();
        }
        public List<StockInfo> GetStockList()
        {
            return _dbContext.Stocks.ToList();
        }
    }
}
