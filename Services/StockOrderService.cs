using System.Globalization;
using DumbTrader.Models;

namespace DumbTrader.Services
{
    public sealed class StockOrderService : IStockOrderService
    {
        private const string NewOrderTrCode = "CSPAT00600";
        private const string ModifyOrderTrCode = "CSPAT00700";
        private const string CancelOrderTrCode = "CSPAT00800";
        private const string BuyBnsTpCode = "2";
        private const string SellBnsTpCode = "1";

        private readonly IXASessionService _sessionService;
        private readonly LoggingService _loggingService;
        private readonly IXAQueryService _newOrderQueryService;
        private readonly IXAQueryService _modifyOrderQueryService;
        private readonly IXAQueryService _cancelOrderQueryService;
        private readonly SemaphoreSlim _orderGate = new(1, 1);
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(10);

        private TaskCompletionSource<CSPAT00600OrderResult>? _pendingNewOrderCompletionSource;
        private TaskCompletionSource<CSPAT00700OrderResult>? _pendingModifyOrderCompletionSource;
        private TaskCompletionSource<CSPAT00800OrderResult>? _pendingCancelOrderCompletionSource;

        public StockOrderService(IXASessionService sessionService, LoggingService loggingService)
        {
            _sessionService = sessionService;
            _loggingService = loggingService;

            _newOrderQueryService = CreateQueryService(@"Res\CSPAT00600.res", ReceiveNewOrderData);
            _modifyOrderQueryService = CreateQueryService(@"Res\CSPAT00700.res", ReceiveModifyOrderData);
            _cancelOrderQueryService = CreateQueryService(@"Res\CSPAT00800.res", ReceiveCancelOrderData);
        }

        public Task<CSPAT00600OrderResult> BuyAsync(
            string acntNo,
            string inptPwd,
            string isuNo,
            long ordQty,
            double ordPrc,
            string ordprcPtnCode = "00",
            string mgntrnCode = "",
            string loanDt = "",
            string ordCndiTpCode = "0",
            string mbrNo = "",
            CancellationToken cancellationToken = default)
        {
            var request = new CSPAT00600InBlock1
            {
                AcntNo = acntNo,
                InptPwd = inptPwd,
                IsuNo = isuNo,
                OrdQty = ordQty,
                OrdPrc = ordPrc,
                BnsTpCode = BuyBnsTpCode,
                OrdprcPtnCode = ordprcPtnCode,
                MgntrnCode = mgntrnCode,
                LoanDt = loanDt,
                OrdCndiTpCode = ordCndiTpCode,
                MbrNo = mbrNo
            };

            return PlaceOrderAsync(request, cancellationToken);
        }

        public Task<CSPAT00600OrderResult> SellAsync(
            string acntNo,
            string inptPwd,
            string isuNo,
            long ordQty,
            double ordPrc,
            string ordprcPtnCode = "00",
            string mgntrnCode = "",
            string loanDt = "",
            string ordCndiTpCode = "0",
            string mbrNo = "",
            CancellationToken cancellationToken = default)
        {
            var request = new CSPAT00600InBlock1
            {
                AcntNo = acntNo,
                InptPwd = inptPwd,
                IsuNo = isuNo,
                OrdQty = ordQty,
                OrdPrc = ordPrc,
                BnsTpCode = SellBnsTpCode,
                OrdprcPtnCode = ordprcPtnCode,
                MgntrnCode = mgntrnCode,
                LoanDt = loanDt,
                OrdCndiTpCode = ordCndiTpCode,
                MbrNo = mbrNo
            };

            return PlaceOrderAsync(request, cancellationToken);
        }

