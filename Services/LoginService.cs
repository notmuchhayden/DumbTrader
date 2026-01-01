using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DumbTrader.Models;

namespace DumbTrader.Services
{
    public class LoginService
    {
        private readonly string _filePath;

        public LoginService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "DumbTrader");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "login.json");
        }

        public void SaveLogin(LoginModel login)
        {
            if (login.Password != null)
            {
                login.EncryptedPasswordBase64 = Protect(login.Password);
                login.Password = null;
            }
            var json = JsonSerializer.Serialize(login, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json, Encoding.UTF8);
        }

        public LoginModel? LoadLogin()
        {
            if (!File.Exists(_filePath))
                return null;
            var json = File.ReadAllText(_filePath, Encoding.UTF8);
            var login = JsonSerializer.Deserialize<LoginModel>(json);
            if (login?.EncryptedPasswordBase64 != null)
            {
                try
                {
                    login.Password = Unprotect(login.EncryptedPasswordBase64);
                }
                catch
                {
                    login.Password = null;
                }
            }
            return login;
        }

        public void DeleteLogin()
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }

        private static string Protect(string plain)
        {
            var bytes = Encoding.UTF8.GetBytes(plain);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        private static string Unprotect(string base64)
        {
            var encrypted = Convert.FromBase64String(base64);
            var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
