using System;
using System.Text.Json.Serialization;

namespace DumbTrader.Models
{
    public class AccountModel
    {
        // User ID
        public string Id { get; set; } = string.Empty;

        // Password kept in memory only; do not serialize plain password
        [JsonIgnore]
        public string? Password { get; set; }

        // Encrypted password stored for persistence (Base64)
        public string? EncryptedPasswordBase64 { get; set; }

        // Support multiple account entries
        public AccountInfo[] Accounts { get; set; } = Array.Empty<AccountInfo>();
    }
}
