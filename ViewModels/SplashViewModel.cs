using System;
using System.Threading.Tasks;
using DumbTrader.Services;

namespace DumbTrader.ViewModels
{
    public class SplashViewModel
    {
        public event Action InitializationComplete;

        public SplashViewModel()
        {
        }

        public async Task InitializeAsync()
        {
            var task1 = Task.Delay(2000);

            await Task.WhenAll(task1);

            InitializationComplete?.Invoke();
        }
    }
}
