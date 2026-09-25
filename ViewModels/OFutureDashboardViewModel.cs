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
    public sealed class OFutureDashboardViewModel : ViewModelBase
    {
        private readonly OFutureDataService _oFutureDataService;
        private readonly OFutureRealDataService _oFutureRealDataService;
        private readonly LoggingService _loggingService;
        private readonly HashSet<string> _dashboardSubscriptions = new(StringComparer.OrdinalIgnoreCase);
        private readonly DateTime _chartBaseDate = new(2000, 1, 1);
        private readonly List<DateTime> _chartDates = new();
        private IReadOnlyList<OFutureChartData> _selectedOFutureChartData = Array.Empty<OFutureChartData>();
        private OFutureRealData? _latestSelectedTrade;
        private double _dataMinOADate = double.NaN;
        private double _dataMaxOADate = double.NaN;
        private bool _isReceivingOFutureData;

        public ObservableCollection<OFutureContract> OFutureWatchlist { get; }
        public ObservableCollection<OFutureCardViewModel> OFutureCards { get; } = new();
        public WpfPlot PlotControl { get; } = new();

        private OFutureCardViewModel? _selectedOFutureCard;
        public OFutureCardViewModel? SelectedOFutureCard
        {
            get => _selectedOFutureCard;
            set
            {
                if (SetProperty(ref _selectedOFutureCard, value))
                {
                    _latestSelectedTrade = value is null ? null : _oFutureRealDataService.GetLatestTrade(value.Symbol);
                    LoadSelectedOFutureChartData();
                    UpdatePlot();
                }
            }
        }

        public ICommand SelectOFutureCardCommand { get; }
        public ICommand StartOFutureReceivingCommand { get; }
        public ICommand StopOFutureReceivingCommand { get; }

        public OFutureDashboardViewModel(
            OFutureDataService oFutureDataService,
            OFutureRealDataService oFutureRealDataService,
            LoggingService loggingService)
        {
            _oFutureDataService = oFutureDataService;
            _oFutureRealDataService = oFutureRealDataService;
            _loggingService = loggingService;

            OFutureWatchlist = new ObservableCollection<OFutureContract>(_oFutureDataService.Watchlist);
            _oFutureDataService.WatchlistUpdated += OnOFutureWatchlistUpdated;
            _oFutureDataService.ChartDataUpdated += OnOFutureChartDataUpdated;
            _oFutureRealDataService.RealDataUpdated += OnOFutureRealDataUpdated;
            _oFutureRealDataService.OrderBookUpdated += OnOFutureOrderBookUpdated;

            SelectOFutureCardCommand = new RelayCommand(SelectOFutureCard);
            StartOFutureReceivingCommand = new RelayCommand(StartOFutureReceiving);
            StopOFutureReceivingCommand = new RelayCommand(StopOFutureReceiving);

            RebuildOFutureCards();
            SelectedOFutureCard = OFutureCards.FirstOrDefault();
            PlotControl.MouseUp += (_, _) => ClampXAxis();
            PlotControl.MouseWheel += (_, _) => ClampXAxis();
        }

        private void OnOFutureWatchlistUpdated(object? sender, EventArgs e)
        {
            OnUiThread(() =>
            {
                var selectedSymbol = SelectedOFutureCard?.Symbol;
                OFutureWatchlist.Clear();
                foreach (var contract in _oFutureDataService.Watchlist)
                    OFutureWatchlist.Add(contract);

                RebuildOFutureCards(selectedSymbol);
                if (_isReceivingOFutureData)
                    SyncOFutureSubscriptions();
            });
        }

        private void RebuildOFutureCards(string? selectedSymbol = null)
        {
            OFutureCards.Clear();
            foreach (var contract in OFutureWatchlist)
            {
                var card = new OFutureCardViewModel(contract);
                card.Update(
                    _oFutureRealDataService.GetLatestTrade(contract.Symbol),
                    _oFutureRealDataService.GetLatestOrderBook(contract.Symbol));
                OFutureCards.Add(card);
            }

            SelectedOFutureCard = selectedSymbol is null
                ? OFutureCards.FirstOrDefault()
                : OFutureCards.FirstOrDefault(card => string.Equals(card.Symbol, selectedSymbol, StringComparison.OrdinalIgnoreCase))
                    ?? OFutureCards.FirstOrDefault();
        }

        private void StartOFutureReceiving(object? parameter)
        {
            _isReceivingOFutureData = true;
            SyncOFutureSubscriptions();
            _loggingService.Log("해외선물 실시간 데이터 수신 시작");
        }

        private void StopOFutureReceiving(object? parameter)
        {
            foreach (var symbol in _dashboardSubscriptions.ToArray())
            {
                _oFutureRealDataService.Unsubscribe(symbol);
                _dashboardSubscriptions.Remove(symbol);
            }
            _isReceivingOFutureData = false;
            _loggingService.Log("해외선물 실시간 데이터 수신 중지");
        }

        private void SyncOFutureSubscriptions()
        {
            var watchlistSymbols = OFutureWatchlist.Select(contract => contract.Symbol)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var contract in OFutureWatchlist)
            {
                if (_dashboardSubscriptions.Add(contract.Symbol))
                    _oFutureRealDataService.Subscribe(contract.Symbol);
            }

            foreach (var symbol in _dashboardSubscriptions.Where(symbol => !watchlistSymbols.Contains(symbol)).ToArray())
            {
                _oFutureRealDataService.Unsubscribe(symbol);
                _dashboardSubscriptions.Remove(symbol);
            }
        }

        private void SelectOFutureCard(object? parameter)
        {
            if (parameter is OFutureCardViewModel card)
                SelectedOFutureCard = card;
        }

        private void OnOFutureRealDataUpdated(object? sender, OFutureRealData e)
        {
            OnUiThread(() =>
            {
                var card = OFutureCards.FirstOrDefault(item => string.Equals(item.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase));
                if (card is null) return;
                card.Update(e, _oFutureRealDataService.GetLatestOrderBook(e.Symbol));

                if (string.Equals(SelectedOFutureCard?.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase))
                {
                    _latestSelectedTrade = e;
                    UpdatePlot();
                }
            });
        }

        private void OnOFutureOrderBookUpdated(object? sender, OFutureOrderBook e)
        {
            OnUiThread(() =>
            {
                var card = OFutureCards.FirstOrDefault(item => string.Equals(item.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase));
                if (card is not null)
                    card.Update(_oFutureRealDataService.GetLatestTrade(e.Symbol), e);
            });
        }

        private void LoadSelectedOFutureChartData()
        {
            var card = SelectedOFutureCard;
            if (card is null)
            {
                _selectedOFutureChartData = Array.Empty<OFutureChartData>();
                return;
            }

            var endDate = DateTime.Today;
            var startDate = endDate.AddYears(-1);
            _selectedOFutureChartData = FilterChartData(
                _oFutureDataService.GetChartData(card.Symbol), card.Symbol, startDate, endDate);
            _oFutureDataService.RequestChartData(card.Symbol, startDate, endDate, periodCode: "2", count: 2000);
        }

        private void OnOFutureChartDataUpdated(object? sender, OFutureChartDataEventArgs e)
        {
            if (!string.Equals(SelectedOFutureCard?.Symbol, e.Symbol, StringComparison.OrdinalIgnoreCase)) return;

            OnUiThread(() =>
            {
                _selectedOFutureChartData = FilterChartData(
                    e.Data, e.Symbol, DateTime.Today.AddYears(-1), DateTime.Today);
                UpdatePlot();
            });
        }

        private static IReadOnlyList<OFutureChartData> FilterChartData(
            IEnumerable<OFutureChartData> data, string symbol, DateTime startDate, DateTime endDate)
            => data.Where(item => string.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                    && DateTime.TryParseExact(item.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    && date.Date >= startDate.Date && date.Date <= endDate.Date)
                .OrderBy(item => item.Date, StringComparer.Ordinal)
                .ToArray();

        private void UpdatePlot()
        {
            var plot = PlotControl.Plot;
            plot.Clear();
            _chartDates.Clear();

            if (SelectedOFutureCard is null)
            {
                _dataMinOADate = double.NaN;
                _dataMaxOADate = double.NaN;
                PlotControl.Refresh();
                return;
            }

            var chartData = _selectedOFutureChartData.ToList();
            var latestTrade = _latestSelectedTrade?.Symbol == SelectedOFutureCard.Symbol
                ? _latestSelectedTrade
                : _oFutureRealDataService.GetLatestTrade(SelectedOFutureCard.Symbol);
            if (latestTrade is not null)
                ApplyLatestOFutureTrade(chartData, latestTrade);

            var candles = new List<OHLC>(chartData.Count);
            foreach (var item in chartData.OrderBy(item => item.Date, StringComparer.Ordinal))
            {
                if (!DateTime.TryParseExact(item.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;
                _chartDates.Add(date);
                candles.Add(new OHLC(item.Open, item.High, item.Low, item.Close,
                    _chartBaseDate.AddDays(candles.Count), TimeSpan.FromDays(1)));
            }

            if (candles.Count == 0)
            {
                _dataMinOADate = double.NaN;
                _dataMaxOADate = double.NaN;
                PlotControl.Refresh();
                return;
            }

            var candlePlot = plot.Add.Candlestick(candles);
            candlePlot.FallingColor = new ScottPlot.Color(0, 0, 255);
            candlePlot.RisingColor = new ScottPlot.Color(255, 0, 0);
            plot.Axes.DateTimeTicksBottom();
            if (plot.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.DateTimeAutomatic ticks)
            {
                ticks.LabelFormatter = tickDate =>
                {
                    var index = (int)Math.Round(tickDate.Subtract(_chartBaseDate).TotalDays);
                    return index >= 0 && index < _chartDates.Count
                        ? _chartDates[index].ToString("yy/MM/dd", CultureInfo.InvariantCulture)
                        : string.Empty;
                };
            }

            _dataMinOADate = candles[0].DateTime.ToOADate();
            _dataMaxOADate = candles[^1].DateTime.ToOADate();
            plot.Axes.SetLimitsX(Math.Max(_dataMinOADate - 2, _dataMaxOADate - 60), _dataMaxOADate + 2);
            plot.Axes.ContinuouslyAutoscale = true;
            plot.Axes.ContinuousAutoscaleAction = renderPack =>
            {
                var start = DateTime.FromOADate(renderPack.Plot.Axes.Bottom.Min);
                var end = DateTime.FromOADate(renderPack.Plot.Axes.Bottom.Max);
                var visible = candles.Where(candle => candle.DateTime >= start && candle.DateTime <= end).ToList();
                if (visible.Count == 0) return;
                var min = visible.Min(candle => candle.Low);
                var max = visible.Max(candle => candle.High);
                var padding = Math.Max((max - min) * 0.05, Math.Max(Math.Abs(min), Math.Abs(max)) * 0.001);
                if (padding == 0) padding = 0.01;
                renderPack.Plot.Axes.SetLimitsY(min - padding, max + padding);
            };

            PlotControl.Refresh();
            ClampXAxis();
        }

        private static void ApplyLatestOFutureTrade(List<OFutureChartData> chartData, OFutureRealData trade)
        {
            var today = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var candle = new OFutureChartData
            {
                Symbol = trade.Symbol,
                Date = today,
                Open = trade.Open != 0 ? trade.Open : trade.Price,
                High = trade.High != 0 ? trade.High : trade.Price,
                Low = trade.Low != 0 ? trade.Low : trade.Price,
                Close = trade.Price,
                Volume = trade.TotalQuantity
            };

            var todayIndex = chartData.FindIndex(item => item.Date == today);
            if (todayIndex >= 0) chartData[todayIndex] = candle;
            else chartData.Add(candle);
        }

        private void ClampXAxis()
        {
            if (double.IsNaN(_dataMinOADate) || double.IsNaN(_dataMaxOADate)) return;
            const double margin = 2;
            var allowedMin = _dataMinOADate - margin;
            var allowedMax = _dataMaxOADate + margin;
            var currentMin = PlotControl.Plot.Axes.Bottom.Min;
            var currentMax = PlotControl.Plot.Axes.Bottom.Max;
            var width = currentMax - currentMin;
            var allowedWidth = allowedMax - allowedMin;

            if (width >= allowedWidth)
                PlotControl.Plot.Axes.SetLimitsX(allowedMin, allowedMax);
            else if (currentMin < allowedMin)
                PlotControl.Plot.Axes.SetLimitsX(allowedMin, allowedMin + width);
            else if (currentMax > allowedMax)
                PlotControl.Plot.Axes.SetLimitsX(allowedMax - width, allowedMax);
            else return;

            PlotControl.Refresh();
        }

        private static void OnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) action();
            else _ = dispatcher.BeginInvoke(action);
        }
    }
}
