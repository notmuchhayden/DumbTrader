using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;
using ScottPlot;
using ScottPlot.WPF;

namespace DumbTrader.ViewModels
{
    public sealed class OFutureStrategyViewModel : ViewModelBase
    {
        private readonly OFutureStrategyService _strategyService;
        private readonly OFutureSimulationService _simulationService;
        private readonly LoggingService _loggingService;
        private OFutureStrategyAssignment? _selectedAssignment;
        private string? _selectedMainStrategyFile;
        private double _simulationSeedMoney = 10000;
        private DateTime _simulationStartDate = DateTime.Today.AddYears(-1);
        private int _maxContracts = 1;
        private OFutureSimulationPeriodOption? _selectedSimulationPeriod;
        private string _simulationStatus = "시뮬레이션 대기";
        private string _resultSummary = "—";
        private bool _isSimulationRunning;
        private long _resultVersion;
        private CancellationTokenSource? _simulationCancellation;
        private readonly OFutureChartMarkers _markers;
        public WpfPlot SimulationPlot { get; } = new();

        public ObservableCollection<string> MainStrategyFiles { get; } = new();
        public ObservableCollection<OFutureSimulationPeriodOption> SimulationPeriods { get; } = new()
        {
            new("일봉", 0), new("1분봉", 1), new("10분봉", 10)
        };

        public string? SelectedMainStrategyFile
        {
            get => _selectedMainStrategyFile;
            set
            {
                if (!SetProperty(ref _selectedMainStrategyFile, value)) return;
                if (_selectedAssignment is null) return;
                _selectedAssignment.MainStrategyPath = value ?? string.Empty;
                ResetResults();
                _strategyService.SaveConfig();
                _strategyService.ResetLiveContext(_selectedAssignment.Contract.Symbol);
                SimulationStatus = string.IsNullOrWhiteSpace(value) ? "주전략을 선택하세요" : "전략 설정 저장됨 · 실시간 신호 전용";
            }
        }

        public double SimulationSeedMoney
        {
            get => _simulationSeedMoney;
            set => SetProperty(ref _simulationSeedMoney, value);
        }

        public DateTime SimulationStartDate
        {
            get => _simulationStartDate;
            set => SetProperty(ref _simulationStartDate, value);
        }

        public int MaxContracts
        {
            get => _maxContracts;
            set => SetProperty(ref _maxContracts, value);
        }

        public OFutureSimulationPeriodOption? SelectedSimulationPeriod
        {
            get => _selectedSimulationPeriod;
            set
            {
                if (SetProperty(ref _selectedSimulationPeriod, value))
                    ResetResults();
            }
        }

        public string SimulationStatus
        {
            get => _simulationStatus;
            private set => SetProperty(ref _simulationStatus, value);
        }

        public string ResultSummary
        {
            get => _resultSummary;
            private set => SetProperty(ref _resultSummary, value);
        }

        public bool IsSimulationRunning
        {
            get => _isSimulationRunning;
            private set => SetProperty(ref _isSimulationRunning, value);
        }

        public ICommand StartSimulationCommand { get; }
        public ICommand ResetSimulationCommand { get; }
        public double CommissionPerSide { get; set; }
        public double SlippageTicksPerSide { get; set; }

        public OFutureStrategyViewModel(
            OFutureStrategyService strategyService,
            OFutureSimulationService simulationService,
            LoggingService loggingService)
        {
            _strategyService = strategyService;
            _simulationService = simulationService;
            _loggingService = loggingService;
            _markers = new OFutureChartMarkers(SimulationPlot);
            _selectedSimulationPeriod = SimulationPeriods[0];
            LoadMainStrategyFiles();
            StartSimulationCommand = new AsyncRelayCommand(ExecuteStartSimulationAsync);
            ResetSimulationCommand = new RelayCommand(_ => ResetResults());
        }

        public void SetSelectedContract(OFutureContract? contract)
        {
            _selectedAssignment = contract is null ? null : _strategyService.FindAssignment(contract.Symbol);
            var selectedFile = _selectedAssignment?.MainStrategyPath ?? string.Empty;
            if (SetProperty(ref _selectedMainStrategyFile, selectedFile, nameof(SelectedMainStrategyFile)))
                OnPropertyChanged(nameof(SelectedMainStrategyFile));
            SimulationStartDate = DateTime.Today.AddYears(-1);
            ResetResults();
            if (contract is null)
                SimulationStatus = "관심계약을 선택하세요";
            else if (string.IsNullOrWhiteSpace(selectedFile))
                SimulationStatus = "해외선물 주전략을 선택하세요";
            else
                SimulationStatus = "전략 설정 준비됨 · 실제 주문은 전송하지 않음";
        }

