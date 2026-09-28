using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Securely stores Discord tokens using Windows DPAPI encryption
    /// </summary>
    public class DiscordTokenStorage
    {
        private readonly string _storagePath;
        private readonly string _cachePath;
        private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("ConditioningControlPanel_Discord_v1");

        public DiscordTokenStorage()
        {
            var storageDir = App.UserDataPath;

            // Ensure directory exists
            if (!Directory.Exists(storageDir))
            {
                Directory.CreateDirectory(storageDir);
            }

            _storagePath = Path.Combine(storageDir, "discord_auth.dat");
            _cachePath = Path.Combine(storageDir, "discord_cache.dat");
        }

        private static readonly Lazy<DiscordTokenStorage> Shared = new(() => new DiscordTokenStorage());

        /// <summary>
        /// Core DiscordAccount's CoreSecrets names (<c>discord_auth</c> / <c>discord_cache</c>, JSON of the
        /// same models) mapped onto this store, so files, entropy and bytes on disk are unchanged.
        /// False for any other name. Null clears.
        /// </summary>
        public static bool TryReadSecret(string name, out string? json)
        {
            object? value = name switch
            {
                "discord_auth" => Shared.Value.RetrieveTokens(),
                "discord_cache" => Shared.Value.RetrieveCachedState(),
                _ => null,
            };
            json = value == null ? null : JsonConvert.SerializeObject(value);
            return name is "discord_auth" or "discord_cache";
        }

        public static bool TryWriteSecret(string name, string? json)
        {
            if (name == "discord_cache")
            {
                if (json == null) Shared.Value.ClearCachedState();
                else Shared.Value.StoreCachedState(JsonConvert.DeserializeObject<DiscordCachedState>(json)!);
                return true;
            }
            if (name != "discord_auth") return false;
            if (json == null) Shared.Value.ClearTokens();
            else if (JsonConvert.DeserializeObject<DiscordTokenData>(json) is { } t) Shared.Value.StoreTokens(t.AccessToken, t.RefreshToken, t.ExpiresAt);
            return true;
        }

        /// <summary>
        /// Store tokens securely using DPAPI
        /// </summary>
        public void StoreTokens(string accessToken, string refreshToken, DateTime expiresAt)
        {
            try
            {
                var tokenData = new DiscordTokenData
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

                App.Logger?.Information("Discord tokens stored securely");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to store Discord tokens");
                throw;
            }
        }

        /// <summary>
        /// Retrieve and decrypt stored tokens
        /// </summary>
        public DiscordTokenData? RetrieveTokens()
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

                return JsonConvert.DeserializeObject<DiscordTokenData>(json);
            }
            catch (CryptographicException ex)
            {
                // Token was encrypted by different user or corrupted
                App.Logger?.Warning(ex, "Failed to decrypt Discord tokens - may be corrupted or from different user");
                ClearTokens();
                return null;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to retrieve Discord tokens");
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

                App.Logger?.Information("Discord tokens cleared");
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to clear Discord tokens");
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
        /// Store cached user state
        /// </summary>
        public void StoreCachedState(DiscordCachedState state)
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
                App.Logger?.Error(ex, "Failed to store Discord cache");
            }
        }

        /// <summary>
        /// Retrieve cached user state
        /// </summary>
        public DiscordCachedState? RetrieveCachedState()
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

                return JsonConvert.DeserializeObject<DiscordCachedState>(json);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to retrieve Discord cache");
                return null;
            }
        }

        /// <summary>
        /// Clear cached user state
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
                App.Logger?.Warning(ex, "Failed to clear Discord cache");
            }
        }
    }
}
