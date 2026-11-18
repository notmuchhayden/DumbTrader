using System.Windows;
using System.Windows.Controls;
using DumbTrader.Services;
using DumbTrader.ViewModels;

namespace DumbTrader.Views
{
    public partial class StockDetailView : UserControl
    {
        public StockDetailView()
        {
            InitializeComponent();
        }

        private void OpenEvolution_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is StockDetailViewModel { SelectedWatchlist.Stock: { } stock })
                StrategyEvolutionWindow.Open(Window.GetWindow(this), MarketSnapshotBuilder.Stock(stock));
        }
    }
}
