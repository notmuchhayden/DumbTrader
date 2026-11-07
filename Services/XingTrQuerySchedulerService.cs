using System.Threading.Channels;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Windows.Threading;

namespace DumbTrader.Services
{
    /// <summary>주식·해외선물의 일반 TR 전송을 앱 전체 예산으로 제한한다.</summary>
    public sealed class XingTrQuerySchedulerService
    {
        public sealed class ServerRateCountersEventArgs(
            string trCode, int perSec, int baseSec, int requests, int limit) : EventArgs
        {
            public string TrCode { get; } = trCode;
            public int PerSec { get; } = perSec;
            public int BaseSec { get; } = baseSec;
            public int Requests { get; } = requests;
            public int Limit { get; } = limit;
        }

        private sealed record ServerRateCounters(int PerSec, int BaseSec, int Requests, int Limit);

        private sealed record ScheduledRequest(
            string TrCode,
            object QueryIdentity,
            Func<int> Send,
            Action? ResetQuery,
            Action<string>? OnRejected,
            CancellationToken CancellationToken,
            TimeSpan ResponseTimeout,
            Action<string>? OnStatusChanged);

        // Leave a small margin over the server's one-request-per-second boundary to avoid edge timing rejections.
        private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(1100);
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromSeconds(30);
        private const int WindowLimit = 200;

        private readonly Channel<ScheduledRequest> _queue = Channel.CreateUnbounded<ScheduledRequest>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private readonly Dictionary<string, ScheduledRequest> _inFlight = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConditionalWeakTable<object, RetiredMarker> _retiredQueries = new();
        private readonly object _stateLock = new();
        private readonly SemaphoreSlim _queueChanged = new(0);
        private readonly Queue<long> _sentAt = new();
        private readonly Dictionary<string, Queue<long>> _sentAtByTr = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dispatcher? _ownerDispatcher = Dispatcher.FromThread(Thread.CurrentThread);
        private readonly SynchronizationContext? _ownerContext = SynchronizationContext.Current;
        private long _lastSentTimestamp;
        private sealed class RetiredMarker { }

        public event EventHandler<string>? RequestRejected;
        public event EventHandler<ServerRateCountersEventArgs>? ServerRateCountersRead;

        public XingTrQuerySchedulerService() => _ = ProcessQueueAsync();

        public bool Enqueue(
            string trCode,
            object queryIdentity,
            Func<int> send,
            Action? resetQuery = null,
            Action<string>? onRejected = null,
            CancellationToken cancellationToken = default,
            TimeSpan? responseTimeout = null,
            Action<string>? onStatusChanged = null)
        {
            if (string.IsNullOrWhiteSpace(trCode) || queryIdentity is null || send is null)
                return false;

            var request = new ScheduledRequest(trCode, queryIdentity, send, resetQuery, onRejected,
                cancellationToken, responseTimeout ?? DefaultResponseTimeout, onStatusChanged);
            if (!_queue.Writer.TryWrite(request)) return false;
            NotifyStatus(request, "대기열 등록");
            _queueChanged.Release();
            return true;
        }

        /// <summary>해당 Query 인스턴스의 응답 처리가 끝났을 때 호출한다.</summary>
        public void Complete(string trCode, object queryIdentity)
        {
            lock (_stateLock)
            {
                if (_inFlight.TryGetValue(trCode, out var active) && ReferenceEquals(active.QueryIdentity, queryIdentity))
                    _inFlight.Remove(trCode);
            }
            _queueChanged.Release();
        }

        /// <summary>서버 오류 메시지로 요청 실패를 확정하고 Query 인스턴스를 격리한다.</summary>
        public void Fail(string trCode, object queryIdentity, string reason)
            => RetireAndRelease(trCode, queryIdentity, reason, resetQuery: true);

