using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;
using ScottPlot;
using ScottPlot.WPF;

namespace DumbTrader.ViewModels
{
    public enum OFutureChartQueryPeriod
    {
        OneDay,
        AWeek,
        AMonth,
        ThreeMonths,
        SixMonths,
        AYear,
        ThreeYears,
        FiveYears,
        All
    }

    public sealed class OFutureDetailViewModel : ViewModelBase, IDisposable
    {
        private readonly OFutureDataService _oFutureDataService;
        private readonly LoggingService _loggingService;
        private readonly DateTime _chartBaseDate = new(2000, 1, 1);
        private readonly List<DateTime> _chartDates = new();
        private double _dataMinOADate = double.NaN;
        private double _dataMaxOADate = double.NaN;
        private DateTime _requestedStartDate;
        private DateTime _requestedEndDate;
        private ScottPlot.Plottables.Annotation _annotation = null!;
        private bool _disposed;

        public ObservableCollection<OFutureContract> OFutureWatchlist { get; }

        private OFutureContract? _selectedOFutureContract;
        public OFutureContract? SelectedOFutureContract
        {
            get => _selectedOFutureContract;
            set
            {
                if (SetProperty(ref _selectedOFutureContract, value))
                {
                    if (value is null)
                    {
                        ChartData = new ObservableCollection<OFutureChartData>();
                    }
                    else
                    {
                        QueryOFutureChartData(OFutureChartQueryPeriod.AYear);
                    }
                }
            }
        }

        private ObservableCollection<OFutureChartData> _chartData = new();
        public ObservableCollection<OFutureChartData> ChartData
        {
            get => _chartData;
            private set
            {
                if (SetProperty(ref _chartData, value))
                    UpdatePlot();
            }
        }

        public ICommand QueryOFutureChartDataCommand { get; }
        public WpfPlot PlotControl { get; } = new();

        public OFutureDetailViewModel(OFutureDataService oFutureDataService, LoggingService loggingService)
        {
            _oFutureDataService = oFutureDataService;
            _loggingService = loggingService;
            OFutureWatchlist = new ObservableCollection<OFutureContract>(_oFutureDataService.Watchlist);
            _oFutureDataService.WatchlistUpdated += OnOFutureWatchlistUpdated;
            _oFutureDataService.ChartDataUpdated += OnOFutureChartDataUpdated;
            QueryOFutureChartDataCommand = new RelayCommand(_ =>
                QueryOFutureChartData(OFutureChartQueryPeriod.AYear));

            _annotation = PlotControl.Plot.Add.Annotation(string.Empty);
            SetAnnotation(_annotation);
            PlotControl.MouseMove += OnPlotMouseMove;
            PlotControl.MouseUp += (_, _) => ClampXAxis();
            PlotControl.MouseWheel += (_, _) => ClampXAxis();
        }

        private void OnOFutureWatchlistUpdated(object? sender, EventArgs e)
        {
            OnUiThread(() =>
            {
                var selectedSymbol = SelectedOFutureContract?.Symbol;
                OFutureWatchlist.Clear();
                foreach (var contract in _oFutureDataService.Watchlist)
                    OFutureWatchlist.Add(contract);

                if (selectedSymbol is not null)
                    SelectedOFutureContract = OFutureWatchlist.FirstOrDefault(item => item.Symbol == selectedSymbol);
            });
        }

        private void OnOFutureChartDataUpdated(object? sender, OFutureChartDataEventArgs e)
        {
            if (SelectedOFutureContract is null || !string.Equals(e.Symbol, SelectedOFutureContract.Symbol, StringComparison.OrdinalIgnoreCase))
                return;

            OnUiThread(() =>
            {
                var filteredData = e.Data
                    .Where(data => TryParseOFutureDate(data.Date, out var date)
                        && date.Date >= _requestedStartDate.Date
                        && date.Date <= _requestedEndDate.Date)
                    .OrderBy(data => data.Date, StringComparer.Ordinal)
                    .ToList();
                _loggingService.Log($"해외선물 차트 데이터 업데이트: 계약코드={e.Symbol}, 데이터={filteredData.Count}건");
                ChartData = new ObservableCollection<OFutureChartData>(filteredData);
            });
        }

