using System.Windows.Input;
using DumbTrader.Core;
using DumbTrader.Models;
using DumbTrader.Services;

namespace DumbTrader.ViewModels
{
    public sealed class StockSimulationViewModel : ViewModelBase
    {
        private readonly SimulationService _simulationService;
        private readonly LoggingService _loggingService;

        private StrategyStockInfo? _selectedWatchlist;

        private int _simulationSeedMoney;
        public int SimulationSeedMoney
        {
            get => _simulationSeedMoney;
            set
            {
                if (SetProperty(ref _simulationSeedMoney, value))
                {
                    if (SimInitialCapital == 0)
                    {
                        SimInitialCapital = value;
                        SimFinalCapital = value;
                    }
                }
            }
        }

        private DateTime _simulationStartDate;
        public DateTime SimulationStartDate
        {
            get => _simulationStartDate;
            set => SetProperty(ref _simulationStartDate, value);
        }

        private long _simInitialCapital;
        public long SimInitialCapital
        {
            get => _simInitialCapital;
            set => SetProperty(ref _simInitialCapital, value);
        }

        private string _simStockName = string.Empty;
        public string SimStockName
        {
            get => _simStockName;
            set => SetProperty(ref _simStockName, value);
        }

        private long _simFinalCapital;
        public long SimFinalCapital
        {
            get => _simFinalCapital;
            set => SetProperty(ref _simFinalCapital, value);
        }

        private long _simTotalProfit;
        public long SimTotalProfit
        {
            get => _simTotalProfit;
            set => SetProperty(ref _simTotalProfit, value);
        }

        private double _simProfitRate;
        public double SimProfitRate
        {
            get => _simProfitRate;
            set => SetProperty(ref _simProfitRate, value);
        }

        private double _simWinRate;
        public double SimWinRate
        {
            get => _simWinRate;
            set => SetProperty(ref _simWinRate, value);
        }

        private int _simTotalTrades;
        public int SimTotalTrades
        {
            get => _simTotalTrades;
            set => SetProperty(ref _simTotalTrades, value);
        }

        public ICommand StartSimulationCommand { get; }
        public ICommand ResetSimulationCommand { get; }

        public StockSimulationViewModel(SimulationService simulationService, LoggingService loggingService)
        {
            _simulationService = simulationService;
            _loggingService = loggingService;

            SimulationSeedMoney = 50000000;
            SimulationStartDate = new DateTime(2010, 1, 1);
            ResetSimulationResult();

            StartSimulationCommand = new AsyncRelayCommand(ExecuteStartSimulationAsync);
            ResetSimulationCommand = new RelayCommand(ExecuteResetSimulation);
        }

        public void SetSelectedWatchlist(StrategyStockInfo? watchlist)
        {
            _selectedWatchlist = watchlist;
            if (watchlist == null)
            {
                ResetSimulationResult();
            }
        }

        private async Task ExecuteStartSimulationAsync(object? parameter)
        {
            if (_selectedWatchlist == null)
                return;

            try
            {
                ResetSimulationResult();
                SimStockName = _selectedWatchlist.Stock.hname;

                var result = await _simulationService.RunAsync(_selectedWatchlist, SimulationSeedMoney, SimulationStartDate);
                if (result == null)
                    return;

                ApplyResult(result);
            }
            catch (Exception ex)
            {
                _loggingService.Log($"시뮬레이션 실행 중 오류 발생: {ex.Message}");
            }
        }

        private void ApplyResult(SimulationRunResult result)
        {
            SimStockName = result.StockName;
            SimInitialCapital = result.InitialCapital;
            SimFinalCapital = result.FinalCapital;
            SimTotalProfit = result.TotalProfit;
            SimProfitRate = result.ProfitRate;
            SimWinRate = result.WinRate;
            SimTotalTrades = result.TotalTrades;
        }

        private void ExecuteResetSimulation(object? parameter)
        {
            ResetSimulationResult();
        }

        private void ResetSimulationResult()
        {
            SimStockName = string.Empty;
            SimInitialCapital = SimulationSeedMoney;
            SimFinalCapital = SimulationSeedMoney;
            SimTotalProfit = 0;
            SimProfitRate = 0;
            SimWinRate = 0;
            SimTotalTrades = 0;
        }
    }
}
