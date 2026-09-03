using System;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The secret-at-rest seam. The settings model keeps two values out of settings.json - the
    /// AI API key and the account token - and hands them to a store that encrypts them for the
    /// current user. On Windows that store is DPAPI (<c>ProtectedData</c>), which the Core
    /// guards forbid because it throws everywhere else. So the stores stay in the head and the
    /// model asks here, by name.
    ///
    /// <para><b>Unseeded means "no store", never "store in the clear".</b> A head that has not
    /// attached a provider gets null from <see cref="Retrieve"/> and a silent no-op from
    /// <see cref="Store"/>. The Linux head is in that state today: it has no secure store yet,
    /// so on Linux these two values are not persisted at all. That is the honest behaviour until
    /// a keyring-backed provider exists; writing them to disk unencrypted is not a fallback.</para>
    ///
    /// <para>Two delegates, no interface, matching <see cref="CoreMods"/>. Volatile for the same
    /// reason as there.</para>
    /// </summary>
    public static class CoreSecrets
    {
        /// <summary>Well-known names. The head maps each to its own store.</summary>
        public const string ApiKey = "apikey";
        public const string AuthToken = "authtoken";

        public static volatile Func<string, string?>? RetrieveProvider;
        public static volatile Action<string, string?>? StoreProvider;

        /// <summary>The stored value, or null when there is none or no store is attached.
        /// Faults are swallowed: a broken store must not take the settings model down.</summary>
        public static string? Retrieve(string name)
        {
            try { return RetrieveProvider?.Invoke(name); } catch { return null; }
        }

        /// <summary>Stores the value, or clears it when null. No-op with no store attached.</summary>
        public static void Store(string name, string? value)
        {
            try { StoreProvider?.Invoke(name, value); } catch { /* see Retrieve */ }
        }
    }
}
