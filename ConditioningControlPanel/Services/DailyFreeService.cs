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
        /// What the SERVER may name, which is wider than what the wheel spins: the live pool,
        /// the two benched features whose unlock plumbing remains in place, and "dtrh" - the
        /// off-pool T2 drop (owner, 2026-08-11: promo Saturdays). DtRH is wired through
        /// TierGate's keyed Lab overloads at all four gate sites, but the WHEEL never lands on
        /// it: T2 content is only ever given away on a day the owner explicitly names.
        /// </summary>
        private static readonly string[] OverridableKeys = { "takeover", "awareness", "fyp", "remote", "haptics", "voice", "dtrh" };

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
        private string? _chainKey;       // the pick for _chainDate
        private string? _chainPrevKey;   // the pick for the day before _chainDate

        /// <summary>
        /// Deterministic pick for a date, with a THREE-DAY SPACING RULE (owner, 2026-08-11:
        /// "never have the same back to back", then "space out so we dont get the same feat in
        /// 3 days"): each day's hash picks from the pool MINUS the previous two days' picks, so
        /// a feature can never appear twice inside any 3-day window - structurally, not by
        /// re-roll. With the 4-feature pool that is a choice of 2 each day. NOTE: shrinking the
        /// pool below 3 would leave zero candidates; the exclusion window must stay at most
        /// Pool.Length - 1 wide.
        ///
        /// <para>The exclusion makes the pick a chain, so it is walked from
        /// <see cref="ChainEpoch"/> instead of computed point-wise; the walk is a few hundred
        /// FNV hashes at worst, the (date, key, prevKey) memo makes the per-gate-check cost
        /// zero, and the memo walks FORWARD from its last answer so the midnight rollover costs
        /// one step, not a re-walk.</para>
        ///
        /// <para>FNV-1a over the stamp, NOT string.GetHashCode - that one is randomized per
        /// process since .NET Core, which would hand every install (and every app restart) a
        /// different "today".</para>
        ///
        /// <para>A server override does not enter the chain: the seeded wheel ignores overrides,
        /// so the days around an override CAN seed the key the override named. Accepted - the
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
                DateTime cursor;
                string key;
                string? prev;
                if (_chainKey != null && _chainDate > ChainEpoch && _chainDate < date)
                {
                    cursor = _chainDate;
                    key = _chainKey;
                    prev = _chainPrevKey;
                }
                else
                {
                    cursor = ChainEpoch;
                    key = Pool[Fnv1a(DateStamp(ChainEpoch)) % (uint)Pool.Length];
                    prev = null;   // the epoch's first day only excludes itself
                }

                for (var d = cursor.AddDays(1); d <= date; d = d.AddDays(1))
                {
                    // Pool order minus the last two picks keeps the candidate list stable, so
                    // appending to the pool stays calendar-safe for all dates before the append.
                    var candidates = Pool.Where(k => k != key && k != prev).ToArray();
                    var next = candidates[Fnv1a(DateStamp(d)) % (uint)candidates.Length];
                    prev = key;
                    key = next;
                }

                // A pre-epoch date (clock rolled way back) walks zero steps and returns the epoch
                // key - deterministic and harmless, so it is not special-cased.
                _chainDate = date;
                _chainKey = key;
                _chainPrevKey = prev;
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

                // The server may name anything, but a typo must not brick the box, so unknown
                // keys are logged and ignored. A NEW off-pool drop needs: its key here in
                // OverridableKeys, a keyed TierGate overload at every gate site, and rows in
                // CardMystery_Click / MysteryFeatureName / MysteryFeatureArtPath. "dtrh" and the
                // benched pool members (haptics, voice) are already wired.
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
