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
        /// </summary>
        public static readonly string[] Pool = { "takeover", "awareness", "haptics", "voice", "fyp", "remote" };

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
                var today = LocalDateStamp();
                if (_serverKey != null && _serverKeyForDate == today) return _serverKey;
                return SeededPick(today);
            }
        }

        /// <summary>True when <paramref name="featureKey"/> is today's free feature.</summary>
        public bool IsFreeToday(string? featureKey) =>
            featureKey != null && string.Equals(TodayKey, featureKey, StringComparison.OrdinalIgnoreCase);

        private static string LocalDateStamp() => DateTime.Now.ToString("yyyy-MM-dd");

        /// <summary>
        /// Deterministic pick for a date stamp. FNV-1a over the stamp, NOT string.GetHashCode -
        /// that one is randomized per process since .NET Core, which would hand every install
        /// (and every app restart) a different "today".
        /// </summary>
        private static string SeededPick(string dateStamp)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var ch in dateStamp)
                {
                    hash ^= ch;
                    hash *= 16777619;
                }
                return Pool[hash % (uint)Pool.Length];
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
                // this list (and the ShowTab map in CardMystery_Click) to enable off-pool drops.
                if (!Pool.Contains(key))
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
