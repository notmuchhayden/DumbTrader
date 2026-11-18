using System.Globalization;
using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services;

internal sealed class OFutureCompletedBarStrategy(
    OFutureStrategyService strategies, OFutureStrategyAssignment assignment, int interval, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private const double PaperCapital = 100_000;
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Dictionary<string, object> _context = new();
    private readonly StrategyLedger _ledger = new(MarketSnapshotBuilder.Instrument(assignment.Contract),
        new StrategyCosts(), PaperCapital, 1);
    private readonly OFutureStrategyAssignment _assignment = new()
    {
        Contract = assignment.Contract, MainStrategyPath = assignment.MainStrategyPath
    };
    private DateTime? _firstEligible;
    private DateTime _processedThrough;
    private DateTime _bucket;
    private DateTime _rolloverUtc;
    private DateTime _retryAfterUtc;
    private string? _code;

    public bool Matches(OFutureStrategyAssignment current) =>
        _assignment.Contract.Symbol == current.Contract.Symbol &&
        _assignment.MainStrategyPath == current.MainStrategyPath;

    public DateTime Bucket => _bucket;

    public async Task<IReadOnlyList<OFutureChartEvent>> EvaluateAsync(
        OFutureRealData quote, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        if (quote.ReceivedAtUtc < now.AddMinutes(-1)
            || !double.IsFinite(quote.Price) || quote.Price <= 0 || quote.TradeQuantity <= 0
            || !TryTimestamp(quote.OverseasDate, quote.TradeTime, out var timestamp))
            return [];

        var bucket = interval == 0 ? timestamp.Date :
            timestamp.Date.AddMinutes((int)timestamp.TimeOfDay.TotalMinutes / interval * interval);
        if (bucket < _bucket || (bucket == _bucket && now < _retryAfterUtc)) return [];
        if (bucket > _bucket)
        {
            _firstEligible ??= interval == 0 ? bucket : bucket.AddMinutes(interval);
            _bucket = bucket;
            _rolloverUtc = quote.ReceivedAtUtc;
        }
        _retryAfterUtc = now.AddSeconds(interval == 0 ? 15 : 2);

        _code ??= await strategies.LoadMainScriptAsync(_assignment, cancellationToken).ConfigureAwait(false);
        await using var db = await strategies.CreateBarContextAsync(cancellationToken).ConfigureAwait(false);
        var symbol = _assignment.Contract.Symbol;
        var start = (_processedThrough == default ? bucket.AddYears(-1) : _processedThrough).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var end = bucket.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var bars = new List<StrategyBar>();
        if (interval == 0)
        {
            var rows = await db.OFutureDailyBars.AsNoTracking().Where(row => row.Symbol == symbol
                && row.SourceTrCode == "o3108" && row.QueryType == "0"
                && string.Compare(row.TradeDate, start) >= 0 && string.Compare(row.TradeDate, end) < 0)
                .OrderBy(row => row.TradeDate).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                if (TryTimestamp(row.TradeDate, "000000", out var date)
                    && (date < _firstEligible || row.ReceivedAtUtc >= _rolloverUtc))
                    bars.Add(new(date, row.Open, row.High, row.Low, row.Close, row.Volume));
            }
        }
        else
        {
            var rows = await db.OFutureMinuteBars.AsNoTracking().Where(row => row.Symbol == symbol
                && row.IntervalMinutes == interval
                && string.Compare(row.TradeDate, start) >= 0 && string.Compare(row.TradeDate, end) <= 0)
                .OrderBy(row => row.TradeDate).ThenBy(row => row.TradeTime)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
                if (TryTimestamp(row.TradeDate, row.TradeTime, out var time))
                    bars.Add(new(time, row.Open, row.High, row.Low, row.Close, row.Volume));
        }

        var completed = bars.Where(bar => bar.Time > _processedThrough && bar.Time < bucket
            && bar.IsValid && bar.Open > 0 && bar.Low > 0 && bar.Volume > 0
            // The first observed intraday bucket can contain a partial OVC history.
            && (interval == 0 || bar.Time != _firstEligible!.Value.AddMinutes(-interval))).ToArray();
        var events = new List<OFutureChartEvent>();
        foreach (var bar in completed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Advance before execution so redispatch cannot duplicate a script step.
            _processedThrough = bar.Time;
            var result = await strategies.RunCompletedBarAsync(_code, _assignment.Contract, bar, interval,
                db, _context, PaperCapital + _ledger.Realized, _ledger.Position, cancellationToken).ConfigureAwait(false);
            if (!result.Success) throw new InvalidOperationException($"완성 봉 전략 실행 실패 ({symbol}, {bar.Time:g})");
            if (bar.Time < _firstEligible || result.Action is not ("BUY" or "SELL")) continue;

            var before = _ledger.Position;
            var canFill = bar == completed[^1] && (interval == 0 || bar.Time.AddMinutes(interval) == bucket)
                && quote.ReceivedAtUtc >= _clock.GetUtcNow().UtcDateTime.AddMinutes(-1);
            if (canFill)
                _ledger.Fill(new StrategySignal(result.Action == "BUY" ? StrategyAction.Buy : StrategyAction.Sell,
                    result.MainResult?.Count ?? 0, "완성 봉", 0), quote.Price);
            var filled = before != _ledger.Position;
            var signal = new OFutureChartEvent(Guid.NewGuid(), bar.Time, bar.Close, result.MainResult?.Count ?? 0,
                result.Action, OFutureChartEventKind.Signal, symbol, _assignment.MainStrategyPath, "페이퍼",
                Detail: filled ? null : canFill ? "페이퍼 미체결" : "페이퍼 미체결 · 과거 봉")
                { IntervalMinutes = interval, RunId = _sessionId, PublishedAtUtc = quote.ReceivedAtUtc };
            events.Add(signal);
            if (filled)
                events.Add(new OFutureChartEvent(Guid.NewGuid(), timestamp, quote.Price,
                    Math.Abs(_ledger.Position - before), result.Action,
                    before == 0 ? OFutureChartEventKind.Entry : OFutureChartEventKind.Exit,
                    symbol, _assignment.MainStrategyPath, "페이퍼", signal.Id)
                {
                    RunId = _sessionId, PublishedAtUtc = quote.ReceivedAtUtc,
                    PositionBefore = before, PositionAfter = _ledger.Position
                });
        }
        return events;
    }

    private static bool TryTimestamp(string date, string time, out DateTime timestamp) =>
        DateTime.TryParseExact(date + time, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out timestamp);
}
