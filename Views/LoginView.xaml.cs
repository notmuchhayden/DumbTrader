using DumbTrader.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace DumbTrader.Views
{
    public partial class LoginView : Window
    {
        public LoginView()
        {
            InitializeComponent();
            // if DataContext not set by caller, resolve from DI
            if (this.DataContext == null)
            {
                var sp = DumbTrader.App.ServiceProvider as global::System.IServiceProvider;
                if (sp is not null)
                {
                    this.DataContext = sp.GetService(typeof(DumbTrader.ViewModels.LoginViewModel));
                }
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is LoginViewModel viewModel)
            {
                viewModel.Password = ((PasswordBox)sender).Password;
            }
        }
    }
}
