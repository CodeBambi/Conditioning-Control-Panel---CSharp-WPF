using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Core.Services.Auth;

/// <summary>
/// Cross-platform seam for v2 authentication using the v2 API endpoints.
/// Handles monthly seasons system with OG recognition.
/// </summary>
public interface IV2AuthService
{
    /// <summary>
    /// Authenticate with Discord using the v2 API.
    /// </summary>
    Task<V2AuthResponse> AuthenticateWithDiscordAsync(string accessToken, string? displayName = null);

    /// <summary>
    /// Authenticate with Patreon using the v2 API.
    /// </summary>
    Task<V2AuthResponse> AuthenticateWithPatreonAsync(string accessToken, string? displayName = null);

    /// <summary>
    /// Authenticate with SubscribeStar using the v2 API.
    /// </summary>
    Task<V2AuthResponse> AuthenticateWithSubstarAsync(string accessToken, string? displayName = null);

    /// <summary>
    /// Register a new account with invite code, display name, and password.
    /// </summary>
    Task<V2AuthResponse> RegisterAsync(string inviteCode, string displayName, string password);

    /// <summary>
    /// Login with display name and password.
    /// </summary>
    Task<V2AuthResponse> LoginAsync(string displayName, string password);

    /// <summary>
    /// Link a second provider to an existing unified account.
    /// </summary>
    Task<LinkResponse> LinkProviderAsync(string unifiedId, string provider, string accessToken);

    /// <summary>
    /// Get user profile from the v2 API.
    /// </summary>
    Task<V2User?> GetUserProfileAsync(string unifiedId);

    /// <summary>
    /// Update user profile (XP, level, stats, achievements).
    /// </summary>
    Task<bool> UpdateUserProfileAsync(string unifiedId, int? xp = null, int? level = null,
        JObject? stats = null, string[]? achievements = null);

    /// <summary>
    /// Delete user account (GDPR).
    /// </summary>
    Task<bool> DeleteAccountAsync(string unifiedId);

    /// <summary>
    /// Apply v2 user data to local settings, optionally storing an auth token.
    /// </summary>
    void ApplyUserDataToSettings(V2User user, string? authToken = null);
}
