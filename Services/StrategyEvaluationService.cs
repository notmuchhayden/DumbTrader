using DumbTrader.Models;

namespace DumbTrader.Services;

public sealed class StrategyEvaluationService
{
    public const int MinimumBars = 150;
    public const int MaximumBars = 5000;
    public static int TrainingEnd(int count) => count == 1 ? 1 : count * 3 / 5;

    public StrategyMetrics Training(StrategySnapshot snapshot, Func<double[], StrategySignal> champion, CancellationToken token)
        => Replay(snapshot, champion, CostsFor(snapshot), 0, 0, TrainingEnd(snapshot.Bars.Length), token);

    private static StrategyCosts CostsFor(StrategySnapshot snapshot)
        => snapshot.Instrument.Market == "Stock" ? snapshot.Options.StockCosts : snapshot.Options.Costs;

    public StrategyEvaluation Evaluate(StrategySnapshot snapshot, Func<double[], StrategySignal> candidate,
        Func<double[], StrategySignal> champion, CancellationToken cancellationToken, Func<DateTime, DateTime, bool>? claimFinal = null)
    {
        var bars = snapshot.Bars;
        if (bars.Length < MinimumBars) throw new InvalidOperationException($"평가에는 유효한 봉 {MinimumBars}개 이상이 필요합니다.");
        var first = TrainingEnd(bars.Length);
        var second = bars.Length * 7 / 10;
        var final = bars.Length * 4 / 5;
        var costs = CostsFor(snapshot);
        var doubled = new StrategyCosts(costs.Commission * 2, costs.SlippageTicks * 2,
            costs.CommissionRate * 2, costs.SellTaxRate * 2);
        var windows = new List<StrategyWindow>();
        var failures = new List<string>();

        foreach (var (name, warmup, from, to) in new[]
        {
            ("학습", 0, 0, first),
            ("워크포워드 1", 0, first, second),
            ("워크포워드 2", second - first, second, final),
            ("최종 평가", final - first, final, bars.Length)
        })
        {
            if (from == final)
            {
                if (failures.Count > 0) break;
                if (claimFinal is not null && !claimFinal(bars[from].Time, bars[to - 1].Time))
                {
                    failures.Add("최종 평가에는 이전 최종 구간과 겹치지 않는 새 데이터가 필요합니다.");
                    break;
                }
            }
            var result = Replay(snapshot, candidate, costs, warmup, from, to, cancellationToken);
            var baseline = Replay(snapshot, champion, costs, warmup, from, to, cancellationToken);
            var sensitivity = Replay(snapshot, candidate, doubled, warmup, from, to, cancellationToken);
            var change = bars[to - 1].Close.CompareTo(bars[from].Open);
            windows.Add(new(name, bars[from].Time, bars[to - 1].Time, to - from, result, baseline,
                sensitivity.NetProfit, change > 0 ? "상승" : change < 0 ? "하락" : "횡보"));
            if (from == 0) continue;
            if (result.Trades < snapshot.Options.MinTradesPerWindow) failures.Add($"{name}: 완료 거래 표본 부족");
            if (result.NetProfit <= 0 || result.NetProfit < baseline.NetProfit) failures.Add($"{name}: 비용 차감 순손익 기준 미달");
            if (result.MaxDrawdown > snapshot.Options.MaxDrawdown) failures.Add($"{name}: 최대 낙폭 초과");
        }
        return new(windows, failures);
    }

    private static StrategyMetrics Replay(StrategySnapshot snapshot, Func<double[], StrategySignal> strategy,
        StrategyCosts costs, int warmup, int from, int to, CancellationToken token)
    {
        var runner = new StrategySignalRunner(strategy);
        var ledger = new StrategyLedger(snapshot.Instrument, costs, snapshot.Options.CapitalFor(snapshot.Instrument), snapshot.Options.MaxQuantity);
        StrategySignal? pending = null;
        for (var i = warmup; i < to; i++)
        {
            token.ThrowIfCancellationRequested();
            var bar = snapshot.Bars[i];
            if (i >= from)
            {
                if (pending is not null) ledger.Fill(pending, bar.Open);
                ledger.Mark(bar.Close);
            }
            var signal = runner.Run(bar, ledger.Position);
            if (i >= from) pending = signal;
        }
        return ledger.Metrics;
    }
}

internal sealed class StrategySignalRunner(Func<double[], StrategySignal> strategy)
{
    internal sealed record Checkpoint(StrategyBar[] Bars, int State);
    private readonly Queue<StrategyBar> _bars = new(21);
    private int _state;
    public Checkpoint Save() => new(_bars.ToArray(), _state);
    public void Restore(Checkpoint checkpoint)
    {
        _bars.Clear();
        foreach (var bar in checkpoint.Bars) _bars.Enqueue(bar);
        _state = checkpoint.State;
    }

    public StrategySignal Run(StrategyBar bar, long position)
    {
        if (_bars.Count == 21) _bars.Dequeue();
        _bars.Enqueue(bar);
        var prior = _bars.Take(_bars.Count - 1).ToArray();
        double Average(IEnumerable<StrategyBar> bars, int count) =>
            bars.TakeLast(count).Select(item => item.Close).DefaultIfEmpty(bar.Close).Average();
        var changes = _bars.Zip(_bars.Skip(1), (old, current) => Math.Abs(current.Close - old.Close));
        var result = strategy([bar.Open, bar.High, bar.Low, bar.Close, bar.Volume,
            Average(_bars, 5), Average(_bars, 20), Average(prior, 5), Average(prior, 20),
            changes.DefaultIfEmpty().Average(), _bars.TakeLast(20).Average(item => (double)item.Volume),
            _bars.Count, position, _state]);
        _state = result.StrategyState;
        return result;
    }
}

