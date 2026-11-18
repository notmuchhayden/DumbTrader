using System.Windows;
using System.Windows.Controls;
using DumbTrader.Services;
using DumbTrader.ViewModels;

namespace DumbTrader.Views
{
    public partial class OFutureDetailView : UserControl
    {
        public OFutureDetailView()
        {
            InitializeComponent();
            Unloaded += (_, _) => (DataContext as System.IDisposable)?.Dispose();
        }

        private void OpenEvolution_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is OFutureDetailViewModel { SelectedOFutureContract: { } contract })
                StrategyEvolutionWindow.Open(Window.GetWindow(this), MarketSnapshotBuilder.Instrument(contract));
        }
    }
}
