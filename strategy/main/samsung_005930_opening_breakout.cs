// KRX intraday forward test: 5-minute opening range, 0.10% breakout buffer.
if (Stock.shcode != "005930")
    return new ScriptResult(true, "HOLD", 0);
return DumbTrader.Services.StockOpeningRangeStrategy.Evaluate(
    new DumbTrader.Services.StrategyGlobals { RealData = RealData, IsSimulation = IsSimulation, Context = Context },
    "005930", 5, 0.001, 0.012, 0.024, 0.014);
