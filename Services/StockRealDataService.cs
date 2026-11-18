using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace DumbTrader.Services
{
    public enum MarketType
    {
        KOSPI,
        KOSDAQ
    }

    /// <summary>
    /// 증권(주식) 실시간 데이터 처리를 담당하는 서비스. IXARealService를 래핑하여 더 직관적인 API 제공
    /// </summary>
    public class StockRealDataService
    {
        private readonly DumbTraderDbContext _dbContext;
        // KOSPI 체결
        private XARealService _S3_ = new XARealService();
        // KOSDAQ 체결
        private XARealService _K3_ = new XARealService();
        // Subscribed shcode 저장
        private Dictionary<string, MarketType> _subscribtions = new Dictionary<string, MarketType>();
        private readonly Dictionary<string, HashSet<string>> _subscriptionOwners = new();
        public IReadOnlyDictionary<string, MarketType> Subscriptions => _subscribtions;

        // S3_/K3_ 실시간 체결 데이터 업데이트 이벤트
        public event EventHandler<RealS3_K3_Data>? RealDataUpdated; 

        public StockRealDataService(DumbTraderDbContext dbContext)
        {
            _dbContext = dbContext;
            
            _S3_.LoadFromResFile("Res\\S3_.res");
            _K3_.LoadFromResFile("Res\\K3_.res");

            _S3_.AddReceiveRealDataEventHandler(S3K3_ReceiveRealData);
            _K3_.AddReceiveRealDataEventHandler(S3K3_ReceiveRealData);
        }

        // 단일 종목 실시간 데이터 구독
        public void SubscribeStockRealData(string shcode, MarketType marketType, string owner = "dashboard")
        {
            if (_subscriptionOwners.TryGetValue(shcode, out var owners))
            {
                owners.Add(owner);
                return;
            }
            if (marketType == MarketType.KOSPI)
            {
                _S3_.SetFieldData("InBlock", "shcode", shcode);
                _S3_.AdviseRealData();
            }
            else if (marketType == MarketType.KOSDAQ)
            {
                _K3_.SetFieldData("InBlock", "shcode", shcode);
                _K3_.AdviseRealData();
            }
            _subscribtions[shcode] = marketType;
            _subscriptionOwners.Add(shcode, [owner]);
        }

        // 단일 종목 실시간 데이터 구독 해제
        public void UnsubscribeStockRealData(string stockCode, MarketType marketType, string owner = "dashboard")
        {
            if (!_subscriptionOwners.TryGetValue(stockCode, out var owners) || !owners.Remove(owner) || owners.Count > 0)
                return;
            if (marketType == MarketType.KOSPI)
            {
                _S3_.UnadviseRealDataWithKey(stockCode);
            }
            else if (marketType == MarketType.KOSDAQ)
            {
                _K3_.UnadviseRealDataWithKey(stockCode);
            }
            _subscribtions.Remove(stockCode);
            _subscriptionOwners.Remove(stockCode);
        }

        public void UnsubscribeAll()
        {
            _S3_.UnadviseRealData();
            _K3_.UnadviseRealData();
            _subscribtions.Clear();
            _subscriptionOwners.Clear();
        }

        public void UnsubscribeDashboard()
        {
            foreach (var (symbol, market) in _subscribtions.ToArray())
                UnsubscribeStockRealData(symbol, market);
        }

        private void S3K3_ReceiveRealData(string trcode)
        {
            // KOSPI/KOSDAQ 체결 데이터 수신 시 처리 로직
            var feed = trcode == "K3_" ? _K3_ : _S3_;
            var realData = new RealS3_K3_Data
            {
                chetime = feed.GetFieldData("OutBlock", "chetime").Trim(),
                sign = feed.GetFieldData("OutBlock", "sign").Trim(),
                change = long.Parse(feed.GetFieldData("OutBlock", "change").Trim()),
                drate = float.Parse(feed.GetFieldData("OutBlock", "drate").Trim()),
                price = long.Parse(feed.GetFieldData("OutBlock", "price").Trim()),
                opentime = feed.GetFieldData("OutBlock", "opentime").Trim(),
                open = long.Parse(feed.GetFieldData("OutBlock", "open").Trim()),
                hightime = feed.GetFieldData("OutBlock", "hightime").Trim(),
                high = long.Parse(feed.GetFieldData("OutBlock", "high").Trim()),
                lowtime = feed.GetFieldData("OutBlock", "lowtime").Trim(),
                low = long.Parse(feed.GetFieldData("OutBlock", "low").Trim()),
                cgubun = feed.GetFieldData("OutBlock", "cgubun").Trim(),
                cvolume = long.Parse(feed.GetFieldData("OutBlock", "cvolume").Trim()),
                volume = long.Parse(feed.GetFieldData("OutBlock", "volume").Trim()),
                value = long.Parse(feed.GetFieldData("OutBlock", "value").Trim()),
                mdvolume = long.Parse(feed.GetFieldData("OutBlock", "mdvolume").Trim()),
                mdchecnt = long.Parse(feed.GetFieldData("OutBlock", "mdchecnt").Trim()),
                msvolume = long.Parse(feed.GetFieldData("OutBlock", "msvolume").Trim()),
                mschecnt = long.Parse(feed.GetFieldData("OutBlock", "mschecnt").Trim()),
                cpower = float.Parse(feed.GetFieldData("OutBlock", "cpower").Trim()),
                w_avrg = long.Parse(feed.GetFieldData("OutBlock", "w_avrg").Trim()),
                offerho = long.Parse(feed.GetFieldData("OutBlock", "offerho").Trim()),
                bidho = long.Parse(feed.GetFieldData("OutBlock", "bidho").Trim()),
                status = feed.GetFieldData("OutBlock", "status").Trim(),
                jnilvolume = long.Parse(feed.GetFieldData("OutBlock", "jnilvolume").Trim()),
                shcode = feed.GetFieldData("OutBlock", "shcode").Trim(),
                exchname = feed.GetFieldData("OutBlock", "exchname").Trim()
            };

            var existing = _dbContext.RealS3K3Data.Local
                .FirstOrDefault(e => e.shcode == realData.shcode && e.chetime == realData.chetime);

            if (existing == null)
            {
                existing = _dbContext.RealS3K3Data
                    .FirstOrDefault(e => e.shcode == realData.shcode && e.chetime == realData.chetime);
            }

            if (existing == null)
            {
                // 데이터 베이스 저장
                _dbContext.RealS3K3Data.Add(realData);
            }
            else
            {
                _dbContext.Entry(existing).CurrentValues.SetValues(realData);
                realData = existing;
            }

            // 실시간 데이터 업데이트 이벤트 발생
            RealDataUpdated?.Invoke(this, realData);
        }
    }
}
