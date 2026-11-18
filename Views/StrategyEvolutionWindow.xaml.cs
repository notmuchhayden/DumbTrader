using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DumbTrader.Models;
using DumbTrader.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DumbTrader.Views;

public partial class StrategyEvolutionWindow : Window
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly StrategyInstrument _instrument;
    private readonly StrategyCandidateRepository _repository;
    private readonly StrategyEvolutionCoordinator _coordinator;
    private readonly PaperTradingService _paper;
    private readonly StrategyEvolutionRuntime _runtime;
    private readonly DispatcherTimer _refresh;
    private CancellationTokenSource? _run;
    private bool _updating;

    private StrategyCandidate? Selected => Candidates.SelectedItem as StrategyCandidate;

    public StrategyEvolutionWindow(IServiceProvider services, StrategyInstrument instrument)
    {
        InitializeComponent();
        _instrument = instrument;
        _repository = services.GetRequiredService<StrategyCandidateRepository>();
        _coordinator = services.GetRequiredService<StrategyEvolutionCoordinator>();
        _paper = services.GetRequiredService<PaperTradingService>();
        _runtime = services.GetRequiredService<StrategyEvolutionRuntime>();
        InstrumentTitle.Text = $"{instrument.Symbol} · {instrument.Currency} · {(instrument.Market == "Stock" ? "주식" : "해외선물")} 신호 전용";
        OFutureSimulationPeriodOption[] periods = instrument.Market == "Stock"
            ? [new("일봉", 0)] : [new("일봉", 0), new("1분봉", 1), new("10분봉", 10)];
        Interval.ItemsSource = periods;
        Interval.SelectedIndex = 0;
        StartDate.SelectedDate = DateTime.Today.AddYears(-1);
        SettingsEditor.Text = JsonSerializer.Serialize(_repository.LoadOptions(), Json);
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refresh.Tick += (_, _) => RunUi(() => Refresh());
        Closed += (_, _) => { _refresh.Stop(); _run?.Cancel(); };
        Refresh();
        _refresh.Start();
    }

    public static void Open(Window? owner, StrategyInstrument instrument)
    {
        try
        {
            var services = App.ServiceProvider ?? throw new InvalidOperationException("앱의 전략 서비스를 사용할 수 없습니다.");
            new StrategyEvolutionWindow(services, instrument) { Owner = owner }.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "전략 후보 관리", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (_run is not null || Interval.SelectedItem is not OFutureSimulationPeriodOption interval || StartDate.SelectedDate is not { } start) return;
        using var cancel = new CancellationTokenSource();
        _run = cancel;
        Generate.IsEnabled = false;
        CancelRun.IsEnabled = true;
        Status.Text = "Codex 후보 생성 및 검증 중";
        try
        {
            var result = await _coordinator.RunAsync(_instrument, interval.IntervalMinutes, start, cancellationToken: cancel.Token);
            Refresh(result?.Id);
            Status.Text = result is null ? "후보 생성 작업 없음" : $"작업 완료 · {result.Status}";
        }
        catch (OperationCanceledException) { Status.Text = "작업 취소됨"; }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally
        {
            _run = null;
            Generate.IsEnabled = true;
            CancelRun.IsEnabled = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _run?.Cancel();
    private void Refresh_Click(object sender, RoutedEventArgs e) => RunUi(() => Refresh());
    private void Candidate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating) RunUi(ShowReport);
    }
    private void StartPaper_Click(object sender, RoutedEventArgs e) => ApplySelected(_runtime.StartPaper);
    private void StopPaper_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        _paper.Stop(_instrument.Market, _instrument.Symbol);
        Refresh();
    });
    private void Reject_Click(object sender, RoutedEventArgs e) => ApplySelected(id =>
    {
        var selected = _repository.Get(id);
        if (selected.Paper?.SessionId == _paper.Current(_instrument.Market, _instrument.Symbol)?.SessionId
            && _paper.Current(_instrument.Market, _instrument.Symbol) is not null)
            _paper.Stop(_instrument.Market, _instrument.Symbol);
        _coordinator.Reject(id);
    });

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not null && MessageBox.Show(this, "모의 운용 결과와 전략 변경을 확인하고 이 신호 버전을 승인하시겠습니까?",
            "후보 승인", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            ApplySelected(_paper.Approve);
    }

    private void Activate_Click(object sender, RoutedEventArgs e) => ApplySelected(id => _paper.Activate(id, rollback: false));
    private void Rollback_Click(object sender, RoutedEventArgs e) => ApplySelected(id => _paper.Activate(id, rollback: true));

    private void ApplySelected(Action<Guid> action) => RunUi(() =>
    {
        var id = Selected?.Id ?? throw new InvalidOperationException("후보를 선택하세요.");
        action(id);
        Refresh(id);
    });

    private void SaveSettings_Click(object sender, RoutedEventArgs e) => RunUi(() => SaveSettings(ReadSettings()));

    private void SetSchedule_Click(object sender, RoutedEventArgs e) => RunUi(() =>
    {
        if (Interval.SelectedItem is not OFutureSimulationPeriodOption interval || StartDate.SelectedDate is not { } start)
            throw new InvalidOperationException("봉 주기와 시작일을 선택하세요.");
        SaveSettings(ReadSettings() with
        {
            ScheduleEnabled = true,
            ScheduledTarget = new(_instrument, interval.IntervalMinutes, Math.Max(1, (DateTime.Today - start.Date).Days))
        });
    });

    private StrategyEvolutionOptions ReadSettings()
        => JsonSerializer.Deserialize<StrategyEvolutionOptions>(SettingsEditor.Text)
            ?? throw new InvalidOperationException("실행 설정을 입력하세요.");

    private void SaveSettings(StrategyEvolutionOptions settings)
    {
        _repository.SaveOptions(settings);
        SettingsEditor.Text = JsonSerializer.Serialize(settings, Json);
        Status.Text = "실행 설정 저장됨";
    }

    private void Refresh(Guid? id = null)
    {
        id ??= Selected?.Id;
        var candidates = _repository.List(_instrument.Market, _instrument.Symbol);
        _updating = true;
        try
        {
            Candidates.ItemsSource = candidates;
            Candidates.SelectedItem = candidates.FirstOrDefault(item => item.Id == id) ?? candidates.FirstOrDefault();
        }
        finally { _updating = false; }
        ShowReport();
    }

    private void ShowReport()
    {
        var output = new StringBuilder();
        var champion = _repository.Active(_instrument.Market, _instrument.Symbol);
        output.AppendLine(champion is null ? "활성 버전 없음 · 평가 기준은 내장 이동평균 전략" : $"활성 버전 {champion.Id}");
        if (_runtime.Latest(_instrument) is { } live && (live.CandidateId == champion?.Id || live.Error is not null))
            output.AppendLine($"최근 실시간 신호 {live.QuoteUtc.ToLocalTime():g} · {live.Signal?.Action.ToString() ?? live.Error} · {live.CandidateId}");
        if (Selected is { } selected)
        {
            output.AppendLine().AppendLine($"{selected.Id} · {selected.Status}");
            output.AppendLine(selected.Summary);
            if (selected.FailureReason is { } failed) output.AppendLine(failed);
            if (selected.CodeHash is not null)
            {
                var snapshot = _repository.ReadSnapshot(selected.Id);
                var costs = _instrument.Market == "Stock" ? snapshot.Options.StockCosts : snapshot.Options.Costs;
                output.AppendLine($"비용 모델 v1 · 편도 수수료 {costs.Commission:N2} + {costs.CommissionRate:P2} · 매도세 {costs.SellTaxRate:P2} · 슬리피지 {costs.SlippageTicks:N2}틱");
                output.AppendLine($"초기 자본 {snapshot.Options.CapitalFor(snapshot.Instrument):N2} · 최대 수량 {snapshot.Options.MaxQuantity} · 기준 낙폭 {snapshot.Options.MaxDrawdown:P1} · 구간별 최소 거래 {snapshot.Options.MinTradesPerWindow}");
                output.AppendLine("종가 신호 → 다음 봉 시가 · 미청산은 마지막 가격으로 평가");
                output.AppendLine($"비교 식  {snapshot.ChampionCode}");
                output.AppendLine($"후보 식  {_repository.ReadCode(selected)}");
                output.AppendLine($"SHA-256  {selected.CodeHash}");
            }
            if (selected.Evaluation is { } evaluation)
                foreach (var window in evaluation.Windows)
                {
                    output.AppendLine().AppendLine($"{window.Name} · {window.From:g} ~ {window.Through:g} · {window.Bars}봉 · {window.Regime}");
                    output.AppendLine($"후보  {Metrics(window.Candidate)}");
                    output.AppendLine($"기준  {Metrics(window.Champion)}");
                    output.AppendLine($"비용 2배 순손익 {window.DoubleCostProfit:N2}");
                }
            var running = _paper.Current(_instrument.Market, _instrument.Symbol);
            var paper = selected.Paper is { } saved && running?.SessionId == saved.SessionId
                ? running : _repository.LatestPaperReport(selected);
            if (paper is not null)
            {
                output.AppendLine().AppendLine($"모의 운용 · 시작 {paper.StartedAtUtc.ToLocalTime():g} · {paper.Samples}봉 · 신호 차이 {paper.SignalDifferences} · 지연 {paper.LatencyMilliseconds}ms");
                output.AppendLine($"후보  {Metrics(paper.Candidate)}");
                output.AppendLine($"기준  {Metrics(paper.Champion)}");
                output.AppendLine(paper.StopReason ?? (running?.SessionId != paper.SessionId ? "세션 종료 또는 앱 재시작" : "시세 수신 중"));
            }
            output.AppendLine().AppendLine("감사 이력");
            foreach (var audit in selected.Audit)
                output.AppendLine($"{audit.AtUtc.ToLocalTime():g} · {audit.Status} · {audit.Reason}");
            output.AppendLine().AppendLine($"기록 폴더  {_repository.DirectoryFor(selected.Id)}");
        }
        var next = output.ToString();
        if (Report.Text != next)
        {
            var offset = Report.VerticalOffset;
            Report.Text = next;
            Report.ScrollToVerticalOffset(offset);
        }
    }

    private static string Metrics(StrategyMetrics metrics) =>
        $"비용 전 {metrics.GrossProfit:N2} · 순손익 {metrics.NetProfit:N2} ({metrics.Return:P2}) · 비용 {metrics.TotalCosts:N2} · 낙폭 {metrics.MaxDrawdown:P2} · 회복 {metrics.RecoveryBars}봉 · 위험조정 {metrics.RiskAdjustedReturn:N3} · 거래 {metrics.Trades} · 평균 {metrics.AverageTrade:N2} · 최대 거래 손실 {metrics.WorstTrade:N2} · {(metrics.HasPosition ? "보유 중" : "FLAT")}";

    private void RunUi(Action action)
    {
        try { action(); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
}
