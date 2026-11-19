using System.IO;

namespace DumbTrader.Services
{
    /// <summary>해외선물 예수금과 잔고 현황을 조회합니다 (CIDBQ03000).</summary>
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
                return new(false, "계좌 구분 코드, 계좌번호와 비밀번호가 필요합니다.", Array.Empty<OFutureBalanceRow>());
            if (request.AccountTypeCode.Length != 1 || request.AccountNumber.Length > 20 || request.Password.Length > 8)
                return new(false, "계좌 정보의 길이가 올바르지 않습니다.", Array.Empty<OFutureBalanceRow>());
            if (!string.IsNullOrEmpty(request.TradeDate) && (request.TradeDate.Length != 8 || !request.TradeDate.All(char.IsDigit)))
                return new(false, "거래일자는 YYYYMMDD 형식이어야 합니다.", Array.Empty<OFutureBalanceRow>());
            if (!_session.IsConnected)
                return new(false, "Xing에 연결되어 있지 않습니다. 로그인 후 다시 조회하세요.", Array.Empty<OFutureBalanceRow>());

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var query = _query;
                var dispatched = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
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
                        dispatched.TrySetResult(result < 0 ? query.GetErrorMessage(query.GetLastError()) : null);
                        return result;
                    }
                    catch (Exception ex) { dispatched.TrySetException(ex); throw; }
                }, resetQuery: () => ReplaceQuery(query), onRejected: reason => dispatched.TrySetException(new InvalidOperationException(reason)),
                    cancellationToken: cancellationToken, responseTimeout: _timeout))
                    return new(false, "공통 TR 요청을 대기열에 추가하지 못했습니다.", Array.Empty<OFutureBalanceRow>());

                var requestError = await dispatched.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (requestError is not null) return new(false, requestError, Array.Empty<OFutureBalanceRow>());
                var rows = await _pending!.Task.WaitAsync(_timeout, cancellationToken).ConfigureAwait(false);
                return new(true, "해외선물 예수금과 잔고 조회를 완료했습니다.", rows);
            }
            catch (TimeoutException)
            {
                return new(false, "CIDBQ03000 응답 시간이 초과되었습니다.", Array.Empty<OFutureBalanceRow>());
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logging.Log($"{TrCode} 조회 실패: {ex.Message}");
                return new(false, ex.Message, Array.Empty<OFutureBalanceRow>());
            }
            finally { _pending = null; _gate.Release(); }
        }

        private IXAQueryService CreateQuery()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Res", $"{TrCode}.res");
            var query = new XAQueryService();
            // Xing can leave ResFileName empty even after a successful LoadFromResFile.
            if (!query.LoadFromResFile(path)) throw new InvalidOperationException($"TR 리소스 로드 실패: {path}");
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
