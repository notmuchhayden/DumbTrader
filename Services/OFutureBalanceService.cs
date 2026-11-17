using System.IO;

namespace DumbTrader.Services
{
    /// <summary>?´ì™¸? ë¬¼ ?ˆìˆ˜ê¸??”ê³ ?„í™© ì¡°íšŒ (CIDBQ03000).</summary>
    public sealed class OFutureBalanceService
    {
        private const string TrCode = "CIDBQ03000";
        private const string Block = "CIDBQ03000OutBlock2";
        private readonly IXASessionService _session;
        private readonly LoggingService _logging;
        private readonly XingTrQuerySchedulerService _scheduler;
        private IXAQueryService _query;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);
        private TaskCompletionSource<IReadOnlyList<OFutureBalanceRow>>? _pending;

        public OFutureBalanceService(IXASessionService session, LoggingService logging, XingTrQuerySchedulerService scheduler)
        {
            _session = session;
            _logging = logging;
            _scheduler = scheduler;
            _query = CreateQuery();
        }

        public async Task<OFutureBalanceResult> QueryAsync(OFutureBalanceRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.AccountTypeCode) ||
                string.IsNullOrWhiteSpace(request.AccountNumber) || string.IsNullOrWhiteSpace(request.Password))
                return new(false, "ê³„ì¢Œêµ¬ë¶„ì½”ë“œ, ê³„ì¢Œë²ˆí˜¸, ë¹„ë?ë²ˆí˜¸ë¥??…ë ¥?´ì•¼ ?©ë‹ˆ??", Array.Empty<OFutureBalanceRow>());
            if (request.AccountTypeCode.Length != 1 || request.AccountNumber.Length > 20 || request.Password.Length > 8)
                return new(false, "ê³„ì¢Œ ?•ë³´??ê¸¸ì´ê°€ ?¬ë°”ë¥´ì? ?ŠìŠµ?ˆë‹¤.", Array.Empty<OFutureBalanceRow>());
            if (!string.IsNullOrEmpty(request.TradeDate) && (request.TradeDate.Length != 8 || !request.TradeDate.All(char.IsDigit)))
                return new(false, "ê±°ë˜?¼ì??YYYYMMDD ?•ì‹?´ì–´???©ë‹ˆ??", Array.Empty<OFutureBalanceRow>());
            if (!_session.IsConnected || string.IsNullOrWhiteSpace(_query.ResFileName))
                return new(false, "Xing ?¸ì…˜ ?ëŠ” CIDBQ03000 ë¦¬ì†Œ?¤ê? ì¤€ë¹„ë˜ì§€ ?Šì•˜?µë‹ˆ??", Array.Empty<OFutureBalanceRow>());

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var query = _query;
                var dispatched = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!_scheduler.Enqueue(TrCode, query, () =>
                {
                    try
                    {
                        _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        query.ClearBlockdata(TrCode + "InBlock1");
                        query.SetFieldData(TrCode + "InBlock1", "RecCnt", 0, "1");
                        query.SetFieldData(TrCode + "InBlock1", "AcntTpCode", 0, request.AccountTypeCode);
                        query.SetFieldData(TrCode + "InBlock1", "AcntNo", 0, request.AccountNumber);
                        query.SetFieldData(TrCode + "InBlock1", "AcntPwd", 0, request.Password);
                        query.SetFieldData(TrCode + "InBlock1", "TrdDt", 0, request.TradeDate ?? string.Empty);
                        var result = query.Request(false);
                        dispatched.TrySetResult(result);
                        return result;
                    }
                    catch (Exception ex) { dispatched.TrySetException(ex); throw; }
                }, resetQuery: () => ReplaceQuery(query), onRejected: reason => dispatched.TrySetException(new InvalidOperationException(reason)),
                    cancellationToken: cancellationToken, responseTimeout: _timeout))
                    return new(false, "ê³µí†µ TR ?”ì²­ ?ì— ì¶”ê??˜ì? ëª»í–ˆ?µë‹ˆ??", Array.Empty<OFutureBalanceRow>());

                var code = await dispatched.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (code < 0) return new(false, query.GetErrorMessage(query.GetLastError()), Array.Empty<OFutureBalanceRow>());
                var rows = await _pending!.Task.WaitAsync(_timeout, cancellationToken).ConfigureAwait(false);
                return new(true, "?´ì™¸? ë¬¼ ?ˆìˆ˜ê¸??”ê³ ?„í™© ì¡°íšŒ ?„ë£Œ", rows);
            }
            catch (TimeoutException)
            {
                return new(false, "CIDBQ03000 ?‘ë‹µ ?œê°„??ì´ˆê³¼?˜ì—ˆ?µë‹ˆ??", Array.Empty<OFutureBalanceRow>());
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logging.Log($"{TrCode} ì¡°íšŒ ?¤íŒ¨: {ex.Message}");
                return new(false, ex.Message, Array.Empty<OFutureBalanceRow>());
            }
            finally { _pending = null; _gate.Release(); }
        }

        private IXAQueryService CreateQuery()
        {
            const string path = "Res\\CIDBQ03000.res";
            var query = new XAQueryService();
            if (!query.LoadFromResFile(path)) throw new InvalidOperationException($"TR ë¦¬ì†Œ??ë¡œë“œ ?¤íŒ¨: {path}");
            query.AddReceiveDataEventHandler(code =>
            {
                try
                {
                    if (!ReferenceEquals(_query, query) || !string.Equals(code, TrCode, StringComparison.OrdinalIgnoreCase)) return;
                    var rows = new List<OFutureBalanceRow>();
                    var count = Math.Max(0, query.GetBlockCount(Block));
                    for (var i = 0; i < count; i++)
                        rows.Add(new OFutureBalanceRow(
                            Field(query, i, "AcntNo"), Field(query, i, "TrdDt"), Field(query, i, "CrcyObjCode"),
                            Field(query, i, "OvrsFutsDps"), Field(query, i, "CustmMnyioAmt"), Field(query, i, "AbrdFutsLqdtPnlAmt"),
                            Field(query, i, "AbrdFutsCmsnAmt"), Field(query, i, "PrexchDps"), Field(query, i, "EvalAssetAmt"),
                            Field(query, i, "AbrdFutsCsgnMgn"), Field(query, i, "AbrdFutsAddMgn"),
                            Field(query, i, "AbrdFutsWthdwAbleAmt"), Field(query, i, "AbrdFutsOrdAbleAmt"),
                            Field(query, i, "AbrdFutsEvalPnlAmt"), Field(query, i, "LastSettPnlAmt"),
                            Field(query, i, "OvrsOptSettAmt"), Field(query, i, "OvrsOptBalEvalAmt")));
                    _pending?.TrySetResult(rows);
                }
                catch (Exception ex) { _pending?.TrySetException(ex); }
                finally { _scheduler.Complete(TrCode, query); }
            });
            query.AddReceiveMessageEventHandler((systemError, messageCode, message) =>
            {
                if (systemError && ReferenceEquals(_query, query)) _scheduler.Fail(TrCode, query, $"{messageCode}: {message}");
            });
            return query;
        }

        private static string Field(IXAQueryService query, int index, string name)
        {
            return query.GetFieldData(Block, name, index).Trim();
        }

        private void ReplaceQuery(IXAQueryService oldQuery)
        {
            if (!ReferenceEquals(_query, oldQuery)) return;
            _query = CreateQuery();
            if (oldQuery is IDisposable disposable) disposable.Dispose();
        }
    }

    public sealed record OFutureBalanceRequest(string AccountTypeCode, string AccountNumber, string Password, string? TradeDate = null);
    public sealed record OFutureBalanceRow(string AccountNumber, string TradeDate, string CurrencyCode, string Deposit,
        string CustomerCashFlow, string LiquidationPnl, string Commission, string PreExchangeDeposit, string EvaluationAsset,
        string ConsignmentMargin, string AdditionalMargin, string WithdrawableAmount, string OrderableAmount,
        string EvaluationPnl, string LastSettlementPnl, string OverseasOptionSettlement, string OverseasOptionBalanceEvaluation);
    public sealed record OFutureBalanceResult(bool Success, string Message, IReadOnlyList<OFutureBalanceRow> Rows);
}