        public Task<CSPAT00700OrderResult> ModifyAsync(
            long orgOrdNo,
            string acntNo,
            string inptPwd,
            string isuNo,
            long ordQty,
            double ordPrc,
            string ordprcPtnCode = "00",
            string ordCndiTpCode = "0",
            CancellationToken cancellationToken = default)
        {
            var request = new CSPAT00700InBlock1
            {
                OrgOrdNo = orgOrdNo,
                AcntNo = acntNo,
                InptPwd = inptPwd,
                IsuNo = isuNo,
                OrdQty = ordQty,
                OrdPrc = ordPrc,
                OrdprcPtnCode = ordprcPtnCode,
                OrdCndiTpCode = ordCndiTpCode
            };

            return ModifyOrderAsync(request, cancellationToken);
        }

        public Task<CSPAT00800OrderResult> CancelAsync(
            long orgOrdNo,
            string acntNo,
            string inptPwd,
            string isuNo,
            long ordQty,
            CancellationToken cancellationToken = default)
        {
            var request = new CSPAT00800InBlock1
            {
                OrgOrdNo = orgOrdNo,
                AcntNo = acntNo,
                InptPwd = inptPwd,
                IsuNo = isuNo,
                OrdQty = ordQty
            };

            return CancelOrderAsync(request, cancellationToken);
        }

        public async Task<CSPAT00600OrderResult> PlaceOrderAsync(
            CSPAT00600InBlock1 request,
            CancellationToken cancellationToken = default)
        {
            var validationMessage = ValidateCommon(request.OrdQty);
            if (validationMessage is not null)
            {
                return new CSPAT00600OrderResult(false, validationMessage, null, null);
            }

            await _orderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _pendingNewOrderCompletionSource = new TaskCompletionSource<CSPAT00600OrderResult>(TaskCreationOptions.RunContinuationsAsynchronously);

                _newOrderQueryService.ClearBlockdata("CSPAT00600InBlock1");
                _newOrderQueryService.SetFieldData("CSPAT00600InBlock1", "AcntNo", 0, request.AcntNo);
                _newOrderQueryService.SetFieldData("CSPAT00600InBlock1", "InptPwd", 0, request.InptPwd);
                _newOrderQueryService.SetFieldData("CSPAT00600InBlock1", "IsuNo", 0, request.IsuNo);
                _newOrderQueryService.SetFieldData("CSPAT00600InBlock1", "OrdQty", 0, FormatLong(request.OrdQty));
                _newOrderQueryService.SetFieldData("CSPAT00600InBlock1", "OrdPrc", 0, FormatDouble(request.OrdPrc));
                _newOrderQueryService.SetFieldData("CSPAT00600InBlock1", "BnsTpCode", 0, request.BnsTpCode);
                _newOrderQueryService.SetFieldData("CSPAT00600InBlock1", "OrdprcPtnCode", 0, request.OrdprcPtnCode);
                _newOrderQueryService.SetFieldData("CSPAT00600InBlock1", "MgntrnCode", 0, request.MgntrnCode);
                _newOrderQueryService.SetFieldData("CSPAT00600InBlock1", "LoanDt", 0, request.LoanDt);
                _newOrderQueryService.SetFieldData("CSPAT00600InBlock1", "OrdCndiTpCode", 0, request.OrdCndiTpCode);
                _newOrderQueryService.SetFieldData("CSPAT00600InBlock1", "MbrNo", 0, request.MbrNo);

                var requestError = Request(_newOrderQueryService, NewOrderTrCode);
                if (requestError is not null)
                {
                    _pendingNewOrderCompletionSource = null;
                    return new CSPAT00600OrderResult(false, requestError, null, null);
                }

                return await WaitForResultAsync(_pendingNewOrderCompletionSource.Task, NewOrderTrCode, "order", cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _pendingNewOrderCompletionSource = null;
                _orderGate.Release();
            }
        }

