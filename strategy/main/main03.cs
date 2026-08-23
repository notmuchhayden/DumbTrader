// 기관·CTA에서 사용하는 추세추종(Time-Series Momentum) 계열 전략
// 200일 추세 필터 + 55일 Donchian 돌파 진입 + 20일 이탈/3 ATR 추적 손절
const int historyCapacity = 260;
const int fastMovingAverageDays = 50;
const int slowMovingAverageDays = 200;
const int entryBreakoutDays = 55;
const int exitBreakoutDays = 20;
const int atrDays = 20;
const double trailingAtrMultiple = 3.0;

var periodKey = IsSimulation &&
                RealData.chetime?.Length == 8 &&
                RealData.chetime.All(char.IsDigit)
    ? RealData.chetime
    : DateTime.Today.ToString("yyyyMMdd");

// 최초 실행 시 현재 판단 시점보다 과거인 일봉만 읽어 미래 데이터 참조를 방지한다.
if (!Context.TryGetValue("Main03DailyBars", out var dailyBarsValue) ||
    dailyBarsValue is not List<StockChartData> dailyBars)
{
    dailyBars = DbContext.StockChartDatas
        .Where(x => x.shcode == Stock.shcode && string.Compare(x.date, periodKey) < 0)
        .OrderByDescending(x => x.date)
        .Take(historyCapacity)
        .Select(x => new StockChartData
        {
            shcode = x.shcode,
            date = x.date,
            open = x.open,
            high = x.high,
            low = x.low,
            close = x.close,
            jdiff_vol = x.jdiff_vol,
            value = x.value,
            sign = x.sign
        })
        .ToList();
    dailyBars.Reverse();

    Context["Main03DailyBars"] = dailyBars;
    Context.Remove("Main03LastPeriodKey");
}

var currentPrice = RealData.price;
var currentOpen = RealData.open > 0 ? RealData.open : currentPrice;
var currentHigh = RealData.high > 0 ? RealData.high : currentPrice;
var currentLow = RealData.low > 0 ? RealData.low : currentPrice;
var currentBar = new StockChartData
{
    shcode = Stock.shcode,
    date = periodKey,
    open = currentOpen,
    high = Math.Max(currentHigh, currentPrice),
    low = Math.Min(currentLow, currentPrice),
    close = currentPrice,
    jdiff_vol = RealData.volume,
    value = RealData.value,
    sign = RealData.sign
};

var isSamePeriod = Context.TryGetValue("Main03LastPeriodKey", out var lastPeriodValue) &&
                   lastPeriodValue is string lastPeriodKey &&
                   lastPeriodKey == periodKey;

if (isSamePeriod && dailyBars.Count > 0)
{
    // 실시간 틱은 새로운 봉을 만들지 않고 오늘 일봉을 갱신한다.
    dailyBars[dailyBars.Count - 1] = currentBar;
}
else
{
    dailyBars.Add(currentBar);
    Context["Main03LastPeriodKey"] = periodKey;
}

while (dailyBars.Count > historyCapacity)
{
    dailyBars.RemoveAt(0);
}

if (currentPrice <= 0 || dailyBars.Count < slowMovingAverageDays)
{
    Context["Main03Ready"] = false;
    return new ScriptResult(true, "HOLD", 0);
}

Context["Main03Ready"] = true;

var fastAverage = dailyBars
    .Skip(dailyBars.Count - fastMovingAverageDays)
    .Average(x => (double)x.close);
var slowAverage = dailyBars
    .Skip(dailyBars.Count - slowMovingAverageDays)
    .Average(x => (double)x.close);

// 진입·청산 돌파선은 오늘 봉을 제외한 과거 데이터만 사용한다.
var priorBars = dailyBars.Take(dailyBars.Count - 1).ToList();
var entryHigh = priorBars
    .Skip(Math.Max(0, priorBars.Count - entryBreakoutDays))
    .Max(x => (double)x.high);
