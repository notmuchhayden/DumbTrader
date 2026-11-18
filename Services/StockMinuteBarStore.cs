using System.Globalization;
using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services;

public sealed class StockMinuteBarStore(IDbContextFactory<DumbTraderDbContext> factory)
{
    public static DateTime MarketNow => DateTime.UtcNow.AddHours(9);
    private static string OldestDate(DateTime marketDate) =>
        marketDate.AddDays(-6).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<StockMinuteBar>> ReadAsync(string symbol, DateTime marketDate)
    {
        await using var db = await factory.CreateDbContextAsync().ConfigureAwait(false);
        var oldest = OldestDate(marketDate);
        return await db.StockMinuteBars.AsNoTracking().Where(bar => bar.Symbol == symbol && string.Compare(bar.TradeDate, oldest) >= 0)
            .OrderBy(bar => bar.TradeDate).ThenBy(bar => bar.TradeTime).ToListAsync().ConfigureAwait(false);
    }

    public async Task SaveAsync(IEnumerable<StockMinuteBar> bars, DateTime marketDate, bool prune)
    {
        await using var db = await factory.CreateDbContextAsync().ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync().ConfigureAwait(false);
        var oldest = OldestDate(marketDate);
        foreach (var bar in bars)
        {
            if (string.CompareOrdinal(bar.TradeDate, oldest) < 0) continue;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO StockMinuteBars (Symbol, TradeDate, TradeTime, Open, High, Low, Close, Volume, IsServer)
                VALUES ({bar.Symbol}, {bar.TradeDate}, {bar.TradeTime}, {bar.Open}, {bar.High}, {bar.Low}, {bar.Close}, {bar.Volume}, {bar.IsServer})
                ON CONFLICT (Symbol, TradeDate, TradeTime) DO UPDATE SET
                    Open = excluded.Open, High = excluded.High, Low = excluded.Low,
                    Close = excluded.Close, Volume = excluded.Volume, IsServer = excluded.IsServer
                WHERE excluded.IsServer = 1 OR StockMinuteBars.IsServer = 0
                """).ConfigureAwait(false);
        }
        if (prune)
            await db.StockMinuteBars.Where(bar => string.Compare(bar.TradeDate, oldest) < 0)
                .ExecuteDeleteAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
    }
}
