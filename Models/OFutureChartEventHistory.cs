namespace DumbTrader.Models;

internal sealed class OFutureChartEventHistory(int capacity = 2000)
{
    private readonly Queue<OFutureChartEvent> _events = new();
    private readonly HashSet<Guid> _ids = new();
    private DateTime _sessionStartedUtc;

    public void StartSession(DateTime startedUtc)
    {
        _sessionStartedUtc = startedUtc;
        _events.Clear();
        _ids.Clear();
    }

    public bool Add(OFutureChartEvent marker, DateTime receivedAt)
    {
        if (receivedAt < _sessionStartedUtc || !_ids.Add(marker.Id)) return false;
        _events.Enqueue(marker);
        if (_events.Count > capacity) _ids.Remove(_events.Dequeue().Id);
        return true;
    }

    public IEnumerable<OFutureChartEvent> ForSymbol(string? symbol) =>
        _events.Where(marker => string.Equals(marker.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
}
