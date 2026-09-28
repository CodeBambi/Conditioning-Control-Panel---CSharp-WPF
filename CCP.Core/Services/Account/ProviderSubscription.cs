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
    /// The Patreon / SubscribeStar token lifecycle every head shares (exchange, refresh, validate, tier,
    /// whitelist, 24h cache, 14-day grace, Patreon's <see cref="GrantLooksDead"/>), moved from WPF
    /// PatreonService / SubscribeStarService, which delegate; providers differ where those did (<c>_patreon</c>).
    /// Tokens/cache go through <see cref="CoreSecrets"/> as <c>{prefix}_auth</c>/<c>_cache</c> (WPF's JSON),
    /// so each head keeps its own store. No store: no tokens, not entitled. The browser flow stays with the caller.
    /// </summary>
    public sealed class ProviderSubscription : IDisposable
    {
        public const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
        private const int CacheHours = 24;
        // Offline grace window. ONE constant for both tiers on purpose: a Lab window that outlived
        // the premium one would let a lapsed tier-2 keep the Lab after losing everything else.
        private const int GraceDays = 14;

        private readonly string _prefix, _label;
        private readonly bool _patreon;
        private readonly Func<AppSettings?> _settings;

        /// <summary>WPF: <c>ProfileSyncService.ApplyValidatePrizes</c>. Unset: prizes ignored.</summary>
        public static volatile Action<string?, string?, PrizesBlock?, string>? PrizesSink;

        /// <summary>Patreon only: another provider's name to adopt (WPF: Discord's). Unset: none.</summary>
        public Func<string?>? PeerDisplayName { get; set; }

        /// <summary>The proxy client. The head's own proxy calls (display-name check) share it.</summary>
        public HttpClient Http { get; }

        public event EventHandler<PatreonTier>? TierChanged;

        public PatreonTier CurrentTier { get; private set; } = PatreonTier.None;
        public bool IsActive { get; private set; }
        public bool IsWhitelisted { get; private set; }
        public bool IsVerifying { get; set; }
        public string? DisplayName { get; set; }
        public string? UnifiedUserId { get; set; }
        public bool NeedsRegistration { get; private set; }
        public bool NeedsDisplayNameMigration { get; set; }

        /// <summary>Patreon only: the proxy REFUSED to refresh this grant (<see cref="PatreonGrantHealth"/>). Offers
        /// a repair, grants nothing; cleared by any good refresh/validate/exchange; in memory only.</summary>
        public bool GrantLooksDead { get; private set; }

        /// <summary>False until <see cref="InitializeAsync"/> settled this launch's entitlement
        /// (#1048: a destructive repair must never fire against the pre-validation blank).</summary>
        public bool EntitlementResolved { get; private set; }

        /// <param name="prefix">"patreon" or "substar". <paramref name="settings"/> defaults to CoreSettings; <paramref name="handler"/> is for tests.</param>
        public ProviderSubscription(string prefix, Func<AppSettings?>? settings = null, HttpMessageHandler? handler = null)
        {
            _prefix = prefix;
            _patreon = prefix == "patreon";
            _label = _patreon ? "Patreon" : "SubscribeStar";
            _settings = settings ?? (() => CoreSettings.HasProvider ? CoreSettings.Service?.Current : null);
            Http = handler == null ? new HttpClient() : new HttpClient(handler);
            Http.BaseAddress = new Uri(ProxyBaseUrl);
            Http.Timeout = TimeSpan.FromSeconds(30);
            Http.DefaultRequestHeaders.Add("X-Client-Version", CoreReleaseContent.AppVersion);
            Http.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{CoreReleaseContent.AppVersion}");
            LoadCachedState();
        }

        /// <summary>The canonical premium (tier 1) gate: either provider's tier 1+ or whitelist, or
        /// the 14-day offline grace. Any null reads as not entitled.</summary>
        public static bool HasPremiumAccess(ProviderSubscription? patreon, ProviderSubscription? substar, AppSettings? settings) =>
            patreon?.CurrentTier >= PatreonTier.Level1 || patreon?.IsWhitelisted == true
            || settings?.HasCachedPremiumAccess == true
            || substar?.CurrentTier >= PatreonTier.Level1 || substar?.IsWhitelisted == true;

        /// <summary>The Lab (tier 2) HOST bar, as the server's <c>computeEffectiveTier &gt;= 2</c>; whitelist
        /// folds to tier 2. Only the Lab's own grace stamp counts, never the premium one.</summary>
        public static bool HasLabAccess(ProviderSubscription? patreon, ProviderSubscription? substar, AppSettings? settings) =>
            patreon?.CurrentTier >= PatreonTier.Level2 || patreon?.IsWhitelisted == true
            || substar?.CurrentTier >= PatreonTier.Level2 || substar?.IsWhitelisted == true
            || settings?.HasCachedLabAccess == true;

        /// <summary>Startup: validate online, or settle from cache in offline mode.</summary>
        public async Task InitializeAsync()
        {
            try
            {
                if (_settings()?.OfflineMode == true)
                {
                    Log.Information("Offline mode enabled, using cached {Provider} state only", _label);
                    LoadCachedState();
                    return;
                }

                // Patreon: force clear cache for v4.1 to pick up whitelist changes.
                if (_patreon) ClearCachedState();

                if (IsAuthenticated) await ValidateSubscriptionAsync();
                else if (_patreon && _settings() is { } s)
                {
                    // No tokens: clear the cached tier. Grace stamps are kept while a unified session
                    // exists (Discord/SubscribeStar accounts have no Patreon tokens); logout clears them.
                    s.PatreonTier = 0;
                    var hasUnifiedSession = !string.IsNullOrWhiteSpace(s.UnifiedId) || !string.IsNullOrWhiteSpace(s.AuthToken);
                    if (!hasUnifiedSession) s.PatreonPremiumValidUntil = s.PatreonLabValidUntil = null;
                    Log.Debug("No Patreon tokens found, cleared cached tier (grace stamps kept: {Kept})", hasUnifiedSession);
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to validate {Provider} subscription on startup", _label); }
            finally
            {
                // Resolved either way: a throw leaves cache/grace as this launch's answer (#1048).
                EntitlementResolved = true;
            }
        }

        /// <summary>Trades the code the loopback caught for tokens (<see cref="LoopbackOAuth.ExchangeAsync"/>)
        /// and stores them. Throws on failure, as the OAuth flow expects.</summary>
        public async Task ExchangeCodeAsync(object body)
        {
            var tokenResponse = await LoopbackOAuth.ExchangeAsync(Http, $"/{_prefix}/token", body);
            StoreTokens(tokenResponse.AccessToken, tokenResponse.RefreshToken, DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn));
            // A brand new grant. Whatever the old one did, this is the repair.
            GrantLooksDead = false;
            Log.Information("{Provider} tokens stored successfully", _label);
        }

        /// <summary>The OAuth sign-in (WPF PatreonService / SubscribeStarService.StartOAuthFlowAsync): browser flow,
        /// exchange, forced validate. No-op while one is running; throws as WPF did.</summary>
        public async Task SignInAsync(Action<string> openBrowser, CancellationToken ct = default)
        {
            if (IsVerifying) return;
            try
            {
                IsVerifying = true;
                var cb = await LoopbackOAuth.SignInAsync(_prefix, openBrowser, ct);
                // The proxy holds the client_secret; SubscribeStar trades the code only against our verifier.
                await ExchangeCodeAsync(_prefix == "substar"
                    ? new { code = cb.Code, state = cb.State, code_verifier = cb.Verifier }
                    : (object)new { code = cb.Code, redirect_uri = cb.CallbackUrl });
                await ValidateSubscriptionAsync(forceRefresh: true);
            }
            finally { IsVerifying = false; }
        }

        /// <summary>Patreon: set whitelist status from an external source (the V2 sync response).</summary>
        public void SetWhitelistStatus(bool whitelisted, PatreonTier minTier = PatreonTier.Level2)
        {
            IsWhitelisted = whitelisted;
            if (!whitelisted || CurrentTier >= minTier) return;
            CurrentTier = minTier;
            IsActive = true;
            TierChanged?.Invoke(this, minTier);
        }

        public void Logout()
        {
            ClearTokens();
            DisplayName = null;
            IsWhitelisted = false;
            if (_patreon)
            {
                NeedsDisplayNameMigration = false;
                GrantLooksDead = false; // nothing to be dead: the tokens are gone with it
                // Clear all cached premium access - user explicitly logged out.
                if (_settings() is { } s)
                {
                    s.PatreonPremiumValidUntil = s.PatreonLabValidUntil = null;
                    s.PatreonTier = 0;
                    CoreSettings.Save();
                }
            }
            else UnifiedUserId = null;
            UpdateTier(PatreonTier.None, false);
            Log.Information("{Provider} logout completed", _label);
        }

        public void Dispose() => Http.Dispose();

        /// <summary>Validate subscription status with the proxy. Never throws: any failure keeps
        /// the current (cached or None) tier.</summary>
        public async Task<PatreonTier> ValidateSubscriptionAsync(bool forceRefresh = false)
        {
            if (IsVerifying && !forceRefresh) return CurrentTier;

            var settings = _settings();
            if (settings?.OfflineMode == true) { Log.Debug("Offline mode enabled, skipping {Provider} validation", _label); return CurrentTier; }

            try
            {
                if (!forceRefresh)
                {
                    var cached = RetrieveCachedState();
                    if (cached != null && !cached.IsExpired)
                    {
                        IsWhitelisted = cached.IsWhitelisted;
                        // Whitelisted with a cached None still gets Level2.
                        var cachedTier = cached.IsWhitelisted && cached.Tier == PatreonTier.None ? PatreonTier.Level2 : cached.Tier;
                        UpdateTier(cachedTier, cached.IsActive || cached.IsWhitelisted, cached.DisplayName);
                        return CurrentTier;
                    }
                }

                var tokens = RetrieveTokens();
                if (tokens == null) return Drop();

                if (tokens.IsExpired)
                {
                    var refreshed = await RefreshTokensAsync(tokens.RefreshToken, tokens.ExpiresAt);
                    if (!refreshed)
                    {
                        // Patreon keeps the cached tier (#585: usually transient; cache + grace time-box it).
                        // SubscribeStar drops to None, as it did.
                        if (!_patreon) return Drop();
                        Log.Warning("Patreon token refresh failed (expired access token) - keeping cached tier {Tier}", CurrentTier);
                        return CurrentTier;
                    }
                    tokens = RetrieveTokens();
                    if (tokens == null)
                    {
                        if (!_patreon) return Drop();
                        // Stored and then gone: nothing here can talk to Patreon, so offer the repair.
                        GrantLooksDead = true;
                        Log.Warning("Patreon tokens missing after successful refresh - keeping cached tier {Tier}", CurrentTier);
                        return CurrentTier;
                    }
                }

                IsVerifying = true;

                // Per-request headers (never accumulate); X-Auth-Token lets the server heal it (BUG-7DCJHDP3JZ).
                using var request = new HttpRequestMessage(_patreon ? HttpMethod.Get : HttpMethod.Post, $"/{_prefix}/validate");
                var prizesFor = settings?.UnifiedId;
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
                if (!string.IsNullOrEmpty(settings?.AuthToken)) request.Headers.Add("X-Auth-Token", settings.AuthToken);

                var response = await Http.SendAsync(request);

                // Contract D: a merge tombstone; the swap re-signs in on the canonical. Keep the tier.
                if (V2AuthService.MergedRecovery is { } merged && await merged(response, null)) return CurrentTier;

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    if (await RefreshTokensAsync(tokens.RefreshToken, tokens.ExpiresAt))
                        return await ValidateSubscriptionAsync(forceRefresh: true);
                    ClearTokens();
                    return Drop();
                }

                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("{Provider} validation failed with status {Status}", _label, response.StatusCode);
                    return CurrentTier;
                }

                var sub = await response.Content.ReadFromJsonAsync<PatreonSubscriptionResponse>();
                if (sub == null || !string.IsNullOrEmpty(sub.Error))
                {
                    Log.Warning("{Provider} validation error: {Error}", _label, sub?.Error);
                    return CurrentTier;
                }

                return ApplyValidated(sub, settings, prizesFor);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to validate {Provider} subscription", _label);
                return CurrentTier; // fail closed: current tier (cached or None)
            }
            finally { IsVerifying = false; }
        }

        private PatreonTier Drop() { UpdateTier(PatreonTier.None, false); return PatreonTier.None; }

        private PatreonTier ApplyValidated(PatreonSubscriptionResponse sub, AppSettings? settings, string? prizesFor)
        {
            // The stored grant just reached the provider and came back with an answer: it is alive.
            GrantLooksDead = false;
            PrizesSink?.Invoke(prizesFor, sub.UnifiedId, sub.Prizes, $"{_label} validate");

            // Server-healed auth token (only on mismatch; never cached). Patreon refuses one for a
            // DIFFERENT unified record: adopting it would flip-flop with the other provider forever.
            if (!string.IsNullOrEmpty(sub.AuthToken) && settings != null)
            {
                var local = settings.UnifiedId;
                if (_patreon && !string.IsNullOrEmpty(local) && !string.IsNullOrEmpty(sub.UnifiedId)
                    && !string.Equals(sub.UnifiedId, local, StringComparison.Ordinal))
                {
                    Log.Warning("[Auth] Patreon validate resolved to unified account {ServerId} but this session is {LocalId} - " +
                        "REFUSING the re-issued auth token (it belongs to the other record).", sub.UnifiedId, local);
                }
                else
                {
                    settings.AuthToken = sub.AuthToken;
                    CoreSettings.Save();
                    Log.Information("[Auth] Stored re-issued auth token from {Provider} validate (token recovery)", _label);
                }
            }

            var whitelisted = sub.IsWhitelisted;
            IsWhitelisted = whitelisted;
            if (_patreon) NeedsRegistration = sub.NeedsRegistration;

            // Adopt the server's unified id; never clobber one another provider set.
            if (!string.IsNullOrEmpty(sub.UnifiedId))
            {
                UnifiedUserId = sub.UnifiedId;
                if (string.IsNullOrEmpty(CoreAccount.UnifiedUserId)) CoreAccount.UnifiedUserId = sub.UnifiedId;
            }

            // Active with tier 0 defaults to Level1 (the proxy may not return the tier); whitelisted gets Level2.
            var active = sub.IsActive || whitelisted;
            var newTier = active
                ? (sub.Tier > PatreonTier.None ? sub.Tier : (whitelisted ? PatreonTier.Level2 : PatreonTier.Level1))
                : PatreonTier.None;
            UpdateTier(newTier, active);

            var serverName = sub.DisplayName;
            string? effectiveName;
            if (_patreon)
            {
                // Priority: server > local > the other provider's.
                var localName = RetrieveCachedState()?.DisplayName ?? DisplayName;
                var peerName = PeerDisplayName?.Invoke();
                effectiveName = !string.IsNullOrEmpty(serverName) ? serverName
                    : !string.IsNullOrEmpty(localName) ? localName : peerName;
                NeedsDisplayNameMigration = !string.IsNullOrEmpty(localName) && string.IsNullOrEmpty(serverName);
                if (!string.IsNullOrEmpty(serverName)) { DisplayName = serverName; NeedsDisplayNameMigration = false; }
                else if (!string.IsNullOrEmpty(peerName) && string.IsNullOrEmpty(DisplayName)) DisplayName = peerName;
            }
            else
            {
                effectiveName = !string.IsNullOrEmpty(serverName) ? serverName : (RetrieveCachedState()?.DisplayName ?? DisplayName);
                if (!string.IsNullOrEmpty(serverName)) DisplayName = serverName;
            }

            StoreCachedState(new PatreonCachedState
            {
                Tier = newTier,
                IsActive = active,
                LastVerified = DateTime.UtcNow,
                CacheExpiresAt = DateTime.UtcNow.AddHours(CacheHours),
                DisplayName = effectiveName,
                IsWhitelisted = whitelisted,
                UnifiedId = sub.UnifiedId
            });

            // Premium extends the 14-day grace; the Lab half follows this tier. Patreon tier 1 kills a stale
            // Lab window; SubscribeStar also stamps it when whitelisted and leaves it on tier 1.
            if ((newTier >= PatreonTier.Level1 || whitelisted) && settings != null)
            {
                settings.PatreonPremiumValidUntil = DateTime.UtcNow.AddDays(GraceDays);
                if (newTier >= PatreonTier.Level2 || (!_patreon && whitelisted))
                    settings.PatreonLabValidUntil = DateTime.UtcNow.AddDays(GraceDays);
                else if (_patreon)
                    settings.PatreonLabValidUntil = null;
            }

            Log.Information("{Provider} subscription validated: Tier={Tier}, ProxyActive={ProxyActive}, Whitelisted={Whitelisted}",
                _label, newTier, sub.IsActive, whitelisted);
            return newTier;
        }

        /// <param name="expiresAtUtc">Tells a revoked grant from a proxy outage (<see cref="PatreonGrantHealth"/>).</param>
        private async Task<bool> RefreshTokensAsync(string refreshToken, DateTime? expiresAtUtc)
        {
            try
            {
                var response = await Http.PostAsJsonAsync($"/{_prefix}/refresh", new { refresh_token = refreshToken });
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("{Provider} token refresh failed with status {Status}", _label, response.StatusCode);
                    return NoteRefresh(PatreonGrantHealth.Classify(response.StatusCode, null, threw: false, expiresAtUtc, DateTime.UtcNow));
                }

                var tokenResponse = await response.Content.ReadFromJsonAsync<PatreonTokenResponse>();
                if (tokenResponse == null || !string.IsNullOrEmpty(tokenResponse.Error))
                {
                    Log.Warning("{Provider} token refresh error: {Error}", _label, tokenResponse?.ErrorDescription);
                    // A missing body is no verdict; an OAuth error field is a refusal wearing a 200.
                    return NoteRefresh(tokenResponse == null
                        ? PatreonRefreshOutcome.Unavailable
                        : PatreonGrantHealth.Classify(response.StatusCode, tokenResponse.Error, threw: false, expiresAtUtc, DateTime.UtcNow));
                }

                StoreTokens(tokenResponse.AccessToken, tokenResponse.RefreshToken, DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn));
                Log.Information("{Provider} tokens refreshed successfully", _label);
                return NoteRefresh(PatreonRefreshOutcome.Refreshed);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to refresh {Provider} tokens", _label);
                return NoteRefresh(PatreonGrantHealth.Classify(null, null, threw: true, expiresAtUtc, DateTime.UtcNow));
            }
        }

        /// <summary>Refusal raises <see cref="GrantLooksDead"/>, success lowers it, an outage leaves it (Patreon only).</summary>
        private bool NoteRefresh(PatreonRefreshOutcome outcome)
        {
            if (_patreon && outcome == PatreonRefreshOutcome.Refreshed) GrantLooksDead = false;
            else if (_patreon && PatreonGrantHealth.MarksGrantDead(outcome)) GrantLooksDead = true;
            return outcome == PatreonRefreshOutcome.Refreshed;
        }

        private void UpdateTier(PatreonTier tier, bool isActive, string? displayName = null)
        {
            var changed = CurrentTier != tier;
            CurrentTier = tier;
            IsActive = isActive;
            if (displayName != null) DisplayName = displayName;
            if (changed) TierChanged?.Invoke(this, tier);
            if (IsWhitelisted) Log.Information("{Provider} user is whitelisted - granting premium access", _label);
        }

        private void LoadCachedState()
        {
            try
            {
                var cached = RetrieveCachedState();
                if (cached == null || cached.IsExpired || !IsAuthenticated) return;
                IsWhitelisted = cached.IsWhitelisted;
                var active = cached.IsActive || cached.IsWhitelisted;
                CurrentTier = active && cached.Tier == PatreonTier.None
                    ? (cached.IsWhitelisted ? PatreonTier.Level2 : PatreonTier.Level1)
                    : cached.Tier;
                IsActive = active;
                DisplayName = cached.DisplayName;
                if (!string.IsNullOrEmpty(cached.UnifiedId))
                {
                    UnifiedUserId = cached.UnifiedId;
                    if (string.IsNullOrEmpty(CoreAccount.UnifiedUserId)) CoreAccount.UnifiedUserId = cached.UnifiedId;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to load cached {Provider} state", _label); }
        }

        // ---- token + cache store (CoreSecrets) ----

        public PatreonTokenData? RetrieveTokens() => Read<PatreonTokenData>(_prefix + "_auth");
        public PatreonCachedState? RetrieveCachedState() => Read<PatreonCachedState>(_prefix + "_cache");
        public bool IsAuthenticated => !string.IsNullOrEmpty(RetrieveTokens()?.AccessToken);
        public string? GetAccessToken() => RetrieveTokens()?.AccessToken;

        /// <summary>Throws on a store fault, as WPF StoreTokens did (else a failed save reads as Refreshed).</summary>
        public void StoreTokens(string accessToken, string refreshToken, DateTime expiresAt) =>
            CoreSecrets.StoreOrThrow(_prefix + "_auth", JsonConvert.SerializeObject(new PatreonTokenData
            { AccessToken = accessToken, RefreshToken = refreshToken, ExpiresAt = expiresAt }));

        public void StoreCachedState(PatreonCachedState state) =>
            CoreSecrets.Store(_prefix + "_cache", JsonConvert.SerializeObject(state));

        /// <summary>As SecureTokenStorage.ClearTokens: the cache goes with the tokens.</summary>
        public void ClearTokens() { CoreSecrets.Store(_prefix + "_auth", null); ClearCachedState(); }
        public void ClearCachedState() => CoreSecrets.Store(_prefix + "_cache", null);

        private static T? Read<T>(string name) where T : class
        {
            var json = CoreSecrets.Retrieve(name);
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonConvert.DeserializeObject<T>(json); } catch { return null; }
        }
    }
}
