using System.Globalization;
using DumbTrader.Models;

namespace DumbTrader.Services
{
    /// <summary>Builds rolling one and ten minute bars from OVC trades before snapshot coalescing.</summary>
    public sealed class OFutureMinuteBarAggregator
    {
        public static IReadOnlyList<OFutureMinuteBar> AggregateTenMinuteBars(IEnumerable<OFutureMinuteBar> oneMinuteBars)
        {
            var minutes = oneMinuteBars
                .Where(bar => bar.IntervalMinutes == 1 && bar.Volume >= 0
                    && double.IsFinite(bar.Open) && double.IsFinite(bar.High)
                    && double.IsFinite(bar.Low) && double.IsFinite(bar.Close)
                    && bar.High >= Math.Max(bar.Open, bar.Close) && bar.Low <= Math.Min(bar.Open, bar.Close)
                    && DateTime.TryParseExact(bar.TradeDate + bar.TradeTime, "yyyyMMddHHmmss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                .Select(bar =>
                {
                    DateTime.TryParseExact(bar.TradeDate + bar.TradeTime, "yyyyMMddHHmmss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var tradeTime);
                    var bucket = new DateTime(tradeTime.Year, tradeTime.Month, tradeTime.Day,
                        tradeTime.Hour, tradeTime.Minute - tradeTime.Minute % 10, 0);
                    return (Bar: bar, TradeTime: tradeTime, Bucket: bucket);
                });

            return minutes
                .GroupBy(item => (item.Bar.Symbol, item.Bucket))
                .Select(group =>
                {
                    var ordered = group.OrderBy(item => item.TradeTime).ToArray();
                    return new OFutureMinuteBar
                    {
                        Symbol = group.Key.Symbol,
                        IntervalMinutes = 10,
                        TradeDate = group.Key.Bucket.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                        TradeTime = group.Key.Bucket.ToString("HHmmss", CultureInfo.InvariantCulture),
                        Open = ordered[0].Bar.Open,
                        High = ordered.Max(item => item.Bar.High),
                        Low = ordered.Min(item => item.Bar.Low),
                        Close = ordered[^1].Bar.Close,
                        Volume = ordered.Sum(item => item.Bar.Volume),
                        ReceivedAtUtc = ordered.Max(item => item.Bar.ReceivedAtUtc)
                    };
                })
                .OrderBy(bar => bar.Symbol, StringComparer.OrdinalIgnoreCase)
                .ThenBy(bar => bar.TradeDate, StringComparer.Ordinal)
                .ThenBy(bar => bar.TradeTime, StringComparer.Ordinal)
                .ToArray();
        }

        private sealed class MinuteState(DateTime start, OFutureRealData trade, long sequence, DateTime receivedAtUtc)
        {
            public DateTime Start { get; } = start;
            public DateTime OpenTime { get; private set; } = start.AddSeconds(ParseSecond(trade.TradeTime));
            public long OpenSequence { get; private set; } = sequence;
            public double Open { get; private set; } = trade.Price;
            public DateTime CloseTime { get; private set; } = start.AddSeconds(ParseSecond(trade.TradeTime));
            public long CloseSequence { get; private set; } = sequence;
            public double Close { get; private set; } = trade.Price;
            public double High { get; private set; } = trade.Price;
            public double Low { get; private set; } = trade.Price;
            public long Volume { get; private set; } = Math.Max(0, trade.TradeQuantity);
            public DateTime LastReceivedAtUtc { get; private set; } = receivedAtUtc;

            public void Add(OFutureRealData trade, long sequence, DateTime receivedAtUtc)
            {
                if (receivedAtUtc > LastReceivedAtUtc) LastReceivedAtUtc = receivedAtUtc;
                var time = Start.AddSeconds(ParseSecond(trade.TradeTime));
                if (time < OpenTime || (time == OpenTime && sequence < OpenSequence))
                {
                    OpenTime = time;
                    OpenSequence = sequence;
                    Open = trade.Price;
                }
                if (time > CloseTime || (time == CloseTime && sequence >= CloseSequence))
                {
                    CloseTime = time;
                    CloseSequence = sequence;
                    Close = trade.Price;
                }
                High = Math.Max(High, trade.Price);
                Low = Math.Min(Low, trade.Price);
                Volume += Math.Max(0, trade.TradeQuantity);
            }

            public OFutureMinuteBar ToBar(string symbol, int interval, DateTime receivedAt) => new()
            {
                Symbol = symbol,
                IntervalMinutes = interval,
                TradeDate = Start.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                TradeTime = Start.ToString("HHmmss", CultureInfo.InvariantCulture),
                Open = Open,
                High = High,
                Low = Low,
                Close = Close,
                Volume = Volume,
                ReceivedAtUtc = receivedAt
            };

            private static int ParseSecond(string value)
                => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var raw) ? raw % 100 : 0;
        }

        private sealed class SymbolBuffer
        {
            public readonly object Gate = new();
            // Keep source OVC events for the full rolling window so bars can be rebuilt after late ticks.
            public readonly Queue<(DateTime ReceivedUtc, OFutureRealData Trade)> RecentTrades = new();
            public readonly Dictionary<DateTime, MinuteState> Minutes = new();
            public DateTime LatestTradeTime;
            public long Sequence;
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SymbolBuffer> _buffers =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan Retention = TimeSpan.FromMinutes(10);

        public void RemoveSymbol(string symbol)
        {
            if (!string.IsNullOrWhiteSpace(symbol)) _buffers.TryRemove(symbol, out _);
        }

        public void Prune()
        {
            var cutoff = DateTime.UtcNow - Retention;
            foreach (var buffer in _buffers.Values)
            {
                lock (buffer.Gate)
                {
                    while (buffer.RecentTrades.TryPeek(out var oldest) && oldest.ReceivedUtc < cutoff)
                        buffer.RecentTrades.Dequeue();
                    foreach (var expired in buffer.Minutes.Where(pair => pair.Value.LastReceivedAtUtc < cutoff)
                        .Select(pair => pair.Key).ToArray())
                        buffer.Minutes.Remove(expired);
                }
            }
        }

        public IReadOnlyList<OFutureMinuteBar> Add(OFutureRealData trade)
        {
            if (string.IsNullOrWhiteSpace(trade.Symbol) || !double.IsFinite(trade.Price) ||
                !DateTime.TryParseExact(trade.OverseasDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                !DateTime.TryParseExact(trade.TradeTime, "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                return Array.Empty<OFutureMinuteBar>();

            var tradeAt = date.Date.Add(time.TimeOfDay);
            var receivedAt = trade.ReceivedAtUtc == default ? DateTime.UtcNow : trade.ReceivedAtUtc;
            if (receivedAt < DateTime.UtcNow - Retention) return Array.Empty<OFutureMinuteBar>();
            var buffer = _buffers.GetOrAdd(trade.Symbol, _ => new SymbolBuffer());
            lock (buffer.Gate)
            {
                buffer.RecentTrades.Enqueue((receivedAt, trade));
                var receivedCutoff = DateTime.UtcNow - Retention;
                while (buffer.RecentTrades.TryPeek(out var oldest) && oldest.ReceivedUtc < receivedCutoff)
                    buffer.RecentTrades.Dequeue();

                if (tradeAt > buffer.LatestTradeTime) buffer.LatestTradeTime = tradeAt;
                if (buffer.LatestTradeTime != default && tradeAt < buffer.LatestTradeTime - Retention)
                    return Array.Empty<OFutureMinuteBar>();

                var cutoff = buffer.LatestTradeTime - Retention;
                foreach (var oldMinute in buffer.Minutes.Keys.Where(key => key < cutoff).ToArray())
                    buffer.Minutes.Remove(oldMinute);

                var minuteStart = new DateTime(tradeAt.Year, tradeAt.Month, tradeAt.Day, tradeAt.Hour, tradeAt.Minute, 0);
                var sequence = ++buffer.Sequence;
                if (buffer.Minutes.TryGetValue(minuteStart, out var minuteState)) minuteState.Add(trade, sequence, receivedAt);
                else buffer.Minutes[minuteStart] = new MinuteState(minuteStart, trade, sequence, receivedAt);

                var receivedAtUtc = trade.ReceivedAtUtc == default ? DateTime.UtcNow : trade.ReceivedAtUtc;
                var bars = new List<OFutureMinuteBar>(2) { buffer.Minutes[minuteStart].ToBar(trade.Symbol, 1, receivedAtUtc) };

                var tenMinuteStart = minuteStart.AddMinutes(-(minuteStart.Minute % 10));
                var tenMinuteMinutes = buffer.Minutes.Where(pair => pair.Key >= tenMinuteStart && pair.Key < tenMinuteStart.AddMinutes(10))
                    .OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
                if (tenMinuteMinutes.Length > 0)
                {
                    var minuteBars = tenMinuteMinutes.Select(state => state.ToBar(trade.Symbol, 1, receivedAtUtc)).ToArray();
                    bars.Add(new OFutureMinuteBar
                    {
                        Symbol = trade.Symbol,
                        IntervalMinutes = 10,
                        TradeDate = tenMinuteStart.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                        TradeTime = tenMinuteStart.ToString("HHmmss", CultureInfo.InvariantCulture),
                        Open = minuteBars[0].Open,
                        High = minuteBars.Max(bar => bar.High),
                        Low = minuteBars.Min(bar => bar.Low),
                        Close = minuteBars[^1].Close,
                        Volume = minuteBars.Sum(bar => bar.Volume),
                        ReceivedAtUtc = receivedAtUtc
                    });
                }
                return bars;
            }
        }
    }
}