        private void QueryOFutureChartData(OFutureChartQueryPeriod period)
        {
            var selectedContract = SelectedOFutureContract;
            if (selectedContract is null)
            {
                ChartData = new ObservableCollection<OFutureChartData>();
                return;
            }

            _requestedEndDate = DateTime.Today;
            _requestedStartDate = period switch
            {
                OFutureChartQueryPeriod.OneDay => _requestedEndDate.AddDays(-1),
                OFutureChartQueryPeriod.AWeek => _requestedEndDate.AddDays(-7),
                OFutureChartQueryPeriod.AMonth => _requestedEndDate.AddMonths(-1),
                OFutureChartQueryPeriod.ThreeMonths => _requestedEndDate.AddMonths(-3),
                OFutureChartQueryPeriod.SixMonths => _requestedEndDate.AddMonths(-6),
                OFutureChartQueryPeriod.AYear => _requestedEndDate.AddYears(-1),
                OFutureChartQueryPeriod.ThreeYears => _requestedEndDate.AddYears(-3),
                OFutureChartQueryPeriod.FiveYears => _requestedEndDate.AddYears(-5),
                OFutureChartQueryPeriod.All => _requestedEndDate.AddYears(-10),
                _ => _requestedEndDate.AddYears(-1)
            };

            var cachedData = _oFutureDataService.GetChartData(selectedContract.Symbol)
                .Where(data => TryParseOFutureDate(data.Date, out var date)
                    && date.Date >= _requestedStartDate.Date
                    && date.Date <= _requestedEndDate.Date)
                .OrderBy(data => data.Date, StringComparer.Ordinal)
                .ToList();
            ChartData = new ObservableCollection<OFutureChartData>(cachedData);

            if (!_oFutureDataService.RequestChartData(
                    selectedContract.Symbol,
                    _requestedStartDate,
                    _requestedEndDate,
                    periodCode: "2",
                    count: 2000))
            {
                _loggingService.Log($"해외선물 차트 조회 요청 실패: {selectedContract.Symbol}");
            }
        }

        private void UpdatePlot()
        {
            var data = ChartData
                .Where(item => TryParseOFutureDate(item.Date, out _))
                .OrderBy(item => item.Date, StringComparer.Ordinal)
                .ToList();

            var plot = PlotControl.Plot;
            plot.Clear();
            _annotation = plot.Add.Annotation(string.Empty);
            SetAnnotation(_annotation);

            if (data.Count == 0)
            {
                _chartDates.Clear();
                _dataMinOADate = double.NaN;
                _dataMaxOADate = double.NaN;
                PlotControl.Refresh();
                return;
            }

            var candles = new List<OHLC>(data.Count);
            _chartDates.Clear();
            for (var index = 0; index < data.Count; index++)
            {
                TryParseOFutureDate(data[index].Date, out var tradeDate);
                _chartDates.Add(tradeDate);
                candles.Add(new OHLC(
                    data[index].Open,
                    data[index].High,
                    data[index].Low,
                    data[index].Close,
                    _chartBaseDate.AddDays(index),
                    TimeSpan.FromDays(1)));
            }

            _dataMinOADate = candles[0].DateTime.ToOADate();
            _dataMaxOADate = candles[^1].DateTime.ToOADate();

            var candlePlot = plot.Add.Candlestick(candles);
            candlePlot.FallingColor = new ScottPlot.Color(0, 0, 255);
            candlePlot.RisingColor = new ScottPlot.Color(255, 0, 0);
            plot.Axes.DateTimeTicksBottom();
            if (plot.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.DateTimeAutomatic dateTimeTicks)
            {
                dateTimeTicks.LabelFormatter = tickDate =>
                {
                    if (_chartDates.Count == 0) return string.Empty;
                    var index = (int)Math.Round(tickDate.Subtract(_chartBaseDate).TotalDays);
                    return index >= 0 && index < _chartDates.Count
                        ? _chartDates[index].ToString("yy/MM/dd", CultureInfo.InvariantCulture)
                        : string.Empty;
                };
            }

            var fullRange = _dataMaxOADate - _dataMinOADate;
            var visibleWidth = Math.Min(fullRange, 60);
            plot.Axes.SetLimitsX(
                fullRange <= 0 ? _dataMinOADate - 1 : Math.Max(_dataMinOADate, _dataMaxOADate - visibleWidth),
                fullRange <= 0 ? _dataMaxOADate + 1 : _dataMaxOADate);

            plot.Axes.ContinuouslyAutoscale = true;
            plot.Axes.ContinuousAutoscaleAction = renderPack =>
            {
                var visibleStart = DateTime.FromOADate(renderPack.Plot.Axes.Bottom.Min);
                var visibleEnd = DateTime.FromOADate(renderPack.Plot.Axes.Bottom.Max);
                var visibleCandles = candles.Where(candle => candle.DateTime >= visibleStart && candle.DateTime <= visibleEnd).ToList();
                if (visibleCandles.Count == 0) return;
                var min = visibleCandles.Min(candle => candle.Low);
                var max = visibleCandles.Max(candle => candle.High);
                var padding = Math.Max((max - min) * 0.05, Math.Max(Math.Abs(min), Math.Abs(max)) * 0.001);
                if (padding == 0) padding = 0.01;
                renderPack.Plot.Axes.SetLimitsY(min - padding, max + padding);
            };

            PlotControl.Refresh();
            ClampXAxis();
        }

