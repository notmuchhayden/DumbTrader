using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;

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
            set => SetProperty(ref _selectedSimulationPeriod, value);
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

        public OFutureStrategyViewModel(
            OFutureStrategyService strategyService,
            OFutureSimulationService simulationService,
            LoggingService loggingService)
        {
            _strategyService = strategyService;
            _simulationService = simulationService;
            _loggingService = loggingService;
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
            SimulationStatus = "저장된 봉과 전략 확인 중";
            ResultSummary = "—";
            try
            {
                var result = await _simulationService.RunAsync(
                    _selectedAssignment, SimulationSeedMoney, SimulationStartDate,
                    SelectedSimulationPeriod.IntervalMinutes, MaxContracts);
                ResultSummary = $"{result.FinalCapital:N2} {result.CurrencyCode} · 손익 {result.TotalProfit:+#,##0.00;-#,##0.00;0.00} · 수익률 {result.ProfitRate:P2} · 승률 {result.WinRate:P1} · 거래 {result.CompletedTrades}회 · 진입 {result.EntryCount}회 / 청산 {result.ExitCount}회 · {result.BarsProcessed:N0}봉";
                if (result.HasOpenPosition)
                    ResultSummary += $" · 미청산 {result.PositionSide} {result.PositionQuantity}계약";
                SimulationStatus = $"완료 · {result.FirstBarDate:yyyy-MM-dd} ~ {result.LastBarDate:yyyy-MM-dd} · 수수료·슬리피지·환전 미반영";
            }
            catch (Exception ex)
            {
                SimulationStatus = $"시뮬레이션 실패: {ex.Message}";
                _loggingService.Log($"해외선물 시뮬레이션 실패: {ex.Message}");
            }
            finally { IsSimulationRunning = false; }
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
            ResultSummary = "—";
            if (_selectedAssignment is null) SimulationStatus = "관심계약을 선택하세요";
            else if (string.IsNullOrWhiteSpace(_selectedAssignment.MainStrategyPath)) SimulationStatus = "해외선물 주전략을 선택하세요";
            else SimulationStatus = "시뮬레이션 대기";
        }
    }
}
