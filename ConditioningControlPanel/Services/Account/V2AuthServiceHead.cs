using System;
using static ConditioningControlPanel.Services.V2AuthService;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The WPF call-site shim for Core <see cref="ProfileAdopt.ApplyUserData"/>: an extension so
    /// every <c>v2Auth.ApplyUserDataToSettings(...)</c> call site is unchanged. Saves through App.Settings.
    /// </summary>
    public static class V2AuthServiceHead
    {
        /// <summary>
        /// Apply v2 user data to local settings, optionally storing an auth token.
        /// </summary>
        public static void ApplyUserDataToSettings(this V2AuthService _, V2User user, string? authToken = null)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;
            ProfileAdopt.ApplyUserData(settings, user, authToken, DateTime.UtcNow);
            App.Settings?.Save();
        }
    }
}
