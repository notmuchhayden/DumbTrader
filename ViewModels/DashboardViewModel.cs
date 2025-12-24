using DumbTrader.Services;
using DumbTrader.Core;
using System;
using System.Windows.Input;

namespace DumbTrader.ViewModels
{
    public class DashboardViewModel : ViewModelBase
    {
        private readonly IXingSessionService _sessionService;

        private string _title = "Dumb Trader - Main";
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        private string _welcomeMessage = "Welcome";
        public string WelcomeMessage
        {
            get => _welcomeMessage;
            set => SetProperty(ref _welcomeMessage, value);
        }

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand Test1Command { get; }
        public ICommand Test2Command { get; }
        public ICommand Test3Command { get; }

        public DashboardViewModel(IXingSessionService sessionService)
        {
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            Test1Command = new RelayCommand(ExecuteTest1);
            Test2Command = new RelayCommand(ExecuteTest2);
            Test3Command = new RelayCommand(ExecuteTest3);
        }

        private void ExecuteTest1(object? parameter) { }
        private void ExecuteTest2(object? parameter) { }
        private void ExecuteTest3(object? parameter) { }
    }
}
