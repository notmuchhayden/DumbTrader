using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services
{
    /// <summary>해외선물의 사용자 전략 신호를 Roslyn 스크립트로 계산한다. 이 서비스는 주문을 제출하지 않는다.</summary>
    public sealed class OFutureStrategyService
    {
        private sealed record SavedStrategy(string Symbol, string MainStrategyPath);

        private readonly IDbContextFactory<DumbTraderDbContext> _dbFactory;
        private readonly OFutureDataService _oFutureDataService;
        private readonly LoggingService _loggingService;
        private readonly RoslynScriptRunner _scriptRunner = new();
        private readonly string _configPath;
        private readonly Dictionary<string, OFutureStrategyAssignment> _assignments = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _savedMainPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _assignmentsLock = new();
        private readonly ConcurrentDictionary<string, byte> _runningSymbols = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Dictionary<string, object>> _liveContexts = new(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<OFutureStrategyAssignment> StrategyContracts { get; } = new();
        public event EventHandler<OFutureStrategySignalEventArgs>? StrategySignalUpdated;
        public event EventHandler? StrategyConfigurationUpdated;

        public OFutureStrategyService(
            IDbContextFactory<DumbTraderDbContext> dbFactory,
            OFutureDataService oFutureDataService,
            LoggingService loggingService)
        {
            _dbFactory = dbFactory;
            _oFutureDataService = oFutureDataService;
            _loggingService = loggingService;
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ofuture_strategy_config.json");
            LoadConfig();
            SynchronizeWatchlist();
            _oFutureDataService.WatchlistUpdated += OnWatchlistUpdated;
        }

        public OFutureStrategyAssignment? FindAssignment(string symbol)
        {
            lock (_assignmentsLock)
                return _assignments.TryGetValue(symbol, out var assignment) ? assignment : null;
        }

        public string GetLiveStrategyStatus(string symbol)
        {
            var assignment = FindAssignment(symbol);
            if (assignment is null)
                return "전략 설정 없음 · 상세 화면의 ‘전략 / 시뮬레이션’에서 주전략을 선택하세요";
            if (string.IsNullOrWhiteSpace(assignment.MainStrategyPath))
                return "전략 미설정 · 상세 화면의 ‘전략 / 시뮬레이션’에서 주전략을 선택하세요";
            if (ResolveMainScriptPath(assignment.MainStrategyPath) is null)
                return $"전략 파일을 찾을 수 없음: {assignment.MainStrategyPath}";
            return $"전략 준비됨: {assignment.MainStrategyPath} · OVC 신호 대기 · 주문 미전송";
        }

        public void SaveConfig()
        {
            try
            {
                List<SavedStrategy> saved;
                lock (_assignmentsLock)
                {
                    saved = _assignments.Values
                        .Select(item => new SavedStrategy(item.Contract.Symbol, item.MainStrategyPath))
                        .ToList();
                    _savedMainPaths.Clear();
                    foreach (var item in saved) _savedMainPaths[item.Symbol] = item.MainStrategyPath;
                }
                File.WriteAllText(_configPath, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
                StrategyConfigurationUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _loggingService.Log($"해외선물 전략 설정 저장 오류: {ex.Message}");
            }
        }

        public async Task<StrategyExecutionResult?> RunOnLiveDataAsync(
            OFutureRealData realData, long requestGeneration = 0, CancellationToken cancellationToken = default)
        {
            if (realData is null || string.IsNullOrWhiteSpace(realData.Symbol)) return null;
            var assignment = FindAssignment(realData.Symbol);
            if (assignment is null || string.IsNullOrWhiteSpace(assignment.MainStrategyPath)) return null;
            if (!_runningSymbols.TryAdd(realData.Symbol, 0)) return null;

            try
            {
                var scriptPath = ResolveMainScriptPath(assignment.MainStrategyPath);
                if (scriptPath is null)
                    return PublishFailure(realData, assignment.Contract.Name, "선택한 해외선물 전략 파일을 찾을 수 없습니다.", requestGeneration);

                var code = await File.ReadAllTextAsync(scriptPath, cancellationToken).ConfigureAwait(false);
                await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                var context = _liveContexts.GetOrAdd(realData.Symbol, _ => new Dictionary<string, object>());
                var result = await RunMainScriptAsync(code, assignment.Contract, realData, db, context,
                    isSimulation: false, seedMoney: 0, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result.MainResult is { Success: true, Message: "BUY" or "SELL" or "HOLD" } signal)
                {
                    RaiseSignalUpdated(new OFutureStrategySignalEventArgs(
                        realData.Symbol, assignment.Contract.Name, signal.Message, signal.Count, realData.ReceivedAtUtc,
                        requestGeneration: requestGeneration));
                    if (signal.Message is "BUY" or "SELL")
                        _loggingService.Log($"해외선물 전략 신호: {realData.Symbol} {signal.Message} ({signal.Count}) - 주문은 제출되지 않았습니다.");
                }
                else
                {
                    var message = result.MainResult?.Message ?? result.Action;
                    _loggingService.Log($"해외선물 전략 실행 실패 ({realData.Symbol}): {message}");
                    RaiseSignalUpdated(new OFutureStrategySignalEventArgs(
                        realData.Symbol, assignment.Contract.Name, "ERROR", 0, realData.ReceivedAtUtc, message,
                        requestGeneration));
                }
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Failed("해외선물 전략 실행이 취소되었습니다.");
            }
            catch (Exception ex)
            {
                _loggingService.Log($"해외선물 전략 실행 오류 ({realData.Symbol}): {ex.Message}");
                return PublishFailure(realData, assignment.Contract.Name, ex.Message, requestGeneration);
            }
            finally
            {
                _runningSymbols.TryRemove(realData.Symbol, out _);
            }
        }

        public async Task<string> LoadMainScriptAsync(
            OFutureStrategyAssignment assignment, CancellationToken cancellationToken = default)
        {
            var path = ResolveMainScriptPath(assignment.MainStrategyPath);
            if (path is null) throw new InvalidOperationException("주전략 스크립트를 선택하세요.");
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }

        public Task<StrategyExecutionResult> RunSimulationStepAsync(
            string scriptCode,
            OFutureContract contract,
            OFutureRealData bar,
            DumbTraderDbContext dbContext,
            Dictionary<string, object> simulationContext,
            double capital,
            string positionSide,
            long positionQuantity,
            CancellationToken cancellationToken = default)
            => RunMainScriptAsync(scriptCode, contract, bar, dbContext, simulationContext,
                isSimulation: true, seedMoney: capital, cancellationToken: cancellationToken,
                positionSide: positionSide, positionQuantity: positionQuantity, offload: false);

        public void ResetLiveContext(string symbol) => _liveContexts.TryRemove(symbol, out _);

        private async Task<StrategyExecutionResult> RunMainScriptAsync(
            string code,
            OFutureContract contract,
            OFutureRealData realData,
            DumbTraderDbContext dbContext,
            Dictionary<string, object> context,
            bool isSimulation,
            double seedMoney,
            CancellationToken cancellationToken,
            string positionSide = "FLAT",
            long positionQuantity = 0,
            bool offload = true)
        {
            var globals = new OFutureStrategyGlobals
            {
                Contract = contract,
                RealData = realData,
                DbContext = dbContext,
                Logging = _loggingService,
                IsSimulation = isSimulation,
                SeedMoney = seedMoney,
                SimulationDate = ParseDate(realData),
                PositionSide = positionSide,
                PositionQuantity = positionQuantity,
                Context = context
            };

            var scriptValue = offload
                ? await Task.Run(() => _scriptRunner.RunScriptAsync(code, globals, TimeSpan.FromSeconds(5), cancellationToken),
                    cancellationToken).ConfigureAwait(false)
                : await _scriptRunner.RunScriptAsync(code, globals, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            if (scriptValue is not ScriptResult scriptResult)
                return Failed("해외선물 주전략이 ScriptResult를 반환하지 않았습니다.");
            if (scriptResult.Success && scriptResult.Message is not ("BUY" or "SELL" or "HOLD"))
                return Failed($"지원하지 않는 전략 신호입니다: {scriptResult.Message}");
            return new StrategyExecutionResult(scriptResult.Success, scriptResult.Message, scriptResult, null);
        }

        private string? ResolveMainScriptPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName
                || !string.Equals(Path.GetExtension(fileName), ".cs", StringComparison.OrdinalIgnoreCase))
                return null;
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "strategy", "ofuture", "main", fileName);
            return File.Exists(path) ? path : null;
        }

        private void LoadConfig()
        {
            if (!File.Exists(_configPath)) return;
            try
            {
                var saved = JsonSerializer.Deserialize<List<SavedStrategy>>(File.ReadAllText(_configPath));
                if (saved is null) return;
                foreach (var item in saved.Where(item => !string.IsNullOrWhiteSpace(item.Symbol)))
                    _savedMainPaths[item.Symbol] = item.MainStrategyPath ?? string.Empty;
            }
            catch (Exception ex)
            {
                _loggingService.Log($"해외선물 전략 설정 로드 오류: {ex.Message}");
            }
        }

        private void OnWatchlistUpdated(object? sender, EventArgs e) => SynchronizeWatchlist();

        private void SynchronizeWatchlist()
        {
            var watchlist = _oFutureDataService.Watchlist;
            void Update()
            {
                lock (_assignmentsLock)
                {
                    var symbols = watchlist.Select(item => item.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var symbol in _assignments.Keys.Where(symbol => !symbols.Contains(symbol)).ToArray())
                    {
                        var removed = _assignments[symbol];
                        _assignments.Remove(symbol);
                        _liveContexts.TryRemove(symbol, out _);
                        StrategyContracts.Remove(removed);
                    }

                    foreach (var contract in watchlist)
                    {
                        if (_assignments.TryGetValue(contract.Symbol, out var current))
                        {
                            current.Contract = contract;
                            continue;
                        }

                        var assignment = new OFutureStrategyAssignment
                        {
                            Contract = contract,
                            MainStrategyPath = _savedMainPaths.GetValueOrDefault(contract.Symbol, string.Empty)
                        };
                        _assignments[contract.Symbol] = assignment;
                        StrategyContracts.Add(assignment);
                    }
                }
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) Update();
            else _ = dispatcher.BeginInvoke(Update);
        }

        private static DateTime ParseDate(OFutureRealData data)
        {
            if (DateTime.TryParseExact(data.KoreaDate, "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var date)) return date.Date;
            if (DateTime.TryParseExact(data.OverseasDate, "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out date)) return date.Date;
            return data.ReceivedAtUtc.Date;
        }

        private static StrategyExecutionResult Failed(string message)
            => new(false, message, null, null);

        private StrategyExecutionResult PublishFailure(
            OFutureRealData data, string contractName, string message, long requestGeneration)
        {
            RaiseSignalUpdated(new OFutureStrategySignalEventArgs(
                data.Symbol, contractName, "ERROR", 0, data.ReceivedAtUtc, message, requestGeneration));
            return Failed(message);
        }

        private void RaiseSignalUpdated(OFutureStrategySignalEventArgs args)
        {
            try { StrategySignalUpdated?.Invoke(this, args); }
            catch (Exception ex) { _loggingService.Log($"해외선물 전략 신호 화면 반영 오류: {ex.Message}"); }
        }
    }
}
