Context["HasPosition"] = false;
Logging.Log($"전략 내부 매도 · {Stock.shcode} · 기준가 {RealData.price:N0} · 주문 미전송");

return new ScriptResult(true, "SELL", 1);
