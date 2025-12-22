using DumbTrader.ViewModels;
using DumbTrader.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Windows;

namespace DumbTrader
{
    public partial class App : Application
    {
        private global::Microsoft.Extensions.DependencyInjection.ServiceProvider? _serviceProvider;
        public static global::System.IServiceProvider? ServiceProvider { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Build DI container
            var services = new global::Microsoft.Extensions.DependencyInjection.ServiceCollection();
            // ensure extension methods are available
            // add using via fully qualified type references below

            // Register services
            services.AddSingleton<DumbTrader.Services.IXingSessionService, DumbTrader.Services.XingSessionService>();
            services.AddSingleton<DumbTrader.Services.AccountService>();

            // Register ViewModels
            services.AddTransient<DumbTrader.ViewModels.LoginViewModel>(sp => new DumbTrader.ViewModels.LoginViewModel(sp.GetRequiredService<DumbTrader.Services.IXingSessionService>(), sp.GetRequiredService<DumbTrader.Services.AccountService>()));
            services.AddTransient<DumbTrader.ViewModels.MainViewModel>(sp => new DumbTrader.ViewModels.MainViewModel(
 sp.GetRequiredService<DumbTrader.Services.IXingSessionService>(),
 sp.GetRequiredService<DumbTrader.Services.AccountService>(),
 sp
));
            services.AddTransient<DumbTrader.ViewModels.SidebarViewModel>();
            services.AddTransient<DumbTrader.ViewModels.SummaryViewModel>();
            services.AddTransient<DumbTrader.ViewModels.LogViewModel>();
            services.AddTransient<DumbTrader.ViewModels.DashboardViewModel>();
            services.AddTransient<DumbTrader.ViewModels.AccountViewModel>();

            _serviceProvider = services.BuildServiceProvider();
            ServiceProvider = _serviceProvider;

            // Prevent app from shutting down when the login dialog closes
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Resolve and show login
            var loginView = new LoginView();
            loginView.DataContext = _serviceProvider.GetRequiredService<DumbTrader.ViewModels.LoginViewModel>();

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
                    mainWindow.DataContext = _serviceProvider.GetRequiredService<DumbTrader.ViewModels.MainViewModel>();
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