        public async Task<CSPAT00700OrderResult> ModifyOrderAsync(
            CSPAT00700InBlock1 request,
            CancellationToken cancellationToken = default)
        {
            var validationMessage = ValidateCommon(request.OrdQty, request.OrgOrdNo);
            if (validationMessage is not null)
            {
                return new CSPAT00700OrderResult(false, validationMessage, null, null);
            }

            await _orderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _pendingModifyOrderCompletionSource = new TaskCompletionSource<CSPAT00700OrderResult>(TaskCreationOptions.RunContinuationsAsynchronously);

                _modifyOrderQueryService.ClearBlockdata("CSPAT00700InBlock1");
                _modifyOrderQueryService.SetFieldData("CSPAT00700InBlock1", "OrgOrdNo", 0, FormatLong(request.OrgOrdNo));
                _modifyOrderQueryService.SetFieldData("CSPAT00700InBlock1", "AcntNo", 0, request.AcntNo);
                _modifyOrderQueryService.SetFieldData("CSPAT00700InBlock1", "InptPwd", 0, request.InptPwd);
                _modifyOrderQueryService.SetFieldData("CSPAT00700InBlock1", "IsuNo", 0, request.IsuNo);
                _modifyOrderQueryService.SetFieldData("CSPAT00700InBlock1", "OrdQty", 0, FormatLong(request.OrdQty));
                _modifyOrderQueryService.SetFieldData("CSPAT00700InBlock1", "OrdprcPtnCode", 0, request.OrdprcPtnCode);
                _modifyOrderQueryService.SetFieldData("CSPAT00700InBlock1", "OrdCndiTpCode", 0, request.OrdCndiTpCode);
                _modifyOrderQueryService.SetFieldData("CSPAT00700InBlock1", "OrdPrc", 0, FormatDouble(request.OrdPrc));

                var requestError = Request(_modifyOrderQueryService, ModifyOrderTrCode);
                if (requestError is not null)
                {
                    _pendingModifyOrderCompletionSource = null;
                    return new CSPAT00700OrderResult(false, requestError, null, null);
                }

                return await WaitForResultAsync(_pendingModifyOrderCompletionSource.Task, ModifyOrderTrCode, "modify order", cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _pendingModifyOrderCompletionSource = null;
                _orderGate.Release();
            }
        }

        public async Task<CSPAT00800OrderResult> CancelOrderAsync(
            CSPAT00800InBlock1 request,
            CancellationToken cancellationToken = default)
        {
            var validationMessage = ValidateCommon(request.OrdQty, request.OrgOrdNo);
            if (validationMessage is not null)
            {
                return new CSPAT00800OrderResult(false, validationMessage, null, null);
            }

            await _orderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _pendingCancelOrderCompletionSource = new TaskCompletionSource<CSPAT00800OrderResult>(TaskCreationOptions.RunContinuationsAsynchronously);

                _cancelOrderQueryService.ClearBlockdata("CSPAT00800InBlock1");
                _cancelOrderQueryService.SetFieldData("CSPAT00800InBlock1", "OrgOrdNo", 0, FormatLong(request.OrgOrdNo));
                _cancelOrderQueryService.SetFieldData("CSPAT00800InBlock1", "AcntNo", 0, request.AcntNo);
                _cancelOrderQueryService.SetFieldData("CSPAT00800InBlock1", "InptPwd", 0, request.InptPwd);
                _cancelOrderQueryService.SetFieldData("CSPAT00800InBlock1", "IsuNo", 0, request.IsuNo);
                _cancelOrderQueryService.SetFieldData("CSPAT00800InBlock1", "OrdQty", 0, FormatLong(request.OrdQty));

                var requestError = Request(_cancelOrderQueryService, CancelOrderTrCode);
                if (requestError is not null)
                {
                    _pendingCancelOrderCompletionSource = null;
                    return new CSPAT00800OrderResult(false, requestError, null, null);
                }

                return await WaitForResultAsync(_pendingCancelOrderCompletionSource.Task, CancelOrderTrCode, "cancel order", cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _pendingCancelOrderCompletionSource = null;
                _orderGate.Release();
            }
        }

