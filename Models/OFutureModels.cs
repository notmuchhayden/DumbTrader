namespace DumbTrader.Models
{
    /// <summary>해외선물 계약 마스터 정보 (o3101/o3105).</summary>
    public sealed class OFutureContract
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BaseProductCode { get; set; } = string.Empty;
        public string BaseProductName { get; set; } = string.Empty;
        public string ExchangeCode { get; set; } = string.Empty;
        public string ExchangeName { get; set; } = string.Empty;
        public string CurrencyCode { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public string MaturityYear { get; set; } = string.Empty;
        public string MaturityMonth { get; set; } = string.Empty;
        public string ApplicationDate { get; set; } = string.Empty;
        public double TickSize { get; set; }
        public double TickValue { get; set; }
        public double ContractValue { get; set; }
        public double OpeningMargin { get; set; }
        public double MaintenanceMargin { get; set; }
    }

    /// <summary>해외선물 현재 호가/시세 (o3106).</summary>
    public sealed class OFutureQuote
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double Price { get; set; }
        public string Sign { get; set; } = string.Empty;
        public double Change { get; set; }
        public double ChangeRate { get; set; }
        public double Volume { get; set; }
        public double PreviousClose { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public string QuoteTime { get; set; } = string.Empty;
        public double[] AskPrices { get; } = new double[5];
        public double[] BidPrices { get; } = new double[5];
        public long[] AskSizes { get; } = new long[5];
        public long[] BidSizes { get; } = new long[5];
        public long TotalAskSize { get; set; }
        public long TotalBidSize { get; set; }
        public long TotalAskCount { get; set; }
        public long TotalBidCount { get; set; }
    }

    /// <summary>해외선물 관심종목 일괄 시세 조회 결과 (o3107OutBlock).</summary>
    public sealed class OFutureWatchlistQuote
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double Price { get; set; }
        public string Sign { get; set; } = string.Empty;
        public double Change { get; set; }
        public double ChangeRate { get; set; }
        public long Volume { get; set; }
        public double PreviousClose { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double AskPrice { get; set; }
        public double BidPrice { get; set; }
        public long AskCount { get; set; }
        public long BidCount { get; set; }
        public long AskSize { get; set; }
        public long BidSize { get; set; }
        public long TotalAskCount { get; set; }
        public long TotalBidCount { get; set; }
        public long TotalAskSize { get; set; }
        public long TotalBidSize { get; set; }
    }

    public sealed class OFutureWatchlistQuotesEventArgs(IReadOnlyList<OFutureWatchlistQuote> data) : EventArgs
    {
        public IReadOnlyList<OFutureWatchlistQuote> Data { get; } = data;
    }

    /// <summary>해외선물 N분봉 차트 데이터 (o3103OutBlock1). Time은 현지시간이다.</summary>
    public sealed class OFutureMinuteChartData
    {
        public string Symbol { get; set; } = string.Empty;
        public int IntervalMinutes { get; set; }
        public string Date { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public long Volume { get; set; }
    }

    /// <summary>해외선물 분봉 조회 결과. 연속조회 시 응답의 일자/시간 키를 함께 전달한다.</summary>
    public sealed class OFutureMinuteChartDataEventArgs : EventArgs
    {
        public string Symbol { get; init; } = string.Empty;
        public int IntervalMinutes { get; init; }
        public long TimeDifference { get; init; }
        public long ReadCount { get; init; }
        public string ContinuationDate { get; init; } = string.Empty;
        public string ContinuationTime { get; init; } = string.Empty;
        public bool IsNext { get; init; }
        public IReadOnlyList<OFutureMinuteChartData> Data { get; init; } = Array.Empty<OFutureMinuteChartData>();
    }

    /// <summary>해외선물 일별 체결 데이터 (o3104OutBlock1).</summary>
    public sealed class OFutureDailyTradeData
    {
        public string Symbol { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public double Price { get; set; }
        public string Sign { get; set; } = string.Empty;
        public double Change { get; set; }
        public double ChangeRate { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public string TradeDirection { get; set; } = string.Empty;
        public long Volume { get; set; }
    }

    public sealed class OFutureDailyTradeDataEventArgs : EventArgs
    {
        public string Symbol { get; init; } = string.Empty;
        public string QueryType { get; init; } = string.Empty;
        public string QueryDate { get; init; } = string.Empty;
        public IReadOnlyList<OFutureDailyTradeData> Data { get; init; } = Array.Empty<OFutureDailyTradeData>();
    }

    /// <summary>해외선물 일봉/주봉/월봉 차트 데이터 (o3108).</summary>
    public sealed class OFutureChartData
    {
        public string Symbol { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public long Volume { get; set; }
    }

    public sealed class OFutureChartDataEventArgs(string symbol, IReadOnlyList<OFutureChartData> data) : EventArgs
    {
        public string Symbol { get; } = symbol;
        public IReadOnlyList<OFutureChartData> Data { get; } = data;
    }

    /// <summary>해외선물 실시간 체결 데이터 (OVC).</summary>
    public sealed class OFutureRealData
    {
        public string Symbol { get; set; } = string.Empty;
        public string OverseasDate { get; set; } = string.Empty;
        public string KoreaDate { get; set; } = string.Empty;
        public string TradeTime { get; set; } = string.Empty;
        public string KoreaTradeTime { get; set; } = string.Empty;
        public double Price { get; set; }
        public double Change { get; set; }
        public string ChangeSign { get; set; } = string.Empty;
        public double ChangeRate { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public long TradeQuantity { get; set; }
        public long TotalQuantity { get; set; }
        public string TradeDirection { get; set; } = string.Empty;
        public long SellTradeQuantity { get; set; }
        public long BuyTradeQuantity { get; set; }
    }

    /// <summary>해외선물 실시간 호가 데이터 (OVH).</summary>
    public sealed class OFutureOrderBook
    {
        public string Symbol { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public double[] AskPrices { get; } = new double[5];
        public double[] BidPrices { get; } = new double[5];
        public long[] AskSizes { get; } = new long[5];
        public long[] BidSizes { get; } = new long[5];
        public long[] AskCounts { get; } = new long[5];
        public long[] BidCounts { get; } = new long[5];
        public long TotalAskSize { get; set; }
        public long TotalBidSize { get; set; }
        public long TotalAskCount { get; set; }
        public long TotalBidCount { get; set; }
    }

    /// <summary>해외선물 신규/정정/취소 주문 입력에 사용하는 TR 공통 필드.</summary>
    public sealed class OFutureOrderRequest
    {
        public string OrderDate { get; set; } = DateTime.Now.ToString("yyyyMMdd");
        public string BranchCode { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string OrderTypeCode { get; set; } = string.Empty;
        public string BuySellCode { get; set; } = string.Empty;
        public string OrderPatternCode { get; set; } = string.Empty;
        public string CurrencyCode { get; set; } = string.Empty;
        public double Price { get; set; }
        public double ConditionPrice { get; set; }
        public long Quantity { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductTypeCode { get; set; } = string.Empty;
        public string MaturityYearMonth { get; set; } = string.Empty;
        public string ExchangeCode { get; set; } = string.Empty;
        public string OriginalOrderNumber { get; set; } = string.Empty;
    }

    public sealed record OFutureOrderResult(bool Success, string Message, string OrderNumber = "");
}
