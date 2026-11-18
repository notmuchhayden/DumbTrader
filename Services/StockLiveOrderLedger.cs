using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services;

public sealed class StockLiveOrderLedger(IDbContextFactory<DumbTraderDbContext> factory)
{
    private readonly SemaphoreSlim _gate = new(1);

    public async Task<bool> ReserveAsync(StockLiveOrder intent)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var db = await factory.CreateDbContextAsync().ConfigureAwait(false);
            if (await db.StockLiveOrders.AnyAsync(row => row.IntentId == intent.IntentId ||
                (row.Account == intent.Account && row.Symbol == intent.Symbol
                && (row.State == StockLiveOrderState.Submitting || row.State == StockLiveOrderState.NeedsReview
                    || row.State == StockLiveOrderState.Accepted || row.State == StockLiveOrderState.PartiallyFilled)))
                .ConfigureAwait(false)) return false;
            intent.State = StockLiveOrderState.Submitting;
            db.StockLiveOrders.Add(intent);
            await db.SaveChangesAsync().ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task ConfirmSubmissionAsync(Guid intentId, long? orderNumber)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var db = await factory.CreateDbContextAsync().ConfigureAwait(false);
            var order = await db.StockLiveOrders.SingleAsync(row => row.IntentId == intentId).ConfigureAwait(false);
            if (order.State != StockLiveOrderState.Submitting) return;
            if (orderNumber is > 0)
            {
                order.OrderNumber = orderNumber;
                order.State = StockLiveOrderState.Accepted;
                await RebuildOrderAsync(db, order).ConfigureAwait(false);
            }
            else order.State = StockLiveOrderState.NeedsReview;
            await db.SaveChangesAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> RecordBrokerEventAsync(StockBrokerOrderEvent update)
    {
        if (update.OrderNumber <= 0 || string.IsNullOrWhiteSpace(update.Account)
            || update.Code is not ("SC0" or "SC1" or "SC2" or "SC3" or "SC4")
            || (update.Code == "SC1" && (update.ExecutionNumber <= 0 || update.Quantity <= 0 || update.Price <= 0)))
            return false;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var db = await factory.CreateDbContextAsync().ConfigureAwait(false);
            await using var transaction = await db.Database.BeginTransactionAsync().ConfigureAwait(false);
            var normalized = update with { ExecutionNumber = update.Code == "SC1" ? update.ExecutionNumber : 0 };
            if (await db.StockBrokerOrderEvents.FindAsync(normalized.Account, normalized.TradeDate,
                normalized.Code, normalized.OrderNumber, normalized.ExecutionNumber).ConfigureAwait(false) is not null)
                return false;
            db.StockBrokerOrderEvents.Add(normalized);
            await db.SaveChangesAsync().ConfigureAwait(false);
            var orderNumber = update.Code == "SC3" && update.OriginalOrderNumber > 0
                ? update.OriginalOrderNumber : update.OrderNumber;
            var order = await db.StockLiveOrders.SingleOrDefaultAsync(row => row.Account == update.Account
                && row.TradeDate == update.TradeDate && row.OrderNumber == orderNumber).ConfigureAwait(false);
            if (order is not null) await RebuildOrderAsync(db, order).ConfigureAwait(false);
            await db.SaveChangesAsync().ConfigureAwait(false);
            await transaction.CommitAsync().ConfigureAwait(false);
            return true;
        }
        finally { _gate.Release(); }
    }

    private static async Task RebuildOrderAsync(DumbTraderDbContext db, StockLiveOrder order)
    {
        var events = await db.StockBrokerOrderEvents.Where(update => update.Account == order.Account
            && update.TradeDate == order.TradeDate &&
            ((update.OrderNumber == order.OrderNumber && (update.Code == "SC1" || update.Code == "SC4"))
            || (update.Code == "SC3" && update.OriginalOrderNumber == order.OrderNumber)))
            .ToListAsync().ConfigureAwait(false);
        var fills = events.Where(update => update.Code == "SC1").ToArray();
        var filled = fills.Sum(fill => fill.Quantity);
        var cancelled = events.Where(update => update.Code == "SC3").Sum(update => Math.Max(0, update.Quantity));
        order.FilledQuantity = filled;
        order.CancelledQuantity = cancelled;
        order.AverageFillPrice = filled == 0 ? 0 : fills.Sum(fill => (decimal)fill.Quantity * fill.Price) / filled;
        order.State = filled > order.Quantity || cancelled > order.Quantity - filled ? StockLiveOrderState.NeedsReview
            : filled == order.Quantity ? StockLiveOrderState.Filled
            : cancelled == order.Quantity - filled && cancelled > 0 ? StockLiveOrderState.Cancelled
            : filled > 0 ? StockLiveOrderState.PartiallyFilled
            : events.Any(update => update.Code == "SC4") ? StockLiveOrderState.Rejected
            : StockLiveOrderState.Accepted;
    }
}
