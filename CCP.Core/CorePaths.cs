using System.IO;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The two filesystem roots Core code needs, without reaching into the WPF <c>App</c>.
    ///
    /// <para><see cref="UserData"/> resolves itself — it is immutable for the process lifetime and
    /// already platform-neutral (LocalApplicationData maps to ~/.local/share on Linux), so nothing
    /// has to seed it and there is no initialization-order hazard.</para>
    ///
    /// <para><see cref="EffectiveAssets"/> is NOT immutable: it changes when the user picks a custom
    /// assets folder in Settings, so it must stay a live delegate, never a snapshot. The WPF head
    /// seeds <see cref="EffectiveAssetsProvider"/> in <c>App</c>'s static constructor; a future
    /// Avalonia/VR head seeds the same hook with its own resolution.</para>
    /// </summary>
    public static class CorePaths
    {
        /// <summary>
        /// User data folder in LocalAppData - persists across updates.
        /// CCP_USERDATA_DIR redirects the whole tree (settings, logs, content, mods) so test
        /// harnesses can run against a sandbox instead of the real profile; same env-hook
        /// pattern as the CCP_STRESS_* knobs.
        /// </summary>
        public static string UserData { get; } = ResolveUserData();

        private static string ResolveUserData()
        {
            try
            {
                var overrideDir = Environment.GetEnvironmentVariable("CCP_USERDATA_DIR");
                if (!string.IsNullOrWhiteSpace(overrideDir) && Path.IsPathRooted(overrideDir))
                    return overrideDir;
            }
            catch { }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ConditioningControlPanel");
        }

        /// <summary>
        /// Supplied by the host head. Invoked on every <see cref="EffectiveAssets"/> read on purpose:
        /// the answer changes at runtime, so caching it here would serve a stale folder the moment
        /// the user repoints Settings.CustomAssetsPath. volatile because the host seeds it on the
        /// startup thread and background readers never touch the host's type initializer.
        /// </summary>
        public static volatile Func<string>? EffectiveAssetsProvider;

        /// <summary>
        /// Effective assets folder - the user's custom folder when one is configured and present,
        /// otherwise the default under <see cref="UserData"/>. Use this for all asset loading.
        /// The fallback is exactly what the host returns before it has settings loaded.
        /// Not free: the host's getter stats the custom folder on every read, so hoist it into a
        /// local inside a loop rather than re-reading it per item.
        /// </summary>
        public static string EffectiveAssets =>
            EffectiveAssetsProvider?.Invoke() ?? Path.Combine(UserData, "assets");
    }
}
