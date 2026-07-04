using System;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Auth;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Services.Auth;

/// <summary>
/// Avalonia port of the WPF <see cref="ConditioningControlPanel.Services.DiscordService"/>.
/// Handles Discord OAuth, token storage, user validation, and whitelist-based premium grace.
/// </summary>
public sealed class AvaloniaDiscordProvider : IAuthProvider, INotifyPropertyChanged, IDisposable
{
    private readonly AvaloniaTokenStorage _tokenStorage;
    private readonly ISettingsService _settingsService;
    private readonly IBrowserHost _browserHost;
    private readonly IDialogService _dialogService;
    private readonly ILogger<AvaloniaDiscordProvider>? _logger;
    private readonly HttpClient _httpClient;

    private HttpListener? _callbackListener;
    private CancellationTokenSource? _oauthCts;
    private bool _disposed;
    private bool _isVerifying;
    private bool _isWhitelisted;
    private string? _userId;
    private string? _username;
    private string? _globalName;
    private string? _avatar;
    private string? _customDisplayName;
    private string? _unifiedUserId;

    public event EventHandler<bool>? AuthenticationChanged;
    public event EventHandler<string>? AuthenticationFailed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProviderName => "discord";

    public bool IsLoggedIn => _tokenStorage.HasValidTokens<DiscordTokenData>();

    public bool IsAuthenticated => IsLoggedIn;

    public bool IsVerifying
    {
        get => _isVerifying;
        private set => SetProperty(ref _isVerifying, value);
    }

    public string? UserId
    {
        get => _userId;
        private set => SetProperty(ref _userId, value);
    }

    public string? Username
    {
        get => _username;
        private set => SetProperty(ref _username, value);
    }

    public string? GlobalName
    {
        get => _globalName;
        private set => SetProperty(ref _globalName, value);
    }

    /// <summary>
    /// Discord avatar hash.
    /// </summary>
    public string? Avatar
    {
        get => _avatar;
        private set => SetProperty(ref _avatar, value);
    }

    /// <summary>
    /// Discord-calculated display name (global_name if set, otherwise username).
    /// </summary>
    public string DisplayName => !string.IsNullOrEmpty(GlobalName) ? GlobalName : Username ?? string.Empty;

    /// <summary>
    /// Custom user-chosen display name (server synced).
    /// </summary>
    public string? CustomDisplayName
    {
        get => _customDisplayName;
        private set => SetProperty(ref _customDisplayName, value);
    }

    public string? UnifiedUserId
    {
        get => _unifiedUserId;
        set => SetProperty(ref _unifiedUserId, value);
    }

    public bool NeedsRegistration { get; private set; }

    public bool IsWhitelisted => _isWhitelisted;

    public bool HasPremiumAccess
    {
        get
        {
            var settings = _settingsService.Current;
            return _isWhitelisted || (settings?.HasCachedPremiumAccess == true);
        }
    }

    string? IAuthProvider.DisplayName
    {
        get => DisplayName;
        set => CustomDisplayName = value;
    }

    // D7: WPF DiscordService.IsFirstLogin gates on the PATREON display name, not the
    // Discord-derived DisplayName (which is always non-empty once authed, so the old check was
    // always false). Cross-check the unified/Patreon display name (settings.UserDisplayName,
    // written by Patreon/unified validation) so genuinely new users are still prompted to pick
    // a name while users who already have one are not re-prompted.
    public bool IsFirstLogin => IsAuthenticated
        && string.IsNullOrEmpty(CustomDisplayName)
        && string.IsNullOrEmpty(_settingsService.Current?.UserDisplayName);

