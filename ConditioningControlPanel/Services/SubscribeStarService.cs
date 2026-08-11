using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles SubscribeStar OAuth authentication and subscription validation.
    ///
    /// Mirrors <see cref="PatreonService"/> but with one structural difference:
    /// SubscribeStar's OAuth client form REQUIRES an https:// redirect URL, so we
    /// cannot register a raw http://localhost loopback the way Patreon does. Instead
    /// the redirect target is the proxy's https callback
    /// (https://codebambi-proxy.vercel.app/substar/callback), which performs the
    /// client_secret token exchange server-side, stashes the result in Redis keyed
    /// by the CSRF state, and then 302-redirects the browser to our local listener
    /// (http://localhost:47834/callback/?state=...). We then POST /substar/exchange
    /// to pull the tokens back. Everything else (validate / refresh / cache / DPAPI
    /// storage) reuses the Patreon plumbing and model types, since the proxy returns
    /// identical response shapes and SubscribeStar tiers map 1:1 onto Patreon tiers.
    /// </summary>
    public class SubscribeStarService : IDisposable
    {
        private readonly SecureTokenStorage _tokenStorage;
        private readonly HttpClient _httpClient;
        private HttpListener? _callbackListener;
        private CancellationTokenSource? _oauthCts;
        private bool _disposed;

        // Same hosted proxy as Patreon; SubscribeStar gets its own endpoints + port.
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
        private const int LocalCallbackPort = 47834; // Patreon=47832, Discord=47833
        private const int CacheHours = 24;
        private const int OAuthTimeoutMinutes = 5;

        // Server-side whitelist status (fetched from proxy)
        private bool _isWhitelisted;

        /// <summary>Fired when the SubscribeStar tier changes</summary>
        public event EventHandler<PatreonTier>? TierChanged;

        /// <summary>Fired when authentication fails</summary>
        public event EventHandler<string>? AuthenticationFailed;

        /// <summary>Current subscription tier (reuses the shared PatreonTier scale)</summary>
        public PatreonTier CurrentTier { get; private set; } = PatreonTier.None;

        /// <summary>Whether the user is authenticated with SubscribeStar (has valid tokens)</summary>
        public bool IsAuthenticated => _tokenStorage.HasValidTokens();

        /// <summary>Whether the user is an active paying subscriber</summary>
        public bool IsActiveSubscriber { get; private set; }

        /// <summary>Whether verification is currently in progress</summary>
        public bool IsVerifying { get; private set; }

        /// <summary>Display name resolved from the server (shared across providers)</summary>
        public string? DisplayName { get; set; }

        /// <summary>Unified user ID from the server (links providers into one account)</summary>
        public string? UnifiedUserId { get; set; }

        /// <summary>Whether the user is whitelisted (server-determined)</summary>
        public bool IsWhitelisted => _isWhitelisted;

        /// <summary>
        /// Whether the user has AI access via SubscribeStar (Tier 1+ OR whitelisted).
        /// OR'd into the canonical gate in <see cref="PatreonService.HasAiAccess"/>.
        /// </summary>
        public bool HasAiAccess => CurrentTier >= PatreonTier.Level1 || IsWhitelisted;

        /// <summary>
        /// Whether the user has premium access via SubscribeStar (Tier 1+ OR whitelisted).
        /// OR'd into the canonical gate in <see cref="PatreonService.HasPremiumAccess"/>.
        /// </summary>
        public bool HasPremiumAccess => CurrentTier >= PatreonTier.Level1 || IsWhitelisted;

        public SubscribeStarService()
        {
            _tokenStorage = new SecureTokenStorage("substar");
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(ProxyBaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");

            LoadCachedState();
        }

        /// <summary>
        /// Initialize and validate subscription on startup
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                if (App.Settings?.Current?.OfflineMode == true)
                {
                    App.Logger?.Information("Offline mode enabled, using cached SubscribeStar state only");
                    LoadCachedState();
                    return;
                }

                if (_tokenStorage.HasValidTokens())
                {
                    await ValidateSubscriptionAsync();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to validate SubscribeStar subscription on startup");
            }
        }

        /// <summary>
        /// Start OAuth2 browser flow (proxy-bridged — see class summary).
        /// </summary>
        public async Task StartOAuthFlowAsync()
        {
            if (IsVerifying) return;

            try
            {
                IsVerifying = true;
                _oauthCts = new CancellationTokenSource();

                // Generate CSRF state token (hex is URL-safe)
                var stateBytes = new byte[16];
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                {
                    rng.GetBytes(stateBytes);
                }
                var state = Convert.ToHexString(stateBytes);

                // Start local HTTP listener for the proxy's final redirect.
                _callbackListener = new HttpListener();
                var callbackUrl = $"http://localhost:{LocalCallbackPort}/callback/";
                _callbackListener.Prefixes.Add(callbackUrl);
                _callbackListener.Start();

                App.Logger?.Information("Started SubscribeStar OAuth callback listener on {Url}", callbackUrl);

                // Open browser to the proxy authorize endpoint. The proxy redirects to
                // SubscribeStar using its registered https redirect_uri, so we do NOT
                // pass redirect_uri here (only the CSRF state, which round-trips).
                var authUrl = $"{ProxyBaseUrl}/substar/authorize?state={state}";

                // Robust open with fallbacks; on total failure copies the link to the clipboard
                // and prompts the user (machines with no default browser otherwise fail silently —
                // see ccp-bugs #404). The callback listener keeps waiting in the meantime.
                Helpers.BrowserLauncher.OpenUrlOrPrompt(authUrl, "sign in with SubscribeStar");

                // Wait for callback with timeout
                var getContextTask = _callbackListener.GetContextAsync();
                // Observe any future fault so a disposed-listener race doesn't surface
                // as an UnobservedTaskException on the finalizer thread.
                _ = getContextTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(OAuthTimeoutMinutes), _oauthCts.Token);

                var completedTask = await Task.WhenAny(getContextTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException("OAuth login timed out. Please try again.");
                }

                var context = await getContextTask;
                var query = context.Request.QueryString;
                var returnedState = query["state"];
                var error = query["error"];

                // Send response to browser
                await SendBrowserResponse(context, string.IsNullOrEmpty(error));

                // Validate state to prevent CSRF
                if (!SecurityHelper.SecureCompare(state, returnedState ?? ""))
                {
                    throw new SecurityException("OAuth state mismatch - possible CSRF attack");
                }

                if (!string.IsNullOrEmpty(error))
                {
                    throw new Exception($"SubscribeStar authorization failed: {error}");
                }

                // The proxy already exchanged the code (it holds the client_secret) and
                // stashed the tokens under this state. Pull them down via /substar/exchange.
                await ExchangeViaProxyAsync(state);

                // Validate subscription immediately
                await ValidateSubscriptionAsync(forceRefresh: true);

                App.Logger?.Information("SubscribeStar OAuth flow completed successfully");
            }
            catch (OperationCanceledException)
            {
                App.Logger?.Information("SubscribeStar OAuth flow cancelled");
                throw;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "SubscribeStar OAuth flow failed");
                AuthenticationFailed?.Invoke(this, ex.Message);
                throw;
            }
            finally
            {
                IsVerifying = false;
                StopCallbackListener();
            }
        }

        /// <summary>Cancel ongoing OAuth flow</summary>
        public void CancelOAuthFlow()
        {
            _oauthCts?.Cancel();
            StopCallbackListener();
        }

        private void StopCallbackListener()
        {
            try
            {
                _callbackListener?.Stop();
                _callbackListener?.Close();
                _callbackListener = null;
            }
            catch { }
        }

        private async Task SendBrowserResponse(HttpListenerContext context, bool success)
        {
            var response = context.Response;
            var html = success
                ? @"<!DOCTYPE html>
<html>
<head>
    <title>Login Successful</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
               display: flex; justify-content: center; align-items: center;
               height: 100vh; margin: 0; background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%); }
        .container { text-align: center; color: white; }
        h1 { color: #ff69b4; }
        p { color: #888; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>Login Successful!</h1>
        <p>You can close this window and return to the application.</p>
    </div>
</body>
</html>"
                : @"<!DOCTYPE html>
<html>
<head>
    <title>Login Failed</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
               display: flex; justify-content: center; align-items: center;
               height: 100vh; margin: 0; background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%); }
        .container { text-align: center; color: white; }
        h1 { color: #ff4444; }
        p { color: #888; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>Login Failed</h1>
        <p>Please try again from the application.</p>
    </div>
</body>
</html>";

            var buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer);
            response.Close();
        }

        /// <summary>
        /// Pull the proxy-exchanged tokens for this OAuth session and store them.
        /// </summary>
        private async Task ExchangeViaProxyAsync(string state)
        {
            var response = await _httpClient.PostAsJsonAsync("/substar/exchange", new { state });

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                throw new Exception($"Token exchange failed: {response.StatusCode} - {errorText}");
            }

            // The exchange blob is a superset of the token shape; extra fields are ignored.
            var tokenResponse = await response.Content.ReadFromJsonAsync<PatreonTokenResponse>();

            if (tokenResponse == null || !string.IsNullOrEmpty(tokenResponse.Error))
            {
                throw new Exception($"Token exchange failed: {tokenResponse?.ErrorDescription ?? "Unknown error"}");
            }

            _tokenStorage.StoreTokens(
                tokenResponse.AccessToken,
                tokenResponse.RefreshToken,
                DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn));

            App.Logger?.Information("SubscribeStar tokens stored successfully");
        }

        /// <summary>
        /// Validate subscription status with the server
        /// </summary>
        public async Task<PatreonTier> ValidateSubscriptionAsync(bool forceRefresh = false)
        {
            if (IsVerifying && !forceRefresh) return CurrentTier;

            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("Offline mode enabled, skipping SubscribeStar validation");
                return CurrentTier;
            }

            try
            {
                // Check cache first (unless forcing refresh)
                if (!forceRefresh)
                {
                    var cachedState = _tokenStorage.RetrieveCachedState();
                    if (cachedState != null && !cachedState.IsExpired)
                    {
                        _isWhitelisted = cachedState.IsWhitelisted;
                        var cachedEffectiveTier = cachedState.IsWhitelisted && cachedState.Tier == PatreonTier.None
                            ? PatreonTier.Level2
                            : cachedState.Tier;
                        var cachedEffectivelyActive = cachedState.IsActive || cachedState.IsWhitelisted;
                        UpdateTier(cachedEffectiveTier, cachedEffectivelyActive, cachedState.DisplayName);
                        return CurrentTier;
                    }
                }

                var tokens = _tokenStorage.RetrieveTokens();
                if (tokens == null)
                {
                    UpdateTier(PatreonTier.None, false);
                    return PatreonTier.None;
                }

                // Refresh if expired
                if (tokens.IsExpired)
                {
                    var refreshed = await RefreshTokensAsync(tokens.RefreshToken);
                    if (!refreshed)
                    {
                        UpdateTier(PatreonTier.None, false);
                        return PatreonTier.None;
                    }
                    tokens = _tokenStorage.RetrieveTokens();
                    if (tokens == null)
                    {
                        UpdateTier(PatreonTier.None, false);
                        return PatreonTier.None;
                    }
                }

                IsVerifying = true;

                // Validate via hosted proxy (POST, no body — Bearer carries identity).
                // Per-request message so the Bearer header doesn't accumulate on
                // DefaultRequestHeaders across calls.
                using var validateRequest = new HttpRequestMessage(HttpMethod.Post, "/substar/validate");
                validateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
                var currentAuthToken = App.Settings?.Current?.AuthToken;
                if (!string.IsNullOrEmpty(currentAuthToken))
                {
                    validateRequest.Headers.Add("X-Auth-Token", currentAuthToken);
                }

                var response = await _httpClient.SendAsync(validateRequest);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    var refreshed = await RefreshTokensAsync(tokens.RefreshToken);
                    if (refreshed)
                    {
                        return await ValidateSubscriptionAsync(forceRefresh: true);
                    }
                    _tokenStorage.ClearTokens();
                    UpdateTier(PatreonTier.None, false);
                    return PatreonTier.None;
                }

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("SubscribeStar validation failed with status {Status}", response.StatusCode);
                    return CurrentTier; // fail closed to cached/current tier
                }

                var subscription = await response.Content.ReadFromJsonAsync<PatreonSubscriptionResponse>();

                if (subscription == null || !string.IsNullOrEmpty(subscription.Error))
                {
                    App.Logger?.Warning("SubscribeStar validation error: {Error}", subscription?.Error);
                    return CurrentTier;
                }

                // Server may heal a divergent auth token (present only on mismatch; never cached).
                if (!string.IsNullOrEmpty(subscription.AuthToken) && App.Settings?.Current != null)
                {
                    App.Settings.Current.AuthToken = subscription.AuthToken;
                    App.Settings.Save();
                    App.Logger?.Information("[Auth] Stored re-issued auth token from SubscribeStar validate (token recovery)");
                }

                var userIsWhitelisted = subscription.IsWhitelisted;
                _isWhitelisted = userIsWhitelisted;

                // Unified account: adopt the server's unified id (don't clobber another
                // provider's — AccountService handles conflict detection).
                if (!string.IsNullOrEmpty(subscription.UnifiedId))
                {
                    UnifiedUserId = subscription.UnifiedId;
                    if (string.IsNullOrEmpty(App.UnifiedUserId))
                    {
                        App.UnifiedUserId = subscription.UnifiedId;
                        App.Logger?.Information("Set UnifiedUserId from SubscribeStar validate: {UnifiedId}", subscription.UnifiedId);
                    }
                }

                var effectivelyActive = subscription.IsActive || userIsWhitelisted;
                var newTier = effectivelyActive
                    ? (subscription.Tier > PatreonTier.None ? subscription.Tier : (userIsWhitelisted ? PatreonTier.Level2 : PatreonTier.Level1))
                    : PatreonTier.None;
                UpdateTier(newTier, effectivelyActive);

                // Display name: prefer server, else keep existing local/cached.
                var existingCache = _tokenStorage.RetrieveCachedState();
                var serverDisplayName = subscription.DisplayName;
                var effectiveDisplayName = !string.IsNullOrEmpty(serverDisplayName)
                    ? serverDisplayName
                    : (existingCache?.DisplayName ?? DisplayName);
                if (!string.IsNullOrEmpty(serverDisplayName))
                {
                    DisplayName = serverDisplayName;
                }

                _tokenStorage.StoreCachedState(new PatreonCachedState
                {
                    Tier = newTier,
                    IsActive = effectivelyActive,
                    LastVerified = DateTime.UtcNow,
                    CacheExpiresAt = DateTime.UtcNow.AddHours(CacheHours),
                    DisplayName = effectiveDisplayName,
                    IsWhitelisted = userIsWhitelisted,
                    UnifiedId = subscription.UnifiedId
                });

                // Extend the shared premium grace window so a SubscribeStar-only premium
                // user keeps access across restarts / brief offline windows (same flag
                // the Patreon gate already honors via HasCachedPremiumAccess).
                if ((newTier >= PatreonTier.Level1 || userIsWhitelisted) && App.Settings?.Current != null)
                {
                    App.Settings.Current.PatreonPremiumValidUntil = DateTime.UtcNow.AddDays(14);

                    // Tier-2 half of the same grace, so a SubscribeStar tier 2 (or a whitelisted
                    // account, which resolves to Level2 everywhere else) keeps the Lab offline
                    // exactly as long as it keeps premium.
                    if (newTier >= PatreonTier.Level2 || userIsWhitelisted)
                        App.Settings.Current.PatreonLabValidUntil = DateTime.UtcNow.AddDays(14);
                }

                App.Logger?.Information("SubscribeStar subscription validated: Tier={Tier}, Active={Active}, Whitelisted={Whitelisted}",
                    newTier, effectivelyActive, userIsWhitelisted);

                return newTier;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to validate SubscribeStar subscription");
                return CurrentTier; // fail closed
            }
            finally
            {
                IsVerifying = false;
            }
        }

        private async Task<bool> RefreshTokensAsync(string refreshToken)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("/substar/refresh", new
                {
                    refresh_token = refreshToken
                });

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("SubscribeStar token refresh failed with status {Status}", response.StatusCode);
                    return false;
                }

                var tokenResponse = await response.Content.ReadFromJsonAsync<PatreonTokenResponse>();

                if (tokenResponse == null || !string.IsNullOrEmpty(tokenResponse.Error))
                {
                    App.Logger?.Warning("SubscribeStar token refresh error: {Error}", tokenResponse?.ErrorDescription);
                    return false;
                }

                _tokenStorage.StoreTokens(
                    tokenResponse.AccessToken,
                    tokenResponse.RefreshToken,
                    DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn));

                App.Logger?.Information("SubscribeStar tokens refreshed successfully");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to refresh SubscribeStar tokens");
                return false;
            }
        }

        private void UpdateTier(PatreonTier tier, bool isActive, string? displayName = null)
        {
            var tierChanged = CurrentTier != tier;
            CurrentTier = tier;
            IsActiveSubscriber = isActive;
            if (displayName != null)
            {
                DisplayName = displayName;
            }

            if (tierChanged)
            {
                TierChanged?.Invoke(this, tier);
            }

            if (IsWhitelisted)
            {
                App.Logger?.Information("SubscribeStar user is whitelisted - granting premium access");
            }
        }

        private void LoadCachedState()
        {
            try
            {
                var cachedState = _tokenStorage.RetrieveCachedState();
                if (cachedState != null && !cachedState.IsExpired && _tokenStorage.HasValidTokens())
                {
                    _isWhitelisted = cachedState.IsWhitelisted;
                    var effectivelyActive = cachedState.IsActive || cachedState.IsWhitelisted;
                    CurrentTier = effectivelyActive && cachedState.Tier == PatreonTier.None
                        ? (cachedState.IsWhitelisted ? PatreonTier.Level2 : PatreonTier.Level1)
                        : cachedState.Tier;
                    IsActiveSubscriber = effectivelyActive;
                    DisplayName = cachedState.DisplayName;

                    if (!string.IsNullOrEmpty(cachedState.UnifiedId))
                    {
                        UnifiedUserId = cachedState.UnifiedId;
                        if (string.IsNullOrEmpty(App.UnifiedUserId))
                        {
                            App.UnifiedUserId = cachedState.UnifiedId;
                            App.Logger?.Information("Restored UnifiedUserId from SubscribeStar cache: {UnifiedId}", cachedState.UnifiedId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to load cached SubscribeStar state");
            }
        }

        /// <summary>Get access token for API calls</summary>
        public string? GetAccessToken()
        {
            var tokens = _tokenStorage.RetrieveTokens();
            return tokens?.AccessToken;
        }

        /// <summary>Logout and clear all stored data</summary>
        public void Logout()
        {
            _tokenStorage.ClearTokens();
            _tokenStorage.ClearCachedState();
            DisplayName = null;
            _isWhitelisted = false;
            UnifiedUserId = null;
            UpdateTier(PatreonTier.None, false);
            App.Logger?.Information("SubscribeStar logout completed");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _oauthCts?.Cancel();
            _oauthCts?.Dispose();
            StopCallbackListener();
            _httpClient.Dispose();
        }
    }
}
