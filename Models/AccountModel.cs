using System.Text.Json.Serialization;

namespace DumbTrader.Models
{
 public class AccountModel
 {
 public string AccountNumber { get; set; } = string.Empty;
 public string AccountId { get; set; } = string.Empty;

 // Password kept in memory only; do not serialize plain password
 [JsonIgnore]
 public string? Password { get; set; }

 // Encrypted password stored for persistence (Base64)
 public string? EncryptedPasswordBase64 { get; set; }
 }
}
