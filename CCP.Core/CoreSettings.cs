using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The settings seam. Engine code that needs the live <see cref="AppSettings"/> reads it here
    /// rather than through <c>App.Settings.Current</c>, which lives on the WPF <c>App</c>.
    ///
    /// <para><see cref="Current"/> is never null. With no head attached it is one shared default
    /// instance, which is the honest "nothing loaded" state under tests, the Linux smoke runner
    /// and startup before the settings service exists: every property reads its default and a
    /// write lands in an object nobody persists. <see cref="HasProvider"/> says which case you
    /// are in, for the rare caller that must not write into the void.</para>
    ///
    /// <para>One delegate, no interface, matching <see cref="CoreMods"/>. Volatile for the same
    /// reason as there. A head seeds <see cref="CurrentProvider"/> once its settings service is
    /// up; the delegate is read on every access, so a reload that swaps the instance is seen.</para>
    /// </summary>
    public static class CoreSettings
    {
        public static volatile Func<AppSettings?>? CurrentProvider;

        private static readonly Lazy<AppSettings> Fallback = new(() => new AppSettings());

        public static bool HasProvider => CurrentProvider is not null;

        /// <summary>The live settings, or the shared default instance when no head has seeded one.
        /// Faults in the provider fall back the same way: settings must never take a caller down.</summary>
        public static AppSettings Current
        {
            get
            {
                try { return CurrentProvider?.Invoke() ?? Fallback.Value; }
                catch { return Fallback.Value; }
            }
        }
    }
}
