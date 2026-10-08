using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Discord's token lifecycle every head shares (exchange, refresh, validate/identity, whitelist grant,
    /// 24h cache), moved from WPF DiscordService, which delegates. Tokens/cache go through
    /// <see cref="CoreSecrets"/> as <c>discord_auth</c>/<c>discord_cache</c> (WPF's DiscordTokenStorage JSON).
    /// No store: no tokens, nothing granted. The browser flow, display-name calls and webhooks stay with the caller.
    /// </summary>
    public sealed class DiscordAccount : IDisposable
    {
        /// <summary>Our Discord server's guild id, for per-guild avatar CDN URLs.</summary>
        public const string GuildId = "1456573221489999934";
        private const int CacheHours = 24;

        private readonly Func<AppSettings?> _settings;

        private readonly Func<ProviderSubscription?>? _whitelistPeer;

        /// <summary>The proxy client. The head's own proxy calls (display name, webhooks) share it.</summary>
        public HttpClient Http { get; }

        public string? UserId { get; private set; }
        public string? Username { get; private set; }
        public string? DisplayName { get; private set; }
        public string? Avatar { get; private set; }
        public string? GuildAvatar { get; private set; }
        public bool IsStaff { get; private set; }
        public string? StaffRole { get; private set; }
        public bool IsVerifying { get; set; }
        public string? CustomDisplayName { get; set; }
        public string? UnifiedUserId { get; set; }
        public bool NeedsRegistration { get; private set; }

        /// <param name="whitelistPeer">A whitelisted Discord user is promoted there (WPF: App.Patreon); a constructor
        /// argument because the cache load below already applies it. <paramref name="settings"/> defaults to CoreSettings;
        /// <paramref name="handler"/> is for tests.</param>
        public DiscordAccount(Func<ProviderSubscription?>? whitelistPeer = null, Func<AppSettings?>? settings = null, HttpMessageHandler? handler = null)
        {
            _whitelistPeer = whitelistPeer;
            _settings = settings ?? (() => CoreSettings.HasProvider ? CoreSettings.Service?.Current : null);
            Http = handler == null ? new HttpClient() : new HttpClient(handler);
            Http.BaseAddress = new Uri(ProviderSubscription.ProxyBaseUrl);
            Http.Timeout = TimeSpan.FromSeconds(30);
            Http.DefaultRequestHeaders.Add("X-Client-Version", CoreReleaseContent.AppVersion);
            Http.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{CoreReleaseContent.AppVersion}");
            LoadCachedState();
        }

        /// <summary>Guild avatar first, then the global one; null without a user or avatar.</summary>
        public string? GetAvatarUrl(int size = 128)
        {
            if (string.IsNullOrEmpty(UserId)) return null;
            if (!string.IsNullOrEmpty(GuildAvatar))
                return $"https://cdn.discordapp.com/guilds/{GuildId}/users/{UserId}/avatars/{GuildAvatar}.{(GuildAvatar.StartsWith("a_") ? "gif" : "png")}?size={size}";
            if (string.IsNullOrEmpty(Avatar)) return null;
            return $"https://cdn.discordapp.com/avatars/{UserId}/{Avatar}.{(Avatar.StartsWith("a_") ? "gif" : "png")}?size={size}";
        }

        /// <summary>Startup: validate online, or settle from cache in offline mode. Never throws.</summary>
        public async Task InitializeAsync()
        {
            try
            {
                if (_settings()?.OfflineMode == true)
                {
                    Log.Information("Offline mode enabled, using cached Discord state only");
                    LoadCachedState();
                    return;
                }
                if (IsAuthenticated) await ValidateAndRefreshUserAsync();
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to validate Discord session on startup"); }
        }

        /// <summary>Trades the code the loopback caught for tokens and stores them. Throws on failure,
        /// including a store that could not save them.</summary>
        public async Task ExchangeCodeAsync(string code, string redirectUri)
        {
            var t = await LoopbackOAuth.ExchangeAsync(Http, "/discord/token", new { code, redirect_uri = redirectUri });
            StoreTokens(t.AccessToken, t.RefreshToken, DateTime.UtcNow.AddSeconds(t.ExpiresIn));
            Log.Information("Discord tokens stored successfully");
        }

        /// <summary>The OAuth sign-in (WPF DiscordService.StartOAuthFlowAsync): browser flow, exchange, forced
        /// validate. No-op while one is running; throws as WPF did.</summary>
        public async Task SignInAsync(Action<string> openBrowser, CancellationToken ct = default)
        {
            if (IsVerifying) return;
            try
            {
                IsVerifying = true;
                var cb = await LoopbackOAuth.SignInAsync("discord", openBrowser, ct);
                await ExchangeCodeAsync(cb.Code, cb.CallbackUrl);
                await ValidateAndRefreshUserAsync(forceRefresh: true);
            }
            finally { IsVerifying = false; }
        }

        /// <summary>Validate the user, refreshing if needed. Never throws: a failure leaves what was there.</summary>
        public async Task ValidateAndRefreshUserAsync(bool forceRefresh = false)
        {
            if (IsVerifying && !forceRefresh) return;
            var settings = _settings();
            if (settings?.OfflineMode == true) { Log.Debug("Offline mode enabled, skipping Discord validation"); return; }

            try
            {
                if (!forceRefresh && RetrieveCachedState() is { IsExpired: false } cached) { UpdateUserInfo(cached); return; }

                var tokens = RetrieveTokens();
                if (tokens == null) { ClearUserInfo(); return; }
                if (tokens.IsExpired)
                {
                    if (!await RefreshTokensAsync(tokens.RefreshToken) || (tokens = RetrieveTokens()) == null) { ClearUserInfo(); return; }
                }

                IsVerifying = true;

                // Per-request headers; X-Auth-Token lets the server heal a divergent token (BUG-7DCJHDP3JZ).
                using var request = new HttpRequestMessage(HttpMethod.Get, "/discord/validate");
                var prizesFor = settings?.UnifiedId;
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
                if (!string.IsNullOrEmpty(settings?.AuthToken)) request.Headers.Add("X-Auth-Token", settings.AuthToken);

                var response = await Http.SendAsync(request);

                // Contract D: a merge tombstone; the swap re-signs in on the canonical.
                if (V2AuthService.MergedRecovery is { } merged && await merged(response, null)) return;

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    if (await RefreshTokensAsync(tokens.RefreshToken)) { await ValidateAndRefreshUserAsync(forceRefresh: true); return; }
                    ClearTokens();
                    ClearUserInfo();
                    return;
                }

                if (!response.IsSuccessStatusCode) { Log.Warning("Discord validation failed with status {Status}", response.StatusCode); return; }

                var user = await response.Content.ReadFromJsonAsync<DiscordUserResponse>();
                if (user == null || !string.IsNullOrEmpty(user.Error)) { Log.Warning("Discord validation error: {Error}", user?.Error); return; }

                ProviderSubscription.PrizesSink?.Invoke(prizesFor, user.UnifiedId, user.Prizes, "Discord validate");

                // Server-healed auth token, refused for a DIFFERENT unified record: adopting it would 401 every
                // unified_id request and flip-flop with the other provider (duplicate-identity accounts).
                if (!string.IsNullOrEmpty(user.AuthToken) && settings != null)
                {
                    var local = settings.UnifiedId;
                    if (!string.IsNullOrEmpty(local) && !string.IsNullOrEmpty(user.UnifiedId)
                        && !string.Equals(user.UnifiedId, local, StringComparison.Ordinal))
                    {
                        Log.Warning("[Auth] Discord validate resolved to unified account {ServerId} but this session is {LocalId} - " +
                            "REFUSING the re-issued auth token (it belongs to the other record). " +
                            "This account pair likely needs a server-side merge.", user.UnifiedId, local);
                    }
                    else
                    {
                        settings.AuthToken = user.AuthToken;
                        CoreSettings.Save();
                        Log.Information("[Auth] Stored re-issued auth token from Discord validate (token recovery)");
                    }
                }

                UserId = user.Id;
                Username = user.Username;
                DisplayName = user.DisplayName;
                Avatar = user.Avatar;
                GuildAvatar = user.GuildAvatar;
                IsStaff = user.IsStaff;
                StaffRole = user.StaffRole;
                NeedsRegistration = user.NeedsRegistration;

                if (user.IsWhitelisted)
                    Log.Information("Discord validate: whitelisted user - granting premium access (server tier {Tier})", user.PatreonTier);
                ApplyWhitelistAccess(user.IsWhitelisted);

                StoreCachedState(new DiscordCachedState
                {
                    UserId = user.Id,
                    Username = user.Username,
                    GlobalName = user.GlobalName,
                    Avatar = user.Avatar,
                    GuildAvatar = user.GuildAvatar,
                    IsStaff = user.IsStaff,
                    StaffRole = user.StaffRole,
                    IsWhitelisted = user.IsWhitelisted,
                    LastVerified = DateTime.UtcNow,
                    CacheExpiresAt = DateTime.UtcNow.AddHours(CacheHours)
                });

                Log.Information("Discord user validated: {Id}, NeedsRegistration={NeedsReg}, Whitelisted={Whitelisted}", UserId, user.NeedsRegistration, user.IsWhitelisted);
            }
            catch (Exception ex) { Log.Error(ex, "Failed to validate Discord user"); }
            finally { IsVerifying = false; }
        }

        private async Task<bool> RefreshTokensAsync(string refreshToken)
        {
            // A grant Discord already refused this session is not asked about again (release/6.11.5,
            // WPF DiscordService.RefreshTokensAsync): the proxy answers the same invalid_grant every time.
            if (DeadRefreshTokens.IsDead(refreshToken))
            {
                Log.Debug("Discord token refresh skipped - this grant was refused earlier this session");
                return false;
            }
            try
            {
                var response = await Http.PostAsJsonAsync("/discord/refresh", new { refresh_token = refreshToken });
                if (!response.IsSuccessStatusCode)
                {
                    string? body = null;
                    try { body = await response.Content.ReadAsStringAsync(); } catch { /* status alone */ }
                    var code = (int)response.StatusCode;
                    if ((code >= 400 && code < 500 && code != 408 && code != 429) || PatreonGrantHealth.BodySaysGrantDead(body))
                    {
                        DeadRefreshTokens.MarkDead(refreshToken);
                        Log.Warning("Discord token refresh refused ({Status}): the grant is dead, sign in with Discord again to repair it", response.StatusCode);
                        return false;
                    }
                    Log.Warning("Discord token refresh failed with status {Status}", response.StatusCode);
                    return false;
                }

                var t = await response.Content.ReadFromJsonAsync<DiscordTokenResponse>();
                if (t == null || !string.IsNullOrEmpty(t.Error))
                {
                    Log.Warning("Discord token refresh error: {Error}", t?.ErrorDescription);
                    // An OAuth error field is the refusal wearing a 200.
                    if (!string.IsNullOrEmpty(t?.Error)) DeadRefreshTokens.MarkDead(refreshToken);
                    return false;
                }

                StoreTokens(t.AccessToken, t.RefreshToken, DateTime.UtcNow.AddSeconds(t.ExpiresIn));
                Log.Information("Discord tokens refreshed successfully");
                return true;
            }
            catch (Exception ex) { Log.Error(ex, "Failed to refresh Discord tokens"); return false; }
        }

        private void UpdateUserInfo(DiscordCachedState c)
        {
            UserId = c.UserId;
            Username = c.Username;
            DisplayName = c.DisplayName;
            Avatar = c.Avatar;
            GuildAvatar = c.GuildAvatar;
            IsStaff = c.IsStaff;
            StaffRole = c.StaffRole;
            CustomDisplayName = c.CustomDisplayName;
            // Re-apply on a cache hit so access survives restarts without a network validate.
            ApplyWhitelistAccess(c.IsWhitelisted);
        }

        /// <summary>Sticky-true, as the server: only ever promotes. 25h &gt; the 24h cache, so each
        /// re-validate renews it and it survives restarts.</summary>
        private void ApplyWhitelistAccess(bool isWhitelisted)
        {
            if (!isWhitelisted) return;
            if (_settings() is { } s)
            {
                s.PatreonPremiumValidUntil = DateTime.UtcNow.AddHours(25);
                CoreSettings.Save();
            }
            _whitelistPeer?.Invoke()?.SetWhitelistStatus(true);
        }

        private void ClearUserInfo()
        {
            UserId = Username = DisplayName = Avatar = GuildAvatar = StaffRole = CustomDisplayName = null;
            IsStaff = false;
        }

        private void LoadCachedState()
        {
            try
            {
                if (RetrieveCachedState() is { IsExpired: false } c && IsAuthenticated) UpdateUserInfo(c);
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to load cached Discord state"); }
        }

        public void Logout()
        {
            ClearTokens();
            ClearUserInfo();
            Log.Information("Discord logout completed");
        }

        /// <summary>
        /// Map the active mod to the theme id the community bot uses to pick a voice for the post.
        /// Community/custom mods (and anything we can't resolve) fall back to "default".
        /// </summary>
        public static string ModThemeId(string? activeModId)
        {
            try
            {
                return activeModId switch
                {
                    BuiltInMods.CCPDefaultId => "default",
                    BuiltInMods.BambiSleepId => "bambi",
                    BuiltInMods.SissyHypnoId => "sissy",
                    BuiltInMods.DronificationId => "drone",
                    BuiltInMods.LockedId => "circe",
                    _ => "default"
                };
            }
            catch
            {
                // Never throw: this runs inside the fire-and-forget share path.
                return "default";
            }
        }

        /// <summary>
        /// Send achievement announcement to community Discord via server
        /// </summary>
        public async Task<bool> SendAchievementWebhookAsync(Achievement achievement, string name, string? unifiedId, Func<string?> authToken, string modThemeId)
        {
            try
            {
                if (string.IsNullOrEmpty(unifiedId))
                {
                    Log.Warning("Achievement share skipped: no unified user id (sharing is on but the account isn't fully linked)");
                    return false;
                }

                var payload = new
                {
                    type = "achievement",
                    display_name = name,
                    unified_id = unifiedId,
                    achievement_name = achievement.Name,
                    achievement_requirement = achievement.Requirement,
                    image_name = achievement.ImageName,
                    // New fields the bot composes mod-themed posts from; the legacy fields above
                    // stay so servers that haven't rolled out yet keep working.
                    achievement_id = achievement.Id,
                    mod_id = modThemeId
                };

                var request = new HttpRequestMessage(HttpMethod.Post, "/discord/community-webhook")
                {
                    Content = JsonContent.Create(payload)
                };
                var token = authToken(); // read only past the unified-id check (a keyring read), as WPF did
                if (!string.IsNullOrEmpty(token))
                    request.Headers.Add("X-Auth-Token", token);

                var response = await Http.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    Log.Information("Achievement shared to community: {Achievement} ({Status}, {Bytes} bytes)",
                        achievement.Id, (int)response.StatusCode, responseText?.Length ?? 0);
                    return true;
                }
                else
                {
                    Log.Warning("Achievement share failed: {Status} (body {Bytes} bytes)",
                        (int)response.StatusCode, responseText?.Length ?? 0);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to share achievement to community");
                return false;
            }
        }

        public void Dispose() => Http.Dispose();

        // ---- token + cache store (CoreSecrets) ----

        public DiscordTokenData? RetrieveTokens() => Read<DiscordTokenData>("discord_auth");
        public DiscordCachedState? RetrieveCachedState() => Read<DiscordCachedState>("discord_cache");
        public bool IsAuthenticated => !string.IsNullOrEmpty(RetrieveTokens()?.AccessToken);
        public string? GetAccessToken() => RetrieveTokens()?.AccessToken;

        /// <summary>Throws on a store fault, as WPF DiscordTokenStorage.StoreTokens did.</summary>
        public void StoreTokens(string accessToken, string refreshToken, DateTime expiresAt) =>
            CoreSecrets.StoreOrThrow("discord_auth", JsonConvert.SerializeObject(new DiscordTokenData
            { AccessToken = accessToken, RefreshToken = refreshToken, ExpiresAt = expiresAt }));

        public void StoreCachedState(DiscordCachedState state) =>
            CoreSecrets.Store("discord_cache", JsonConvert.SerializeObject(state));

        /// <summary>As DiscordTokenStorage.ClearTokens: the cache goes with the tokens.</summary>
        public void ClearTokens() { CoreSecrets.Store("discord_auth", null); CoreSecrets.Store("discord_cache", null); }

        private static T? Read<T>(string name) where T : class
        {
            var json = CoreSecrets.Retrieve(name);
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonConvert.DeserializeObject<T>(json); } catch { return null; }
        }
    }
}
