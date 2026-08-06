using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The v2 observer: the one thing that decides a moment happened.
    ///
    /// <para>It evolves <c>WindowAwarenessService</c> rather than replacing it — the 1.5s title poll is
    /// cheap and works. What changes is everything after the poll (doc 02 §4):</para>
    /// <code>
    /// title poll (1.5s)
    ///   → privacy layer (incognito hard-drop, deny list, identity resolution)  ← BEFORE any write
    ///     → ledger.NoteFocus
    ///       → transition detector (did the resolved app id change?)
    ///         → DWELL GATE: a candidate must survive ~20s before a frame is cut
    ///   → DND filter (frame is already in the ledger — she can joke about it later)
    ///   → trend derivation → frame builder → worthiness scorer → arbiter
    /// </code>
    ///
    /// <para><b>Frames are cut on events, never on ticks.</b> There is no code path from a timer to an
    /// LLM call in this design, and v2 must not grow one (doc 02 §7.6). The poll produces a frame only
    /// when something actually happened: a new app survived the gate, a fullscreen stint ended, the
    /// user churned through half a dozen windows, a dwell milestone was crossed, or the track changed.</para>
    ///
    /// <para><b>Lifecycle is eager.</b> The ledger is started — and therefore loaded and PRUNED — from
    /// <see cref="Start"/>, with nothing to do with any UI being open. A retention promise honoured
    /// only when someone opens the Companion tab is not a retention promise.</para>
    /// </summary>
    public sealed class AwarenessObserver : IDisposable
    {
        /// <summary>Poll cadence, unchanged from the service this evolves.</summary>
        public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);

        /// <summary>
        /// How long a candidate app must hold the foreground before a frame is cut. Kills alt-tab spam
        /// and pass-through windows; the churn itself becomes ONE
        /// <see cref="TransitionKind.RapidCycling"/> event rather than five reactions.
        /// </summary>
        public const int DwellGateSeconds = 20;

        /// <summary>
        /// Real input idle after which accrual is suspended — unless the user is plainly watching
        /// something (fullscreen, or media playing). Doc 02 §1.4's complaint is that today's code calls
        /// video-watching "idle"; the fix is not to call AFK "watching" either.
        /// </summary>
        public const int AfkSuspendSeconds = 180;

        /// <summary>Pending-app changes inside <see cref="RapidCyclingWindowSeconds"/> that make it churn.</summary>
        public const int RapidCyclingSwitches = 4;

        /// <summary>Window the churn counter is measured over.</summary>
        public const int RapidCyclingWindowSeconds = 120;

        /// <summary>Minimum gap between two RapidCycling frames. "Pick a window already~" does not bear repeating.</summary>
        public const int RapidCyclingCooldownSeconds = 600;

        /// <summary>A fullscreen stint shorter than this is not worth an exit beat.</summary>
        public const int MinFullscreenStintSeconds = 60;

        /// <summary>Position rewind, in seconds, that reads as "the track started over" rather than a seek.</summary>
        public const int MediaRewindSeconds = 10;

        /// <summary>Poll ticks between dwell-milestone checks. Milestones are minute-scale; polling is not.</summary>
        private const int MilestoneCheckEveryTicks = 4;

        private readonly ActivityLedger _ledger;
        private readonly WorthinessScorer _scorer;
        private readonly IReactionArbiter _arbiter;
        private readonly ICompanionMemory _memory;
        private readonly Func<DateTime> _clock;

        private readonly IForegroundProbe _foreground;
        private readonly IInputProbe _input;
        private readonly IMicrophoneProbe _microphone;
        private readonly IMediaWatcher? _media;
        private readonly IAppStateProbe _appState;
        private readonly Func<AwarenessPolicySettings?> _policy;

        private DispatcherTimer? _pollTimer;

        // Cancels anything still in flight when she is told to stop watching. The LLM leg can take
        // eight seconds; without this a user who closes her eyes mid-call still gets a line about what
        // they were doing six seconds after telling her to stop.
        private CancellationTokenSource? _lifetime;

        private bool _running;
        private bool _disposed;
        private int _tickInFlight;
        private int _tickCount;

        // --- dwell gate / transitions ---
        private string? _pendingAppId;
        private DateTime _pendingSince;
        private string? _committedAppId;
        private string? _committedCluster;
        private string? _previousAppId;
        private int _committedMilestoneMinutes;

        // --- churn ---
        private int _gateSwitches;
        private DateTime _gateWindowStart;
        private DateTime _lastRapidCycleAt = DateTime.MinValue;

        // --- fullscreen ---
        private bool _wasFullscreen;
        private DateTime _fullscreenSince;

        // --- idle / wake ---
        private bool _afk;
        private int _peakIdleSeconds;
        private int _wakeIdleSeconds;
        private bool _wakePending;

        // --- media ---
        private string? _mediaTitle;
        private TimeSpan _mediaPosition;
        private int _mediaRepeats;
        private bool _mediaChangePending;

        // --- diagnostics ---
        private DateTime _lastTickAt;
        private string _lastDropSignature = "";
        private string? _currentAppId;
        private ContextFrame? _lastFrame;

        /// <summary>
        /// Production constructor. The four collaborators are required: an observer with no ledger, no
        /// scorer, no arbiter or no memory is not a degraded observer, it is a bug with a heartbeat.
        /// </summary>
        public AwarenessObserver(
            ActivityLedger ledger,
            WorthinessScorer scorer,
            IReactionArbiter arbiter,
            ICompanionMemory memory,
            Func<DateTime>? localClock = null)
            : this(ledger, scorer, arbiter, memory, localClock, null, null, null, null, null, null)
        {
        }

        /// <summary>
        /// Test/diagnostic constructor: every edge that needs a desktop, an audio stack, WinRT or a
        /// settings object is behind one of these seams, so the whole pipeline runs headlessly.
        /// </summary>
        internal AwarenessObserver(
            ActivityLedger ledger,
            WorthinessScorer scorer,
            IReactionArbiter arbiter,
            ICompanionMemory memory,
            Func<DateTime>? localClock,
            IForegroundProbe? foreground,
            IInputProbe? input,
            IMicrophoneProbe? microphone,
            IMediaWatcher? media,
            IAppStateProbe? appState,
            Func<AwarenessPolicySettings?>? policy)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _scorer = scorer ?? throw new ArgumentNullException(nameof(scorer));
            _arbiter = arbiter ?? throw new ArgumentNullException(nameof(arbiter));
            _memory = memory ?? throw new ArgumentNullException(nameof(memory));
            _clock = localClock ?? (() => DateTime.Now);

            _foreground = foreground ?? new Win32ForegroundProbe();
            _input = input ?? new Win32InputProbe();
            _microphone = microphone ?? new WasapiMicrophoneProbe();
            _media = media ?? new SmtcMediaWatcher();
            _appState = appState ?? new AppStateProbe();
            _policy = policy ?? AwarenessPolicySettings.FromSettings;

            _pendingSince = _clock();
            _gateWindowStart = _pendingSince;
            _fullscreenSince = _pendingSince;
            _lastTickAt = _pendingSince;
        }

        /// <summary>Raised when a frame has been cut, scored and cleared for the arbiter.</summary>
        public event EventHandler<ContextFrame>? FrameCut;

        /// <summary>The ledger this observer feeds. Shared with the privacy panel's forget/wipe controls.</summary>
        public ActivityLedger Ledger => _ledger;

        /// <summary>The pacing state. Shared so a delivered line raises the threshold wherever it came from.</summary>
        public WorthinessScorer Scorer => _scorer;

        /// <summary>
        /// The one cooldown ledger. Exposed so BarkService and the keyword engine can ASK before
        /// speaking (<see cref="IReactionArbiter.CanSpeak"/>) rather than apologise afterwards — which
        /// is the whole of "one mouth".
        /// </summary>
        public IReactionArbiter Arbiter => _arbiter;

        /// <summary>True between a successful <see cref="Start"/> and <see cref="Stop"/>.</summary>
        public bool IsRunning => _running;

        /// <summary>
        /// The app id the user is on RIGHT NOW, as last resolved by the privacy layer, or null when the
        /// foreground is dropped/unresolved. This is the delivery-time staleness check (doc 02 §4.3):
        /// compare it against <see cref="ContextFrame.AppId"/> when a line finally comes back, and
        /// re-tag as a callback or drop rather than delivering a stale observation.
        /// </summary>
        public string? CurrentAppId => _currentAppId;

        /// <summary>The last frame cut, for diagnostics and the privacy panel's live view.</summary>
        public ContextFrame? LastFrame => _lastFrame;

        /// <summary>True when the SMTC media signal is live on this machine.</summary>
        public bool MediaSignalAvailable => _media?.IsAvailable == true;

        /// <summary>
        /// Whether v2 may run at all: the kill switch, the feature toggle, the legacy consent AND the
        /// v2 consent, all four.
        ///
        /// <para><b>Why <c>AwarenessConsentShownV2</c> is in here.</b> v1 persisted nothing; v2 keeps a
        /// 30-day on-disk record of per-app visit counts, minute totals, day streaks and a machine-wide
        /// hourly histogram. That is a materially new capability, and an upgrader already has
        /// <c>AwarenessModeEnabled</c> and <c>AwarenessConsentGiven</c> set from the old silent
        /// auto-consent — so without this clause the ledger starts recording on the first launch after
        /// the update, for a dialog the user has never seen. Until the v2 explanation is accepted the
        /// legacy pipeline runs exactly as it does today and nothing is written.</para>
        ///
        /// <para>Reads defensively — headless and during early startup <c>App.Settings</c> is null, and
        /// a null settings object must read as "no consent", never as "sure, go ahead".</para>
        /// </summary>
        public static bool IsEnabled
        {
            get
            {
                try
                {
                    var s = App.Settings?.Current;
                    if (s == null) return false;
                    return s.UseAwarenessV2 && s.AwarenessModeEnabled &&
                           s.AwarenessConsentGiven && s.AwarenessConsentShownV2;
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Starts the ledger (which loads and prunes) and arms the poll. A no-op when awareness is off,
        /// unconsented or the v2 kill switch is down — in which case the legacy
        /// <c>WindowAwarenessService</c> pipeline keeps running exactly as it does today.
        /// </summary>
        public void Start()
        {
            if (_disposed || _running) return;

            if (!IsEnabled)
            {
                App.Logger?.Debug("AwarenessObserver: not starting (v2 off, awareness off, or no consent)");
                return;
            }

            // Before anything is observed: load the ledger and age out anything past retention. This
            // must not depend on a window being open.
            _ledger.Start();
            _running = true;
            _lifetime = new CancellationTokenSource();

            var now = _clock();
            _pendingSince = now;
            _gateWindowStart = now;
            _fullscreenSince = now;
            _lastTickAt = now;

            try { _input.Start(); } catch (Exception ex) { App.Logger?.Debug("AwarenessObserver: input probe start failed - {Error}", ex.Message); }
            try { _media?.Start(); } catch (Exception ex) { App.Logger?.Debug("AwarenessObserver: media watcher start failed - {Error}", ex.Message); }
            try { (_appState as AppStateProbe)?.Attach(); } catch { }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                App.Logger?.Warning("AwarenessObserver: no dispatcher — ledger is live, polling is not");
                return;
            }

            _pollTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = PollInterval };
            _pollTimer.Tick += OnPollTick;
            _pollTimer.Start();

            App.Logger?.Information("AwarenessObserver: started (poll {Ms}ms, dwell gate {Gate}s)",
                (int)PollInterval.TotalMilliseconds, DwellGateSeconds);
        }

        /// <summary>Disarms the poll and flushes the ledger. Safe to call when never started.</summary>
        public void Stop()
        {
            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer.Tick -= OnPollTick;
                _pollTimer = null;
            }

            try { _input.Stop(); } catch { }
            try { _media?.Stop(); } catch { }

            // Anything mid-flight stops being wanted the moment she is told to stop watching.
            var lifetime = _lifetime;
            _lifetime = null;
            if (lifetime != null)
            {
                try { lifetime.Cancel(); } catch { }
                try { lifetime.Dispose(); } catch { }
            }

            if (!_running) return;
            _running = false;

            try { _ledger.NoteFocusEnd(_clock()); } catch { }
            _ledger.Stop();
            ResetTransientState();
            App.Logger?.Information("AwarenessObserver: stopped");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Stop(); } catch { }
            try { _input.Dispose(); } catch { }
            try { _media?.Dispose(); } catch { }
            try { (_appState as IDisposable)?.Dispose(); } catch { }

            // Stop() flushed it; this is what actually releases its two System.Threading.Timers, which
            // otherwise survive shutdown.
            try { _ledger.Dispose(); } catch { }
        }

        /// <summary>
        /// Clears everything this observer holds in memory about the current moment: the dwell gate's
        /// pending candidate, the committed app, the churn counter, the fullscreen and wake state, the
        /// media loop counter and the last frame.
        ///
        /// <para>The privacy panel's wipe path calls this alongside <see cref="ActivityLedger.Wipe"/>
        /// and <see cref="ICompanionMemory.ForgetAsync"/>: a wipe that left a queued frame or a
        /// half-open visit in RAM would still be able to produce a line about what was just erased.</para>
        /// </summary>
        public void ResetTransientState()
        {
            _pendingAppId = null;
            _committedAppId = null;
            _committedCluster = null;
            _previousAppId = null;
            _committedMilestoneMinutes = 0;
            _gateSwitches = 0;
            _lastRapidCycleAt = DateTime.MinValue;
            _wasFullscreen = false;
            _afk = false;
            _peakIdleSeconds = 0;
            _wakeIdleSeconds = 0;
            _wakePending = false;
            _mediaTitle = null;
            _mediaPosition = TimeSpan.Zero;
            _mediaRepeats = 0;
            _mediaChangePending = false;
            _currentAppId = null;
            _lastFrame = null;
            _lastDropSignature = "";
            _lastTickAt = _clock();
        }

        /// <summary>
        /// Publishes a cut frame. The pipeline calls this at the end of its run; it is public rather
        /// than private so the arbiter and prompt packages can drive the whole chain from a synthetic
        /// frame in tests without a foreground window.
        /// </summary>
        public void PublishFrame(ContextFrame frame) => PublishFrame(frame, null);

        private void PublishFrame(ContextFrame frame, TrendDerivation? trends)
        {
            if (frame == null) return;

            _lastFrame = frame;

            // The privacy panel's "what she can see" wire view renders the LAST FRAME, verbatim,
            // through AwarenessProjection. Publishing here is what makes that view show the real thing
            // rather than a reconstruction — and it is also the frame the panel's wipe has to erase.
            try
            {
                AwarenessLive.Publish(frame);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AwarenessObserver: AwarenessLive.Publish threw - {Error}", ex.Message);
            }

            try
            {
                FrameCut?.Invoke(this, frame);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AwarenessObserver: a FrameCut subscriber threw");
            }

            _ = SubmitAsync(frame, trends);
        }

        private async Task SubmitAsync(ContextFrame frame, TrendDerivation? trends)
        {
            try
            {
                var token = _lifetime?.Token ?? CancellationToken.None;
                var decision = await _arbiter.SubmitAsync(frame, token).ConfigureAwait(false);

                // The one-shot trend guards burn HERE, on an actual delivery — not when the trend was
                // derived. A frame gated by the global gap, starved by the hourly budget, refused as
                // busy or scored below threshold must leave that day's callback available for the next
                // frame that does get spoken.
                if (decision.Verdict != AwarenessVerdict.Silence) _ledger.CommitTrends(trends);

                App.Logger?.Debug("[AWARE] arbiter verdict={Verdict} tier={Tier} gate={Reason}",
                    decision.Verdict, decision.Tier, decision.Reason);
            }
            catch (Exception ex)
            {
                // A background awareness failure is a missed joke. It must never be a crash log.
                App.Logger?.Warning(ex, "AwarenessObserver: arbiter submit failed");
            }
        }

        private void OnPollTick(object? sender, EventArgs e)
        {
            if (_disposed || !_running) return;
            if (Application.Current?.Dispatcher == null) return;
            if (Application.Current.Dispatcher.HasShutdownStarted) return;

            _ = TickAsync(_clock());
        }

        // =================================================================================
        //  the pipeline
        // =================================================================================

        /// <summary>
        /// One pass of the pipeline at <paramref name="now"/>. Internal so tests drive it directly with
        /// an injected clock — twenty simulated seconds instead of twenty real ones.
        /// </summary>
        internal async Task TickAsync(DateTime now)
        {
            // A slow memory/arbiter call must not let two passes interleave over the same state.
            if (Interlocked.Exchange(ref _tickInFlight, 1) == 1) return;

            try
            {
                var candidate = Observe(now);
                if (candidate != null) await CutFrameAsync(candidate, now).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AwarenessObserver: poll tick failed");
            }
            finally
            {
                Interlocked.Exchange(ref _tickInFlight, 0);
            }
        }

        /// <summary>What the observer decided this tick: nothing, or one frame's worth of reasons.</summary>
        private sealed record Candidate(
            PrivacyVerdict Verdict,
            TransitionKind Transition,
            bool IsFullscreen,
            int IdleSeconds,
            int WakeIdleSeconds,
            MediaSample? Media,
            int MediaRepeats,
            string ProcessName);

        /// <summary>
        /// The synchronous half: read the world, apply privacy, record to the ledger, run the dwell
        /// gate, and decide whether anything happened. Returns null on the overwhelming majority of
        /// ticks, which is the point.
        /// </summary>
        private Candidate? Observe(DateTime now)
        {
            _tickCount++;

            // The instant the previous pass ran. An AFK stretch is closed AT that instant rather than
            // at `now`, so a machine left alone for four hours does not credit those hours to whatever
            // happened to be in the foreground when the user walked away.
            var previousTick = _lastTickAt == default || _lastTickAt > now ? now : _lastTickAt;
            _lastTickAt = now;

            var policy = _policy();
            if (policy == null)
            {
                // Fail closed: with no policy we cannot know what is deny-listed, so nothing is looked at.
                _ledger.Heartbeat(now);
                DropForeground(now, FrameDrop.PolicyUnavailable, "");
                return null;
            }

            var sample = _foreground.Read();
            int idle = SafeIdleSeconds();
            var media = TrackMedia();

            // --- AFK: suspend accrual, but never call watching "idle" ---
            bool watching = (sample?.IsFullscreen ?? false) || (media?.IsPlaying ?? false);
            if (idle >= AfkSuspendSeconds && !watching)
            {
                if (!_afk) App.Logger?.Debug("AwarenessObserver: suspending accrual (idle {Idle}s)", idle);
                _afk = true;
                _peakIdleSeconds = Math.Max(_peakIdleSeconds, idle);

                // Close first (at the previous tick), roll the day over second. Reversing these would
                // accrue the whole away-stretch before anyone noticed it was an away-stretch.
                DropForeground(previousTick, FrameDrop.None, "afk");
                _ledger.Heartbeat(now);
                return null;
            }

            // Keeps "today"/"this week" honest and rolls the day over while the machine sits idle.
            _ledger.Heartbeat(now);

            if (_afk)
            {
                _afk = false;
                _wakeIdleSeconds = Math.Max(_peakIdleSeconds, idle);
                _wakePending = true;
                _peakIdleSeconds = 0;
                App.Logger?.Debug("AwarenessObserver: wake after {Idle}s idle", _wakeIdleSeconds);
            }

            // --- privacy, BEFORE anything is written anywhere ---
            // `now` is the observer's clock, not DateTime.Now, so a test that drives the tick also
            // drives the pause window rather than silently falling back to wall time.
            var verdict = AwarenessObserverPolicy.EvaluatePrivacy(sample, policy, now);
            if (!verdict.Allowed)
            {
                DropForeground(now, verdict.Drop, sample?.ProcessName ?? "");
                return null;
            }

            _lastDropSignature = "";
            _currentAppId = verdict.AppId;

            // --- ledger ---
            _ledger.NoteFocus(verdict.AppId, verdict.Cluster, verdict.Category, now);

            // --- fullscreen edges ---
            bool fullscreen = sample!.IsFullscreen;
            bool exitedFullscreen = _wasFullscreen && !fullscreen &&
                                    (now - _fullscreenSince).TotalSeconds >= MinFullscreenStintSeconds;
            if (fullscreen && !_wasFullscreen) _fullscreenSince = now;
            _wasFullscreen = fullscreen;

            // --- dwell gate ---
            if (!string.Equals(_pendingAppId, verdict.AppId, StringComparison.OrdinalIgnoreCase))
            {
                // A pending candidate that never made it to committed is churn, by definition.
                if (_pendingAppId != null &&
                    !string.Equals(_pendingAppId, _committedAppId, StringComparison.OrdinalIgnoreCase))
                {
                    if ((now - _gateWindowStart).TotalSeconds > RapidCyclingWindowSeconds)
                    {
                        _gateSwitches = 0;
                        _gateWindowStart = now;
                    }
                    _gateSwitches++;
                }

                _pendingAppId = verdict.AppId;
                _pendingSince = now;
            }

            var process = sample.ProcessName ?? "";

            // --- candidate selection: at most ONE frame per tick, most interesting first ---

            // 1. The fullscreen stint ended. This is the payoff for the DND suppression that preceded
            //    it — "so how many hours was THAT?" — and it does not wait for the dwell gate.
            if (exitedFullscreen)
            {
                CommitTo(verdict, now);
                return new Candidate(verdict, TransitionKind.ExitFullscreen, false, idle,
                    TakeWakeIdle(), media, _mediaRepeats, process);
            }

            // 2. Sustained churn collapses into ONE event rather than five reactions.
            if (_gateSwitches >= RapidCyclingSwitches &&
                (now - _lastRapidCycleAt).TotalSeconds >= RapidCyclingCooldownSeconds)
            {
                _lastRapidCycleAt = now;
                _gateSwitches = 0;
                _gateWindowStart = now;
                CommitTo(verdict, now);
                return new Candidate(verdict, TransitionKind.RapidCycling, fullscreen, idle,
                    TakeWakeIdle(), media, _mediaRepeats, process);
            }

            // 3. The dwell gate: a genuinely new app that stuck around.
            bool newApp = !string.Equals(_committedAppId, verdict.AppId, StringComparison.OrdinalIgnoreCase);
            if (newApp && (now - _pendingSince).TotalSeconds >= DwellGateSeconds)
            {
                var snapshot = _ledger.Snapshot(verdict.AppId, now);
                var kind = _wakePending ? TransitionKind.WakeFromIdle
                    : snapshot.VisitsToday >= 2 ? TransitionKind.ReturnVisit
                    : TransitionKind.NewApp;

                CommitTo(verdict, now);
                _gateSwitches = 0;
                _gateWindowStart = now;
                return new Candidate(verdict, kind, fullscreen, idle, TakeWakeIdle(), media, _mediaRepeats, process);
            }

            if (newApp) return null;   // still inside the gate — nothing happened yet

            // 4. Same app, different cluster: a tab change worth noticing without ever seeing a title
            //    (a browser that went from site_video to site_shopping). The cheapest transition there
            //    is, and it will usually score below threshold — which is correct.
            if (!string.Equals(_committedCluster, verdict.Cluster, StringComparison.OrdinalIgnoreCase) &&
                (now - _pendingSince).TotalSeconds >= DwellGateSeconds)
            {
                _committedCluster = verdict.Cluster;
                return new Candidate(verdict, TransitionKind.TabChange, fullscreen, idle,
                    TakeWakeIdle(), media, _mediaRepeats, process);
            }

            // 5. A cumulative-dwell milestone (doc 02 §4.4 — this is what replaces the {1,5,10} nag).
            if (_tickCount % MilestoneCheckEveryTicks == 0)
            {
                var snapshot = _ledger.Snapshot(verdict.AppId, now);
                int crossed = HighestMilestoneCrossed(snapshot.CurrentVisitDwellSeconds);
                if (crossed > _committedMilestoneMinutes)
                {
                    _committedMilestoneMinutes = crossed;
                    return new Candidate(verdict, TransitionKind.Milestone, fullscreen, idle,
                        TakeWakeIdle(), media, _mediaRepeats, process);
                }
            }

            // 6. The track changed under a stationary app.
            if (_mediaChangePending)
            {
                _mediaChangePending = false;
                return new Candidate(verdict, TransitionKind.MediaChanged, fullscreen, idle,
                    TakeWakeIdle(), media, _mediaRepeats, process);
            }

            return null;
        }

        /// <summary>
        /// The asynchronous half: DND, trends, memory hooks, scoring, publication.
        ///
        /// <para>DND is applied BEFORE the trends are derived at all, and the trends that ARE derived
        /// are only RESERVED (<see cref="ActivityLedger.PeekTrends"/>) until a line actually reaches
        /// the user (<see cref="ActivityLedger.CommitTrends"/>). Both halves protect the same thing:
        /// the whole design of "record it anyway, she can bring it up later" depends on a once-per-day
        /// callback still being available when the fullscreen stint ends — or when the next frame that
        /// clears the cooldowns comes along.</para>
        /// </summary>
        private async Task CutFrameAsync(Candidate candidate, DateTime now)
        {
            var verdict = candidate.Verdict;
            var state = SafeAppState(now);
            bool adult = string.Equals(verdict.Cluster, AwarenessClusters.Adult, StringComparison.OrdinalIgnoreCase);

            var dnd = AwarenessObserverPolicy.EvaluateDnd(new DndInput(
                IsFullscreen: candidate.IsFullscreen,
                InputIdleSeconds: candidate.IdleSeconds,
                ProcessName: candidate.ProcessName,
                // The mic sweep is only asked for when it could change the answer: a conferencing app
                // in the foreground. Everywhere else the WASAPI enumeration is pure cost.
                MicrophoneInUse: AwarenessObserverPolicy.IsMeetingProcess(candidate.ProcessName) && SafeMicInUse(now),
                IsTypingBurst: SafeTypingBurst(),
                CcpSurfaceActive: state.BlockingSurfaceActive,
                IsAdultCluster: adult,
                AdultReactionsEnabled: _policy()?.AdultReactionsEnabled ?? false));

            if (dnd != DndGate.None)
            {
                // Invariant: every candidate leaves exactly one [AWARE] line. Debug, not Information:
                // it names the resolved app id, and Serilog's floor is Information (see
                // ReactionArbiter.Log for the whole argument).
                App.Logger?.Debug(
                    "[AWARE] app={App} score=gated tier=- verdict=Silence gate=dnd-{Gate} transition={Transition}",
                    AwarenessText.SanitizeId(verdict.AppId), dnd.ToString().ToLowerInvariant(), candidate.Transition);
                return;
            }

            // RESERVES the one-shot guards without burning them. They are committed in SubmitAsync,
            // and only when a line actually reached the user — every gate between here and there
            // (threshold, bark floor, global gap, hourly budget, same-app gap, busy, stale) would
            // otherwise spend that day's best callback on a frame nobody hears.
            var derivation = _ledger.PeekTrends(
                verdict.AppId, verdict.Cluster, now,
                inputIdleSecondsBeforeWake: candidate.WakeIdleSeconds,
                mediaRepeatCount: candidate.MediaRepeats);
            var trends = derivation.Trends;

            var snapshot = _ledger.Snapshot(verdict.AppId, now);

            var input = new WorthinessInput(
                AppId: verdict.AppId,
                FirstEverSeen: snapshot.FirstEverVisit,
                FirstTimeToday: snapshot.FirstVisitToday,
                Transition: candidate.Transition,
                DwellSeconds: snapshot.CurrentVisitDwellSeconds,
                Trends: trends,
                CcpSessionRunning: state.SessionRunning,
                HasRecentAchievement: state.RecentAchievementId != null,
                LoginStreakDays: state.LoginStreakDays);

            // Logs the one authoritative [AWARE] line for this event.
            var scored = _scorer.Score(input, now);
            if (scored.Verdict == AwarenessVerdict.Silence) return;

            var habits = await SafeHabitsAsync(verdict.AppId, verdict.Cluster).ConfigureAwait(false);
            var recent = await SafeRecentAsync().ConfigureAwait(false);

            var frame = new ContextFrame
            {
                AppId = verdict.AppId,
                AppCluster = verdict.Cluster,
                Category = verdict.Category,
                ServiceName = verdict.ServiceName,
                PageTitleSanitized = verdict.PageTitleSanitized,
                IsFullscreen = candidate.IsFullscreen,
                NowPlaying = candidate.Media == null
                    ? null
                    : new MediaInfo(candidate.Media.Title, candidate.Media.Artist,
                        candidate.Media.PlaybackState, candidate.MediaRepeats),
                InputIdleSeconds = candidate.IdleSeconds,

                Transition = candidate.Transition,
                DwellSeconds = snapshot.CurrentVisitDwellSeconds,
                PreviousAppId = _previousAppId,
                SwitchesLast10Min = snapshot.SwitchesLast10Min,

                VisitsToday = snapshot.VisitsToday,
                MinutesToday = snapshot.MinutesToday,
                MinutesThisWeek = snapshot.MinutesThisWeek,
                SinceLastVisit = snapshot.SinceLastVisit,
                DayStreak = snapshot.DayStreak,
                DayArcSummary = snapshot.DayArcSummary,

                CcpSessionRunning = state.SessionRunning,
                UserLevel = state.UserLevel,
                LoginStreakDays = state.LoginStreakDays,
                RecentAchievementId = state.RecentAchievementId,
                TimeOfDay = ActivityLedger.BucketOf(now),
                Weekday = now.DayOfWeek,

                Trends = trends,
                MatchedHabits = habits,
                RecentReactions = recent,

                Novelty = scored.Novelty,
                Worthiness = scored.Score,
                Tier = scored.Tier,
                CutAt = now
            };

            PublishFrame(frame, derivation);
        }

        // =================================================================================
        //  helpers
        // =================================================================================

        private void CommitTo(PrivacyVerdict verdict, DateTime now)
        {
            if (!string.Equals(_committedAppId, verdict.AppId, StringComparison.OrdinalIgnoreCase))
            {
                _previousAppId = _committedAppId;
                _committedMilestoneMinutes = 0;
            }

            _committedAppId = verdict.AppId;
            _committedCluster = verdict.Cluster;
            _pendingAppId = verdict.AppId;
            _pendingSince = now;
        }

        /// <summary>
        /// Closes the ledger's current segment and clears the gate. Called for every reason the
        /// foreground stops being observable — dropped by the privacy layer, unresolvable, or AFK — so
        /// that time on a denied window is never counted as time on the app before it.
        /// </summary>
        private void DropForeground(DateTime now, FrameDrop drop, string context)
        {
            try { _ledger.NoteFocusEnd(now); } catch { }

            _pendingAppId = null;
            _committedAppId = null;
            _committedCluster = null;
            _committedMilestoneMinutes = 0;
            _currentAppId = null;

            if (drop == FrameDrop.None) return;

            // Logged once per change: a deny-listed window in the foreground would otherwise write a
            // line every 1.5 seconds for as long as the user sits there.
            var signature = drop + "|" + context;
            if (signature == _lastDropSignature) return;
            _lastDropSignature = signature;
            App.Logger?.Debug("[AWARE] foreground dropped: {Drop}", drop);
        }

        private int TakeWakeIdle()
        {
            if (!_wakePending) return 0;
            _wakePending = false;
            var seconds = _wakeIdleSeconds;
            _wakeIdleSeconds = 0;
            return seconds;
        }

        private static int HighestMilestoneCrossed(int dwellSeconds)
        {
            int best = 0;
            foreach (var milestone in ActivityLedger.LongHaulMilestonesMinutes)
            {
                if (dwellSeconds >= milestone * 60) best = milestone;
            }
            return best;
        }

        /// <summary>
        /// Folds the SMTC read into the loop counter. A repeat is either the title changing back to what
        /// it just was, or the position rewinding to the start while the title stands still — the two
        /// shapes a track on loop takes from a poller's point of view.
        /// </summary>
        private MediaSample? TrackMedia()
        {
            var sample = _media?.Current;
            if (sample == null)
            {
                _mediaTitle = null;
                _mediaPosition = TimeSpan.Zero;
                _mediaRepeats = 0;
                return null;
            }

            if (!string.Equals(sample.Title, _mediaTitle, StringComparison.Ordinal))
            {
                _mediaRepeats = 1;
                if (_mediaTitle != null) _mediaChangePending = true;
                _mediaTitle = sample.Title;
            }
            else if (sample.Position + TimeSpan.FromSeconds(MediaRewindSeconds) < _mediaPosition)
            {
                // The track started over. Only worth a frame once it is genuinely a loop — a single
                // replay is not a bit, and every restart becoming a candidate would be its own spam.
                _mediaRepeats++;
                if (_mediaRepeats >= ActivityLedger.MediaLoopMinimum) _mediaChangePending = true;
            }

            _mediaPosition = sample.Position;
            return sample;
        }

        private int SafeIdleSeconds()
        {
            try { return Math.Max(0, _input.IdleSeconds); } catch { return 0; }
        }

        private bool SafeTypingBurst()
        {
            try { return _input.IsTypingBurst; } catch { return false; }
        }

        private bool SafeMicInUse(DateTime now)
        {
            try { return _microphone.IsInUse(now); } catch { return false; }
        }

        private AppStateSample SafeAppState(DateTime now)
        {
            try { return _appState.Read(now) ?? AppStateSample.Empty; } catch { return AppStateSample.Empty; }
        }

        private async Task<IReadOnlyList<HabitRecord>> SafeHabitsAsync(string appId, string? cluster)
        {
            try { return await _memory.GetHabitsAsync(appId, cluster).ConfigureAwait(false) ?? Array.Empty<HabitRecord>(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("AwarenessObserver: habit lookup failed - {Error}", ex.Message);
                return Array.Empty<HabitRecord>();
            }
        }

        private async Task<IReadOnlyList<ReactionSummary>> SafeRecentAsync()
        {
            try
            {
                return await _memory.GetRecentReactionsAsync(AwarenessProjection.MaxRecentReactions)
                    .ConfigureAwait(false) ?? Array.Empty<ReactionSummary>();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AwarenessObserver: ban-list lookup failed - {Error}", ex.Message);
                return Array.Empty<ReactionSummary>();
            }
        }
    }
}
