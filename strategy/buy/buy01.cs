Context["HasPosition"] = true;
Logging.Log($"BUY {Stock.shcode} at {RealData.price}");

return new ScriptResult(true, "BUY", 1);
