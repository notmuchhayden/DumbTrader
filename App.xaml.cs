using DumbTrader.Views;
using Microsoft.Extensions.DependencyInjection;
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

            // Build DI container
            var services = new ServiceCollection();

            // 서비스 등록
            services.AddSingleton<Services.DumbTraderDbContext>();
            services.AddSingleton(sp => new Services.AccountService(sp.GetRequiredService<Services.DumbTraderDbContext>()));
            services.AddSingleton<Services.LoginService>();
            services.AddSingleton<Services.IXASessionService, Services.XASessionService>();
            services.AddSingleton(sp => new Services.StockDataService(sp.GetRequiredService<Services.DumbTraderDbContext>()));
            services.AddSingleton<Services.StockRealDataService>();
            services.AddSingleton<Services.StrategyService>();

            // Register ViewModels
            services.AddTransient(sp => new ViewModels.LoginViewModel(
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.LoginService>()));

            services.AddTransient(sp => new ViewModels.MainViewModel(
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.AccountService>(), sp));
            services.AddTransient<ViewModels.SidebarViewModel>();
            services.AddTransient(sp => new ViewModels.SummaryViewModel(sp.GetRequiredService<Services.AccountService>()));
            services.AddTransient<ViewModels.LogViewModel>();
            services.AddTransient<ViewModels.DashboardViewModel>();
            services.AddTransient(sp => new ViewModels.AccountViewModel(
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.AccountService>()));
            services.AddTransient(sp => new ViewModels.WatchlistViewModel(
                sp.GetRequiredService<Services.StockDataService>(),
                sp.GetRequiredService<Services.StrategyService>()));

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
