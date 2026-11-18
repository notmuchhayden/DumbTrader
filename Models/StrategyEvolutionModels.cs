namespace DumbTrader.Models;

public enum CandidateStatus { Generating, Generated, Compiled, Evaluated, Paper, Approved, Rejected, Active, RolledBack, Preview }
public enum StrategyAction { Hold, Buy, Sell }

public sealed record StrategySignal(StrategyAction Action, int QuantityHint, string Reason, int StrategyState);
public sealed record StrategyBar(DateTime Time, double Open, double High, double Low, double Close, long Volume)
{
    public bool IsValid => double.IsFinite(Open) && double.IsFinite(High) && double.IsFinite(Low)
        && double.IsFinite(Close) && High >= Math.Max(Open, Close) && Low <= Math.Min(Open, Close) && Volume >= 0;
}

public sealed record StrategyInstrument(string Symbol, string Currency, double TickSize, double TickValue,
    double Margin, string Market = "OFuture", bool Kosdaq = false);

public sealed record StrategyCosts(double Commission = 0, double SlippageTicks = 0,
    double CommissionRate = 0, double SellTaxRate = 0);

public sealed record StrategyEvolutionOptions
{
    public string CliPath { get; init; } = "codex.exe";
    public string? Model { get; init; }
    public string? WorkRoot { get; init; }
    public int TimeoutSeconds { get; init; } = 180;
    public bool ScheduleEnabled { get; init; }
    public int ScheduledHourLocal { get; init; } = 8;
    public StrategyScheduleTarget? ScheduledTarget { get; init; }
    public double Capital { get; init; } = 10_000;
    public int MaxQuantity { get; init; } = 1;
    public double MaxDrawdown { get; init; } = 0.10;
    public double DailyLossLimit { get; init; } = 500;
    public int MinTradesPerWindow { get; init; } = 3;
    public int MinPaperTrades { get; init; } = 5;
    public int MinPaperHours { get; init; } = 24;
    public int MaxQuoteAgeSeconds { get; init; } = 60;
    public int MaxSignalErrors { get; init; } = 3;
    public bool KillSwitch { get; init; }
    public StrategySessionHours? FuturesSessionHoursKorea { get; init; }
    public double StockCapital { get; init; } = 10_000_000;
    public double StockDailyLossLimit { get; init; } = 100_000;
    public StrategyCosts Costs { get; init; } = new(2, 1);
    public StrategyCosts StockCosts { get; init; } = new(CommissionRate: 0.001, SellTaxRate: 0.003);

    public void Validate()
    {
        if (TimeoutSeconds is < 1 or > 1800 || ScheduledHourLocal is < 0 or > 23
            || MaxQuantity is < 1 or > 1000 || MinTradesPerWindow < 1 || MinPaperTrades < 1
            || MinPaperHours is < 1 or > 8760 || MaxQuoteAgeSeconds is < 1 or > 3600 || MaxSignalErrors < 1
            || !double.IsFinite(Capital) || Capital <= 0 || !double.IsFinite(DailyLossLimit) || DailyLossLimit <= 0
            || !double.IsFinite(StockCapital) || StockCapital <= 0 || !double.IsFinite(StockDailyLossLimit) || StockDailyLossLimit <= 0
            || !double.IsFinite(MaxDrawdown) || MaxDrawdown is <= 0 or >= 1)
            throw new InvalidOperationException("전략 개선 설정의 범위를 확인하세요.");
        if (ScheduledTarget is { } schedule && (schedule.LookbackDays is < 1 or > 3650
            || schedule.Instrument is null || schedule.Instrument.Market is not ("Stock" or "OFuture")
            || schedule.IntervalMinutes is not (0 or 1 or 10) || schedule.Instrument.Market == "Stock" && schedule.IntervalMinutes != 0))
            throw new InvalidOperationException("예약 대상, 봉 주기 및 조회 기간을 확인하세요.");
        if (FuturesSessionHoursKorea is { } session && session.Open == session.Close)
            throw new InvalidOperationException("해외선물 세션 시작과 종료 시각을 구분하세요.");
        ValidateCosts(Costs);
        ValidateCosts(StockCosts);
    }

