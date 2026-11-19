using System.Windows.Controls;
using System.Windows.Input;
using DumbTrader.Models;

namespace DumbTrader.Views
{
    public partial class SummaryView : UserControl
    {
        public SummaryView()
        {
            InitializeComponent();
            var sp = App.ServiceProvider;
            if (sp != null)
            {
                DataContext = sp.GetService(typeof(ViewModels.SummaryViewModel)) as ViewModels.SummaryViewModel;
            }
        }

        public SummaryView(ViewModels.SummaryViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void ConfigureStockReturn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.SummaryViewModel viewModel) return;
            var password = StockReturnPassword.Password;
            StockReturnPassword.Clear();
            viewModel.ConfigureStockReturn(StockReturnStart.SelectedDate, StockReturnEnd.SelectedDate, password);
            StockReturnToggle.IsChecked = false;
        }

        private void StockReturnPopup_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || StockReturnStart.IsDropDownOpen || StockReturnEnd.IsDropDownOpen) return;
            StockReturnToggle.IsChecked = false;
            StockReturnToggle.Focus();
            e.Handled = true;
        }

        private void FutureBalancePopup_Opened(object? sender, EventArgs e)
        {
            if (DataContext is not ViewModels.SummaryViewModel viewModel) return;
            var previousNumber = (FutureAccount.SelectedItem as AccountInfo)?.AccountNumber;
            viewModel.LoadFutureAccounts();
            FutureAccount.SelectedItem = viewModel.FutureAccounts.FirstOrDefault(account => account.AccountNumber == previousNumber)
                ?? (viewModel.FutureAccounts.Count == 1 ? viewModel.FutureAccounts[0] : null);
        }

        private void FutureBalancePopup_Closed(object? sender, EventArgs e) => FuturePassword.Clear();

        private void FutureAccount_SelectionChanged(object sender, SelectionChangedEventArgs e) => FuturePassword?.Clear();

        private async void QueryFutureBalance_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.SummaryViewModel viewModel) return;
            var password = FuturePassword.Password;
            var account = FutureAccount.SelectedItem as AccountInfo;
            FuturePassword.Clear();
            if (sender is not Button button) return;
            if (account is not null && !string.IsNullOrWhiteSpace(password))
                FutureBalanceToggle.IsChecked = false;
            button.IsEnabled = false;
            try
            {
                await viewModel.RefreshFutureBalanceAsync(account, password);
            }
            finally { button.IsEnabled = true; }
        }
    }
}
