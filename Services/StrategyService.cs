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
        public bool IsSimulation { get; set; }
        public int SeedMoney { get; set; }
    }

    // 주식 매매 전략을 관장하는 서비스
    public class StrategyService
    {
        private readonly IDbContextFactory<DumbTraderDbContext> _dbFactory;

        // 관심종목과 해당 전략 정보를 담는 리스트
        private ObservableCollection<StrategyStockInfo> _strategyStocks;
        public ObservableCollection<StrategyStockInfo> StrategyStocks => _strategyStocks;

        // 설정 파일 경로
        private readonly string _configPath;

        // Roslyn 스크립트 실행기
        private readonly RoslynScriptRunner _scriptRunner;

        public StrategyService(IDbContextFactory<DumbTraderDbContext> dbFactory)
        {
            _dbFactory = dbFactory;

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

            // shcode 기준으로 관심종목 리스트에서 해당 종목 찾기
            var item = _strategyStocks.FirstOrDefault(s => s.Stock?.shcode == realData.shcode);
            if (item == null)
                return false;

            var strategy = item.Strategy;
            if (strategy == null)
                return false;

            // strategy.MainStrategyPath 에는 파일 명만 들어 있음.
            // 따라서 아래처럼 폴더를 붙여서 전체 경로를 만들어야 함.
            // 주전략 : ./strategy/main/*.cs
            // 매도전략 : ./strategy/sell/*.cs
            // 매수전략 : ./strategy/buy/*.cs 
            // 경로가 절대경로가 아닌 경우 앱 베이스 디렉터리 기준으로 해석
            var mainScriptFile = strategy.MainStrategyPath;
            if (string.IsNullOrWhiteSpace(mainScriptFile))
                return false;
            
            var mainScriptRelPath = Path.IsPathRooted(mainScriptFile) ? mainScriptFile : Path.Combine("strategy", "main", mainScriptFile);
            var mainFullPath = Path.IsPathRooted(mainScriptRelPath) ? mainScriptRelPath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, mainScriptRelPath);
            if (!File.Exists(mainFullPath))
                return false;

            // 매도/매수 전략 경로 재구성 (사용이 필요한 경우를 대비해 구현)
            var sellScriptFile = strategy.SellStrategyPath;
            var sellFullPath = string.Empty;
            if (!string.IsNullOrWhiteSpace(sellScriptFile))
            {
                var sellScriptRelPath = Path.IsPathRooted(sellScriptFile) ? sellScriptFile : Path.Combine("strategy", "sell", sellScriptFile);
                sellFullPath = Path.IsPathRooted(sellScriptRelPath) ? sellScriptRelPath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, sellScriptRelPath);
            }

            var buyScriptFile = strategy.BuyStrategyPath;
            var buyFullPath = string.Empty;
            if (!string.IsNullOrWhiteSpace(buyScriptFile))
            {
                var buyScriptRelPath = Path.IsPathRooted(buyScriptFile) ? buyScriptFile : Path.Combine("strategy", "buy", buyScriptFile);
                buyFullPath = Path.IsPathRooted(buyScriptRelPath) ? buyScriptRelPath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, buyScriptRelPath);
            }

            try
            {
                using var dbContext = _dbFactory.CreateDbContext();

                // globals로 안전하게 필요한 데이터만 전달
                var globals = new StrategyGlobals
                {
                    Stock = item.Stock,
                    RealData = realData,
                    DbContext = dbContext,
                    IsSimulation = isSimulation,
                    SeedMoney = seedMoney
                };

                // 동기 환경에서도 안전하게 실행되도록 ThreadPool에서 실행하고 결과를 기다림
                var task = Task.Run(() => _scriptRunner.RunScriptFromFileAsync(mainFullPath, globals, TimeSpan.FromSeconds(5), CancellationToken.None));
                var result = task.GetAwaiter().GetResult();

                if (result != null)
                {
                    if (result == "BUY")
                    {
                        // 매수 로직 실행
                        var buyTask = Task.Run(() => _scriptRunner.RunScriptFromFileAsync(buyFullPath, globals, TimeSpan.FromSeconds(5), CancellationToken.None));
                        var buyResult = buyTask.GetAwaiter().GetResult();
                    }
                    else if (result == "SELL")
                    {
                        // 매도 로직 실행
                        var sellTask = Task.Run(() => _scriptRunner.RunScriptFromFileAsync(sellFullPath, globals, TimeSpan.FromSeconds(5), CancellationToken.None));
                        var sellResult = sellTask.GetAwaiter().GetResult();
                    }
                }
                
                return true;
            }
            catch (Exception)
            {
                // 필요시 로깅 추가
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
                    // Handle or log error as needed
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
                // Handle or log error as needed
            }
        }
    }
}
