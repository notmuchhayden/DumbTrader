using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services
{
    /// <summary>OFuture 차트와 OVC 최신값 영속화. 각 작업에서 독립 DbContext를 사용한다.</summary>
    public sealed class OFutureChartStoreService(IDbContextFactory<DumbTraderDbContext> contextFactory)
    {
        private readonly SemaphoreSlim _writeGate = new(1, 1);

        public async Task<IReadOnlyList<OFutureMinuteBar>> ReadMinuteBarsAsync(
            string symbol, int intervalMinutes, DateTime startDate, DateTime endDate,
            CancellationToken cancellationToken = default)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await db.OFutureMinuteBars.AsNoTracking()
                .Where(bar => bar.Symbol == symbol && bar.IntervalMinutes == intervalMinutes
                    && string.Compare(bar.TradeDate, startDate.ToString("yyyyMMdd")) >= 0
                    && string.Compare(bar.TradeDate, endDate.ToString("yyyyMMdd")) <= 0)
                .OrderBy(bar => bar.TradeDate).ThenBy(bar => bar.TradeTime)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<OFutureDailyBar>> ReadDailyBarsAsync(
            string symbol, DateTime startDate, DateTime endDate,
            CancellationToken cancellationToken = default)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await db.OFutureDailyBars.AsNoTracking()
                .Where(bar => bar.Symbol == symbol
                    && string.Compare(bar.TradeDate, startDate.ToString("yyyyMMdd")) >= 0
                    && string.Compare(bar.TradeDate, endDate.ToString("yyyyMMdd")) <= 0)
                .OrderBy(bar => bar.TradeDate).ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<OFutureLatestTradeSnapshot?> ReadLatestTradeAsync(
            string symbol, CancellationToken cancellationToken = default)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await db.OFutureLatestTradeSnapshots.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Symbol == symbol, cancellationToken).ConfigureAwait(false);
        }

        public async Task UpsertMinuteBarsAsync(
            IReadOnlyCollection<OFutureMinuteBar> bars, CancellationToken cancellationToken = default)
        {
            if (bars.Count == 0) return;
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                foreach (var bar in bars)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO OFutureMinuteBars
                        (Symbol, IntervalMinutes, TradeDate, TradeTime, Open, High, Low, Close, Volume, ReceivedAtUtc)
                    VALUES
                        ({bar.Symbol}, {bar.IntervalMinutes}, {bar.TradeDate}, {bar.TradeTime}, {bar.Open}, {bar.High},
                         {bar.Low}, {bar.Close}, {bar.Volume}, {bar.ReceivedAtUtc})
                    ON CONFLICT(Symbol, IntervalMinutes, TradeDate, TradeTime) DO UPDATE SET
                        Open=excluded.Open, High=excluded.High, Low=excluded.Low, Close=excluded.Close,
                        Volume=excluded.Volume, ReceivedAtUtc=excluded.ReceivedAtUtc
                    WHERE excluded.ReceivedAtUtc >= OFutureMinuteBars.ReceivedAtUtc;
                    """, cancellationToken).ConfigureAwait(false);
                }
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally { _writeGate.Release(); }
        }

        public async Task UpsertDailyBarsAsync(
            IReadOnlyCollection<OFutureDailyBar> bars, CancellationToken cancellationToken = default)
        {
            if (bars.Count == 0) return;
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                foreach (var bar in bars.Where(item => item.SourceTrCode == "o3108" && item.QueryType == "0"))
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO OFutureDailyBars
                        (Symbol, TradeDate, SourceTrCode, QueryType, Open, High, Low, Close, SourcePrice,
                         Volume, Change, ChangeRate, Sign, ReceivedAtUtc)
                    VALUES
                        ({bar.Symbol}, {bar.TradeDate}, {bar.SourceTrCode}, {bar.QueryType}, {bar.Open}, {bar.High},
                         {bar.Low}, {bar.Close}, {bar.SourcePrice}, {bar.Volume}, {bar.Change}, {bar.ChangeRate},
                         {bar.Sign}, {bar.ReceivedAtUtc})
                    ON CONFLICT(Symbol, TradeDate) DO UPDATE SET
                        SourceTrCode=excluded.SourceTrCode, QueryType=excluded.QueryType,
                        Open=excluded.Open, High=excluded.High, Low=excluded.Low, Close=excluded.Close,
                        SourcePrice=excluded.SourcePrice, Volume=excluded.Volume, Change=excluded.Change,
                        ChangeRate=excluded.ChangeRate, Sign=excluded.Sign, ReceivedAtUtc=excluded.ReceivedAtUtc
                    WHERE excluded.ReceivedAtUtc >= OFutureDailyBars.ReceivedAtUtc;
                    """, cancellationToken).ConfigureAwait(false);
                }
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally { _writeGate.Release(); }
        }

        public async Task UpsertLatestTradeAsync(
            OFutureLatestTradeSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                var storedAtUtc = DateTime.UtcNow;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO OFutureLatestTradeSnapshots
                    (Symbol, OverseasDate, KoreaDate, TradeTime, KoreaTradeTime, Price, Change, ChangeSign,
                     ChangeRate, Open, High, Low, TradeQuantity, TotalQuantity, TradeDirection,
                     SellTradeQuantity, BuyTradeQuantity, ReceivedAtUtc, StoredAtUtc)
                VALUES
                    ({snapshot.Symbol}, {snapshot.OverseasDate}, {snapshot.KoreaDate}, {snapshot.TradeTime},
                     {snapshot.KoreaTradeTime}, {snapshot.Price}, {snapshot.Change}, {snapshot.ChangeSign},
                     {snapshot.ChangeRate}, {snapshot.Open}, {snapshot.High}, {snapshot.Low},
                     {snapshot.TradeQuantity}, {snapshot.TotalQuantity}, {snapshot.TradeDirection},
                     {snapshot.SellTradeQuantity}, {snapshot.BuyTradeQuantity}, {snapshot.ReceivedAtUtc}, {storedAtUtc})
                ON CONFLICT(Symbol) DO UPDATE SET
                    OverseasDate=excluded.OverseasDate, KoreaDate=excluded.KoreaDate,
                    TradeTime=excluded.TradeTime, KoreaTradeTime=excluded.KoreaTradeTime,
                    Price=excluded.Price, Change=excluded.Change, ChangeSign=excluded.ChangeSign,
                    ChangeRate=excluded.ChangeRate, Open=excluded.Open, High=excluded.High, Low=excluded.Low,
                    TradeQuantity=excluded.TradeQuantity, TotalQuantity=excluded.TotalQuantity,
                    TradeDirection=excluded.TradeDirection, SellTradeQuantity=excluded.SellTradeQuantity,
                    BuyTradeQuantity=excluded.BuyTradeQuantity, ReceivedAtUtc=excluded.ReceivedAtUtc,
                    StoredAtUtc=excluded.StoredAtUtc
                WHERE excluded.ReceivedAtUtc >= OFutureLatestTradeSnapshots.ReceivedAtUtc;
                """, cancellationToken).ConfigureAwait(false);
            }
            finally { _writeGate.Release(); }
        }
    }
}
