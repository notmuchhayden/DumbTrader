// Steel Hot Rolled Coil(2026.11). Daily bar simulation, calibrated on 22 positive-volume training bars.
const int lookback = 5;
const int breakoutTicks = 1;
var hold = new ScriptResult(true, "HOLD", 0);

if (Contract.Symbol != "LHCX26" || CompletedBarIntervalMinutes != 0
    || RealData.TradeQuantity <= 0 || !double.IsFinite(RealData.Price) || RealData.Price <= 0)
    return hold;

if (!Context.TryGetValue("closes", out var stored) || stored is not Queue<double> closes)
{
    closes = new Queue<double>();
    Context["closes"] = closes;
}

var action = "HOLD";
if (closes.Count == lookback)
{
    var upper = closes.Max();
    var lower = closes.Min();
    var midpoint = (upper + lower) / 2;
    var buffer = breakoutTicks * Contract.TickSize;

    if (PositionSide == "FLAT")
    {
        if (RealData.Price >= upper + buffer)
            action = "BUY";
        else if (RealData.Price <= lower - buffer)
            action = "SELL";
    }
    else if (PositionSide == "LONG" && RealData.Price < midpoint)
        action = "SELL";
    else if (PositionSide == "SHORT" && RealData.Price > midpoint)
        action = "BUY";

    closes.Dequeue();
}
closes.Enqueue(RealData.Price);

var result = action == "HOLD" ? hold : new ScriptResult(true, action, 1);
if (closes.Count < lookback)
    return result;

// These levels are for the next completed bar, after the current close enters the window.
var nextUpper = closes.Max();
var nextLower = closes.Min();
var nextBuffer = breakoutTicks * Contract.TickSize;
return result with
{
    FuturePriceLevels = new OFuturePriceLevels(
        nextUpper + nextBuffer, nextLower - nextBuffer, (nextUpper + nextLower) / 2)
};
