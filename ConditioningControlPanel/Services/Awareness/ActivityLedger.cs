using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>What the ledger knows about one app at one instant (doc 02 §2.3, history block).</summary>
    /// <param name="SinceLastVisit">
    /// Gap between the END of the previous visit and the start of the current one. Null on a
    /// first-ever visit — and note that "null" is a different joke from "zero".
    /// </param>
    public sealed record LedgerSnapshot(
        int VisitsToday,
        int MinutesToday,
        int MinutesThisWeek,
        TimeSpan? SinceLastVisit,
        int DayStreak,
        int LongestDwellTodaySeconds,
        int CurrentVisitDwellSeconds,
        int SwitchesLast10Min,
        string DayArcSummary,
        bool FirstEverVisit,
        bool FirstVisitToday)
    {
        /// <summary>A snapshot of an app the ledger has never heard of.</summary>
        public static LedgerSnapshot Empty { get; } =
            new(0, 0, 0, null, 0, 0, 0, 0, "", true, true);
    }

    /// <summary>
    /// One entry of the in-memory session ring: an app the user left, and how long that stint lasted.
    ///
    /// <para><paramref name="DwellSeconds"/> is the SEGMENT, not the cumulative visit — a visit that
    /// survives two excursions produces three ring entries whose seconds sum to the visit, so the day
    /// arc can add them up without counting the same minutes three times.</para>
    /// </summary>
    public sealed record LedgerTransition(
        string AppId,
        string? Cluster,
        ActivityCategory Category,
        int DwellSeconds,
        DateTime At);

    /// <summary>
    /// One <see cref="ActivityLedger.PeekTrends"/> result: the trends themselves, plus the one-shot
    /// guard keys they RESERVED but did not consume.
    ///
    /// <para>The guards are opaque on purpose. Recomputing them from a <see cref="TrendEvent"/> would
    /// need the visit sequence and the wake minute, neither of which the event carries — and a
    /// commit that recomputed a key slightly differently from the peek would silently re-offer a
    /// once-per-day callback forever.</para>
    /// </summary>
    public sealed class TrendDerivation
    {
        /// <summary>A derivation that reserved nothing. Committing it is a no-op.</summary>
        public static TrendDerivation Empty { get; } =
            new(Array.Empty<TrendEvent>(), Array.Empty<string>());

        internal TrendDerivation(IReadOnlyList<TrendEvent> trends, IReadOnlyList<string> guards)
        {
            Trends = trends;
            Guards = guards;
        }

        /// <summary>The trends this frame may use.</summary>
        public IReadOnlyList<TrendEvent> Trends { get; }

        /// <summary>The guard keys <see cref="ActivityLedger.CommitTrends"/> will burn on delivery.</summary>
        internal IReadOnlyList<string> Guards { get; }
    }

    /// <summary>
    /// The local, persisted memory of what this machine has been used for — day-keyed counters per app
    /// id plus an in-memory ring of the last <see cref="SessionRingCapacity"/> transitions. Everything
    /// that makes an awareness line a callback rather than an observation comes from here.
    ///
    /// <para><b>Correctness is the feature.</b> "Fifth visit" when it was the second does not read as a
    /// small bug — it reads as the character being fake, and the whole trick is gone. Every number this
    /// class produces is pure logic over an injected clock, and every boundary in it is unit-tested.</para>
    ///
    /// <para><b>Privacy.</b> Keys are <see cref="AppClusterMap"/> ids / resolved service ids, run
    /// through <see cref="AwarenessText.SanitizeId"/>. There is no parameter on this class that can
    /// carry a window title, an URL or OCR text, and the serialised file therefore cannot contain one.
    /// The only free-form strings on disk are app ids, cluster ids and <c>yyyy-MM-dd</c> day keys.</para>
    ///
    /// <para><b>Time is local.</b> "Today", "this week", the day streak and the histogram are all about
    /// the user's day, not UTC's. The injected clock returns local time and every persisted timestamp
    /// is local for the same reason.</para>
    /// </summary>
    public sealed class ActivityLedger : IDisposable
    {
        // ===================== tuning constants =====================

        /// <summary>Schema version of awareness_ledger.json.</summary>
        public const int SchemaVersion = 1;

        /// <summary>Transitions kept in memory for arc/restlessness maths (doc 02 §2.1: "last ~50").</summary>
        public const int SessionRingCapacity = 50;

        /// <summary>Default retention in days, matching the privacy panel's 7/30 slider top end.</summary>
        public const int DefaultRetentionDays = 30;

        /// <summary>
        /// How long a user may be away from an app before coming back counts as a NEW visit. Below this
        /// the visit simply continues, which is what stops a Discord title flicker or a two-second
        /// alt-tab from resetting the dwell clock and inventing visits (doc 02 §2.4, LongHaul note).
        /// </summary>
        public const int ExcursionToleranceSeconds = 30;

        /// <summary>Minimum visits today before <see cref="TrendKind.ReturnVisit"/> is worth saying.</summary>
        public const int ReturnVisitMinimum = 3;

        /// <summary>Minimum consecutive days before <see cref="TrendKind.Streak"/> is worth saying.</summary>
        public const int StreakMinimumDays = 3;

        /// <summary>Minimum consecutive plays of one track before <see cref="TrendKind.MediaLoop"/> fires.</summary>
        public const int MediaLoopMinimum = 3;

        /// <summary>Cumulative-dwell milestones, in minutes (doc 02 §4.4 — replaces the {1,5,10} nag).</summary>
        public static readonly int[] LongHaulMilestonesMinutes = { 30, 60, 120, 180 };

        /// <summary>Reopening a doomscroll app within this many seconds of closing it is a Backslide.</summary>
        public const int BacksideWindowSeconds = 300;

        /// <summary>Real input idle that must precede activity for <see cref="TrendKind.GhostTown"/>.</summary>
        public const int GhostTownMinimumIdleSeconds = 3 * 3600;

        /// <summary>Hour the "night" window opens (local). 20:00.</summary>
        public const int NightWindowStartHour = 20;

        /// <summary>Hour the "night" window closes (local, next morning). 06:00.</summary>
        public const int NightWindowEndHour = 6;

        /// <summary>Past nights considered when learning a typical bedtime.</summary>
        public const int BedtimeSampleDays = 14;

        /// <summary>Nights of evidence required before the bedtime is "learned" at all. Fewer = no NightShift.</summary>
        public const int BedtimeMinimumSamples = 3;

        /// <summary>Seconds of activity an hour bin needs before it counts as "awake at that hour".</summary>
        public const int NightHourMinimumSeconds = 60;

        /// <summary>
        /// A single accrual longer than this is treated as machine sleep / process suspension and
        /// contributes nothing. Counting six hours of a closed laptop lid as "on YouTube" is exactly
        /// the wrong-number failure this class exists to avoid.
        /// </summary>
        public const int MaxSingleAccrualSeconds = 6 * 3600;

        private static readonly TimeSpan SaveDebounce = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RolloverCheckInterval = TimeSpan.FromMinutes(5);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        // ===================== state =====================

        private readonly object _lock = new();
        private readonly object _writeLock = new();

        private readonly string _path;
        private readonly Func<DateTime> _clock;
        private readonly Func<int> _retentionDays;

        private readonly Dictionary<string, AppEntry> _apps = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Machine-wide hourly activity, day key → 24 bins of seconds. Deliberately NOT per app: it
        /// answers "was this machine awake at 02:00", which is all the bedtime maths needs, and it
        /// therefore survives <see cref="Forget"/> without carrying anyone's app identity.
        /// </summary>
        private readonly Dictionary<string, int[]> _nightHistogram = new(StringComparer.Ordinal);

        private readonly Dictionary<string, VisitState> _visits = new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<LedgerTransition> _ring = new();
        private readonly HashSet<string> _emitted = new(StringComparer.Ordinal);

        private string? _currentAppId;
        private string? _currentCluster;
        private ActivityCategory _currentCategory = ActivityCategory.Unknown;
        private DateTime _segmentStart;
        private int _segmentSeconds;
        private DateTime _lastRolloverDate;

        /// <summary>
        /// Bumped by <see cref="Wipe"/> and <see cref="Forget"/>. <see cref="SaveNow"/> captures it
        /// alongside the JSON and refuses to write if it moved in between — otherwise a debounced save
        /// that serialised just before the wipe can win the race for <c>_writeLock</c> and recreate the
        /// file the user just erased, every counter intact.
        /// </summary>
        private long _generation;

        private Timer? _saveTimer;
        private Timer? _rolloverTimer;
        private bool _started;
        private bool _disposed;

        /// <summary>Production constructor: the real path, the real clock, retention from settings.</summary>
        public ActivityLedger() : this(null, null, null) { }

        /// <summary>
        /// Test/diagnostic constructor. Nothing subscribes to app events and nothing is read from
        /// <c>App</c> unless a null is passed, so this is safe to build headlessly.
        /// </summary>
        /// <param name="ledgerPath">Full path of awareness_ledger.json.</param>
        /// <param name="localClock">Injectable LOCAL clock. Everything here is about the user's day.</param>
        /// <param name="retentionDays">Injectable retention, normally <c>AwarenessRetentionDays</c>.</param>
        public ActivityLedger(string? ledgerPath, Func<DateTime>? localClock = null, Func<int>? retentionDays = null)
        {
            _path = string.IsNullOrWhiteSpace(ledgerPath) ? DefaultLedgerPath : ledgerPath!;
            _clock = localClock ?? (() => DateTime.Now);
            _retentionDays = retentionDays ?? DefaultRetentionFromSettings;
            _segmentStart = _clock();
            _lastRolloverDate = _segmentStart.Date;
        }

        /// <summary><c>%LOCALAPPDATA%\ConditioningControlPanel\awareness_ledger.json</c>.</summary>
        public static string DefaultLedgerPath => Path.Combine(App.UserDataPath, "awareness_ledger.json");

        /// <summary>Full path of the ledger file — surfaced for the privacy panel and diagnostics.</summary>
        public string LedgerPath => _path;

        /// <summary>Sibling of <see cref="LedgerPath"/> left behind by an interrupted atomic write.</summary>
        public string LedgerTempPath => _path + ".tmp";

        /// <summary>The session ring, newest last. In memory only; never persisted.</summary>
        public IReadOnlyList<LedgerTransition> RecentTransitions
        {
            get { lock (_lock) return _ring.ToList(); }
        }

        /// <summary>
        /// Every app id the ledger is actually holding counters for, most recently seen first — up to
        /// <c>AwarenessRetentionDays</c> of history, including the app in the foreground right now.
        ///
        /// <para>This, not <see cref="RecentTransitions"/>, is what the privacy panel's per-app forget
        /// must enumerate. The ring is populated only when the user LEAVES an app and never survives a
        /// restart, so a panel built on it offers no chips at all on a fresh launch — leaving
        /// "forget everything" as the only control for a user who came looking to remove one site.</para>
        /// </summary>
        public IReadOnlyList<string> KnownAppIds
        {
            get
            {
                lock (_lock)
                {
                    return _apps
                        .OrderByDescending(p => p.Value.LastSeen ?? DateTime.MinValue)
                        .ThenBy(p => p.Key, StringComparer.Ordinal)
                        .Select(p => p.Key)
                        .ToList();
                }
            }
        }

        private static int DefaultRetentionFromSettings()
        {
            try { return App.Settings?.Current?.AwarenessRetentionDays ?? DefaultRetentionDays; }
            catch { return DefaultRetentionDays; }
        }

        // ===================== lifecycle =====================

        /// <summary>
        /// Loads the file, prunes to retention and arms the rollover timer. Idempotent.
        ///
        /// <para><b>Pruning happens HERE, not when a UI surface asks.</b> A retention promise that is
        /// only honoured when the user happens to open the Companion tab is not a retention promise;
        /// this feature must age its own data out whether or not anything is ever looked at.</para>
        /// </summary>
        public void Start()
        {
            if (_disposed) return;

            lock (_lock)
            {
                if (_started) return;
                _started = true;

                Load();

                var now = _clock();
                _segmentStart = now;
                _lastRolloverDate = now.Date;
                PruneRetentionLocked(now);
            }

            _saveTimer ??= new Timer(_ => { try { SaveNow(); } catch { } }, null, Timeout.Infinite, Timeout.Infinite);
            _rolloverTimer ??= new Timer(_ => OnRolloverTick(), null, RolloverCheckInterval, RolloverCheckInterval);

            // ALWAYS re-arm, not just on the first Start: Stop() disarms this timer and leaves the
            // object non-null, so a pause/resume or an awareness off/on cycle would otherwise kill the
            // rollover backstop for the rest of the session — and the backstop matters most exactly
            // when the observer's own poll is not running (no dispatcher: "ledger is live, polling is not").
            try { _rolloverTimer.Change(RolloverCheckInterval, RolloverCheckInterval); }
            catch (ObjectDisposedException) { }

            App.Logger?.Information("ActivityLedger: started ({Apps} app(s), retention {Days}d)",
                AppCount, Math.Clamp(_retentionDays(), 1, 365));
        }

        /// <summary>Closes the open segment, flushes and disarms the timers. Safe to call twice.</summary>
        public void Stop()
        {
            if (!_started) return;

            lock (_lock)
            {
                Accrue(_clock());
                _currentAppId = null;
                _started = false;
            }

            try { _rolloverTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch (ObjectDisposedException) { }
            SaveNow();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { Stop(); } catch { }
            try { _saveTimer?.Dispose(); } catch { }
            try { _rolloverTimer?.Dispose(); } catch { }
            _saveTimer = null;
            _rolloverTimer = null;
        }

        /// <summary>Apps currently held in memory. Diagnostics and tests.</summary>
        public int AppCount
        {
            get { lock (_lock) return _apps.Count; }
        }

        private void OnRolloverTick()
        {
            if (_disposed) return;
            try
            {
                Heartbeat(_clock());
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ActivityLedger: rollover tick failed");
            }
        }

        // ===================== recording =====================

        /// <summary>
        /// Folds elapsed time into the counters and runs the day-rollover check. The observer calls
        /// this on its poll; the rollover timer calls it too, so a machine left on across midnight
        /// rolls over and prunes with no UI and no window changes at all.
        /// </summary>
        public void Heartbeat(DateTime at)
        {
            lock (_lock)
            {
                Accrue(at);
                CheckRolloverLocked(at);
            }
        }

        /// <summary>
        /// Records that the foreground is now <paramref name="appId"/>.
        ///
        /// <para>Returns true when this opened a NEW visit (as opposed to continuing one through a
        /// sub-<see cref="ExcursionToleranceSeconds"/> excursion). A blank or unresolvable id closes the
        /// current segment and records nothing — when the classifier cannot answer, the ledger does not
        /// guess.</para>
        /// </summary>
        public bool NoteFocus(string? appId, string? cluster, ActivityCategory category, DateTime at)
        {
            var id = string.IsNullOrWhiteSpace(appId) ? null : AwarenessText.SanitizeId(appId);
            if (id == null || id == AwarenessText.UnknownId)
            {
                NoteFocusEnd(at);
                return false;
            }

            var clusterId = string.IsNullOrWhiteSpace(cluster) ? null : AwarenessText.SanitizeId(cluster);

            lock (_lock)
            {
                Accrue(at);
                CheckRolloverLocked(at);

                var previousId = _currentAppId;
                if (string.Equals(previousId, id, StringComparison.OrdinalIgnoreCase))
                {
                    if (_visits.TryGetValue(id, out var open)) open.LastActiveAt = at;
                    _currentCluster = clusterId ?? _currentCluster;
                    _currentCategory = category;
                    return false;
                }

                if (previousId != null)
                {
                    if (_visits.TryGetValue(previousId, out var leaving)) leaving.LastActiveAt = at;

                    PushTransitionLocked(new LedgerTransition(previousId, _currentCluster, _currentCategory,
                        _segmentSeconds, at));

                    if (_apps.TryGetValue(previousId, out var prevApp)) prevApp.LastSeen = at;
                }

                DropStaleVisitsLocked(at, id);

                bool newVisit;
                if (_visits.TryGetValue(id, out var resumed) &&
                    (at - resumed.LastActiveAt).TotalSeconds <= ExcursionToleranceSeconds)
                {
                    resumed.LastActiveAt = at;
                    newVisit = false;
                }
                else
                {
                    bool firstEver = !_apps.ContainsKey(id);
                    var app = GetOrCreateAppLocked(id, clusterId);
                    var day = GetOrCreateDayLocked(app, at);
                    var previousLastSeen = app.LastSeen;

                    day.Visits++;
                    app.FirstSeen ??= at;

                    _visits[id] = new VisitState
                    {
                        Seq = day.Visits,
                        StartedAt = at,
                        LastActiveAt = at,
                        PreviousLastSeen = previousLastSeen,
                        IsFirstEver = firstEver,
                        DwellSeconds = 0
                    };
                    newVisit = true;
                }

                if (clusterId != null && _apps.TryGetValue(id, out var entry)) entry.Cluster = clusterId;

                _currentAppId = id;
                _currentCluster = clusterId;
                _currentCategory = category;
                _segmentStart = at;
                _segmentSeconds = 0;

                RequestSave();
                return newVisit;
            }
        }

        /// <summary>
        /// Closes the current segment without opening another — the machine locked, went idle, or the
        /// foreground resolved to something the privacy layer refused. Nothing accrues until the next
        /// <see cref="NoteFocus"/>.
        /// </summary>
        public void NoteFocusEnd(DateTime at)
        {
            lock (_lock)
            {
                Accrue(at);
                CheckRolloverLocked(at);

                if (_currentAppId == null) return;

                if (_visits.TryGetValue(_currentAppId, out var open)) open.LastActiveAt = at;

                PushTransitionLocked(new LedgerTransition(_currentAppId, _currentCluster, _currentCategory,
                    _segmentSeconds, at));

                if (_apps.TryGetValue(_currentAppId, out var app)) app.LastSeen = at;
                _currentAppId = null;
                _segmentSeconds = 0;
                RequestSave();
            }
        }

        // ===================== reading =====================

        /// <summary>The history block of a <see cref="ContextFrame"/> for one app, as of <paramref name="at"/>.</summary>
        public LedgerSnapshot Snapshot(string? appId, DateTime at)
        {
            var id = string.IsNullOrWhiteSpace(appId) ? AwarenessText.UnknownId : AwarenessText.SanitizeId(appId);

            lock (_lock)
            {
                Accrue(at);
                CheckRolloverLocked(at);
                return SnapshotLocked(id, at);
            }
        }

        private LedgerSnapshot SnapshotLocked(string id, DateTime at)
        {
            if (!_apps.TryGetValue(id, out var app)) return LedgerSnapshot.Empty with { DayArcSummary = BuildDayArcLocked(at) };

            var todayKey = DayKey(at);
            app.Days.TryGetValue(todayKey, out var today);

            _visits.TryGetValue(id, out var visit);

            int minutesWeek = 0;
            for (int d = 0; d < 7; d++)
            {
                if (app.Days.TryGetValue(DayKey(at.Date.AddDays(-d)), out var day)) minutesWeek += day.Seconds;
            }

            return new LedgerSnapshot(
                VisitsToday: today?.Visits ?? 0,
                MinutesToday: (today?.Seconds ?? 0) / 60,
                MinutesThisWeek: minutesWeek / 60,
                // Fixed at the moment the visit opened, not "now minus then": the gap the joke is about
                // is how long they stayed away, and it must not grow while they sit there.
                SinceLastVisit: visit?.PreviousLastSeen == null ? null : visit.StartedAt - visit.PreviousLastSeen.Value,
                DayStreak: DayStreakLocked(app, at),
                LongestDwellTodaySeconds: today?.LongestDwellSeconds ?? 0,
                CurrentVisitDwellSeconds: visit?.DwellSeconds ?? 0,
                SwitchesLast10Min: SwitchesLocked(at, TimeSpan.FromMinutes(10)),
                DayArcSummary: BuildDayArcLocked(at),
                FirstEverVisit: visit?.IsFirstEver ?? (app.Days.Count == 0),
                FirstVisitToday: (today?.Visits ?? 0) <= 1);
        }

        private int SwitchesLocked(DateTime at, TimeSpan window)
        {
            var cutoff = at - window;
            int count = 0;
            foreach (var t in _ring)
            {
                if (t.At >= cutoff) count++;
            }
            return count;
        }

        private int DayStreakLocked(AppEntry app, DateTime at)
        {
            var cursor = at.Date;
            if (!HasActivity(app, cursor))
            {
                cursor = cursor.AddDays(-1);
                if (!HasActivity(app, cursor)) return 0;
            }

            int streak = 0;
            while (HasActivity(app, cursor))
            {
                streak++;
                cursor = cursor.AddDays(-1);
            }
            return streak;

            static bool HasActivity(AppEntry entry, DateTime date) =>
                entry.Days.TryGetValue(DayKey(date), out var day) && (day.Visits > 0 || day.Seconds > 0);
        }

        /// <summary>
        /// "morning: work 2h → afternoon: youtube 40m → now". Built from the session ring plus the open
        /// visit, so it is bounded by the ring and never survives a restart. Ids only — the arc has to
        /// be safe to hand to a cloud model verbatim.
        /// </summary>
        private string BuildDayArcLocked(DateTime at)
        {
            var start = at.Date;
            var totals = new Dictionary<TimeBucket, Dictionary<string, int>>();

            void Add(TimeBucket bucket, string appId, int seconds)
            {
                if (seconds <= 0) return;
                if (!totals.TryGetValue(bucket, out var apps))
                {
                    apps = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    totals[bucket] = apps;
                }
                apps[appId] = apps.TryGetValue(appId, out var have) ? have + seconds : seconds;
            }

            foreach (var t in _ring)
            {
                if (t.At < start) continue;
                Add(BucketOf(t.At), ArcLabel(t.AppId, t.Cluster), t.DwellSeconds);
            }

            if (_currentAppId != null && _segmentSeconds > 0)
            {
                Add(BucketOf(_segmentStart), ArcLabel(_currentAppId, _currentCluster), _segmentSeconds);
            }

            if (totals.Count == 0) return "";

            var parts = new List<string>();
            foreach (TimeBucket bucket in Enum.GetValues(typeof(TimeBucket)))
            {
                if (!totals.TryGetValue(bucket, out var apps) || apps.Count == 0) continue;
                var top = apps.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).First();
                parts.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{bucket.ToString().ToLowerInvariant()}: {top.Key} {FormatDuration(top.Value)}"));
            }

            parts.Add("now");
            return string.Join(" → ", parts);
        }

        /// <summary>
        /// What an app is called inside the day arc. Adult-cluster apps collapse to the cluster id.
        ///
        /// <para>The arc is a plain string that rides into the cloud projection of EVERY frame, not
        /// just adult ones — so without this, one visit to a site_eh app would put that site's id in
        /// front of the model for the rest of the day, straight past the §6.1 rule that only the cluster
        /// id ever crosses the wire for that cluster.</para>
        /// </summary>
        private static string ArcLabel(string appId, string? cluster) =>
            string.Equals(cluster, AwarenessClusters.Adult, StringComparison.OrdinalIgnoreCase)
                ? AwarenessClusters.Adult
                : appId;

        private static string FormatDuration(int seconds)
        {
            int minutes = Math.Max(1, seconds / 60);
            int hours = minutes / 60;
            int rest = minutes % 60;
            if (hours <= 0) return string.Create(CultureInfo.InvariantCulture, $"{minutes}m");
            return rest == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{hours}h")
                : string.Create(CultureInfo.InvariantCulture, $"{hours}h{rest}m");
        }

        // ===================== trends =====================

        /// <summary>
        /// Derives the trend events for a frame AND consumes their one-shot guards in one step. Kept
        /// for callers that genuinely deliver whatever they derive (and for tests that pin the
        /// once-only semantics); the observer uses <see cref="PeekTrends"/> +
        /// <see cref="CommitTrends"/> instead, because it cannot know at derivation time whether the
        /// frame will ever be spoken.
        /// </summary>
        /// <param name="inputIdleSecondsBeforeWake">Real input idle that immediately preceded this frame.</param>
        /// <param name="mediaRepeatCount">Consecutive plays of the current track, counted by the observer.</param>
        public IReadOnlyList<TrendEvent> DeriveTrends(string? appId, string? cluster, DateTime at,
            int inputIdleSecondsBeforeWake = 0, int mediaRepeatCount = 0)
        {
            var derivation = PeekTrends(appId, cluster, at, inputIdleSecondsBeforeWake, mediaRepeatCount);
            CommitTrends(derivation);
            return derivation.Trends;
        }

        /// <summary>
        /// Derives the trend events for a frame about <paramref name="appId"/> (doc 02 §2.4) WITHOUT
        /// consuming their one-shot guards.
        ///
        /// <para><b>Why derivation and consumption are separate.</b> A LongHaul milestone, a Streak, a
        /// ReturnVisit number and a NightShift each fire once per day/night and then stay quiet. If the
        /// guard burns when the trend is DERIVED, every frame that is scored below threshold, refused
        /// by the arbiter's global gap, starved by the hourly budget or dropped as stale takes that
        /// day's best callback with it — permanently, because the guard set only clears on rollover.
        /// The DND path already returned early for exactly this reason; the other exits did not.</para>
        ///
        /// <para>Hand the returned token to <see cref="CommitTrends"/> only once a line has actually
        /// reached the user.</para>
        /// </summary>
        public TrendDerivation PeekTrends(string? appId, string? cluster, DateTime at,
            int inputIdleSecondsBeforeWake = 0, int mediaRepeatCount = 0)
        {
            var id = string.IsNullOrWhiteSpace(appId) ? AwarenessText.UnknownId : AwarenessText.SanitizeId(appId);
            var clusterId = string.IsNullOrWhiteSpace(cluster) ? null : AwarenessText.SanitizeId(cluster);

            lock (_lock)
            {
                Accrue(at);
                CheckRolloverLocked(at);

                var snap = SnapshotLocked(id, at);
                var day = DayKey(at);
                _visits.TryGetValue(id, out var visit);
                int seq = visit?.Seq ?? 0;

                var trends = new List<TrendEvent>(2);
                var guards = new List<string>(2);

                TrendEvent Make(TrendKind kind, int magnitude) => new(
                    kind, id, clusterId, magnitude,
                    snap.VisitsToday, snap.MinutesToday, snap.CurrentVisitDwellSeconds, snap.SinceLastVisit);

                // Reserve, do not consume: true when this guard has not fired yet AND has not already
                // been reserved by this same derivation. The reservation only becomes permanent in
                // CommitTrends, i.e. only if a line reaches the user.
                bool Available(string key) =>
                    !_emitted.Contains(key) && !guards.Contains(key, StringComparer.Ordinal) && Reserve(key);

                bool Reserve(string key) { guards.Add(key); return true; }

                // ReturnVisit — the nth arrival today.
                if (snap.VisitsToday >= ReturnVisitMinimum &&
                    Available(Key("rv", id, day, snap.VisitsToday)))
                {
                    trends.Add(Make(TrendKind.ReturnVisit, snap.VisitsToday));
                }

                // LongHaul — largest newly-crossed cumulative-dwell milestone; smaller ones are consumed
                // silently so a 2h session does not fire 30m, 1h and 2h in a row.
                int? crossed = null;
                foreach (var milestone in LongHaulMilestonesMinutes)
                {
                    if (snap.CurrentVisitDwellSeconds < milestone * 60) continue;
                    if (Available(Key("lh", id, day, seq * 1000 + milestone))) crossed = milestone;
                }
                if (crossed.HasValue) trends.Add(Make(TrendKind.LongHaul, crossed.Value));

                // Streak — same app d days running. One mention per day; the number does not change.
                if (snap.DayStreak >= StreakMinimumDays && Available(Key("st", id, day, 0)))
                {
                    trends.Add(Make(TrendKind.Streak, snap.DayStreak));
                }

                // MediaLoop — k consecutive plays of the same track (counter fed by the observer).
                if (mediaRepeatCount >= MediaLoopMinimum && Available(Key("ml", id, day, mediaRepeatCount)))
                {
                    trends.Add(Make(TrendKind.MediaLoop, mediaRepeatCount));
                }

                // Backslide — a doomscroll app closed and reopened inside five minutes. Only a genuinely
                // new visit counts: an excursion under the tolerance never ended the visit at all.
                if (string.Equals(clusterId, AwarenessClusters.Doomscroll, StringComparison.OrdinalIgnoreCase) &&
                    visit != null && snap.SinceLastVisit is { } gap &&
                    gap.TotalSeconds > ExcursionToleranceSeconds && gap.TotalSeconds <= BacksideWindowSeconds &&
                    Available(Key("bs", id, day, seq)))
                {
                    trends.Add(Make(TrendKind.Backslide, (int)Math.Round(gap.TotalSeconds)));
                }

                // GhostTown — first activity after a long REAL idle. Keyed to the minute of the wake, so
                // a second nap the same day gets its own greeting.
                if (inputIdleSecondsBeforeWake >= GhostTownMinimumIdleSeconds &&
                    Available(Key("gt", id, at.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture), 0)))
                {
                    trends.Add(Make(TrendKind.GhostTown, Math.Max(1, inputIdleSecondsBeforeWake / 3600)));
                }

                // NightShift — past this machine's own learned bedtime. Purely local maths.
                if (TryNightShiftLocked(at, out int hoursPast) &&
                    Available(Key("ns", "", DayKey(NightDateOf(at)), 0)))
                {
                    trends.Add(Make(TrendKind.NightShift, hoursPast));
                }

                return new TrendDerivation(trends, guards);
            }
        }

        /// <summary>
        /// Permanently consumes the one-shot guards a <see cref="PeekTrends"/> reserved. Called ONLY
        /// after a line has actually reached the user — the same rule the cooldown ledger follows, for
        /// the same reason: a joke that was never told has not been told.
        ///
        /// <para>Idempotent, and a no-op for a null or empty derivation.</para>
        /// </summary>
        public void CommitTrends(TrendDerivation? derivation)
        {
            if (derivation == null || derivation.Guards.Count == 0) return;
            lock (_lock)
            {
                foreach (var key in derivation.Guards) _emitted.Add(key);
            }
        }

        /// <summary>
        /// The learned typical bedtime as a night-index hour (20 = 20:00, 25 = 01:00 next morning), or
        /// null when this machine has not shown enough nights to have a habit worth naming.
        ///
        /// <para>Median rather than mean: one all-nighter should not move the boundary the companion
        /// jokes about for the next fortnight.</para>
        /// </summary>
        public double? LearnedBedtimeHour(DateTime at)
        {
            lock (_lock) return LearnedBedtimeLocked(at);
        }

        private double? LearnedBedtimeLocked(DateTime at)
        {
            var currentNight = NightDateOf(at);
            var samples = new List<int>(BedtimeSampleDays);

            for (int d = 1; d <= BedtimeSampleDays; d++)
            {
                var hour = LatestNightHourLocked(currentNight.AddDays(-d));
                if (hour.HasValue) samples.Add(hour.Value);
            }

            if (samples.Count < BedtimeMinimumSamples) return null;

            samples.Sort();
            int mid = samples.Count / 2;
            return samples.Count % 2 == 1
                ? samples[mid]
                : (samples[mid - 1] + samples[mid]) / 2.0;
        }

        private bool TryNightShiftLocked(DateTime at, out int hoursPast)
        {
            hoursPast = 0;

            int index = NightIndex(at.Hour);
            if (index < NightWindowStartHour) return false;

            var learned = LearnedBedtimeLocked(at);
            if (learned == null) return false;

            double past = index - learned.Value;
            if (past < 1.0) return false;

            hoursPast = Math.Max(1, (int)Math.Floor(past));
            return true;
        }

        private int? LatestNightHourLocked(DateTime nightDate)
        {
            int? latest = null;

            if (_nightHistogram.TryGetValue(DayKey(nightDate), out var evening))
            {
                for (int h = NightWindowStartHour; h < 24; h++)
                {
                    if (evening[h] >= NightHourMinimumSeconds) latest = Math.Max(latest ?? 0, h);
                }
            }

            if (_nightHistogram.TryGetValue(DayKey(nightDate.AddDays(1)), out var morning))
            {
                for (int h = 0; h < NightWindowEndHour; h++)
                {
                    if (morning[h] >= NightHourMinimumSeconds) latest = Math.Max(latest ?? 0, h + 24);
                }
            }

            return latest;
        }

        /// <summary>Maps an hour to the night-index space where 01:00 (25) is later than 23:00 (23).</summary>
        private static int NightIndex(int hour) => hour < NightWindowEndHour ? hour + 24 : hour;

        /// <summary>The date a night "belongs to": 01:00 on the 7th is part of the night that began on the 6th.</summary>
        private static DateTime NightDateOf(DateTime at) =>
            at.Hour < NightWindowEndHour ? at.Date.AddDays(-1) : at.Date;

        private static string Key(string kind, string id, string day, int magnitude) =>
            string.Create(CultureInfo.InvariantCulture, $"{kind}|{id}|{day}|{magnitude}");

        // ===================== accrual =====================

        private void Accrue(DateTime at)
        {
            if (_currentAppId == null)
            {
                _segmentStart = at;
                return;
            }

            if (at <= _segmentStart) return;

            var total = at - _segmentStart;
            if (total.TotalSeconds > MaxSingleAccrualSeconds)
            {
                // Machine sleep, a suspended process or a clock jump. None of it was screen time.
                App.Logger?.Debug("ActivityLedger: discarded a {Seconds}s gap as machine sleep",
                    (int)total.TotalSeconds);
                _segmentStart = at;
                return;
            }

            var cursor = _segmentStart;
            while (cursor < at)
            {
                var nextHour = cursor.Date.AddHours(cursor.Hour + 1);
                var sliceEnd = nextHour < at ? nextHour : at;
                int seconds = (int)(sliceEnd - cursor).TotalSeconds;
                if (seconds > 0) AddSliceLocked(_currentAppId, _currentCluster, cursor, seconds);
                cursor = sliceEnd;
            }

            _segmentStart = at;
        }

        private void AddSliceLocked(string appId, string? cluster, DateTime sliceStart, int seconds)
        {
            var app = GetOrCreateAppLocked(appId, cluster);
            var day = GetOrCreateDayLocked(app, sliceStart);

            day.Seconds += seconds;
            day.Buckets[(int)BucketOf(sliceStart)] += seconds;
            app.LastSeen = sliceStart.AddSeconds(seconds);
            app.FirstSeen ??= sliceStart;

            var bins = NightBinsLocked(sliceStart);
            bins[sliceStart.Hour] += seconds;

            _segmentSeconds += seconds;

            if (_visits.TryGetValue(appId, out var visit))
            {
                visit.DwellSeconds += seconds;
                visit.LastActiveAt = app.LastSeen.Value;
                if (visit.DwellSeconds > day.LongestDwellSeconds) day.LongestDwellSeconds = visit.DwellSeconds;
            }

            RequestSave();
        }

        private void CheckRolloverLocked(DateTime at)
        {
            if (at.Date == _lastRolloverDate) return;

            _lastRolloverDate = at.Date;
            _emitted.Clear();

            // A visit that spans midnight is still a visit on the new day. `Visits` is only ever
            // incremented by NoteFocus's new-visit branch, and a foreground that never changes takes
            // its early return — so without this the new day accrues MINUTES with a visit count of
            // ZERO, and a frame cut at 00:30 after three hours reports "visits_today: 0,
            // minutes_today: 30" and re-reads as "first visit today". Wrong numbers are the one bug
            // this class cannot survive.
            if (_currentAppId != null && _apps.TryGetValue(_currentAppId, out var live))
            {
                var newDay = GetOrCreateDayLocked(live, at);
                if (newDay.Visits == 0)
                {
                    newDay.Visits = 1;
                    if (_visits.TryGetValue(_currentAppId, out var open)) open.Seq = 1;
                }
            }

            PruneRetentionLocked(at);
            App.Logger?.Information("ActivityLedger: day rollover to {Day}", DayKey(at));
            RequestSave();
        }

        private void PushTransitionLocked(LedgerTransition transition)
        {
            _ring.AddLast(transition);
            while (_ring.Count > SessionRingCapacity) _ring.RemoveFirst();
        }

        private void DropStaleVisitsLocked(DateTime at, string keepId)
        {
            List<string>? stale = null;
            foreach (var pair in _visits)
            {
                if (string.Equals(pair.Key, keepId, StringComparison.OrdinalIgnoreCase)) continue;
                if ((at - pair.Value.LastActiveAt).TotalSeconds <= ExcursionToleranceSeconds) continue;
                (stale ??= new List<string>()).Add(pair.Key);
            }

            if (stale == null) return;
            foreach (var key in stale) _visits.Remove(key);
        }

        private AppEntry GetOrCreateAppLocked(string appId, string? cluster)
        {
            if (!_apps.TryGetValue(appId, out var app))
            {
                app = new AppEntry { Cluster = cluster };
                _apps[appId] = app;
            }
            else if (cluster != null)
            {
                app.Cluster = cluster;
            }
            return app;
        }

        private static DayEntry GetOrCreateDayLocked(AppEntry app, DateTime at)
        {
            var key = DayKey(at);
            if (!app.Days.TryGetValue(key, out var day))
            {
                day = new DayEntry();
                app.Days[key] = day;
            }
            return day;
        }

        private int[] NightBinsLocked(DateTime at)
        {
            var key = DayKey(at);
            if (!_nightHistogram.TryGetValue(key, out var bins))
            {
                bins = new int[24];
                _nightHistogram[key] = bins;
            }
            return bins;
        }

        /// <summary>Local day key. The only date format that ever reaches the file.</summary>
        internal static string DayKey(DateTime at) => at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>Six-hour bucket of a local timestamp.</summary>
        public static TimeBucket BucketOf(DateTime at) => (TimeBucket)(at.Hour / 6);

        // ===================== retention & erasure =====================

        /// <summary>
        /// Drops day entries (and the machine histogram) older than the retention window, then any app
        /// left with no days at all. Called on <see cref="Start"/> and on every day rollover.
        /// </summary>
        public void PruneRetention(DateTime at)
        {
            lock (_lock) PruneRetentionLocked(at);
        }

        private void PruneRetentionLocked(DateTime at)
        {
            int days = Math.Clamp(_retentionDays(), 1, 365);
            var cutoff = DayKey(at.Date.AddDays(-(days - 1)));

            int droppedDays = 0;
            var emptyApps = new List<string>();

            foreach (var pair in _apps)
            {
                var stale = pair.Value.Days.Keys
                    .Where(k => string.CompareOrdinal(k, cutoff) < 0)
                    .ToList();

                foreach (var key in stale) pair.Value.Days.Remove(key);
                droppedDays += stale.Count;

                if (pair.Value.Days.Count == 0) emptyApps.Add(pair.Key);
            }

            foreach (var appId in emptyApps)
            {
                // The app in the foreground right now is live even with no day rows yet — dropping it
                // would orphan its visit. Everything else goes, visit state included.
                if (string.Equals(_currentAppId, appId, StringComparison.OrdinalIgnoreCase)) continue;
                _apps.Remove(appId);
                _visits.Remove(appId);
            }

            var staleNights = _nightHistogram.Keys.Where(k => string.CompareOrdinal(k, cutoff) < 0).ToList();
            foreach (var key in staleNights) _nightHistogram.Remove(key);

            if (droppedDays > 0 || emptyApps.Count > 0 || staleNights.Count > 0)
            {
                App.Logger?.Information(
                    "ActivityLedger: pruned {Days} day record(s), {Apps} app(s), {Nights} night histogram(s) past {Retention}d",
                    droppedDays, emptyApps.Count, staleNights.Count, days);
                RequestSave();
            }
        }

        /// <summary>
        /// Erases everything this class has ever created. The complete artifact list, because a purge
        /// that misses one of them is a purge that failed:
        /// <list type="number">
        /// <item><see cref="LedgerPath"/> — awareness_ledger.json;</item>
        /// <item><see cref="LedgerTempPath"/> — the ".tmp" sibling an interrupted atomic write leaves
        /// behind, which holds a full copy of the data and which no other code path deletes;</item>
        /// <item>the in-memory per-app day counters;</item>
        /// <item>the machine-wide hourly histogram (the bedtime evidence);</item>
        /// <item>the in-memory session ring of transitions;</item>
        /// <item>every open and suspended visit, including the one in progress;</item>
        /// <item>the one-shot trend guard set;</item>
        /// <item>the pending debounced save — cancelled, so a queued write cannot resurrect any of it.</item>
        /// </list>
        ///
        /// <para>Not in this list because this class never creates them: delivered lines (the ban list,
        /// <see cref="ICompanionMemory.ForgetAsync"/>) and awareness turns held in the brain's session.
        /// The privacy panel calls both.</para>
        /// </summary>
        public void Wipe()
        {
            lock (_lock)
            {
                ClearInMemoryLocked();
                _currentCategory = ActivityCategory.Unknown;
                _segmentStart = _clock();

                // Invalidates any save that has already serialised but not yet written. See _generation.
                _generation++;
            }

            CancelPendingSave();

            lock (_writeLock)
            {
                DeleteQuietly(_path);
                DeleteQuietly(LedgerTempPath);
            }

            App.Logger?.Information("ActivityLedger: wiped");
        }

        /// <summary>
        /// Forgets one app completely: its counters, its place in the session ring, its open visit and
        /// its trend guards, then writes through immediately rather than waiting for the debounce — a
        /// "forget this" that survives on disk until the next unrelated save is not a forget.
        ///
        /// <para>The machine-wide hourly histogram is deliberately untouched: it records only that the
        /// machine was awake at an hour and carries no app identity, so removing it would degrade the
        /// bedtime maths without erasing anything about the app.</para>
        /// </summary>
        public void Forget(string? appId)
        {
            if (string.IsNullOrWhiteSpace(appId)) return;
            var id = AwarenessText.SanitizeId(appId);

            lock (_lock)
            {
                _apps.Remove(id);
                _visits.Remove(id);

                var doomed = _ring.Where(t => string.Equals(t.AppId, id, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var t in doomed) _ring.Remove(t);

                _emitted.RemoveWhere(k => k.Contains(string.Create(CultureInfo.InvariantCulture, $"|{id}|"), StringComparison.Ordinal));

                if (string.Equals(_currentAppId, id, StringComparison.OrdinalIgnoreCase))
                {
                    _currentAppId = null;
                    _currentCluster = null;
                    _segmentSeconds = 0;
                }

                // Same race as Wipe: a debounced save that serialised before this call must not write
                // the forgotten app back out.
                _generation++;
            }

            SaveNow();
            App.Logger?.Information("ActivityLedger: forgot {App}", id);
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ActivityLedger: failed to delete {File}", path);
            }
        }

        // ===================== persistence =====================

        /// <summary>Schedules a debounced background save. Cheap and safe to call on every mutation.</summary>
        public void RequestSave()
        {
            if (_disposed) return;
            try { _saveTimer?.Change(SaveDebounce, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }

        private void CancelPendingSave()
        {
            try { _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite); }
            catch (ObjectDisposedException) { }
        }

        /// <summary>Writes the ledger synchronously. Used on stop, on forget and by tests.</summary>
        public void SaveNow()
        {
            string json;
            long generation;
            try
            {
                lock (_lock)
                {
                    json = SerializeLocked();
                    generation = _generation;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ActivityLedger: failed to serialize");
                return;
            }

            WriteSnapshotIfCurrent(json, generation);
        }

        /// <summary>
        /// The write half of <see cref="SaveNow"/>, split out so the erasure race is testable rather
        /// than only arguable.
        ///
        /// <para><paramref name="generation"/> is the value <see cref="_generation"/> had when
        /// <paramref name="json"/> was serialised. If a <see cref="Wipe"/> or a <see cref="Forget"/>
        /// happened in between, this snapshot is a copy of data the user just erased and writing it
        /// would recreate <c>awareness_ledger.json</c> with every counter intact — the one failure a
        /// "forget everything" button cannot have. Erasure wins the race by construction.</para>
        /// </summary>
        internal void WriteSnapshotIfCurrent(string json, long generation)
        {
            lock (_writeLock)
            {
                if (Volatile.Read(ref _generation) != generation)
                {
                    App.Logger?.Debug("ActivityLedger: dropped a stale save (the ledger was erased mid-write)");
                    return;
                }

                try
                {
                    var dir = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    AtomicWrite(_path, json);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "ActivityLedger: failed to persist");
                }
            }
        }

        /// <summary>
        /// The serialise half of <see cref="SaveNow"/> — the JSON plus the generation it belongs to.
        /// Split out for the same reason as <see cref="WriteSnapshotIfCurrent"/>.
        /// </summary>
        internal (string Json, long Generation) SnapshotForWrite()
        {
            lock (_lock) return (SerializeLocked(), _generation);
        }

        /// <summary>
        /// Ages the on-disk ledger out to the retention window WITHOUT starting the observer — load,
        /// prune, write back, release.
        ///
        /// <para><b>Why it exists.</b> Every other pruning path hangs off <see cref="Start"/>, which
        /// returns early when awareness is switched off. A user who ran awareness for three weeks and
        /// then turned it off would keep those three weeks on disk forever, while the consent dialog
        /// and the settings notice both say the counts are deleted after the retention period. This is
        /// called unconditionally at startup so the promise holds in exactly the state a
        /// privacy-conscious user puts the feature into.</para>
        ///
        /// <para>A no-op when there is no file (it must never CREATE one) or when the ledger is
        /// already live, in which case the running instance owns pruning.</para>
        /// </summary>
        public void PruneOnDisk()
        {
            if (_disposed) return;

            string json;
            long generation;
            lock (_lock)
            {
                if (_started) { PruneRetentionLocked(_clock()); return; }

                try
                {
                    if (!File.Exists(_path)) return;
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("ActivityLedger: retention sweep could not stat the ledger - {Error}", ex.Message);
                    return;
                }

                Load();
                PruneRetentionLocked(_clock());

                try { json = SerializeLocked(); }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "ActivityLedger: retention sweep failed to serialize");
                    ClearInMemoryLocked();
                    return;
                }

                generation = _generation;

                // Release the loaded state again: this instance is not started, and leaving a loaded
                // copy behind would let Start() skip Load() and work from a snapshot taken at boot.
                ClearInMemoryLocked();
            }

            lock (_writeLock)
            {
                if (Volatile.Read(ref _generation) != generation) return;
                try { AtomicWrite(_path, json); }
                catch (Exception ex) { App.Logger?.Warning(ex, "ActivityLedger: retention sweep failed to persist"); }
            }

            App.Logger?.Information("ActivityLedger: retention swept on disk (awareness need not be running)");
        }

        private void ClearInMemoryLocked()
        {
            _apps.Clear();
            _nightHistogram.Clear();
            _visits.Clear();
            _ring.Clear();
            _emitted.Clear();
            _currentAppId = null;
            _currentCluster = null;
            _segmentSeconds = 0;
        }

        /// <summary>
        /// Temp-then-move, the same shape as <c>MemoryStore.AtomicWrite</c>: a truncating in-place write
        /// that is interrupted leaves a half-document, and a half-document parses as "empty" here, which
        /// silently resets every counter the feature exists to be right about.
        /// </summary>
        internal static void AtomicWrite(string path, string json)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json, Encoding.UTF8);
            File.Move(tmp, path, overwrite: true);
        }

        private string SerializeLocked()
        {
            var payload = new PersistedLedger
            {
                Version = SchemaVersion,
                Apps = _apps.ToDictionary(
                    p => p.Key,
                    p => new PersistedApp
                    {
                        Cluster = p.Value.Cluster,
                        FirstSeen = p.Value.FirstSeen,
                        LastSeen = p.Value.LastSeen,
                        Days = p.Value.Days.ToDictionary(
                            d => d.Key,
                            d => new PersistedDay
                            {
                                Visits = d.Value.Visits,
                                Seconds = d.Value.Seconds,
                                LongestDwellSeconds = d.Value.LongestDwellSeconds,
                                Buckets = d.Value.Buckets
                            },
                            StringComparer.Ordinal)
                    },
                    StringComparer.OrdinalIgnoreCase),
                Hours = _nightHistogram.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)
            };

            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        /// <summary>
        /// Reads the file. A missing, empty, truncated or otherwise unreadable ledger is not an error
        /// worth surfacing — it means she starts counting again from today, which is annoying and
        /// harmless. It must never be a crash on a background timer.
        /// </summary>
        private void Load()
        {
            string json;
            try
            {
                if (!File.Exists(_path)) return;
                json = File.ReadAllText(_path);
                if (string.IsNullOrWhiteSpace(json)) return;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ActivityLedger: could not read the ledger — starting empty");
                return;
            }

            PersistedLedger? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<PersistedLedger>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ActivityLedger: ledger file is corrupt — starting empty");
                return;
            }

            if (parsed?.Apps == null) return;

            foreach (var pair in parsed.Apps)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) continue;
                var id = AwarenessText.SanitizeId(pair.Key);
                if (id == AwarenessText.UnknownId) continue;

                var app = new AppEntry
                {
                    Cluster = string.IsNullOrWhiteSpace(pair.Value.Cluster) ? null : AwarenessText.SanitizeId(pair.Value.Cluster),
                    FirstSeen = pair.Value.FirstSeen,
                    LastSeen = pair.Value.LastSeen
                };

                if (pair.Value.Days != null)
                {
                    foreach (var day in pair.Value.Days)
                    {
                        if (!IsDayKey(day.Key) || day.Value == null) continue;
                        app.Days[day.Key] = new DayEntry
                        {
                            Visits = Math.Max(0, day.Value.Visits),
                            Seconds = Math.Max(0, day.Value.Seconds),
                            LongestDwellSeconds = Math.Max(0, day.Value.LongestDwellSeconds),
                            Buckets = NormalizeBuckets(day.Value.Buckets, 4)
                        };
                    }
                }

                if (app.Days.Count > 0) _apps[id] = app;
            }

            if (parsed.Hours != null)
            {
                foreach (var pair in parsed.Hours)
                {
                    if (!IsDayKey(pair.Key)) continue;
                    _nightHistogram[pair.Key] = NormalizeBuckets(pair.Value, 24);
                }
            }

            App.Logger?.Information("ActivityLedger: loaded {Apps} app(s)", _apps.Count);
        }

        private static int[] NormalizeBuckets(int[]? raw, int size)
        {
            var buckets = new int[size];
            if (raw == null) return buckets;
            for (int i = 0; i < size && i < raw.Length; i++) buckets[i] = Math.Max(0, raw[i]);
            return buckets;
        }

        private static bool IsDayKey(string? key) =>
            !string.IsNullOrEmpty(key) &&
            DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _);

        // ===================== shapes =====================

        private sealed class VisitState
        {
            public int Seq;
            public DateTime StartedAt;
            public DateTime LastActiveAt;
            public DateTime? PreviousLastSeen;
            public bool IsFirstEver;
            public int DwellSeconds;
        }

        private sealed class AppEntry
        {
            public string? Cluster;
            public DateTime? FirstSeen;
            public DateTime? LastSeen;
            public readonly Dictionary<string, DayEntry> Days = new(StringComparer.Ordinal);
        }

        private sealed class DayEntry
        {
            public int Visits;
            public int Seconds;
            public int LongestDwellSeconds;
            public int[] Buckets = new int[4];
        }

        private sealed class PersistedLedger
        {
            public int Version { get; set; } = SchemaVersion;
            public Dictionary<string, PersistedApp>? Apps { get; set; }
            public Dictionary<string, int[]>? Hours { get; set; }
        }

        private sealed class PersistedApp
        {
            public string? Cluster { get; set; }
            public DateTime? FirstSeen { get; set; }
            public DateTime? LastSeen { get; set; }
            public Dictionary<string, PersistedDay>? Days { get; set; }
        }

        private sealed class PersistedDay
        {
            public int Visits { get; set; }
            public int Seconds { get; set; }
            public int LongestDwellSeconds { get; set; }
            public int[] Buckets { get; set; } = new int[4];
        }
    }
}
