using System;
using System.Threading.Tasks;
using DumbTrader.Services;

namespace DumbTrader.ViewModels
{
    public class SplashViewModel
    {
        private readonly StrategyService _strategyService;

        public event Action InitializationComplete;

        public SplashViewModel(StrategyService strategyService)
        {
            _strategyService = strategyService;
        }

        public async Task InitializeAsync()
        {
            var task1 = Task.Run(() =>
            {
                _strategyService.UpdateLatestWatchlistData();
            });
            var task2 = Task.Delay(3000);

            await Task.WhenAll(task1, task2);

            InitializationComplete?.Invoke();
        }
    }
}
