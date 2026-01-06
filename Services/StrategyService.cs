using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DumbTrader.Models;

namespace DumbTrader.Services
{
    public class StrategyService
    {
        private readonly DumbTraderDbContext _dbContext;
        private ObservableCollection<StrategyStock> _strategyStocks;

        public StrategyService(DumbTraderDbContext dbContext)
        {
            _dbContext = dbContext;
            _strategyStocks = new ObservableCollection<StrategyStock>();
        }

        public ObservableCollection<StrategyStock> StrategyStocks => _strategyStocks;

        public void AddStock(StockInfo stock)
        {
            if (!_strategyStocks.Any(s => s.Stock.shcode == stock.shcode))
            {
                _strategyStocks.Add(new StrategyStock { Stock = stock });
            }
        }

        public void RemoveStock(StockInfo stock)
        {
            var toRemove = _strategyStocks.FirstOrDefault(s => s.Stock.shcode == stock.shcode);
            if (toRemove != null)
            {
                _strategyStocks.Remove(toRemove);
            }
        }
    }
}
