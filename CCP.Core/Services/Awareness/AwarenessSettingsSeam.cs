using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// WPF read <c>App.Settings?.Current</c>, which is null headlessly and during early startup, and the
    /// observer family fails closed on that null. <see cref="CoreSettings.Current"/> hands back a
    /// fallback object instead, so this seam keeps the WPF null: no settings provider, no settings.
    /// </summary>
    internal static class AwarenessSettingsSeam
    {
        public static AppSettings? Current =>
            CoreSettings.HasProvider ? CoreSettings.Service?.Current : null;
    }
}
