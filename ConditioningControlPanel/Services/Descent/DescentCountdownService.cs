using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>
    /// How close the ceremony is, named. The boundaries are the contract's
    /// (CONTRACT-FUSE-0816 §2.1) and every surface keys off this enum rather than off raw
    /// arithmetic, so "when does the spark appear" has exactly one answer in the codebase.
    ///
    /// <para>Ordered from far to near ON PURPOSE: surfaces ask
    /// <c>phase &gt;= DescentFusePhase.Clock</c> ("from the Clock phase onward"), which is how the
    /// contract phrases every rule. <see cref="Dark"/> sorts below everything, so that comparison
    /// is also the "is anything showing at all" check.</para>
    /// </summary>
    public enum DescentFusePhase
    {
        /// <summary>No fuse: no cached timestamp, or more than seven days out. Nothing renders.</summary>
        Dark = 0,
        /// <summary>≤7 days. The spark appears. No digits, no explanation.</summary>
        Whisper = 1,
        /// <summary>≤72 hours. Hovering the spark reads out T-minus.</summary>
        Clock = 2,
        /// <summary>≤24 hours. Neutral chrome starts darkening, one step per 6h.</summary>
        Dimming = 3,
        /// <summary>≤12 hours. A candle beside the companion.</summary>
        Candle = 4,
        /// <summary>≤1 hour. The readout is promoted to a persistent corner element.</summary>
        Vigil = 5,
        /// <summary>≤10 minutes. Gold digits, then silence.</summary>
        Terminal = 6,
        /// <summary>The instant has passed.</summary>
        Zero = 7,
    }

    /// <summary>Old and new, for handlers that care which way the phase moved.</summary>
    public sealed class DescentFusePhaseChangedEventArgs : EventArgs
    {
        public DescentFusePhaseChangedEventArgs(DescentFusePhase previous, DescentFusePhase current)
        {
            Previous = previous;
            Current = current;
        }

        public DescentFusePhase Previous { get; }
        public DescentFusePhase Current { get; }
    }

    /// <summary>
    /// THE FUSE — the countdown to the migration ceremony, and the only clock any tease surface
    /// reads (CONTRACT-FUSE-0816 §2.1).
    ///
    /// <para><b>DORMANCY, stated once so it can be checked.</b> Everything here hangs off one
    /// nullable string: <see cref="AppSettings.DescentCeremonyAtUtc"/>. With no cached timestamp
    /// there is no timer, no event, no surface and no network request — <see cref="Start"/> arms
    /// nothing and returns. That string is written by exactly one place (ProfileSyncService, from
    /// the additive <c>descent_countdown</c> block on a /v2/user/sync response) and the server does
    /// not send that block until the owner sets DESCENT_CEREMONY_AT. On today's server this whole
    /// file is unreachable, and every surface is byte-identical to the build before it.</para>
    ///
    /// <para><b>The kill switch is live, not just at launch.</b> Clearing the cached timestamp —
    /// which a sync that no longer carries the block does automatically — tears the timer down,
    /// restores the chrome, and drops every surface back to Dark on the next
    /// <see cref="PhaseChanged"/>. Mid-flight, without a restart. That is tested.</para>
    ///
    /// <para><b>Offline law (§0).</b> The countdown runs off the CACHED timestamp and nothing else,
    /// so an offline install still counts down correctly. The only network this service does is one
    /// optional /config fetch that refreshes the presence count, and it takes the same hard floor
    /// every other network path takes: OfflineMode, no unified id, or no auth token ⇒ no request.
    /// A keyless install that never synced has no cached timestamp at all, so it has no countdown —
    /// and that is the correct answer, not a gap.</para>
    ///
    /// <para><b>Threading.</b> The clock is a single <see cref="DispatcherTimer"/>, so
    /// <see cref="Tick"/>, <see cref="PhaseChanged"/> and <see cref="ZeroReached"/> all arrive on
    /// the UI thread and handlers may touch visuals directly. <see cref="ApplyCeremonyAt"/> is the
    /// one entry point that can be called from a background sync continuation, and it marshals with
    /// the CLAUDE.md rule-6/8 guards (bail if the dispatcher is gone or shutting down).</para>
    ///
    /// <para><b>Public contract for the zero-show lane.</b> That lane (§2.3/§2.4) builds on top of
    /// this service and consumes, specifically: <see cref="ZeroReached"/> to open the live show,
    /// <see cref="ZeroPassedWhileAway"/> + <see cref="ShouldPlayCatchUp"/> to decide whether a
    /// launch owes the subject the condensed crack instead, <see cref="MarkLastNightWitnessed"/> /
    /// <see cref="MarkCatchUpCrackPlayed"/> to record what was actually watched, and
    /// <see cref="AudioEnabled"/> for the heartbeat hook's gate. None of those need the timer to be
    /// running to answer.</para>
    /// </summary>
    public sealed class DescentCountdownService : IDisposable
    {
        // ---- phase boundaries (the contract's, in one place) -------------------

        internal static readonly TimeSpan WhisperAt  = TimeSpan.FromDays(7);
        internal static readonly TimeSpan ClockAt    = TimeSpan.FromHours(72);
        internal static readonly TimeSpan DimmingAt  = TimeSpan.FromHours(24);
        internal static readonly TimeSpan CandleAt   = TimeSpan.FromHours(12);
        internal static readonly TimeSpan VigilAt    = TimeSpan.FromHours(1);
        internal static readonly TimeSpan TerminalAt = TimeSpan.FromMinutes(10);

        /// <summary>Coarse cadence. Thirty seconds is invisible on a day counter and costs nothing.</summary>
        private static readonly TimeSpan SlowTick = TimeSpan.FromSeconds(30);

        /// <summary>Fine cadence, inside the final hour, where seconds are the whole point.</summary>
        private static readonly TimeSpan FastTick = TimeSpan.FromSeconds(1);

        private const string ConfigEndpoint = "https://codebambi-proxy.vercel.app/config/descent-countdown";

        /// <summary>How often the presence count may be re-fetched while the vigil is on.</summary>
        private static readonly TimeSpan VigilRefetchEvery = TimeSpan.FromMinutes(5);

        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private readonly HashSet<DescentFusePhase> _spokenPhases = new();

        private DispatcherTimer? _timer;
        private bool _fastCadence;
        private bool _started;
        private bool _disposed;
        private bool _zeroRaised;
        private bool _scriptedSilence;
        private DateTime _lastConfigFetchUtc = DateTime.MinValue;

        // ------------------------------------------------------------------
        // Events (UI thread; see the threading note above)
        // ------------------------------------------------------------------

        /// <summary>
        /// The phase moved. Fires on the transition only — including the transition to
        /// <see cref="DescentFusePhase.Dark"/> when the kill switch fires, which is a surface's cue
        /// to tear itself down. Also fires once at <see cref="Start"/> if the fuse is already lit,
        /// so a surface that subscribes at construction never has to poll for its initial state.
        /// </summary>
        public event EventHandler<DescentFusePhaseChangedEventArgs>? PhaseChanged;

        /// <summary>
        /// Every tick, with the time left. 30s cadence, 1s inside the final hour. Carries
        /// <see cref="TimeSpan.Zero"/> at and after the instant — never a negative span, so a
        /// readout cannot render a minus sign by accident.
        /// </summary>
        public event EventHandler<TimeSpan>? Tick;

        /// <summary>
        /// The instant passed WHILE THIS SESSION WAS WATCHING. Raised exactly once per process, on
        /// the UI thread, and never for an app that launched after the fact — that case is
        /// <see cref="ZeroPassedWhileAway"/> and belongs to the catch-up path. The zero-show lane
        /// opens its window from here.
        /// </summary>
        public event EventHandler? ZeroReached;

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------

        /// <summary>
        /// The ceremony instant (UTC), or null when there is no fuse. Re-parsed from settings on
        /// every read: the cached string is the single source of truth, so a sync that rewrites or
        /// clears it is felt immediately by every reader without an invalidation dance.
        /// </summary>
        public DateTime? CeremonyAtUtc => ParseCeremonyAt(App.Settings?.Current?.DescentCeremonyAtUtc);

        /// <summary>True when a usable timestamp is cached. The one-line "is the fuse lit" check.</summary>
        public bool IsArmed => CeremonyAtUtc.HasValue;

        /// <summary>
        /// Time until the ceremony, floored at zero, or null when unarmed. Computed live from the
        /// clock — no staleness between ticks.
        /// </summary>
        public TimeSpan? Remaining
        {
            get
            {
                var at = CeremonyAtUtc;
                if (at is null) return null;
                var left = at.Value - DateTime.UtcNow;
                return left < TimeSpan.Zero ? TimeSpan.Zero : left;
            }
        }

        /// <summary>The live phase. <see cref="DescentFusePhase.Dark"/> when unarmed.</summary>
        public DescentFusePhase Phase => PhaseFor(CeremonyAtUtc, DateTime.UtcNow);

        /// <summary>The phase the last <see cref="PhaseChanged"/> announced. Surfaces may read it
        /// instead of recomputing, and it is what the transition is measured against.</summary>
        public DescentFusePhase LastAnnouncedPhase { get; private set; } = DescentFusePhase.Dark;

        /// <summary>
        /// How dark the chrome should be right now: 0 before the Dimming phase, then 1..4, one step
        /// per six hours. Read by the chrome choke point and by nothing else.
        /// </summary>
        public int DimStep => DimStepFor(CeremonyAtUtc, DateTime.UtcNow,
            App.Settings?.Current?.DescentMigrationCompleted ?? false);

        /// <summary>
        /// The server's presence reading, or null when it did not speak — which is the state unless
        /// DESCENT_VIGIL is on AND we are inside the final hour. NEVER invented client-side: a
        /// fabricated count is a lie about other people, so null means the line does not render.
        ///
        /// <para><b>ZERO IS A READING.</b> The server OMITS the field when out of window rather
        /// than sending 0 (server lane, PR #44), so a 0 that arrives is a real in-window
        /// measurement and is displayed. Only null is absence.</para>
        ///
        /// <para><b>It counts BEATS, not humans.</b> The figure is sync beats in a fifteen-minute
        /// sliding window, so it reads high and one person can contribute several. That is why the
        /// copy is nounless ("N falling with you") — see DescentFuseCopy.Presence.</para>
        /// </summary>
        public int? VigilCount { get; private set; }

        /// <summary>
        /// TRUE when this process started up to find the instant already behind it. The catch-up
        /// path's precondition, and the reason <see cref="ZeroReached"/> can promise "live only":
        /// the two are mutually exclusive by construction.
        /// </summary>
        public bool ZeroPassedWhileAway { get; private set; }

        /// <summary>
        /// TRUE when this launch owes the subject the condensed catch-up crack (§2.4): the instant
        /// passed while the app was closed, the ceremony has not been completed, the live night was
        /// never witnessed, and the crack has not already played once. Every one of those is a
        /// reason not to, which is why they are all listed here rather than spread across callers.
        /// </summary>
        public bool ShouldPlayCatchUp
        {
            get
            {
                if (!ZeroPassedWhileAway) return false;
                var s = App.Settings?.Current;
                if (s is null) return false;
                if (s.DescentMigrationCompleted) return false;
                if (s.DescentLastNightWitnessed) return false;
                if (s.DescentCatchUpCrackPlayed) return false;
                return true;
            }
        }

        /// <summary>
        /// The audio hook's gate (<see cref="AppSettings.DescentCountdownAudio"/>). A convenience
        /// so the zero-show lane does not have to know which settings flag it is.
        /// </summary>
        public bool AudioEnabled => App.Settings?.Current?.DescentCountdownAudio ?? true;

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        /// <summary>
        /// Arm the clock, if there is anything to arm. Called once from App.OnStartup after
        /// Settings and the sync services exist.
        ///
        /// <para>With no cached timestamp this allocates nothing and starts nothing — the whole
        /// feature costs one null check per launch on every install in the world today.</para>
        /// </summary>
        public void Start()
        {
            if (_disposed || _started) return;
            _started = true;

            var at = CeremonyAtUtc;
            if (at is null)
            {
                Log.Debug("[Fuse] No cached ceremony timestamp — the countdown stays dark.");
                return;
            }

            // Decided ONCE, here, before the first tick can move anything: was the instant already
            // behind us when this process opened its eyes? Everything downstream (live show vs
            // catch-up crack) forks on this single answer, and re-deriving it later would get a
            // different one the moment the clock crossed zero mid-session.
            ZeroPassedWhileAway = at.Value <= DateTime.UtcNow;

            Log.Information("[Fuse] Armed for {At:o} ({Phase}{Away}).",
                at.Value, Phase, ZeroPassedWhileAway ? ", already passed" : "");

            ArmTimer();
            AnnouncePhase(force: true);
            _ = RefreshConfigAsync();
        }

        /// <summary>
        /// THE CACHE WRITE — the only way a timestamp gets in or out.
        ///
        /// <para>Called by ProfileSyncService on every successful sync: with the block's value when
        /// the server sent one, and with <c>null</c> when it did not. That null is the kill switch,
        /// and handling it here (rather than at the call site) is what makes the switch live: the
        /// settings write, the timer teardown, the chrome restore and the Dark announcement all
        /// happen in one place, in that order.</para>
        ///
        /// <para>A no-op when the value has not changed, so the sixty-second heartbeat does not
        /// re-arm the clock or re-save settings sixty times an hour.</para>
        /// </summary>
        /// <param name="isoUtc">The server's ISO-8601 instant verbatim, or null to clear.</param>
        public void ApplyCeremonyAt(string? isoUtc)
        {
            if (_disposed) return;

            // CLAUDE.md async rules 6/8: this arrives on a sync continuation, and a dispatcher that
            // has begun shutting down must not be posted to.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted) return;

            if (dispatcher.CheckAccess()) ApplyCeremonyAtCore(isoUtc);
            else dispatcher.BeginInvoke(new Action(() => ApplyCeremonyAtCore(isoUtc)));
        }

        private void ApplyCeremonyAtCore(string? isoUtc)
        {
            try
            {
                if (_disposed) return;
                var settings = App.Settings?.Current;
                if (settings is null) return;

                var incoming = string.IsNullOrWhiteSpace(isoUtc) ? null : isoUtc.Trim();

                // Compare the STRINGS, not the parsed instants: an unparseable value that the
                // server keeps re-sending must not re-log and re-arm on every sync.
                if (string.Equals(settings.DescentCeremonyAtUtc, incoming, StringComparison.Ordinal)) return;

                var hadFuse = ParseCeremonyAt(settings.DescentCeremonyAtUtc).HasValue;
                settings.DescentCeremonyAtUtc = incoming;
                App.Settings?.Save();

                var parsed = ParseCeremonyAt(incoming);
                if (incoming != null && parsed is null)
                    Log.Warning("[Fuse] Server sent an unparseable ceremony_at; the countdown stays dark.");

                if (parsed is null)
                {
                    if (hadFuse) Log.Information("[Fuse] Ceremony timestamp cleared — tearing every surface down.");
                    Disarm();
                    AnnouncePhase(force: true);   // -> Dark, and every surface hears it
                    return;
                }

                Log.Information("[Fuse] Ceremony timestamp is now {At:o} ({Phase}).", parsed.Value, Phase);

                // A NEW timestamp is a new fuse: a phase already spoken for the old one has not
                // been spoken for this one, so the companion is allowed her lines again. (The
                // owner moving the date is the only way this happens.)
                if (hadFuse)
                {
                    _spokenPhases.Clear();
                    _scriptedSilence = false;
                }

                if (!_started)
                {
                    // Armed after Start() found nothing. Settle the away/live fork now, on the same
                    // rule Start uses, so a first-sync arrival cannot be mistaken for a live zero.
                    _started = true;
                    ZeroPassedWhileAway = parsed.Value <= DateTime.UtcNow;
                }

                ArmTimer();
                AnnouncePhase(force: true);
                _ = RefreshConfigAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Fuse] Applying the ceremony timestamp failed — the countdown is unchanged.");
            }
        }

        /// <summary>Stop the clock and drop the presence count. The chrome restore rides the
        /// Dark announcement that follows.</summary>
        private void Disarm()
        {
            try
            {
                _timer?.Stop();
                _timer = null;
                _fastCadence = false;
                VigilCount = null;
                _lastConfigFetchUtc = DateTime.MinValue;
            }
            catch (Exception ex)
            {
                Log.Debug("[Fuse] Disarm failed: {Error}", ex.Message);
            }
        }

        private void ArmTimer()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted) return;

            var wantFast = (Remaining ?? TimeSpan.MaxValue) <= VigilAt;

            if (_timer != null)
            {
                if (_fastCadence == wantFast) return;   // already at the right cadence
                _timer.Stop();
                _timer = null;
            }

            _fastCadence = wantFast;
            _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = wantFast ? FastTick : SlowTick
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            try
            {
                if (_disposed) return;
                if (Application.Current?.Dispatcher?.HasShutdownStarted != false) return;

                var at = CeremonyAtUtc;
                if (at is null)
                {
                    // The cache was cleared by something other than ApplyCeremonyAt (a settings
                    // restore, a hand-edited file). Same teardown, same announcement.
                    Disarm();
                    AnnouncePhase(force: true);
                    return;
                }

                var remaining = Remaining ?? TimeSpan.Zero;

                // Cadence before events: crossing into the final hour should tighten the clock on
                // the very tick that noticed, not one slow tick later.
                ArmTimer();

                AnnouncePhase(force: false);

                // Re-offer the current phase's line. Idempotent (a spoken phase is remembered), and
                // it exists so a line that had nowhere to land at startup — the companion window is
                // built after this service — still gets said. See SpeakForPhase.
                SpeakForPhase(LastAnnouncedPhase);

                try { Tick?.Invoke(this, remaining); }
                catch (Exception ex) { Log.Debug("[Fuse] A Tick handler threw: {Error}", ex.Message); }

                if (!_zeroRaised && remaining <= TimeSpan.Zero && !ZeroPassedWhileAway)
                {
                    _zeroRaised = true;
                    Log.Information("[Fuse] Zero, live. Raising ZeroReached.");
                    try { ZeroReached?.Invoke(this, EventArgs.Empty); }
                    catch (Exception ex) { Log.Error(ex, "[Fuse] A ZeroReached handler threw."); }
                }

                // Past zero the clock has nothing left to say; the show owns the moment now.
                if (remaining <= TimeSpan.Zero) Disarm();
                else if (LastAnnouncedPhase >= DescentFusePhase.Vigil) _ = RefreshConfigAsync();
            }
            catch (Exception ex)
            {
                Log.Debug("[Fuse] Tick failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Raise <see cref="PhaseChanged"/> if the phase moved (or if forced, which is how the
        /// initial state and the kill switch are delivered). A handler that throws must not stop
        /// the rest of the surfaces from hearing it, so each is isolated at the event boundary.
        /// </summary>
        private void AnnouncePhase(bool force)
        {
            var current = Phase;
            if (!force && current == LastAnnouncedPhase) return;

            var previous = LastAnnouncedPhase;
            LastAnnouncedPhase = current;

            if (previous != current)
                Log.Information("[Fuse] Phase {Previous} -> {Current}.", previous, current);

            try { PhaseChanged?.Invoke(this, new DescentFusePhaseChangedEventArgs(previous, current)); }
            catch (Exception ex) { Log.Debug("[Fuse] A PhaseChanged handler threw: {Error}", ex.Message); }

            if (previous != current) SpeakForPhase(current);
        }

        /// <summary>
        /// One scripted line per phase per session, and after the Terminal line, nothing.
        ///
        /// <para><b>The silence is the instrument</b> (§2.1). From T-10m the countdown stops
        /// talking on purpose — but the suppression is scoped to THIS file and nothing else: user
        /// chat, every other bark system and the ceremony's own lines are untouched. There is no
        /// global mute here, and there must never be one.</para>
        /// </summary>
        private void SpeakForPhase(DescentFusePhase phase)
        {
            try
            {
                if (_scriptedSilence) return;

                var line = DescentFuseCopy.CompanionLine(phase);
                if (string.IsNullOrWhiteSpace(line)) return;

                // STARTUP ORDERING, and getting this wrong loses the line silently. This service is
                // constructed in App.OnStartup BEFORE the avatar window exists, so the very first
                // phase announcement can land with nowhere to say it. Marking the phase spoken here
                // would burn it for the whole session. Instead we bail without marking, and
                // OnTimerTick re-offers the current phase on every tick — so the line arrives at
                // most one tick after the companion does, and never at all if she is switched off.
                var avatar = App.AvatarWindow;
                if (avatar is null) return;

                if (!_spokenPhases.Add(phase)) return;

                // The petname pass by hand. These lines are not localized, so they never pass
                // through LocalizationManager.Get — which is where the {petname} substitution
                // normally happens — and GigglePriority substitutes nothing. Applying it here gives
                // scripted copy the same mod-aware word every localized surface gets, instead of
                // calling a Circe subject "sweetie". See the note in DescentFuseCopy.
                line = VocabTokens.Apply(line);

                // playSound:false and aiGenerated:false — scripted copy, no AI badge, and no
                // chat-suppression window opened on the companion's side.
                avatar.GigglePriority(line, playSound: false, aiGenerated: false);

                if (phase == DescentFusePhase.Terminal) _scriptedSilence = true;
            }
            catch (Exception ex)
            {
                Log.Debug("[Fuse] Companion line for {Phase} failed: {Error}", phase, ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Keepsake flags (the zero-show lane's writers)
        // ------------------------------------------------------------------

        /// <summary>
        /// Record that the LIVE zero sequence was watched through to its bloom. Idempotent — the
        /// keepsake is a fact about a night, not a counter.
        /// </summary>
        public void MarkLastNightWitnessed()
        {
            try
            {
                var s = App.Settings?.Current;
                if (s is null || s.DescentLastNightWitnessed) return;
                s.DescentLastNightWitnessed = true;
                App.Settings?.Save();
                Log.Information("[Fuse] Last night witnessed, live. Keepsake flag set.");
            }
            catch (Exception ex) { Log.Debug("[Fuse] Could not persist the witnessed flag: {Error}", ex.Message); }
        }

        /// <summary>Record that the condensed catch-up crack has played. Once per account.</summary>
        public void MarkCatchUpCrackPlayed()
        {
            try
            {
                var s = App.Settings?.Current;
                if (s is null || s.DescentCatchUpCrackPlayed) return;
                s.DescentCatchUpCrackPlayed = true;
                App.Settings?.Save();
                Log.Information("[Fuse] Catch-up crack played. It will not play again.");
            }
            catch (Exception ex) { Log.Debug("[Fuse] Could not persist the catch-up flag: {Error}", ex.Message); }
        }

        // ------------------------------------------------------------------
        // The presence count (/config/descent-countdown)
        // ------------------------------------------------------------------

        /// <summary>
        /// Fetch the public config, for one reason only: the presence count, which the sync
        /// response does not carry. The timestamp it also returns is NOT adopted — the sync
        /// response is the cache's single writer, and letting an unauthenticated endpoint arm a
        /// countdown would give the feature a second, weaker source of truth.
        ///
        /// <para>The wire (server lane, PR #44): dark is a 200 with <c>{"ceremony_at": null}</c> —
        /// same shape when the configured timestamp is junk — and <c>vigil</c> is a JSON integer
        /// ≥ 0 that is ABSENT when out of window rather than 0. So 0 is a real reading and is kept;
        /// only a missing field, a null, or a negative (which the server cannot send) becomes null.</para>
        ///
        /// <para>Every failure mode (offline, 404, garbage body, no network at all) means the same
        /// thing: no presence line. The countdown itself never depends on this call.</para>
        /// </summary>
        private async Task RefreshConfigAsync()
        {
            try
            {
                if (_disposed) return;

                // THE OFFLINE FLOOR, same as DescentService.RefreshAsync and every other network
                // path: a user who turned networking off must not see a request leave for a
                // decorative counter, and a keyless install has nobody to count.
                var settings = App.Settings?.Current;
                if (settings is null) return;
                if (settings.OfflineMode) return;
                if (string.IsNullOrEmpty(settings.UnifiedId)) return;
                if (string.IsNullOrEmpty(settings.AuthToken)) return;

                // The count only exists inside the final hour, so there is nothing to ask for
                // before it — one fetch at startup to learn the shape, then only during the vigil.
                var now = DateTime.UtcNow;
                if (_lastConfigFetchUtc != DateTime.MinValue && now - _lastConfigFetchUtc < VigilRefetchEvery) return;
                _lastConfigFetchUtc = now;

                var resp = await _http.GetAsync(ConfigEndpoint).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return;

                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                // DateParseHandling.None, via the same boundary the descent block uses. JObject.Parse
                // would silently rewrite the ISO-shaped ceremony_at into a JTokenType.Date, and a
                // strict string read of that is ABSENT — the repo's standing coercion trap.
                var json = DescentReader.ParseWire(body);
                if (json is null) return;

                int? vigil = null;
                var raw = json["vigil"];
                if (raw != null && raw.Type != JTokenType.Null)
                {
                    if (raw.Type == JTokenType.Integer) vigil = (int)raw;
                    else if (raw.Type == JTokenType.String &&
                             int.TryParse((string?)raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                        vigil = parsed;
                }
                if (vigil is < 0) vigil = null;

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null || dispatcher.HasShutdownStarted) return;
                // Discarded on purpose: this is a fire-and-forget UI hop inside an async method, so
                // the DispatcherOperation would otherwise read as a forgotten await (CS4014).
                _ = dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_disposed) return;
                    if (VigilCount == vigil) return;
                    VigilCount = vigil;
                    // Surfaces repaint off Tick, which is at most one second away during the vigil.
                }));
            }
            catch (Exception ex)
            {
                Log.Debug("[Fuse] Config fetch failed (no presence line; the countdown is unaffected): {Error}", ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Pure math — the part the tests pin
        // ------------------------------------------------------------------

        /// <summary>
        /// Read <c>descent_countdown.ceremony_at</c> off a raw /v2/user/sync body (§1.3).
        ///
        /// <para><b>Tri-state, and the third state is the whole point.</b> The return value says
        /// whether the body could be read at all; <paramref name="ceremonyAt"/> says what it
        /// contained. So:</para>
        /// <list type="bullet">
        /// <item><c>true</c> + a string — the server armed the fuse. Cache it.</item>
        /// <item><c>true</c> + null — the server did NOT send the block. CLEAR the cache; this is
        /// the kill switch, and it has to be as loud as the arming.</item>
        /// <item><c>false</c> — the payload was unreadable. Change NOTHING. A mangled transport has
        /// told us nothing about the owner's intent, and reading "call it off" into a truncated
        /// response is the one failure that would silently un-ship the feature.</item>
        /// </list>
        ///
        /// <para><b>The server never sends the block present-with-null</b> (server lane, PR #44): it
        /// omits the key entirely when dark, so KEY ABSENCE is the dark signal. The null and
        /// wrong-type branches below therefore land on the same answer as absence rather than on a
        /// third behaviour — there is deliberately no branch that treats an explicit null
        /// differently, because inventing a distinction the wire does not make is how a kill switch
        /// stops working.</para>
        ///
        /// <para>Reads through <see cref="DescentReader.ParseWire"/> (DateParseHandling.None)
        /// because <c>JObject.Parse</c> would rewrite the ISO-shaped string into a date token, and
        /// a strict string read of a date token is ABSENT — which this method would then report as
        /// the kill switch. That is the repo's standing coercion trap, pointed straight at the one
        /// field this feature has.</para>
        ///
        /// <para>Static and pure so the tests can pin all three states without a server.</para>
        /// </summary>
        internal static bool TryReadCeremonyAt(string? rawSyncJson, out string? ceremonyAt)
        {
            ceremonyAt = null;

            var body = DescentReader.ParseWire(rawSyncJson);
            if (body is null) return false;

            if (body["descent_countdown"] is JObject block &&
                block["ceremony_at"] is { Type: JTokenType.String } token)
            {
                var value = (string?)token;
                if (!string.IsNullOrWhiteSpace(value)) ceremonyAt = value.Trim();
            }

            return true;
        }

        /// <summary>
        /// Parse a cached ISO instant to UTC, or null for anything unusable.
        ///
        /// <para><see cref="DateTimeStyles.RoundtripKind"/> ALONE, and it must stay alone: combining
        /// it with AdjustToUniversal is an ArgumentException, not a stricter parse (the framework
        /// rejects the pair outright). RoundtripKind gives us the Kind the string actually carried —
        /// Utc for a Z, Local for an offset, Unspecified for a bare instant — and the switch below
        /// does the normalizing that AdjustToUniversal would have.</para>
        ///
        /// <para>A value with no zone at all is read as UTC rather than as this machine's local
        /// time: the field's name says UTC, and guessing local would shift the whole countdown by
        /// the user's offset.</para>
        /// </summary>
        internal static DateTime? ParseCeremonyAt(string? iso)
        {
            if (string.IsNullOrWhiteSpace(iso)) return null;
            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsed))
                return null;

            return parsed.Kind switch
            {
                DateTimeKind.Utc => parsed,
                DateTimeKind.Unspecified => DateTime.SpecifyKind(parsed, DateTimeKind.Utc),
                _ => parsed.ToUniversalTime(),
            };
        }

        /// <summary>
        /// The phase for an instant and a now. Pure, static and internal so the tests can pin every
        /// boundary without standing an app up. Boundaries are inclusive on the near side:
        /// exactly 72h out is Clock, not Whisper.
        /// </summary>
        internal static DescentFusePhase PhaseFor(DateTime? ceremonyAtUtc, DateTime nowUtc)
        {
            if (ceremonyAtUtc is null) return DescentFusePhase.Dark;

            var left = ceremonyAtUtc.Value - nowUtc;
            if (left <= TimeSpan.Zero)  return DescentFusePhase.Zero;
            if (left <= TerminalAt)     return DescentFusePhase.Terminal;
            if (left <= VigilAt)        return DescentFusePhase.Vigil;
            if (left <= CandleAt)       return DescentFusePhase.Candle;
            if (left <= DimmingAt)      return DescentFusePhase.Dimming;
            if (left <= ClockAt)        return DescentFusePhase.Clock;
            if (left <= WhisperAt)      return DescentFusePhase.Whisper;
            return DescentFusePhase.Dark;
        }

        /// <summary>
        /// The chrome darkening step: 0 until the Dimming phase, then 1..4, one per six hours
        /// elapsed since T-24h. Four steps over the last day, and step 4 holds through zero — the
        /// chrome does not brighten back up while the show is starting.
        /// </summary>
        internal static int DimStepFor(DateTime? ceremonyAtUtc, DateTime nowUtc) =>
            DimStepFor(ceremonyAtUtc, nowUtc, migrationCompleted: false);

        /// <summary>
        /// The same walk, with the one thing that ends it.
        ///
        /// <para><b>A migrated account is not in the dark any more</b> (CONTRACT-FUSE-0816 §2.4, the
        /// ignition's chrome restore). Step 4 deliberately HOLDS through zero so the room does not
        /// brighten while the show is opening — but "through zero" is not "forever". Without this,
        /// the pure arithmetic above returns 4 for every moment after the instant, so a subject who
        /// took the ceremony on the first night would keep a dimmed app until the owner eventually
        /// unset the server timestamp days later. The ignition's <c>RefreshThemeAwareElements</c>
        /// call is what repaints; this is what gives it something to repaint back to.</para>
        ///
        /// <para>Additive: the two-argument overload is unchanged, so every existing caller and
        /// every pinned boundary reads exactly as it did.</para>
        /// </summary>
        internal static int DimStepFor(DateTime? ceremonyAtUtc, DateTime nowUtc, bool migrationCompleted)
        {
            if (migrationCompleted) return 0;
            if (ceremonyAtUtc is null) return 0;

            var left = ceremonyAtUtc.Value - nowUtc;
            if (left > DimmingAt) return 0;
            if (left <= TimeSpan.Zero) return 4;

            var elapsed = DimmingAt - left;
            var step = 1 + (int)Math.Floor(elapsed.TotalHours / 6.0);
            return Math.Clamp(step, 1, 4);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disarm();
            _http.Dispose();
        }
    }
}
