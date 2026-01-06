using System.Collections.Generic;

namespace DumbTrader.Models
{
    // 전략 및 부가정보가 포함된 관심 주식 모델
    public class StrategyStock
    {
        public StockInfo Stock { get; set; } = new StockInfo();
        public string StrategyName { get; set; } = string.Empty;
    }
}
