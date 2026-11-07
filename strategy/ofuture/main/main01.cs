// Sample only: 5/20 close moving-average crossover for the selected futures contract.
// This script returns signals; it never sends orders.
var prices = Context.TryGetValue("prices", out var stored)
    ? stored as List<double> ?? new List<double>()
    : new List<double>();

if (RealData.Price > 0 && double.IsFinite(RealData.Price))
{
    prices.Add(RealData.Price);
    if (prices.Count > 22)
        prices.RemoveAt(0);
}
Context["prices"] = prices;

if (prices.Count < 21)
    return new ScriptResult(true, "HOLD", 0);

var previousFast = prices.Skip(prices.Count - 6).Take(5).Average();
var previousSlow = prices.Skip(prices.Count - 21).Take(20).Average();
var currentFast = prices.Skip(prices.Count - 5).Average();
var currentSlow = prices.Skip(prices.Count - 20).Average();

if (previousFast <= previousSlow && currentFast > currentSlow)
    return new ScriptResult(true, "BUY", 1);
if (previousFast >= previousSlow && currentFast < currentSlow)
    return new ScriptResult(true, "SELL", 1);
return new ScriptResult(true, "HOLD", 0);
