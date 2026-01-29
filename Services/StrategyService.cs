using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DumbTrader.Models;

namespace DumbTrader.Services
{
    // 주식 매매 전략을 관장하는 서비스
    public class StrategyService
    {
        private ObservableCollection<StrategyStockInfo> _strategyStocks;
        private readonly string _configPath;

        public StrategyService()
        {
            _strategyStocks = new ObservableCollection<StrategyStockInfo>();
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            LoadConfig();
        }

        public ObservableCollection<StrategyStockInfo> StrategyStocks => _strategyStocks;

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
                    if (list != null)
                    {
                        _strategyStocks = new ObservableCollection<StrategyStockInfo>(list);
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