        private async Task ProcessQueueAsync()
        {
            var deferred = new Queue<ScheduledRequest>();
            while (true)
            {
                ScheduledRequest? request = null;
                while (request is null)
                {
                    while (_queue.Reader.TryRead(out var candidate))
                    {
                        if (candidate.CancellationToken.IsCancellationRequested)
                            continue;
                        if (IsRetired(candidate.QueryIdentity))
                        {
                            Reject(candidate, "대기 중 Query가 시간 초과 또는 서버 오류로 교체되었습니다.");
                            continue;
                        }
                        if (!IsInFlight(candidate.TrCode))
                        {
                            request = candidate;
                            break;
                        }
                        NotifyStatus(candidate, "같은 TR 응답 대기");
                        deferred.Enqueue(candidate);
                    }

                    if (request is null && deferred.Count > 0)
                    {
                        var deferredCount = deferred.Count;
                        while (deferredCount-- > 0)
                        {
                            var candidate = deferred.Dequeue();
                            if (candidate.CancellationToken.IsCancellationRequested)
                                continue;
                            if (IsRetired(candidate.QueryIdentity))
                            {
                                Reject(candidate, "대기 중 Query가 시간 초과 또는 서버 오류로 교체되었습니다.");
                                continue;
                            }
                            if (request is null && !IsInFlight(candidate.TrCode)) request = candidate;
                            else
                            {
                                NotifyStatus(candidate, "같은 TR 응답 대기");
                                deferred.Enqueue(candidate);
                            }
                        }
                    }

                    if (request is null)
                        await _queueChanged.WaitAsync();
                }

                ServerRateCounters? serverCounters;
                try
                {
                    serverCounters = await WaitForPermitAsync(request);
                }
                catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
                {
                    continue;
                }
                if (request.CancellationToken.IsCancellationRequested || IsRetired(request.QueryIdentity))
                    continue;

                lock (_stateLock)
                    _inFlight[request.TrCode] = request;

                if (request.CancellationToken.IsCancellationRequested)
                {
                    Complete(request.TrCode, request.QueryIdentity);
                    continue;
                }

                int result;
                var sendAttempted = false;
                try
                {
                    result = InvokeOnOwnerContext(() =>
                    {
                        if (request.CancellationToken.IsCancellationRequested) return -1;
                        sendAttempted = true;
                        NotifyStatus(request, serverCounters is null
                            ? "전송 중"
                            : $"전송 중 · 서버 TR 카운터 {serverCounters.PerSec}/{serverCounters.BaseSec}s, {serverCounters.Requests}/{serverCounters.Limit}");
                        if (serverCounters is not null)
                            NotifyServerRateCounters(request.TrCode, serverCounters);
                        // 호출 직전에 기록하므로 Dispatcher 대기 시간도 실제 요청 간격에 반영한다.
                        RecordSent(request.TrCode, Stopwatch.GetTimestamp());
                        return request.Send();
                    });
                }
                catch (Exception ex)
                {
                    RetireAndRelease(request.TrCode, request.QueryIdentity,
                        $"Request 호출 예외: {ex.Message}", resetQuery: true);
                    continue;
                }

                if (!sendAttempted)
                {
                    Complete(request.TrCode, request.QueryIdentity);
                    continue;
                }

                if (result < 0)
                {
                    Complete(request.TrCode, request.QueryIdentity);
                    var apiMessage = GetRequestErrorMessage(request.QueryIdentity, result);
                    var description = result == -21 ? "TR 쿼터 초과" : "요청 거부";
                    Reject(request, $"{request.TrCode} Request 반환값 {result} ({description}){apiMessage}");
                    continue;
                }

                _ = WatchResponseTimeoutAsync(request);
            }
        }

        private async Task WatchResponseTimeoutAsync(ScheduledRequest request)
        {
            if (request.ResponseTimeout == Timeout.InfiniteTimeSpan) return;
            await Task.Delay(request.ResponseTimeout);
            RetireAndRelease(request.TrCode, request.QueryIdentity,
                $"응답 시간 초과 ({request.ResponseTimeout.TotalSeconds:0}초)", resetQuery: true,
                expectedRequest: request);
        }

