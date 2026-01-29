using System;
using System.Collections.ObjectModel;

namespace DumbTrader.Services
{
    public class LoggingService
    {
        public ObservableCollection<string> Logs { get; } = new();

        public void Log(string message)
        {
            Logs.Insert(0, $"{DateTime.Now:HH:mm} {message}");
        }
    }
}
