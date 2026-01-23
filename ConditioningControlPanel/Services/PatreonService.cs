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
    /// Handles Patreon OAuth authentication and subscription validation
    /// </summary>
    public class PatreonService : IDisposable
    {
        private readonly SecureTokenStorage _tokenStorage;
        private readonly HttpClient _httpClient;
        private HttpListener? _callbackListener;
        private CancellationTokenSource? _oauthCts;
        private bool _disposed;

        // Configuration - update these with your actual proxy URL
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
        private const int LocalCallbackPort = 47832;
        private const int CacheHours = 24;
        private const int OAuthTimeoutMinutes = 5;

        // Server-side whitelist status (fetched from proxy)
        private bool _isWhitelisted;

        /// <summary>
        /// Fired when the Patreon tier changes
        /// </summary>
        public event EventHandler<PatreonTier>? TierChanged;

        /// <summary>
        /// Fired when authentication fails
        /// </summary>
        public event EventHandler<string>? AuthenticationFailed;

        /// <summary>
        /// Current subscription tier
        /// </summary>
        public PatreonTier CurrentTier { get; private set; } = PatreonTier.None;

        /// <summary>
        /// Whether the user is authenticated with Patreon (has valid tokens)
        /// </summary>
        public bool IsAuthenticated => _tokenStorage.HasValidTokens();

        /// <summary>
        /// Whether the user is an active paying patron
        /// </summary>
        public bool IsActivePatron { get; private set; }

        /// <summary>
        /// Whether verification is currently in progress
        /// </summary>
        public bool IsVerifying { get; private set; }

        /// <summary>
        /// Patron display name if available
        /// </summary>
        public string? PatronName { get; private set; }

        /// <summary>
        /// Patron email if available (used for whitelist checking)
        /// </summary>
        public string? PatronEmail { get; private set; }

        /// <summary>
        /// Custom display name chosen by user on first login
        /// </summary>
        public string? DisplayName { get; private set; }

        /// <summary>
        /// True if user has a local display name that needs to be synced to server
        /// </summary>
        public bool NeedsDisplayNameMigration { get; private set; }

        /// <summary>
        /// Whether this is the user's first login (no display name set yet on ANY provider).
        /// If Discord already has a display name, this returns false to avoid re-prompting.
        /// </summary>
        public bool IsFirstLogin => IsAuthenticated
            && string.IsNullOrEmpty(DisplayName)
            && string.IsNullOrEmpty(App.Discord?.CustomDisplayName);

        /// <summary>
        /// Whether the user is whitelisted (gets Tier 1 access regardless of subscription)
        /// This is now determined by the server-side whitelist
        /// </summary>
        public bool IsWhitelisted => _isWhitelisted;

        /// <summary>
        /// Whether the user has AI access (Tier 1+ OR whitelisted)
        /// All features are currently Tier 1. Also grants access during 2-week grace period.
        /// </summary>
        public bool HasAiAccess => CurrentTier >= PatreonTier.Level1 || IsWhitelisted || (App.Settings?.Current?.HasCachedPremiumAccess == true);

        /// <summary>
        /// Whether the user has any premium feature access (Tier 1+ OR whitelisted OR within 2-week grace period)
        /// </summary>
        public bool HasPremiumAccess => CurrentTier >= PatreonTier.Level1 || IsWhitelisted || (App.Settings?.Current?.HasCachedPremiumAccess == true);

        public PatreonService()
        {
            _tokenStorage = new SecureTokenStorage();
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(ProxyBaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };

            // Load cached state on startup
            LoadCachedState();
        }

        /// <summary>
        /// Initialize and validate subscription on startup
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                // Skip online validation if offline mode is enabled
                if (App.Settings?.Current?.OfflineMode == true)
                {
                    App.Logger?.Information("Offline mode enabled, using cached Patreon state only");
                    LoadCachedState();
                    return;
                }

                // Force clear cache for v4.1 to pick up whitelist changes
                _tokenStorage.ClearCachedState();
                App.Logger?.Debug("Cleared Patreon cache for fresh validation");

                if (_tokenStorage.HasValidTokens())
                {
                    await ValidateSubscriptionAsync();
                }
                else
                {
                    // No valid tokens - ensure cached premium access is cleared
                    if (App.Settings?.Current != null)
                    {
                        App.Settings.Current.PatreonTier = 0;
                        App.Settings.Current.PatreonPremiumValidUntil = null;
                        App.Logger?.Debug("No Patreon tokens found, cleared cached premium access");
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to validate Patreon subscription on startup");
            }
        }

        /// <summary>
        /// Start OAuth2 browser flow
        /// </summary>
        public async Task StartOAuthFlowAsync()
        {
            if (IsVerifying) return;

            try
            {
                IsVerifying = true;
                _oauthCts = new CancellationTokenSource();

                // Generate CSRF state token (URL-safe: replace +/= with URL-safe chars)
                var stateBytes = new byte[16];
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                {
                    rng.GetBytes(stateBytes);
                }
                var state = Convert.ToHexString(stateBytes); // Hex is URL-safe

                // Start local HTTP listener for callback
                _callbackListener = new HttpListener();
                var callbackUrl = $"http://localhost:{LocalCallbackPort}/callback/";
                _callbackListener.Prefixes.Add(callbackUrl);
                _callbackListener.Start();

                App.Logger?.Information("Started OAuth callback listener on {Url}", callbackUrl);

                // Open browser to authorization URL
                var authUrl = $"{ProxyBaseUrl}/patreon/authorize?redirect_uri={Uri.EscapeDataString(callbackUrl)}&state={state}";

                Process.Start(new ProcessStartInfo
                {
                    FileName = authUrl,
                    UseShellExecute = true
                });

                // Wait for callback with timeout
                var getContextTask = _callbackListener.GetContextAsync();
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(OAuthTimeoutMinutes), _oauthCts.Token);

                var completedTask = await Task.WhenAny(getContextTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException("OAuth login timed out. Please try again.");
                }

                var context = await getContextTask;
                var query = context.Request.QueryString;
                var code = query["code"];
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
                    var errorDesc = query["error_description"] ?? "Unknown error";
                    throw new Exception($"Patreon authorization failed: {errorDesc}");
                }

                if (string.IsNullOrEmpty(code))
                {
                    throw new Exception("No authorization code received");
                }

                // Exchange code for tokens
                await ExchangeCodeForTokensAsync(code, callbackUrl);

                // Validate subscription immediately
                await ValidateSubscriptionAsync(forceRefresh: true);

                App.Logger?.Information("Patreon OAuth flow completed successfully");
            }
            catch (OperationCanceledException)
            {
                App.Logger?.Information("Patreon OAuth flow cancelled");
                throw;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Patreon OAuth flow failed");
                AuthenticationFailed?.Invoke(this, ex.Message);
                throw;
            }
            finally
            {
                IsVerifying = false;
                StopCallbackListener();
            }
        }

        /// <summary>
        /// Cancel ongoing OAuth flow
        /// </summary>
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

        private async Task ExchangeCodeForTokensAsync(string code, string redirectUri)
        {
            var response = await _httpClient.PostAsJsonAsync("/patreon/token", new
            {
                code,
                redirect_uri = redirectUri
            });

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                throw new Exception($"Token exchange failed: {response.StatusCode} - {errorText}");
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<PatreonTokenResponse>();

            if (tokenResponse == null || !string.IsNullOrEmpty(tokenResponse.Error))
            {
                throw new Exception($"Token exchange failed: {tokenResponse?.ErrorDescription ?? "Unknown error"}");
            }

            // Store tokens securely
            _tokenStorage.StoreTokens(
                tokenResponse.AccessToken,
                tokenResponse.RefreshToken,
                DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn));

            App.Logger?.Information("Patreon tokens stored successfully");
        }

        /// <summary>
        /// Validate subscription status with the server
        /// </summary>
        public async Task<PatreonTier> ValidateSubscriptionAsync(bool forceRefresh = false)
        {
            if (IsVerifying && !forceRefresh) return CurrentTier;

            // Skip online validation if offline mode is enabled
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("Offline mode enabled, skipping Patreon validation");
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
                        // Use cached whitelist status from server
                        _isWhitelisted = cachedState.IsWhitelisted;

                        // If whitelisted, ensure they get Level1 access even if cached tier is None
                        var cachedEffectiveTier = cachedState.IsWhitelisted && cachedState.Tier == PatreonTier.None
                            ? PatreonTier.Level1
                            : cachedState.Tier;
                        var cachedEffectivelyActive = cachedState.IsActive || cachedState.IsWhitelisted;

                        UpdateTier(cachedEffectiveTier, cachedEffectivelyActive, cachedState.PatronName, cachedState.PatronEmail, cachedState.DisplayName);
                        return CurrentTier;
                    }
                }

                // Get tokens
                var tokens = _tokenStorage.RetrieveTokens();
                if (tokens == null)
                {
                    UpdateTier(PatreonTier.None, false, null);
                    return PatreonTier.None;
                }

                // Check if token expired and needs refresh
                if (tokens.IsExpired)
                {
                    var refreshed = await RefreshTokensAsync(tokens.RefreshToken);
                    if (!refreshed)
                    {
                        UpdateTier(PatreonTier.None, false, null);
                        return PatreonTier.None;
                    }
                    tokens = _tokenStorage.RetrieveTokens();
                    if (tokens == null)
                    {
                        UpdateTier(PatreonTier.None, false, null);
                        return PatreonTier.None;
                    }
                }

                IsVerifying = true;

                // Validate via hosted proxy
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

                var response = await _httpClient.GetAsync("/patreon/validate");

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Token may be invalid, try refresh
                    var refreshed = await RefreshTokensAsync(tokens.RefreshToken);
                    if (refreshed)
                    {
                        return await ValidateSubscriptionAsync(forceRefresh: true);
                    }
                    else
                    {
                        _tokenStorage.ClearTokens();
                        UpdateTier(PatreonTier.None, false, null);
                        return PatreonTier.None;
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("Patreon validation failed with status {Status}", response.StatusCode);
                    // Use cached tier if available, otherwise fail closed
                    return CurrentTier;
                }

                var subscription = await response.Content.ReadFromJsonAsync<PatreonSubscriptionResponse>();

                if (subscription == null || !string.IsNullOrEmpty(subscription.Error))
                {
                    App.Logger?.Warning("Patreon validation error: {Error}", subscription?.Error);
                    return CurrentTier;
                }

                // Get whitelist status from server response
                var userIsWhitelisted = subscription.IsWhitelisted;
                _isWhitelisted = userIsWhitelisted;

                App.Logger?.Debug("Server whitelist check: Email={Email}, Name={Name}, Whitelisted={Whitelisted}",
                    subscription.PatronEmail, subscription.PatronName, userIsWhitelisted);

                // Update state and cache
                // If active but tier is 0, default to Level1 (proxy may not return tier correctly)
                // Also treat whitelisted users as active with Level1
                var effectivelyActive = subscription.IsActive || userIsWhitelisted;
                var newTier = effectivelyActive
                    ? (subscription.Tier > PatreonTier.None ? subscription.Tier : PatreonTier.Level1)
                    : PatreonTier.None;
                UpdateTier(newTier, effectivelyActive, subscription.PatronName, subscription.PatronEmail);

                // Use DisplayName from server if available, otherwise preserve existing local one
                // Also check Discord as a fallback (for linked accounts)
                var existingCache = _tokenStorage.RetrieveCachedState();
                var serverDisplayName = subscription.DisplayName;
                var localDisplayName = existingCache?.DisplayName ?? DisplayName;
                var discordDisplayName = App.Discord?.CustomDisplayName;

                // Priority: server > local > Discord
                var effectiveDisplayName = !string.IsNullOrEmpty(serverDisplayName)
                    ? serverDisplayName
                    : !string.IsNullOrEmpty(localDisplayName)
                        ? localDisplayName
                        : discordDisplayName;

                // Check if we need to migrate a local name to server
                // This happens when user has a local name but server doesn't have it yet
                NeedsDisplayNameMigration = !string.IsNullOrEmpty(localDisplayName)
                    && string.IsNullOrEmpty(serverDisplayName);

                // Update the DisplayName property
                if (!string.IsNullOrEmpty(serverDisplayName))
                {
                    DisplayName = serverDisplayName;
                    NeedsDisplayNameMigration = false; // Server already has it
                }
                else if (!string.IsNullOrEmpty(discordDisplayName) && string.IsNullOrEmpty(DisplayName))
                {
                    // Adopt Discord's display name if Patreon doesn't have one
                    DisplayName = discordDisplayName;
                    App.Logger?.Information("Adopted display name from Discord: {Name}", DisplayName);
                }

                // Cache result for 24 hours (use effective values for whitelisted users)
                _tokenStorage.StoreCachedState(new PatreonCachedState
                {
                    Tier = newTier,
                    IsActive = effectivelyActive,
                    LastVerified = DateTime.UtcNow,
                    CacheExpiresAt = DateTime.UtcNow.AddHours(CacheHours),
                    PatronName = subscription.PatronName,
                    PatronEmail = subscription.PatronEmail,
                    DisplayName = effectiveDisplayName,
                    IsWhitelisted = userIsWhitelisted
                });

                // If user has premium access, extend the 2-week grace period
                // This allows users to log in with Discord and still keep premium features
                if (newTier >= PatreonTier.Level1 || userIsWhitelisted)
                {
                    if (App.Settings?.Current != null)
                    {
                        App.Settings.Current.PatreonPremiumValidUntil = DateTime.UtcNow.AddDays(14);
                        App.Logger?.Information("Extended premium access grace period to {Date}", App.Settings.Current.PatreonPremiumValidUntil);
                    }
                }

                App.Logger?.Information("Patreon subscription validated: Tier={Tier}, ProxyActive={ProxyActive}, EffectiveActive={EffectiveActive}, Name={Name}, Email={Email}, Whitelisted={Whitelisted}",
                    newTier, subscription.IsActive, effectivelyActive, subscription.PatronName, subscription.PatronEmail, userIsWhitelisted);

                return newTier;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to validate Patreon subscription");
                // Fail closed - return current tier (which may be cached or None)
                return CurrentTier;
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
                var response = await _httpClient.PostAsJsonAsync("/patreon/refresh", new
                {
                    refresh_token = refreshToken
                });

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("Token refresh failed with status {Status}", response.StatusCode);
                    return false;
                }

                var tokenResponse = await response.Content.ReadFromJsonAsync<PatreonTokenResponse>();

                if (tokenResponse == null || !string.IsNullOrEmpty(tokenResponse.Error))
                {
                    App.Logger?.Warning("Token refresh error: {Error}", tokenResponse?.ErrorDescription);
                    return false;
                }

                _tokenStorage.StoreTokens(
                    tokenResponse.AccessToken,
                    tokenResponse.RefreshToken,
                    DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn));

                App.Logger?.Information("Patreon tokens refreshed successfully");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to refresh Patreon tokens");
                return false;
            }
        }

        private void UpdateTier(PatreonTier tier, bool isActive, string? patronName, string? patronEmail = null, string? displayName = null)
        {
            var tierChanged = CurrentTier != tier;
            CurrentTier = tier;
            IsActivePatron = isActive;
            PatronName = patronName;
            PatronEmail = patronEmail;
            // Only update DisplayName if provided (preserve existing)
            if (displayName != null)
            {
                DisplayName = displayName;
            }

            if (tierChanged)
            {
                TierChanged?.Invoke(this, tier);
            }

            // Log whitelist status
            if (IsWhitelisted)
            {
                App.Logger?.Information("User {Email} is whitelisted - granting premium access", PatronEmail);
            }
        }

        /// <summary>
        /// Set the user's display name (can only be set once) and save to server
        /// </summary>
        /// <returns>True if successful, false if name is taken or other error</returns>
        public async Task<(bool Success, string? Error)> SetDisplayNameAsync(string displayName)
        {
            if (!string.IsNullOrEmpty(DisplayName))
            {
                App.Logger?.Warning("Attempted to change display name, but it's already set");
                return (false, "Display name is already set");
            }

            var trimmedName = displayName.Trim();

            // Check if name is already taken on server
            var checkResult = await CheckDisplayNameAvailableAsync(trimmedName);
            if (!checkResult.Available)
            {
                App.Logger?.Warning("Display name '{Name}' is already taken", trimmedName);
                return (false, checkResult.Error ?? "This name is already taken. Please choose another.");
            }

            DisplayName = trimmedName;

            // Update the cached state with the new display name
            var cachedState = _tokenStorage.RetrieveCachedState();
            if (cachedState != null)
            {
                cachedState.DisplayName = DisplayName;
                _tokenStorage.StoreCachedState(cachedState);
            }

            // Save to server so it syncs across devices
            await SaveDisplayNameToServerAsync(DisplayName);

            App.Logger?.Information("Display name set to: {DisplayName}", DisplayName);
            NeedsDisplayNameMigration = false;
            return (true, null);
        }

        /// <summary>
        /// Migrate existing local display name to server (for legacy users).
        /// Returns success if migrated, or false if name is taken and user needs to pick a new one.
        /// </summary>
        public async Task<(bool Success, string? Error)> TryMigrateDisplayNameAsync()
        {
            if (!NeedsDisplayNameMigration || string.IsNullOrEmpty(DisplayName))
            {
                return (true, null); // Nothing to migrate
            }

            App.Logger?.Information("Attempting to migrate local display name to server: {Name}", DisplayName);

            // Check if the name is available
            var checkResult = await CheckDisplayNameAvailableAsync(DisplayName);
            if (!checkResult.Available)
            {
                App.Logger?.Warning("Migration failed - name '{Name}' is already taken", DisplayName);
                // Clear the local name so user can pick a new one
                DisplayName = null;
                var cachedState = _tokenStorage.RetrieveCachedState();
                if (cachedState != null)
                {
                    cachedState.DisplayName = null;
                    _tokenStorage.StoreCachedState(cachedState);
                }
                NeedsDisplayNameMigration = false;
                return (false, checkResult.Error ?? "This name is already taken by another user. Please choose a different name.");
            }

            // Name is available - sync to server
            await SaveDisplayNameToServerAsync(DisplayName);
            NeedsDisplayNameMigration = false;
            App.Logger?.Information("Successfully migrated display name to server: {Name}", DisplayName);
            return (true, null);
        }

        /// <summary>
        /// Check if a display name is available (not already taken)
        /// </summary>
        public async Task<(bool Available, string? Error)> CheckDisplayNameAvailableAsync(string displayName)
        {
            try
            {
                var tokens = _tokenStorage.RetrieveTokens();
                if (tokens == null)
                {
                    return (true, null); // Can't check, allow optimistically
                }

                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

                var response = await _httpClient.GetAsync($"/user/check-display-name?name={Uri.EscapeDataString(displayName)}");

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<DisplayNameCheckResult>();
                    return (result?.Available ?? true, result?.Error);
                }

                // If endpoint doesn't exist or errors, allow optimistically
                return (true, null);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to check display name availability");
                return (true, null); // Allow optimistically on error
            }
        }

        private class DisplayNameCheckResult
        {
            public bool Available { get; set; }
            public string? Error { get; set; }
        }

        /// <summary>
        /// Save display name to the server for cross-device sync
        /// </summary>
        private async Task SaveDisplayNameToServerAsync(string displayName)
        {
            try
            {
                var tokens = _tokenStorage.RetrieveTokens();
                if (tokens == null)
                {
                    App.Logger?.Warning("Cannot save display name: no tokens available");
                    return;
                }

                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

                var response = await _httpClient.PostAsJsonAsync("/user/set-display-name", new
                {
                    display_name = displayName
                });

                if (response.IsSuccessStatusCode)
                {
                    App.Logger?.Information("Display name saved to server successfully");
                }
                else
                {
                    App.Logger?.Warning("Failed to save display name to server: {Status}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to save display name to server");
                // Don't throw - local save was successful, server sync is best-effort
            }
        }

        private void LoadCachedState()
        {
            try
            {
                var cachedState = _tokenStorage.RetrieveCachedState();
                if (cachedState != null && !cachedState.IsExpired && _tokenStorage.HasValidTokens())
                {
                    // Load whitelist status from cache
                    _isWhitelisted = cachedState.IsWhitelisted;

                    // If active or whitelisted but tier is 0, default to Level1
                    var effectivelyActive = cachedState.IsActive || cachedState.IsWhitelisted;
                    CurrentTier = effectivelyActive && cachedState.Tier == PatreonTier.None
                        ? PatreonTier.Level1
                        : cachedState.Tier;
                    IsActivePatron = effectivelyActive;
                    PatronName = cachedState.PatronName;
                    PatronEmail = cachedState.PatronEmail;
                    DisplayName = cachedState.DisplayName;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to load cached Patreon state");
            }
        }

        /// <summary>
        /// Get access token for API calls (used by AiService)
        /// </summary>
        public string? GetAccessToken()
        {
            var tokens = _tokenStorage.RetrieveTokens();
            return tokens?.AccessToken;
        }

        /// <summary>
        /// Logout and clear all stored data
        /// </summary>
        public void Logout()
        {
            _tokenStorage.ClearTokens();
            _tokenStorage.ClearCachedState(); // Clear cached state including DisplayName
            DisplayName = null; // Explicitly clear DisplayName
            NeedsDisplayNameMigration = false;
            _isWhitelisted = false; // Clear whitelist status

            // Clear all cached premium access - user explicitly logged out
            if (App.Settings?.Current != null)
            {
                App.Settings.Current.PatreonPremiumValidUntil = null;
                App.Settings.Current.PatreonTier = 0; // Clear cached tier
                App.Settings.Save(); // Force save immediately
            }

            UpdateTier(PatreonTier.None, false, null);
            App.Logger?.Information("Patreon logout completed, all premium access cleared");
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