        private bool RetireAndRelease(string trCode, object queryIdentity, string reason, bool resetQuery,
            ScheduledRequest? expectedRequest = null)
        {
            ScheduledRequest? active;
            lock (_stateLock)
            {
                if (!_inFlight.TryGetValue(trCode, out active) || !ReferenceEquals(active.QueryIdentity, queryIdentity))
                    return false;
                // A Query instance may be reused after a successful response. An old timeout
                // must only retire the exact send that created its timer.
                if (expectedRequest is not null && !ReferenceEquals(active, expectedRequest))
                    return false;
                _inFlight.Remove(trCode);
                if (resetQuery && !_retiredQueries.TryGetValue(queryIdentity, out _))
                    _retiredQueries.Add(queryIdentity, new RetiredMarker());
            }

            if (resetQuery)
            {
                try { if (active!.ResetQuery is not null) InvokeOnOwnerContext(active.ResetQuery); }
                catch (Exception ex) { Console.WriteLine($"[{trCode} query reset] {ex}"); }
            }
            _queueChanged.Release();
            Reject(active!, reason);
            return true;
        }

        private T InvokeOnOwnerContext<T>(Func<T> callback)
        {
            if (_ownerDispatcher is not null)
                return _ownerDispatcher.CheckAccess() ? callback() : _ownerDispatcher.Invoke(callback);
            if (_ownerContext is null || SynchronizationContext.Current == _ownerContext) return callback();
            T result = default!;
            _ownerContext.Send(_ => result = callback(), null);
            return result;
        }

        private void InvokeOnOwnerContext(Action callback)
        {
            if (_ownerDispatcher is not null)
            {
                if (_ownerDispatcher.CheckAccess()) callback();
                else _ownerDispatcher.Invoke(callback);
                return;
            }
            if (_ownerContext is null || SynchronizationContext.Current == _ownerContext) callback();
            else _ownerContext.Send(_ => callback(), null);
        }

        private bool IsInFlight(string trCode)
        {
            lock (_stateLock) return _inFlight.ContainsKey(trCode);
        }

        private bool IsRetired(object queryIdentity)
        {
            lock (_stateLock) return _retiredQueries.TryGetValue(queryIdentity, out _);
        }

        private async Task<ServerRateCounters?> WaitForPermitAsync(ScheduledRequest request)
        {
            var notifiedWaiting = false;
            while (true)
            {
                request.CancellationToken.ThrowIfCancellationRequested();
                var now = Stopwatch.GetTimestamp();
                TimeSpan delay;
                lock (_sentAt)
                {
                    while (_sentAt.Count > 0 && Stopwatch.GetElapsedTime(_sentAt.Peek(), now) >= Window)
                        _sentAt.Dequeue();

                    var intervalDelay = _lastSentTimestamp == 0
                        ? TimeSpan.Zero
                        : MinimumInterval - Stopwatch.GetElapsedTime(_lastSentTimestamp, now);
                    var windowDelay = _sentAt.Count < WindowLimit
                        ? TimeSpan.Zero
                        : Window - Stopwatch.GetElapsedTime(_sentAt.Peek(), now);
                    delay = intervalDelay > windowDelay ? intervalDelay : windowDelay;
                }

                ServerRateCounters? serverCounters;
                try { serverCounters = InvokeOnOwnerContext(() => ReadServerRateCounters(request)); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{request.TrCode} TR counter dispatch] {ex.Message}");
                    serverCounters = null;
                }
                var perTrDelay = GetTrRateDelay(request.TrCode, serverCounters, now);
                if (perTrDelay > delay) delay = perTrDelay;
                var serverCountLimited = serverCounters is { Limit: > 0 } counters
                    && counters.Requests >= counters.Limit;
                if (serverCountLimited && delay < TimeSpan.FromSeconds(1))
                    delay = TimeSpan.FromSeconds(1);

                if (delay <= TimeSpan.Zero) return serverCounters;
                if (!notifiedWaiting)
                {
                    notifiedWaiting = true;
                    try { request.OnStatusChanged?.Invoke(serverCountLimited ? "서버 TR 한도 대기" : "호출 한도 대기"); }
                    catch (Exception ex) { Console.WriteLine($"[TR rate-limit status] {ex}"); }
                }
                await Task.Delay(delay, request.CancellationToken);
            }
        }

