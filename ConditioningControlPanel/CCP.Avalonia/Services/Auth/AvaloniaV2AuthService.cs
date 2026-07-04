using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Core.Services.Auth;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Update;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Services.Auth;

/// <summary>
/// Avalonia (Core-aware) implementation of the v2 authentication service.
/// Mirrors the legacy WPF <c>V2AuthService</c> behavior while using the cross-platform
/// <see cref="ISettingsService"/>, <see cref="IProgressionService"/>, <see cref="IUpdateService"/>,
/// and <see cref="ILogger{T}"/> seams.
/// </summary>
public sealed class AvaloniaV2AuthService : IV2AuthService
{
    private const string ServerUrl = "https://codebambi-proxy.vercel.app";

    private readonly HttpClient _http = new();
    private readonly IUpdateService _updateService;
    private readonly ISettingsService _settingsService;
    private readonly IProgressionService _progressionService;
    private readonly ILogger<AvaloniaV2AuthService> _logger;

    public AvaloniaV2AuthService(IUpdateService updateService, ISettingsService settingsService,
        IProgressionService progressionService, ILogger<AvaloniaV2AuthService> logger)
    {
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _progressionService = progressionService ?? throw new ArgumentNullException(nameof(progressionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Add("X-Client-Version", _updateService.CurrentVersion);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{_updateService.CurrentVersion}");
    }

    /// <summary>Redact auth_token and password values from JSON strings before logging.</summary>
    private static string RedactSensitiveFields(string json)
    {
        json = Regex.Replace(json, @"""auth_token""\s*:\s*""[^""]+""", @"""auth_token"":""[REDACTED]""");
        json = Regex.Replace(json, @"""access_token""\s*:\s*""[^""]+""", @"""access_token"":""[REDACTED]""");
        json = Regex.Replace(json, @"""password""\s*:\s*""[^""]+""", @"""password"":""[REDACTED]""");
        return json;
    }

    /// <summary>Safely extract error message from a response body that may not be JSON.</summary>
    private static string ParseErrorMessage(string body, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            var obj = JObject.Parse(body);
            return obj["error"]?.ToString() ?? $"HTTP {(int)statusCode}";
        }
        catch
        {
            return $"HTTP {(int)statusCode}";
        }
    }

    /// <inheritdoc />
    public async Task<V2AuthResponse> AuthenticateWithDiscordAsync(string accessToken, string? displayName = null)
    {
        try
        {
            var payload = new JObject { ["access_token"] = accessToken };
            if (!string.IsNullOrWhiteSpace(displayName))
                payload["display_name"] = displayName;

            var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ServerUrl}/v2/auth/discord", content);
            var json = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("[V2Auth] Discord auth response: {Json}", RedactSensitiveFields(json));

            if (!response.IsSuccessStatusCode)
                return new V2AuthResponse { Success = false, Error = ParseErrorMessage(json, response.StatusCode) };

            return JsonConvert.DeserializeObject<V2AuthResponse>(json)
                ?? new V2AuthResponse { Success = false, Error = "Invalid response" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[V2Auth] Discord auth failed");
            return new V2AuthResponse { Success = false, Error = ex.Message };
        }
    }

    /// <inheritdoc />
    public async Task<V2AuthResponse> AuthenticateWithPatreonAsync(string accessToken, string? displayName = null)
    {
        try
        {
            var payload = new JObject { ["access_token"] = accessToken };
            if (!string.IsNullOrWhiteSpace(displayName))
                payload["display_name"] = displayName;

            var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ServerUrl}/v2/auth/patreon", content);
            var json = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("[V2Auth] Patreon auth response: {Json}", RedactSensitiveFields(json));

            if (!response.IsSuccessStatusCode)
                return new V2AuthResponse { Success = false, Error = ParseErrorMessage(json, response.StatusCode) };

            return JsonConvert.DeserializeObject<V2AuthResponse>(json)
                ?? new V2AuthResponse { Success = false, Error = "Invalid response" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[V2Auth] Patreon auth failed");
            return new V2AuthResponse { Success = false, Error = ex.Message };
        }
    }

    /// <inheritdoc />
    public async Task<V2AuthResponse> AuthenticateWithSubstarAsync(string accessToken, string? displayName = null)
    {
        try
        {
            var payload = new JObject { ["access_token"] = accessToken };
            if (!string.IsNullOrWhiteSpace(displayName))
                payload["display_name"] = displayName;

            var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ServerUrl}/v2/auth/substar", content);
            var json = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("[V2Auth] SubscribeStar auth response: {Json}", RedactSensitiveFields(json));

            if (!response.IsSuccessStatusCode)
                return new V2AuthResponse { Success = false, Error = ParseErrorMessage(json, response.StatusCode) };

            return JsonConvert.DeserializeObject<V2AuthResponse>(json)
                ?? new V2AuthResponse { Success = false, Error = "Invalid response" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[V2Auth] SubscribeStar auth failed");
            return new V2AuthResponse { Success = false, Error = ex.Message };
        }
    }

    /// <inheritdoc />
    public async Task<V2AuthResponse> RegisterAsync(string inviteCode, string displayName, string password)
    {
        try
        {
            var payload = new JObject
            {
                ["invite_code"] = inviteCode,
                ["display_name"] = displayName,
                ["password"] = password
            };

            var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ServerUrl}/v2/auth/register", content);
            var json = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("[V2Auth] Register response: {Json}", RedactSensitiveFields(json));

            if (!response.IsSuccessStatusCode)
                return new V2AuthResponse { Success = false, Error = ParseErrorMessage(json, response.StatusCode) };

            return JsonConvert.DeserializeObject<V2AuthResponse>(json)
                ?? new V2AuthResponse { Success = false, Error = "Invalid response" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[V2Auth] Register failed");
            return new V2AuthResponse { Success = false, Error = "Registration failed. Please try again." };
        }
    }

    /// <inheritdoc />
    public async Task<V2AuthResponse> LoginAsync(string displayName, string password)
    {
        try
        {
            var payload = new JObject
            {
                ["display_name"] = displayName,
                ["password"] = password
            };

            var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync($"{ServerUrl}/v2/auth/login", content);
            var json = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("[V2Auth] Login response: {Json}", RedactSensitiveFields(json));

            if (!response.IsSuccessStatusCode)
                return new V2AuthResponse { Success = false, Error = ParseErrorMessage(json, response.StatusCode) };

            return JsonConvert.DeserializeObject<V2AuthResponse>(json)
                ?? new V2AuthResponse { Success = false, Error = "Invalid response" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[V2Auth] Login failed");
            return new V2AuthResponse { Success = false, Error = "Login failed. Please try again." };
        }
    }

    /// <inheritdoc />
    public async Task<LinkResponse> LinkProviderAsync(string unifiedId, string provider, string accessToken)
    {
        try
        {
            var payload = new JObject
            {
                ["unified_id"] = unifiedId,
                ["provider"] = provider,
                ["access_token"] = accessToken
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/v2/auth/link")
            {
                Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json")
            };
            AddAuthHeader(request);
            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            _logger.LogDebug("[V2Auth] Link response: {Json}", RedactSensitiveFields(json));

            if (!response.IsSuccessStatusCode)
                return new LinkResponse { Success = false, Error = ParseErrorMessage(json, response.StatusCode) };

            return JsonConvert.DeserializeObject<LinkResponse>(json)
                ?? new LinkResponse { Success = false, Error = "Invalid response" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[V2Auth] Link provider failed");
            return new LinkResponse { Success = false, Error = ex.Message };
        }
    }

    /// <inheritdoc />
    public async Task<V2User?> GetUserProfileAsync(string unifiedId)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{ServerUrl}/v2/user/profile?unified_id={Uri.EscapeDataString(unifiedId)}");
            AddAuthHeader(request);
            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[V2Auth] Get profile failed: {Json}", RedactSensitiveFields(json));
                return null;
            }

            var result = JObject.Parse(json);
            return result["user"]?.ToObject<V2User>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[V2Auth] Get profile failed");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateUserProfileAsync(string unifiedId, int? xp = null, int? level = null,
        JObject? stats = null, string[]? achievements = null)
    {
        try
        {
            var payload = new JObject { ["unified_id"] = unifiedId };

            if (xp.HasValue) payload["xp"] = xp.Value;
            if (level.HasValue) payload["level"] = level.Value;
            if (stats != null) payload["stats"] = stats;
            if (achievements != null) payload["achievements"] = new JArray(achievements);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/v2/user/update");
            AddAuthHeader(request);
            request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var response = await _http.SendAsync(request);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[V2Auth] Update profile failed");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SendHeartbeatAsync(string unifiedId)
    {
        try
        {
            var payload = new JObject { ["unified_id"] = unifiedId };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/v2/user/heartbeat");
            AddAuthHeader(request);
            request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var response = await _http.SendAsync(request);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAccountAsync(string unifiedId)
    {
        try
        {
            var payload = new JObject
            {
                ["unified_id"] = unifiedId,
                ["confirmation"] = "DELETE"
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/v2/user/delete-account");
            AddAuthHeader(request);
            request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var response = await _http.SendAsync(request);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[V2Auth] Delete account failed");
            return false;
        }
    }

    /// <summary>
    /// Adds the X-Auth-Token header to a V2 API request if an auth token is available.
    /// </summary>
    private void AddAuthHeader(HttpRequestMessage request)
    {
        var token = _settingsService.Current?.AuthToken;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Add("X-Auth-Token", token);
    }

    /// <inheritdoc />
    public void ApplyUserDataToSettings(V2User user, string? authToken = null)
    {
        var settings = _settingsService.Current;
        if (settings == null) return;

        settings.UnifiedId = user.UnifiedId;
        settings.UserDisplayName = user.DisplayName;
        settings.IsSeason0Og = user.IsSeason0Og;
        settings.CurrentSeason = user.CurrentSeason;
        settings.HighestLevelEver = user.HighestLevelEver;
        settings.HasLinkedDiscord = !string.IsNullOrEmpty(user.DiscordId);
        settings.HasLinkedPatreon = !string.IsNullOrEmpty(user.PatreonId);
        settings.PatreonTier = user.PatreonTier;

        // AC-3 (#465): Discord/unified-login users with a LINKED Patreon sub have no local
        // Patreon tokens, so PatreonProvider.CurrentTier stays None and nothing else refreshes
        // the cached-premium window for them. A server-confirmed linked tier must keep premium
        // alive here, mirroring the 2-week grace that direct Patreon validation writes. Never
        // shorten an existing longer window.
        if (user.PatreonTier >= 1)
        {
            var until = DateTime.UtcNow.AddDays(14);
            if (settings.PatreonPremiumValidUntil == null || settings.PatreonPremiumValidUntil < until)
                settings.PatreonPremiumValidUntil = until;
        }

        if (!string.IsNullOrEmpty(authToken))
            settings.AuthToken = authToken;

        // Sync level/XP using "take higher" logic to prevent progress loss.
        // Server returns TOTAL accumulated XP, but PlayerXP stores current-level XP.
        if (user.Level > 0)
        {
            var localTotalXp = _progressionService.GetTotalXP(settings.PlayerLevel, settings.PlayerXP);
            var serverTotalXp = (double)user.Xp;

            if (serverTotalXp >= localTotalXp)
            {
                settings.PlayerLevel = user.Level;
                settings.PlayerXP = _progressionService.GetCurrentLevelXP(user.Level, user.Xp);
            }
            else
            {
                _logger.LogInformation("[V2Auth] Keeping local progress (higher): Local Level {LocalLevel} ({LocalXP} total) > Server Level {ServerLevel} ({ServerXP} total)",
                    settings.PlayerLevel, (int)localTotalXp, user.Level, user.Xp);
            }
        }

        _settingsService.Save();
    }
}