        private IXAQueryService CreateQueryService(string resFilePath, XA_DATASETLib._IXAQueryEvents_ReceiveDataEventHandler receiveDataHandler)
        {
            var queryService = new XAQueryService();
            if (!queryService.LoadFromResFile(resFilePath))
            {
                _loggingService.Log($"{resFilePath} load failed.");
            }
            else
            {
                queryService.AddReceiveDataEventHandler(receiveDataHandler);
            }

            return queryService;
        }

        private string? ValidateCommon(long ordQty, long? orgOrdNo = null)
        {
            if (!_sessionService.IsConnected)
            {
                return "Login session is not connected.";
            }

            if (orgOrdNo is not null && orgOrdNo <= 0)
            {
                return "Original order number must be greater than zero.";
            }

            if (ordQty <= 0 && orgOrdNo is null)
            {
                return "Order quantity must be greater than zero.";
            }

            if (ordQty <= 0)
            {
                return "Modify/cancel quantity must be greater than zero.";
            }

            return null;
        }

        private string? Request(IXAQueryService queryService, string trCode)
        {
            var requestResult = queryService.Request(false);
            if (requestResult >= 0)
            {
                return null;
            }

            var errorCode = queryService.GetLastError();
            var errorMessage = queryService.GetErrorMessage(errorCode);
            _loggingService.Log($"{trCode} request failed: {errorCode} {errorMessage}");
            return $"Request failed: {errorMessage}";
        }

