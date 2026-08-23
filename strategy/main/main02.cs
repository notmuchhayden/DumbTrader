// 연속 등락 + RSI + 이동평균 이격도 기반 단기 평균회귀 전략
// 같은 날짜의 실시간 체결은 하나의 일봉 종가로 갱신하며, 미래 데이터는 참조하지 않는다.
const int historyCapacity = 60;
const int minimumHistoryCount = 20;
const int rsiPeriod = 5;
const int movingAveragePeriod = 20;
const double signalThreshold = 55.0;

var periodKey = IsSimulation &&
                RealData.chetime?.Length == 8 &&
                RealData.chetime.All(char.IsDigit)
    ? RealData.chetime
    : DateTime.Today.ToString("yyyyMMdd");

// 최초 실행 시 현재 판단 시점보다 과거인 일봉만 적재한다.
if (!Context.TryGetValue("Main02ClosePrices", out var closePricesValue) ||
    closePricesValue is not List<long> closePrices)
{
    closePrices = DbContext.StockChartDatas
        .Where(x => x.shcode == Stock.shcode && string.Compare(x.date, periodKey) < 0)
        .OrderByDescending(x => x.date)
        .Take(historyCapacity)
        .Select(x => x.close)
        .ToList();
    closePrices.Reverse();

    Context["Main02ClosePrices"] = closePrices;
    Context.Remove("Main02LastPeriodKey");
}

var isSamePeriod = Context.TryGetValue("Main02LastPeriodKey", out var lastPeriodValue) &&
                   lastPeriodValue is string lastPeriodKey &&
                   lastPeriodKey == periodKey;

if (isSamePeriod && closePrices.Count > 0)
{
    // 실시간 체결이 여러 번 들어와도 오늘의 마지막 종가만 변경한다.
    closePrices[closePrices.Count - 1] = RealData.price;
}
else
{
    closePrices.Add(RealData.price);
    Context["Main02LastPeriodKey"] = periodKey;
}

while (closePrices.Count > historyCapacity)
{
    closePrices.RemoveAt(0);
}

if (RealData.price <= 0 || closePrices.Count < minimumHistoryCount)
{
    Context["Main02BuyScore"] = 0.0;
    Context["Main02SellScore"] = 0.0;
    return new ScriptResult(true, "HOLD", 0);
}

// 가장 최근 방향이 이어진 일수를 계산한다.
var upStreak = 0;
var downStreak = 0;
for (var i = closePrices.Count - 1; i > 0; i--)
{
    if (closePrices[i] > closePrices[i - 1])
    {
        if (downStreak > 0)
        {
            break;
        }

        upStreak++;
    }
    else if (closePrices[i] < closePrices[i - 1])
    {
        if (upStreak > 0)
        {
            break;
        }

        downStreak++;
    }
    else
    {
        break;
    }
}

// 최근 5개 변화량으로 단기 RSI를 계산한다.
var rsiChangeCount = Math.Min(rsiPeriod, closePrices.Count - 1);
double totalGain = 0;
double totalLoss = 0;
for (var i = closePrices.Count - rsiChangeCount; i < closePrices.Count; i++)
{
    var change = closePrices[i] - closePrices[i - 1];
    if (change > 0)
    {
        totalGain += change;
    }
    else
    {
        totalLoss -= change;
    }
}

double rsi;
if (totalGain == 0 && totalLoss == 0)
{
    rsi = 50;
}
else if (totalLoss == 0)
{
    rsi = 100;
}
else
{
    var relativeStrength = totalGain / totalLoss;
    rsi = 100.0 - (100.0 / (1.0 + relativeStrength));
}

// 최근 20일 평균으로부터 얼마나 벗어났는지 표준편차 단위로 계산한다.
var movingAverageValues = closePrices
    .Skip(Math.Max(0, closePrices.Count - movingAveragePeriod))
    .Select(x => (double)x)
    .ToList();
var movingAverage = movingAverageValues.Average();
var variance = movingAverageValues
    .Select(x => (x - movingAverage) * (x - movingAverage))
    .Average();
var standardDeviation = Math.Sqrt(variance);
var zScore = standardDeviation > 0
    ? (RealData.price - movingAverage) / standardDeviation
    : 0;

// 연속 일수 50점, RSI 25점, 평균 이격도 25점으로 각각 제한한다.
var buyStreakScore = Math.Min(50.0, downStreak * 15.0);
var sellStreakScore = Math.Min(50.0, upStreak * 15.0);
var buyRsiScore = rsi < 45
    ? Math.Min(25.0, (45.0 - rsi) / 45.0 * 25.0)
    : 0;
var sellRsiScore = rsi > 55
    ? Math.Min(25.0, (rsi - 55.0) / 45.0 * 25.0)
    : 0;
var buyDeviationScore = zScore < 0
    ? Math.Min(25.0, -zScore / 2.0 * 25.0)
    : 0;
var sellDeviationScore = zScore > 0
    ? Math.Min(25.0, zScore / 2.0 * 25.0)
    : 0;

var buyScore = Math.Min(100.0, buyStreakScore + buyRsiScore + buyDeviationScore);
var sellScore = Math.Min(100.0, sellStreakScore + sellRsiScore + sellDeviationScore);
Context["Main02BuyScore"] = buyScore;
Context["Main02SellScore"] = sellScore;
Context["Main02Rsi"] = rsi;
Context["Main02ZScore"] = zScore;
Context["Main02UpStreak"] = upStreak;
Context["Main02DownStreak"] = downStreak;

var hasPosition = Context.TryGetValue("HasPosition", out var positionValue) &&
                  positionValue is bool position &&
                  position;

var alreadySignaledToday = Context.TryGetValue("Main02LastSignalPeriod", out var signalPeriodValue) &&
                           signalPeriodValue is string signalPeriod &&
                           signalPeriod == periodKey;

// 하루에 한 번만 신규 주문 신호를 허용한다.
if (!alreadySignaledToday && !hasPosition && downStreak >= 2 && buyScore >= signalThreshold)
{
    Context["Main02LastSignalPeriod"] = periodKey;
    Context["Main02LastSignal"] = "BUY";
    Logging.Log($"평균회귀 매수 신호: {Stock.shcode}, 점수={buyScore:F1}, 연속하락={downStreak}, RSI={rsi:F1}, Z={zScore:F2}");
    return new ScriptResult(true, "BUY", 1);
}

if (!alreadySignaledToday && hasPosition && upStreak >= 2 && sellScore >= signalThreshold)
{
    Context["Main02LastSignalPeriod"] = periodKey;
    Context["Main02LastSignal"] = "SELL";
    Logging.Log($"평균회귀 매도 신호: {Stock.shcode}, 점수={sellScore:F1}, 연속상승={upStreak}, RSI={rsi:F1}, Z={zScore:F2}");
    return new ScriptResult(true, "SELL", 1);
}

return new ScriptResult(true, "HOLD", 0);
