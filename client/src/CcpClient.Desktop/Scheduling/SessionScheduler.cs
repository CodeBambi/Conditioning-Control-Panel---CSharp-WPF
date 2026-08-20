using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Scheduling;

/// <summary>What one tick did. Exactly one per tick, never a bool.</summary>
public enum SchedulerAction
{
    /// <summary>The enable is off; the tick returned before reading the clock.</summary>
    Disabled,

    /// <summary>A session was started that nobody pressed a button for.</summary>
    Started,

    /// <summary>The start conditions held and <see cref="SessionEngine.Start"/> refused.</summary>
    StartRefusedBySession,

    /// <summary>A session this scheduler started was stopped because its window closed.</summary>
    Stopped,

    /// <summary>Outside the window; both flags cleared for the next opening.</summary>
    FlagsCleared,

    /// <summary>Inside the window and deliberately doing nothing.</summary>
    Held,
}

/// <summary>One tick's typed outcome: what it did, why, and the reading it decided on.</summary>
/// <param name="Action">What happened.</param>
/// <param name="Code">A <see cref="SchedulerReasonCodes"/> value.</param>
/// <param name="Detail">The sentence a user or a bug report can read.</param>
/// <param name="Reading">The window evaluation this tick used, or null when the enable was off and
/// the clock was never read (WPF returns at <c>MainWindow/MainWindow.StartStop.cs:604</c> before
/// <c>IsInScheduledTimeWindow()</c> is called).</param>
public sealed record SchedulerTickOutcome(
    SchedulerAction Action, string Code, string Detail, ScheduleWindowReading? Reading);

/// <summary>
/// SP-118 — the <b>Scheduler</b>: WPF's <c>SchedulerTimer_Tick</c>
/// (<c>MainWindow/MainWindow.StartStop.cs:601-639</c>).
///
/// <para><b>This is the first thing the port has built that OWNS the engine rather than running
/// under it.</b> Thirteen ported rows are modules INSIDE a session somebody started. This one runs
/// while nothing is running and calls <see cref="SessionEngine.Start"/> (WPF's
/// <c>StartEngine()</c>, <c>:618</c>) and <see cref="SessionEngine.Stop"/> (WPF's
/// <c>StopEngine()</c>, <c>:629</c>). A defect here does not degrade an effect — it puts a
/// conditioning session on a user's screen unbidden, or refuses to end one.</para>
///
/// <para><b>So the interesting half of this class is where it says NO</b>, and every clause below
/// is one of upstream's, in upstream's order:</para>
/// <list type="number">
/// <item><b>the enable</b> (<c>:604</c>) — and it returns FIRST, before the clock is read, which is
/// why a disabled scheduler also clears no flags and stops nothing it had started;</item>
/// <item><b>the window</b> (<c>:606</c> → <see cref="ScheduleWindow"/>);</item>
/// <item><b>a session already running</b> (<c>:608</c> <c>!_isRunning</c>);</item>
/// <item><b>this window opening already served</b> (<c>:608</c> <c>!_schedulerAutoStarted</c>);</item>
/// <item><b>the user stopped it by hand inside this window</b> (<c>:608</c>
/// <c>!_manuallyStoppedDuringSchedule</c>, set at <c>:98-101</c>) — the strongest of the five,
/// because it is the one that answers "I pressed STOP and it came back".</item>
/// </list>
///
/// <para><b>It drives the REAL engine, never a seam.</b> There is no <c>ISchedulerTarget</c> here
/// and there will not be one: SP-110's review found a user-harm defect that survived a whole
/// mutation sweep because "the test double diverged from the product exactly where the bug lived",
/// and the cheapest way not to repeat that is not to have a double. A unit test builds a real
/// <see cref="SessionEngine"/> over an empty effect list.</para>
///
/// <para><b>Threading.</b> <see cref="Tick"/> arrives on a thread-pool thread from
/// <see cref="IScheduleClock.Schedule"/>. The decision and the engine call happen TOGETHER under
/// <see cref="Gate"/> on that thread — not posted — because the port already stops the engine from
/// a pool thread (<c>Session/SessionParticipant.cs</c>: <c>Ramp.Completed += () =&gt; Engine.Stop()</c>)
/// and because a decision posted away from its own flag write would open a window in which two
/// ticks could both decide to start. Everything the UI must see travels the other way, through
/// <see cref="EffectSignal"/>, which is the port's one marshalling rule.</para>
/// </summary>
public sealed class SessionScheduler
{
    /// <summary>WPF's rack key for this row (<c>Views/Tabs/StudioTabView.xaml.cs:535</c>). Never a
    /// display string, never localized.</summary>
    public const string RowId = "scheduler";

