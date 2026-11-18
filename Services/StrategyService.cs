using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services
{
    // 전략 실행에 필요한 전역 데이터와 서비스들을 담는 클래스
    public class StrategyGlobals
    {
        public StockInfo Stock { get; set; }
        public RealS3_K3_Data RealData { get; set; }
        public DumbTraderDbContext DbContext { get; set; }
        public LoggingService Logging { get; set; }
        public IStockDataService StockDataService { get; set; }
        public bool IsSimulation { get; set; }
        public int SeedMoney { get; set; }

        // 스크립트 컨텍스트 - 필요에 따라 스크립트 간 데이터 공유용으로 사용
        public Dictionary<string, object> Context { get; set; } = new();
    }

    // 주식 매매 전략을 관장하는 서비스
    public class StrategyService
    {
        private readonly IDbContextFactory<DumbTraderDbContext> _dbFactory;
        private readonly StockDataService _stockDataService;

        // 관심종목과 해당 전략 정보를 담는 리스트
        private ObservableCollection<StrategyStockInfo> _strategyStocks;
        public ObservableCollection<StrategyStockInfo> StrategyStocks => _strategyStocks;

        private readonly ConcurrentDictionary<string, Dictionary<string, object>> _liveContexts = new(StringComparer.Ordinal);

        // 설정 파일 경로
        private readonly string _configPath;

        // Roslyn 스크립트 실행기
        private readonly RoslynScriptRunner _scriptRunner;
        private readonly ConcurrentDictionary<string, byte> _runningStocks = new(StringComparer.Ordinal);
        
        private readonly LoggingService _loggingService;

        public StrategyService(IDbContextFactory<DumbTraderDbContext> dbFactory,
            LoggingService loggingService,
            StockDataService stockDataService)
        {
            _dbFactory = dbFactory;
            _loggingService = loggingService;
            _stockDataService = stockDataService;

            _strategyStocks = new ObservableCollection<StrategyStockInfo>();
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            _scriptRunner = new RoslynScriptRunner();

            // 앱 시작 시 설정 파일에서 관심종목과 전략 정보 로드
            LoadConfig();
        }

        // 주어진 종목코드에 대해 해당 전략을 실행하는 메서드
        public bool Run(RealS3_K3_Data realData, bool isSimulation, int seedMoney)
        {
            return RunWithResult(realData, isSimulation, seedMoney).Success;
        }

        public StrategyExecutionResult RunWithResult(RealS3_K3_Data realData, bool isSimulation, int seedMoney)
        {
            if (realData == null)
                return FailedExecution();

            var shcode = realData.shcode;
            if (string.IsNullOrWhiteSpace(shcode))
                return FailedExecution();

            var item = _strategyStocks.FirstOrDefault(s => s.Stock?.shcode == shcode);
            if (item?.Strategy == null) return FailedExecution();
            if (isSimulation) return Execute(item, realData, true, seedMoney, new());
            return RunLiveStep(item, realData, seedMoney);
        }

        internal StrategyExecutionResult RunLiveStep(StrategyStockInfo item, RealS3_K3_Data realData, int seedMoney = 0)
        {
            var shcode = realData.shcode;
            if (!_runningStocks.TryAdd(shcode, 0))
            {
                return BusyExecution();
            }

            try { return Execute(item, realData, false, seedMoney, _liveContexts.GetOrAdd(shcode, _ => new())); }
            finally { _runningStocks.TryRemove(shcode, out _); }
        }

        internal StrategyExecutionResult RunSimulationStep(StrategyStockInfo item, RealS3_K3_Data bar,
            int seedMoney, Dictionary<string, object> context) => Execute(item, bar, true, seedMoney, context);

        private StrategyExecutionResult Execute(StrategyStockInfo item, RealS3_K3_Data realData,
            bool isSimulation, int seedMoney, Dictionary<string, object> context)
        {
            var shcode = realData.shcode;
            var scope = isSimulation ? "시뮬레이션" : "실시간";
            var stage = "주전략";
            var scriptName = string.Empty;
            try
            {
                if (item.Strategy == null)
                    return FailedExecution();

                var mainFullPath = GetScriptFullPath(item.Strategy.MainStrategyPath, "main");
                if (string.IsNullOrEmpty(mainFullPath))
                    return FailedExecution();
                scriptName = Path.GetFileName(mainFullPath);
                if (!File.Exists(mainFullPath))
                {
                    LogStrategyProblem(shcode, $"[{scope}] 주전략 파일 없음 · {scriptName}");
                    return FailedExecution();
                }

                var sellFullPath = GetScriptFullPath(item.Strategy.SellStrategyPath, "sell");
                var buyFullPath = GetScriptFullPath(item.Strategy.BuyStrategyPath, "buy");

                using var dbContext = _dbFactory.CreateDbContext();
                var globals = new StrategyGlobals
                {
                    Stock = item.Stock, RealData = realData, DbContext = dbContext, Context = context,
                    StockDataService = _stockDataService, Logging = _loggingService,
                    IsSimulation = isSimulation, SeedMoney = seedMoney
                };
                var result = RunScript(mainFullPath, globals);

                if (result is not ScriptResult scriptResult)
                {
                    LogStrategyProblem(shcode, $"[{scope}] {scriptName} · 주전략이 ScriptResult를 반환하지 않았습니다.");
                    return FailedExecution();
                }

                if (!scriptResult.Success)
                {
                    LogStrategyProblem(shcode, $"[{scope}] {scriptName} · 주전략 실패 · {scriptResult.Message}");
                    return new StrategyExecutionResult(false, scriptResult.Message, scriptResult, null);
                }

                if (scriptResult.Message is "BUY" or "SELL")
                {
                    var action = LoggingService.TradeActionLabel(scriptResult.Message);
                    _loggingService.Log($"[주식 전략 신호] {scope} · {shcode} · {scriptName} · {action} {scriptResult.Count}주 · 기준가 {realData.price:N0} · 시각 {realData.chetime}");
                    stage = $"{action} 스크립트";
                    var orderScript = scriptResult.Message == "BUY" ? buyFullPath : sellFullPath;
                    scriptName = Path.GetFileName(orderScript);
                    return RunOrderScript(scriptResult.Message, orderScript, globals, scriptResult);
                }

                return new StrategyExecutionResult(scriptResult.Success, scriptResult.Message, scriptResult, null);
            }
            catch (Exception ex)
            {
                LogStrategyProblem(shcode, $"[{scope}] {stage} · {scriptName} · 시각 {realData.chetime} · {ex.GetType().Name}: {ex.Message}");
                return FailedExecution();
            }
        }

        private readonly ConcurrentDictionary<string, (string Message, DateTime LoggedAtUtc)> _lastStrategyProblem = new();

        private void LogStrategyProblem(string symbol, string message)
        {
            var now = DateTime.UtcNow;
            // Tick timestamps change even when the same script error is repeated.
            var fingerprint = System.Text.RegularExpressions.Regex.Replace(message, @" · 시각 \d+", "");
            if (_lastStrategyProblem.TryGetValue(symbol, out var previous) && previous.Message == fingerprint
                && now - previous.LoggedAtUtc < TimeSpan.FromSeconds(30)) return;
            _lastStrategyProblem[symbol] = (fingerprint, now);
            _loggingService.Log($"[주식 전략 오류] {symbol} · {message}");
        }

        public void ResetContext(string shcode) => _liveContexts.TryRemove(shcode, out _);

        private StrategyExecutionResult RunOrderScript(
            string action,
            string scriptPath,
            StrategyGlobals globals,
            ScriptResult mainResult)
        {
            if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            {
                LogStrategyProblem(globals.Stock.shcode, $"{LoggingService.TradeActionLabel(action)} 스크립트 파일 없음 · {Path.GetFileName(scriptPath)}");
                return new StrategyExecutionResult(false, action, mainResult, null);
            }

            var orderResult = RunScript(scriptPath, globals) as ScriptResult;
            var scope = globals.IsSimulation ? "시뮬레이션" : "실시간";
            var outcome = orderResult is null ? "응답 없음" : orderResult.Success ? "성공" : "실패";
            _loggingService.Log($"[주식 매매 스크립트] {scope} · {globals.Stock.shcode} · {Path.GetFileName(scriptPath)} · {outcome} · {orderResult?.Message} · {orderResult?.Count ?? 0}주");

            return new StrategyExecutionResult(
                mainResult.Success && orderResult?.Success == true,
                action,
                mainResult,
                orderResult);
        }

        private object? RunScript(string scriptPath, StrategyGlobals globals)
        {
            var task = Task.Run(() => _scriptRunner.RunScriptFromFileAsync(scriptPath, globals, TimeSpan.FromSeconds(5), CancellationToken.None));
            return task.GetAwaiter().GetResult();
        }

        private static StrategyExecutionResult FailedExecution()
        {
            return new StrategyExecutionResult(false, "NONE", null, null);
        }

        private static StrategyExecutionResult BusyExecution()
        {
            return new StrategyExecutionResult(false, "BUSY", null, null);
        }

        public void AddStock(StockInfo stock)
        {
            if (stock == null)
                return;

            if (!_strategyStocks.Any(s => s.Stock.shcode == stock.shcode))
            {
                _strategyStocks.Add(new StrategyStockInfo { Stock = stock });
                SaveConfig();
            }
        }

        public void RemoveStock(StockInfo stock)
        {
            if (stock == null)
                return;

            var toRemove = _strategyStocks.FirstOrDefault(s => s.Stock.shcode == stock.shcode);
            if (toRemove != null)
            {
                _strategyStocks.Remove(toRemove);
                ResetContext(stock.shcode);
                SaveConfig();
            }
        }

        public void LoadConfig()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = File.ReadAllText(_configPath);
                    var list = JsonSerializer.Deserialize<List<StrategyStockInfo>>(json);

                    _strategyStocks.Clear();
                    _liveContexts.Clear();
                    if (list != null)
                    {
                        foreach (var item in list)
                        {
                            _strategyStocks.Add(item);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _loggingService.Log($"설정 로드 오류: {ex.Message}");
                }
            }
        }

        public void SaveConfig()
        {
            try
            {
                var json = JsonSerializer.Serialize(_strategyStocks.ToList(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                _loggingService.Log($"설정 저장 오류: {ex.Message}");
            }
        }

        private string GetScriptFullPath(string scriptFile, string folderName)
        {
            if (string.IsNullOrWhiteSpace(scriptFile))
                return string.Empty;
            
            var relPath = Path.IsPathRooted(scriptFile) ? scriptFile : Path.Combine("strategy", folderName, scriptFile);
            return Path.IsPathRooted(relPath) ? relPath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relPath);
        }

        // 프로그램 시작시 관심종목의 데이터를 최신으로 업데이트
        public void UpdateLatestWatchlistData()
        {
            using var dbContext = _dbFactory.CreateDbContext();

            foreach (var item in _strategyStocks)
            {
                if (item.Stock != null)
                {
                    // DB 에서 가장 최근 데이터의 날짜 조회 (문자열 "yyyyMMdd" 형태)
                    var latestData = dbContext.StockChartDatas
                        .Where(x => x.shcode == item.Stock.shcode)
                        .OrderByDescending(x => x.date)
                        .FirstOrDefault();

                    DateTime sdate;
                    DateTime edate = DateTime.Today;

                    if (latestData != null && DateTime.TryParseExact(latestData.date, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var lastDate))
                    {
                        sdate = lastDate;
                    }
                    else
                    {
                        // 데이터가 아예 없으면 8년치 요청으로 기본 설정
                        sdate = edate.AddYears(-8);
                    }

                    // 최신 날짜가 오늘과 일치하면 스킵
                    if (sdate.Date >= edate.Date)
                        continue;

                    // 주말 제외 처리 로직 (공휴일은 별도 캘린더나 API 없이는 완벽히 거르기 어려우므로 주말만 거름)
                    // 시작일 부터 오늘까지 영업일이 있는지 체크
                    bool hasTradingDay = false;
                    for (DateTime date = sdate.Date; date <= edate.Date; date = date.AddDays(1))
                    {
                        if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                        {
                            hasTradingDay = true;
                            break;
                        }
                    }

                    if (!hasTradingDay)
                        continue;

                    _stockDataService.RequestStockChartData(item.Stock.shcode, sdate, edate);
                }
            }
        }
    }
}
