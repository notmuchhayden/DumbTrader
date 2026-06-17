namespace DumbTrader.Models
{
    public sealed record CSPAT00700OrderResult(
        bool Success,
        string Message,
        CSPAT00700OutBlock1? OutBlock1,
        CSPAT00700OutBlock2? OutBlock2);
}
