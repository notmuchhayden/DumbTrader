namespace DumbTrader.Services;

internal sealed class StockStrategyWorkQueue(Action<string> log)
{
    private sealed class SymbolQueue
    {
        public Queue<Action> Pending { get; } = new();
        public bool OverloadReported { get; set; }
    }

    private readonly Dictionary<string, SymbolQueue> _workers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _slots = new(2);
    private readonly object _gate = new();

    public void Enqueue(string symbol, Action evaluate)
    {
        lock (_gate)
        {
            if (!_workers.TryGetValue(symbol, out var worker))
            {
                _workers[symbol] = worker = new();
                _ = Task.Run(() => RunAsync(symbol, worker));
            }
            if (worker.Pending.Count >= 256)
            {
                if (!worker.OverloadReported)
                    log($"[주식 전략] {symbol} · 평가 대기열이 가득 차 새 틱을 건너뜁니다.");
                worker.OverloadReported = true;
                return;
            }
            worker.Pending.Enqueue(evaluate);
        }
    }

    public void ClearPending()
    {
        lock (_gate)
            foreach (var worker in _workers.Values) worker.Pending.Clear();
    }

    private async Task RunAsync(string symbol, SymbolQueue worker)
    {
        while (true)
        {
            await _slots.WaitAsync().ConfigureAwait(false);
            try
            {
                Action evaluate;
                lock (_gate)
                {
                    if (!worker.Pending.TryDequeue(out var work))
                    {
                        _workers.Remove(symbol);
                        return;
                    }
                    evaluate = work;
                    if (worker.Pending.Count < 128) worker.OverloadReported = false;
                }
                try { evaluate(); }
                catch (Exception ex) { log($"[주식 전략 작업 오류] {symbol} · {ex.GetType().Name}: {ex.Message}"); }
            }
            finally { _slots.Release(); }
        }
    }
}
