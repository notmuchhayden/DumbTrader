using System;
using System.Windows;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Views;
using DumbTrader.Services;

namespace DumbTrader.ViewModels
{
public class MainViewModel : ViewModelBase
{
    private readonly IXingSessionService _sessionService;

    private string _title = "Dumb Trader - Main";
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    private string _welcomeMessage = "Welcome";
    public string WelcomeMessage
    {
        get => _welcomeMessage;
        set => SetProperty(ref _welcomeMessage, value);
    }

    private string _statusMessage = "Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand ExitCommand { get; }

    // New test commands that do nothing
    public ICommand Test1Command { get; }
    public ICommand Test2Command { get; }
    public ICommand Test3Command { get; }

    public MainViewModel(IXingSessionService sessionService)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));

        ExitCommand = new RelayCommand(ExecuteExit);

        Test1Command = new RelayCommand(ExecuteTest1);
        Test2Command = new RelayCommand(ExecuteTest2);
        Test3Command = new RelayCommand(ExecuteTest3);
    }

    private void ExecuteExit(object? parameter)
    {
        // Ensure we log out from the session before exiting
        try
        {
            _sessionService.Logout();
        }
        catch
        {
            // ignore logout failures
        }

        if (parameter is Window win)
        {
            win.Close();
        }
        else
        {
            Application.Current?.Shutdown();
        }
    }

    // Empty test command handlers
    private void ExecuteTest1(object? parameter)
    {
        // Intentionally left blank
    }

    private void ExecuteTest2(object? parameter)
    {
        // Intentionally left blank
    }

    private void ExecuteTest3(object? parameter)
    {
        // Intentionally left blank
    }
}
}
