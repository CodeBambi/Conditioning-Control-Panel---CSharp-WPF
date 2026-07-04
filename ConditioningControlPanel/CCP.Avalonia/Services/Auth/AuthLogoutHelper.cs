using System;
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Services.Auth;

/// <summary>
/// Single shared logout path for every VM logout entry point (AC-2).
/// Revokes every registered <see cref="IAuthProvider"/> (clearing OAuth tokens and cached
/// premium), clears the unified <c>AuthToken</c> (AC-1), and persists immediately.
/// Previously two of the three logout paths never called <see cref="IAuthProvider.Logout"/>,
/// leaving provider tokens and cached premium alive after a "logout".
/// </summary>
public static class AuthLogoutHelper
{
    /// <summary>
    /// Log out of every registered auth provider, clear the unified auth token, and save.
    /// Both arguments are tolerated as null so callers do not need to null-check first.
    /// </summary>
    public static void LogoutAll(IServiceProvider? services, ISettingsService? settings)
    {
        if (services != null)
        {
            foreach (var provider in services.GetServices<IAuthProvider>())
            {
                try
                {
                    provider.Logout();
                }
                catch
                {
                    // Best-effort: one provider failing must not block clearing the others.
                }
            }
        }

        var current = settings?.Current;
        if (current != null)
        {
            current.AuthToken = null;
            settings!.SaveImmediate();
        }
    }
}
