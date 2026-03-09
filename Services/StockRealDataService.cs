using DumbTrader.Models;

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
        public void SubscribeStockRealData(string shcode, MarketType marketType)
        {
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
        }

        // 단일 종목 실시간 데이터 구독 해제
        public void UnsubscribeStockRealData(string stockCode, MarketType marketType)
        {
            if (marketType == MarketType.KOSPI)
            {
                _S3_.UnadviseRealDataWithKey(stockCode);
            }
            else if (marketType == MarketType.KOSDAQ)
            {
                _K3_.UnadviseRealDataWithKey(stockCode);
            }
            _subscribtions.Remove(stockCode);
        }

        public void UnsubscribeAll()
        {
            _S3_.UnadviseRealData();
            _K3_.UnadviseRealData();
            _subscribtions.Clear();
        }

        private void S3K3_ReceiveRealData(string trcode)
        {
            // KOSPI/KOSDAQ 체결 데이터 수신 시 처리 로직
            var realData = new RealS3_K3_Data
            {
                chetime = _S3_.GetFieldData("OutBlock", "chetime").Trim(),
                sign = _S3_.GetFieldData("OutBlock", "sign").Trim(),
                change = long.Parse(_S3_.GetFieldData("OutBlock", "change").Trim()),
                drate = float.Parse(_S3_.GetFieldData("OutBlock", "drate").Trim()),
                price = long.Parse(_S3_.GetFieldData("OutBlock", "price").Trim()),
                opentime = _S3_.GetFieldData("OutBlock", "opentime").Trim(),
                open = long.Parse(_S3_.GetFieldData("OutBlock", "open").Trim()),
                hightime = _S3_.GetFieldData("OutBlock", "hightime").Trim(),
                high = long.Parse(_S3_.GetFieldData("OutBlock", "high").Trim()),
                lowtime = _S3_.GetFieldData("OutBlock", "lowtime").Trim(),
                low = long.Parse(_S3_.GetFieldData("OutBlock", "low").Trim()),
                cgubun = _S3_.GetFieldData("OutBlock", "cgubun").Trim(),
                cvolume = long.Parse(_S3_.GetFieldData("OutBlock", "cvolume").Trim()),
                volume = long.Parse(_S3_.GetFieldData("OutBlock", "volume").Trim()),
                value = long.Parse(_S3_.GetFieldData("OutBlock", "value").Trim()),
                mdvolume = long.Parse(_S3_.GetFieldData("OutBlock", "mdvolume").Trim()),
                mdchecnt = long.Parse(_S3_.GetFieldData("OutBlock", "mdchecnt").Trim()),
                msvolume = long.Parse(_S3_.GetFieldData("OutBlock", "msvolume").Trim()),
                mschecnt = long.Parse(_S3_.GetFieldData("OutBlock", "mschecnt").Trim()),
                cpower = float.Parse(_S3_.GetFieldData("OutBlock", "cpower").Trim()),
                w_avrg = long.Parse(_S3_.GetFieldData("OutBlock", "w_avrg").Trim()),
                offerho = long.Parse(_S3_.GetFieldData("OutBlock", "offerho").Trim()),
                bidho = long.Parse(_S3_.GetFieldData("OutBlock", "bidho").Trim()),
                status = _S3_.GetFieldData("OutBlock", "status").Trim(),
                jnilvolume = long.Parse(_S3_.GetFieldData("OutBlock", "jnilvolume").Trim()),
                shcode = _S3_.GetFieldData("OutBlock", "shcode").Trim(),
                exchname = _S3_.GetFieldData("OutBlock", "exchname").Trim()
            };

            // 데이터 베이스 저장
            _dbContext.RealS3K3Data.Add(realData);

            // 실시간 데이터 업데이트 이벤트 발생
            RealDataUpdated?.Invoke(this, realData);
        }
    }
}
