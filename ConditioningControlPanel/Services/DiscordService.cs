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
    /// Handles Discord OAuth authentication and webhook announcements
    /// </summary>
    public class DiscordService : IDisposable
    {
        private readonly DiscordTokenStorage _tokenStorage;
        private readonly HttpClient _httpClient;
        private HttpListener? _callbackListener;
        private CancellationTokenSource? _oauthCts;
        private bool _disposed;

        // Configuration
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
        private const int LocalCallbackPort = 47833; // Different port than Patreon (47832)
        private const int CacheHours = 24;
        private const int OAuthTimeoutMinutes = 5;

        /// <summary>
        /// Fired when Discord authentication state changes
        /// </summary>
        public event EventHandler<bool>? AuthenticationChanged;

        /// <summary>
        /// Fired when authentication fails
        /// </summary>
        public event EventHandler<string>? AuthenticationFailed;

        /// <summary>
        /// Discord user ID
        /// </summary>
        public string? UserId { get; private set; }

        /// <summary>
        /// Discord username
        /// </summary>
        public string? Username { get; private set; }

        /// <summary>
        /// Discord display name (global_name or username)
        /// </summary>
        public string? DisplayName { get; private set; }

        /// <summary>
        /// Discord avatar hash
        /// </summary>
        public string? Avatar { get; private set; }

        /// <summary>
        /// Discord email (if scope granted)
        /// </summary>
        public string? Email { get; private set; }

        /// <summary>
        /// Whether the user is authenticated with Discord
        /// </summary>
        public bool IsAuthenticated => _tokenStorage.HasValidTokens();

        /// <summary>
        /// Whether verification is currently in progress
        /// </summary>
        public bool IsVerifying { get; private set; }

        /// <summary>
        /// Custom display name chosen by the user (for leaderboards/community)
        /// </summary>
        public string? CustomDisplayName { get; private set; }

        /// <summary>
        /// Whether this is the user's first login (no display name set yet on ANY provider).
        /// If Patreon already has a display name, this returns false to avoid re-prompting.
        /// </summary>
        public bool IsFirstLogin => IsAuthenticated
            && string.IsNullOrEmpty(CustomDisplayName)
            && string.IsNullOrEmpty(App.Patreon?.DisplayName);

        /// <summary>
        /// Get the user's avatar URL
        /// </summary>
        public string? GetAvatarUrl(int size = 128)
        {
            if (string.IsNullOrEmpty(Avatar) || string.IsNullOrEmpty(UserId))
                return null;

            var extension = Avatar.StartsWith("a_") ? "gif" : "png";
            return $"https://cdn.discordapp.com/avatars/{UserId}/{Avatar}.{extension}?size={size}";
        }

        public DiscordService()
        {
            _tokenStorage = new DiscordTokenStorage();
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(ProxyBaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };

            // Load cached state on startup
            LoadCachedState();
        }

        /// <summary>
        /// Initialize and validate Discord session on startup
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                // Skip online validation if offline mode is enabled
                if (App.Settings?.Current?.OfflineMode == true)
                {
                    App.Logger?.Information("Offline mode enabled, using cached Discord state only");
                    LoadCachedState();
                    return;
                }

                if (_tokenStorage.HasValidTokens())
                {
                    await ValidateAndRefreshUserAsync();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to validate Discord session on startup");
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

                // Generate CSRF state token (URL-safe hex)
                var stateBytes = new byte[16];
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                {
                    rng.GetBytes(stateBytes);
                }
                var state = Convert.ToHexString(stateBytes);

                // Start local HTTP listener for callback
                _callbackListener = new HttpListener();
                var callbackUrl = $"http://localhost:{LocalCallbackPort}/callback/";
                _callbackListener.Prefixes.Add(callbackUrl);
                _callbackListener.Start();

                App.Logger?.Information("Started Discord OAuth callback listener on {Url}", callbackUrl);

                // Open browser to authorization URL
                var authUrl = $"{ProxyBaseUrl}/discord/authorize?redirect_uri={Uri.EscapeDataString(callbackUrl)}&state={state}";

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
                    throw new TimeoutException("Discord login timed out. Please try again.");
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
                    throw new Exception($"Discord authorization failed: {errorDesc}");
                }

                if (string.IsNullOrEmpty(code))
                {
                    throw new Exception("No authorization code received");
                }

                // Exchange code for tokens
                await ExchangeCodeForTokensAsync(code, callbackUrl);

                // Get user info
                await ValidateAndRefreshUserAsync(forceRefresh: true);

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
    <title>Discord Login Successful</title>
    <style>
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
               display: flex; justify-content: center; align-items: center;
               height: 100vh; margin: 0; background: linear-gradient(135deg, #5865F2 0%, #1a1a2e 100%); }
        .container { text-align: center; color: white; }
        h1 { color: #5865F2; background: white; padding: 10px 20px; border-radius: 8px; }
        p { color: #ccc; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>Discord Connected!</h1>
        <p>You can close this window and return to the application.</p>
    </div>
</body>
</html>"
                : @"<!DOCTYPE html>
<html>
<head>
    <title>Discord Login Failed</title>
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
            var response = await _httpClient.PostAsJsonAsync("/discord/token", new
            {
                code,
                redirect_uri = redirectUri
            });

            if (!response.IsSuccessStatusCode)
            {
                var errorText = await response.Content.ReadAsStringAsync();
                throw new Exception($"Token exchange failed: {response.StatusCode} - {errorText}");
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<DiscordTokenResponse>();

            if (tokenResponse == null || !string.IsNullOrEmpty(tokenResponse.Error))
            {
                throw new Exception($"Token exchange failed: {tokenResponse?.ErrorDescription ?? "Unknown error"}");
            }

            // Store tokens securely
            _tokenStorage.StoreTokens(
                tokenResponse.AccessToken,
                tokenResponse.RefreshToken,
                DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn));

            App.Logger?.Information("Discord tokens stored successfully");
        }

        /// <summary>
        /// Validate user and refresh if needed
        /// </summary>
        public async Task ValidateAndRefreshUserAsync(bool forceRefresh = false)
        {
            if (IsVerifying && !forceRefresh) return;

            // Skip online validation if offline mode is enabled
            if (App.Settings?.Current?.OfflineMode == true)
            {
                App.Logger?.Debug("Offline mode enabled, skipping Discord validation");
                return;
            }

            try
            {
                // Check cache first (unless forcing refresh)
                if (!forceRefresh)
                {
                    var cachedState = _tokenStorage.RetrieveCachedState();
                    if (cachedState != null && !cachedState.IsExpired)
                    {
                        UpdateUserInfo(cachedState);
                        return;
                    }
                }

                // Get tokens
                var tokens = _tokenStorage.RetrieveTokens();
                if (tokens == null)
                {
                    ClearUserInfo();
                    return;
                }

                // Check if token expired and needs refresh
                if (tokens.IsExpired)
                {
                    var refreshed = await RefreshTokensAsync(tokens.RefreshToken);
                    if (!refreshed)
                    {
                        ClearUserInfo();
                        return;
                    }
                    tokens = _tokenStorage.RetrieveTokens();
                    if (tokens == null)
                    {
                        ClearUserInfo();
                        return;
                    }
                }

                IsVerifying = true;

                // Validate via proxy
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

                var response = await _httpClient.GetAsync("/discord/validate");

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Token may be invalid, try refresh
                    var refreshed = await RefreshTokensAsync(tokens.RefreshToken);
                    if (refreshed)
                    {
                        await ValidateAndRefreshUserAsync(forceRefresh: true);
                        return;
                    }
                    else
                    {
                        _tokenStorage.ClearTokens();
                        ClearUserInfo();
                        return;
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("Discord validation failed with status {Status}", response.StatusCode);
                    return;
                }

                var user = await response.Content.ReadFromJsonAsync<DiscordUserResponse>();

                if (user == null || !string.IsNullOrEmpty(user.Error))
                {
                    App.Logger?.Warning("Discord validation error: {Error}", user?.Error);
                    return;
                }

                // Update state and cache
                UserId = user.Id;
                Username = user.Username;
                DisplayName = user.DisplayName;
                Avatar = user.Avatar;
                Email = user.Email;

                // Cache result for 24 hours
                _tokenStorage.StoreCachedState(new DiscordCachedState
                {
                    UserId = user.Id,
                    Username = user.Username,
                    GlobalName = user.GlobalName,
                    Avatar = user.Avatar,
                    Email = user.Email,
                    LastVerified = DateTime.UtcNow,
                    CacheExpiresAt = DateTime.UtcNow.AddHours(CacheHours)
                });

                App.Logger?.Information("Discord user validated: {Username} ({Id})", Username, UserId);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to validate Discord user");
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
                var response = await _httpClient.PostAsJsonAsync("/discord/refresh", new
                {
                    refresh_token = refreshToken
                });

                if (!response.IsSuccessStatusCode)
                {
                    App.Logger?.Warning("Discord token refresh failed with status {Status}", response.StatusCode);
                    return false;
                }

                var tokenResponse = await response.Content.ReadFromJsonAsync<DiscordTokenResponse>();

                if (tokenResponse == null || !string.IsNullOrEmpty(tokenResponse.Error))
                {
                    App.Logger?.Warning("Discord token refresh error: {Error}", tokenResponse?.ErrorDescription);
                    return false;
                }

                _tokenStorage.StoreTokens(
                    tokenResponse.AccessToken,
                    tokenResponse.RefreshToken,
                    DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn));

                App.Logger?.Information("Discord tokens refreshed successfully");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to refresh Discord tokens");
                return false;
            }
        }

        private void UpdateUserInfo(DiscordCachedState cachedState)
        {
            UserId = cachedState.UserId;
            Username = cachedState.Username;
            DisplayName = cachedState.DisplayName;
            Avatar = cachedState.Avatar;
            Email = cachedState.Email;
            CustomDisplayName = cachedState.CustomDisplayName;
        }

        private void ClearUserInfo()
        {
            UserId = null;
            Username = null;
            DisplayName = null;
            Avatar = null;
            Email = null;
            CustomDisplayName = null;
        }

        private void LoadCachedState()
        {
            try
            {
                var cachedState = _tokenStorage.RetrieveCachedState();
                if (cachedState != null && !cachedState.IsExpired && _tokenStorage.HasValidTokens())
                {
                    UpdateUserInfo(cachedState);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to load cached Discord state");
            }
        }

        /// <summary>
        /// Send achievement announcement to community Discord via server
        /// </summary>
        public async Task<bool> SendAchievementWebhookAsync(Achievement achievement, string? displayName = null)
        {
            try
            {
                // Use display name setting
                var name = displayName ?? App.Patreon?.DisplayName ?? App.Discord?.DisplayName ?? "Someone";

                var payload = new
                {
                    type = "achievement",
                    display_name = name,
                    achievement_name = achievement.Name,
                    achievement_requirement = achievement.Requirement
                };

                var response = await _httpClient.PostAsJsonAsync("/discord/community-webhook", payload);
                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    App.Logger?.Information("Achievement shared to community: {Name} - Response: {Response}", achievement.Name, responseText);
                    return true;
                }
                else
                {
                    App.Logger?.Warning("Achievement share failed: {Status} - {Response}", response.StatusCode, responseText);
                    return false;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to share achievement to community");
                return false;
            }
        }

        /// <summary>
        /// Send level up announcement to community Discord via server
        /// </summary>
        public async Task<bool> SendLevelUpWebhookAsync(int level, string? displayName = null)
        {
            try
            {
                var name = displayName ?? App.Patreon?.DisplayName ?? App.Discord?.DisplayName ?? "Someone";

                var payload = new
                {
                    type = "level_up",
                    display_name = name,
                    level = level
                };

                var response = await _httpClient.PostAsJsonAsync("/discord/community-webhook", payload);
                var responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    App.Logger?.Information("Level up shared to community: Level {Level} - Response: {Response}", level, responseText);
                    return true;
                }
                else
                {
                    App.Logger?.Warning("Level up share failed: {Status} - {Response}", response.StatusCode, responseText);
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
            var cachedState = _tokenStorage.RetrieveCachedState();
            if (cachedState != null)
            {
                cachedState.CustomDisplayName = CustomDisplayName;
                _tokenStorage.StoreCachedState(cachedState);
            }

            App.Logger?.Information("Custom display name set to: {DisplayName} (claimed: {Claimed})", CustomDisplayName, claimExisting);
            return (true, null, false);
        }

        /// <summary>
        /// Check if a display name is available (not already taken)
        /// </summary>
        public async Task<(bool Available, string? Error, bool CanClaim)> CheckDisplayNameAvailableAsync(string displayName)
        {
            try
            {
                var tokens = _tokenStorage.RetrieveTokens();
                if (tokens == null)
                {
                    return (true, null, false); // Can't check, allow optimistically
                }

                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

                var response = await _httpClient.GetAsync($"/user/check-display-name-discord?name={Uri.EscapeDataString(displayName)}");

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
                var tokens = _tokenStorage.RetrieveTokens();
                if (tokens == null)
                {
                    App.Logger?.Warning("Cannot save display name: no tokens available");
                    return (false, "Not authenticated", false);
                }

                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

                var response = await _httpClient.PostAsJsonAsync("/user/set-display-name-discord", new
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
                var tokens = _tokenStorage.RetrieveTokens();
                if (tokens == null) return;

                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

                var response = await _httpClient.GetAsync("/user/profile-discord");
                if (response.IsSuccessStatusCode)
                {
                    var profile = await response.Content.ReadFromJsonAsync<DiscordUserProfile>();
                    if (!string.IsNullOrEmpty(profile?.DisplayName))
                    {
                        CustomDisplayName = profile.DisplayName;
                        var cachedState = _tokenStorage.RetrieveCachedState();
                        if (cachedState != null)
                        {
                            cachedState.CustomDisplayName = CustomDisplayName;
                            _tokenStorage.StoreCachedState(cachedState);
                        }
                        App.Logger?.Information("Loaded display name from server: {Name}", CustomDisplayName);
                        return;
                    }
                }

                // If no display name from Discord server, check if Patreon already has one
                // This handles the case where user set their name via Patreon first
                if (string.IsNullOrEmpty(CustomDisplayName) && !string.IsNullOrEmpty(App.Patreon?.DisplayName))
                {
                    CustomDisplayName = App.Patreon.DisplayName;
                    var cachedState = _tokenStorage.RetrieveCachedState();
                    if (cachedState != null)
                    {
                        cachedState.CustomDisplayName = CustomDisplayName;
                        _tokenStorage.StoreCachedState(cachedState);
                    }
                    App.Logger?.Information("Adopted display name from Patreon: {Name}", CustomDisplayName);
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
            _tokenStorage.ClearTokens();
            _tokenStorage.ClearCachedState();
            ClearUserInfo();
            CustomDisplayName = null;
            App.Logger?.Information("Discord logout completed");
            AuthenticationChanged?.Invoke(this, false);
        }

        /// <summary>
        /// Get access token for API calls
        /// </summary>
        public string? GetAccessToken()
        {
            var tokens = _tokenStorage.RetrieveTokens();
            return tokens?.AccessToken;
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
