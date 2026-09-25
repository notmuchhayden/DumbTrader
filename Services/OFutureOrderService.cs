using System.Globalization;
using DumbTrader.Models;

namespace DumbTrader.Services
{
    /// <summary>해외선물 신규/정정/취소 주문 (CIDBT00100/00900/01000).</summary>
    public sealed class OFutureOrderService
    {
        private const string NewOrderTrCode = "CIDBT00100";
        private const string ModifyOrderTrCode = "CIDBT00900";
        private const string CancelOrderTrCode = "CIDBT01000";

        private readonly IXASessionService _sessionService;
        private readonly LoggingService _loggingService;
        private readonly IXAQueryService _newOrderQuery;
        private readonly IXAQueryService _modifyOrderQuery;
        private readonly IXAQueryService _cancelOrderQuery;
        private readonly SemaphoreSlim _orderGate = new(1, 1);
        private readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(10);
        private TaskCompletionSource<OFutureOrderResult>? _pendingResult;

        public OFutureOrderService(IXASessionService sessionService, LoggingService loggingService)
        {
            _sessionService = sessionService;
            _loggingService = loggingService;
            _newOrderQuery = CreateQuery("Res\\CIDBT00100.res", ReceiveNewOrderData);
            _modifyOrderQuery = CreateQuery("Res\\CIDBT00900.res", ReceiveModifyOrderData);
            _cancelOrderQuery = CreateQuery("Res\\CIDBT01000.res", ReceiveCancelOrderData);
        }

        public Task<OFutureOrderResult> PlaceOrderAsync(OFutureOrderRequest request, CancellationToken cancellationToken = default)
        {
            var validation = ValidateOrderRequest(request, requireSide: true, requireOrderPattern: true, requireOriginalOrder: false, requireProductType: false, requireQuantity: true);
            if (validation is not null) return Task.FromResult(validation);
            return ExecuteAsync(NewOrderTrCode, _newOrderQuery, () =>
            {
                Set(_newOrderQuery, "CIDBT00100InBlock1", "RecCnt", "1");
                Set(_newOrderQuery, "CIDBT00100InBlock1", "OrdDt", request.OrderDate);
                Set(_newOrderQuery, "CIDBT00100InBlock1", "BrnCode", request.BranchCode);
                Set(_newOrderQuery, "CIDBT00100InBlock1", "AcntNo", request.AccountNumber);
                Set(_newOrderQuery, "CIDBT00100InBlock1", "Pwd", request.Password);
                Set(_newOrderQuery, "CIDBT00100InBlock1", "IsuCodeVal", request.Symbol);
                Set(_newOrderQuery, "CIDBT00100InBlock1", "FutsOrdTpCode", request.OrderTypeCode);
                Set(_newOrderQuery, "CIDBT00100InBlock1", "BnsTpCode", request.BuySellCode);
                Set(_newOrderQuery, "CIDBT00100InBlock1", "AbrdFutsOrdPtnCode", request.OrderPatternCode);
                Set(_newOrderQuery, "CIDBT00100InBlock1", "CrcyCode", request.CurrencyCode);
                Set(_newOrderQuery, "CIDBT00100InBlock1", "OvrsDrvtOrdPrc", Format(request.Price));
                Set(_newOrderQuery, "CIDBT00100InBlock1", "CndiOrdPrc", Format(request.ConditionPrice));
                Set(_newOrderQuery, "CIDBT00100InBlock1", "OrdQty", Format(request.Quantity));
                Set(_newOrderQuery, "CIDBT00100InBlock1", "PrdtCode", request.ProductCode);
                Set(_newOrderQuery, "CIDBT00100InBlock1", "DueYymm", request.MaturityYearMonth);
                Set(_newOrderQuery, "CIDBT00100InBlock1", "ExchCode", request.ExchangeCode);
            }, cancellationToken);
        }

