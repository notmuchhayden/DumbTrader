using DumbTrader.Models;

namespace DumbTrader.Services;

public sealed class RiskGate
{
    private static readonly StrategySessionHours StockHours = new(new(9, 0), new(15, 30));

    public string? CheckQuote(StrategyEvolutionOptions options, DateTime quoteUtc, DateTime nowUtc, double price)
    {
        if (options.KillSwitch) return "킬 스위치가 켜져 있습니다.";
        if (!double.IsFinite(price)) return "유효하지 않은 시세입니다.";
        if (quoteUtc == default || quoteUtc > nowUtc.AddSeconds(5)
            || nowUtc - quoteUtc > TimeSpan.FromSeconds(options.MaxQuoteAgeSeconds)) return "시세 지연 또는 시각 이상";
        return null;
    }

    public string? CheckLimits(StrategyInstrument instrument, StrategyEvolutionOptions options,
        long position, double dailyPnl, double drawdown)
    {
        if (Math.Abs(position) > options.MaxQuantity) return "최대 보유 수량 초과";
        if (dailyPnl <= -options.DailyLossFor(instrument)) return "일일 손실 한도 초과";
        if (drawdown > options.MaxDrawdown) return "최대 낙폭 초과";
        return null;
    }

    public bool FeedTimedOut(StrategyInstrument instrument, StrategyEvolutionOptions options, DateTime nowUtc, DateTime lastQuoteUtc)
    {
        var hours = instrument.Market == "Stock" ? StockHours : options.FuturesSessionHoursKorea;
        if (hours is null || SessionStartUtc(hours, nowUtc) is not { } sessionOpenUtc) return false;
        return lastQuoteUtc >= sessionOpenUtc && nowUtc - lastQuoteUtc > TimeSpan.FromSeconds(options.MaxQuoteAgeSeconds);
    }

    public bool InConfiguredSession(StrategyEvolutionOptions options, DateTime quoteUtc)
        => options.FuturesSessionHoursKorea is not { } hours || SessionStartUtc(hours, quoteUtc) is not null;

    private static DateTime? SessionStartUtc(StrategySessionHours hours, DateTime nowUtc)
    {
        var local = nowUtc.AddHours(9);
        var time = TimeOnly.FromDateTime(local);
        var overnight = hours.Open > hours.Close;
        var inside = overnight ? time >= hours.Open || time < hours.Close : time >= hours.Open && time < hours.Close;
        if (!inside) return null;
        var openDay = overnight && time < hours.Close ? local.Date.AddDays(-1) : local.Date;
        if (openDay.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return null;
        return openDay.Add(hours.Open.ToTimeSpan()).AddHours(-9);
    }
}
