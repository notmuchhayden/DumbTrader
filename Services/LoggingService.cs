using System;
using System.Collections.ObjectModel;
using System.Windows.Data;

namespace DumbTrader.Services
{
    public class LoggingService
    {
        private readonly object _logsLock = new object();

        public ObservableCollection<string> Logs { get; } = new();

        public LoggingService()
        {
            BindingOperations.EnableCollectionSynchronization(Logs, _logsLock);
        }

        public void Log(string message)
        {
            lock (_logsLock)
            {
                Logs.Insert(0, $"{DateTime.Now:HH:mm} {message}");
                
                // 최대 500개까지만 보관하고 오래된 로그(마지막 항목) 삭제
                while (Logs.Count > 500)
                {
                    Logs.RemoveAt(Logs.Count - 1);
                }
            }
        }
    }
}
