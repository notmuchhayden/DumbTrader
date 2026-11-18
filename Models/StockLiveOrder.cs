namespace DumbTrader.Models;

public enum StockLiveOrderState
{
    Submitting = 0, NeedsReview = 1, Accepted = 2, PartiallyFilled = 3, Filled = 4, Cancelled = 5, Rejected = 6
}

public sealed class StockLiveOrder
{
    public Guid IntentId { get; set; }
    public string Account { get; set; } = "";
    public string TradeDate { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string Strategy { get; set; } = "";
    public string Side { get; set; } = "";
    public DateTime DecisionUtc { get; set; }
    public long Quantity { get; set; }
    public long LimitPrice { get; set; }
    public long? OrderNumber { get; set; }
    public long FilledQuantity { get; set; }
    public long CancelledQuantity { get; set; }
    public decimal AverageFillPrice { get; set; }
    public StockLiveOrderState State { get; set; }
}

public sealed record StockBrokerOrderEvent(string Account, string TradeDate, string Code, long OrderNumber,
    long OriginalOrderNumber, long ExecutionNumber, long Quantity, long Price, string Symbol = "", string Side = "");
