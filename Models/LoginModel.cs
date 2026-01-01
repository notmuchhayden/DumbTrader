using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DumbTrader.Models
{
    public class LoginModel
    {
        // User ID
        public string Id { get; set; } = string.Empty;

        // Password kept in memory only; do not serialize plain password
        [JsonIgnore]
        public string? Password { get; set; }

        // Encrypted password stored for persistence (Base64)
        public string? EncryptedPasswordBase64 { get; set; }
    }
}
