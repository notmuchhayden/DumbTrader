using DumbTrader.Core;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace DumbTrader.ViewModels
{
    public class LoginViewModel : ViewModelBase
    {
        private string _username;
        public string Username
        {
            get => _username;
            set
            {
                SetProperty(ref _username, value);
                (LoginCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        // For simplicity in this demo, binding Password directly (insecure). 
        // In production, use PasswordBox parameters or SecureString.
        private string _password; 
        public string Password
        {
            get => _password;
            set
            {
                SetProperty(ref _password, value);
                (LoginCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ICommand LoginCommand { get; }

        public LoginViewModel()
        {
            LoginCommand = new RelayCommand(ExecuteLogin, CanExecuteLogin);
        }

        private bool CanExecuteLogin(object parameter)
        {
            return !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
        }

        private void ExecuteLogin(object parameter)
        {
            // Placeholder for login logic
            MessageBox.Show($"Login attempted for user: {Username}", "Login", MessageBoxButton.OK, MessageBoxImage.Information);
            
            // In a real app, you would verify credentials and then show the main window.
            // For now, we'll just show the main window if available or close this one.
            // To properly navigate, we might need a ViewService or closure.
            
            // Simple navigation to MainWindow for demonstration
            var mainWindow = new MainWindow();
            mainWindow.Show();
            
            // Close the login window
            if (parameter is Window loginWindow)
            {
                loginWindow.Close();
            }
        }
    }
}
