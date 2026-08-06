using System;
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
    ///   → transition detector (did the resolved app id change?)
    ///     → DWELL GATE: a candidate must survive ~20s before a frame is cut
    ///   → frame builder (ledger history + live signals + in-app state)
    ///   → worthiness scorer
    ///   → DND filter
    ///   → arbiter → bark | LLM | silence
    /// </code>
    ///
    /// <para><b>Shell status.</b> The lifecycle, the gates and the dependency wiring are final; the poll
    /// body is a marked TODO for the observer package. What is already load-bearing here is the
    /// lifecycle contract: the ledger is started (and therefore pruned) from <see cref="Start"/>, with
    /// nothing to do with any UI being open — a retention promise honoured only when someone opens the
    /// Companion tab is not a retention promise.</para>
    ///
    /// <para><b>Frames are cut on events, never on ticks.</b> There is no code path from a timer to an
    /// LLM call in this design, and v2 must not grow one (doc 02 §7.6).</para>
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

        private readonly ActivityLedger _ledger;
        private readonly WorthinessScorer _scorer;
        private readonly IReactionArbiter _arbiter;
        private readonly ICompanionMemory _memory;
        private readonly Func<DateTime> _clock;

        private DispatcherTimer? _pollTimer;
        private bool _running;
        private bool _disposed;

        public AwarenessObserver(
            ActivityLedger ledger,
            WorthinessScorer scorer,
            IReactionArbiter arbiter,
            ICompanionMemory memory,
            Func<DateTime>? localClock = null)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _scorer = scorer ?? throw new ArgumentNullException(nameof(scorer));
            _arbiter = arbiter ?? throw new ArgumentNullException(nameof(arbiter));
            _memory = memory ?? throw new ArgumentNullException(nameof(memory));
            _clock = localClock ?? (() => DateTime.Now);
        }

        /// <summary>Raised when a frame has been cut, scored and cleared for the arbiter.</summary>
        public event EventHandler<ContextFrame>? FrameCut;

        /// <summary>The ledger this observer feeds. Shared with the privacy panel's forget/wipe controls.</summary>
        public ActivityLedger Ledger => _ledger;

        /// <summary>The pacing state. Shared so a delivered line raises the threshold wherever it came from.</summary>
        public WorthinessScorer Scorer => _scorer;

        /// <summary>True between a successful <see cref="Start"/> and <see cref="Stop"/>.</summary>
        public bool IsRunning => _running;

        /// <summary>
        /// Whether v2 may run at all: the kill switch, the feature toggle and consent, all three.
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
                    return s.UseAwarenessV2 && s.AwarenessModeEnabled && s.AwarenessConsentGiven;
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

            if (!_running) return;
            _running = false;

            try { _ledger.NoteFocusEnd(_clock()); } catch { }
            _ledger.Stop();
            App.Logger?.Information("AwarenessObserver: stopped");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Stop(); } catch { }
        }

        /// <summary>
        /// Publishes a cut frame. The observer package calls this at the end of its pipeline; it is
        /// internal-facing rather than private so the arbiter and prompt packages can drive the whole
        /// chain from a synthetic frame in tests without a foreground window.
        /// </summary>
        public void PublishFrame(ContextFrame frame)
        {
            if (frame == null) return;

            try
            {
                FrameCut?.Invoke(this, frame);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AwarenessObserver: a FrameCut subscriber threw");
            }

            _ = SubmitAsync(frame);
        }

        private async Task SubmitAsync(ContextFrame frame)
        {
            try
            {
                var decision = await _arbiter.SubmitAsync(frame).ConfigureAwait(false);
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

            try
            {
                // Keeps "today"/"this week" honest and rolls the day over while the machine sits idle.
                _ledger.Heartbeat(_clock());

                // TODO(observer package): read the foreground window, resolve it through AppClusterMap,
                // run it past the privacy layer (deny list, incognito hard-drop, title allow list),
                // apply the dwell gate, detect the transition kind, pull real input idle from
                // ActivityTracker, read fullscreen + SMTC, build the ContextFrame with the ledger's
                // snapshot and trends and the memory hooks, score it, apply DND, then PublishFrame.
                //
                // Unused-field warnings are suppressed by these reads until that lands; the deps are
                // final and every package branches from them.
                _ = _scorer;
                _ = _memory;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AwarenessObserver: poll tick failed");
            }
        }
    }
}
