// KRX intraday forward test: 10-minute opening range with a wider exit.
if (Stock.shcode != "000660")
    return new ScriptResult(true, "HOLD", 0);
return DumbTrader.Services.StockOpeningRangeStrategy.Evaluate(
    new DumbTrader.Services.StrategyGlobals { RealData = RealData, IsSimulation = IsSimulation, Context = Context },
    "000660", 10, 0.002, 0.017, 0.034, 0.019);
