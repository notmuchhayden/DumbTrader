using System.Text.Json.Serialization;

namespace DumbTrader.Models
{
    public class AccountModel
    {
        public string Id { get; set; } = string.Empty;

        // Password kept in memory only; do not serialize plain password
        [JsonIgnore]
        public string? Password { get; set; }

        // Encrypted password stored for persistence (Base64)
        public string? EncryptedPasswordBase64 { get; set; }

        // Support multiple account numbers
        public string[] AccountNumbers { get; set; } = new string[0];
    }
}
