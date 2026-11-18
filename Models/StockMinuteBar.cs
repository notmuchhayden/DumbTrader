namespace DumbTrader.Models;

public sealed record StockMinuteBar(string Symbol, string TradeDate, string TradeTime,
    long Open, long High, long Low, long Close, long Volume, bool IsServer);
