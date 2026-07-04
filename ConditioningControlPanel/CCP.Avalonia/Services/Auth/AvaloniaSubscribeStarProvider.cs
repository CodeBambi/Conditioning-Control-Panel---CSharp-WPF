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
/// Avalonia port of the WPF <see cref="ConditioningControlPanel.Services.SubscribeStarService"/>.
/// Handles SubscribeStar OAuth via proxy-bridged redirect, token storage, subscription validation,
/// and premium gating using the shared Patreon tier scale.
/// </summary>
public sealed class AvaloniaSubscribeStarProvider : IAuthProvider, INotifyPropertyChanged, IDisposable
{
    private readonly AvaloniaTokenStorage _tokenStorage;
    private readonly ISettingsService _settingsService;
    private readonly IBrowserHost _browserHost;
    private readonly IDialogService _dialogService;
    private readonly ILogger<AvaloniaSubscribeStarProvider>? _logger;
    private readonly HttpClient _httpClient;

    private HttpListener? _callbackListener;
    private CancellationTokenSource? _oauthCts;
    private bool _disposed;
    private bool _isVerifying;
    private PatreonTier _currentTier = PatreonTier.None;
    private bool _isWhitelisted;
    private string? _displayName;
    private string? _unifiedUserId;

    public event EventHandler<PatreonTier>? TierChanged;
    public event EventHandler<string>? AuthenticationFailed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProviderName => "substar";

    public bool IsLoggedIn => _tokenStorage.HasValidTokens<PatreonTokenData>();

    public bool IsAuthenticated => IsLoggedIn;

    public bool IsVerifying
    {
        get => _isVerifying;
        private set => SetProperty(ref _isVerifying, value);
    }

    public PatreonTier CurrentTier
    {
        get => _currentTier;
        private set
        {
            var old = _currentTier;
            if (SetProperty(ref _currentTier, value) && old != value)
                TierChanged?.Invoke(this, value);
        }
    }

    public bool IsActiveSubscriber { get; private set; }

    public bool IsWhitelisted => _isWhitelisted;

    public bool HasPremiumAccess => CurrentTier >= PatreonTier.Level1 || _isWhitelisted;

    public string? DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string? UnifiedUserId
    {
        get => _unifiedUserId;
        set => SetProperty(ref _unifiedUserId, value);
    }

    public AvaloniaSubscribeStarProvider(
        ISecretStore secretStore,
        ISettingsService settingsService,
        IBrowserHost browserHost,
        IDialogService dialogService,
        ILogger<AvaloniaSubscribeStarProvider>? logger = null,
        ILogger<AvaloniaTokenStorage>? tokenStorageLogger = null)
    {
        _tokenStorage = new AvaloniaTokenStorage(secretStore, "substar", tokenStorageLogger);
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
                _logger?.LogInformation("Offline mode enabled, using cached SubscribeStar state only");
                LoadCachedState();
                return;
            }

            if (_tokenStorage.HasValidTokens<PatreonTokenData>())
                await ValidateSubscriptionAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to validate SubscribeStar subscription on startup");
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
            var callbackUrl = $"http://localhost:{AuthConstants.SubscribeStarCallbackPort}/callback/";
            _callbackListener.Prefixes.Add(callbackUrl);
            _callbackListener.Start();

            _logger?.LogInformation("Started SubscribeStar OAuth callback listener on {Url}", callbackUrl);

