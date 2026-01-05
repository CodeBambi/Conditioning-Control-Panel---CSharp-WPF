using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Securely stores Patreon tokens using Windows DPAPI encryption
    /// </summary>
    public class SecureTokenStorage
    {
        private readonly string _storagePath;
        private readonly string _cachePath;
        private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("ConditioningControlPanel_Patreon_v1");

        public SecureTokenStorage()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var storageDir = Path.Combine(appData, "ConditioningControlPanel");

            // Ensure directory exists
            if (!Directory.Exists(storageDir))
            {
                Directory.CreateDirectory(storageDir);
            }

            _storagePath = Path.Combine(storageDir, "patreon_auth.dat");
            _cachePath = Path.Combine(storageDir, "patreon_cache.dat");
        }

        /// <summary>
        /// Store tokens securely using DPAPI
        /// </summary>
        public void StoreTokens(string accessToken, string refreshToken, DateTime expiresAt)
        {
            try
            {
                var tokenData = new PatreonTokenData
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    ExpiresAt = expiresAt
                };

                var json = JsonConvert.SerializeObject(tokenData);
                var plainBytes = Encoding.UTF8.GetBytes(json);

                // Encrypt with DPAPI (current user scope)
                var encryptedBytes = ProtectedData.Protect(
                    plainBytes,
                    _entropy,
                    DataProtectionScope.CurrentUser);

                // Write to file
                File.WriteAllBytes(_storagePath, encryptedBytes);

                // Clear sensitive data from memory
                SecurityHelper.SecureClear(plainBytes);

                App.Logger?.Information("Patreon tokens stored securely");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to store Patreon tokens");
                throw;
            }
        }

        /// <summary>
        /// Retrieve and decrypt stored tokens
        /// </summary>
        public PatreonTokenData? RetrieveTokens()
        {
            try
            {
                if (!File.Exists(_storagePath))
                {
                    return null;
                }

                var encryptedBytes = File.ReadAllBytes(_storagePath);

                // Decrypt with DPAPI
                var plainBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    _entropy,
                    DataProtectionScope.CurrentUser);

                var json = Encoding.UTF8.GetString(plainBytes);

                // Clear decrypted bytes from memory
                SecurityHelper.SecureClear(plainBytes);

                return JsonConvert.DeserializeObject<PatreonTokenData>(json);
            }
            catch (CryptographicException ex)
            {
                // Token was encrypted by different user or corrupted
                App.Logger?.Warning(ex, "Failed to decrypt Patreon tokens - may be corrupted or from different user");
                ClearTokens();
                return null;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to retrieve Patreon tokens");
                return null;
            }
        }

        /// <summary>
        /// Clear all stored tokens (logout)
        /// </summary>
        public void ClearTokens()
        {
            try
            {
                if (File.Exists(_storagePath))
                {
                    // Overwrite with random data before deletion for extra security
                    var fileInfo = new FileInfo(_storagePath);
                    var randomBytes = new byte[fileInfo.Length];
                    using (var rng = RandomNumberGenerator.Create())
                    {
                        rng.GetBytes(randomBytes);
                    }
                    File.WriteAllBytes(_storagePath, randomBytes);
                    File.Delete(_storagePath);
                }

                ClearCachedState();

                App.Logger?.Information("Patreon tokens cleared");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to clear Patreon tokens");
            }
        }

        /// <summary>
        /// Check if valid tokens exist
        /// </summary>
        public bool HasValidTokens()
        {
            var tokens = RetrieveTokens();
            return tokens != null && !string.IsNullOrEmpty(tokens.AccessToken);
        }

        /// <summary>
        /// Store cached subscription state
        /// </summary>
        public void StoreCachedState(PatreonCachedState state)
        {
            try
            {
                var json = JsonConvert.SerializeObject(state);
                var plainBytes = Encoding.UTF8.GetBytes(json);

                var encryptedBytes = ProtectedData.Protect(
                    plainBytes,
                    _entropy,
                    DataProtectionScope.CurrentUser);

                File.WriteAllBytes(_cachePath, encryptedBytes);
                SecurityHelper.SecureClear(plainBytes);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to store Patreon cache");
            }
        }

        /// <summary>
        /// Retrieve cached subscription state
        /// </summary>
        public PatreonCachedState? RetrieveCachedState()
        {
            try
            {
                if (!File.Exists(_cachePath))
                {
                    return null;
                }

                var encryptedBytes = File.ReadAllBytes(_cachePath);
                var plainBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    _entropy,
                    DataProtectionScope.CurrentUser);

                var json = Encoding.UTF8.GetString(plainBytes);
                SecurityHelper.SecureClear(plainBytes);

                return JsonConvert.DeserializeObject<PatreonCachedState>(json);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to retrieve Patreon cache");
                return null;
            }
        }

        /// <summary>
        /// Clear cached subscription state
        /// </summary>
        public void ClearCachedState()
        {
            try
            {
                if (File.Exists(_cachePath))
                {
                    File.Delete(_cachePath);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to clear Patreon cache");
            }
        }
    }
}
