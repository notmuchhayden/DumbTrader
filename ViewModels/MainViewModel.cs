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

        public MainViewModel(IXASessionService sessionService, AccountService accountService, IServiceProvider serviceProvider)
        {
            _sessionService = sessionService;
            _accountService = accountService;
            _serviceProvider = serviceProvider;

            SidebarViewModel = new SidebarViewModel();
            // SidebarViewModel에서 계정 메뉴가 첫 항목이므로 주식 대시보드 인덱스를 초기 선택한다.
            SidebarViewModel.SelectedIndex = 1;
            SidebarViewModel.PropertyChanged += SidebarViewModel_PropertyChanged;

            // Default view
            CurrentView = new StockDashboardView { DataContext = _serviceProvider.GetRequiredService<StockDashboardViewModel>() };

            ExitCommand = new RelayCommand(ExecuteExit);
        }

        private void SidebarViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SidebarViewModel.SelectedIndex))
            {
                switch (SidebarViewModel.SelectedIndex)
                {
                    case 0: // 계정관리
                        CurrentView = new AccountView { DataContext = _serviceProvider.GetRequiredService<AccountViewModel>() };
                        break;
                    case 1: // 주식종목모니터
                        CurrentView = new StockDashboardView { DataContext = _serviceProvider.GetRequiredService<StockDashboardViewModel>() };
                        break;
                    case 2: // 주식관심종목관리
                        CurrentView = new StockWatchlistView { DataContext = _serviceProvider.GetRequiredService<StockWatchlistViewModel>() };
                        break;
                    case 3: // 주식개별종목관리
                        CurrentView = new StockDetailView { DataContext = _serviceProvider.GetRequiredService<StockDetailViewModel>() };
                        break;
                    case 4: // 해외선물종목모니터
                        CurrentView = new OFutureDashboardView { DataContext = _serviceProvider.GetRequiredService<OFutureDashboardViewModel>() };
                        break;
                    case 5: // 해외선물관심종목관리
                        CurrentView = new OFutureWatchlistView { DataContext = _serviceProvider.GetRequiredService<OFutureWatchlistViewModel>() };
                        break;
                    case 6: // 해외선물개별종목관리
                        CurrentView = new OFutureDetailView { DataContext = _serviceProvider.GetRequiredService<OFutureDetailViewModel>() };
                        break;
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
