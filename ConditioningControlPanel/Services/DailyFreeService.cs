using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
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

        /// <summary>
        /// Floor between two ATTEMPTS on the same day, whatever they returned. The success gate
        /// above cannot carry this on its own: it is keyed on <see cref="_serverKeyForDate"/>,
        /// which only a 200 ever writes, so while the endpoint 404s (it does not exist yet
        /// server-side) every dashboard repaint would fire a live HTTPS GET.
        /// </summary>
        private static readonly TimeSpan RetryEvery = TimeSpan.FromMinutes(10);

        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private string? _serverKey;          // today's override, when the server sent one
        private string? _serverKeyForDate;   // the local date the override was fetched for
        private DateTime _lastFetchUtc = DateTime.MinValue;
        private DateTime _lastAttemptUtc = DateTime.MinValue;
        private string? _lastAttemptForDate; // the local date the last attempt was made for

        /// <summary>Raised when the effective key may have changed (override landed, or the
        /// local day rolled over).</summary>
        public event Action? TodayChanged;

        private readonly Timer _dayRollTimer;
        private string _dayRollLastStamp = LocalDateStamp();

        /// <summary>
        /// Longest a single wait may be. <see cref="TodayKey"/> is computed live, so nothing here
        /// EXPIRES - but nothing repaints either, and a wall painted before midnight goes on
        /// naming yesterday's feature while a click on it resolves today's. The tick is what
        /// closes that gap. Capped rather than armed once at midnight because a machine that
        /// sleeps through the rollover wakes with a one-shot timer that is simply late; a capped
        /// re-arm notices the new day within one interval however long the sleep was.
        /// </summary>
        private static readonly TimeSpan DayRollMaxWait = TimeSpan.FromMinutes(10);

        public DailyFreeService()
        {
            _dayRollTimer = new Timer(_ => OnDayRollTick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            ArmDayRollTimer();
        }

        private void ArmDayRollTimer()
        {
            try
            {
                var now = DateTime.Now;
                // +2s past midnight: a tick that lands a hair EARLY would read yesterday's date
                // and re-arm for two more seconds, which is harmless but pointless.
                var untilMidnight = now.Date.AddDays(1).AddSeconds(2) - now;
                var due = untilMidnight < DayRollMaxWait ? untilMidnight : DayRollMaxWait;
                if (due < TimeSpan.FromSeconds(1)) due = TimeSpan.FromSeconds(1);
                _dayRollTimer.Change(due, Timeout.InfiniteTimeSpan);
            }
            catch (Exception ex) { App.Logger?.Debug("DailyFree: day-roll re-arm failed: {E}", ex.Message); }
        }

        /// <summary>
        /// Fires on a threadpool thread; every subscriber marshals for itself (MainWindow's does).
        /// Only a real date change raises anything, so the capped heartbeat above costs one
        /// string compare per tick on the 143 ticks a day that are not the rollover.
        /// </summary>
        private void OnDayRollTick()
        {
            try
            {
                var stamp = LocalDateStamp();
                if (stamp == _dayRollLastStamp) return;
                _dayRollLastStamp = stamp;
                App.Logger?.Information("DailyFree: local day rolled over to {Date}", stamp);
                // Yesterday's override must not survive the rollover, and the seeded pick has
                // already moved on by itself - so this is a repaint signal, not an invalidation.
                _ = RefreshAsync();
                TodayChanged?.Invoke();
            }
            catch (Exception ex) { App.Logger?.Debug("DailyFree: day-roll tick failed: {E}", ex.Message); }
            finally { ArmDayRollTimer(); }
        }

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
            if (Pool.Length == 0) return string.Empty;   // nothing to give away; matches no key
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
                    // A pool shrunk below the 3-day window leaves nothing to choose from, and an
                    // empty candidate list is a DivideByZero on the modulo below. Widen the
                    // window instead of throwing: 2 keys alternate, 1 key parks on itself.
                    if (candidates.Length == 0) candidates = Pool.Where(k => k != key).ToArray();
                    if (candidates.Length == 0) candidates = Pool;
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
        /// cheap to call opportunistically (RefreshPremiumRail does): the two gates below make
        /// repeat calls free whether the last one succeeded or not. 404 / offline / bad payload
        /// all mean "no override" - the seeded pick is the designed fallback, so the box works on
        /// day one with no server deploy at all.
        /// </summary>
        public async Task RefreshAsync()
        {
            var today = LocalDateStamp();
            var now = DateTime.UtcNow;

            // Success gate, unchanged: today's override is in hand and still fresh.
            if (_serverKeyForDate == today && now - _lastFetchUtc < RefetchEvery) return;

            // Attempt gate. Keyed on the day so a rollover still refetches at once, and set
            // BEFORE the await so the overlapping fire-and-forget calls a repaint storm makes
            // cannot each slip through.
            if (_lastAttemptForDate == today && now - _lastAttemptUtc < RetryEvery) return;
            _lastAttemptForDate = today;
            _lastAttemptUtc = now;
            _lastFetchUtc = now;
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
