using DumbTrader.Views;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace DumbTrader
{
    public partial class App : Application
    {
        private ServiceProvider? _serviceProvider;
        public static IServiceProvider? ServiceProvider { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ensure strategy folders exist: strategy/main, strategy/sell, strategy/buy
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Environment.CurrentDirectory;
                var strategyRoot = Path.Combine(baseDir, "strategy");
                Directory.CreateDirectory(Path.Combine(strategyRoot, "main"));
                Directory.CreateDirectory(Path.Combine(strategyRoot, "sell"));
                Directory.CreateDirectory(Path.Combine(strategyRoot, "buy"));
            }
            catch (Exception ex)
            {
                // Fail-safe: if we cannot prepare required folders, stop the application
                MessageBox.Show($"초기화 중 오류가 발생했습니다: {ex.Message}", "초기화 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // Build DI container
            var services = new ServiceCollection();

            // 서비스 등록 ========================================
            services.AddSingleton<Services.DumbTraderDbContext>();
            services.AddSingleton(sp => new Services.AccountService(sp.GetRequiredService<Services.DumbTraderDbContext>()));
            services.AddSingleton<Services.LoginService>();
            services.AddSingleton<Services.IXASessionService, Services.XASessionService>();
            services.AddSingleton(sp => new Services.StockDataService(sp.GetRequiredService<Services.DumbTraderDbContext>()));
            services.AddSingleton<Services.StockRealDataService>();
            services.AddSingleton<Services.StrategyService>();
            services.AddSingleton<Services.LoggingService>();

            // Register ViewModels ========================================
            // 로그인 ViewModel 등록
            services.AddTransient(sp => new ViewModels.LoginViewModel(
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.LoginService>()));

            // 메인 ViewModel 등록
            services.AddTransient(sp => new ViewModels.MainViewModel(
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.AccountService>(), sp));
            // 사이드바 ViewModel 등록
            services.AddTransient<ViewModels.SidebarViewModel>();
            // 요약 ViewModel 등록
            services.AddTransient(sp => new ViewModels.SummaryViewModel(sp.GetRequiredService<Services.AccountService>()));
            // 로그 ViewModel 등록
            services.AddTransient(sp => new ViewModels.LogViewModel(
                sp.GetRequiredService<Services.LoggingService>()
            ));
            // 대시보드 ViewModel 등록
            services.AddTransient(sp => new ViewModels.DashboardViewModel(
                sp.GetRequiredService<Services.StrategyService>(),
                sp.GetRequiredService<Services.DumbTraderDbContext>()
            ));
            // 계정관리 ViewModel 등록
            services.AddTransient(sp => new ViewModels.AccountViewModel(
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.AccountService>()));
            // 관심종목관리 ViewModel 등록
            services.AddTransient(sp => new ViewModels.WatchlistViewModel( 
                sp.GetRequiredService<Services.StockDataService>(),
                sp.GetRequiredService<Services.StrategyService>()));
            // 개별종목관리 ViewModel 등록
            services.AddTransient(sp => new ViewModels.StockDetailViewModel(
                sp.GetRequiredService<Services.StockDataService>(),
                sp.GetRequiredService<Services.StrategyService>(),
                sp.GetRequiredService<Services.LoggingService>(),
                sp.GetRequiredService<Services.DumbTraderDbContext>()
            ));

            _serviceProvider = services.BuildServiceProvider();
            ServiceProvider = _serviceProvider;

            // 데이터베이스와 테이블 자동 생성
            _serviceProvider.GetRequiredService<Services.DumbTraderDbContext>().Database.EnsureCreated();

            // Prevent app from shutting down when the login dialog closes
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Resolve and show login
            var loginView = new LoginView();
            loginView.DataContext = _serviceProvider.GetRequiredService<ViewModels.LoginViewModel>();

            var result = loginView.ShowDialog();
            if (result == true)
            {
                // Ensure we don't create a second MainWindow if one already exists
                var existing = Current?.Windows.OfType<MainWindow>().FirstOrDefault();
                if (existing != null)
                {
                    existing.Activate();
                    existing.Focus();
                    MainWindow = existing;
                    ShutdownMode = ShutdownMode.OnMainWindowClose;
                }
                else
                {
                    var mainWindow = new MainWindow();
                    mainWindow.DataContext = _serviceProvider.GetRequiredService<ViewModels.MainViewModel>();
                    // Designate as main window and resume normal shutdown behavior
                    MainWindow = mainWindow;
                    ShutdownMode = ShutdownMode.OnMainWindowClose;
                    mainWindow.Show();
                }
            }
            else
            {
                // Login cancelled/failed -> exit
                Shutdown();
            }
        }
    }
}
