using System;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Services;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.ViewModels
{
    public class DashboardViewModel : ViewModelBase
    {
        private readonly StrategyService _strategyService;
        private readonly DumbTraderDbContext _dbContext;

        public DashboardViewModel(StrategyService strategyService,
            DumbTraderDbContext dbContext)
        {
            _strategyService = strategyService;
            _dbContext = dbContext;
        }
    }
}
