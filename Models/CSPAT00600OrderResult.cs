namespace DumbTrader.Models
{
    public sealed record CSPAT00600OrderResult(
        bool Success,
        string Message,
        CSPAT00600OutBlock1? OutBlock1,
        CSPAT00600OutBlock2? OutBlock2);
}
