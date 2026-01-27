
namespace DumbTrader.Models
{
    public class StrategyInfo 
    {
        // 주 전략 : 주식을 매수할 것인지, 매도할 것인지 결정하는 핵심 전략
        public string MainStrategyPath { get; set; } = string.Empty; // 주전략 C# 소스 파일 경로
        // 부 전략 : 매도가 결정 되면, 어떤 조건에서 어떻게 매도할 것인지 결정하는 보조 전략. 분할 매도등.
        public string SellStrategyPath { get; set; } = string.Empty; // 매도전략 C# 소스 파일 경로
        // 부 전략 : 매수가 결정 되면, 어떤 조건에서 어떻게 매수할 것인지 결정하는 보조 전략. 분할 매수등.
        public string BuyStrategyPath { get; set; } = string.Empty; // 매수전략 C# 소스 파일 경로
    }
    // 전략 및 부가정보가 포함된 관심 주식 모델
    public class StrategyStockInfo
    {
        public StockInfo Stock { get; set; } = new StockInfo();
        public StrategyInfo Strategy { get; set; } = new StrategyInfo();
    }
}
