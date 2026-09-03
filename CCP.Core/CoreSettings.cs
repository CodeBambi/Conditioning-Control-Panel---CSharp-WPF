using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The settings seam. Engine code and ported views reach the live settings here rather than
    /// through <c>App.Settings</c>, which lives on each head's <c>App</c> class.
    ///
    /// <para>Two things a caller wants, and this exposes exactly those: <see cref="Current"/> to
    /// read and write the model, and <see cref="Save"/> / <see cref="SaveImmediate"/> to persist
    /// it, which is the <c>App.Settings?.Current</c> + <c>App.Settings?.Save()</c> pair every WPF
    /// code-behind already uses. A view ports that pair one for one.</para>
    ///
    /// <para><see cref="Current"/> is never null. With no head attached it is one shared default
    /// instance: the honest "nothing loaded" state under tests, the Linux smoke runner and the
    /// headless render path. Saves are then no-ops, because there is no file to save to, and
    /// <see cref="HasProvider"/> says which case you are in for the rare caller that must not
    /// write into the void.</para>
    ///
    /// <para>One delegate, no interface, matching <see cref="CoreMods"/>. Volatile for the same
    /// reason as there. A head seeds <see cref="ServiceProvider"/> once its settings service is
    /// up; the delegate is read on every access, so a reload that swaps the instance is seen.</para>
    /// </summary>
    public static class CoreSettings
    {
        public static volatile Func<SettingsService?>? ServiceProvider;

        private static readonly Lazy<AppSettings> Fallback = new(() => new AppSettings());

        public static bool HasProvider => ServiceProvider is not null;

        /// <summary>The head's settings service, or null with no head attached. Prefer
        /// <see cref="Current"/> and <see cref="Save"/>; this is for the flags the service
        /// carries (restored-from-backup, missing file) that a view occasionally shows.</summary>
        public static SettingsService? Service
        {
            get { try { return ServiceProvider?.Invoke(); } catch { return null; } }
        }

        /// <summary>The live settings, or the shared default instance when no head has seeded a
        /// service. Faults in the provider fall back the same way: settings must never take a
        /// caller down.</summary>
        public static AppSettings Current => Service?.Current ?? Fallback.Value;

        /// <summary>Debounced save, as <c>App.Settings?.Save()</c> was. No-op with no service.</summary>
        public static void Save(bool suppressCloudBackup = false)
        {
            try { Service?.Save(suppressCloudBackup); } catch { /* the service logs; a view must not die on a failed save */ }
        }

        /// <summary>Synchronous save, as <c>App.Settings?.SaveImmediate()</c> was. No-op with no service.</summary>
        public static void SaveImmediate(bool suppressCloudBackup = false)
        {
            try { Service?.SaveImmediate(suppressCloudBackup); } catch { /* see Save */ }
        }
    }
}
