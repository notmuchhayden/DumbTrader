using System.Collections.Concurrent;
using System.Globalization;
using DumbTrader.Models;

namespace DumbTrader.Services
{
    /// <summary>해외선물 체결(OVC)과 호가(OVH) 실시간 구독/최신 데이터 관리.</summary>
    public sealed class OFutureRealDataService
    {
        private readonly IXARealService _tradeFeed = new XARealService();
        private readonly IXARealService _orderBookFeed = new XARealService();
        private readonly ConcurrentDictionary<string, OFutureRealData> _latestTrades = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, OFutureOrderBook> _latestOrderBooks = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<string> Subscriptions => _subscriptions.Keys.ToArray();
        public event EventHandler<OFutureRealData>? RealDataUpdated;
        public event EventHandler<OFutureOrderBook>? OrderBookUpdated;

        public OFutureRealDataService()
        {
            LoadFeed(_tradeFeed, "Res\\OVC.res", ReceiveTradeData);
            LoadFeed(_orderBookFeed, "Res\\OVH.res", ReceiveOrderBookData);
        }

        public bool Subscribe(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return false;
            if (_subscriptions.ContainsKey(symbol)) return true;

            _tradeFeed.SetFieldData("InBlock", "symbol", symbol);
            _orderBookFeed.SetFieldData("InBlock", "symbol", symbol);
            _tradeFeed.AdviseRealData();
            _orderBookFeed.AdviseRealData();
            _subscriptions.TryAdd(symbol, 0);
            return true;
        }

        public bool Unsubscribe(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return false;
            _tradeFeed.UnadviseRealDataWithKey(symbol);
            _orderBookFeed.UnadviseRealDataWithKey(symbol);
            _subscriptions.TryRemove(symbol, out _);
            _latestTrades.TryRemove(symbol, out _);
            _latestOrderBooks.TryRemove(symbol, out _);
            return true;
        }

        public void UnsubscribeAll()
        {
            _tradeFeed.UnadviseRealData();
            _orderBookFeed.UnadviseRealData();
            _subscriptions.Clear();
            _latestTrades.Clear();
            _latestOrderBooks.Clear();
        }

        public OFutureRealData? GetLatestTrade(string symbol)
            => _latestTrades.TryGetValue(symbol, out var value) ? value : null;

        public OFutureOrderBook? GetLatestOrderBook(string symbol)
            => _latestOrderBooks.TryGetValue(symbol, out var value) ? value : null;

        private static void LoadFeed(IXARealService feed, string resourcePath, XA_DATASETLib._IXARealEvents_ReceiveRealDataEventHandler callback)
        {
            if (!feed.LoadFromResFile(resourcePath))
                throw new InvalidOperationException($"해외선물 실시간 리소스 로드 실패: {resourcePath}");
            feed.AddReceiveRealDataEventHandler(callback);
        }

        private void ReceiveTradeData(string trCode)
        {
            var data = new OFutureRealData
            {
                Symbol = Read(_tradeFeed, "symbol"),
                OverseasDate = Read(_tradeFeed, "ovsdate"),
                KoreaDate = Read(_tradeFeed, "kordate"),
                TradeTime = Read(_tradeFeed, "trdtm"),
                KoreaTradeTime = Read(_tradeFeed, "kortm"),
                Price = ReadDouble(_tradeFeed, "curpr"),
                Change = ReadDouble(_tradeFeed, "ydiffpr"),
                ChangeSign = Read(_tradeFeed, "ydiffSign"),
                ChangeRate = ReadDouble(_tradeFeed, "chgrate"),
                Open = ReadDouble(_tradeFeed, "open"),
                High = ReadDouble(_tradeFeed, "high"),
                Low = ReadDouble(_tradeFeed, "low"),
                TradeQuantity = ReadLong(_tradeFeed, "trdq"),
                TotalQuantity = ReadLong(_tradeFeed, "totq"),
                TradeDirection = Read(_tradeFeed, "cgubun"),
                SellTradeQuantity = ReadLong(_tradeFeed, "mdvolume"),
                BuyTradeQuantity = ReadLong(_tradeFeed, "msvolume")
            };
            if (data.Symbol.Length == 0) return;
            _latestTrades[data.Symbol] = data;
            RealDataUpdated?.Invoke(this, data);
        }

        private void ReceiveOrderBookData(string trCode)
        {
            var book = new OFutureOrderBook
            {
                Symbol = Read(_orderBookFeed, "symbol"),
                Time = Read(_orderBookFeed, "hotime"),
                TotalAskCount = ReadLong(_orderBookFeed, "totoffercnt"),
                TotalBidCount = ReadLong(_orderBookFeed, "totbidcnt"),
                TotalAskSize = ReadLong(_orderBookFeed, "totofferrem"),
                TotalBidSize = ReadLong(_orderBookFeed, "totbidrem")
            };
            if (book.Symbol.Length == 0) return;
            for (var level = 0; level < 5; level++)
            {
                var suffix = (level + 1).ToString(CultureInfo.InvariantCulture);
                book.AskPrices[level] = ReadDouble(_orderBookFeed, $"offerho{suffix}");
                book.BidPrices[level] = ReadDouble(_orderBookFeed, $"bidho{suffix}");
                book.AskSizes[level] = ReadLong(_orderBookFeed, $"offerrem{suffix}");
                book.BidSizes[level] = ReadLong(_orderBookFeed, $"bidrem{suffix}");
                book.AskCounts[level] = ReadLong(_orderBookFeed, $"offerno{suffix}");
                book.BidCounts[level] = ReadLong(_orderBookFeed, $"bidno{suffix}");
            }
            _latestOrderBooks[book.Symbol] = book;
            OrderBookUpdated?.Invoke(this, book);
        }

        private static string Read(IXARealService feed, string field)
            => feed.GetFieldData("OutBlock", field).Trim();
        private static double ReadDouble(IXARealService feed, string field)
            => double.TryParse(Read(feed, field), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0d;
        private static long ReadLong(IXARealService feed, string field)
            => long.TryParse(Read(feed, field), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0L;
    }
}
