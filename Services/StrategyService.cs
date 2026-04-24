using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services
{
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
        public Dictionary<string, object> Context { get; set; }
    }

    // 주식 매매 전략을 관장하는 서비스
    public class StrategyService
    {
        private readonly IDbContextFactory<DumbTraderDbContext> _dbFactory;
        private readonly StockDataService _stockDataService;

        // 관심종목과 해당 전략 정보를 담는 리스트
        private ObservableCollection<StrategyStockInfo> _strategyStocks;
        public ObservableCollection<StrategyStockInfo> StrategyStocks => _strategyStocks;

        private Collection<StrategyGlobals> _strategyGlobalsCollection = new Collection<StrategyGlobals>();

        // 설정 파일 경로
        private readonly string _configPath;

        // Roslyn 스크립트 실행기
        private readonly RoslynScriptRunner _scriptRunner;
        
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
            if (realData == null)
                return false;

            // shcode 기준으로 관심 종목 리스트에서 해당 종목 찾기
            var item = _strategyStocks.FirstOrDefault(s => s.Stock?.shcode == realData.shcode);
            if (item == null)
                return false;

            var strategy = item.Strategy;
            if (strategy == null)
                return false;

            // 경로가 절대경로가 아닌 경우 앱 베이스 디렉터리 기준으로 해석
            var mainFullPath = GetScriptFullPath(strategy.MainStrategyPath, "main");
            if (string.IsNullOrEmpty(mainFullPath) || !File.Exists(mainFullPath))
                return false;

            // 매도/매수 전략 경로 재구성 (사용이 필요한 경우를 대비해 구현)
            var sellFullPath = GetScriptFullPath(strategy.SellStrategyPath, "sell");
            var buyFullPath = GetScriptFullPath(strategy.BuyStrategyPath, "buy");

            try
            {
                using var dbContext = _dbFactory.CreateDbContext();

                // globals로 안전하게 필요한 데이터만 전달
                var globals = _strategyGlobalsCollection.FirstOrDefault(g => g.Stock.shcode == item.Stock.shcode);
                if (globals != null)
                {
                    globals.Stock = item.Stock;
                    globals.RealData = realData;
                    globals.DbContext = dbContext;
                    globals.StockDataService = _stockDataService;
                    globals.Logging = _loggingService;
                    globals.IsSimulation = isSimulation;
                    globals.SeedMoney = seedMoney;
                }
                

                // 동기 환경에서도 안전하게 실행되도록 ThreadPool에서 실행하고 결과를 기다림
                var task = Task.Run(() => _scriptRunner.RunScriptFromFileAsync(mainFullPath, globals, TimeSpan.FromSeconds(5), CancellationToken.None));
                var result = task.GetAwaiter().GetResult();

                if (result != null)
                {
                    if (result is ScriptResult scriptResult)
                    {
                        // TODO: ScriptResult 기반 매매 처리 로직 구현
                        if (scriptResult.Message == "BUY")
                        {
                            // 매수 로직 실행
                            var buyTask = Task.Run(() => _scriptRunner.RunScriptFromFileAsync(buyFullPath, globals, TimeSpan.FromSeconds(5), CancellationToken.None));
                            var buyResult = buyTask.GetAwaiter().GetResult();

                            if (buyResult != null)
                            {
                                if (buyResult is ScriptResult buyScriptResult)
                                {
                                    _loggingService.Log($"매수 전략 실행 결과: Success={buyScriptResult.Success}, Message={buyScriptResult.Message}, Count={buyScriptResult.Count}");
                                    // TODO : 매수 결과에 따른 추가 처리 (예: 포트폴리오 업데이트, 알림 발송 등)
                                }
                            }
                        }
                        else if (scriptResult.Message == "SELL")
                        {
                            // 매도 로직 실행
                            var sellTask = Task.Run(() => _scriptRunner.RunScriptFromFileAsync(sellFullPath, globals, TimeSpan.FromSeconds(5), CancellationToken.None));
                            var sellResult = sellTask.GetAwaiter().GetResult();

                            if (sellResult != null)
                            {
                                if (sellResult is ScriptResult sellScriptResult)
                                {
                                    _loggingService.Log($"매도 전략 실행 결과: Success={sellScriptResult.Success}, Message={sellScriptResult.Message}, Count={sellScriptResult.Count}");
                                    // TODO : 매도 결과에 따른 추가 처리 (예: 포트폴리오 업데이트, 알림 발송 등)
                                }
                            }
                        }
                        else
                        {
                            _loggingService.Log($"전략 실행 결과: Success={scriptResult.Success}, Message={scriptResult.Message}, Count={scriptResult.Count}");
                            // TODO : 기타 전략 결과에 따른 처리 (예: 로그 기록, 알림 발송 등)
                        }
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _loggingService.Log($"전략 실행 오류 ({realData.shcode}): {ex.Message}");
                return false;
            }
        }

        public void AddStock(StockInfo stock)
        {
            if (stock == null)
                return;

            if (!_strategyStocks.Any(s => s.Stock.shcode == stock.shcode))
            {
                _strategyStocks.Add(new StrategyStockInfo { Stock = stock });
                _strategyGlobalsCollection.Add(new StrategyGlobals { Stock = stock });
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
                var globalsToRemove = _strategyGlobalsCollection.FirstOrDefault(g => g.Stock.shcode == stock.shcode);
                if (globalsToRemove != null)
                {
                    _strategyGlobalsCollection.Remove(globalsToRemove);
                }
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
                    _strategyGlobalsCollection.Clear();
                    if (list != null)
                    {
                        foreach (var item in list)
                        {
                            _strategyStocks.Add(item);
                            _strategyGlobalsCollection.Add(new StrategyGlobals { Stock = item.Stock });
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
