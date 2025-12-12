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

            var loginView = new LoginView();
            var loginViewModel = new LoginViewModel();
            loginView.DataContext = loginViewModel;
            loginView.Show();
        }
    }
}
