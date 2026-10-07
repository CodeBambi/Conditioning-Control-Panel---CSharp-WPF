using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Discord for the WPF head: the OAuth browser flow, display-name calls and webhook announcements.
    /// The token lifecycle (exchange, refresh, validate/identity, whitelist grant, cache) is Core's
    /// <see cref="DiscordAccount"/>, shared with every head; tokens stay in WPF's own DiscordTokenStorage
    /// files, reached through CoreSecrets.
    /// </summary>
    public class DiscordService : IDisposable
    {
        private readonly DiscordAccount _core = new(() => App.Patreon?.Core);
        private CancellationTokenSource? _oauthCts;
        private bool _disposed;


        /// <summary>Our Discord server's guild id — used to build per-guild avatar CDN URLs.</summary>
        public const string GuildId = DiscordAccount.GuildId;

        public event EventHandler<bool>? AuthenticationChanged;
        public event EventHandler<string>? AuthenticationFailed;

        public string? UserId => _core.UserId;
        public string? Username => _core.Username;
        public string? DisplayName => _core.DisplayName;
        public string? Avatar => _core.Avatar;
        public string? GuildAvatar => _core.GuildAvatar;
        public bool IsStaff => _core.IsStaff;
        public string? StaffRole => _core.StaffRole;
        public bool IsAuthenticated => _core.IsAuthenticated;
        public bool IsVerifying => _core.IsVerifying;
        public string? CustomDisplayName { get => _core.CustomDisplayName; set => _core.CustomDisplayName = value; }
        public string? UnifiedUserId { get => _core.UnifiedUserId; set => _core.UnifiedUserId = value; }
        public bool NeedsRegistration => _core.NeedsRegistration;

        /// <summary>
        /// Whether this is the user's first login (no display name set yet on ANY provider).
        /// If Patreon already has a display name, this returns false to avoid re-prompting.
        /// </summary>
        public bool IsFirstLogin => IsAuthenticated
            && string.IsNullOrEmpty(CustomDisplayName)
            && string.IsNullOrEmpty(App.Patreon?.DisplayName);

        public string? GetAvatarUrl(int size = 128) => _core.GetAvatarUrl(size);

        public Task InitializeAsync() => _core.InitializeAsync();

        public Task ValidateAndRefreshUserAsync(bool forceRefresh = false) => _core.ValidateAndRefreshUserAsync(forceRefresh);

        /// <summary>
        /// Start OAuth2 browser flow
        /// </summary>
        public async Task StartOAuthFlowAsync()
        {
            if (IsVerifying) return;

            try
            {
                _oauthCts = new CancellationTokenSource();

                // Core runs the flow (listener, authorize URL, state/PKCE, exchange, validate). The browser
                // opens through the robust launcher: on total failure it copies the link to the clipboard
                // and prompts the user (ccp-bugs #373/#374/#378/#404); the listener keeps waiting.
                await _core.SignInAsync(url => Helpers.BrowserLauncher.OpenUrlOrPrompt(url, "sign in with Discord"), _oauthCts.Token);

                // Load custom display name from server (for returning users)
                await LoadDisplayNameFromServerAsync();

                App.Logger?.Information("Discord OAuth flow completed successfully");
                AuthenticationChanged?.Invoke(this, true);
            }
            catch (OperationCanceledException)
            {
                App.Logger?.Information("Discord OAuth flow cancelled");
                throw;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Discord OAuth flow failed");
                AuthenticationFailed?.Invoke(this, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Cancel ongoing OAuth flow
        /// </summary>
        public void CancelOAuthFlow()
        {
            _oauthCts?.Cancel();
        }

        /// <summary>The theme id the community bot voices posts with; moved to Core
        /// (<see cref="DiscordAccount.ModThemeId"/>) so both heads share it.</summary>
        private static string GetModThemeId()
        {
            try { return DiscordAccount.ModThemeId(App.Mods?.ActiveModId); }
            catch { return "default"; } // Never throw: this runs inside the fire-and-forget share path.
        }

        /// <summary>
        /// Send achievement announcement to community Discord via server (Core
        /// <see cref="DiscordAccount.SendAchievementWebhookAsync"/>; this head supplies the name, id, token and theme).
        /// </summary>
        public Task<bool> SendAchievementWebhookAsync(Achievement achievement, string? displayName = null) =>
            _core.SendAchievementWebhookAsync(achievement,
                displayName ?? App.Patreon?.DisplayName ?? App.Discord?.DisplayName ?? "Someone",
                App.EffectiveUserId, App.Settings?.Current?.AuthToken, GetModThemeId());

        /// <summary>
        /// Send level up announcement to community Discord via server
        /// </summary>
        public async Task<bool> SendLevelUpWebhookAsync(int level, string? displayName = null)
        {
            try
            {
                var name = displayName ?? App.Patreon?.DisplayName ?? App.Discord?.DisplayName ?? "Someone";

                // Determine image based on level milestone
                var imageName = level switch
                {
                    >= 150 => "PlatinumPuppet.png",
                    >= 125 => "BrainwashedSlavedoll.png",
                    >= 100 => "perfect_plastic_puppet.png",
                    >= 50 => "lv_50.png",
                    >= 20 => "Dumb_Bimbo.png",
                    >= 10 => "lv_10.png",
                    _ => null
                };

                var unifiedId = App.EffectiveUserId;
                if (string.IsNullOrEmpty(unifiedId)) return false;

                var payload = new
                {
                    type = "level_up",
                    display_name = name,
                    unified_id = unifiedId,
                    level = level,
                    image_name = imageName,
                    // Lets the bot theme the post; legacy servers ignore it and keep using image_name.
                    mod_id = GetModThemeId()
                };

                var request = new HttpRequestMessage(HttpMethod.Post, "/discord/community-webhook")
                {
                    Content = JsonContent.Create(payload)
                };
                var authToken = App.Settings?.Current?.AuthToken;
                if (!string.IsNullOrEmpty(authToken))
                    request.Headers.Add("X-Auth-Token", authToken);

                var response = await _core.Http.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    App.Logger?.Information("Level up shared to community: Level {Level} ({Status}, {Bytes} bytes)",
                        level, (int)response.StatusCode, responseText?.Length ?? 0);
                    return true;
                }
                else
                {
                    App.Logger?.Warning("Level up share failed: {Status} (body {Bytes} bytes)",
                        (int)response.StatusCode, responseText?.Length ?? 0);
                    return false;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to share level up to community");
                return false;
            }
        }

        // =============================================================================
        // DISPLAY NAME MANAGEMENT
        // =============================================================================

        /// <summary>
        /// Set the user's custom display name (can only be set once) and save to server
        /// </summary>
        /// <param name="displayName">The display name to set</param>
        /// <param name="claimExisting">If true, claim an existing Patreon name as your own</param>
        /// <returns>Success status, error message, and whether the name can be claimed from Patreon</returns>
        public async Task<(bool Success, string? Error, bool CanClaim)> SetDisplayNameAsync(string displayName, bool claimExisting = false)
        {
            if (!string.IsNullOrEmpty(CustomDisplayName))
            {
                App.Logger?.Warning("Attempted to change display name, but it's already set");
                return (false, "Display name is already set", false);
            }

            var trimmedName = displayName.Trim();

            // Save to server (with claim flag if claiming)
            var saveResult = await SaveDisplayNameToServerAsync(trimmedName, claimExisting);
            if (!saveResult.Success)
            {
                return (false, saveResult.Error ?? "Failed to save display name", saveResult.CanClaim);
            }

            CustomDisplayName = trimmedName;

            // Update the cached state with the new display name
            var cachedState = _core.RetrieveCachedState();
            if (cachedState != null)
            {
                cachedState.CustomDisplayName = CustomDisplayName;
                _core.StoreCachedState(cachedState);
            }

            App.Logger?.Information("Custom display name set ({Chars} chars, claimed: {Claimed})", CustomDisplayName?.Length ?? 0, claimExisting);
            return (true, null, false);
        }

        /// <summary>
        /// Check if a display name is available (not already taken)
        /// </summary>
        public async Task<(bool Available, string? Error, bool CanClaim)> CheckDisplayNameAvailableAsync(string displayName)
        {
            try
            {
                var tokens = _core.RetrieveTokens();
                if (tokens == null)
                {
                    return (true, null, false); // Can't check, allow optimistically
                }

                _core.Http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

                var response = await _core.Http.GetAsync($"/user/check-display-name-discord?name={Uri.EscapeDataString(displayName)}");

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<DisplayNameCheckResult>();
                    return (result?.Available ?? true, result?.Error, result?.CanClaim ?? false);
                }

                // If endpoint doesn't exist or errors, allow optimistically
                return (true, null, false);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to check display name availability");
                return (true, null, false); // Allow optimistically on error
            }
        }

        private class DisplayNameCheckResult
        {
            public bool Available { get; set; }
            public string? Error { get; set; }
            public bool CanClaim { get; set; }
        }

        private class SetDisplayNameResult
        {
            public bool Success { get; set; }
            public string? Error { get; set; }
            public bool CanClaim { get; set; }
            public string? DisplayName { get; set; }
        }

        /// <summary>
        /// Save display name to the server for cross-device sync
        /// </summary>
        private async Task<(bool Success, string? Error, bool CanClaim)> SaveDisplayNameToServerAsync(string displayName, bool claimExisting = false)
        {
            try
            {
                var tokens = _core.RetrieveTokens();
                if (tokens == null)
                {
                    App.Logger?.Warning("Cannot save display name: no tokens available");
                    return (false, "Not authenticated", false);
                }

                _core.Http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

                var response = await _core.Http.PostAsJsonAsync("/user/set-display-name-discord", new
                {
                    display_name = displayName,
                    claim_existing = claimExisting
                });

                if (response.IsSuccessStatusCode)
                {
                    App.Logger?.Information("Display name saved to server successfully");
                    return (true, null, false);
                }

                // Parse error response to check if name can be claimed
                try
                {
                    var errorResult = await response.Content.ReadFromJsonAsync<SetDisplayNameResult>();
                    return (false, errorResult?.Error ?? "Name is taken", errorResult?.CanClaim ?? false);
                }
                catch
                {
                    return (false, "Failed to save display name", false);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to save display name to server");
                return (false, ex.Message, false);
            }
        }

        /// <summary>
        /// Load custom display name from server (called after successful auth).
        /// Falls back to Patreon's display name if Discord doesn't have one but Patreon does.
        /// </summary>
        public async Task LoadDisplayNameFromServerAsync()
        {
            try
            {
                var tokens = _core.RetrieveTokens();
                if (tokens == null) return;

                _core.Http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

                var response = await _core.Http.GetAsync("/user/profile-discord");
                if (response.IsSuccessStatusCode)
                {
                    var profile = await response.Content.ReadFromJsonAsync<DiscordUserProfile>();
                    if (!string.IsNullOrEmpty(profile?.DisplayName))
                    {
                        CustomDisplayName = profile.DisplayName;
                        var cachedState = _core.RetrieveCachedState();
                        if (cachedState != null)
                        {
                            cachedState.CustomDisplayName = CustomDisplayName;
                            _core.StoreCachedState(cachedState);
                        }
                        App.Logger?.Information("Loaded display name from server ({Chars} chars)", CustomDisplayName?.Length ?? 0);
                        return;
                    }
                }

                // If no display name from Discord server, check if Patreon already has one
                // This handles the case where user set their name via Patreon first
                if (string.IsNullOrEmpty(CustomDisplayName) && !string.IsNullOrEmpty(App.Patreon?.DisplayName))
                {
                    CustomDisplayName = App.Patreon.DisplayName;
                    var cachedState = _core.RetrieveCachedState();
                    if (cachedState != null)
                    {
                        cachedState.CustomDisplayName = CustomDisplayName;
                        _core.StoreCachedState(cachedState);
                    }
                    App.Logger?.Information("Adopted display name from Patreon ({Chars} chars)", CustomDisplayName?.Length ?? 0);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to load display name from server");
            }
        }

        private class DiscordUserProfile
        {
            public string? DisplayName { get; set; }
        }

        /// <summary>
        /// Logout and clear all stored data
        /// </summary>
        public void Logout()
        {
            _core.Logout();
            AuthenticationChanged?.Invoke(this, false);
        }

        /// <summary>
        /// Get access token for API calls
        /// </summary>
        public string? GetAccessToken() => _core.GetAccessToken();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _oauthCts?.Cancel();
            _oauthCts?.Dispose();
            _core.Dispose();
        }
    }
}
