const int movingAverageDays = 20;

if (!Context.TryGetValue("ClosePrices", out var closePricesValue) ||
    closePricesValue is not Queue<long> closePrices)
{
    closePrices = new Queue<long>();
    Context["ClosePrices"] = closePrices;
}

var previousCount = closePrices.Count;
var previousAverage = previousCount == 0 ? 0 : closePrices.Average();
var previousClose = previousCount == 0 ? RealData.price : closePrices.Last();

closePrices.Enqueue(RealData.price);
while (closePrices.Count > movingAverageDays)
{
    closePrices.Dequeue();
}

if (closePrices.Count < movingAverageDays)
{
    return new ScriptResult(true, "HOLD", 0);
}

var currentAverage = closePrices.Average();
var hasPosition = Context.TryGetValue("HasPosition", out var positionValue) &&
    positionValue is bool position &&
    position;

var movedAboveAverage = RealData.price > currentAverage &&
    (previousCount < movingAverageDays || previousClose <= previousAverage);
var movedBelowAverage = RealData.price < currentAverage &&
    previousClose >= previousAverage;

if (!hasPosition && movedAboveAverage)
{
    return new ScriptResult(true, "BUY", 1);
}

if (hasPosition && movedBelowAverage)
{
    return new ScriptResult(true, "SELL", 1);
}

return new ScriptResult(true, "HOLD", 0);