        private ServerRateCounters? ReadServerRateCounters(ScheduledRequest request)
        {
            if (request.QueryIdentity is not IXAQueryService query) return null;
            try
            {
                var counters = new ServerRateCounters(
                    query.GetTRCountPerSec(request.TrCode),
                    query.GetTRCountBaseSec(request.TrCode),
                    query.GetTRCountRequest(request.TrCode),
                    query.GetTRCountLimit(request.TrCode));
                return counters.PerSec >= 0 || counters.BaseSec >= 0
                    || counters.Requests >= 0 || counters.Limit >= 0 ? counters : null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{request.TrCode} TR limit counters] {ex.Message}");
                return null;
            }
        }

        private TimeSpan GetTrRateDelay(string trCode, ServerRateCounters? counters, long now)
        {
            if (counters is not { PerSec: > 0, BaseSec: > 0 }) return TimeSpan.Zero;
            lock (_stateLock)
            {
                if (!_sentAtByTr.TryGetValue(trCode, out var requests)) return TimeSpan.Zero;
                var interval = TimeSpan.FromSeconds(counters.BaseSec);
                while (requests.Count > 0 && Stopwatch.GetElapsedTime(requests.Peek(), now) >= interval)
                    requests.Dequeue();
                return requests.Count < counters.PerSec
                    ? TimeSpan.Zero
                    : interval - Stopwatch.GetElapsedTime(requests.Peek(), now);
            }
        }

        private void RecordSent(string trCode, long sentTimestamp)
        {
            lock (_sentAt)
            {
                _lastSentTimestamp = sentTimestamp;
                _sentAt.Enqueue(sentTimestamp);
            }
            lock (_stateLock)
            {
                if (!_sentAtByTr.TryGetValue(trCode, out var requests))
                    _sentAtByTr[trCode] = requests = new Queue<long>();
                requests.Enqueue(sentTimestamp);
            }
        }

        private void NotifyServerRateCounters(string trCode, ServerRateCounters counters)
        {
            try
            {
                ServerRateCountersRead?.Invoke(this, new ServerRateCountersEventArgs(
                    trCode, counters.PerSec, counters.BaseSec, counters.Requests, counters.Limit));
            }
            catch (Exception ex) { Console.WriteLine($"[{trCode} TR limit notification] {ex}"); }
        }

        private static string GetRequestErrorMessage(object queryIdentity, int errorCode)
        {
            if (queryIdentity is not IXAQueryService query) return string.Empty;
            try
            {
                var message = query.GetErrorMessage(errorCode);
                return string.IsNullOrWhiteSpace(message) ? string.Empty : $" · {message}";
            }
            catch { return string.Empty; }
        }

        private void Reject(ScheduledRequest request, string reason)
        {
            try { request.OnRejected?.Invoke(reason); }
            catch (Exception ex) { Console.WriteLine($"[{request.TrCode} request rejected] {ex}"); }
            try { RequestRejected?.Invoke(this, $"{request.TrCode}: {reason}"); }
            catch (Exception ex) { Console.WriteLine($"[{request.TrCode} scheduler notification] {ex}"); }
        }

        private static void NotifyStatus(ScheduledRequest request, string status)
        {
            try { request.OnStatusChanged?.Invoke(status); }
            catch (Exception ex) { Console.WriteLine($"[{request.TrCode} request status] {ex}"); }
        }
    }
}
