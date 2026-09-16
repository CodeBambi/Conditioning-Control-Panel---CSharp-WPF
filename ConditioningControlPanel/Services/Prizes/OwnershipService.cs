using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ConditioningControlPanel.Services.Prizes
{
    /// <summary>What changed in one ownership update.</summary>
    public sealed class OwnershipChangedEventArgs(IReadOnlyCollection<string> added, IReadOnlyCollection<string> removed, long revision) : EventArgs
    {
        /// <summary>Grant ids held now that were not held before.</summary>
        public IReadOnlyCollection<string> Added { get; } = added;
        /// <summary>Grant ids held before that are gone now (a revoke, or <see cref="OwnershipService.Clear"/>).</summary>
        public IReadOnlyCollection<string> Removed { get; } = removed;
        /// <summary>The revision now held (0 after a clear).</summary>
        public long Revision { get; } = revision;
    }

    /// <summary>
    /// Which Back Room prizes the signed-in account owns. The server is the only authority: this
    /// service holds the last snapshot it was handed and answers <see cref="IsGranted"/> from it.
    ///
    /// <para><b>In-memory only.</b> No persistence, settings flag, network or profile sync. A later
    /// lane fetches snapshots and calls <see cref="ApplySnapshot"/>.</para>
    ///
    /// <para><b>Snapshot rules.</b> A snapshot for any account other than the one signed in is
    /// dropped. For the account already held, a LOWER revision is stale and dropped, and an equal
    /// revision with the identical set is a no-op. A snapshot for a different account than the one
    /// held replaces it wholesale whatever its revision. Held grants are also bound to the account
    /// they arrived for: if the signed-in account changes before anyone calls <see cref="Clear"/>,
    /// the old account's grants stop counting at once (silently; call Clear on logout so listeners
    /// hear about it).</para>
    ///
    /// <para><b>DEBUG override.</b> <c>CCP_PRIZE_GRANTS</c>, read once at construction, comma
    /// separated: exact ids, prefix wildcards ending in <c>.*</c> (<c>fx.*</c>), or <c>*</c>.
    /// Compiled only under DEBUG; a Release build never reads the variable.</para>
    ///
    /// <para><b>Threading.</b> Every member is safe from any thread. <see cref="OwnershipChanged"/>
    /// fires on the WPF UI thread (inline when the caller is already on it, BeginInvoke otherwise)
    /// and is skipped once the dispatcher is shutting down. With no WPF Application at all it fires
    /// synchronously on the calling thread.</para>
    /// </summary>
    public sealed class OwnershipService
    {
        public const string OverrideEnvVar = "CCP_PRIZE_GRANTS";

        private readonly object _gate = new();
        private readonly Func<string?> _currentAccountId;
        private readonly Action<Action> _raise;
        private readonly IReadOnlyList<string> _overridePatterns;

        private HashSet<string> _grants = new(StringComparer.Ordinal);
        private string? _heldAccountId;
        private long _revision;

        /// <summary>Raised when the held set changes. See the class notes for the thread.</summary>
        public event EventHandler<OwnershipChangedEventArgs>? OwnershipChanged;

        public OwnershipService() : this(() => App.UnifiedUserId, ReadDebugOverride(), RaiseOnUiThread) { }

        /// <summary>Test seam: the account source, override spec and event marshal are injected.</summary>
        internal OwnershipService(Func<string?> currentAccountId, string? overrideSpec, Action<Action> raise)
        {
            _currentAccountId = currentAccountId ?? throw new ArgumentNullException(nameof(currentAccountId));
            _raise = raise ?? throw new ArgumentNullException(nameof(raise));
            _overridePatterns = ParseOverride(overrideSpec);

            if (_overridePatterns.Count > 0)
            {
                var matched = PrizeGrants.All.Where(id => MatchesOverride(_overridePatterns, id)).ToList();
                App.Logger?.Information("[Prizes] {EnvVar} override active: patterns [{Patterns}] match {Count} known grants [{Matched}]",
                    OverrideEnvVar, string.Join(", ", _overridePatterns), matched.Count, string.Join(", ", matched));
            }
        }

        /// <summary>True if the signed-in account owns <paramref name="grantId"/> (or the DEBUG override names it).</summary>
        public bool IsGranted(string grantId)
        {
            if (string.IsNullOrWhiteSpace(grantId)) return false;
            if (_overridePatterns.Count > 0 && MatchesOverride(_overridePatterns, grantId)) return true;
            lock (_gate) return HeldForCurrentAccount() && _grants.Contains(grantId);
        }

        /// <summary>A copy of the server-granted ids for the signed-in account (the override is not listed).</summary>
        public IReadOnlyCollection<string> Grants
        {
            get { lock (_gate) return HeldForCurrentAccount() ? _grants.ToArray() : Array.Empty<string>(); }
        }

        /// <summary>The revision of the held snapshot, 0 when nothing is held.</summary>
        public long Revision { get { lock (_gate) return _revision; } }

        /// <summary>
        /// Adopt a server snapshot (the seam the server lane calls). Dropped when it is for another
        /// account or older than the held revision; raises <see cref="OwnershipChanged"/> only when
        /// the effective set actually changed. Blank ids are skipped, ids are trimmed.
        /// </summary>
        public void ApplySnapshot(string accountId, long revision, IEnumerable<string> grants)
        {
            if (string.IsNullOrEmpty(accountId) || grants is null) return;

            OwnershipChangedEventArgs? change;
            lock (_gate)
            {
                var current = _currentAccountId();
                if (string.IsNullOrEmpty(current) || !string.Equals(current, accountId, StringComparison.Ordinal))
                {
                    App.Logger?.Debug("[Prizes] snapshot for another account ignored (rev {Revision})", revision);
                    return;
                }

                var sameAccount = string.Equals(_heldAccountId, accountId, StringComparison.Ordinal);
                if (sameAccount && revision < _revision)
                {
                    App.Logger?.Debug("[Prizes] stale snapshot ignored (rev {Revision} < held {Held})", revision, _revision);
                    return;
                }

                var next = new HashSet<string>(
                    grants.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()),
                    StringComparer.Ordinal);
                var previous = sameAccount ? _grants : new HashSet<string>(StringComparer.Ordinal);

                _heldAccountId = accountId;
                _revision = revision;
                _grants = next;

                var added = next.Where(g => !previous.Contains(g)).ToArray();
                var removed = previous.Where(g => !next.Contains(g)).ToArray();
                change = added.Length == 0 && removed.Length == 0
                    ? null
                    : new OwnershipChangedEventArgs(added, removed, revision);
            }

            if (change != null) Raise(change);
        }

        /// <summary>Drop everything held (logout, account switch). Raises only if something was held.</summary>
        public void Clear()
        {
            OwnershipChangedEventArgs? change = null;
            lock (_gate)
            {
                if (_grants.Count > 0)
                    change = new OwnershipChangedEventArgs(Array.Empty<string>(), _grants.ToArray(), 0);
                _grants = new HashSet<string>(StringComparer.Ordinal);
                _heldAccountId = null;
                _revision = 0;
            }

            if (change != null) Raise(change);
        }

        // Caller holds _gate.
        private bool HeldForCurrentAccount()
            => _heldAccountId != null && string.Equals(_currentAccountId(), _heldAccountId, StringComparison.Ordinal);

        private void Raise(OwnershipChangedEventArgs args)
        {
            _raise(() =>
            {
                try { OwnershipChanged?.Invoke(this, args); }
                catch (Exception ex) { App.Logger?.Debug("[Prizes] OwnershipChanged handler threw: {Error}", ex.Message); }
            });
        }

        private static void RaiseOnUiThread(Action invoke)
        {
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null) { invoke(); return; }
                if (dispatcher.HasShutdownStarted) return;
                if (dispatcher.CheckAccess()) invoke();
                else dispatcher.BeginInvoke(invoke);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Prizes] could not raise OwnershipChanged: {Error}", ex.Message);
            }
        }

        private static string? ReadDebugOverride()
        {
#if DEBUG
            try { return Environment.GetEnvironmentVariable(OverrideEnvVar); }
            catch { return null; }
#else
            return null;
#endif
        }

        /// <summary>
        /// Parse an override spec into patterns: trimmed, deduplicated, junk dropped. Kept entries
        /// are <c>*</c>, a prefix wildcard <c>some.prefix.*</c> (no other <c>*</c>, non-empty
        /// prefix), or an exact id (no <c>*</c>, no inner whitespace). Pure, for the tests.
        /// </summary>
        internal static IReadOnlyList<string> ParseOverride(string? spec)
        {
            var patterns = new List<string>();
            if (string.IsNullOrWhiteSpace(spec)) return patterns;

            foreach (var raw in spec.Split(','))
            {
                var entry = raw.Trim();
                if (IsValidPattern(entry) && !patterns.Contains(entry)) patterns.Add(entry);
            }
            return patterns;
        }

        /// <summary>One already-trimmed override entry: <c>*</c>, <c>some.prefix.*</c>, or an exact id.</summary>
        internal static bool IsValidPattern(string? entry)
        {
            if (string.IsNullOrEmpty(entry) || entry.Any(char.IsWhiteSpace)) return false;
            var star = entry.IndexOf('*');
            return star < 0
                || entry == "*"
                || (star == entry.Length - 1 && entry.Length > 2 && entry[^2] == '.' && entry[^3] != '.');
        }

        /// <summary>True if any parsed pattern names <paramref name="grantId"/>. Pure, for the tests.</summary>
        internal static bool MatchesOverride(IReadOnlyList<string> patterns, string grantId)
        {
            foreach (var p in patterns)
            {
                if (p == "*") return true;
                if (p.EndsWith(".*", StringComparison.Ordinal))
                {
                    if (grantId.StartsWith(p.Substring(0, p.Length - 1), StringComparison.Ordinal)) return true;
                }
                else if (string.Equals(p, grantId, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
