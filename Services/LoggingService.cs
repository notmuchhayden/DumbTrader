using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace DumbTrader.Services
{
    public class LoggingService
    {
        private const int MaxLogs = 500;
        private readonly object _pendingLock = new();
        private readonly Queue<string> _pending = new();
        private readonly Dispatcher? _dispatcher = Application.Current?.Dispatcher;
        private bool _flushScheduled;

        public ObservableCollection<string> Logs { get; } = new();
        public event EventHandler? LogsUpdated;

        public void Log(string message)
        {
            var entry = $"{DateTime.Now:HH:mm} {message}";
            lock (_pendingLock)
            {
                if (_dispatcher is null)
                {
                    AppendLogs(new[] { entry });
                    return;
                }

                if (_dispatcher.HasShutdownStarted)
                    return;

                if (_pending.Count == MaxLogs)
                    _pending.Dequeue();
                _pending.Enqueue(entry);

                if (_flushScheduled)
                    return;

                _flushScheduled = true;
                _dispatcher.InvokeAsync(FlushPending, DispatcherPriority.Background);
            }
        }

        private void FlushPending()
        {
            string[] entries;
            lock (_pendingLock)
            {
                entries = _pending.ToArray();
                _pending.Clear();
                _flushScheduled = false;
            }

            AppendLogs(entries);
        }

        private void AppendLogs(IEnumerable<string> entries)
        {
            foreach (var entry in entries)
            {
                if (Logs.Count == MaxLogs)
                    Logs.RemoveAt(Logs.Count - 1);
                Logs.Insert(0, entry);
            }

            LogsUpdated?.Invoke(this, EventArgs.Empty);
        }
    }
}