        private async Task ExecuteStartSimulationAsync(object? parameter)
        {
            if (_selectedAssignment is null)
            {
                SimulationStatus = "관심계약을 선택하세요";
                return;
            }
            if (SelectedSimulationPeriod is null)
            {
                SimulationStatus = "시뮬레이션 주기를 선택하세요";
                return;
            }

            IsSimulationRunning = true;
            ResetResults();
            var version = _resultVersion;
            using var cancellation = new CancellationTokenSource();
            _simulationCancellation = cancellation;
            var costs = new StrategyCosts(CommissionPerSide, SlippageTicksPerSide);
            SimulationStatus = "저장된 봉과 전략 확인 중";
            ResultSummary = "—";
            try
            {
                var result = await _simulationService.RunAsync(
                    _selectedAssignment, SimulationSeedMoney, SimulationStartDate,
                    SelectedSimulationPeriod.IntervalMinutes, MaxContracts,
                    cancellation.Token, costs);
                if (version != _resultVersion) return;
                DrawResult(result);
                ResultSummary = $"{result.FinalCapital:N2} {result.CurrencyCode} · 손익 {result.TotalProfit:+#,##0.00;-#,##0.00;0.00} · 수익률 {result.ProfitRate:P2} · 승률 {result.WinRate:P1} · 거래 {result.CompletedTrades}회 · 진입 {result.EntryCount}회 / 청산 {result.ExitCount}회 · {result.BarsProcessed:N0}봉";
                if (result.HasOpenPosition)
                    ResultSummary += $" · 미청산 {result.PositionSide} {result.PositionQuantity}계약";
                if (result.Metrics is { } metrics)
                    ResultSummary += $" · 비용 전 {metrics.GrossProfit:N2} · 체결 비용 {metrics.TotalCosts:N2} · 최대 낙폭 {metrics.MaxDrawdown:P2} · 위험조정 {metrics.RiskAdjustedReturn:N3}";
                SimulationStatus = $"완료 · {result.FirstBarDate:g} ~ {result.LastBarDate:g} · 편도 수수료 {costs.Commission:N2} · 편도 슬리피지 {costs.SlippageTicks:N2}틱";
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (version == _resultVersion) SimulationStatus = $"시뮬레이션 실패: {ex.Message}";
                _loggingService.Log($"해외선물 시뮬레이션 실패: {ex.Message}");
            }
            finally
            {
                _simulationCancellation = null;
                IsSimulationRunning = false;
            }
        }

        private void LoadMainStrategyFiles()
        {
            MainStrategyFiles.Clear();
            MainStrategyFiles.Add(string.Empty);
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "strategy", "ofuture", "main");
            if (!Directory.Exists(directory)) return;
            foreach (var path in Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName))
                MainStrategyFiles.Add(Path.GetFileName(path));
        }

        private void ResetResults()
        {
            _resultVersion++;
            _simulationCancellation?.Cancel();
            SimulationPlot.Plot.Clear();
            _markers.Draw([], _ => null, false);
            OFuturePriceGuidePlot.Draw(SimulationPlot.Plot, null);
            SimulationPlot.Refresh();
            ResultSummary = "—";
            if (_selectedAssignment is null) SimulationStatus = "관심계약을 선택하세요";
            else if (string.IsNullOrWhiteSpace(_selectedAssignment.MainStrategyPath)) SimulationStatus = "해외선물 주전략을 선택하세요";
            else SimulationStatus = "시뮬레이션 대기";
        }

        private void DrawResult(OFutureSimulationRunResult result)
        {
            var plot = SimulationPlot.Plot;
            var daily = result.IntervalMinutes == 0;
            var width = daily ? TimeSpan.FromDays(1) : TimeSpan.FromMinutes(result.IntervalMinutes);
            var epoch = new DateTime(2000, 1, 1);
            var slots = new Dictionary<DateTime, double>();
            var candles = result.ChartBars.Select((bar, i) =>
            {
                var slot = daily ? bar.Timestamp : epoch + width * i;
                slots[bar.Timestamp] = slot.ToOADate();
                return new OHLC(bar.Open, bar.High, bar.Low, bar.Close, slot, width);
            }).ToArray();
            plot.Add.Candlestick(candles);
            plot.Axes.DateTimeTicksBottom();
            if (!daily && plot.Axes.Bottom.TickGenerator is ScottPlot.TickGenerators.DateTimeAutomatic ticks)
                ticks.LabelFormatter = tick =>
                {
                    var index = (int)Math.Round((tick - epoch) / width);
                    return index >= 0 && index < result.ChartBars.Count
                        ? result.ChartBars[index].Timestamp.ToString("MM/dd HH:mm") : string.Empty;
                };
            _markers.Draw(result.ChartEvents, timestamp => slots.TryGetValue(timestamp, out var x) ? x : null, daily);
            OFuturePriceGuidePlot.Draw(plot, result.PriceGuide);
            plot.Axes.AutoScale();
            SimulationPlot.Refresh();
        }
    }
}
