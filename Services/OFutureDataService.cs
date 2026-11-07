using System.Collections.Concurrent;
using System.Globalization;
using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services
{
    /// <summary>해외선물 종목/시세/차트 조회를 담당한다 (o3101, o3103, o3104, o3105, o3106, o3107, o3108).</summary>
    public sealed class OFutureDataService
    {
        private readonly Dictionary<string, IXAQueryService> _queries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _queryResources = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, XA_DATASETLib._IXAQueryEvents_ReceiveDataEventHandler> _queryHandlers = new(StringComparer.OrdinalIgnoreCase);
        private readonly DumbTraderDbContext _dbContext;
        private readonly XingTrQuerySchedulerService _queryScheduler;
        private readonly ConcurrentDictionary<string, IReadOnlyList<OFutureChartData>> _charts = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<(string Symbol, int IntervalMinutes), IReadOnlyList<OFutureMinuteChartData>> _minuteCharts = new();
        private readonly ConcurrentDictionary<(string Symbol, string QueryType), IReadOnlyList<OFutureDailyTradeData>> _dailyTrades = new();
        private readonly ConcurrentDictionary<(string Symbol, int IntervalMinutes), MinuteHistoryState> _minuteHistory = new();
        private MinuteHistoryState? _activeMinuteHistory;
        private long _activeChartRequestGeneration;
        private readonly object _watchlistQuotesLock = new();
        private IReadOnlyList<OFutureWatchlistQuote> _watchlistQuotes = Array.Empty<OFutureWatchlistQuote>();
        private readonly object _contractsLock = new();
        private readonly object _watchlistLock = new();
        private IReadOnlyList<OFutureContract> _contracts = Array.Empty<OFutureContract>();
        private readonly List<OFutureContract> _watchlist = new();
        private const int MaxMinutePages = 100;
        private const int MaxMinuteBars = 200_000;

        private sealed class MinuteHistoryState(string symbol, int intervalMinutes, DateTime earliestDate,
            CancellationToken cancellationToken, Action<string>? onStatusChanged, long requestGeneration)
        {
            public string Symbol { get; } = symbol;
            public int IntervalMinutes { get; } = intervalMinutes;
            public DateTime EarliestDate { get; } = earliestDate.Date;
            public CancellationToken CancellationToken { get; } = cancellationToken;
            public Action<string>? OnStatusChanged { get; } = onStatusChanged;
            public long RequestGeneration { get; } = requestGeneration;
            public int PageCount { get; set; }
            public HashSet<string> ContinuationKeys { get; } = new(StringComparer.Ordinal);
            public HashSet<(string Date, string Time)> BarKeys { get; } = new();
        }

        public event EventHandler? ContractListUpdated;
        public event EventHandler? WatchlistUpdated;
        public event EventHandler<OFutureContract>? ContractInfoUpdated;
        public event EventHandler<OFutureWatchlistQuotesEventArgs>? WatchlistQuotesUpdated;
        public event EventHandler<OFutureQuote>? QuoteUpdated;
        public event EventHandler<OFutureMinuteChartDataEventArgs>? MinuteChartDataUpdated;
        public event EventHandler<OFutureDailyTradeDataEventArgs>? DailyTradeDataUpdated;
        public event EventHandler<OFutureChartDataEventArgs>? ChartDataUpdated;
        public event EventHandler<OFutureRequestErrorEventArgs>? RequestFailed;

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

        public OFutureDataService(DumbTraderDbContext dbContext, XingTrQuerySchedulerService queryScheduler)
        {
            _dbContext = dbContext;
            _queryScheduler = queryScheduler;
            _watchlist.AddRange(_dbContext.OFutureContracts.AsNoTracking().ToList());

            AddQuery("o3101", "Res\\o3101.res", ReceiveContractList);
            AddQuery("o3103", "Res\\o3103.res", ReceiveMinuteChartData);
            AddQuery("o3104", "Res\\o3104.res", ReceiveDailyTradeData);
            AddQuery("o3105", "Res\\o3105.res", ReceiveContractInfo);
            AddQuery("o3106", "Res\\o3106.res", ReceiveQuote);
            AddQuery("o3107", "Res\\o3107.res", ReceiveWatchlistQuotes);
            AddQuery("o3108", "Res\\o3108.res", ReceiveChartData);
        }

        public IXAQueryService? GetQueryService(string trCode)
            => _queries.TryGetValue(trCode, out var query) ? query : null;

        /// <summary>해외선물 계약 목록 조회. gubun 값은 거래소 API의 구분코드를 그대로 전달한다.</summary>
        public bool RequestContractList(string gubun = "0")
        {
            var query = GetQueryService("o3101");
            if (query is null)
                return false;
            return EnqueueQuery("o3101", query, () =>
            {
                query.ClearBlockdata("o3101InBlock");
                query.SetFieldData("o3101InBlock", "gubun", 0, gubun);
                return query.Request(false);
            });
        }

        /// <summary>
        /// o3103 N분봉 조회. 최초 조회는 연속 키를 비워두고, 연속조회는 직전 응답의 두 키를 전달한다.
        /// 동일 TR의 다음 요청은 MinuteChartDataUpdated 응답 수신 후 실행한다.
        /// </summary>
        public bool RequestMinuteChartData(string symbol, int intervalMinutes = 1, int count = 2000,
            string continuationDate = "", string continuationTime = "", CancellationToken cancellationToken = default,
            DateTime? earliestDate = null, Action<string>? onStatusChanged = null, long requestGeneration = 0)
        {
            if (string.IsNullOrWhiteSpace(symbol) || symbol.Trim().Length > 8
                || intervalMinutes is < 1 or > 9999 || count is < 1 or > 9999)
                return false;
            if (continuationDate is null || continuationTime is null) return false;
            var isNext = continuationDate.Length > 0 || continuationTime.Length > 0;
            if (isNext && (!DateTime.TryParseExact(continuationDate, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _)
                || !DateTime.TryParseExact(continuationTime, "HHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _)))
                return false;

            var normalizedSymbol = symbol.Trim().ToUpperInvariant();
            var historyKey = (normalizedSymbol, intervalMinutes);
            var history = isNext
                ? _minuteHistory.GetOrAdd(historyKey, _ => new MinuteHistoryState(normalizedSymbol, intervalMinutes,
                    earliestDate ?? DateTime.Today.AddYears(-1), cancellationToken, onStatusChanged, requestGeneration))
                : new MinuteHistoryState(normalizedSymbol, intervalMinutes,
                    earliestDate ?? DateTime.Today.AddYears(-1), cancellationToken, onStatusChanged, requestGeneration);
            if (!isNext) _minuteHistory[historyKey] = history;
            if (cancellationToken.CanBeCanceled)
                cancellationToken.Register(() => RemoveMinuteHistoryIfCurrent(historyKey, history));

            var query = GetQueryService("o3103");
            if (query is null)
            {
                RemoveMinuteHistoryIfCurrent(historyKey, history);
                return false;
            }
            var enqueued = EnqueueQuery("o3103", query, () =>
            {
                _activeMinuteHistory = history;
                query.ClearBlockdata("o3103InBlock");
                query.SetFieldData("o3103InBlock", "shcode", 0, symbol.Trim());
                query.SetFieldData("o3103InBlock", "ncnt", 0, intervalMinutes.ToString(CultureInfo.InvariantCulture));
                query.SetFieldData("o3103InBlock", "readcnt", 0, count.ToString(CultureInfo.InvariantCulture));
                query.SetFieldData("o3103InBlock", "cts_date", 0, continuationDate);
                query.SetFieldData("o3103InBlock", "cts_time", 0, continuationTime);
                return query.Request(isNext);
            }, cancellationToken, reason =>
            {
                RemoveMinuteHistoryIfCurrent(historyKey, history);
                ReportRequestFailure("o3103", symbol, reason);
            }, onStatusChanged: history.OnStatusChanged,
                resetQuery: () => ReplaceQuery("o3103", query));
            if (!enqueued) RemoveMinuteHistoryIfCurrent(historyKey, history);
            return enqueued;
        }

        /// <summary>
        /// o3104 일별 체결 조회. gubun은 API의 조회구분 코드를 전달하고, date는 조회 기준일이다.
        /// 동일 TR의 다음 요청은 DailyTradeDataUpdated 응답 수신 후 실행한다.
        /// </summary>
        public bool RequestDailyTradeData(string symbol, string gubun, DateTime date, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol) || symbol.Trim().Length > 8
                || gubun is not ("0" or "1" or "2"))
                return false;
            var query = GetQueryService("o3104");
            if (query is null) return false;
            return EnqueueQuery("o3104", query, () =>
            {
                query.ClearBlockdata("o3104InBlock");
                query.SetFieldData("o3104InBlock", "gubun", 0, gubun);
                query.SetFieldData("o3104InBlock", "shcode", 0, symbol.Trim());
                query.SetFieldData("o3104InBlock", "date", 0, date.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                return query.Request(false);
            }, cancellationToken, reason => ReportRequestFailure("o3104", symbol, reason),
                resetQuery: () => ReplaceQuery("o3104", query));
        }

        /// <summary>해당 계약/분 주기에 대해 마지막으로 수신한 페이지를 반환한다.</summary>
        public IReadOnlyList<OFutureMinuteChartData> GetMinuteChartData(string symbol, int intervalMinutes = 1)
            => _minuteCharts.TryGetValue((symbol.Trim().ToUpperInvariant(), intervalMinutes), out var data)
                ? data.ToArray() : Array.Empty<OFutureMinuteChartData>();

        /// <summary>해당 계약에 대해 마지막으로 수신한 일별 체결 조회 결과를 반환한다.</summary>
        public IReadOnlyList<OFutureDailyTradeData> GetDailyTradeData(string symbol, string queryType = "0")
            => _dailyTrades.TryGetValue((symbol.Trim().ToUpperInvariant(), queryType), out var data)
                ? data.ToArray() : Array.Empty<OFutureDailyTradeData>();

        /// <summary>
        /// o3107 해외선물 관심종목 일괄 시세 조회. 생략하면 저장된 관심계약의 코드를 사용한다.
        /// 동일 TR의 다음 요청은 WatchlistQuotesUpdated 응답 수신 후 실행한다.
        /// </summary>
        public bool RequestWatchlistQuotes(IEnumerable<string>? symbols = null)
        {
            var requestedSymbols = (symbols ?? Watchlist.Select(contract => contract.Symbol)).ToArray();
            if (requestedSymbols.Length == 0
                || requestedSymbols.Any(symbol => string.IsNullOrWhiteSpace(symbol) || symbol.Trim().Length > 8))
                return false;
            var distinctSymbols = requestedSymbols.Select(symbol => symbol.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            var query = GetQueryService("o3107");
            if (query is null) return false;
            return EnqueueQuery("o3107", query, () =>
            {
                query.ClearBlockdata("o3107InBlock");
                query.SetBlockCount("o3107InBlock", distinctSymbols.Length);
                for (var i = 0; i < distinctSymbols.Length; i++)
                    query.SetFieldData("o3107InBlock", "symbol", i, distinctSymbols[i]);
                return query.Request(false);
            });
        }

        /// <summary>마지막으로 수신한 관심종목 일괄 시세 조회 결과를 반환한다.</summary>
        public IReadOnlyList<OFutureWatchlistQuote> GetWatchlistQuotes()
        {
            lock (_watchlistQuotesLock) return _watchlistQuotes.ToArray();
        }

        public bool RequestContractInfo(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return false;
            var query = GetQueryService("o3105");
            if (query is null) return false;
            return EnqueueQuery("o3105", query, () =>
            {
                query.ClearBlockdata("o3105InBlock");
                query.SetFieldData("o3105InBlock", "symbol", 0, symbol);
                return query.Request(false);
            });
        }

        public bool RequestQuote(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol)) return false;
            var query = GetQueryService("o3106");
            if (query is null) return false;
            return EnqueueQuery("o3106", query, () =>
            {
                query.ClearBlockdata("o3106InBlock");
                query.SetFieldData("o3106InBlock", "symbol", 0, symbol);
                return query.Request(false);
            });
        }

        /// <summary>
        /// o3108 일/주/월 차트 요청. periodCode는 리소스/API에서 정의한 주기 코드를 전달하며,
        /// 기본값 "0"는 일봉이다. 요청은 API 제한을 고려해 큐에서 순차 처리된다.
        /// </summary>
        public bool RequestChartData(string symbol, DateTime startDate, DateTime endDate, string periodCode = "0", int count = 2000,
            CancellationToken cancellationToken = default, Action<string>? onStatusChanged = null, long requestGeneration = 0)
        {
            if (string.IsNullOrWhiteSpace(symbol) || startDate > endDate || count is < 1 or > 9999
                || periodCode is not ("0" or "1" or "2"))
                return false;
            var query = GetQueryService("o3108");
            if (query is null) return false;
            return EnqueueQuery("o3108", query, () =>
            {
                _activeChartRequestGeneration = requestGeneration;
                query.ClearBlockdata("o3108InBlock");
                query.SetFieldData("o3108InBlock", "shcode", 0, symbol);
                query.SetFieldData("o3108InBlock", "gubun", 0, periodCode);
                query.SetFieldData("o3108InBlock", "qrycnt", 0, count.ToString(CultureInfo.InvariantCulture));
                query.SetFieldData("o3108InBlock", "sdate", 0, startDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                query.SetFieldData("o3108InBlock", "edate", 0, endDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                query.SetFieldData("o3108InBlock", "cts_date", 0, string.Empty);
                return query.Request(false);
            }, cancellationToken, reason => ReportRequestFailure("o3108", symbol, reason),
                resetQuery: () => ReplaceQuery("o3108", query), onStatusChanged: onStatusChanged);
        }

        public IReadOnlyList<OFutureChartData> GetChartData(string symbol)
            => _charts.TryGetValue(symbol, out var data) ? data.ToArray() : Array.Empty<OFutureChartData>();

        private void AddQuery(string trCode, string resourcePath, XA_DATASETLib._IXAQueryEvents_ReceiveDataEventHandler callback)
        {
            _queryResources[trCode] = resourcePath;
            _queryHandlers[trCode] = callback;
            _queries[trCode] = CreateQuery(trCode, resourcePath, callback);
        }

        private IXAQueryService CreateQuery(string trCode, string resourcePath,
            XA_DATASETLib._IXAQueryEvents_ReceiveDataEventHandler callback)
        {
            var query = new XAQueryService();
            if (!query.LoadFromResFile(resourcePath))
                throw new InvalidOperationException($"해외선물 TR 리소스 로드 실패: {resourcePath}");
            query.AddReceiveDataEventHandler(trCode =>
            {
                if (!ReferenceEquals(GetQueryService(trCode), query)) return;
                try
                {
                    callback(trCode);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{trCode} ReceiveData] {ex}");
                }
                finally
                {
                    _queryScheduler.Complete(trCode, query);
                }
            });
            query.AddReceiveMessageEventHandler((bool isSystemError, string messageCode, string message) =>
            {
                if (!ReferenceEquals(GetQueryService(trCode), query)) return;
                var detail = $"{messageCode}: {message}";
                if (isSystemError) _queryScheduler.Fail(trCode, query, detail);
            });
            return query;
        }

        private bool EnqueueQuery(string trCode, IXAQueryService query, Func<int> send,
            CancellationToken cancellationToken = default, Action<string>? onRejected = null,
            TimeSpan? responseTimeout = null, Action? resetQuery = null, Action<string>? onStatusChanged = null)
        {
            return _queryScheduler.Enqueue(trCode, query, send,
                resetQuery ?? (() => ReplaceQuery(trCode, query)),
                onRejected ?? (reason => ReportRequestFailure(trCode, string.Empty, reason)),
                cancellationToken, responseTimeout, onStatusChanged);
        }

        private void ReplaceQuery(string trCode, IXAQueryService expectedQuery)
        {
            if (!ReferenceEquals(GetQueryService(trCode), expectedQuery)) return;
            var replacement = CreateQuery(trCode, _queryResources[trCode], _queryHandlers[trCode]);
            _queries[trCode] = replacement;
            if (expectedQuery is IDisposable disposable) disposable.Dispose();
        }

        private void ReportRequestFailure(string trCode, string symbol, string reason)
            => RequestFailed?.Invoke(this, new OFutureRequestErrorEventArgs(trCode, symbol, reason));

        // 해외선물 종목마스터 조회
        private void ReceiveContractList(string trCode)
        {
            var query = GetQueryService("o3101");
            if (query is null)
                return;
            var contracts = new List<OFutureContract>();
            var count = Math.Max(0, query.GetBlockCount("o3101OutBlock"));
            for (var i = 0; i < count; i++)
                contracts.Add(ReadContract(query, "o3101OutBlock", i));
            lock (_contractsLock) _contracts = contracts;
            ContractListUpdated?.Invoke(this, EventArgs.Empty);
        }

        // 해외선물차트 (N분) 조회
        private void ReceiveMinuteChartData(string trCode)
        {
            var query = GetQueryService("o3103");
            if (query is null) return;
            var symbol = ReadString(query, "o3103OutBlock", "shcode");
            var intervalMinutes = (int)ReadLong(query, "o3103InBlock", "ncnt");
            if (string.IsNullOrWhiteSpace(symbol) || intervalMinutes <= 0)
            {
                ReportRequestFailure(trCode, symbol, "응답에 계약코드 또는 분 주기가 없습니다.");
                return;
            }

            var normalizedSymbol = symbol.Trim().ToUpperInvariant();
            var historyKey = (normalizedSymbol, intervalMinutes);
            var history = _activeMinuteHistory;
            if (history is null || !string.Equals(history.Symbol, normalizedSymbol, StringComparison.OrdinalIgnoreCase)
                || history.IntervalMinutes != intervalMinutes)
            {
                ReportRequestFailure(trCode, symbol, "응답과 현재 o3103 요청 컨텍스트가 일치하지 않습니다.");
                return;
            }
            var rawData = new List<OFutureMinuteChartData>();
            var count = Math.Max(0, query.GetBlockCount("o3103OutBlock1"));
            for (var i = 0; i < count; i++)
            {
                rawData.Add(new OFutureMinuteChartData
                {
                    Symbol = symbol,
                    IntervalMinutes = intervalMinutes,
                    Date = ReadString(query, "o3103OutBlock1", "date", i),
                    Time = ReadString(query, "o3103OutBlock1", "time", i),
                    Open = ReadDoubleOrNaN(query, "o3103OutBlock1", "open", i),
                    High = ReadDoubleOrNaN(query, "o3103OutBlock1", "high", i),
                    Low = ReadDoubleOrNaN(query, "o3103OutBlock1", "low", i),
                    Close = ReadDoubleOrNaN(query, "o3103OutBlock1", "close", i),
                    Volume = ReadLongOrSentinel(query, "o3103OutBlock1", "volume", i)
                });
            }
            history.PageCount++;
            var candidates = rawData.Where(item => IsValidMinuteChartData(item)
                    && DateTime.TryParseExact(item.Date, "yyyyMMdd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var tradeDate) && tradeDate.Date >= history.EarliestDate)
                .GroupBy(item => (item.Date, item.Time))
                .Select(group => group.Last())
                .ToArray();
            var acceptedBars = new List<OFutureMinuteChartData>(candidates.Length);
            var addedUniqueBar = false;
            var exceededBarLimit = false;
            foreach (var bar in candidates)
            {
                var key = (bar.Date, bar.Time);
                if (!history.BarKeys.Contains(key) && history.BarKeys.Count >= MaxMinuteBars)
                {
                    exceededBarLimit = true;
                    break;
                }

                addedUniqueBar |= history.BarKeys.Add(key);
                acceptedBars.Add(bar);
            }
            var data = acceptedBars.ToArray();

            var isNext = query.IsNext;
            var continuationDate = ReadString(query, "o3103OutBlock", "cts_date");
            var continuationTime = ReadString(query, "o3103OutBlock", "cts_time");
            var validContinuation = DateTime.TryParseExact(continuationDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                && DateTime.TryParseExact(continuationTime, "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
            var continuationKey = continuationDate + continuationTime;
            var repeatedContinuation = validContinuation && !history.ContinuationKeys.Add(continuationKey);
            var reachedEarliestDate = rawData.Any(item => DateTime.TryParseExact(item.Date, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var tradeDate) && tradeDate.Date <= history.EarliestDate);
            var isTruncated = false;
            var continuePaging = isNext && validContinuation && !repeatedContinuation && data.Length > 0 && addedUniqueBar
                && !reachedEarliestDate && history.PageCount < MaxMinutePages && history.BarKeys.Count < MaxMinuteBars
                && !history.CancellationToken.IsCancellationRequested;
            if (exceededBarLimit || (isNext && !continuePaging && !reachedEarliestDate
                && !history.CancellationToken.IsCancellationRequested))
                isTruncated = true;

            var statusMessage = continuePaging ? $"조회 중 · {history.PageCount}페이지"
                : isTruncated ? "부분 조회 · 빈/반복 페이지 또는 백필 상한에 도달했습니다."
                : data.Length == 0 ? "조회 완료 · 표시할 데이터가 없습니다."
                : "조회 완료";

            var minuteKey = (normalizedSymbol, intervalMinutes);
            var mergedData = _minuteCharts.TryGetValue(minuteKey, out var previous)
                ? previous.Concat(data)
                    .GroupBy(item => (item.Date, item.Time))
                    .Select(group => group.Last())
                    .OrderBy(item => item.Date, StringComparer.Ordinal)
                    .ThenBy(item => item.Time, StringComparer.Ordinal)
                    .ToArray()
                : data.ToArray();
            _minuteCharts[minuteKey] = mergedData;
            MinuteChartDataUpdated?.Invoke(this, new OFutureMinuteChartDataEventArgs
            {
                Symbol = symbol,
                IntervalMinutes = intervalMinutes,
                TimeDifference = ReadLong(query, "o3103OutBlock", "timediff"),
                ReadCount = ReadLong(query, "o3103OutBlock", "readcnt"),
                ContinuationDate = continuationDate,
                ContinuationTime = continuationTime,
                IsNext = continuePaging,
                PageNumber = history.PageCount,
                IsComplete = !continuePaging,
                IsTruncated = isTruncated,
                StatusMessage = statusMessage,
                RequestGeneration = history.RequestGeneration,
                Data = data
            });

            if (continuePaging)
            {
                if (!RequestMinuteChartData(symbol, intervalMinutes, 2000, continuationDate, continuationTime,
                        history.CancellationToken, history.EarliestDate, history.OnStatusChanged, history.RequestGeneration))
                    ReportRequestFailure("o3103", symbol, "다음 페이지 요청을 대기열에 넣지 못했습니다.");
            }
            // 첫 1분봉의 모든 연속 페이지를 받은 다음 동일 TR의 10분봉을 요청한다.
            else
            {
                RemoveMinuteHistoryIfCurrent(historyKey, history);
                if (intervalMinutes == 1 && !history.CancellationToken.IsCancellationRequested)
                    RequestMinuteChartData(symbol, 10, 2000, cancellationToken: history.CancellationToken,
                        earliestDate: history.EarliestDate, onStatusChanged: history.OnStatusChanged,
                        requestGeneration: history.RequestGeneration);
            }
        }

        private void RemoveMinuteHistoryIfCurrent((string Symbol, int IntervalMinutes) key, MinuteHistoryState expected)
        {
            if (_minuteHistory.TryGetValue(key, out var current) && ReferenceEquals(current, expected))
                ((ICollection<KeyValuePair<(string Symbol, int IntervalMinutes), MinuteHistoryState>>)_minuteHistory)
                    .Remove(new KeyValuePair<(string Symbol, int IntervalMinutes), MinuteHistoryState>(key, expected));
            if (ReferenceEquals(_activeMinuteHistory, expected)) _activeMinuteHistory = null;
        }

        // 해외선물 일별 체결 조회
        private void ReceiveDailyTradeData(string trCode)
        {
            var query = GetQueryService("o3104");
            if (query is null) return;
            // o3104는 응답에 계약코드가 없으므로 요청 입력 블록에서 조회 조건을 읽는다.
            var symbol = ReadString(query, "o3104InBlock", "shcode");
            var data = new List<OFutureDailyTradeData>();
            var count = Math.Max(0, query.GetBlockCount("o3104OutBlock1"));
            for (var i = 0; i < count; i++)
            {
                data.Add(new OFutureDailyTradeData
                {
                    Symbol = symbol,
                    Date = ReadString(query, "o3104OutBlock1", "chedate", i),
                    Price = ReadDoubleOrNaN(query, "o3104OutBlock1", "price", i),
                    Sign = ReadString(query, "o3104OutBlock1", "sign", i),
                    Change = ReadDoubleOrNaN(query, "o3104OutBlock1", "change", i),
                    ChangeRate = ReadDoubleOrNaN(query, "o3104OutBlock1", "diff", i),
                    Open = ReadDoubleOrNaN(query, "o3104OutBlock1", "open", i),
                    High = ReadDoubleOrNaN(query, "o3104OutBlock1", "high", i),
                    Low = ReadDoubleOrNaN(query, "o3104OutBlock1", "low", i),
                    TradeDirection = ReadString(query, "o3104OutBlock1", "cgubun", i),
                    Volume = ReadLongOrSentinel(query, "o3104OutBlock1", "volume", i)
                });
            }
            data = data.Where(item => DateTime.TryParseExact(item.Date, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _)
                && item.Volume >= 0 && double.IsFinite(item.Price) && double.IsFinite(item.Change)
                && double.IsFinite(item.ChangeRate) && double.IsFinite(item.Open)
                && double.IsFinite(item.High) && double.IsFinite(item.Low)).ToList();
            var queryType = ReadString(query, "o3104InBlock", "gubun");
            _dailyTrades[(symbol.Trim().ToUpperInvariant(), queryType)] = data;
            DailyTradeDataUpdated?.Invoke(this, new OFutureDailyTradeDataEventArgs
            {
                Symbol = symbol,
                QueryType = queryType,
                QueryDate = ReadString(query, "o3104InBlock", "date"),
                Data = data
            });
        }

        // 해외선물 종목 정보
        private void ReceiveContractInfo(string trCode)
        {
            var query = GetQueryService("o3105");
            if (query is null)
                return;
            var contract = ReadContract(query, "o3105OutBlock", 0);
            ContractInfoUpdated?.Invoke(this, contract);
        }

        // 해외선물 호가
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

        // 해외선물 관심종목 일괄 시세 조회
        private void ReceiveWatchlistQuotes(string trCode)
        {
            var query = GetQueryService("o3107");
            if (query is null) return;
            var quotes = new List<OFutureWatchlistQuote>();
            var count = Math.Max(0, query.GetBlockCount("o3107OutBlock"));
            for (var i = 0; i < count; i++)
            {
                quotes.Add(new OFutureWatchlistQuote
                {
                    Symbol = ReadString(query, "o3107OutBlock", "symbol", i),
                    Name = ReadString(query, "o3107OutBlock", "symbolname", i),
                    Price = ReadDouble(query, "o3107OutBlock", "price", i),
                    Sign = ReadString(query, "o3107OutBlock", "sign", i),
                    Change = ReadDouble(query, "o3107OutBlock", "change", i),
                    ChangeRate = ReadDouble(query, "o3107OutBlock", "diff", i),
                    Volume = ReadLong(query, "o3107OutBlock", "volume", i),
                    PreviousClose = ReadDouble(query, "o3107OutBlock", "jnilclose", i),
                    Open = ReadDouble(query, "o3107OutBlock", "open", i),
                    High = ReadDouble(query, "o3107OutBlock", "high", i),
                    Low = ReadDouble(query, "o3107OutBlock", "low", i),
                    AskPrice = ReadDouble(query, "o3107OutBlock", "offerho1", i),
                    BidPrice = ReadDouble(query, "o3107OutBlock", "bidho1", i),
                    AskCount = ReadLong(query, "o3107OutBlock", "offercnt1", i),
                    BidCount = ReadLong(query, "o3107OutBlock", "bidcnt1", i),
                    AskSize = ReadLong(query, "o3107OutBlock", "offerrem1", i),
                    BidSize = ReadLong(query, "o3107OutBlock", "bidrem1", i),
                    TotalAskCount = ReadLong(query, "o3107OutBlock", "offercnt", i),
                    TotalBidCount = ReadLong(query, "o3107OutBlock", "bidcnt", i),
                    TotalAskSize = ReadLong(query, "o3107OutBlock", "offer", i),
                    TotalBidSize = ReadLong(query, "o3107OutBlock", "bid", i)
                });
            }
            var data = quotes.ToArray();
            lock (_watchlistQuotesLock) _watchlistQuotes = data;
            WatchlistQuotesUpdated?.Invoke(this, new OFutureWatchlistQuotesEventArgs(data));
        }

        // 해외선물차트 (일주월) 조회
        private void ReceiveChartData(string trCode)
        {
            var query = GetQueryService("o3108");
            if (query is null)
                return;
            var symbol = ReadString(query, "o3108OutBlock", "shcode");
            var data = new List<OFutureChartData>();
            var count = Math.Max(0, query.GetBlockCount("o3108OutBlock1"));
            for (var i = 0; i < count; i++)
            {
                var bar = new OFutureChartData
                {
                    Symbol = symbol,
                    Date = query.GetFieldData("o3108OutBlock1", "date", i).Trim(),
                    Open = ReadDoubleOrNaN(query, "o3108OutBlock1", "open", i),
                    High = ReadDoubleOrNaN(query, "o3108OutBlock1", "high", i),
                    Low = ReadDoubleOrNaN(query, "o3108OutBlock1", "low", i),
                    Close = ReadDoubleOrNaN(query, "o3108OutBlock1", "close", i),
                    Volume = ReadLongOrSentinel(query, "o3108OutBlock1", "volume", i)
                };
                if (DateTime.TryParseExact(bar.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                    && bar.Volume >= 0 && double.IsFinite(bar.Open) && double.IsFinite(bar.High)
                    && double.IsFinite(bar.Low) && double.IsFinite(bar.Close)
                    && bar.High >= Math.Max(bar.Open, bar.Close) && bar.Low <= Math.Min(bar.Open, bar.Close))
                    data.Add(bar);
            }
            _charts[symbol] = data;
            ChartDataUpdated?.Invoke(this, new OFutureChartDataEventArgs(symbol, data)
            {
                QueryType = ReadString(query, "o3108InBlock", "gubun"),
                RequestGeneration = _activeChartRequestGeneration
            });
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
        private static bool IsValidMinuteChartData(OFutureMinuteChartData bar)
            => DateTime.TryParseExact(bar.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                && DateTime.TryParseExact(bar.Time, "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                && bar.Volume >= 0 && double.IsFinite(bar.Open) && double.IsFinite(bar.High)
                && double.IsFinite(bar.Low) && double.IsFinite(bar.Close)
                && bar.High >= Math.Max(bar.Open, bar.Close) && bar.Low <= Math.Min(bar.Open, bar.Close);
        private static double ReadDouble(IXAQueryService query, string block, string field, int index = 0)
            => ParseDouble(ReadString(query, block, field, index));
        private static long ReadLong(IXAQueryService query, string block, string field, int index = 0)
            => long.TryParse(ReadString(query, block, field, index), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
        private static double ReadDoubleOrNaN(IXAQueryService query, string block, string field, int index = 0)
            => double.TryParse(ReadString(query, block, field, index), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
                ? value : double.NaN;
        private static long ReadLongOrSentinel(IXAQueryService query, string block, string field, int index = 0)
            => long.TryParse(ReadString(query, block, field, index), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) && value >= 0
                ? value : -1;
        private static double ParseDouble(string value)
            => double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0d;
    }
}
