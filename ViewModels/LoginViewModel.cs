using DumbTrader.Services;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System;
using DumbTrader.Core;
using DumbTrader.Models;

namespace DumbTrader.ViewModels
{
    public class LoginViewModel : ViewModelBase
    {
        private readonly IXingSessionService _sessionService;
        private readonly AccountStoreService _accountStore;

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

        private bool _savePassword; 
        public bool SavePassword
        {
            get => _savePassword;
            set => SetProperty(ref _savePassword, value);
        }

        public ICommand LoginCommand { get; }

        public LoginViewModel(IXingSessionService sessionService)
        {
            _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
            _accountStore = new AccountStoreService();

            // try to load saved account
            var saved = _accountStore.LoadAccount();
            if (saved != null)
            {
                Username = saved.Id;
                Password = saved.Password ?? string.Empty;
                SavePassword = !string.IsNullOrEmpty(saved.EncryptedPasswordBase64);
            }

            LoginCommand = new RelayCommand(ExecuteLogin, CanExecuteLogin);
        }

        private bool CanExecuteLogin(object? parameter)
        {
            return !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
        }

        private void ExecuteLogin(object? parameter)
        {
            // Demo server connection
            if (!_sessionService.Connect("demo.ebestsec.co.kr",20001))
            {
                 int err = _session_service_GetLastErrorFallback();
                 MessageBox.Show($"서버 연결 실패: {_sessionService.GetErrorMessage(err)}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                 return;
            }

            if (_sessionService.Login(Username, Password, "",0, false))
            {
                MessageBox.Show("로그인 성공!", "성공", MessageBoxButton.OK, MessageBoxImage.Information);

                // save account (encrypt password) if user requested
                if (SavePassword)
                {
                    var account = new AccountModel
                    {
                        Id = Username,
                        AccountNumber = string.Empty,
                        Password = Password
                    };
                    _accountStore.SaveAccount(account);
                }
                else
                {
                    // remove saved account if exists
                    _accountStore.DeleteAccount();
                }

                var mainWindow = new MainWindow();
                mainWindow.DataContext = new MainViewModel(_sessionService);
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

        // helper to avoid compiler warning if GetLastError isn't available when session creation failed
        private int _session_service_GetLastErrorFallback()
        {
            try
            {
                return _sessionService.GetLastError();
            }
            catch
            {
                return -1;
            }
        }
    }
}
