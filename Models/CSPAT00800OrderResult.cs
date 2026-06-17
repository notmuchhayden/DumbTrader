namespace DumbTrader.Models
{
    public sealed record CSPAT00800OrderResult(
        bool Success,
        string Message,
        CSPAT00800OutBlock1? OutBlock1,
        CSPAT00800OutBlock2? OutBlock2);
}
