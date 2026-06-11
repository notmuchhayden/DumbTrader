using System.Globalization;
using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services
{
    public sealed class SimulationService
    {
        private readonly IDbContextFactory<DumbTraderDbContext> _dbFactory;
        private readonly StrategyService _strategyService;
        private readonly LoggingService _loggingService;

        public SimulationService(
            IDbContextFactory<DumbTraderDbContext> dbFactory,
            StrategyService strategyService,
            LoggingService loggingService)
        {
            _dbFactory = dbFactory;
            _strategyService = strategyService;
            _loggingService = loggingService;
        }

        public async Task<SimulationRunResult?> RunAsync(
            StrategyStockInfo watchlist,
            int seedMoney,
            DateTime startDate,
            CancellationToken cancellationToken = default)
        {
            if (watchlist?.Stock == null)
                return null;

            var shcode = watchlist.Stock.shcode;
            if (string.IsNullOrWhiteSpace(shcode))
                return null;

            _loggingService.Log($"시뮬레이션 시작: 종목코드={shcode}, 종목명={watchlist.Stock.hname}, 시작일={startDate:yyyy-MM-dd}, 초기자본={seedMoney}");

            _strategyService.ResetContext(shcode);

            using var dbContext = _dbFactory.CreateDbContext();

            string startDateKey = startDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var chartItems = await dbContext.StockChartDatas
                .Where(x => x.shcode == shcode && string.Compare(x.date, startDateKey) >= 0)
                .OrderBy(x => x.date)
                .ToListAsync(cancellationToken);

            return await Task.Run(() => RunSimulation(watchlist, chartItems, seedMoney, cancellationToken), cancellationToken);
        }

        private SimulationRunResult RunSimulation(
            StrategyStockInfo watchlist,
            IReadOnlyList<StockChartData> chartItems,
            int seedMoney,
            CancellationToken cancellationToken)
        {
            long cash = seedMoney;
            long quantity = 0;
            long buyAmount = 0;
            long lastPrice = 0;
            int completedTrades = 0;
            int winningTrades = 0;

            foreach (var item in chartItems)
            {
                cancellationToken.ThrowIfCancellationRequested();

                lastPrice = item.close;
                var simData = CreateSimulationData(item);
                var execution = _strategyService.RunWithResult(simData, true, seedMoney);

                if (!execution.Success)
                {
                    _loggingService.Log($"시뮬레이션 전략 실행 실패: 종목코드={item.shcode}, 날짜={item.date}, 액션={execution.Action}");
                    continue;
                }

                if (execution.Action == "BUY" && quantity == 0 && item.close > 0)
                {
                    quantity = cash / item.close;
                    if (quantity > 0)
                    {
                        buyAmount = quantity * item.close;
                        cash -= buyAmount;
                    }
                }
                else if (execution.Action == "SELL" && quantity > 0)
                {
                    long sellAmount = quantity * item.close;
                    long tradeProfit = sellAmount - buyAmount;
                    cash += sellAmount;
                    quantity = 0;
                    buyAmount = 0;
                    completedTrades++;

                    if (tradeProfit > 0)
                        winningTrades++;
                }
            }

            long initialCapital = seedMoney;
            long finalCapital = cash + (quantity * lastPrice);
            long totalProfit = finalCapital - initialCapital;
            double profitRate = initialCapital == 0 ? 0 : (double)totalProfit / initialCapital;
            double winRate = completedTrades == 0 ? 0 : (double)winningTrades / completedTrades;

            _loggingService.Log($"시뮬레이션 완료: 종목코드={watchlist.Stock.shcode}, 종목명={watchlist.Stock.hname}, 최종자본={finalCapital}, 손익={totalProfit}, 거래횟수={completedTrades}");

            return new SimulationRunResult(
                watchlist.Stock.shcode,
                watchlist.Stock.hname,
                initialCapital,
                finalCapital,
                totalProfit,
                profitRate,
                winRate,
                completedTrades);
        }

        private static RealS3_K3_Data CreateSimulationData(StockChartData item)
        {
            return new RealS3_K3_Data
            {
                shcode = item.shcode,
                chetime = item.date,
                sign = item.sign,
                price = item.close,
                open = item.open,
                high = item.high,
                low = item.low,
                volume = item.jdiff_vol,
                value = item.value
            };
        }
    }
}
