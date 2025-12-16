using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DumbTrader.Models;

namespace DumbTrader.Services
{
    public class AccountStoreService
    {
        private readonly string _filePath;

        public AccountStoreService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "DumbTrader");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "accounts.json");
        }

        public void SaveAccount(AccountModel account)
        {
            if (account.Password != null)
            {
                account.EncryptedPasswordBase64 = Protect(account.Password);
                account.Password = null; // clear plaintext from memory reference
            }

            var json = JsonSerializer.Serialize(account, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json, Encoding.UTF8);
        }

        public AccountModel? LoadAccount()
        {
            if (!File.Exists(_filePath))
                return null;
            var json = File.ReadAllText(_filePath, Encoding.UTF8);
            var account = JsonSerializer.Deserialize<AccountModel>(json);
            if (account?.EncryptedPasswordBase64 != null)
            {
                try
                {
                    account.Password = Unprotect(account.EncryptedPasswordBase64);
                }
                catch
                {
                    // if decryption fails, ignore and leave Password null
                    account.Password = null;
                }
            }
            return account;
        }

        public void DeleteAccount()
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
