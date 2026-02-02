using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System;
using DumbTrader.ViewModels;
using ScottPlot;
using System.Globalization;

namespace DumbTrader.Views
{
    public partial class StockDetailView : UserControl
    {
        public StockDetailView()
        {
            InitializeComponent();
            DataContextChanged += StockDetailView_DataContextChanged;
        }

        private void StockDetailView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is StockDetailViewModel oldVm)
            {
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;
            }

            if (e.NewValue is StockDetailViewModel newVm)
            {
                newVm.PropertyChanged += ViewModel_PropertyChanged;
                UpdatePlotFromViewModel(newVm);
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(StockDetailViewModel.ChartData))
            {
                if (DataContext is StockDetailViewModel vm)
                {
                    Dispatcher.Invoke(() => UpdatePlotFromViewModel(vm));
                }
            }
        }

        private void UpdatePlotFromViewModel(StockDetailViewModel vm)
        {
            var data = vm.ChartData;

            // use strongly-typed ScottPlot API (no reflection)
            var plt = WpfPlot.Plot;
            plt.Clear();

            if (data == null || data.Count == 0)
            {
                WpfPlot.Refresh();
                return;
            }

            // prepare arrays
            var dates = data.Select(x => DateTime.ParseExact(x.date, "yyyyMMdd", CultureInfo.InvariantCulture)).ToArray();
            var xs = dates.Select(d => d.ToOADate()).ToArray();
            var opens = data.Select(x => (double)x.open).ToArray();
            var highs = data.Select(x => (double)x.high).ToArray();
            var lows = data.Select(x => (double)x.low).ToArray();
            var closes = data.Select(x => (double)x.close).ToArray();

            bool drew = false;

            // Try to draw candlesticks using ScottPlot5.x API
            try
            {
                // Create OHLC array expected by ScottPlot5.x
                var ohlcs = new ScottPlot.OHLC[data.Count];
                for (int i = 0; i < data.Count; i++)
                {
                    // constructor: OHLC(open, high, low, close, DateTime time, TimeSpan span)
                    // provide a default span of 1 day
                    ohlcs[i] = new ScottPlot.OHLC(opens[i], highs[i], lows[i], closes[i], dates[i], TimeSpan.FromDays(1));
                }

                // add candlesticks
                plt.Add.Candlestick(ohlcs);

                drew = true;
            }
            catch
            {
                // fallback to scatter of closes if candlestick fails
                try
                {
                    plt.Add.Scatter(xs, closes);
                    drew = true;
                }
                catch
                {
                    // last resort: do nothing
                }
            }

            // ensure plot fits data on first render by auto-scaling axes
            try
            {
                plt.Axes.AutoScale();
            }
            catch
            {
                // ignore if method not present
            }

            // refresh view
            WpfPlot.Refresh();
        }
    }
}
