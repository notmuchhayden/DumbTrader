using System.Windows.Controls;

namespace DumbTrader.Views
{
    public partial class OFutureDashboardView : UserControl
    {
        public OFutureDashboardView()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (DataContext is not DumbTrader.ViewModels.OFutureDashboardViewModel viewModel) return;
                DailyPlotHost.Content = viewModel.PlotControl;
                TenMinutePlotHost.Content = viewModel.TenMinutePlot;
                OneMinutePlotHost.Content = viewModel.OneMinutePlot;
                viewModel.Activate();
            };
            Unloaded += (_, _) =>
            {
                (DataContext as DumbTrader.ViewModels.OFutureDashboardViewModel)?.Deactivate();
                DailyPlotHost.Content = null;
                TenMinutePlotHost.Content = null;
                OneMinutePlotHost.Content = null;
            };
        }
    }
}
