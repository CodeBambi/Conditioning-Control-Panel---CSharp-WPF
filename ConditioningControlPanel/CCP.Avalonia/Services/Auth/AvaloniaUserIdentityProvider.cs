using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.Services.Auth;

/// <summary>
/// Avalonia identity provider that surfaces the logged-in user's unified ID,
/// display name, and Discord ID from the active auth provider and settings.
/// </summary>
public sealed class AvaloniaUserIdentityProvider : IUserIdentityProvider
{
    private readonly IReadOnlyList<IAuthProvider> _authProviders;
    private readonly ISettingsService _settingsService;

    // R7: MS.DI resolves a single IAuthProvider to the LAST-registered one (SubscribeStar),
    // which is almost never the provider the user actually logged in with. Inject them all and
    // select the first that is logged in, falling back to persisted settings.
    public AvaloniaUserIdentityProvider(IEnumerable<IAuthProvider> authProviders, ISettingsService settingsService)
    {
        _authProviders = (authProviders ?? Enumerable.Empty<IAuthProvider>()).ToList();
        _settingsService = settingsService;
    }

    private IAuthProvider? ActiveProvider => _authProviders.FirstOrDefault(p => p.IsLoggedIn);

    public string? UnifiedUserId =>
        !string.IsNullOrEmpty(ActiveProvider?.UnifiedUserId)
            ? ActiveProvider!.UnifiedUserId
            : _settingsService.Current?.UnifiedId;

    public string? DisplayName =>
        !string.IsNullOrEmpty(ActiveProvider?.DisplayName)
            ? ActiveProvider!.DisplayName
            : _settingsService.Current?.UserDisplayName;

    // Avalonia auth providers do not currently track a separate Discord ID;
    // the leaderboard percentile fallback will match on unified ID or display name.
    public string? DiscordId => null;
}
