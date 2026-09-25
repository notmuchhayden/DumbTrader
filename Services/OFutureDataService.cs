using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services
{
    /// <summary>해외선물 종목/시세/차트 조회를 담당한다 (o3101, o3105, o3106, o3108).</summary>
    public sealed class OFutureDataService
    {
        private sealed record ChartRequest(string Symbol, DateTime StartDate, DateTime EndDate, string PeriodCode, int Count);

        private readonly Dictionary<string, IXAQueryService> _queries = new(StringComparer.OrdinalIgnoreCase);
        private readonly DumbTraderDbContext _dbContext;
        private readonly Channel<ChartRequest> _chartQueue = Channel.CreateUnbounded<ChartRequest>();
        private readonly ConcurrentDictionary<string, IReadOnlyList<OFutureChartData>> _charts = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _contractsLock = new();
        private readonly object _watchlistLock = new();
        private IReadOnlyList<OFutureContract> _contracts = Array.Empty<OFutureContract>();
        private readonly List<OFutureContract> _watchlist = new();
        private DateTime _lastChartRequest = DateTime.MinValue;

        public event EventHandler? ContractListUpdated;
        public event EventHandler? WatchlistUpdated;
        public event EventHandler<OFutureContract>? ContractInfoUpdated;
        public event EventHandler<OFutureQuote>? QuoteUpdated;
        public event EventHandler<OFutureChartDataEventArgs>? ChartDataUpdated;

        public IReadOnlyList<OFutureContract> Contracts
        {
            get { lock (_contractsLock) return _contracts.ToArray(); }
        }

        public IReadOnlyList<OFutureContract> Watchlist
        {
            get { lock (_watchlistLock) return _watchlist.ToArray(); }
        }

        public bool AddToWatchlist(OFutureContract contract)
        {
            ArgumentNullException.ThrowIfNull(contract);
            if (string.IsNullOrWhiteSpace(contract.Symbol)) return false;
            lock (_watchlistLock)
            {
                if (_watchlist.Any(item => string.Equals(item.Symbol, contract.Symbol, StringComparison.OrdinalIgnoreCase)))
                    return false;

                if (_dbContext.OFutureContracts.Find(contract.Symbol) is not null)
                    return false;

                _dbContext.OFutureContracts.Add(contract);
                _dbContext.SaveChanges();
                _watchlist.Add(contract);
            }
            WatchlistUpdated?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool RemoveFromWatchlist(string symbol)
        {
            bool removed;
            lock (_watchlistLock)
            {
                var item = _watchlist.FirstOrDefault(contract => string.Equals(contract.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
                var storedContract = item is null ? null : _dbContext.OFutureContracts.Find(item.Symbol);
                if (item is not null && storedContract is not null)
                {
                    _dbContext.OFutureContracts.Remove(storedContract);
                    _dbContext.SaveChanges();
                    removed = _watchlist.Remove(item);
                }
                else
                {
                    removed = false;
                }
            }
            if (removed) WatchlistUpdated?.Invoke(this, EventArgs.Empty);
            return removed;
        }

        public OFutureDataService(DumbTraderDbContext dbContext)
        {
            _dbContext = dbContext;
            _watchlist.AddRange(_dbContext.OFutureContracts.AsNoTracking().ToList());

            AddQuery("o3101", "Res\\o3101.res", ReceiveContractList);
            AddQuery("o3105", "Res\\o3105.res", ReceiveContractInfo);
            AddQuery("o3106", "Res\\o3106.res", ReceiveQuote);
            AddQuery("o3108", "Res\\o3108.res", ReceiveChartData);
            _ = Task.Run(ProcessChartQueueAsync);
        }

        public IXAQueryService? GetQueryService(string trCode)
            => _queries.TryGetValue(trCode, out var query) ? query : null;

        /// <summary>해외선물 계약 목록 조회. gubun 값은 거래소 API의 구분코드를 그대로 전달한다.</summary>
        public bool RequestContractList(string gubun = "0")
        {
            var query = GetQueryService("o3101");
            if (query is null) return false;
            query.ClearBlockdata("o3101InBlock");
            query.SetFieldData("o3101InBlock", "gubun", 0, gubun);
            return query.Request(false) >= 0;
        }

        public bool RequestContractInfo(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return false;
            var query = GetQueryService("o3105");
            if (query is null) return false;
            query.ClearBlockdata("o3105InBlock");
            query.SetFieldData("o3105InBlock", "symbol", 0, symbol);
            return query.Request(false) >= 0;
        }

        public bool RequestQuote(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return false;
            var query = GetQueryService("o3106");
            if (query is null) return false;
            query.ClearBlockdata("o3106InBlock");
            query.SetFieldData("o3106InBlock", "symbol", 0, symbol);
            return query.Request(false) >= 0;
        }

        /// <summary>
        /// o3108 일/주/월 차트 요청. periodCode는 리소스/API에서 정의한 주기 코드를 전달하며,
        /// 기본값 "0"는 일봉이다. 요청은 API 제한을 고려해 큐에서 순차 처리된다.
        /// </summary>
        public bool RequestChartData(string symbol, DateTime startDate, DateTime endDate, string periodCode = "0", int count = 2000)
        {
            if (string.IsNullOrWhiteSpace(symbol) || startDate > endDate || count <= 0) return false;
            return _chartQueue.Writer.TryWrite(new ChartRequest(symbol, startDate, endDate, periodCode, count));
        }

        public IReadOnlyList<OFutureChartData> GetChartData(string symbol)
            => _charts.TryGetValue(symbol, out var data) ? data.ToArray() : Array.Empty<OFutureChartData>();

        private void AddQuery(string trCode, string resourcePath, XA_DATASETLib._IXAQueryEvents_ReceiveDataEventHandler callback)
        {
            var query = new XAQueryService();
            if (!query.LoadFromResFile(resourcePath))
                throw new InvalidOperationException($"해외선물 TR 리소스 로드 실패: {resourcePath}");
            query.AddReceiveDataEventHandler(callback);
            _queries.Add(trCode, query);
        }

        private async Task ProcessChartQueueAsync()
        {
            await foreach (var request in _chartQueue.Reader.ReadAllAsync())
            {
                var elapsed = DateTime.Now - _lastChartRequest;
                if (elapsed < TimeSpan.FromSeconds(1))
                    await Task.Delay(TimeSpan.FromSeconds(1) - elapsed);

                var query = GetQueryService("o3108");
                if (query is null) continue;
                query.ClearBlockdata("o3108InBlock");
                query.SetFieldData("o3108InBlock", "shcode", 0, request.Symbol);
                query.SetFieldData("o3108InBlock", "gubun", 0, request.PeriodCode);
                query.SetFieldData("o3108InBlock", "qrycnt", 0, request.Count.ToString(CultureInfo.InvariantCulture));
                query.SetFieldData("o3108InBlock", "sdate", 0, request.StartDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                query.SetFieldData("o3108InBlock", "edate", 0, request.EndDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                query.SetFieldData("o3108InBlock", "cts_date", 0, string.Empty);
                if (query.Request(false) >= 0)
                    _lastChartRequest = DateTime.Now;
            }
        }

        private void ReceiveContractList(string trCode)
        {
            var query = GetQueryService("o3101");
            if (query is null) return;
            var contracts = new List<OFutureContract>();
            var count = Math.Max(0, query.GetBlockCount("o3101OutBlock"));
            for (var i = 0; i < count; i++)
                contracts.Add(ReadContract(query, "o3101OutBlock", i));
            lock (_contractsLock) _contracts = contracts;
            ContractListUpdated?.Invoke(this, EventArgs.Empty);
        }

        private void ReceiveContractInfo(string trCode)
        {
            var query = GetQueryService("o3105");
            if (query is null) return;
            var contract = ReadContract(query, "o3105OutBlock", 0);
            ContractInfoUpdated?.Invoke(this, contract);
        }

        private void ReceiveQuote(string trCode)
        {
            var query = GetQueryService("o3106");
            if (query is null) return;
            var quote = new OFutureQuote
            {
                Symbol = ReadString(query, "o3106OutBlock", "symbol"),
                Name = ReadString(query, "o3106OutBlock", "symbolname"),
                Price = ReadDouble(query, "o3106OutBlock", "price"),
                Sign = ReadString(query, "o3106OutBlock", "sign"),
                Change = ReadDouble(query, "o3106OutBlock", "change"),
                ChangeRate = ReadDouble(query, "o3106OutBlock", "diff"),
                Volume = ReadDouble(query, "o3106OutBlock", "volume"),
                PreviousClose = ReadDouble(query, "o3106OutBlock", "jnilclose"),
                Open = ReadDouble(query, "o3106OutBlock", "open"),
                High = ReadDouble(query, "o3106OutBlock", "high"),
                Low = ReadDouble(query, "o3106OutBlock", "low"),
                QuoteTime = ReadString(query, "o3106OutBlock", "hotime"),
                TotalAskCount = ReadLong(query, "o3106OutBlock", "offercnt"),
                TotalBidCount = ReadLong(query, "o3106OutBlock", "bidcnt"),
                TotalAskSize = ReadLong(query, "o3106OutBlock", "offer"),
                TotalBidSize = ReadLong(query, "o3106OutBlock", "bid")
            };
            for (var level = 0; level < 5; level++)
            {
                var suffix = (level + 1).ToString(CultureInfo.InvariantCulture);
                quote.AskPrices[level] = ReadDouble(query, "o3106OutBlock", $"offerho{suffix}");
                quote.BidPrices[level] = ReadDouble(query, "o3106OutBlock", $"bidho{suffix}");
                quote.AskSizes[level] = ReadLong(query, "o3106OutBlock", $"offerrem{suffix}");
                quote.BidSizes[level] = ReadLong(query, "o3106OutBlock", $"bidrem{suffix}");
            }
            QuoteUpdated?.Invoke(this, quote);
        }

        private void ReceiveChartData(string trCode)
        {
            var query = GetQueryService("o3108");
            if (query is null) return;
            var symbol = ReadString(query, "o3108OutBlock", "shcode");
            var data = new List<OFutureChartData>();
            var count = Math.Max(0, query.GetBlockCount("o3108OutBlock1"));
            for (var i = 0; i < count; i++)
            {
                data.Add(new OFutureChartData
                {
                    Symbol = symbol,
                    Date = query.GetFieldData("o3108OutBlock1", "date", i).Trim(),
                    Open = ReadDouble(query, "o3108OutBlock1", "open", i),
                    High = ReadDouble(query, "o3108OutBlock1", "high", i),
                    Low = ReadDouble(query, "o3108OutBlock1", "low", i),
                    Close = ReadDouble(query, "o3108OutBlock1", "close", i),
                    Volume = ReadLong(query, "o3108OutBlock1", "volume", i)
                });
            }
            _charts[symbol] = data;
            ChartDataUpdated?.Invoke(this, new OFutureChartDataEventArgs(symbol, data));
        }

        private static OFutureContract ReadContract(IXAQueryService query, string block, int index)
        {
            string Field(string name) => query.GetFieldData(block, name, index).Trim();
            var contract = new OFutureContract
            {
                Symbol = Field("Symbol"),
                Name = Field("SymbolNm"),
                BaseProductCode = Field("BscGdsCd"),
                BaseProductName = Field("BscGdsNm"),
                ExchangeCode = Field("ExchCd"),
                ExchangeName = Field("ExchNm"),
                CurrencyCode = Field("CrncyCd"),
                ProductCode = Field("GdsCd"),
                MaturityYear = Field("LstngYr"),
                MaturityMonth = Field("LstngM"),
                ApplicationDate = Field("ApplDate"),
                TickSize = ParseDouble(Field("UntPrc")),
                TickValue = ParseDouble(Field("MnChgAmt")),
                ContractValue = ParseDouble(Field("CtrtPrAmt")),
                OpeningMargin = ParseDouble(Field("OpngMgn")),
                MaintenanceMargin = ParseDouble(Field("MntncMgn"))
            };
            return contract;
        }

        private static string ReadString(IXAQueryService query, string block, string field, int index = 0)
            => query.GetFieldData(block, field, index).Trim();
        private static double ReadDouble(IXAQueryService query, string block, string field, int index = 0)
            => ParseDouble(ReadString(query, block, field, index));
        private static long ReadLong(IXAQueryService query, string block, string field, int index = 0)
            => long.TryParse(ReadString(query, block, field, index), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
        private static double ParseDouble(string value)
            => double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0d;
    }
}
