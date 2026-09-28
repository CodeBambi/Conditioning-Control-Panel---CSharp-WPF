using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The account seam: who is signed in, which bar they clear, and the two destructive
    /// self-service operations on that identity.
    ///
    /// <para>Three head-only services stand behind it and none of them can move.
    /// <c>PatreonService</c> owns an <c>HttpListener</c> on port 47832 for the OAuth callback plus
    /// a <c>SecureTokenStorage</c>; <c>AccountService</c> takes a <c>System.Windows.Window</c>
    /// owner and drives <c>App.Patreon</c>/<c>App.Discord</c>; <c>ProfileSyncService</c> is a
    /// 5,000-line HTTP client with a WPF <c>DispatcherTimer</c> heartbeat. A socket, a window and a
    /// device-bound secret store are exactly the three things that stay in a head, so only the
    /// ANSWERS cross here - never the transport.</para>
    /// <para>Superseded in part (decisions log, "Where OAuth and the V2 client live"): the V2
    /// client is now Core's <c>V2AuthService</c>, and the OAuth listener and code exchange are Core's
    /// <c>LoopbackOAuth</c>, which the WPF provider services delegate to; the Patreon/SubscribeStar
    /// token lifecycle is Core's <c>ProviderSubscription</c>, from which the Avalonia head seeds
    /// this (CCP.Avalonia/Platform/AccountSeed.cs), fail closed.</para>
    ///
    /// <para><b>Unseeded means signed out and NOT ENTITLED, and that is not merely the safe answer
    /// - it is the truth.</b> A head that seeds nothing here has no OAuth flow, and the Avalonia
    /// head also seeds no <c>CoreSecrets</c> store, so there is no token, no session and no tier to
    /// report. Every entitlement property therefore fails CLOSED, including when a seeded provider
    /// throws: an exception means "I could not determine your tier", which must never be read as
    /// "yes". Failing open here would hand every Linux user the paid tier.</para>
    ///
    /// <para>Deliberately NOT exposed, because no ported view asks for it and each is one line for
    /// whoever needs it: <c>HasAiAccess</c>, the raw <c>PatreonTier</c>, <c>EntitlementResolved</c>,
    /// <c>ExportDataAsync</c>. If <c>EntitlementResolved</c> is ever added, unseeded MUST answer
    /// false - a true there beside a false <see cref="HasLabAccess"/> is #1048, the destructive
    /// entitlement repair that cleared a saved setting on every single launch.</para>
    /// </summary>
    public static class CoreAccount
    {
        /// <summary>The unified user id linking the providers for this session (WPF: <c>App.UnifiedUserId</c>,
        /// which reads and writes this). The provider lifecycle adopts it only when it is empty.
        /// A change raises <see cref="UnifiedIdentityChanged"/>; one failing observer never stops the rest.</summary>
        public static string? UnifiedUserId
        {
            get => Volatile.Read(ref _unifiedUserId);
            set
            {
                if (string.Equals(Interlocked.Exchange(ref _unifiedUserId, value), value, StringComparison.Ordinal)) return;
                foreach (EventHandler handler in UnifiedIdentityChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
                {
                    try { handler(null, EventArgs.Empty); }
                    catch (Exception ex) { Log.Debug("Identity observer failed ({Kind})", ex.GetType().Name); }
                }
            }
        }
        private static string? _unifiedUserId;

        /// <summary>The unified identity changed (sign-in, sign-out, account switch). WPF: <c>App.UnifiedIdentityChanged</c>.</summary>
        public static event EventHandler? UnifiedIdentityChanged;

        public static volatile Func<bool>? IsLoggedInProvider;
        public static volatile Func<string?>? DisplayNameProvider;
        public static volatile Func<bool>? IsWhitelistedProvider;
        public static volatile Func<bool>? HasPremiumAccessProvider;
        public static volatile Func<bool>? HasLabAccessProvider;

        /// <summary>
        /// Rename this account on the server. Answers with the name the SERVER settled on, which is
        /// not always the one that was asked for.
        /// </summary>
        public static volatile Func<string, Task<(bool success, string? error, string? newName)>?>? ChangeDisplayNameProvider;

        /// <summary>Delete this account and all its server-side data (GDPR). Irreversible.</summary>
        public static volatile Func<Task<(bool success, string? error)>?>? DeleteAccountProvider;

        /// <summary>Signed in with any provider, or holding a restored cloud identity.</summary>
        public static bool IsLoggedIn => Ask(IsLoggedInProvider);

        /// <summary>The name to show for this user, or null when there is none to show.</summary>
        public static string? DisplayName
        {
            get { try { return DisplayNameProvider?.Invoke(); } catch { return null; } }
        }

        /// <summary>Server-side whitelist: full access regardless of subscription.</summary>
        public static bool IsWhitelisted => Ask(IsWhitelistedProvider);

        /// <summary>The canonical premium (tier 1) gate, offline grace and SubscribeStar folded in.</summary>
        public static bool HasPremiumAccess => Ask(HasPremiumAccessProvider);

        /// <summary>
        /// The canonical Lab (tier 2) gate - the goon-game HOST bar. Advisory: the server refuses
        /// on its own, and this exists so the UI can say so before the round-trip instead of after.
        /// </summary>
        public static bool HasLabAccess => Ask(HasLabAccessProvider);

        /// <summary>
        /// Rename the account. Unseeded - and on an absent or throwing provider - this is a refusal
        /// carrying the head's own precondition message, never a silent success: the caller must
        /// not go on to write the new name locally.
        /// </summary>
        public static async Task<(bool success, string? error, string? newName)> ChangeDisplayNameAsync(string newName)
        {
            var provider = ChangeDisplayNameProvider;
            if (provider == null) return (false, NotSignedIn, null);
            try
            {
                var task = provider(newName);
                if (task == null) return (false, NotSignedIn, null);
                return await task.ConfigureAwait(false);
            }
            catch { return (false, NotSignedIn, null); }
        }

        /// <summary>
        /// Delete the account. Same refusal contract as <see cref="ChangeDisplayNameAsync"/>: an
        /// unseeded head must report that nothing was deleted, so no caller clears local data on
        /// the strength of a deletion that never happened.
        /// </summary>
        public static async Task<(bool success, string? error)> DeleteAccountAsync()
        {
            var provider = DeleteAccountProvider;
            if (provider == null) return (false, NotSignedInDelete);
            try
            {
                var task = provider();
                if (task == null) return (false, NotSignedInDelete);
                return await task.ConfigureAwait(false);
            }
            catch { return (false, NotSignedInDelete); }
        }

        // The WPF preconditions verbatim (ProfileSyncService.ChangeDisplayNameAsync /
        // DeleteAccountAsync). Unseeded IS that precondition failing: IsLoggedIn is false.
        private const string NotSignedIn = "You must be logged in to change your name";
        private const string NotSignedInDelete = "You must be logged in to delete your account";

        private static bool Ask(Func<bool>? provider)
        {
            try { return provider?.Invoke() ?? false; } catch { return false; }
        }
    }
}
