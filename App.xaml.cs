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

            // Register services
            services.AddSingleton<Services.DumbTraderDbContext>();
            services.AddSingleton<Services.AccountService>(sp => new Services.AccountService(sp.GetRequiredService<Services.DumbTraderDbContext>()));
            services.AddSingleton<Services.LoginService>();
            services.AddSingleton<Services.IXASessionService, Services.XASessionService>();
            services.AddSingleton<Services.IXAQueryService, Services.XAQueryService>();
            services.AddSingleton<Services.IXARealService, Services.XARealService>();

            // Register ViewModels
            services.AddTransient(sp => new ViewModels.LoginViewModel(
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.LoginService>()));

            services.AddTransient(sp => new ViewModels.MainViewModel(
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.AccountService>(),sp));
            services.AddTransient<ViewModels.SidebarViewModel>();
            services.AddTransient<ViewModels.SummaryViewModel>();
            services.AddTransient<ViewModels.LogViewModel>();
            services.AddTransient<ViewModels.DashboardViewModel>();
            services.AddTransient(sp => new ViewModels.AccountViewModel(
                sp.GetRequiredService<Services.IXASessionService>(),
                sp.GetRequiredService<Services.AccountService>()
));
            services.AddTransient<ViewModels.WatchlistViewModel>();

            _serviceProvider = services.BuildServiceProvider();
            ServiceProvider = _serviceProvider;

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
