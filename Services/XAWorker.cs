using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace DumbTrader.Services
{
    public class XAWorker
    {
        private static readonly Lazy<XAWorker> _instance = new Lazy<XAWorker>(() => new XAWorker());
        public static XAWorker Instance => _instance.Value;

        private readonly BlockingCollection<RequestItem> _requestQueue = new BlockingCollection<RequestItem>();
        private readonly Thread _workerThread;
        private volatile bool _running = true;
        private object _resultLock = new object();
        public object Result { get; private set; }

        private IXASessionService _sessionService;
        private IXAQueryService _queryService;
        private IXARealService _realService;

        private XAWorker()
        {
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                ApartmentState = ApartmentState.STA
            };
            _workerThread.Start();
        }

        public Task<object> Request(object requestData)
        {
            var tcs = new TaskCompletionSource<object>();
            _requestQueue.Add(new RequestItem { Data = requestData, CompletionSource = tcs });
            return tcs.Task;
        }

        private void WorkerLoop()
        {
            // STA 스레드에서 COM 객체 생성
            _sessionService = CreateXASessionService();
            _queryService = CreateXAQueryService();
            _realService = CreateXARealService();
            while (_running)
            {
                RequestItem item = null;
                try
                {
                    item = _requestQueue.Take();
                    // 실제 요청 처리 로직에 맞게 아래를 구현해야 함
                    // 예시: object result = _queryService.Request(...)
                    object result = null; // TODO: 실제 요청 처리 구현
                    lock (_resultLock)
                    {
                        Result = result;
                    }
                    item.CompletionSource.SetResult(result);
                }
                catch (Exception ex)
                {
                    // 예외 처리 및 Task 실패 알림
                    item?.CompletionSource.SetException(ex);
                }
            }
        }

        private IXAQueryService CreateXAQueryService()
        {
            return new XAQueryService();
        }

        private IXARealService CreateXARealService()
        {
            return new XARealService();
        }

        private IXASessionService CreateXASessionService()
        {
            return new XASessionService();
        }

        public IXASessionService SessionService => _sessionService;
        public IXAQueryService QueryService => _queryService;
        public IXARealService RealService => _realService;
    }

    public class RequestItem
    {
        public object Data { get; set; }
        public TaskCompletionSource<object> CompletionSource { get; set; }
    }
}