internal sealed class StrategyLedger
{
    internal sealed record Checkpoint(long Position, double Entry, double EntryCost, double Realized, double GrossRealized,
        double Costs, double TradePnl, double WorstTrade, int Wins, int Trades, int Entries, int Observations,
        int PeakAt, int Recovery, double MaxDrawdown, double Peak, double Net, double Gross, double Mean,
        double M2, double PreviousEquity);

    private readonly StrategyInstrument _instrument;
    private readonly StrategyCosts _costModel;
    private readonly double _capital;
    private readonly int _maxQuantity;
    private double _entry;
    private double _entryCost;
    private double _realized;
    private double _grossRealized;
    private double _costs;
    private double _tradePnl;
    private double _worstTrade;
    private int _wins;
    private int _trades;
    private int _entries;
    private int _observations;
    private int _peakAt;
    private int _recovery;
    private double _maxDrawdown;
    private double _peak;
    private double _net;
    private double _gross;
    private double _mean;
    private double _m2;
    private double _previousEquity;
    public long Position { get; private set; }
    public double Realized => _realized;

    public StrategyLedger(StrategyInstrument instrument, StrategyCosts costs, double capital, int maxQuantity)
    {
        _instrument = instrument;
        _costModel = costs;
        _capital = _peak = _previousEquity = capital;
        _maxQuantity = maxQuantity;
    }

    public Checkpoint Save() => new(Position, _entry, _entryCost, _realized, _grossRealized, _costs, _tradePnl, _worstTrade,
        _wins, _trades, _entries, _observations, _peakAt, _recovery, _maxDrawdown, _peak, _net, _gross, _mean, _m2, _previousEquity);

    public void Restore(Checkpoint state)
    {
        (Position, _entry, _entryCost, _realized, _grossRealized, _costs, _tradePnl, _worstTrade, _wins, _trades,
            _entries, _observations, _peakAt, _recovery, _maxDrawdown, _peak, _net, _gross, _mean, _m2, _previousEquity)
            = (state.Position, state.Entry, state.EntryCost, state.Realized, state.GrossRealized, state.Costs,
                state.TradePnl, state.WorstTrade, state.Wins, state.Trades, state.Entries, state.Observations,
                state.PeakAt, state.Recovery, state.MaxDrawdown, state.Peak, state.Net, state.Gross, state.Mean,
                state.M2, state.PreviousEquity);
    }

    public void Fill(StrategySignal signal, double price)
    {
        if (signal.Action == StrategyAction.Hold || signal.QuantityHint <= 0) return;
        var direction = signal.Action == StrategyAction.Buy ? 1 : -1;
        var unitCost = FillCost(price, direction);
        if (Position == 0)
        {
            if (direction < 0 && _instrument.Market == "Stock") return;
            var margin = _instrument.Market == "Stock" ? price : _instrument.Margin;
            if (margin <= 0) return;
            var affordable = Math.Floor((_capital + _realized) / (margin + unitCost));
            var quantity = (long)Math.Clamp(affordable, 0, Math.Min(_maxQuantity, signal.QuantityHint));
            if (quantity == 0) return;
            _entry = price;
            Position = direction * quantity;
            _entryCost = unitCost * quantity;
            _costs += _entryCost;
            _realized -= _entryCost;
            _entries++;
            return;
        }

        if (Math.Sign(Position) == direction) return;
        // An opposing signal closes the position. A new entry requires a subsequent signal.
        var gross = OpenProfit(price);
        var exitCost = unitCost * Math.Abs(Position);
        var net = gross - exitCost - _entryCost;
        _grossRealized += gross;
        _realized += gross - exitCost;
        _costs += exitCost;
        _tradePnl += net;
        _trades++;
        if (net > 0) _wins++;
        _worstTrade = Math.Min(_worstTrade, net);
        Position = 0;
    }

    public void Mark(double price)
    {
        var open = OpenProfit(price);
        _net = _realized + open;
        _gross = _grossRealized + open;
        var equity = _capital + _net;
        _observations++;
        var step = (equity - _previousEquity) / _capital;
        var delta = step - _mean;
        _mean += delta / _observations;
        _m2 += delta * (step - _mean);
        _previousEquity = equity;
        if (equity >= _peak)
        {
            _peak = equity;
            _peakAt = _observations;
        }
        else
        {
            _maxDrawdown = Math.Max(_maxDrawdown, (_peak - equity) / _peak);
            _recovery = Math.Max(_recovery, _observations - _peakAt);
        }
        if (!double.IsFinite(_net) || !double.IsFinite(_maxDrawdown) || !double.IsFinite(_m2))
            throw new InvalidOperationException("가격 또는 비용으로 유한한 성과를 계산할 수 없습니다.");
    }

    private double FillCost(double price, int direction) => _costModel.Commission + _costModel.SlippageTicks * _instrument.TickValue
        + Math.Abs(price) * (_costModel.CommissionRate + (direction < 0 ? _costModel.SellTaxRate : 0));

    private double OpenProfit(double price)
        => Position == 0 ? 0 : Math.Round((price - _entry) / _instrument.TickSize, MidpointRounding.AwayFromZero)
            * _instrument.TickValue * Position;

    public double NetAt(double price) => _realized + OpenProfit(price);
    public double DrawdownAt(double price) => Math.Max(_maxDrawdown, (_peak - (_capital + NetAt(price))) / _peak);

    public StrategyMetrics Metrics => new(_gross, _net, _net / _capital, _maxDrawdown, _recovery,
        _m2 > 0 ? _mean / Math.Sqrt(_m2 / (_observations - 1)) : 0,
        _trades, _trades == 0 ? 0 : _tradePnl / _trades, _trades == 0 ? 0 : (double)_wins / _trades,
        Position != 0, _costs, _worstTrade, _entries);
}
