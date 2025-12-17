using DumbTrader.ViewModels;
using DumbTrader.Views;
using System.Windows;

namespace DumbTrader
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Prevent app from shutting down when the login dialog closes
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var sessionService = new DumbTrader.Services.XingSessionService();

            var loginView = new LoginView();
            var loginViewModel = new LoginViewModel(sessionService);
            loginView.DataContext = loginViewModel;

            var result = loginView.ShowDialog();
            if (result == true)
            {
                var mainWindow = new MainWindow();
                mainWindow.DataContext = new MainViewModel(sessionService);
                // Designate as main window and resume normal shutdown behavior
                MainWindow = mainWindow;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();
            }
            else
            {
                // Login cancelled/failed -> exit
                Shutdown();
            }
        }
    }
}
