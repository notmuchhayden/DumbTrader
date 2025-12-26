using System.Text.Json.Serialization;

namespace DumbTrader.Models
{
    public class AccountInfo
    {
        // Account number (string to preserve formatting / leading zeros)
        public string AccountNumber { get; set; } = string.Empty;
        // Account name (e.g., institution or account type)
        public string AccountName { get; set; } = string.Empty;
        // Detailed account name (e.g., specific account description)
        public string AccountDetailName { get; set; } = string.Empty;
        // Account nickname (user-defined or system-assigned)
        public string AccountNickname { get; set; } = string.Empty;
    }
}
