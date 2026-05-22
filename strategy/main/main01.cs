// 20일 이동평균 기반 단순 돌파/이탈 전략
const int movingAverageDays = 20;

// 최근 종가 큐를 컨텍스트에 유지
if (!Context.TryGetValue("ClosePrices", out var closePricesValue) ||
    closePricesValue is not Queue<long> closePrices)
{
    closePrices = new Queue<long>();
    Context["ClosePrices"] = closePrices;
}

var previousCount = closePrices.Count;
var previousAverage = previousCount == 0 ? 0 : closePrices.Average();
var previousClose = previousCount == 0 ? RealData.price : closePrices.Last();

// 최신 종가를 추가하고 20개만 유지
closePrices.Enqueue(RealData.price);
while (closePrices.Count > movingAverageDays)
{
    closePrices.Dequeue();
}

// 데이터가 충분하지 않으면 관망
if (closePrices.Count < movingAverageDays)
{
    return new ScriptResult(true, "HOLD", 0);
}

var currentAverage = closePrices.Average();
var hasPosition = Context.TryGetValue("HasPosition", out var positionValue) &&
    positionValue is bool position &&
    position;

// 가격이 이동평균선을 상향 돌파했는지 확인
var movedAboveAverage = RealData.price > currentAverage &&
    (previousCount < movingAverageDays || previousClose <= previousAverage);
// 가격이 이동평균선을 하향 돌파했는지 확인
var movedBelowAverage = RealData.price < currentAverage &&
    previousClose >= previousAverage;

// 미보유 상태에서 상향 돌파 시 매수
if (!hasPosition && movedAboveAverage)
{
    return new ScriptResult(true, "BUY", 1);
}

// 보유 상태에서 하향 이탈 시 매도
if (hasPosition && movedBelowAverage)
{
    return new ScriptResult(true, "SELL", 1);
}

// 그 외는 관망
return new ScriptResult(true, "HOLD", 0);
