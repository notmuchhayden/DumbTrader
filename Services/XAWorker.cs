using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace DumbTrader.Services
{
    // 서비스 선택자 enum 추가
    public enum XAServiceType
    {
        Session,
        Query,
        Real,
        None
    }

    public enum RequestType
    {
        MethodCall,
        Lambda
    }

    // RequestItem에 서비스 선택자 필드 추가
    public class RequestItem
    {
        public RequestType RequestType { get; set; }
        public XAServiceType? ServiceType { get; set; }
        public string FuncName { get; set; }
        public object[] Args { get; set; }
        public object CompletionSource { get; set; }
        public Func<IXASessionService, IXAQueryService, IXARealService, object> Action { get; set; }
    }

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

        // 서비스 선택자와 메서드 이름, 인수를 받는 버전
        public Task<TResult> Request<TResult>(XAServiceType serviceType, string methodName, params object[] args)
        {
            var tcs = new TaskCompletionSource<TResult>();
            _requestQueue.Add(new RequestItem
            {
                RequestType = RequestType.MethodCall,
                ServiceType = serviceType,
                Args = new object[] { methodName }.Concat(args).ToArray(),
                CompletionSource = tcs
            });
            return tcs.Task;
        }

        // 람다 버전
        public Task<TResult> Request<TResult>(Func<IXASessionService, IXAQueryService, IXARealService,  TResult> action)
        {
            var tcs = new TaskCompletionSource<TResult>();
            _requestQueue.Add(new RequestItem
            {
                RequestType = RequestType.Lambda,
                ServiceType = XAServiceType.None,
                FuncName = action.Method.Name,
                Args = null,
                CompletionSource = tcs 
            });
            return tcs.Task;
        }

        // 0개 파라미터
        public Task<TResult> Request<TResult>(XAServiceType serviceType, string methodName)
        {
            var tcs = new TaskCompletionSource<TResult>();
            _requestQueue.Add(new RequestItem { ServiceType = serviceType, Data = methodName, CompletionSource = tcs });
            return tcs.Task;
        }

        // 1개 파라미터
        public Task<TResult> Request<T1, TResult>(XAServiceType serviceType, string methodName, T1 arg1)
        {
            var tcs = new TaskCompletionSource<TResult>();
            _requestQueue.Add(new RequestItem { ServiceType = serviceType, Data = (methodName, new object[] { arg1 }), CompletionSource = tcs });
            return tcs.Task;
        }

        // 2개 파라미터
        public Task<TResult> Request<T1, T2, TResult>(T1 arg1, T2 arg2)
        {
            var tcs = new TaskCompletionSource<TResult>();
            _requestQueue.Add(new RequestItem { Data = new object[] { arg1, arg2 }, CompletionSource = tcs });
            return tcs.Task;
        }

        // 3개 파라미터
        public Task<TResult> Request<T1, T2, T3, TResult>(T1 arg1, T2 arg2, T3 arg3)
        {
            var tcs = new TaskCompletionSource<TResult>();
            _requestQueue.Add(new RequestItem { Data = new object[] { arg1, arg2, arg3 }, CompletionSource = tcs });
            return tcs.Task;
        }

        // 4개 파라미터
        public Task<TResult> Request<T1, T2, T3, T4, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4)
        {
            var tcs = new TaskCompletionSource<TResult>();
            _requestQueue.Add(new RequestItem { Data = new object[] { arg1, arg2, arg3, arg4 }, CompletionSource = tcs });
            return tcs.Task;
        }

        // 5개 파라미터
        public Task<TResult> Request<T1, T2, T3, T4, T5, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
        {
            var tcs = new TaskCompletionSource<TResult>();
            _requestQueue.Add(new RequestItem { Data = new object[] { arg1, arg2, arg3, arg4, arg5 }, CompletionSource = tcs });
            return tcs.Task;
        }

        private void WorkerLoop()
        {
            _sessionService = CreateXASessionService();
            _queryService = CreateXAQueryService();
            _realService = CreateXARealService();
            while (_running)
            {
                RequestItem item = null;
                try
                {
                    item = _requestQueue.Take();
                    object result = null;

                    if (item.Data is ValueTuple<string, object[]> tuple && item.ServiceType != null)
                    {
                        var (methodName, args) = tuple;
                        // 서비스 타입에 따라 분기
                        switch (item.ServiceType)
                        {
                            case XAServiceType.Session:
                                if (methodName == "Login" && args.Length == 5)
                                {
                                    result = _sessionService.Login(
                                        args[0] as string,
                                        args[1] as string,
                                        args[2] as string,
                                        (int)args[3],
                                        (bool)args[4]);
                                }
                                // 추가 메서드 분기 처리 가능
                                break;
                            case XAServiceType.Query:
                                // QueryService 관련 메서드 처리
                                break;
                            case XAServiceType.Real:
                                // RealService 관련 메서드 처리
                                break;
                        }
                    }
                    else if (item.Data is Delegate func)
                    {
                        // 람다 버전: Delegate로 받아서 DynamicInvoke 사용
                        result = func.DynamicInvoke(_sessionService, _queryService, _realService);
                    }

                    lock (_resultLock)
                    {
                        Result = result;
                    }

                    // 제네릭 TaskCompletionSource로 결과 설정
                    var tcsType = item.CompletionSource.GetType();
                    var setResultMethod = tcsType.GetMethod("SetResult");
                    setResultMethod?.Invoke(item.CompletionSource, new[] { result });
                }
                catch (Exception ex)
                {
                    var tcsType = item?.CompletionSource?.GetType();
                    var setExceptionMethod = tcsType?.GetMethod("SetException", new[] { typeof(Exception) });
                    setExceptionMethod?.Invoke(item.CompletionSource, new object[] { ex });
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

    
}