        private async Task<T> WaitForResultAsync<T>(
            Task<T> task,
            string trCode,
            string orderName,
            CancellationToken cancellationToken)
        {
            try
            {
                return await task.WaitAsync(_defaultTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _loggingService.Log($"{trCode} response timeout.");
                return CreateTimeoutResult<T>($"{orderName} response timed out.");
            }
        }

        private static T CreateTimeoutResult<T>(string message)
        {
            if (typeof(T) == typeof(CSPAT00600OrderResult))
            {
                return (T)(object)new CSPAT00600OrderResult(false, message, null, null);
            }

            if (typeof(T) == typeof(CSPAT00700OrderResult))
            {
                return (T)(object)new CSPAT00700OrderResult(false, message, null, null);
            }

            if (typeof(T) == typeof(CSPAT00800OrderResult))
            {
                return (T)(object)new CSPAT00800OrderResult(false, message, null, null);
            }

            throw new InvalidOperationException($"Unsupported result type: {typeof(T).Name}");
        }

        private void ReceiveNewOrderData(string trcode)
        {
            if (string.Equals(trcode, NewOrderTrCode, StringComparison.OrdinalIgnoreCase))
            {
                _pendingNewOrderCompletionSource?.TrySetResult(MapNewOrderResult());
            }
        }

        private void ReceiveModifyOrderData(string trcode)
        {
            if (string.Equals(trcode, ModifyOrderTrCode, StringComparison.OrdinalIgnoreCase))
            {
                _pendingModifyOrderCompletionSource?.TrySetResult(MapModifyOrderResult());
            }
        }

        private void ReceiveCancelOrderData(string trcode)
        {
            if (string.Equals(trcode, CancelOrderTrCode, StringComparison.OrdinalIgnoreCase))
            {
                _pendingCancelOrderCompletionSource?.TrySetResult(MapCancelOrderResult());
            }
        }

        private CSPAT00600OrderResult MapNewOrderResult()
        {
            var outBlock1 = new CSPAT00600OutBlock1
            {
                RecCnt = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock1", "RecCnt"),
                AcntNo = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "AcntNo"),
                InptPwd = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "InptPwd"),
                IsuNo = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "IsuNo"),
                OrdQty = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock1", "OrdQty"),
                OrdPrc = ReadDouble(_newOrderQueryService, "CSPAT00600OutBlock1", "OrdPrc"),
                BnsTpCode = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "BnsTpCode"),
                OrdprcPtnCode = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "OrdprcPtnCode"),
                PrgmOrdprcPtnCode = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "PrgmOrdprcPtnCode"),
                StslAbleYn = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "StslAbleYn"),
                StslOrdprcTpCode = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "StslOrdprcTpCode"),
                CommdaCode = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "CommdaCode"),
                MgntrnCode = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "MgntrnCode"),
                LoanDt = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "LoanDt"),
                MbrNo = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "MbrNo"),
                OrdCndiTpCode = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "OrdCndiTpCode"),
                StrtgCode = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "StrtgCode"),
                GrpId = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "GrpId"),
                OrdSeqNo = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock1", "OrdSeqNo"),
                PtflNo = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock1", "PtflNo"),
                BskNo = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock1", "BskNo"),
                TrchNo = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock1", "TrchNo"),
                ItemNo = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock1", "ItemNo"),
                OpDrtnNo = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "OpDrtnNo"),
                LpYn = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "LpYn"),
                CvrgTpCode = ReadString(_newOrderQueryService, "CSPAT00600OutBlock1", "CvrgTpCode")
            };

            var outBlock2 = new CSPAT00600OutBlock2
            {
                RecCnt = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock2", "RecCnt"),
                OrdNo = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock2", "OrdNo"),
                OrdTime = ReadString(_newOrderQueryService, "CSPAT00600OutBlock2", "OrdTime"),
                OrdMktCode = ReadString(_newOrderQueryService, "CSPAT00600OutBlock2", "OrdMktCode"),
                OrdPtnCode = ReadString(_newOrderQueryService, "CSPAT00600OutBlock2", "OrdPtnCode"),
                ShtnIsuNo = ReadString(_newOrderQueryService, "CSPAT00600OutBlock2", "ShtnIsuNo"),
                MgempNo = ReadString(_newOrderQueryService, "CSPAT00600OutBlock2", "MgempNo"),
                OrdAmt = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock2", "OrdAmt"),
                SpareOrdNo = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock2", "SpareOrdNo"),
                CvrgSeqno = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock2", "CvrgSeqno"),
                RsvOrdNo = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock2", "RsvOrdNo"),
                SpotOrdQty = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock2", "SpotOrdQty"),
                RuseOrdQty = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock2", "RuseOrdQty"),
                MnyOrdAmt = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock2", "MnyOrdAmt"),
                SubstOrdAmt = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock2", "SubstOrdAmt"),
                RuseOrdAmt = ReadLong(_newOrderQueryService, "CSPAT00600OutBlock2", "RuseOrdAmt"),
                AcntNm = ReadString(_newOrderQueryService, "CSPAT00600OutBlock2", "AcntNm"),
                IsuNm = ReadString(_newOrderQueryService, "CSPAT00600OutBlock2", "IsuNm")
            };

            return CompleteResult(NewOrderTrCode, outBlock2.IsuNm, outBlock2.OrdNo, outBlock1, outBlock2);
        }

        private CSPAT00700OrderResult MapModifyOrderResult()
        {
            var outBlock1 = new CSPAT00700OutBlock1
            {
                RecCnt = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock1", "RecCnt"),
                OrgOrdNo = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock1", "OrgOrdNo"),
                AcntNo = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock1", "AcntNo"),
                InptPwd = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock1", "InptPwd"),
                IsuNo = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock1", "IsuNo"),
                OrdQty = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock1", "OrdQty"),
                OrdprcPtnCode = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock1", "OrdprcPtnCode"),
                OrdCndiTpCode = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock1", "OrdCndiTpCode"),
                OrdPrc = ReadDouble(_modifyOrderQueryService, "CSPAT00700OutBlock1", "OrdPrc"),
                CommdaCode = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock1", "CommdaCode"),
                StrtgCode = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock1", "StrtgCode"),
                GrpId = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock1", "GrpId"),
                OrdSeqNo = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock1", "OrdSeqNo"),
                PtflNo = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock1", "PtflNo"),
                BskNo = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock1", "BskNo"),
                TrchNo = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock1", "TrchNo"),
                ItemNo = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock1", "ItemNo")
            };

            var outBlock2 = new CSPAT00700OutBlock2
            {
                RecCnt = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock2", "RecCnt"),
                OrdNo = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock2", "OrdNo"),
                PrntOrdNo = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock2", "PrntOrdNo"),
                OrdTime = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock2", "OrdTime"),
                OrdMktCode = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock2", "OrdMktCode"),
                OrdPtnCode = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock2", "OrdPtnCode"),
                ShtnIsuNo = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock2", "ShtnIsuNo"),
                PrgmOrdprcPtnCode = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock2", "PrgmOrdprcPtnCode"),
                StslOrdprcTpCode = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock2", "StslOrdprcTpCode"),
                StslAbleYn = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock2", "StslAbleYn"),
                MgntrnCode = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock2", "MgntrnCode"),
                LoanDt = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock2", "LoanDt"),
                CvrgOrdTp = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock2", "CvrgOrdTp"),
                LpYn = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock2", "LpYn"),
                MgempNo = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock2", "MgempNo"),
                OrdAmt = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock2", "OrdAmt"),
                BnsTpCode = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock2", "BnsTpCode"),
                SpareOrdNo = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock2", "SpareOrdNo"),
                CvrgSeqno = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock2", "CvrgSeqno"),
                RsvOrdNo = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock2", "RsvOrdNo"),
                MnyOrdAmt = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock2", "MnyOrdAmt"),
                SubstOrdAmt = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock2", "SubstOrdAmt"),
                RuseOrdAmt = ReadLong(_modifyOrderQueryService, "CSPAT00700OutBlock2", "RuseOrdAmt"),
                AcntNm = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock2", "AcntNm"),
                IsuNm = ReadString(_modifyOrderQueryService, "CSPAT00700OutBlock2", "IsuNm")
            };

            return CompleteResult(ModifyOrderTrCode, outBlock2.IsuNm, outBlock2.OrdNo, outBlock1, outBlock2);
        }

        private CSPAT00800OrderResult MapCancelOrderResult()
        {
            var outBlock1 = new CSPAT00800OutBlock1
            {
                RecCnt = ReadLong(_cancelOrderQueryService, "CSPAT00800OutBlock1", "RecCnt"),
                OrgOrdNo = ReadLong(_cancelOrderQueryService, "CSPAT00800OutBlock1", "OrgOrdNo"),
                AcntNo = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock1", "AcntNo"),
                InptPwd = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock1", "InptPwd"),
                IsuNo = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock1", "IsuNo"),
                OrdQty = ReadLong(_cancelOrderQueryService, "CSPAT00800OutBlock1", "OrdQty"),
                CommdaCode = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock1", "CommdaCode"),
                GrpId = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock1", "GrpId"),
                StrtgCode = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock1", "StrtgCode"),
                OrdSeqNo = ReadLong(_cancelOrderQueryService, "CSPAT00800OutBlock1", "OrdSeqNo"),
                PtflNo = ReadLong(_cancelOrderQueryService, "CSPAT00800OutBlock1", "PtflNo"),
                BskNo = ReadLong(_cancelOrderQueryService, "CSPAT00800OutBlock1", "BskNo"),
                TrchNo = ReadLong(_cancelOrderQueryService, "CSPAT00800OutBlock1", "TrchNo"),
                ItemNo = ReadLong(_cancelOrderQueryService, "CSPAT00800OutBlock1", "ItemNo")
            };

            var outBlock2 = new CSPAT00800OutBlock2
            {
                RecCnt = ReadLong(_cancelOrderQueryService, "CSPAT00800OutBlock2", "RecCnt"),
                OrdNo = ReadLong(_cancelOrderQueryService, "CSPAT00800OutBlock2", "OrdNo"),
                PrntOrdNo = ReadLong(_cancelOrderQueryService, "CSPAT00800OutBlock2", "PrntOrdNo"),
                OrdTime = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock2", "OrdTime"),
                OrdMktCode = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock2", "OrdMktCode"),
                OrdPtnCode = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock2", "OrdPtnCode"),
                ShtnIsuNo = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock2", "ShtnIsuNo"),
                PrgmOrdprcPtnCode = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock2", "PrgmOrdprcPtnCode"),
                StslOrdprcTpCode = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock2", "StslOrdprcTpCode"),
                StslAbleYn = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock2", "StslAbleYn"),
                MgntrnCode = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock2", "MgntrnCode"),
                LoanDt = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock2", "LoanDt"),
                CvrgOrdTp = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock2", "CvrgOrdTp"),
                LpYn = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock2", "LpYn"),
                MgempNo = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock2", "MgempNo"),
                BnsTpCode = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock2", "BnsTpCode"),
                SpareOrdNo = ReadLong(_cancelOrderQueryService, "CSPAT00800OutBlock2", "SpareOrdNo"),
                CvrgSeqno = ReadLong(_cancelOrderQueryService, "CSPAT00800OutBlock2", "CvrgSeqno"),
                RsvOrdNo = ReadLong(_cancelOrderQueryService, "CSPAT00800OutBlock2", "RsvOrdNo"),
                AcntNm = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock2", "AcntNm"),
                IsuNm = ReadString(_cancelOrderQueryService, "CSPAT00800OutBlock2", "IsuNm")
            };

            return CompleteResult(CancelOrderTrCode, outBlock2.IsuNm, outBlock2.OrdNo, outBlock1, outBlock2);
        }

        private CSPAT00600OrderResult CompleteResult(
            string trCode,
            string isuNm,
            long ordNo,
            CSPAT00600OutBlock1 outBlock1,
            CSPAT00600OutBlock2 outBlock2)
        {
            var success = ordNo > 0;
            LogResult(trCode, isuNm, ordNo, success);
            return new CSPAT00600OrderResult(success, CreateResultMessage(success, ordNo), outBlock1, outBlock2);
        }

        private CSPAT00700OrderResult CompleteResult(
            string trCode,
            string isuNm,
            long ordNo,
            CSPAT00700OutBlock1 outBlock1,
            CSPAT00700OutBlock2 outBlock2)
        {
            var success = ordNo > 0;
            LogResult(trCode, isuNm, ordNo, success);
            return new CSPAT00700OrderResult(success, CreateResultMessage(success, ordNo), outBlock1, outBlock2);
        }

        private CSPAT00800OrderResult CompleteResult(
            string trCode,
            string isuNm,
            long ordNo,
            CSPAT00800OutBlock1 outBlock1,
            CSPAT00800OutBlock2 outBlock2)
        {
            var success = ordNo > 0;
            LogResult(trCode, isuNm, ordNo, success);
            return new CSPAT00800OrderResult(success, CreateResultMessage(success, ordNo), outBlock1, outBlock2);
        }

        private void LogResult(string trCode, string isuNm, long ordNo, bool success)
        {
            if (success)
            {
                _loggingService.Log($"{trCode} success: {isuNm} / {ordNo}");
            }
            else
            {
                _loggingService.Log($"{trCode} response received, but order number is empty.");
            }
        }

        private static string CreateResultMessage(bool success, long ordNo)
        {
            return success
                ? $"Order accepted. Order number: {ordNo}"
                : "Response received, but order number is empty.";
        }

        private static string ReadString(IXAQueryService queryService, string blockName, string fieldName)
        {
            return queryService.GetFieldData(blockName, fieldName, 0).Trim();
        }

        private static long ReadLong(IXAQueryService queryService, string blockName, string fieldName)
        {
            var value = ReadString(queryService, blockName, fieldName);
            return long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        }

        private static double ReadDouble(IXAQueryService queryService, string blockName, string fieldName)
        {
            var value = ReadString(queryService, blockName, fieldName);
            return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0d;
        }

        private static string FormatLong(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatDouble(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
