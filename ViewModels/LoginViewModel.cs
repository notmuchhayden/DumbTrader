using DumbTrader.Services;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System;

namespace DumbTrader.ViewModels
{
    public class LoginViewModel : ViewModelBase
    {
        private readonly IXingSessionService _sessionService;
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

        public LoginViewModel(IXingSessionService sessionService)
        {
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            LoginCommand = new RelayCommand(ExecuteLogin, CanExecuteLogin);
        }

        private bool CanExecuteLogin(object? parameter)
        {
            return !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
        }

        private void ExecuteLogin(object? parameter)
        {
            // Demo server connection
            if (!_sessionService.Connect("demo.ebestsec.co.kr", 20001))
            {
                 int err = _sessionService.GetLastError();
                 MessageBox.Show($"서버 연결 실패: {_sessionService.GetErrorMessage(err)}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                 return;
            }

            if (_sessionService.Login(Username, Password, "", 0, false))
            {
                MessageBox.Show("로그인 성공!", "성공", MessageBoxButton.OK, MessageBoxImage.Information);
                
                var mainWindow = new MainWindow();
                mainWindow.Show();

                if (parameter is Window loginWindow)
                {
                    loginWindow.Close();
                }
            }
            else
            {
                int err = _sessionService.GetLastError();
                MessageBox.Show($"로그인 실패: {_sessionService.GetErrorMessage(err)}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
