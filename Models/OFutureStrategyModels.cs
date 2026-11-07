using DumbTrader.Services;

namespace DumbTrader.Models
{
    /// <summary>해외선물 계약별 전략 스크립트 설정.</summary>
    public sealed class OFutureStrategyAssignment
    {
        public OFutureContract Contract { get; set; } = new();
        public string MainStrategyPath { get; set; } = string.Empty;
    }

    /// <summary>Roslyn main 전략 스크립트에 제공되는 해외선물 컨텍스트.</summary>
    public sealed class OFutureStrategyGlobals
    {
        public OFutureContract Contract { get; init; } = new();
        public OFutureRealData RealData { get; init; } = new();
        public DumbTraderDbContext DbContext { get; init; } = null!;
        public LoggingService Logging { get; init; } = null!;
        public bool IsSimulation { get; init; }
        public double SeedMoney { get; init; }
        public DateTime SimulationDate { get; init; }
        public string PositionSide { get; init; } = "FLAT";
        public long PositionQuantity { get; init; }
        public Dictionary<string, object> Context { get; init; } = new();
    }

    public sealed class OFutureStrategySignalEventArgs(
        string symbol, string contractName, string action, int count, DateTime receivedAtUtc,
        string? detail = null, long requestGeneration = 0) : EventArgs
    {
        public string Symbol { get; } = symbol;
        public string ContractName { get; } = contractName;
        public string Action { get; } = action;
        public int Count { get; } = count;
        public DateTime ReceivedAtUtc { get; } = receivedAtUtc;
        public string? Detail { get; } = detail;
        public long RequestGeneration { get; } = requestGeneration;
    }

    public sealed record OFutureSimulationPeriodOption(string Name, int IntervalMinutes);

    public sealed record OFutureSimulationRunResult(
        string Symbol,
        string ContractName,
        string CurrencyCode,
        string PeriodName,
        double InitialCapital,
        double FinalCapital,
        double TotalProfit,
        double ProfitRate,
        double WinRate,
        int EntryCount,
        int ExitCount,
        int CompletedTrades,
        bool HasOpenPosition,
        string PositionSide,
        long PositionQuantity,
        int BarsProcessed,
        DateTime FirstBarDate,
        DateTime LastBarDate);
}
