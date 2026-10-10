using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Back Room prize ownership on this head. PORTED from WPF Services/Prizes/OwnershipService.cs
    /// (ApplySnapshot / IsGranted) and ProfileSyncService.PrizeFeed.Apply (progression#17): the
    /// server's prize block rides every provider validate response (ProviderSubscription.PrizesSink,
    /// seeded by AccountSeed.Seed); a snapshot is held for one account, a stale revision is dropped,
    /// and another account's grants never show. Still missing: the /v2/user/sync and Back Room
    /// counter feeds (no cloud sync or station bridge on this head). Plus WPF's DEBUG-only <c>CCP_PRIZE_GRANTS</c> override
    /// (OwnershipService.ReadDebugOverride): same variable, same patterns (exact id,
    /// <c>prefix.*</c>, <c>*</c>), and a Release build never reads it.
    /// </summary>
    internal static class PrizeOwnership
    {
        /// <summary>WPF <c>PrizeGrants.FlashDriftBounce</c>: a wire id, spelled as the server does.</summary>
        internal const string FlashDriftBounce = "fx.flash.drift_bounce";

        /// <summary>WPF <c>PrizeGrants.FlashPendulum</c>.</summary>
        internal const string FlashPendulum = "fx.flash.pendulum";

        /// <summary>The DEBUG override spec, read once; always null in Release.</summary>
        internal static readonly string? OverrideSpec = ReadOverride();

        /// <summary>Test seam: the owned-check every caller uses.</summary>
        internal static Func<string, bool> IsGranted = id => Matches(OverrideSpec, id) || HeldGrant(id);

        /// <summary>The signed-in account (WPF App.UnifiedUserId). A seam for tests.</summary>
        internal static Func<string?> CurrentAccount = () => CoreAccount.UnifiedUserId;

        /// <summary>WPF PrizeGrants.GrantsChanged: the held grants moved (a snapshot landed, or the
        /// store was cleared). Raised on the caller's thread, outside the lock: UI listeners post.</summary>
        internal static event Action? Changed;

        private static void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { Serilog.Log.Debug("[Prizes] Changed listener threw: {Error}", ex.Message); }
        }

        private static readonly object Gate = new();
        private static string? _heldAccount;
        private static long _revision;
        private static HashSet<string> _grants = new(StringComparer.Ordinal);

        /// <summary>Points the Core prize feed at this store. Idempotent.</summary>
        internal static void Seed() => ConditioningControlPanel.Services.ProviderSubscription.PrizesSink = Apply;

        /// <summary>WPF PrizeFeed.Apply: a block answered for another record is ignored; a missing block
        /// leaves the held state alone.</summary>
        internal static void Apply(string? requestedFor, string? answeredFor, ConditioningControlPanel.Models.PrizesBlock? prizes, string source)
        {
            if (string.IsNullOrEmpty(requestedFor) || prizes?.Grants == null) return;
            if (!string.Equals(requestedFor, answeredFor, StringComparison.Ordinal))
            {
                Serilog.Log.Debug("[Prizes] {Source} answered for another account, block ignored", source);
                return;
            }
            ApplySnapshot(requestedFor, prizes.Revision, prizes.Grants);
            Serilog.Log.Debug("[Prizes] {Source} snapshot rev {Revision}, {Count} grants", source, prizes.Revision, prizes.Grants.Count);
        }

        /// <summary>WPF OwnershipService.ApplySnapshot: dropped for another account or an older revision
        /// of the held one; ids trimmed, blanks skipped.</summary>
        internal static void ApplySnapshot(string accountId, long revision, IEnumerable<string> grants)
        {
            if (string.IsNullOrEmpty(accountId) || grants is null) return;
            lock (Gate)
            {
                var current = CurrentAccount();
                if (string.IsNullOrEmpty(current) || !string.Equals(current, accountId, StringComparison.Ordinal)) return;
                if (string.Equals(_heldAccount, accountId, StringComparison.Ordinal) && revision < _revision) return;
                _heldAccount = accountId;
                _revision = revision;
                _grants = new HashSet<string>(grants.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()), StringComparer.Ordinal);
            }
            RaiseChanged();   // Apply lands here too
        }

        /// <summary>WPF OwnershipService.Clear (logout, tests).</summary>
        internal static void Clear()
        {
            lock (Gate) { _grants = new HashSet<string>(StringComparer.Ordinal); _heldAccount = null; _revision = 0; }
            RaiseChanged();
        }

        private static bool HeldGrant(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            lock (Gate)
                return _heldAccount != null && string.Equals(_heldAccount, CurrentAccount(), StringComparison.Ordinal)
                       && _grants.Contains(id);
        }

        private static string? ReadOverride()
        {
#if DEBUG
            return Environment.GetEnvironmentVariable("CCP_PRIZE_GRANTS");
#else
            return null;
#endif
        }

        /// <summary>WPF OwnershipService.ParseOverride + MatchesOverride over one comma list.</summary>
        internal static bool Matches(string? spec, string id) =>
            !string.IsNullOrEmpty(spec) && spec.Split(',').Select(p => p.Trim()).Any(p =>
                p == "*" || p == id
                || (p.Length > 2 && p.EndsWith(".*", StringComparison.Ordinal) && p.IndexOf('*') == p.Length - 1
                    && p[^3] != '.' && id.StartsWith(p[..^1], StringComparison.Ordinal)));
    }
}
