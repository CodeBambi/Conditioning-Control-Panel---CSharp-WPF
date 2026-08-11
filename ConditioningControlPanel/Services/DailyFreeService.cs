using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The ? box's engine: one premium feature per day, genuinely free for that day (owner
    /// design, 2026-08-11). The pick is date-seeded from a curated pool so every install
    /// agrees on "today's feature" without a server round-trip, and the server can override
    /// any given day (promo days, DtRH drops) via <c>/config/daily-feature</c>.
    ///
    /// <para><b>Curated pool, not the whole paid catalogue.</b> The owner controls the list;
    /// heavyweight tier-2 content (DtRH) only ever appears through a server override, never
    /// through the local rotation.</para>
    ///
    /// <para><b>Enforcement:</b> <see cref="TierGate"/>'s keyed overloads OR
    /// <see cref="IsFreeToday"/> into their verdicts, and the engine-internal access checks of
    /// the pool features (AutonomyService, HapticMixer, KeywordTriggerService) consult it the
    /// same way. The check is live on every read, so the unlock evaporates at local midnight
    /// with no timer to leak. A rolled system clock can cherry-pick a day's feature - accepted:
    /// every entitlement this app enforces is ultimately client-side, and the server override
    /// (fetched with today's date echoed back) corrects any online install.</para>
    /// </summary>
    public class DailyFreeService
    {
        /// <summary>
        /// Keys are OURS (stable API for the server override + TierGate call sites), not ShowTab
        /// keys and not display names. Order matters: it is the rotation's wheel, so appending
        /// is safe but reordering reshuffles everyone's calendar.
        ///
        /// <para>Haptics and Voice were CUT from the rotation (owner, 2026-08-11: "kinda shallow,
        /// I don't want them in the pool") but stay in <see cref="OverridableKeys"/> - their
        /// TierGate keys and engine gates are still wired, so a server override can still hand
        /// either out on a chosen day even though the wheel never lands on them.</para>
        /// </summary>
        public static readonly string[] Pool = { "takeover", "awareness", "fyp", "remote" };

        /// <summary>
        /// What the SERVER may name, which is wider than what the wheel spins: the live pool plus
        /// the two benched features whose unlock plumbing remains in place. Off-pool content keys
        /// (dtrh) still need real client work before they can join - see RefreshAsync.
        /// </summary>
        private static readonly string[] OverridableKeys = { "takeover", "awareness", "fyp", "remote", "haptics", "voice" };

        private const string OverrideEndpoint = "https://codebambi-proxy.vercel.app/config/daily-feature";
        private static readonly TimeSpan RefetchEvery = TimeSpan.FromHours(6);

        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private string? _serverKey;          // today's override, when the server sent one
        private string? _serverKeyForDate;   // the local date the override was fetched for
        private DateTime _lastFetchUtc = DateTime.MinValue;

        /// <summary>Raised when the effective key may have changed (override landed).</summary>
        public event Action? TodayChanged;

        /// <summary>
        /// Today's free feature key. Server override wins when it was fetched for today's local
        /// date; otherwise the date-seeded pick. Never null - the wheel always lands somewhere.
        /// </summary>
        public string TodayKey
        {
            get
            {
                if (_serverKey != null && _serverKeyForDate == LocalDateStamp()) return _serverKey;
                return SeededPick(DateTime.Today);
            }
        }

        /// <summary>True when <paramref name="featureKey"/> is today's free feature.</summary>
        public bool IsFreeToday(string? featureKey) =>
            featureKey != null && string.Equals(TodayKey, featureKey, StringComparison.OrdinalIgnoreCase);

        private static string LocalDateStamp() => DateTime.Now.ToString("yyyy-MM-dd");

        /// <summary>
        /// The no-repeat chain's fixed starting line. Every install walks the same chain from
        /// here, so every install lands on the same key for any given day. Moving this date (or
        /// editing the pool) reshuffles everyone's calendar from that day on.
        /// </summary>
        private static readonly DateTime ChainEpoch = new(2026, 1, 1);

        private readonly object _chainLock = new();
        private DateTime _chainDate = DateTime.MinValue;
        private string? _chainKey;

        /// <summary>
        /// Deterministic pick for a date, with NO BACK-TO-BACK REPEATS (owner, 2026-08-11:
        /// "never have the same back to back"). Each day's hash chooses a STEP of 1..N-1 around
        /// the pool wheel from yesterday's key - a step of 0 is unrepresentable, so a repeat is
        /// structurally impossible rather than re-rolled. That makes the pick a chain, so it is
        /// walked from <see cref="ChainEpoch"/> instead of computed point-wise; the walk is a few
        /// hundred FNV hashes at worst and the (date, key) memo below makes the per-gate-check
        /// cost zero. The memo also walks FORWARD from its last answer, so the midnight rollover
        /// costs one step, not a re-walk.
        ///
        /// <para>FNV-1a over the stamp, NOT string.GetHashCode - that one is randomized per
        /// process since .NET Core, which would hand every install (and every app restart) a
        /// different "today".</para>
        ///
        /// <para>A server override does not enter the chain: the seeded wheel ignores overrides,
        /// so the day after an override CAN seed the same key the override named. Accepted - the
        /// owner is looking at the calendar when they place one.</para>
        /// </summary>
        private string SeededPick(DateTime date)
        {
            date = date.Date;
            lock (_chainLock)
            {
                if (_chainKey != null && _chainDate == date) return _chainKey;

                // Resume from the memo when it is behind us but on the chain; otherwise restart
                // at the epoch (first call, or a clock rolled backwards).
                var cursor = ChainEpoch;
                var key = Pool[Fnv1a(DateStamp(ChainEpoch)) % (uint)Pool.Length];
                if (_chainKey != null && _chainDate > ChainEpoch && _chainDate < date)
                {
                    cursor = _chainDate;
                    key = _chainKey;
                }

                for (var d = cursor.AddDays(1); d <= date; d = d.AddDays(1))
                {
                    var step = 1 + (int)(Fnv1a(DateStamp(d)) % (uint)(Pool.Length - 1));
                    key = Pool[(Array.IndexOf(Pool, key) + step) % Pool.Length];
                }

                // A pre-epoch date (clock rolled way back) walks zero steps and returns the epoch
                // key - deterministic and harmless, so it is not special-cased.
                _chainDate = date;
                _chainKey = key;
                return key;
            }
        }

        private static string DateStamp(DateTime d) => d.ToString("yyyy-MM-dd");

        private static uint Fnv1a(string s)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var ch in s)
                {
                    hash ^= ch;
                    hash *= 16777619;
                }
                return hash;
            }
        }

        /// <summary>
        /// Fetches the server override if the cache is stale. Fire-and-forget from startup and
        /// cheap to call opportunistically (RefreshPremiumRail does): the 6h gate makes repeat
        /// calls free. 404 / offline / bad payload all mean "no override" - the seeded pick is
        /// the designed fallback, so the box works on day one with no server deploy at all.
        /// </summary>
        public async Task RefreshAsync()
        {
            if (DateTime.UtcNow - _lastFetchUtc < RefetchEvery && _serverKeyForDate == LocalDateStamp())
                return;
            _lastFetchUtc = DateTime.UtcNow;
            try
            {
                var url = $"{OverrideEndpoint}?date={LocalDateStamp()}";
                var resp = await _http.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return;

                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var json = JObject.Parse(body);
                var key = (string?)json["key"];
                if (string.IsNullOrWhiteSpace(key)) return;
                key = key.Trim().ToLowerInvariant();

                // The server may name anything - including off-pool drops like "dtrh" - but a
                // typo must not brick the box, so unknown keys are logged and ignored. Extend
                // OverridableKeys (and the ShowTab map in CardMystery_Click) to enable off-pool
                // drops. Benched pool members (haptics, voice) stay overridable on purpose.
                if (!OverridableKeys.Contains(key))
                {
                    App.Logger?.Warning("DailyFree: server override '{Key}' not in pool, ignoring", key);
                    return;
                }

                var changed = _serverKey != key || _serverKeyForDate != LocalDateStamp();
                _serverKey = key;
                _serverKeyForDate = LocalDateStamp();
                App.Logger?.Information("DailyFree: server override for {Date}: {Key}", _serverKeyForDate, key);
                if (changed) TodayChanged?.Invoke();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DailyFree: override fetch failed (seeded pick stands): {E}", ex.Message);
            }
        }
    }
}
