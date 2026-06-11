namespace DumbTrader.Models
{
    public sealed record SimulationRunResult(
        string StockCode,
        string StockName,
        long InitialCapital,
        long FinalCapital,
        long TotalProfit,
        double ProfitRate,
        double WinRate,
        int TotalTrades);
}
