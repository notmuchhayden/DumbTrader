using System.Globalization;
using DumbTrader.Models;

namespace DumbTrader.Services;

public static class StockOpeningRangeStrategy
{
    private const string StateKey = "StockOpeningRange";
    private static readonly TimeSpan Open = TimeSpan.FromHours(9);
    private static readonly TimeSpan LastEntry = new(14, 30, 0);
    private static readonly TimeSpan ExitTime = new(15, 15, 0);

    private sealed class Session
    {
        public DateTime Date;
        public DateTime LastTick;
        public long RangeHigh;
        public int Entries;
        public long EntryPrice;
        public long Peak;
    }

    public static ScriptResult Evaluate(StrategyGlobals globals, string symbol, int openingMinutes,
        double breakoutBuffer, double stopPercent, double targetPercent, double trailingPercent)
    {
        var trade = globals.RealData;
        if (globals.IsSimulation || trade.shcode != symbol || trade.price <= 0
            || !TimeSpan.TryParseExact(trade.chetime, @"hhmmss", CultureInfo.InvariantCulture, out var time))
            return new(true, "HOLD", 0);
        return EvaluateTick(globals.Context, DateTime.UtcNow.AddHours(9).Date + time, trade.price,
            trade.high, openingMinutes, breakoutBuffer, stopPercent, targetPercent, trailingPercent);
    }

    internal static ScriptResult EvaluateTick(Dictionary<string, object> context, DateTime time, long price, long sessionHigh,
        int openingMinutes, double breakoutBuffer, double stopPercent, double targetPercent, double trailingPercent)
    {
        var hold = new ScriptResult(true, "HOLD", 0);
        if (time.TimeOfDay < Open) return hold;
        if (!context.TryGetValue(StateKey, out var stored) || stored is not Session state)
            context[StateKey] = state = new Session { Date = time.Date };
        if (time < state.LastTick) return hold;
        state.LastTick = time;
        if (time.Date != state.Date)
        {
            state.Date = time.Date;
            state.RangeHigh = 0;
            state.Entries = 0;
        }

        if (time.TimeOfDay < Open.Add(TimeSpan.FromMinutes(openingMinutes)))
            state.RangeHigh = Math.Max(state.RangeHigh, Math.Max(price, sessionHigh));

        if (context.TryGetValue("HasPosition", out var value) && value is true)
        {
            if (state.EntryPrice <= 0) return hold;
            state.Peak = Math.Max(state.Peak, price);
            var exit = Math.Max(state.EntryPrice * (1 - stopPercent), state.Peak * (1 - trailingPercent));
            var target = state.EntryPrice * (1 + targetPercent);
            var sell = price <= exit || price >= target || time.TimeOfDay >= ExitTime;
            return new ScriptResult(true, sell ? "SELL" : "HOLD", sell ? 1 : 0)
            {
                StockPriceLevels = [new("Exit stop <=", exit), new("Profit target >=", target)]
            };
        }

        if (state.RangeHigh <= 0 || state.Entries >= 2 || time.TimeOfDay >= LastEntry
            || time.TimeOfDay < Open.Add(TimeSpan.FromMinutes(openingMinutes))) return hold;
        var entry = Math.Ceiling(state.RangeHigh * (1 + breakoutBuffer));
        var buy = price >= entry;
        if (buy)
        {
            state.Entries++;
            state.EntryPrice = state.Peak = price;
        }
        return new ScriptResult(true, buy ? "BUY" : "HOLD", buy ? 1 : 0)
        {
            StockPriceLevels = [new("Buy breakout >=", entry)]
        };
    }
}
