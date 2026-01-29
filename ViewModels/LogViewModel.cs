using System.Collections.ObjectModel;
using DumbTrader.Core;
using DumbTrader.Services;

namespace DumbTrader.ViewModels
{
    public class LogViewModel : ViewModelBase
    {
        public ObservableCollection<string> Logs => _loggingService.Logs;
        private readonly LoggingService _loggingService;
        public LogViewModel(LoggingService loggingService)
        {
            _loggingService = loggingService;
        }
    }
}