    public double CapitalFor(StrategyInstrument instrument) => instrument.Market == "Stock" ? StockCapital : Capital;
    public double DailyLossFor(StrategyInstrument instrument) => instrument.Market == "Stock" ? StockDailyLossLimit : DailyLossLimit;

    public static void ValidateCosts(StrategyCosts costs)
    {
        ArgumentNullException.ThrowIfNull(costs);
        var values = new[] { costs.Commission, costs.SlippageTicks, costs.CommissionRate, costs.SellTaxRate };
        if (values.Any(value => !double.IsFinite(value) || value < 0)
            || costs.CommissionRate + costs.SellTaxRate >= 1)
            throw new InvalidOperationException("거래 비용 설정의 범위를 확인하세요.");
    }
}

public sealed record StrategyScheduleTarget(StrategyInstrument Instrument, int IntervalMinutes, int LookbackDays);
public sealed record StrategySessionHours(TimeOnly Open, TimeOnly Close);

public sealed record StrategyMetrics(double GrossProfit, double NetProfit, double Return, double MaxDrawdown,
    int RecoveryBars, double RiskAdjustedReturn, int Trades, double AverageTrade, double WinRate,
    bool HasPosition, double TotalCosts, double WorstTrade, int Entries);

public sealed record StrategyWindow(string Name, DateTime From, DateTime Through, int Bars,
    StrategyMetrics Candidate, StrategyMetrics Champion, double DoubleCostProfit, string Regime);

public sealed record StrategyEvaluation(IReadOnlyList<StrategyWindow> Windows, IReadOnlyList<string> Failures);

public sealed record StrategySnapshot(StrategyInstrument Instrument, DateTime CreatedAtUtc, int IntervalMinutes,
    StrategyBar[] Bars, StrategyEvolutionOptions Options, string ChampionCode, Guid? ChampionId);

public sealed record StrategyPaperReport(Guid SessionId, DateTime StartedAtUtc, DateTime LastQuoteUtc,
    StrategyMetrics Candidate, StrategyMetrics Champion, int SignalDifferences, long LatencyMilliseconds,
    int Samples, string? StopReason);

public sealed record StrategyPaperSample(DateTime AtUtc, StrategyBar Bar, StrategySignal? Candidate,
    StrategySignal? Champion, long CandidatePosition, long ChampionPosition, double ExecutionPrice,
    long CandidateFilledQuantity, long ChampionFilledQuantity, long LatencyMilliseconds, string? Error, StrategyPaperReport Report);

public sealed record StrategyAudit(DateTime AtUtc, CandidateStatus Status, string Reason);

public sealed record StrategyCandidate(Guid Id, string Market, string Symbol, DateTime CreatedAtUtc,
    Guid? ParentId, string? CodeHash, string? Model, CandidateStatus Status, string? Summary,
    string? FailureReason, StrategyEvaluation? Evaluation, StrategyPaperReport? Paper,
    IReadOnlyList<StrategyAudit> Audit, DateTime? ApprovedAtUtc = null, DateTime? ActivatedAtUtc = null)
{
    public bool IsNonRecommendedStockCandidate() =>
        Market == "Stock" && Status == CandidateStatus.Rejected && CodeHash is not null
        && Evaluation is { Failures.Count: > 0, Windows.Count: > 0 }
        && Audit.Count >= 2 && Audit[^2].Status == CandidateStatus.Evaluated;

    public bool CanStartPaper() => Status switch
    {
        CandidateStatus.Evaluated => Evaluation?.Failures.Count == 0,
        CandidateStatus.Paper => Evaluation?.Failures.Count == 0 || (Market == "Stock" && Paper is not null),
        CandidateStatus.Rejected => IsNonRecommendedStockCandidate(),
        _ => false
    };
}
