using System.Globalization;
using System.Text.Json;
using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services;

public sealed class MarketSnapshotBuilder(IDbContextFactory<DumbTraderDbContext> factory)
{
    public static StrategyInstrument Instrument(OFutureContract contract) =>
        new(contract.Symbol, contract.CurrencyCode, contract.TickSize, contract.TickValue, contract.OpeningMargin);

    public static StrategyInstrument Stock(StockInfo stock) => new(stock.shcode, "KRW", 1, 1, 1, "Stock", stock.gubun == "2");

    public async Task<StrategySnapshot> BuildAsync(StrategyInstrument instrument, int interval, DateTime start,
        StrategyEvolutionOptions options, string championCode, Guid? championId, CancellationToken token)
    {
        options.Validate();
        if (string.IsNullOrWhiteSpace(instrument.Symbol) || !double.IsFinite(instrument.TickSize)
            || !double.IsFinite(instrument.TickValue) || !double.IsFinite(instrument.Margin)
            || instrument.TickSize <= 0 || instrument.TickValue <= 0 || instrument.Margin <= 0)
            throw new InvalidOperationException("종목과 틱 크기·틱 가치·증거금 정보를 확인하세요.");

        await using var db = await factory.CreateDbContextAsync(token).ConfigureAwait(false);
        var from = start.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        const int limit = StrategyEvaluationService.MaximumBars + 1;
        var symbol = instrument.Symbol;
        IEnumerable<StrategyBar?> data;
        if (instrument.Market == "Stock" && interval == 0)
        {
            var rows = await db.StockChartDatas.AsNoTracking()
                .Where(bar => bar.shcode == symbol && string.Compare(bar.date, from) >= 0)
                .OrderByDescending(bar => bar.date).Take(limit).ToListAsync(token).ConfigureAwait(false);
            data = rows.Where(bar => bar.open > 0 && bar.low > 0 && bar.jdiff_vol > 0)
                .Select(bar => Parse(bar.date, "000000", bar.open, bar.high, bar.low, bar.close, bar.jdiff_vol));
        }
        else if (instrument.Market == "OFuture" && interval == 0)
        {
            var rows = await db.OFutureDailyBars.AsNoTracking()
                .Where(bar => bar.Symbol == symbol && bar.SourceTrCode == "o3108" && bar.QueryType == "0"
                    && string.Compare(bar.TradeDate, from) >= 0)
                .OrderByDescending(bar => bar.TradeDate).Take(limit).ToListAsync(token).ConfigureAwait(false);
            data = rows.Select(bar => Parse(bar.TradeDate, "000000", bar.Open, bar.High, bar.Low, bar.Close, bar.Volume));
        }
        else if (instrument.Market == "OFuture" && interval is 1 or 10)
        {
            var rows = await db.OFutureMinuteBars.AsNoTracking()
                .Where(bar => bar.Symbol == symbol && bar.IntervalMinutes == interval && string.Compare(bar.TradeDate, from) >= 0)
                .OrderByDescending(bar => bar.TradeDate).ThenByDescending(bar => bar.TradeTime)
                .Take(limit).ToListAsync(token).ConfigureAwait(false);
            data = rows.Select(bar => Parse(bar.TradeDate, bar.TradeTime, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume));
        }
        else throw new InvalidOperationException("지원하지 않는 시장 또는 봉 주기입니다.");

        // The newest stored bucket can still be updated by the live chart store.
        var bars = data.Skip(1).OfType<StrategyBar>().Where(bar => bar.IsValid)
            .OrderBy(bar => bar.Time).ToArray();
        if (bars.Length < StrategyEvaluationService.MinimumBars)
            throw new InvalidOperationException($"완료된 유효 봉이 {StrategyEvaluationService.MinimumBars}개 이상 필요합니다. (현재 {bars.Length}개)");
        return new(instrument, DateTime.UtcNow, interval, bars, options, championCode, championId);
    }

    public static string BuildPrompt(StrategySnapshot snapshot, StrategyMetrics championTraining)
    {
        var training = snapshot.Bars.Take(StrategyEvaluationService.TrainingEnd(snapshot.Bars.Length)).ToArray();
        var recent = training.TakeLast(48).ToArray();
        var data = JsonSerializer.Serialize(new
        {
            snapshot.CreatedAtUtc,
            snapshot.Instrument,
            snapshot.IntervalMinutes,
            SnapshotVersion = 1,
            PositionAtTrainingStart = 0,
            RecentTrainingBars = recent,
            MeanVolume = recent.Average(bar => (double)bar.Volume),
            MeanAbsoluteCloseChange = recent.Zip(recent.Skip(1), (a, b) => Math.Abs(b.Close - a.Close)).Average(),
            ChampionTraining = championTraining,
            Costs = snapshot.Instrument.Market == "Stock" ? snapshot.Options.StockCosts : snapshot.Options.Costs,
            snapshot.Options.MaxDrawdown,
            snapshot.Options.MinTradesPerWindow,
            snapshot.Options.MaxQuantity
        });
        return $"""
            Propose one deterministic strategy candidate for sandboxed {snapshot.Instrument.Market} simulation.
            Use the supplied data only and return the requested JSON. Code is a single expression without Markdown.
            Summary should briefly explain the change and rationale in Korean.
            {StrategyCompiler.Contract}
            Champion expression:
            {snapshot.ChampionCode}
            Training snapshot:
            {data}
            """;
    }

    internal static StrategyBar? Parse(string date, string time, double open, double high, double low, double close, long volume)
        => DateTime.TryParseExact(date + time, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var timestamp) ? new(timestamp, open, high, low, close, volume) : null;
}
