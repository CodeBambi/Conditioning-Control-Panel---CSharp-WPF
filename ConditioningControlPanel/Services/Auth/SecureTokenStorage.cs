using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Securely stores OAuth provider tokens using Windows DPAPI encryption.
    /// Defaults to the Patreon key prefix for backward compatibility; pass a
    /// different prefix (e.g. "substar") to get a separate, isolated token store.
    /// </summary>
    public class SecureTokenStorage
    {
        private readonly string _storagePath;
        private readonly string _cachePath;
        private readonly byte[] _entropy;
        private readonly string _label;

        /// <param name="keyPrefix">
        /// Per-provider key prefix. "patreon" (default) yields patreon_auth.dat /
        /// patreon_cache.dat with entropy "ConditioningControlPanel_Patreon_v1" —
        /// identical to the original behavior. "substar" yields substar_*.dat etc.
        /// </param>
        public SecureTokenStorage(string keyPrefix = "patreon")
        {
            var prefix = string.IsNullOrWhiteSpace(keyPrefix) ? "patreon" : keyPrefix.Trim().ToLowerInvariant();
            _label = char.ToUpperInvariant(prefix[0]) + prefix.Substring(1); // "patreon" -> "Patreon"
            _entropy = Encoding.UTF8.GetBytes($"ConditioningControlPanel_{_label}_v1");

            var storageDir = App.UserDataPath;

            // Ensure directory exists
            if (!Directory.Exists(storageDir))
            {
                Directory.CreateDirectory(storageDir);
            }

            _storagePath = Path.Combine(storageDir, $"{prefix}_auth.dat");
            _cachePath = Path.Combine(storageDir, $"{prefix}_cache.dat");
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

                App.Logger?.Information("{Provider} tokens stored securely", _label);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to store {Provider} tokens", _label);
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
                App.Logger?.Warning(ex, "Failed to decrypt {Provider} tokens - may be corrupted or from different user", _label);
                ClearTokens();
                return null;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to retrieve {Provider} tokens", _label);
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

                App.Logger?.Information("{Provider} tokens cleared", _label);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to clear {Provider} tokens", _label);
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
                App.Logger?.Error(ex, "Failed to store {Provider} cache", _label);
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
                App.Logger?.Warning(ex, "Failed to retrieve {Provider} cache", _label);
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
                App.Logger?.Warning(ex, "Failed to clear {Provider} cache", _label);
            }
        }
    }
}