        private void OnPlotMouseMove(object? sender, System.Windows.Input.MouseEventArgs e)
        {
            var mouse = e.GetPosition(PlotControl);
            var plot = PlotControl.Plot;
            if (PlotControl.ActualWidth <= 0) return;
            var x = plot.Axes.Bottom.Min + mouse.X / PlotControl.ActualWidth * (plot.Axes.Bottom.Max - plot.Axes.Bottom.Min);
            var candles = plot.GetPlottables().OfType<ScottPlot.Plottables.CandlestickPlot>()
                .SelectMany(candlePlot => candlePlot.Data.GetOHLCs())
                .ToList();

            if (candles.Count == 0 || candles.Count != _chartDates.Count) return;
            var index = (int)Math.Round(DateTime.FromOADate(x).Subtract(_chartBaseDate).TotalDays);
            index = Math.Clamp(index, 0, candles.Count - 1);
            var candle = candles[index];
            _annotation.Text = $"Date: {_chartDates[index]:yy/MM/dd}\nOpen: {candle.Open}\nHigh: {candle.High}\nLow: {candle.Low}\nClose: {candle.Close}";
            _annotation.IsVisible = true;
            _annotation.Alignment = Alignment.UpperLeft;
            ComputeAnnotationOffsets(mouse.X, mouse.Y, out var offsetX, out var offsetY);
            _annotation.OffsetX = offsetX - 50;
            _annotation.OffsetY = offsetY - 5;
            PlotControl.Refresh();
        }

        private void ClampXAxis()
        {
            var plot = PlotControl.Plot;
            var min = plot.Axes.Bottom.Min;
            var max = plot.Axes.Bottom.Max;
            if (double.IsNaN(_dataMinOADate) || double.IsNaN(_dataMaxOADate)) return;

            const double margin = 2.0;
            var leftLimit = _dataMinOADate - margin;
            var rightLimit = _dataMaxOADate + margin;
            var width = max - min;
            var availableRange = rightLimit - leftLimit;
            if (width >= availableRange)
                plot.Axes.SetLimitsX(leftLimit, rightLimit);
            else if (min < leftLimit)
                plot.Axes.SetLimitsX(leftLimit, leftLimit + width);
            else if (max > rightLimit)
                plot.Axes.SetLimitsX(rightLimit - width, rightLimit);
            PlotControl.Refresh();
        }

        private void ComputeAnnotationOffsets(double mouseX, double mouseY, out float offsetX, out float offsetY)
        {
            const double margin = 8;
            const double estimatedWidth = 90;
            const double estimatedHeight = 95;
            var x = mouseX + margin;
            var y = mouseY + margin;
            if (PlotControl.ActualWidth > 0 && x + estimatedWidth > PlotControl.ActualWidth)
                x = mouseX - estimatedWidth - margin;
            if (PlotControl.ActualHeight > 0 && y + estimatedHeight > PlotControl.ActualHeight)
                y = mouseY - estimatedHeight - margin;
            offsetX = (float)Math.Max(margin, x);
            offsetY = (float)Math.Max(margin, y);
        }

        private void SetAnnotation(ScottPlot.Plottables.Annotation annotation)
        {
            annotation.IsVisible = false;
            annotation.LabelBackgroundColor = ScottPlot.Colors.Yellow.WithAlpha(0.7);
            annotation.LabelFontColor = ScottPlot.Colors.Black;
            annotation.LabelBorderColor = ScottPlot.Colors.Black;
            annotation.LabelBorderWidth = 1;
            annotation.LabelShadowColor = ScottPlot.Colors.Transparent;
            annotation.LabelFontName = Fonts.Detect("한국어");
        }

        private static bool TryParseOFutureDate(string value, out DateTime date)
            => DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

        private static void OnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) action();
            else _ = dispatcher.BeginInvoke(action);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _oFutureDataService.WatchlistUpdated -= OnOFutureWatchlistUpdated;
            _oFutureDataService.ChartDataUpdated -= OnOFutureChartDataUpdated;
            GC.SuppressFinalize(this);
        }
    }
}
