using System.Collections.Generic;
using System.ComponentModel;
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

        // 종목 리스트 업데이트 알림용 속성
        public event EventHandler? StockListUpdated;

        public StockDataService(DumbTraderDbContext dbContext)
        {
            _dbContext = dbContext;

            // t8430 초기화
            _xaQueryServices = new Dictionary<string, IXAQueryService>();

            // 주식 현재가 호가 조회
            var t1101 = new XAQueryService();
            t1101.ResFileName = "Res\\t1101.res";
            //t1101.AddReceiveDataEventHandler(t1101ReceiveData);
            _xaQueryServices.Add("t1101", t1101);

            // 주식종목조회
            var t8430 = new XAQueryService();
            t8430.ResFileName = "Res\\t8430.res";
            t8430.AddReceiveDataEventHandler(t8430ReceiveData);
            _xaQueryServices.Add("t8430", t8430);
        }

        //tcode로 IXAQueryService 가져오기
        public IXAQueryService? GetQueryService(string tcode)
        {
            _xaQueryServices.TryGetValue(tcode, out var service);
            return service;
        }

        // 종목 리스트 요청 (t8430)
        public bool RequestStockList()
        {
            var t8430 = GetQueryService("t8430");
            if (t8430 == null)
                return false;
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
