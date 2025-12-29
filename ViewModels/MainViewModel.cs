using System;
using System.Windows;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Views;
using DumbTrader.Services;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace DumbTrader.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly Services.XAWorker _worker;
        private readonly IXASessionService _sessionService;
        private readonly AccountService _accountService;
        private readonly IServiceProvider _serviceProvider;

        public ICommand ExitCommand { get; }

        public object? CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }
        private object? _currentView;

        public SidebarViewModel SidebarViewModel { get; }

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

        public MainViewModel(XAWorker worker, AccountService accountService, IServiceProvider serviceProvider)
        {
            _worker = worker ?? throw new ArgumentNullException(nameof(worker));
            _sessionService = worker.SessionService;
            _accountService = accountService;
            _serviceProvider = serviceProvider;

            SidebarViewModel = new SidebarViewModel();
            SidebarViewModel.PropertyChanged += SidebarViewModel_PropertyChanged;

            // Default view
            CurrentView = new DashboardView { DataContext = _serviceProvider.GetRequiredService<DashboardViewModel>() };

            ExitCommand = new RelayCommand(ExecuteExit);
        }

        private void SidebarViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SidebarViewModel.SelectedIndex))
            {
                switch (SidebarViewModel.SelectedIndex)
                {
                    case 0:
                        CurrentView = new DashboardView { DataContext = _serviceProvider.GetRequiredService<DashboardViewModel>() };
                        break;
                    case 1:
                        CurrentView = new AccountView { DataContext = _serviceProvider.GetRequiredService<AccountViewModel>() };
                        break;
                    // Add more cases for other navigation items if needed
                    default:
                        CurrentView = null;
                        break;
                }
            }
        }

        private void ExecuteExit(object? parameter)
        {
            if (parameter is Window win)
                win.Close();
            else
                Application.Current?.Shutdown();
        }
    }
}
