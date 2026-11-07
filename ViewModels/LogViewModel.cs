using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Services;

namespace DumbTrader.ViewModels
{
    public class LogViewModel : ViewModelBase
    {
        public ObservableCollection<string> Logs => _loggingService.Logs;
        public string LogText => string.Join(Environment.NewLine, Logs);
        public ICommand CopyLogsCommand { get; }
        private readonly LoggingService _loggingService;
        private readonly RelayCommand _copyLogsCommand;

        public LogViewModel(LoggingService loggingService)
        {
            _loggingService = loggingService;
            _copyLogsCommand = new RelayCommand(_ => Clipboard.SetText(LogText), _ => Logs.Count > 0);
            CopyLogsCommand = _copyLogsCommand;
            Logs.CollectionChanged += OnLogsChanged;
        }

        private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(LogText));
            _copyLogsCommand.RaiseCanExecuteChanged();
        }
    }
}
