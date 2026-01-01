using DumbTrader.Services;
using DumbTrader.Core;
using DumbTrader.Models;
using System;
using System.Windows;
using System.Windows.Input;

namespace DumbTrader.ViewModels
{
    public class LoginViewModel : ViewModelBase
    {
        private readonly IXASessionService _sessionService;
        private readonly LoginService _loginService;

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

        public LoginViewModel(IXASessionService sessionService, LoginService loginService)
        {
            _sessionService = sessionService;
            _loginService = loginService ?? throw new ArgumentNullException(nameof(loginService));

            // try to load saved login
            var saved = _loginService.LoadLogin();
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
            // 실제 서버 연결 및 로그인 코드는 개발 버전에서 비활성화

            // Demo server connection
            if (!_sessionService.Connect("demo.ebestsec.co.kr", 20001))
            {
                int err = _session_service_GetLastErrorFallback();
                MessageBox.Show($"서버 연결 실패: {_sessionService.GetErrorMessage(err)}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_sessionService.Login(Username, Password, "", 0, false))
            {
                // save login (encrypt password) if user requested
                if (SavePassword)
                {
                    var login = new LoginModel
                    {
                        Id = Username,
                        Password = Password
                    };
                    _loginService.SaveLogin(login);
                }
                else
                {
                    // remove saved login if exists
                    _loginService.DeleteLogin();
                }

                // signal success to the window and close
                if (parameter is Window loginWindow)
                {
                    loginWindow.DialogResult = true;
                    loginWindow.Close();
                }
            }
            else
            {
                int err = _sessionService.GetLastError();
                MessageBox.Show($"로그인 실패: {_sessionService.GetErrorMessage(err)}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }


            // 개발 버전: 로그인 버튼 클릭 시 바로 성공 처리
            /*if (parameter is Window loginWindow)
            {
                loginWindow.DialogResult = true;
                loginWindow.Close();
            }*/
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
