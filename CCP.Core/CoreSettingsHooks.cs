using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// What the settings model tells the outside world, without knowing who listens. Today one
    /// thing: a property changed by name, which the WPF head forwards to the bark service so a
    /// companion line can react to a setting flip. The model used to call <c>App.Bark</c> for
    /// that directly, which is the one reason an 8,000-line pure data class was pinned to WPF.
    ///
    /// <para>Fired on every property set, so it is a bare volatile delegate and the invoke is
    /// wrapped: a slow or throwing listener must never break a settings write. Unseeded is the
    /// normal state under tests and during startup load, and means nobody is listening.</para>
    /// </summary>
    public static class CoreSettingsHooks
    {
        public static volatile Action<string?>? SettingChangedSink;

        public static void NotifySettingChanged(string? name)
        {
            try { SettingChangedSink?.Invoke(name); } catch { /* never break settings for a listener */ }
        }
    }
}
