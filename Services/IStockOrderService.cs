using DumbTrader.Models;

namespace DumbTrader.Services
{
    public interface IStockOrderService
    {
        Task<CSPAT00600OrderResult> PlaceOrderAsync(
            CSPAT00600InBlock1 request,
            CancellationToken cancellationToken = default);

        Task<CSPAT00700OrderResult> ModifyOrderAsync(
            CSPAT00700InBlock1 request,
            CancellationToken cancellationToken = default);

        Task<CSPAT00800OrderResult> CancelOrderAsync(
            CSPAT00800InBlock1 request,
            CancellationToken cancellationToken = default);

        Task<CSPAT00600OrderResult> BuyAsync(
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
            CancellationToken cancellationToken = default);

        Task<CSPAT00600OrderResult> SellAsync(
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
            CancellationToken cancellationToken = default);

        Task<CSPAT00700OrderResult> ModifyAsync(
            long orgOrdNo,
            string acntNo,
            string inptPwd,
            string isuNo,
            long ordQty,
            double ordPrc,
            string ordprcPtnCode = "00",
            string ordCndiTpCode = "0",
            CancellationToken cancellationToken = default);

        Task<CSPAT00800OrderResult> CancelAsync(
            long orgOrdNo,
            string acntNo,
            string inptPwd,
            string isuNo,
            long ordQty,
            CancellationToken cancellationToken = default);
    }
}
