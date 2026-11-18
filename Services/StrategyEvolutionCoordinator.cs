using System.IO;
using DumbTrader.Models;

namespace DumbTrader.Services;

public sealed class StrategyEvolutionCoordinator(StrategyCandidateRepository repository, MarketSnapshotBuilder snapshots,
    ICodexStrategyGenerator generator, StrategyCompiler compiler, StrategyEvaluationService evaluation) : IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task<StrategyCandidate?> RunAsync(StrategyInstrument instrument, int interval, DateTime start,
        bool scheduled = false, CancellationToken cancellationToken = default)
        => Task.Run(() => RunCoreAsync(instrument, interval, start, scheduled, cancellationToken), cancellationToken);

    private async Task<StrategyCandidate?> RunCoreAsync(StrategyInstrument instrument, int interval, DateTime start,
        bool scheduled, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, cancellationToken);
        var token = linked.Token;
        if (!await _gate.WaitAsync(0, token).ConfigureAwait(false))
        {
            if (scheduled) return null;
            throw new InvalidOperationException("이미 실행 중인 전략 개선 작업이 있습니다.");
        }

        try
        {
            var options = repository.LoadOptions();
            if (options.KillSwitch) throw new InvalidOperationException("킬 스위치가 켜져 있습니다.");
            var now = DateTime.Now;
            if (scheduled && (!options.ScheduleEnabled || now.Hour < options.ScheduledHourLocal)) return null;
            using var workLock = repository.AcquireGenerationLock();
            var recent = repository.List(instrument.Market, instrument.Symbol);
            foreach (var interrupted in recent.Where(item => item.Status is CandidateStatus.Generating or CandidateStatus.Generated or CandidateStatus.Compiled))
                repository.Update(interrupted.Id, interrupted.Status, CandidateStatus.Rejected, "이전 실행이 완료되기 전에 종료되었습니다.");
            if (scheduled && recent.Any(item => item.CreatedAtUtc.ToLocalTime().Date == now.Date)) return null;

            var champion = repository.Active(instrument.Market, instrument.Symbol);
            var candidate = repository.Create(instrument, champion?.Id, options.Model);
            try
            {
                var championCode = champion is null
                    ? instrument.Market == "Stock" ? StrategyCompiler.StockBaseline : StrategyCompiler.Baseline
                    : repository.ReadCode(champion);
                var snapshot = await snapshots.BuildAsync(instrument, interval, start, options, championCode,
                    champion?.Id, token, allowPreview: !scheduled).ConfigureAwait(false);
                if (champion is not null && repository.ReadSnapshot(champion.Id).IntervalMinutes != interval)
                    throw new InvalidOperationException("활성 버전과 동일한 봉 주기로 평가하세요.");
                repository.SaveSnapshot(candidate.Id, snapshot);
                var compiledChampion = compiler.Compile(championCode);
                var prompt = MarketSnapshotBuilder.BuildPrompt(snapshot, evaluation.Training(snapshot, compiledChampion, token));
                repository.WriteArtifact(candidate.Id, "prompt-v1.txt", prompt);
                var generated = await generator.GenerateAsync(candidate.Id, prompt, options, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var hash = repository.WriteCode(candidate.Id, generated.Code);
                candidate = repository.Update(candidate.Id, CandidateStatus.Generating, CandidateStatus.Generated, "후보 코드 저장",
                    item => item with { CodeHash = hash, Summary = generated.Summary });
                var compiled = compiler.Compile(repository.ReadCode(candidate));
                candidate = repository.Update(candidate.Id, CandidateStatus.Generated, CandidateStatus.Compiled, "Roslyn 진단 통과");
                if (snapshot.Bars.Length < StrategyEvaluationService.MinimumBars)
                    return repository.Update(candidate.Id, CandidateStatus.Compiled, CandidateStatus.Preview,
                        $"유효 봉 {snapshot.Bars.Length}개 · 탐색용 미리보기",
                        item => item with { Summary = $"탐색용 · 검증 봉 부족 ({snapshot.Bars.Length}/{StrategyEvaluationService.MinimumBars})\n{item.Summary}" });
                var report = evaluation.Evaluate(snapshot, compiled, compiledChampion, token,
                    (from, through) => repository.ClaimFinalWindow(candidate.Id, recent, interval, from, through));
                candidate = repository.Update(candidate.Id, CandidateStatus.Compiled, CandidateStatus.Evaluated, "비용 포함 평가 완료",
                    item => item with { Evaluation = report });
                return report.Failures.Count == 0 ? candidate
                    : repository.Update(candidate.Id, CandidateStatus.Evaluated, CandidateStatus.Rejected, string.Join(" / ", report.Failures));
            }
            catch (Exception ex)
            {
                var reason = ex is OperationCanceledException ? "후보 생성이 취소되었습니다."
                    : ex is System.ComponentModel.Win32Exception ? "Codex 실행 파일 경로를 확인하세요." : ex.Message;
                return repository.Update(candidate.Id, candidate.Status, CandidateStatus.Rejected, reason);
            }
        }
        finally { _gate.Release(); }
    }

    public void Reject(Guid id)
    {
        var candidate = repository.Get(id);
        if (candidate.Status is not (CandidateStatus.Preview or CandidateStatus.Evaluated or CandidateStatus.Paper or CandidateStatus.Approved))
            throw new InvalidOperationException("평가·모의 운용·승인 상태에 있는 후보를 선택하세요.");
        repository.Update(id, candidate.Status, CandidateStatus.Rejected, "사용자가 후보를 거절했습니다.");
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        // The coordinator uses background continuations, so shutdown can wait for the child process cleanup.
        if (_gate.Wait(TimeSpan.FromSeconds(10))) _gate.Release();
    }
}