            var authUrl = $"{AuthConstants.ProxyBaseUrl}/substar/authorize?state={state}";
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
                "sign in with SubscribeStar");

            var getContextTask = _callbackListener.GetContextAsync();
            _ = getContextTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(AuthConstants.OAuthTimeoutMinutes), _oauthCts.Token);

            var completedTask = await Task.WhenAny(getContextTask, timeoutTask);
            if (completedTask == timeoutTask)
                throw new TimeoutException("SubscribeStar login timed out. Please try again.");

            var context = await getContextTask;
            var query = context.Request.QueryString;
            var returnedState = query["state"];
            var error = query["error"];

            await SendBrowserResponseAsync(context, string.IsNullOrEmpty(error));

            if (!AuthSecurityHelper.SecureCompare(state, returnedState ?? ""))
                throw new SecurityException("OAuth state mismatch - possible CSRF attack");

            if (!string.IsNullOrEmpty(error))
                throw new Exception($"SubscribeStar authorization failed: {error}");

            await ExchangeViaProxyAsync(state);
            await ValidateSubscriptionAsync(forceRefresh: true);

            _logger?.LogInformation("SubscribeStar OAuth flow completed successfully");
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("SubscribeStar OAuth flow cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SubscribeStar OAuth flow failed");
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

    public async Task<PatreonTier> ValidateSubscriptionAsync(bool forceRefresh = false)
    {
        if (IsVerifying && !forceRefresh) return CurrentTier;

        var settings = _settingsService.Current;
        if (settings.OfflineMode)
        {
            _logger?.LogDebug("Offline mode enabled, skipping SubscribeStar validation");
            return CurrentTier;
        }

        try
        {
            if (!forceRefresh)
            {
                var cachedState = _tokenStorage.RetrieveCachedState<PatreonCachedState>();
                if (cachedState != null && !cachedState.IsExpired)
                {
                    _isWhitelisted = cachedState.IsWhitelisted;
                    var cachedEffectiveTier = cachedState.IsWhitelisted && cachedState.Tier == PatreonTier.None
                        ? PatreonTier.Level2
                        : cachedState.Tier;
                    var cachedEffectivelyActive = cachedState.IsActive || cachedState.IsWhitelisted;
                    UpdateTier(cachedEffectiveTier, cachedEffectivelyActive, cachedState.DisplayName);
                    SetUnifiedIdIfEmpty(cachedState.UnifiedId);
                    return CurrentTier;
                }
            }

            var tokens = _tokenStorage.RetrieveTokens<PatreonTokenData>();
            if (tokens == null)
            {
                UpdateTier(PatreonTier.None, false);
                return PatreonTier.None;
            }

            if (tokens.IsExpired)
            {
                var refreshed = await RefreshTokensAsync(tokens.RefreshToken);
                if (!refreshed)
                {
                    UpdateTier(PatreonTier.None, false);
                    return PatreonTier.None;
                }
                tokens = _tokenStorage.RetrieveTokens<PatreonTokenData>();
                if (tokens == null)
                {
                    UpdateTier(PatreonTier.None, false);
                    return PatreonTier.None;
                }
            }

            IsVerifying = true;

            using var validateRequest = new HttpRequestMessage(HttpMethod.Post, "/substar/validate");
            validateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            var currentAuthToken = settings.AuthToken;
            if (!string.IsNullOrEmpty(currentAuthToken))
                validateRequest.Headers.Add("X-Auth-Token", currentAuthToken);

            var response = await _httpClient.SendAsync(validateRequest);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var refreshed = await RefreshTokensAsync(tokens.RefreshToken);
                if (refreshed)
                    return await ValidateSubscriptionAsync(forceRefresh: true);

                _tokenStorage.ClearTokens();
                UpdateTier(PatreonTier.None, false);
                return PatreonTier.None;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("SubscribeStar validation failed with status {Status}", response.StatusCode);
                return CurrentTier;
            }

            var json = await response.Content.ReadAsStringAsync();
            var subscription = JsonConvert.DeserializeObject<PatreonSubscriptionResponse>(json);

            if (subscription == null || !string.IsNullOrEmpty(subscription.Error))
            {
                _logger?.LogWarning("SubscribeStar validation error: {Error}", subscription?.Error);
                return CurrentTier;
            }

            if (!string.IsNullOrEmpty(subscription.AuthToken))
            {
                settings.AuthToken = subscription.AuthToken;
                _settingsService.SaveImmediate();
                _logger?.LogInformation("[Auth] Stored re-issued auth token from SubscribeStar validate (token recovery)");
            }

            var userIsWhitelisted = subscription.IsWhitelisted;
            _isWhitelisted = userIsWhitelisted;

            SetUnifiedIdIfEmpty(subscription.UnifiedId);

            var effectivelyActive = subscription.IsActive || userIsWhitelisted;
            var newTier = effectivelyActive
                ? (subscription.Tier > PatreonTier.None ? subscription.Tier : (userIsWhitelisted ? PatreonTier.Level2 : PatreonTier.Level1))
                : PatreonTier.None;
            UpdateTier(newTier, effectivelyActive);

            var existingCache = _tokenStorage.RetrieveCachedState<PatreonCachedState>();
            var serverDisplayName = subscription.DisplayName;
            var effectiveDisplayName = !string.IsNullOrEmpty(serverDisplayName)
                ? serverDisplayName
                : (existingCache?.DisplayName ?? DisplayName);
            if (!string.IsNullOrEmpty(serverDisplayName))
            {
                DisplayName = serverDisplayName;
                settings.UserDisplayName = serverDisplayName;
            }

            _tokenStorage.StoreCachedState(new PatreonCachedState
            {
                Tier = newTier,
                IsActive = effectivelyActive,
                LastVerified = DateTime.UtcNow,
                CacheExpiresAt = DateTime.UtcNow.AddHours(AuthConstants.CacheHours),
                DisplayName = effectiveDisplayName,
                IsWhitelisted = userIsWhitelisted,
                UnifiedId = subscription.UnifiedId
            });

            if ((newTier >= PatreonTier.Level1 || userIsWhitelisted) && settings != null)
            {
                settings.PatreonPremiumValidUntil = DateTime.UtcNow.AddDays(14);
                _settingsService.SaveImmediate();
            }

            _logger?.LogInformation("SubscribeStar subscription validated: Tier={Tier}, Active={Active}, Whitelisted={Whitelisted}",
                newTier, effectivelyActive, userIsWhitelisted);

            return newTier;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to validate SubscribeStar subscription");
            return CurrentTier;
        }
        finally
        {
            IsVerifying = false;
        }
    }

    public string? GetAccessToken()
    {
        var tokens = _tokenStorage.RetrieveTokens<PatreonTokenData>();
        return tokens?.AccessToken;
    }

    public void Logout()
    {
        _tokenStorage.ClearTokens();
        _tokenStorage.ClearCachedState();
        DisplayName = null;
        _isWhitelisted = false;
        UnifiedUserId = null;

        // AC-5: SubscribeStar validation writes the shared cached-premium window and tier scale
        // (see ValidateSubscriptionAsync). Clear them on logout so premium access does not
        // survive, mirroring AvaloniaPatreonProvider.Logout.
        var settings = _settingsService.Current;
        settings.PatreonPremiumValidUntil = null;
        settings.PatreonTier = 0;
        _settingsService.SaveImmediate();

        UpdateTier(PatreonTier.None, false);
        _logger?.LogInformation("SubscribeStar logout completed, cached premium cleared");
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

    private async Task ExchangeViaProxyAsync(string state)
    {
        var payload = new { state };
        var response = await _httpClient.PostAsync("/substar/exchange",
            new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            throw new Exception($"Token exchange failed: {response.StatusCode} - {errorText}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonConvert.DeserializeObject<PatreonTokenResponse>(json);

        if (tokenResponse == null || !string.IsNullOrEmpty(tokenResponse.Error))
            throw new Exception($"Token exchange failed: {tokenResponse?.ErrorDescription ?? "Unknown error"}");

        // R12: a 200 with an empty/missing access token must never persist null tokens.
        if (string.IsNullOrEmpty(tokenResponse.AccessToken))
            throw new InvalidOperationException("SubscribeStar token exchange returned an empty access token.");

        _tokenStorage.StoreTokens(new PatreonTokenData
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
        });

        _logger?.LogInformation("SubscribeStar tokens stored successfully");
    }

    private async Task<bool> RefreshTokensAsync(string refreshToken)
    {
        try
        {
            var payload = new { refresh_token = refreshToken };
            var response = await _httpClient.PostAsync("/substar/refresh",
                new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("SubscribeStar token refresh failed with status {Status}", response.StatusCode);
                return false;
            }

            var json = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonConvert.DeserializeObject<PatreonTokenResponse>(json);

            if (tokenResponse == null || !string.IsNullOrEmpty(tokenResponse.Error))
            {
                _logger?.LogWarning("SubscribeStar token refresh error: {Error}", tokenResponse?.ErrorDescription);
                return false;
            }

            // R12: reject a 200 with an empty/missing access token so we never persist null tokens.
            if (string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                _logger?.LogWarning("SubscribeStar token refresh returned an empty access token");
                return false;
            }

            _tokenStorage.StoreTokens(new PatreonTokenData
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
            });

            _logger?.LogInformation("SubscribeStar tokens refreshed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to refresh SubscribeStar tokens");
            return false;
        }
    }

    private void UpdateTier(PatreonTier tier, bool isActive, string? displayName = null)
    {
        CurrentTier = tier;
        IsActiveSubscriber = isActive;
        if (displayName != null)
            DisplayName = displayName;

        if (_isWhitelisted)
            _logger?.LogInformation("SubscribeStar user is whitelisted - granting premium access");

        NotifyPropertyChanged(nameof(HasPremiumAccess));
    }

    private void SetUnifiedIdIfEmpty(string? unifiedId)
    {
        if (string.IsNullOrEmpty(unifiedId)) return;

        UnifiedUserId = unifiedId;
        var settings = _settingsService.Current;
        if (string.IsNullOrEmpty(settings.UnifiedId))
        {
            settings.UnifiedId = unifiedId;
            _logger?.LogInformation("Set UnifiedUserId from SubscribeStar validate: {UnifiedId}", unifiedId);
        }
    }

    private void LoadCachedState()
    {
        try
        {
            var cachedState = _tokenStorage.RetrieveCachedState<PatreonCachedState>();
            if (cachedState != null && !cachedState.IsExpired && _tokenStorage.HasValidTokens<PatreonTokenData>())
            {
                _isWhitelisted = cachedState.IsWhitelisted;
                var effectivelyActive = cachedState.IsActive || cachedState.IsWhitelisted;
                CurrentTier = effectivelyActive && cachedState.Tier == PatreonTier.None
                    ? (cachedState.IsWhitelisted ? PatreonTier.Level2 : PatreonTier.Level1)
                    : cachedState.Tier;
                IsActiveSubscriber = effectivelyActive;
                DisplayName = cachedState.DisplayName;
                UnifiedUserId = cachedState.UnifiedId;

                if (!string.IsNullOrEmpty(cachedState.UnifiedId))
                {
                    var settings = _settingsService.Current;
                    if (string.IsNullOrEmpty(settings.UnifiedId))
                        settings.UnifiedId = cachedState.UnifiedId;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load cached SubscribeStar state");
        }
    }

    private async Task SendBrowserResponseAsync(HttpListenerContext context, bool success)
    {
        var response = context.Response;
        var html = success
            ? @"<!DOCTYPE html>
<html><head><title>Login Successful</title><style>
body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;background:linear-gradient(135deg,#1a1a2e 0%,#16213e 100%);}
.container{text-align:center;color:white;}h1{color:#ff69b4;}p{color:#888;}
</style></head><body><div class='container'><h1>Login Successful!</h1><p>You can close this window and return to the application.</p></div></body></html>"
            : @"<!DOCTYPE html>
<html><head><title>Login Failed</title><style>
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
}
