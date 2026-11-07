using System.Globalization;
using DumbTrader.Models;
using Microsoft.EntityFrameworkCore;

namespace DumbTrader.Services
{
    /// <summary>저장된 해외선물 봉과 사용자 main 전략으로 단일 계약의 현금결제 시뮬레이션을 수행한다.</summary>
    public sealed class OFutureSimulationService
    {
        private sealed record SimulationBar(
            string Date, string Time, double Open, double High, double Low, double Close, long Volume)
        {
            public DateTime Timestamp => DateTime.ParseExact(Date + Time, "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        }

        private readonly IDbContextFactory<DumbTraderDbContext> _dbFactory;
        private readonly OFutureStrategyService _strategyService;

        public OFutureSimulationService(
            IDbContextFactory<DumbTraderDbContext> dbFactory,
            OFutureStrategyService strategyService)
        {
            _dbFactory = dbFactory;
            _strategyService = strategyService;
        }

        public async Task<OFutureSimulationRunResult> RunAsync(
            OFutureStrategyAssignment assignment,
            double seedMoney,
            DateTime startDate,
            int intervalMinutes,
            int maxContracts,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(assignment);
            var contract = assignment.Contract;
            if (string.IsNullOrWhiteSpace(contract.Symbol))
                throw new InvalidOperationException("해외선물 계약을 선택하세요.");
            if (string.IsNullOrWhiteSpace(assignment.MainStrategyPath))
                throw new InvalidOperationException("해외선물 주전략을 선택하세요.");
            if (!double.IsFinite(seedMoney) || seedMoney <= 0)
                throw new InvalidOperationException("시뮬레이션 자본금은 0보다 커야 합니다.");
            if (maxContracts is < 1 or > 1000)
                throw new InvalidOperationException("계약 수는 1~1000 범위여야 합니다.");
            if (intervalMinutes is not (0 or 1 or 10))
                throw new InvalidOperationException("시뮬레이션 주기는 일봉, 1분봉 또는 10분봉이어야 합니다.");
            if (!double.IsFinite(contract.TickSize) || contract.TickSize <= 0
                || !double.IsFinite(contract.TickValue) || contract.TickValue <= 0
                || !double.IsFinite(contract.OpeningMargin) || contract.OpeningMargin <= 0)
                throw new InvalidOperationException("계약의 틱 크기·틱 가치·개시증거금이 없어 손익을 계산할 수 없습니다.");

            var scriptCode = await _strategyService.LoadMainScriptAsync(assignment, cancellationToken).ConfigureAwait(false);
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var bars = await LoadBarsAsync(db, contract.Symbol, intervalMinutes, cancellationToken).ConfigureAwait(false);
            if (bars.Count == 0)
                throw new InvalidOperationException(intervalMinutes == 0
                    ? "저장된 일봉이 없습니다. 먼저 해외선물 차트 데이터를 조회하세요."
                    : $"저장된 {intervalMinutes}분봉이 없습니다. 먼저 대시보드에서 해당 주기를 조회하세요.");

            var firstTradingBar = bars.FirstOrDefault(bar => ParseDate(bar.Date) >= startDate.Date);
            if (firstTradingBar is null)
                throw new InvalidOperationException("선택한 시작일 이후의 저장 데이터가 없습니다.");

            var context = new Dictionary<string, object>();
            var positionSide = "FLAT";
            long positionQuantity = 0;
            var entryPrice = 0d;
            var pendingAction = string.Empty;
            var realizedProfit = 0d;
            var entryCount = 0;
            var exitCount = 0;
            var completedTrades = 0;
            var winningTrades = 0;
            var barsProcessed = 0;
            double? lastTradingClose = null;
            DateTime? lastTradingDate = null;

            foreach (var bar in bars)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var barDate = ParseDate(bar.Date);
                var simData = CreateRealData(contract.Symbol, bar);

                // 종가에서 나온 신호는 다음 봉 시가에 체결해 같은 봉 종가를 미리 아는 편향을 줄인다.
                if (pendingAction.Length > 0 && barDate >= startDate.Date)
                {
                    var trade = ApplySignal(pendingAction, bar.Open, contract, seedMoney + realizedProfit,
                        maxContracts, ref positionSide, ref positionQuantity, ref entryPrice);
                    if (trade.Opened)
                    {
                        entryCount++;
                    }
                    if (trade.RealizedProfit is { } tradePnl)
                    {
                        realizedProfit += tradePnl;
                        completedTrades++;
                        if (tradePnl > 0) winningTrades++;
                        exitCount++;
                    }
                }
                pendingAction = string.Empty;

                var execution = await _strategyService.RunSimulationStepAsync(
                    scriptCode, contract, simData, db, context, seedMoney + realizedProfit,
                    positionSide, positionQuantity, cancellationToken).ConfigureAwait(false);
                if (!execution.Success)
                    throw new InvalidOperationException($"전략 실행 실패 ({bar.Date} {bar.Time}): {execution.Action}");

                if (barDate >= startDate.Date)
                {
                    barsProcessed++;
                    lastTradingClose = bar.Close;
                    lastTradingDate = barDate;
                    if (execution.Action is "BUY" or "SELL") pendingAction = execution.Action;
                }
            }

            var unrealizedProfit = positionSide == "FLAT" || lastTradingClose is null
                ? 0
                : CalculateProfit(positionSide, entryPrice, lastTradingClose.Value, contract, positionQuantity);
            var totalProfit = realizedProfit + unrealizedProfit;
            var finalCapital = seedMoney + totalProfit;
            return new OFutureSimulationRunResult(
                contract.Symbol,
                contract.Name,
                contract.CurrencyCode,
                intervalMinutes == 0 ? "일봉" : $"{intervalMinutes}분봉",
                seedMoney,
                finalCapital,
                totalProfit,
                totalProfit / seedMoney,
                completedTrades == 0 ? 0 : (double)winningTrades / completedTrades,
                entryCount,
                exitCount,
                completedTrades,
                positionSide != "FLAT",
                positionSide,
                positionQuantity,
                barsProcessed,
                firstTradingBar.Timestamp,
                lastTradingDate ?? firstTradingBar.Timestamp);
        }

        private static async Task<List<SimulationBar>> LoadBarsAsync(
            DumbTraderDbContext db, string symbol, int intervalMinutes, CancellationToken cancellationToken)
        {
            if (intervalMinutes == 0)
            {
                var daily = await db.OFutureDailyBars.AsNoTracking()
                    .Where(item => item.Symbol == symbol && item.SourceTrCode == "o3108" && item.QueryType == "0")
                    .OrderBy(item => item.TradeDate)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                return daily.Select(item => new SimulationBar(item.TradeDate, "000000", item.Open,
                        item.High, item.Low, item.Close, item.Volume))
                    .Where(IsValid).ToList();
            }

            var minute = await db.OFutureMinuteBars.AsNoTracking()
                .Where(item => item.Symbol == symbol && item.IntervalMinutes == intervalMinutes)
                .OrderBy(item => item.TradeDate).ThenBy(item => item.TradeTime)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return minute.Select(item => new SimulationBar(item.TradeDate, item.TradeTime, item.Open,
                    item.High, item.Low, item.Close, item.Volume))
                .Where(IsValid).ToList();
        }

        private static bool IsValid(SimulationBar bar)
            => DateTime.TryParseExact(bar.Date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                && DateTime.TryParseExact(bar.Time, "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                && bar.Volume >= 0 && double.IsFinite(bar.Open) && double.IsFinite(bar.High)
                && double.IsFinite(bar.Low) && double.IsFinite(bar.Close)
                && bar.High >= Math.Max(bar.Open, bar.Close) && bar.Low <= Math.Min(bar.Open, bar.Close);

        private (bool Opened, double? RealizedProfit) ApplySignal(
            string action,
            double executionPrice,
            OFutureContract contract,
            double seedMoney,
            int maxContracts,
            ref string positionSide,
            ref long positionQuantity,
            ref double entryPrice)
        {
            if (positionSide == "FLAT")
            {
                if (seedMoney <= 0 || contract.OpeningMargin <= 0) return (false, null);
                var affordable = (long)Math.Floor(seedMoney / contract.OpeningMargin);
                positionQuantity = Math.Min(maxContracts, affordable);
                if (positionQuantity <= 0) return (false, null);
                positionSide = action == "BUY" ? "LONG" : "SHORT";
                entryPrice = executionPrice;
                return (true, null);
            }

            var closesPosition = (positionSide == "LONG" && action == "SELL")
                || (positionSide == "SHORT" && action == "BUY");
            if (!closesPosition) return (false, null);
            var profit = CalculateProfit(positionSide, entryPrice, executionPrice, contract, positionQuantity);
            positionSide = "FLAT";
            positionQuantity = 0;
            entryPrice = 0;
            return (false, profit);
        }

        private static double CalculateProfit(
            string positionSide, double entryPrice, double exitPrice, OFutureContract contract, long quantity)
        {
            var direction = positionSide == "LONG" ? 1d : -1d;
            var tickCount = Math.Round(direction * (exitPrice - entryPrice) / contract.TickSize,
                MidpointRounding.AwayFromZero);
            return tickCount * contract.TickValue * quantity;
        }

        private static OFutureRealData CreateRealData(string symbol, SimulationBar bar) => new()
        {
            Symbol = symbol,
            OverseasDate = bar.Date,
            KoreaDate = bar.Date,
            TradeTime = bar.Time,
            KoreaTradeTime = bar.Time,
            Price = bar.Close,
            Open = bar.Open,
            High = bar.High,
            Low = bar.Low,
            TradeQuantity = bar.Volume,
            TotalQuantity = bar.Volume,
            ReceivedAtUtc = DateTime.UtcNow
        };

        private static DateTime ParseDate(string date)
            => DateTime.ParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture);
    }
}
