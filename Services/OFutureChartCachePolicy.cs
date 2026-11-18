using System.Globalization;
using DumbTrader.Models;

namespace DumbTrader.Services;

public static class OFutureChartCachePolicy
{
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LiveQuoteAge = TimeSpan.FromMinutes(10);

    public static DateTime? DailyRequestStart(DateTime? latest, DateTime start, DateTime today)
    {
        var expected = today.Date.AddDays(today.DayOfWeek switch
        {
            DayOfWeek.Saturday => -1,
            DayOfWeek.Sunday => -2,
            _ => 0
        });
        if (latest is null) return start.Date;
        if (latest.Value.Date >= expected) return null;
        return latest.Value.Date < start.Date ? start.Date : latest.Value.Date;
    }

    public static (DateTime EarliestMinute, int Count)? MinuteRequest(OFutureMinuteBar? latest,
        OFutureRealData? latestTrade, DateTime nowUtc, DateTime oldestAllowed)
    {
        var savedTime = ParseMinute(latest?.TradeDate, latest?.TradeTime);
        if (savedTime is null) return (oldestAllowed.Date, 2000);
        DateTime? liveTime = latestTrade is not null && nowUtc - latestTrade.ReceivedAtUtc <= LiveQuoteAge
            ? ParseMinute(latestTrade.OverseasDate, latestTrade.TradeTime) : null;

        if (liveTime is null && nowUtc - latest!.ReceivedAtUtc < RetryInterval) return null;
        if (liveTime is not null && liveTime.Value <= savedTime.Value.AddMinutes(1)) return null;

        var count = liveTime is not null && liveTime.Value.Date == savedTime.Value.Date
            && liveTime.Value - savedTime.Value <= TimeSpan.FromMinutes(45) ? 50 : 2000;
        return (savedTime.Value > oldestAllowed.Date ? savedTime.Value : oldestAllowed.Date, count);
    }

    public static DateTime? ParseDate(string? date)
        => DateTime.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed : null;

    private static DateTime? ParseMinute(string? date, string? time)
        => DateTime.TryParseExact(date + time, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.Date.AddHours(parsed.Hour).AddMinutes(parsed.Minute) : null;
}