        public Task<OFutureOrderResult> ModifyOrderAsync(OFutureOrderRequest request, CancellationToken cancellationToken = default)
        {
            var validation = ValidateOrderRequest(request, requireSide: true, requireOrderPattern: true, requireOriginalOrder: true, requireProductType: false, requireQuantity: true);
            if (validation is not null) return Task.FromResult(validation);
            return ExecuteAsync(ModifyOrderTrCode, _modifyOrderQuery, () =>
            {
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "RecCnt", "1");
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "OrdDt", request.OrderDate);
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "RegBrnNo", request.BranchCode);
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "AcntNo", request.AccountNumber);
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "Pwd", request.Password);
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "OvrsFutsOrgOrdNo", request.OriginalOrderNumber);
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "IsuCodeVal", request.Symbol);
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "FutsOrdTpCode", request.OrderTypeCode);
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "BnsTpCode", request.BuySellCode);
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "FutsOrdPtnCode", request.OrderPatternCode);
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "CrcyCodeVal", request.CurrencyCode);
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "OvrsDrvtOrdPrc", Format(request.Price));
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "CndiOrdPrc", Format(request.ConditionPrice));
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "OrdQty", Format(request.Quantity));
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "OvrsDrvtPrdtCode", request.ProductCode);
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "DueYymm", request.MaturityYearMonth);
                Set(_modifyOrderQuery, "CIDBT00900InBlock1", "ExchCode", request.ExchangeCode);
            }, cancellationToken);
        }

        public Task<OFutureOrderResult> CancelOrderAsync(OFutureOrderRequest request, CancellationToken cancellationToken = default)
        {
            var validation = ValidateOrderRequest(request, requireSide: false, requireOrderPattern: false, requireOriginalOrder: true, requireProductType: true, requireQuantity: false);
            if (validation is not null) return Task.FromResult(validation);
            return ExecuteAsync(CancelOrderTrCode, _cancelOrderQuery, () =>
            {
                Set(_cancelOrderQuery, "CIDBT01000InBlock1", "RecCnt", "1");
                Set(_cancelOrderQuery, "CIDBT01000InBlock1", "OrdDt", request.OrderDate);
                Set(_cancelOrderQuery, "CIDBT01000InBlock1", "BrnNo", request.BranchCode);
                Set(_cancelOrderQuery, "CIDBT01000InBlock1", "AcntNo", request.AccountNumber);
                Set(_cancelOrderQuery, "CIDBT01000InBlock1", "Pwd", request.Password);
                Set(_cancelOrderQuery, "CIDBT01000InBlock1", "IsuCodeVal", request.Symbol);
                Set(_cancelOrderQuery, "CIDBT01000InBlock1", "OvrsFutsOrgOrdNo", request.OriginalOrderNumber);
                Set(_cancelOrderQuery, "CIDBT01000InBlock1", "FutsOrdTpCode", request.OrderTypeCode);
                Set(_cancelOrderQuery, "CIDBT01000InBlock1", "PrdtTpCode", request.ProductTypeCode);
                Set(_cancelOrderQuery, "CIDBT01000InBlock1", "ExchCode", request.ExchangeCode);
            }, cancellationToken);
        }

        private async Task<OFutureOrderResult> ExecuteAsync(string trCode, IXAQueryService query, Action populate, CancellationToken cancellationToken)
        {
            var validation = Validate(trCode, query);
            if (validation is not null) return validation;

            await _orderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _pendingResult = new TaskCompletionSource<OFutureOrderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                var blockName = trCode + "InBlock1";
                query.ClearBlockdata(blockName);
                populate();

                var result = query.Request(false);
                if (result < 0)
                {
                    var code = query.GetLastError();
                    var message = query.GetErrorMessage(code);
                    _loggingService.Log($"{trCode} request failed: {code} {message}");
                    return new OFutureOrderResult(false, $"Request failed: {message}");
                }

                try
                {
                    return await _pendingResult.Task.WaitAsync(_requestTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _loggingService.Log($"{trCode} response timeout.");
                    return new OFutureOrderResult(false, "해외선물 주문 응답 시간이 초과되었습니다.");
                }
            }
            finally
            {
                _pendingResult = null;
                _orderGate.Release();
            }
        }

        private OFutureOrderResult? Validate(string trCode, IXAQueryService query)
        {
            if (!_sessionService.IsConnected) return new OFutureOrderResult(false, "Xing 세션에 연결되어 있지 않습니다.");
            if (string.IsNullOrWhiteSpace(query.ResFileName)) return new OFutureOrderResult(false, $"{trCode} TR 리소스가 로드되지 않았습니다.");
            return null;
        }

        private static OFutureOrderResult? ValidateOrderRequest(
            OFutureOrderRequest request,
            bool requireSide,
            bool requireOrderPattern,
            bool requireOriginalOrder,
            bool requireProductType,
            bool requireQuantity)
        {
            if (request is null) return new OFutureOrderResult(false, "주문 정보가 없습니다.");
            if (string.IsNullOrWhiteSpace(request.BranchCode) || string.IsNullOrWhiteSpace(request.AccountNumber) || string.IsNullOrWhiteSpace(request.Password))
                return new OFutureOrderResult(false, "지점코드, 계좌번호, 비밀번호를 모두 입력해야 합니다.");
            if (string.IsNullOrWhiteSpace(request.Symbol) || string.IsNullOrWhiteSpace(request.OrderTypeCode) || string.IsNullOrWhiteSpace(request.ExchangeCode))
                return new OFutureOrderResult(false, "계약코드, 주문구분코드, 거래소코드는 필수입니다.");
            if (requireSide && (string.IsNullOrWhiteSpace(request.BuySellCode) || string.IsNullOrWhiteSpace(request.CurrencyCode)))
                return new OFutureOrderResult(false, "매수/매도 코드와 통화코드는 필수입니다.");
            if (requireOrderPattern && string.IsNullOrWhiteSpace(request.OrderPatternCode))
                return new OFutureOrderResult(false, "주문유형 코드는 필수입니다.");
            if (requireOriginalOrder && string.IsNullOrWhiteSpace(request.OriginalOrderNumber))
                return new OFutureOrderResult(false, "원주문번호는 필수입니다.");
            if (requireProductType && string.IsNullOrWhiteSpace(request.ProductTypeCode))
                return new OFutureOrderResult(false, "상품유형 코드는 필수입니다.");
            if (requireSide && (string.IsNullOrWhiteSpace(request.ProductCode) || string.IsNullOrWhiteSpace(request.MaturityYearMonth)))
                return new OFutureOrderResult(false, "상품코드와 만기월은 필수입니다.");
            if (requireQuantity && request.Quantity <= 0)
                return new OFutureOrderResult(false, "주문 수량은 0보다 커야 합니다.");
            if (!double.IsFinite(request.Price) || !double.IsFinite(request.ConditionPrice))
                return new OFutureOrderResult(false, "주문 가격이 올바르지 않습니다.");
            return null;
        }

        private IXAQueryService CreateQuery(string path, XA_DATASETLib._IXAQueryEvents_ReceiveDataEventHandler callback)
        {
            var query = new XAQueryService();
            if (!query.LoadFromResFile(path))
                throw new InvalidOperationException($"해외선물 주문 TR 리소스 로드 실패: {path}");
            query.AddReceiveDataEventHandler(callback);
            return query;
        }

        private void ReceiveNewOrderData(string trCode) => CompleteIfMatching(trCode, NewOrderTrCode);
        private void ReceiveModifyOrderData(string trCode) => CompleteIfMatching(trCode, ModifyOrderTrCode);
        private void ReceiveCancelOrderData(string trCode) => CompleteIfMatching(trCode, CancelOrderTrCode);

        private void CompleteIfMatching(string receivedTrCode, string expectedTrCode)
        {
            if (!string.Equals(receivedTrCode, expectedTrCode, StringComparison.OrdinalIgnoreCase)) return;
            var query = expectedTrCode switch
            {
                NewOrderTrCode => _newOrderQuery,
                ModifyOrderTrCode => _modifyOrderQuery,
                _ => _cancelOrderQuery
            };
            var orderNumber = query.GetFieldData(expectedTrCode + "OutBlock2", "OvrsFutsOrdNo", 0).Trim();
            var result = !string.IsNullOrWhiteSpace(orderNumber);
            var innerMessage = expectedTrCode == NewOrderTrCode
                ? string.Empty
                : query.GetFieldData(expectedTrCode + "OutBlock2", "InnerMsgCnts", 0).Trim();
            var message = result
                ? $"해외선물 주문 접수: {orderNumber}"
                : string.IsNullOrWhiteSpace(innerMessage) ? "주문번호가 수신되지 않았습니다." : innerMessage;
            _loggingService.Log($"{expectedTrCode} {(result ? "success" : "failed")}: {message}");
            _pendingResult?.TrySetResult(new OFutureOrderResult(result, message, orderNumber));
        }

        private static void Set(IXAQueryService query, string block, string field, string value)
            => query.SetFieldData(block, field, 0, value ?? string.Empty);
        private static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Format(double value) => value.ToString("0.################", CultureInfo.InvariantCulture);
    }
}
