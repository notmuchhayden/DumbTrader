using System.Windows;
using DumbTrader.ViewModels;

namespace DumbTrader.Views
{
    public partial class SplashView : Window
    {
        public SplashView()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is SplashViewModel viewModel)
            {
                viewModel.InitializationComplete += OnInitializationComplete;
                await viewModel.InitializeAsync();
            }
        }

        private void OnInitializationComplete()
        {
            Dispatcher.Invoke(() =>
            {
                DialogResult = true;
                Close();
            });
        }
    }
}
