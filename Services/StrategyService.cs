using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using DumbTrader.Models;

namespace DumbTrader.Services
{
    // 주식 매매 전략을 관장하는 서비스
    public class StrategyService
    {
        // 관심종목과 해당 전략 정보를 담는 리스트
        private ObservableCollection<StrategyStockInfo> _strategyStocks;
        public ObservableCollection<StrategyStockInfo> StrategyStocks => _strategyStocks;

        // 설정 파일 경로
        private readonly string _configPath;

        // Roslyn 스크립트 실행기
        private readonly RoslynScriptRunner _scriptRunner;

        public StrategyService()
        {
            _strategyStocks = new ObservableCollection<StrategyStockInfo>();
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            _scriptRunner = new RoslynScriptRunner();

            // 앱 시작 시 설정 파일에서 관심종목과 전략 정보 로드
            LoadConfig();
        }

        // 주어진 종목코드에 대해 해당 전략을 실행하는 메서드
        public bool Run(string shcode)
        {
            if (string.IsNullOrEmpty(shcode))
                return false;

            var item = _strategyStocks.FirstOrDefault(s => s.Stock?.shcode == shcode);
            if (item == null)
                return false;

            var strategy = item.Strategy;
            if (strategy == null)
                return false;

            var scriptPath = strategy.MainStrategyPath;
            if (string.IsNullOrWhiteSpace(scriptPath))
                return false;

            // 경로가 절대경로가 아닌 경우 앱 베이스 디렉터리 기준으로 해석
            var fullPath = Path.IsPathRooted(scriptPath) ? scriptPath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, scriptPath);
            if (!File.Exists(fullPath))
                return false;

            try
            {
                // globals로 안전하게 필요한 데이터만 전달
                var globals = new
                {
                    Stock = item.Stock,
                };

                // 동기 환경에서도 안전하게 실행되도록 ThreadPool에서 실행하고 결과를 기다림
                var task = Task.Run(() => _scriptRunner.RunScriptFromFileAsync(fullPath, globals, TimeSpan.FromSeconds(5), CancellationToken.None));
                var result = task.GetAwaiter().GetResult();

                // 결과 해석: 스크립트에서 문자열 "BUY"/"SELL" 등을 반환하도록 규약을 둘 수 있음
                // 여기서는 스크립트가 정상적으로 실행되면 true 반환
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
