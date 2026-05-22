Context["HasPosition"] = false;
Logging.Log($"SELL {Stock.shcode} at {RealData.price}");

return new ScriptResult(true, "SELL", 1);
