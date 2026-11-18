using System.Windows.Controls;

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
        }

        private async void QueryFutureBalance_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.SummaryViewModel viewModel) return;
            var password = FuturePassword.Password;
            FuturePassword.Clear();
            if (sender is not Button button) return;
            button.IsEnabled = false;
            try
            {
                await viewModel.RefreshFutureBalanceAsync(FutureAccountType.Text, FutureAccount.Text, password);
            }
            finally { button.IsEnabled = true; }
        }
    }
}
