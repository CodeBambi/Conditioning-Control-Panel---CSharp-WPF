using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// DPAPI-encrypted storage for the V2 API auth token.
    /// Replaces plaintext storage in settings.json.
    /// </summary>
    public static class SecureAuthTokenStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ConditioningControlPanel_AuthToken_v1");
        private static readonly string StoragePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel",
            "auth_token.dat");

        private static string? _cached;
        private static bool _loaded;

        /// <summary>
        /// Store auth token encrypted with DPAPI.
        /// </summary>
        public static void Store(string? token)
        {
            _cached = token;
            _loaded = true;

            try
            {
                if (string.IsNullOrEmpty(token))
                {
                    Clear();
                    return;
                }

                var plainBytes = Encoding.UTF8.GetBytes(token);
                var encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(StoragePath, encryptedBytes);
                SecurityHelper.SecureClear(plainBytes);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to store auth token securely");
            }
        }

        /// <summary>
        /// Retrieve and decrypt the stored auth token.
        /// </summary>
        public static string? Retrieve()
        {
            if (_loaded) return _cached;

            try
            {
                if (!File.Exists(StoragePath))
                {
                    _loaded = true;
                    return null;
                }

                var encryptedBytes = File.ReadAllBytes(StoragePath);
                var plainBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
                _cached = Encoding.UTF8.GetString(plainBytes);
                SecurityHelper.SecureClear(plainBytes);
                _loaded = true;
                return _cached;
            }
            catch (CryptographicException ex)
            {
                App.Logger?.Warning(ex, "Failed to decrypt auth token - may be corrupted or from different user");
                Clear();
                return null;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to retrieve auth token");
                _loaded = true;
                return null;
            }
        }

        /// <summary>
        /// Clear the in-memory cached token (call on app exit to reduce memory exposure).
        /// Does NOT delete the on-disk encrypted file.
        /// </summary>
        public static void ClearMemoryCache()
        {
            _cached = null;
            _loaded = false;
        }

        /// <summary>
        /// Securely delete the stored auth token.
        /// </summary>
        public static void Clear()
        {
            _cached = null;
            _loaded = true;

            try
            {
                if (File.Exists(StoragePath))
                {
                    // Overwrite with random data before deletion
                    var fileInfo = new FileInfo(StoragePath);
                    var randomBytes = new byte[fileInfo.Length];
                    using (var rng = RandomNumberGenerator.Create())
                    {
                        rng.GetBytes(randomBytes);
                    }
                    File.WriteAllBytes(StoragePath, randomBytes);
                    File.Delete(StoragePath);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to clear auth token file");
            }
        }
    }
}