    /// <summary>WPF's <c>DispatcherTimer</c> interval (<c>MainWindow/MainWindow.xaml.cs:618</c>).</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    /// <summary>WPF's <c>const int schedulerGracePeriodSeconds = 60</c>
    /// (<c>MainWindow/MainWindow.xaml.cs:624</c>): the scheduler does nothing at all for the first
    /// minute of a launch, so an app restarted mid-window by an update does not immediately
    /// re-open a session on top of whatever the user is doing (<c>:622-623</c>).</summary>
    public static readonly TimeSpan StartupGrace = TimeSpan.FromSeconds(60);

    private readonly PersistenceStore<SchedulerPresetDocument> _store;
    private readonly SessionEngine _engine;
    private readonly IScheduleClock _clock;
    private readonly EffectSignal _signal;
    private readonly object _gate = new();

    private bool _autoStarted;
    private bool _manuallyStopped;
    private bool _polling;
    private SchedulerTickOutcome? _last;
    private int _starts;
    private int _stops;

    /// <param name="store">The ten persisted settings.</param>
    /// <param name="engine">The REAL session. The scheduler owns it from outside; nothing else does.</param>
    /// <param name="clock">The LOCAL clock (<see cref="IScheduleClock"/>), never
    /// <see cref="ISessionClock"/>: the user typed a wall-clock time.</param>
    /// <param name="signal">Where state changes and the auto-start projection are allowed to
    /// arrive. Same boundary every module uses.</param>
    public SessionScheduler(
        PersistenceStore<SchedulerPresetDocument> store,
        SessionEngine engine,
        IScheduleClock clock,
        EffectSignal signal)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(signal);
        _store = store;
        _engine = engine;
        _clock = clock;
        _signal = signal;
    }

    /// <summary>Raised when the scheduler's own state moves, on the signal thread.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised, as a PROJECTION on the UI thread, when the scheduler has just auto-started a
    /// session. The shell answers it with <see cref="Tray.ShellTray.Duck"/> — WPF's
    /// <c>_trayIcon?.MinimizeToTray()</c> at <c>MainWindow/MainWindow.StartStop.cs:615</c>, which
    /// runs INSIDE the same <c>Dispatcher.Invoke</c> as the start.
    ///
    /// <para>Skip-until-bound: with no window there is nothing to minimize, and the session still
    /// starts. The balloon WPF sends beside the minimize (<c>:616</c>) is NOT sent: <c>ShellTray</c>
    /// exposes no arbitrary-notification entry and <c>Tray/**</c> was outside this packet's File
    /// Scope. The absence is stated on the panel where a user reads it, and recorded as D183 —
    /// deliberately NOT as a reason code, because nothing would ever emit one.</para>
    /// </summary>
    public event Action? AutoStarted;

    /// <summary>The persisted settings currently in force.</summary>
    public SchedulerPresetDocument Preset => _store.Current;

    /// <summary>WPF's <c>SchedulerEnabled</c> — the row's master switch and the tick's first
    /// clause.</summary>
    public bool Enabled => _store.Current.Enabled;

    /// <summary>
    /// True only while a tick is REALLY on the clock: the 60 s grace has elapsed and the
    /// participant has armed the next poll. This is the scheduler's analogue of
    /// <c>PacedSessionEffect.ScheduleArmed</c> and it is what the dot is entitled to read — not a
    /// stored "I was told to start" bool, which is exactly what the spine forbids.
    /// </summary>
    public bool Polling
    {
        get { lock (_gate) { return _polling; } }
    }

    /// <summary>WPF's <c>_schedulerAutoStarted</c> (<c>MainWindow.xaml.cs:207</c>): true only
    /// between a start this scheduler made and the stop or reset that answers it. It is what stops
    /// the scheduler ever ending a session it did not begin.</summary>
    public bool AutoStartedSession
    {
        get { lock (_gate) { return _autoStarted; } }
    }

    /// <summary>WPF's <c>_manuallyStoppedDuringSchedule</c> (<c>MainWindow.xaml.cs:208</c>): true
    /// from the moment the user presses STOP inside an active window until that window closes.
    /// While it is set the scheduler will not start anything, which is the whole point of it.</summary>
    public bool ManuallyStoppedInWindow
    {
        get { lock (_gate) { return _manuallyStopped; } }
    }

    /// <summary>The last tick's typed outcome; null before the first one.</summary>
    public SchedulerTickOutcome? Last
    {
        get { lock (_gate) { return _last; } }
    }

    /// <summary>How many sessions this scheduler has started by itself.</summary>
    public int StartsPerformed
    {
        get { lock (_gate) { return _starts; } }
    }

    /// <summary>How many it has ended by itself.</summary>
    public int StopsPerformed
    {
        get { lock (_gate) { return _stops; } }
    }

    /// <summary>The window as it stands right now, read off the LOCAL clock. The panel renders
    /// this; the tick computes its own so a repaint can never move a decision.</summary>
    public ScheduleWindowReading Reading => ScheduleWindow.Read(_store.Current, _clock.LocalNow);

    /// <summary>
    /// The row's dot.
    ///
    /// <para><b>Upstream passes a dot here</b> — <c>() =&gt; App.Settings?.Current?.SchedulerEnabled</c>
    /// (<c>Views/Tabs/StudioTabView.xaml.cs:536</c>), unlike Visuals' <c>null</c> at <c>:496</c> —
    /// so this row has one. But upstream's is a claim about a CHECKBOX and this port's dot is a
    /// claim about what the module can DO, so the three states are earned:</para>
    /// <list type="bullet">
    /// <item><see cref="EffectDotState.Off"/> — the enable is off, OR nothing is on the clock (the
    /// 60 s grace has not elapsed, or the participant has stopped). Both mean the same thing to a
    /// user: this will not act.</item>
    /// <item><see cref="EffectDotState.Armed"/> — enabled, really polling, and the local clock is
    /// OUTSIDE the window. It will act when the window opens.</item>
    /// <item><see cref="EffectDotState.Live"/> — enabled, really polling, and the local clock is
    /// INSIDE the window right now.</item>
    /// </list>
    /// <para><b>Live is NOT "a session is running".</b> A scheduler inside its window whose session
    /// the user stopped by hand is still doing its job — waiting out the window without restarting
    /// anything — and a dot that went dark there would claim the schedule had ended. What the
    /// module is doing, the panel says in words.</para>
    /// </summary>
    public EffectDotState Dot
    {
        get
        {
            if (!Enabled)
            {
                return EffectDotState.Off;
            }

            if (!Polling)
            {
                return EffectDotState.Off;
            }

            return Reading.InWindow ? EffectDotState.Live : EffectDotState.Armed;
        }
    }

    /// <summary>The row's master switch — upstream's quick-toggle flips the panel's own enable box
    /// and saves (<c>Views/Tabs/StudioTabView.xaml.cs:533-537</c>,
    /// <c>Features/SchedulerFeatureControl.xaml.cs:63-69</c>), which is what this does.</summary>
    public void SetEnabled(bool enabled)
    {
        _store.Mutate(doc => doc.Enabled = enabled);
        _ = _store.Save();
        RaiseChanged();
    }

    /// <summary>Write both time strings, as upstream's one <c>LostFocus</c> handler does — it
    /// assigns start AND end and saves once (<c>Features/SchedulerFeatureControl.xaml.cs:71-79</c>).</summary>
    public void SetTimes(string? start, string? end)
    {
        _store.Mutate(doc =>
        {
            doc.StartTime = start;
            doc.EndTime = end;
        });
        _ = _store.Save();
        RaiseChanged();
    }

    /// <summary>Write one day box. Upstream writes all seven from one handler
    /// (<c>Features/SchedulerFeatureControl.xaml.cs:81-93</c>); per-day is the same outcome and
    /// keeps the panel's seven checkboxes independent.</summary>
    public void SetDay(DayOfWeek day, bool active)
    {
        _store.Mutate(doc =>
        {
            switch (day)
            {
                case DayOfWeek.Monday: doc.Monday = active; break;
                case DayOfWeek.Tuesday: doc.Tuesday = active; break;
                case DayOfWeek.Wednesday: doc.Wednesday = active; break;
                case DayOfWeek.Thursday: doc.Thursday = active; break;
                case DayOfWeek.Friday: doc.Friday = active; break;
                case DayOfWeek.Saturday: doc.Saturday = active; break;
                case DayOfWeek.Sunday: doc.Sunday = active; break;
                default: break;
            }
        });
        _ = _store.Save();
        RaiseChanged();
    }

    /// <summary>Called by the participant when the grace has elapsed and a poll is really on the
    /// clock, and again with <c>false</c> when it stops. It is the ONLY writer of
    /// <see cref="Polling"/>, so the dot can never claim a tick that is not scheduled.</summary>
    internal void SetPolling(bool polling)
    {
        lock (_gate)
        {
            if (_polling == polling)
            {
                return;
            }

            _polling = polling;
        }

        RaiseChanged();
    }

    /// <summary>
    /// WPF's <c>CheckSchedulerOnStartup()</c> (<c>MainWindow/MainWindow.StartStop.cs:562-581</c>),
    /// run once when the 60 s grace elapses (<c>MainWindow.xaml.cs:635</c>).
    ///
    /// <para>It reads only the enable and the window — <b>not</b> the two flags, which are both
    /// false at launch anyway, and <b>not</b> whether a session is running, which upstream can get
    /// away with because <c>StartEngine()</c> has no guard of its own. The port's engine does have
    /// one, so a session the user started during the grace is left alone and NOT marked
    /// auto-started (<see cref="SchedulerReasonCodes.SchedulerStartRefusedBySession"/>).</para>
    /// </summary>
    public SchedulerTickOutcome RunStartupCheck()
    {
        SchedulerTickOutcome outcome;
        var autoStartedNow = false;

        lock (_gate)
        {
            // WPF :568 — the enable, first and alone.
            if (!_store.Current.Enabled)
            {
                outcome = Record(new SchedulerTickOutcome(
                    SchedulerAction.Disabled, SchedulerReasonCodes.SchedulerDisabled,
                    "the scheduler is switched off, so the start-up check read no clock and changed nothing",
                    Reading: null));
            }
            else
            {
                var reading = ScheduleWindow.Read(_store.Current, _clock.LocalNow);

                // WPF :570 — in the window at launch means auto-start.
                if (!reading.InWindow)
                {
                    outcome = Record(new SchedulerTickOutcome(
                        SchedulerAction.Held, SchedulerReasonCodes.SchedulerHeld,
                        $"the app started outside the scheduled window ({Describe(reading)}), so nothing was started",
                        reading));
                }
                else if (_engine.Start())
                {
                    _autoStarted = true;
                    _starts++;
                    autoStartedNow = true;
                    outcome = Record(new SchedulerTickOutcome(
                        SchedulerAction.Started, SchedulerReasonCodes.SchedulerStartedSession,
                        $"the app started inside the scheduled window ({Describe(reading)}), so a session was started for you",
                        reading));
                }
                else
                {
                    // The port's divergence, and the safe half of it: WPF would have re-armed every
                    // service over the running session and then marked it auto-started.
                    outcome = Record(new SchedulerTickOutcome(
                        SchedulerAction.StartRefusedBySession,
                        SchedulerReasonCodes.SchedulerStartRefusedBySession,
                        "the app started inside the scheduled window, but a session was already running — "
                        + "it was left alone and is NOT counted as scheduler-started, so the window closing will not stop it",
                        reading));
                }
            }
        }

        Announce(autoStartedNow);
        return outcome;
    }

    /// <summary>
    /// WPF's <c>SchedulerTimer_Tick</c> (<c>MainWindow/MainWindow.StartStop.cs:601-639</c>), body
    /// for body, branch for branch, in its order.
    /// </summary>
    public SchedulerTickOutcome Tick()
    {
        SchedulerTickOutcome outcome;
        var autoStartedNow = false;

        lock (_gate)
        {
            // :604 — and it returns before IsInScheduledTimeWindow() is ever called, which is why a
            // disabled scheduler neither clears its flags nor stops a session it started. Both
            // consequences are upstream's and both are pinned.
            if (!_store.Current.Enabled)
            {
                outcome = Record(new SchedulerTickOutcome(
                    SchedulerAction.Disabled, SchedulerReasonCodes.SchedulerDisabled,
                    "the scheduler is switched off, so this tick read no clock and changed nothing",
                    Reading: null));
            }
            else
            {
                // :606
                var reading = ScheduleWindow.Read(_store.Current, _clock.LocalNow);

                if (reading.InWindow && !_engine.Running && !_autoStarted && !_manuallyStopped)
                {
                    // :608-620. WPF's order inside the Dispatcher.Invoke is minimize, balloon,
                    // StartEngine, flag. The minimize is a UI projection and leaves through
                    // AutoStarted below, AFTER the flag, so the decision and the flag stay atomic.
                    if (_engine.Start())
                    {
                        _autoStarted = true;
                        _starts++;
                        autoStartedNow = true;
                        outcome = Record(new SchedulerTickOutcome(
                            SchedulerAction.Started, SchedulerReasonCodes.SchedulerStartedSession,
                            $"the scheduled window opened ({Describe(reading)}), so a session was started for you",
                            reading));
                    }
                    else
                    {
                        outcome = Record(new SchedulerTickOutcome(
                            SchedulerAction.StartRefusedBySession,
                            SchedulerReasonCodes.SchedulerStartRefusedBySession,
                            "the scheduled window is open and the session refused the start, so nothing is "
                            + "counted as scheduler-started",
                            reading));
                    }
                }
                else if (!reading.InWindow && _engine.Running && _autoStarted)
                {
                    // :622-632. Stop first, then clear the flag — WPF's order, and the one that
                    // cannot leave a stopped session still marked as the scheduler's.
                    _engine.Stop();
                    _autoStarted = false;
                    _stops++;
                    outcome = Record(new SchedulerTickOutcome(
                        SchedulerAction.Stopped, SchedulerReasonCodes.SchedulerStoppedSession,
                        $"the scheduled window closed ({Describe(reading)}), so the session it had started was ended",
                        reading));
                }
                else if (!reading.InWindow)
                {
                    // :635-639. Both flags, for the next opening. This is also the ONLY thing that
                    // ever clears the manual-stop flag from the scheduler's side, which is why a
                    // hand-stopped session stays stopped for the rest of its window.
                    _autoStarted = false;
                    _manuallyStopped = false;
                    outcome = Record(new SchedulerTickOutcome(
                        SchedulerAction.FlagsCleared, SchedulerReasonCodes.SchedulerClearedFlags,
                        $"outside the scheduled window ({Describe(reading)}); ready for the next opening",
                        reading));
                }
                else
                {
                    // All three branches false: in the window, and deliberately doing nothing.
                    outcome = Record(new SchedulerTickOutcome(
                        SchedulerAction.Held, SchedulerReasonCodes.SchedulerHeld,
                        HeldDetail(reading), reading));
                }
            }
        }

        Announce(autoStartedNow);
        return outcome;
    }

    /// <summary>
    /// The user pressed the ONE start/stop control. WPF's <c>BtnStart_Click</c> writes the
    /// manual-stop flag on the way past, in two branches
    /// (<c>MainWindow/MainWindow.StartStop.cs:98-101</c> and <c>:106-107</c>), and it reads
    /// <c>_isRunning</c> BEFORE it calls <c>StopEngine</c>/<c>StartEngine</c> — so this is called
    /// with the session's state as it was before the press.
    /// </summary>
    /// <param name="sessionWasRunning">True when the press is a STOP, false when it is a START.</param>
    public void NoteManualToggle(bool sessionWasRunning)
    {
        lock (_gate)
        {
            if (sessionWasRunning)
            {
                // :98-101 — the flag is set ONLY when the scheduler is enabled AND the moment is
                // inside the window. Stopping at 3 a.m. outside any window sets nothing, because
                // there is no scheduled start for it to countermand.
                if (_store.Current.Enabled && ScheduleWindow.Read(_store.Current, _clock.LocalNow).InWindow)
                {
                    _manuallyStopped = true;
                }
            }
            else
            {
                // :106-107 — a manual START is the user overriding their own earlier stop.
                _manuallyStopped = false;
            }
        }

        RaiseChanged();
    }

    private static string Describe(ScheduleWindowReading reading)
    {
        var window = reading.Overnight
            ? $"{Hhmm(reading.Start)} to {Hhmm(reading.End)} the next day"
            : $"{Hhmm(reading.Start)} to {Hhmm(reading.End)}";
        return $"{reading.Day}, {Hhmm(reading.TimeOfDay)}, window {window}";
    }

    private static string Hhmm(TimeSpan value) =>
        value.Ticks is >= 0 and < TimeSpan.TicksPerDay
            ? value.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture)
            // A parsed value outside a single day is exactly what "8" produces (eight DAYS), so it
            // is shown whole rather than truncated into something that looks like a time.
            : value.ToString(null, System.Globalization.CultureInfo.InvariantCulture);

    private string HeldDetail(ScheduleWindowReading reading)
    {
        var why = _engine.Running && !_autoStarted
            ? "a session you started yourself is already running, and the scheduler will not touch it"
            : _manuallyStopped
                ? "you stopped a session by hand inside this window, so nothing will be started again until it closes"
                : _autoStarted
                    ? "the session for this window has already been started"
                    : "a session is already running";
        return $"inside the scheduled window ({Describe(reading)}) — {why}";
    }

    private SchedulerTickOutcome Record(SchedulerTickOutcome outcome)
    {
        _last = outcome;
        return outcome;
    }

    private void Announce(bool autoStartedNow)
    {
        if (autoStartedNow)
        {
            // The projection: WPF minimizes to tray inside the same invoke as the start
            // (:615). Posted, never inline — it touches a window.
            _signal.Post(() => AutoStarted?.Invoke());
        }

        RaiseChanged();
    }

    private void RaiseChanged() => _signal.Raise(() => Changed?.Invoke());
}