    public AvaloniaDiscordProvider(
        ISecretStore secretStore,
        ISettingsService settingsService,
        IBrowserHost browserHost,
        IDialogService dialogService,
        ILogger<AvaloniaDiscordProvider>? logger = null,
        ILogger<AvaloniaTokenStorage>? tokenStorageLogger = null)
    {
        _tokenStorage = new AvaloniaTokenStorage(secretStore, "discord", tokenStorageLogger);
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _browserHost = browserHost ?? throw new ArgumentNullException(nameof(browserHost));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _logger = logger;

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(AuthConstants.ProxyBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("X-Client-Version", AuthConstants.ClientVersion);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{AuthConstants.ClientVersion}");

        LoadCachedState();
    }

    public async Task InitializeAsync()
    {
        try
        {
            if (_settingsService.Current.OfflineMode)
            {
                _logger?.LogInformation("Offline mode enabled, using cached Discord state only");
                LoadCachedState();
                return;
            }

            if (_tokenStorage.HasValidTokens<DiscordTokenData>())
                await ValidateAndRefreshUserAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to validate Discord session on startup");
        }
    }

    public async Task StartOAuthFlowAsync()
    {
        if (IsVerifying) return;

        try
        {
            IsVerifying = true;
            _oauthCts = new CancellationTokenSource();

            var stateBytes = new byte[16];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(stateBytes);
            var state = Convert.ToHexString(stateBytes);

            _callbackListener = new HttpListener();
            var callbackUrl = $"http://localhost:{AuthConstants.DiscordCallbackPort}/callback/";
            _callbackListener.Prefixes.Add(callbackUrl);
            _callbackListener.Start();

            _logger?.LogInformation("Started Discord OAuth callback listener on {Url}", callbackUrl);

            var authUrl = $"{AuthConstants.ProxyBaseUrl}/discord/authorize?redirect_uri={Uri.EscapeDataString(callbackUrl)}&state={state}";
            await BrowserLauncher.OpenUrlOrPromptAsync(
                _browserHost,
                _dialogService,
                static async text =>
                {
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                        && desktop.MainWindow?.Clipboard is IClipboard clipboard)
                        await clipboard.SetTextAsync(text);
                },
                authUrl,
                "sign in with Discord");

            var getContextTask = _callbackListener.GetContextAsync();
            _ = getContextTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(AuthConstants.OAuthTimeoutMinutes), _oauthCts.Token);

            var completedTask = await Task.WhenAny(getContextTask, timeoutTask);
            if (completedTask == timeoutTask)
                throw new TimeoutException("Discord login timed out. Please try again.");

            var context = await getContextTask;
            var query = context.Request.QueryString;
            var code = query["code"];
            var returnedState = query["state"];
            var error = query["error"];

            await SendBrowserResponseAsync(context, string.IsNullOrEmpty(error));

            if (!AuthSecurityHelper.SecureCompare(state, returnedState ?? ""))
                throw new SecurityException("OAuth state mismatch - possible CSRF attack");

            if (!string.IsNullOrEmpty(error))
                throw new Exception($"Discord authorization failed: {query["error_description"] ?? "Unknown error"}");

            if (string.IsNullOrEmpty(code))
                throw new Exception("No authorization code received");

            await ExchangeCodeForTokensAsync(code, callbackUrl);
            await ValidateAndRefreshUserAsync(forceRefresh: true);
            await LoadDisplayNameFromServerAsync();

            _logger?.LogInformation("Discord OAuth flow completed successfully");
            AuthenticationChanged?.Invoke(this, true);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Discord OAuth flow cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Discord OAuth flow failed");
            AuthenticationFailed?.Invoke(this, ex.Message);
            throw;
        }
        finally
        {
            IsVerifying = false;
            _oauthCts?.Cancel(); // R6: also cancel the OAuth timeout CTS on the success path
            StopCallbackListener();
        }
    }

    public void CancelOAuthFlow()
    {
        _oauthCts?.Cancel();
        StopCallbackListener();
    }

    public async Task ValidateAndRefreshUserAsync(bool forceRefresh = false)
    {
        if (IsVerifying && !forceRefresh) return;

        var settings = _settingsService.Current;
        if (settings.OfflineMode)
        {
            _logger?.LogDebug("Offline mode enabled, skipping Discord validation");
            return;
        }

        try
        {
            if (!forceRefresh)
            {
                var cachedState = _tokenStorage.RetrieveCachedState<DiscordCachedState>();
                if (cachedState != null && !cachedState.IsExpired)
                {
                    UpdateUserInfo(cachedState);
                    return;
                }
            }

            var tokens = _tokenStorage.RetrieveTokens<DiscordTokenData>();
            if (tokens == null)
            {
                ClearUserInfo();
                return;
            }

            if (tokens.IsExpired)
            {
                var refreshed = await RefreshTokensAsync(tokens.RefreshToken);
                if (!refreshed)
                {
                    ClearUserInfo();
                    return;
                }
                tokens = _tokenStorage.RetrieveTokens<DiscordTokenData>();
                if (tokens == null)
                {
                    ClearUserInfo();
                    return;
                }
            }

            IsVerifying = true;

            using var validateRequest = new HttpRequestMessage(HttpMethod.Get, "/discord/validate");
            validateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            var currentAuthToken = settings.AuthToken;
            if (!string.IsNullOrEmpty(currentAuthToken))
                validateRequest.Headers.Add("X-Auth-Token", currentAuthToken);

            var response = await _httpClient.SendAsync(validateRequest);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var refreshed = await RefreshTokensAsync(tokens.RefreshToken);
                if (refreshed)
                {
                    await ValidateAndRefreshUserAsync(forceRefresh: true);
                    return;
                }
                _tokenStorage.ClearTokens();
                ClearUserInfo();
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Discord validation failed with status {Status}", response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var user = JsonConvert.DeserializeObject<DiscordUserResponse>(json);

            if (user == null || !string.IsNullOrEmpty(user.Error))
            {
                _logger?.LogWarning("Discord validation error: {Error}", user?.Error);
                return;
            }

            if (!string.IsNullOrEmpty(user.AuthToken))
            {
                settings.AuthToken = user.AuthToken;
                _settingsService.SaveImmediate();
                _logger?.LogInformation("[Auth] Stored re-issued auth token from Discord validate (token recovery)");
            }

            UserId = user.Id;
            Username = user.Username;
            GlobalName = user.GlobalName;
            Avatar = user.Avatar;
            NeedsRegistration = user.NeedsRegistration;

            if (user.IsWhitelisted)
            {
                _logger?.LogInformation("Discord validate: whitelisted user — granting premium access");
            }
            ApplyWhitelistAccess(user.IsWhitelisted);

            _tokenStorage.StoreCachedState(new DiscordCachedState
            {
                UserId = user.Id,
                Username = user.Username,
                GlobalName = user.GlobalName,
                Avatar = user.Avatar,
                CustomDisplayName = CustomDisplayName,
                IsWhitelisted = user.IsWhitelisted,
                LastVerified = DateTime.UtcNow,
                CacheExpiresAt = DateTime.UtcNow.AddHours(AuthConstants.CacheHours)
            });

            settings.HasLinkedDiscord = true;
            _settingsService.SaveImmediate();

            _logger?.LogInformation("Discord user validated: {Id}, NeedsRegistration={NeedsReg}, Whitelisted={Whitelisted}",
                UserId, user.NeedsRegistration, user.IsWhitelisted);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to validate Discord user");
        }
        finally
        {
            IsVerifying = false;
        }
    }

    public string? GetAvatarUrl(int size = 128)
    {
        if (string.IsNullOrEmpty(Avatar) || string.IsNullOrEmpty(UserId))
            return null;

        var extension = Avatar.StartsWith("a_") ? "gif" : "png";
        return $"https://cdn.discordapp.com/avatars/{UserId}/{Avatar}.{extension}?size={size}";
    }

    public string? GetAccessToken()
    {
        var tokens = _tokenStorage.RetrieveTokens<DiscordTokenData>();
        return tokens?.AccessToken;
    }

    public void Logout()
    {
        _tokenStorage.ClearTokens();
        _tokenStorage.ClearCachedState();
        ClearUserInfo();
        CustomDisplayName = null;
        _isWhitelisted = false;
        UnifiedUserId = null;

        var settings = _settingsService.Current;
        settings.HasLinkedDiscord = false;
        _settingsService.SaveImmediate();

        _logger?.LogInformation("Discord logout completed");
        AuthenticationChanged?.Invoke(this, false);
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

    private async Task ExchangeCodeForTokensAsync(string code, string redirectUri)
    {
        var payload = new { code, redirect_uri = redirectUri };
        var response = await _httpClient.PostAsync("/discord/token",
            new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            throw new Exception($"Token exchange failed: {response.StatusCode} - {errorText}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonConvert.DeserializeObject<DiscordTokenResponse>(json);

        if (tokenResponse == null || !string.IsNullOrEmpty(tokenResponse.Error))
            throw new Exception($"Token exchange failed: {tokenResponse?.ErrorDescription ?? "Unknown error"}");

        // R12: a 200 with an empty/missing access token must never persist null tokens.
        if (string.IsNullOrEmpty(tokenResponse.AccessToken))
            throw new InvalidOperationException("Discord token exchange returned an empty access token.");

        _tokenStorage.StoreTokens(new DiscordTokenData
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
        });

        _logger?.LogInformation("Discord tokens stored successfully");
    }

    private async Task<bool> RefreshTokensAsync(string refreshToken)
    {
        try
        {
            var payload = new { refresh_token = refreshToken };
            var response = await _httpClient.PostAsync("/discord/refresh",
                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("Discord token refresh failed with status {Status}", response.StatusCode);
                return false;
            }

            var json = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonConvert.DeserializeObject<DiscordTokenResponse>(json);

            if (tokenResponse == null || !string.IsNullOrEmpty(tokenResponse.Error))
            {
                _logger?.LogWarning("Discord token refresh error: {Error}", tokenResponse?.ErrorDescription);
                return false;
            }

            // R12: reject a 200 with an empty/missing access token so we never persist null tokens.
            if (string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                _logger?.LogWarning("Discord token refresh returned an empty access token");
                return false;
            }

            _tokenStorage.StoreTokens(new DiscordTokenData
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
            });

            _logger?.LogInformation("Discord tokens refreshed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to refresh Discord tokens");
            return false;
        }
    }

    private void UpdateUserInfo(DiscordCachedState cachedState)
    {
        UserId = cachedState.UserId;
        Username = cachedState.Username;
        GlobalName = cachedState.GlobalName;
        Avatar = cachedState.Avatar;
        CustomDisplayName = cachedState.CustomDisplayName;
        ApplyWhitelistAccess(cachedState.IsWhitelisted);
    }

    private void ApplyWhitelistAccess(bool isWhitelisted)
    {
        if (!isWhitelisted) return;

        _isWhitelisted = true;
        var settings = _settingsService.Current;
        if (settings != null)
        {
            settings.PatreonPremiumValidUntil = DateTime.UtcNow.AddHours(25);
            _settingsService.SaveImmediate();
        }
        NotifyPropertyChanged(nameof(HasPremiumAccess));
    }

    private void ClearUserInfo()
    {
        UserId = null;
        Username = null;
        GlobalName = null;
        Avatar = null;
    }

    private void LoadCachedState()
    {
        try
        {
            var cachedState = _tokenStorage.RetrieveCachedState<DiscordCachedState>();
            if (cachedState != null && !cachedState.IsExpired && _tokenStorage.HasValidTokens<DiscordTokenData>())
            {
                UpdateUserInfo(cachedState);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load cached Discord state");
        }
    }

    private async Task LoadDisplayNameFromServerAsync()
    {
        try
        {
            var tokens = _tokenStorage.RetrieveTokens<DiscordTokenData>();
            if (tokens == null) return;

            using var request = new HttpRequestMessage(HttpMethod.Get, "/user/profile-discord");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var profile = JsonConvert.DeserializeObject<DiscordUserProfile>(json);
                if (!string.IsNullOrEmpty(profile?.DisplayName))
                {
                    CustomDisplayName = profile.DisplayName;
                    var cachedState = _tokenStorage.RetrieveCachedState<DiscordCachedState>();
                    if (cachedState != null)
                    {
                        cachedState.CustomDisplayName = CustomDisplayName;
                        _tokenStorage.StoreCachedState(cachedState);
                    }
                    _logger?.LogInformation("Loaded display name from server: {Name}", CustomDisplayName);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load display name from server");
        }
    }

    private async Task SendBrowserResponseAsync(HttpListenerContext context, bool success)
    {
        var response = context.Response;
        var html = success
            ? @"<!DOCTYPE html>
<html><head><title>Discord Login Successful</title><style>
body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;background:linear-gradient(135deg,#5865F2 0%,#1a1a2e 100%);}
.container{text-align:center;color:white;}h1{color:#5865F2;background:white;padding:10px 20px;border-radius:8px;}p{color:#ccc;}
</style></head><body><div class='container'><h1>Discord Connected!</h1><p>You can close this window and return to the application.</p></div></body></html>"
            : @"<!DOCTYPE html>
<html><head><title>Discord Login Failed</title><style>
body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;background:linear-gradient(135deg,#1a1a2e 0%,#16213e 100%);}
.container{text-align:center;color:white;}h1{color:#ff4444;}p{color:#888;}
</style></head><body><div class='container'><h1>Login Failed</h1><p>Please try again from the application.</p></div></body></html>";

        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.Close();
    }

    private void StopCallbackListener()
    {
        try
        {
            _callbackListener?.Stop();
            _callbackListener?.Close();
        }
        catch { }
        finally
        {
            _callbackListener = null;
        }
    }

    private void NotifyPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        NotifyPropertyChanged(propertyName ?? string.Empty);
        return true;
    }

    private class DiscordUserProfile
    {
        [JsonProperty("display_name")]
        public string? DisplayName { get; set; }
    }
}