var exitLow = priorBars
    .Skip(Math.Max(0, priorBars.Count - exitBreakoutDays))
    .Min(x => (double)x.low);

// 갭을 반영한 Wilder True Range의 최근 20일 단순 평균으로 ATR을 계산한다.
var trueRanges = new List<double>();
var atrStartIndex = Math.Max(1, dailyBars.Count - atrDays);
for (var i = atrStartIndex; i < dailyBars.Count; i++)
{
    var highLow = (double)dailyBars[i].high - dailyBars[i].low;
    var highPreviousClose = Math.Abs(dailyBars[i].high - (double)dailyBars[i - 1].close);
    var lowPreviousClose = Math.Abs(dailyBars[i].low - (double)dailyBars[i - 1].close);
    trueRanges.Add(Math.Max(highLow, Math.Max(highPreviousClose, lowPreviousClose)));
}

var atr = trueRanges.Count > 0 ? trueRanges.Average() : 0;
var trendStrengthPercent = slowAverage > 0
    ? (fastAverage / slowAverage - 1.0) * 100.0
    : 0;

Context["Main03FastAverage"] = fastAverage;
Context["Main03SlowAverage"] = slowAverage;
Context["Main03EntryHigh"] = entryHigh;
Context["Main03ExitLow"] = exitLow;
Context["Main03Atr"] = atr;
Context["Main03TrendStrengthPercent"] = trendStrengthPercent;

var hasPosition = Context.TryGetValue("HasPosition", out var positionValue) &&
                  positionValue is bool position &&
                  position;
var alreadySignaledToday = Context.TryGetValue("Main03LastSignalPeriod", out var signalPeriodValue) &&
                           signalPeriodValue is string signalPeriod &&
                           signalPeriod == periodKey;

if (hasPosition)
{
    var highestClose = Context.TryGetValue("Main03HighestClose", out var highestCloseValue) &&
                       highestCloseValue is double savedHighestClose
        ? Math.Max(savedHighestClose, currentPrice)
        : currentPrice;
    Context["Main03HighestClose"] = highestClose;

    var trailingStop = highestClose - (trailingAtrMultiple * atr);
    Context["Main03TrailingStop"] = trailingStop;

    var brokeExitLow = currentPrice < exitLow;
    var brokeTrailingStop = atr > 0 && currentPrice < trailingStop;
    var lostLongTermTrend = fastAverage < slowAverage;

    if (!alreadySignaledToday && (brokeExitLow || brokeTrailingStop || lostLongTermTrend))
    {
        var exitReason = brokeTrailingStop
            ? "ATR 추적 손절"
            : brokeExitLow
                ? "20일 저가 이탈"
                : "장기 추세 하락 전환";

        Context["Main03LastSignalPeriod"] = periodKey;
        Context["Main03LastSignal"] = "SELL";
        Context["Main03LastExitReason"] = exitReason;
        Logging.Log($"추세추종 매도 신호: {Stock.shcode}, 사유={exitReason}, 가격={currentPrice}, ATR={atr:F1}");
        return new ScriptResult(true, "SELL", 1);
    }

    return new ScriptResult(true, "HOLD", 0);
}

Context.Remove("Main03HighestClose");
Context.Remove("Main03TrailingStop");

var longTermUptrend = fastAverage > slowAverage && currentPrice > slowAverage;
var brokeEntryHigh = currentPrice > entryHigh;

if (!alreadySignaledToday && longTermUptrend && brokeEntryHigh)
{
    Context["Main03LastSignalPeriod"] = periodKey;
    Context["Main03LastSignal"] = "BUY";
    Logging.Log($"추세추종 매수 신호: {Stock.shcode}, 가격={currentPrice}, 55일고가={entryHigh:F0}, 추세강도={trendStrengthPercent:F2}%");
    return new ScriptResult(true, "BUY", 1);
}

return new ScriptResult(true, "HOLD", 0);
