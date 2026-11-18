using System.Windows.Controls;
using System.Windows.Input;

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

        private async void QueryFutureBalance_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.SummaryViewModel viewModel) return;
            var password = FuturePassword.Password;
            FuturePassword.Clear();
            if (sender is not Button button) return;
            FutureBalanceToggle.IsChecked = false;
            button.IsEnabled = false;
            try
            {
                await viewModel.RefreshFutureBalanceAsync(FutureAccountType.Text, FutureAccount.Text, password);
            }
            finally { button.IsEnabled = true; }
        }
    }
}
