using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// One counting game, as a subscriber sees it. <b>No path, no file name and no COUNT</b>: the clips
/// are the user's own media, and the number of bubbles is the answer — a record that carried it
/// would put the answer in every log line and every bug report the moment one is written.
/// </summary>
/// <param name="Ordinal">Which game this was, from 1.</param>
/// <param name="At">When it started, on the session clock.</param>
/// <param name="Difficulty">Which dial it was played at.</param>
public readonly record struct BubbleCountEvent(int Ordinal, DateTimeOffset At, BubbleCountDifficulty Difficulty);

/// <summary>What one firing produced, before it is delivered.</summary>
/// <param name="Event">The public half.</param>
/// <param name="Path">The clip. Stays inside the module and the surface.</param>
public sealed record BubbleCountFiring(BubbleCountEvent Event, string Path);

/// <summary>How a counting game ended.</summary>
public enum BubbleCountResolution
{
    /// <summary>Still running, or none has been played.</summary>
    None,

    /// <summary>The user got the count right. THE answer.</summary>
    Counted,

    /// <summary>Every attempt was spent on a wrong number.</summary>
    Missed,

    /// <summary>The user pressed Escape at the question. No answer, deliberately given.</summary>
    Dismissed,

    /// <summary>The session stopped, or the module was switched off, with the game still up.</summary>
    Withdrawn,

    /// <summary>The operating system would not give the question the input, so it was taken straight
    /// back down rather than left on screen unanswerable.</summary>
    Refused,

    /// <summary>
    /// The clip could not really be watched — the surface refused it, stopped holding it, or ended
    /// before one bubble had been drawn into a picture — <b>so the question was never asked</b>.
    /// Upstream's own answer to a game it could not show: skip it outright rather than let it land in
    /// the completion callback as a failure (<c>Services/BubbleCountService.cs:224-233</c>).
    /// </summary>
    Abandoned,
}

/// <summary>
/// <b>Bubble Count</b> — WPF's second GAMES &amp; CARDS rack row
/// (<c>Views/Tabs/StudioTabView.xaml.cs:502-503</c>), started by <c>StartEngine</c> at
/// <c>MainWindow/MainWindow.StartStop.cs:212-215</c> and stopped at <c>:340</c> with its windows
/// force-closed at <c>:368-369</c>.
///
/// <para><b>WHY THIS MODULE EXISTS IN THE PORT: it is the first module that consumes a
/// capability it did not shape — two of them.</b> Nine modules run; three capabilities landed in the
/// three waves before this one, and each had exactly one consumer, the module that introduced it. A
/// capability with one consumer is a capability shaped around one caller. This row plays a clip
/// through the shared video capability and asks its question through the shared input capability, and
/// its compatibility is established by the capability contracts and focused tests rather than by a
/// successful compile.</para>
///
/// <para><b>The two capabilities, and what each is asked for.</b> The clip goes up on the SHARED
/// <see cref="IVideoSurface"/> — the same instance Mandatory Video plays on, which is what makes the
/// two video-class rows mutually exclusive the way upstream's interaction queue makes them
/// (<c>Services/BubbleCountService.cs:169-186</c>) without this port having a queue. The bubbles are
/// painted INTO the clip's own pictures through <see cref="IVideoFramePainter"/>, so the video
/// capability's own read-back is what proves the operating system was holding them. The question
/// goes up on the SHARED <see cref="IInputPresence"/>, the same instance the Lock Card asks
/// through.</para>
///
/// <para><b>THE DOT reuses TWO existing meanings and invents none.</b> See
/// <see cref="WorkIsRunning"/>.</para>
///
/// <para><b>What is NOT ported</b>, and is declared rather than stubbed: the strict lock and the
/// whole WRONG! WATCH AGAIN retry/mercy machine, including the mercy LOCK CARD a failed non-strict
/// game shows (<c>BubbleCountService.cs:296-436</c>,
/// <c>Windows/BubbleCountResultWindow.xaml.cs:296-340</c>); the game's own XP grant
/// (<c>BubbleCountService.cs:303</c>, <c>BubbleCountResultWindow.xaml.cs:201</c> — the port has an
/// XP ledger now, <c>Features/Progression/ProgressionLedger</c>, but this effect has no completion
/// payout to hand it) and its achievements; the interaction queue and its stuck-detection timeouts
/// (<c>BubbleCountService.cs:169-186</c>, <c>:262-268</c>); the fullscreen cover on every monitor
/// (<c>Windows/BubbleCountWindow.xaml.cs:229-380</c>) — this port plays on one bounded rectangle on
/// the primary display, D123's placement; content-pack clips and their decryption
/// (<c>BubbleCountService.cs:551-575</c>); the browser video engine and the LibVLC poison cooldown
/// (<c>:224-240</c>); the Bambi-freeze subliminal and the bubble-pop pause that bracket the game
/// (<c>:200-204</c>); the pop SOUND; and the #633 inactivity watchdog, whose cause cannot occur here
/// because Escape always closes a card in this build (D112). Each is a subsystem this port does not
/// have, and a silent no-op would make the row look complete.</para>
///
/// <para><b>The level-50 unlock is NOT among them, because it is not a gate upstream either.</b>
/// <c>BubbleCountService.cs:19</c> says <i>"Unlocks at Level 50"</i> in a doc comment and no code
/// anywhere reads it; the one gating call the app still has funnels through
/// <c>AppSettings.IsLevelUnlocked</c>, which is <c>return true;</c>
/// (<c>Models/AppSettings.cs:5434-5442</c>: <i>"Feature level gating has been removed"</i>). The 50
/// is recorded as data on <c>Features/Progression/LevelUnlocks</c> and refuses nobody, here or
/// there.</para>
/// </summary>
public sealed class BubbleCountEffect : PacedSessionEffect<BubbleCountFiring>
{
    /// <summary>WPF's rack key for this module (<c>StudioTabView.xaml.cs:502</c>), and the same key
    /// its quick-toggle switches on.</summary>
    public const string EffectId = "bubblecount";

    /// <summary>The row's label as the shipping app shows it, minus the emoji the port's rack does
    /// not render (<c>StudioTabView.xaml.cs:502</c> — "🔢 Bubble Count").</summary>
    public const string DisplayTitle = "Bubble Count";

    /// <summary>How much of the primary display the question card covers. The same fractions the
    /// Lock Card uses, for the same reason and under the same divergence (D110): upstream's result
    /// window is maximised on every monitor
    /// (<c>Windows/BubbleCountResultWindow.xaml.cs:86</c>, <c>:111-139</c>).</summary>
    public const double CardWidthFraction = 0.55;

    /// <summary>See <see cref="CardWidthFraction"/>.</summary>
    public const double CardHeightFraction = 0.38;

    /// <summary>The exit line on the question card. Escape is always live here — see
    /// <see cref="BubbleCountAnswer"/> and D112.</summary>
    public const string GiveUpHint = "Press Esc to give up";

    private readonly PersistenceStore<BubbleCountPresetDocument> _preset;
    private readonly IVideoClipPool _pool;
    private readonly IVideoSurface _surface;
    private readonly IInputPresence _presence;
    private readonly Random _random;
    private readonly Func<InputBounds> _placement;

    private BubbleCountRun? _run;
    private BubbleCountAnswer? _answer;
    private IDisposable? _safety;
    private bool _playing;
    private CapabilityState? _lastPlayback;
    private CapabilityState? _lastPrompt;
    private BubbleCountResolution _lastResolution = BubbleCountResolution.None;
    private int _countedCount;
    private int _missedCount;
    private int _abandonedCount;
    private int _lastAskedAbout;

    /// <param name="owner">This module's operation owner: one generation per armed schedule.</param>
    /// <param name="signal">The one boundary a state change, a draw or a safety end may arrive on.</param>
    /// <param name="clock">The clock the FIRING schedule and the safety end both ride.</param>
    /// <param name="preset">This module's persisted dials.</param>
    /// <param name="pool">Where the clips come from — the same folder Mandatory Video reads, which
    /// is upstream's own arrangement (<c>Services/BubbleCountService.cs:63</c> and
    /// <c>Services/Video/VideoService.cs:1212</c> both compose
    /// <c>&lt;assets&gt;/videos</c>), through a SEPARATE pool instance so each row deals its own
    /// shuffled bag exactly as upstream's two services keep their own lists.</param>
    /// <param name="surface">Where the pictures go. SHARED with Mandatory Video.</param>
    /// <param name="presence">Where the question goes. SHARED with the Lock Card.</param>
    /// <param name="random">Injected, so a fact pins the arithmetic rather than re-deriving it.</param>
    /// <param name="placement">Where the question card goes; the primary display by default.</param>
    public BubbleCountEffect(
        AsyncOperationOwner owner,
        EffectSignal signal,
        ISessionClock clock,
        PersistenceStore<BubbleCountPresetDocument> preset,
        IVideoClipPool pool,
        IVideoSurface surface,
        IInputPresence presence,
        Random? random = null,
        Func<InputBounds>? placement = null)
        : base(owner, signal, clock, "bubblecount-schedule")
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(presence);
        _preset = preset;
        _pool = pool;
        _surface = surface;
        _presence = presence;
        _random = random ?? new Random();
        _placement = placement ?? DefaultPlacement;
    }

    /// <inheritdoc/>
    public override string Id => EffectId;

    /// <inheritdoc/>
    public override string Title => DisplayTitle;

    /// <inheritdoc/>
    public override bool Enabled => _preset.Current.Enabled;

    /// <summary>This module's persisted dials (public so the panel reads the real document).</summary>
    public BubbleCountPresetDocument Preset => _preset.Current;

    /// <summary>The input capability this module asks through. Public so a panel can render the OS's
    /// own answer rather than a sentence about a platform.</summary>
    public IInputPresence Presence => _presence;

    /// <summary>How many clips the folder holds. The panel shows it so an empty folder has an
    /// answer.</summary>
    public int ClipCount => _pool.ActiveCount;

    /// <summary>Where the clips are read from. A path, never a file name.</summary>
    public string ClipFolder => _pool.Folder;

    /// <summary>Games that really started a clip.</summary>
    public int PlayedCount => FireCount;

    /// <summary>The most recent game, or null if none has happened yet.</summary>
    public BubbleCountEvent? Last => LastFiring?.Event;

    /// <summary>Games whose count the user got right.</summary>
    public int CountedCount
    {
        get { lock (Gate) { return _countedCount; } }
    }

    /// <summary>Games where every attempt was spent on a wrong number.</summary>
    public int MissedCount
    {
        get { lock (Gate) { return _missedCount; } }
    }

    /// <summary>Games that were never asked about, because the clip could not really be
    /// watched.</summary>
    public int AbandonedCount
    {
        get { lock (Gate) { return _abandonedCount; } }
    }

    /// <summary>How the last game ended. <see cref="BubbleCountResolution.None"/> while one is
    /// running.</summary>
    public BubbleCountResolution LastResolution
    {
        get { lock (Gate) { return _lastResolution; } }
    }

    /// <summary>
    /// How many bubbles the last question was really about — the number that was drawn into pictures
    /// the operating system confirmed it was holding. Zero before a question has been asked. The
    /// panel does NOT render it while a card is up: it is the answer.
    /// </summary>
    public int LastAskedAbout
    {
        get { lock (Gate) { return _lastAskedAbout; } }
    }

    /// <summary>What the VIDEO capability said about the last clip this module started, verbatim.
    /// Null before anything played.</summary>
    public CapabilityState? LastPlayback
    {
        get { lock (Gate) { return _lastPlayback; } }
    }

    /// <summary>What the INPUT capability said about the last question this module put up, verbatim.
    /// Null before anything was asked.</summary>
    public CapabilityState? LastPrompt
    {
        get { lock (Gate) { return _lastPrompt; } }
    }

    /// <summary>The run that is playing right now, or null. Public so a fact can read where the
    /// bubbles went without the module projecting it.</summary>
    public BubbleCountRun? Run
    {
        get { lock (Gate) { return _run; } }
    }

    /// <summary>The question that is up right now, or null.</summary>
    public BubbleCountAnswer? Answer
    {
        get { lock (Gate) { return _answer; } }
    }

    /// <summary>True while a clip THIS module started is on screen. Never
    /// <see cref="IVideoSurface.Showing"/> on its own: that surface is shared, and Mandatory Video's
    /// clip is not this module's work.</summary>
    public bool Playing => ClipIsUp;

    /// <summary>True while a question THIS module asked is up.</summary>
    public bool Asking => CardIsUp;

    /// <summary>Raised on the signal thread once per clip that really started.</summary>
    public event Action<BubbleCountEvent>? Started;

    /// <summary>Raised on the signal thread when a question really goes up.</summary>
    public event Action<BubbleCountEvent>? Asked;

    /// <summary>Raised when a game ends, however it ended — including the endings nobody answered.</summary>
    public event Action<BubbleCountResolution>? Resolved;

    /// <inheritdoc/>
    public override void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
        {
            return;
        }

        _preset.Mutate(p => p.Enabled = enabled);
        RaiseChanged();
    }

    /// <summary>The frequency dial. Writes and RE-PACES, the port's settled convention and
    /// upstream's own <c>RefreshSchedule</c> (<c>Services/BubbleCountService.cs:735-739</c>).</summary>
    public void SetPerHour(int perHour)
    {
        var clamped = Math.Clamp(perHour, BubbleCountSchedule.MinPerHour, BubbleCountSchedule.MaxPerHour);
        if (_preset.Current.PerHour == clamped)
        {
            return;
        }

        _preset.Mutate(p => p.PerHour = perHour);
        Refresh();
    }

    /// <summary>The difficulty dial. It changes the NEXT game, never the one on screen: upstream
    /// reads it once, at trigger time (<c>BubbleCountService.cs:243</c>), and a target that moved
    /// under a user mid-clip would be a count nobody could get right.</summary>
    public void SetDifficulty(BubbleCountDifficulty difficulty)
    {
        if (_preset.Current.Difficulty == difficulty)
        {
            return;
        }

        _preset.Mutate(p => p.Difficulty = difficulty);
        RaiseChanged();
    }

    /// <summary>
    /// <b>THE DOT — and it reuses the sixth and seventh meanings rather than inventing an
    /// eighth.</b>
    ///
    /// <code>
    /// Live = a firing is on the clock
    ///     &amp;&amp; the OS says this process can put a video surface on a display   (channel 1)
    ///     &amp;&amp; the OS says this process can put a window in front of a user    (channel 2)
    ///     &amp;&amp; ( no clip of MINE is up      OR  the OS's copy of the surface ADVANCED )
    ///     &amp;&amp; ( no question of MINE is up  OR  the OS says the card holds foreground+focus )
    /// </code>
    ///
    /// <para><b>The finding this expresses.</b> The seven meanings the dot has carried — clock,
    /// screen, change, custody, reach, demand, motion — are properties of the CAPABILITIES, not of
    /// the modules. This module consumes two capabilities and therefore inherits two meanings:
    /// MOTION while its clip plays, DEMAND while its question is up. The third and
    /// fourth clauses are a phase switch between two existing facts, not a new fact about the world,
    /// so no eighth meaning is owed.</para>
    ///
    /// <para><b>Both "of MINE" qualifiers are load-bearing and neither is a stored 'I was told to
    /// start' flag.</b> Each is a conjunction of this module's own intent with the CAPABILITY's own
    /// live answer (<c>_playing &amp;&amp; _surface.Showing</c>,
    /// <c>_answer is not null &amp;&amp; _presence.IsPrompting</c>). The surface and the presence are
    /// SHARED with two other rows, so a bare <c>_surface.Showing</c> would darken this row's dot for
    /// Mandatory Video's clip and a bare <c>_presence.IsPrompting</c> would darken it for a Lock
    /// Card — which is a lie about a module that is idle and healthy.</para>
    /// </summary>
    protected override bool WorkIsRunning =>
        ScheduleArmed
        && _surface.CanReachADisplay
        && _presence.CanReachAUser
        && (!ClipIsUp || _surface.Running)
        && (!CardIsUp || _presence.HoldsTheInput);

    /// <summary>The interval to the next game, from this module's own dial
    /// (<c>Services/BubbleCountService.cs:88-96</c>). Upstream has no first-game offset here — that
    /// is the Lock Card's (<c>LockCardService.cs:159-168</c>) and this scheduler simply spaces from
    /// the start (<c>:83-114</c>).</summary>
    protected override TimeSpan NextInterval() =>
        BubbleCountSchedule.Interval(_preset.Current.PerHour, _random.NextDouble());

    /// <summary>
    /// One game comes due. Upstream's own guards, in upstream's own order — with the two the port's
    /// capabilities add, which upstream has no analogue for because a WPF window on a WPF desktop is
    /// not in doubt.
    /// </summary>
    protected override BubbleCountFiring? Compose()
    {
        // (1) A video-class interaction is already on screen. Upstream defers this game behind
        // another fullscreen interaction and drops it outright when the feed is up
        // (BubbleCountService.cs:156-177); with no queue in this port, DropNoQueue is the whole
        // answer — and because the surface is SHARED, this one guard covers Mandatory Video's clip
        // and this module's own.
        if (_surface.Showing)
        {
            return null;
        }

        // (2) A card is already up — this module's question or the Lock Card's. Same rule, same
        // reason, on the other shared capability.
        if (_presence.IsPrompting)
        {
            return null;
        }

        // (3) Nowhere to show a picture, or nobody to ask. Not counted, not played, and the schedule
        // keeps running so a display or a desktop that comes back is used at the next game.
        if (!_surface.CanReachADisplay || !_presence.CanReachAUser)
        {
            return null;
        }

        // (4) No clip: upstream logs "No videos found", resumes and completes without showing
        // anything (BubbleCountService.cs:210-217).
        var path = _pool.Draw();
        if (path is null)
        {
            return null;
        }

        return new BubbleCountFiring(new BubbleCountEvent(0, default, _preset.Current.Difficulty), path);
    }

    /// <inheritdoc/>
    protected override BubbleCountFiring Stamp(BubbleCountFiring firing, int ordinal, DateTimeOffset at) =>
        firing with { Event = firing.Event with { Ordinal = ordinal, At = at } };

    /// <summary>
    /// Start the clip with its bubbles on it, then raise <see cref="Started"/> — that order, the
    /// same rule every other module follows: the user's outcome must not be hostage to whatever a UI
    /// subscriber does.
    /// </summary>
    protected override void Deliver(BubbleCountFiring firing)
    {
        var run = new BubbleCountRun(firing.Event.Difficulty, _random);
        lock (Gate)
        {
            _run = run;
            _answer = null;
            _lastResolution = BubbleCountResolution.None;
        }

        var outcome = _surface.Begin(firing.Path, TimeSpan.Zero, OnClipEnded, run);
        lock (Gate)
        {
            _lastPlayback = outcome;
            _playing = outcome is CapabilityState.Available;
        }

        if (outcome is not CapabilityState.Available)
        {
            // The presenter already took its own surface back down on every refusing path. Nothing
            // was watched, so nothing is asked — and the game is ABANDONED rather than failed, which
            // is upstream's own distinction (:224-233).
            Resolve(run, BubbleCountResolution.Abandoned);
            RaiseChanged();
            return;
        }

        // Upstream's safety timer, ported where upstream puts it: in the GAME, armed once the clip's
        // own length is known (Windows/BubbleCountWindow.xaml.cs:611, :1179-1195 — "Safety timeout -
        // forcing video end"). The length is the operating system's, read by the capability at open
        // and handed to the painter, so this is the clip's real length rather than a guess.
        ArmSafety(run.Duration + BubbleCountArithmetic.SafetyMargin);
        Started?.Invoke(firing.Event);
    }

    /// <summary>
    /// Take everything this module has on screen back down. Called from
    /// <see cref="OwnedSessionEffect.Disarm"/> and nowhere else, which is where WPF force-closes both
    /// of this game's windows on a stop (<c>MainWindow/MainWindow.StartStop.cs:340</c>,
    /// <c>:368-369</c>).
    ///
    /// <para><b>Every call here is GUARDED, and that is this packet's central finding in code.</b>
    /// Both capabilities are SINGLE-TENANT: neither <see cref="IVideoSurface"/> nor
    /// <see cref="IInputPresence"/> knows whose clip or whose card is up, so an unguarded
    /// <c>End()</c> here would tear down Mandatory Video's clip and an unguarded <c>Dismiss()</c>
    /// would take down a Lock Card. The first consumer of each could not have known — with one
    /// consumer there is nothing else to hit.</para>
    /// </summary>
    protected override void OnDisarmed()
    {
        CancelSafety();

        BubbleCountRun? run;
        BubbleCountAnswer? answer;
        bool playing;
        lock (Gate)
        {
            run = _run;
            answer = _answer;
            playing = _playing;
            _playing = false;
        }

        if (playing)
        {
            _surface.End();
        }

        if (answer is not null)
        {
            _presence.Dismiss();
        }

        if (run is not null)
        {
            Resolve(run, BubbleCountResolution.Withdrawn);
        }
    }

    /// <summary>
    /// Narrow the arm result to what this row can honestly claim. It has TWO channels and either can
    /// be missing, so it has two Unavailable outcomes with two different codes — and where both are
    /// gone, BOTH travel, the rule set after an earlier module shipped its opposite once.
    /// </summary>
    protected override CapabilityState Ready(CapabilityState scheduled)
    {
        if (scheduled is not CapabilityState.Available)
        {
            return scheduled;
        }

        var noDisplay = !_surface.CanReachADisplay;
        var noUser = !_presence.CanReachAUser;
        if (noDisplay || noUser)
        {
            var detail = noDisplay && noUser
                ? $"nothing it plays can reach a display ({DescribeSurface()}) AND nothing it asks can "
                    + $"reach a user ({DescribeUser()})"
                : noDisplay
                    ? $"nothing it plays can reach a display: {DescribeSurface()}"
                    : $"nothing it asks can reach a user: {DescribeUser()}";

            // The VIDEO code when the picture channel is the one that is gone (including when both
            // are), because the game cannot even begin without it; the INPUT code when the picture
            // would arrive and only the question could not be asked.
            return new CapabilityState.Unavailable(new CapabilityReason(
                noDisplay ? EffectReasonCodes.VideoSurfaceUnavailable : EffectReasonCodes.InputCaptureUnavailable,
                $"the '{Id}' module is armed and cannot run a game: {detail}"));
        }

        if (_pool.ActiveCount == 0)
        {
            // A pool is CONTENT, not a channel — the Subliminals answer: Degraded here and Live in
            // the dot, because dropping a clip in mid-session is picked up at the next game with no
            // re-arm.
            return new CapabilityState.Degraded(
                "the schedule is armed on a display and a desktop the operating system confirms, and every "
                + "game that comes due will have nothing to play",
                new CapabilityReason(
                    EffectReasonCodes.VideoNoClip,
                    $"there is no video in {_pool.Folder}, so no game will be played and nothing counted. "
                    + "Dropping an .mp4/.mov/.avi/.wmv/.mkv/.webm in there (or in a subfolder) is picked up at "
                    + "the next game, without restarting the session"));
        }

        return scheduled;
    }

    /// <summary>True while a clip THIS module started is up: its own intent AND the capability's own
    /// live answer.</summary>
    private bool ClipIsUp
    {
        get
        {
            lock (Gate)
            {
                return _playing && _surface.Showing;
            }
        }
    }

    /// <summary>True while a question THIS module asked is up, on the same two-part rule.</summary>
    private bool CardIsUp
    {
        get
        {
            lock (Gate)
            {
                return _answer is not null && _presence.IsPrompting;
            }
        }
    }

    /// <summary>
    /// The clip finished, was capped, or the surface stopped holding it. Arrives on the surface
    /// thread, from inside the presenter.
    /// </summary>
    private void OnClipEnded()
    {
        CancelSafety();
        EndOfClip();
    }

    /// <summary>
    /// The safety end: the clip outlived its own reported length by five seconds and nothing has
    /// ended it. Upstream forces the video end and shows the question anyway
    /// (<c>Windows/BubbleCountWindow.xaml.cs:1185-1192</c>), and so does this — the bubbles that were
    /// drawn were really drawn, and the count is about them.
    /// </summary>
    private void OnSafetyEnd()
    {
        bool playing;
        lock (Gate)
        {
            playing = _playing;
        }

        if (!playing)
        {
            return;
        }

        _surface.End();
        EndOfClip();
    }

    /// <summary>
    /// The clip is over, whichever way. Decide whether there is a question to ask at all, then ask
    /// it or abandon the game.
    /// </summary>
    private void EndOfClip()
    {
        BubbleCountRun? run;
        lock (Gate)
        {
            if (!_playing || _run is null)
            {
                return;
            }

            _playing = false;
            run = _run;
        }

        // WHY THIS READS THE SURFACE'S LAST STATE RATHER THAN AN ARGUMENT: the end callback carries
        // nothing, so "the clip finished" and "the surface stopped holding the picture" arrive
        // identically. The capability's own last typed outcome is what tells them apart, and it is
        // safe to read here because every Begin, every frame and this callback all run on the one
        // surface thread. Reported as a finding rather than worked around.
        if (_surface.LastPlacement is not CapabilityState.Available)
        {
            Resolve(run, BubbleCountResolution.Abandoned);
            ReSchedule();
            return;
        }

        if (run.BubblesShown == 0)
        {
            // The clip ran and not one bubble was ever drawn into a picture. Asking "how many
            // bubbles?" about a clip that carried none is a question with a cruel answer, and
            // upstream cannot produce this state at all: its bubbles ride a wall clock rather than
            // the pictures.
            Resolve(run, BubbleCountResolution.Abandoned);
            ReSchedule();
            return;
        }

        Ask(run);
    }

    /// <summary>Put the question up on the shared input capability.</summary>
    private void Ask(BubbleCountRun run)
    {
        // Somebody else's card is up. Prompting over it would silently replace its content and its
        // keystroke callback inside the shared presence, stranding that module's card for the rest
        // of the session — so this game stands down instead. The other direction of the same race is
        // the Lock Card's to close and is reported rather than edited into a landed module.
        if (_presence.IsPrompting)
        {
            Resolve(run, BubbleCountResolution.Abandoned);
            ReSchedule();
            return;
        }

        var answer = new BubbleCountAnswer(run.BubblesShown);
        lock (Gate)
        {
            _answer = answer;
            _lastAskedAbout = run.BubblesShown;
        }

        var outcome = _presence.Prompt(new InputPromptRequest(
            _placement(),
            ContentFor(answer),
            keystroke => OnKeystroke(answer, keystroke)));

        lock (Gate)
        {
            _lastPrompt = outcome;
        }

        if (outcome is not CapabilityState.Available)
        {
            // The Lock Card's load-bearing dismiss: an Unavailable outcome has already hidden the window
            // inside the presence, but a DEGRADED one has not — the OS gave the card the keyboard
            // and only the ink read-back said no, so without this the card stays up, blank, holding
            // the user's input, with its keystroke identity already cleared.
            _presence.Dismiss();
            Resolve(run, BubbleCountResolution.Refused);
            ReSchedule();
            return;
        }

        Asked?.Invoke(LastFiring?.Event ?? default);
        RaiseChanged();
    }

    /// <summary>
    /// One delivered keystroke. Runs on the thread whose message loop delivered it — the UI thread in
    /// the product — inside the presence's own catch.
    /// </summary>
    private void OnKeystroke(BubbleCountAnswer answer, InputKeystroke keystroke)
    {
        BubbleCountRun? run;
        lock (Gate)
        {
            // A keystroke for a question this module has already finished with. It happens: the OS
            // delivers what was already in the queue.
            if (!ReferenceEquals(_answer, answer))
            {
                return;
            }

            run = _run;
        }

        var step = answer.Apply(
            keystroke.Character,
            keystroke.Kind == InputKeystrokeKind.Character,
            keystroke.Kind == InputKeystrokeKind.Backspace,
            keystroke.Kind == InputKeystrokeKind.Cancel,
            keystroke.VirtualKey);

        switch (step)
        {
            case BubbleCountStep.Correct:
                Finish(run, answer, BubbleCountResolution.Counted);
                return;

            case BubbleCountStep.Exhausted:
                Finish(run, answer, BubbleCountResolution.Missed);
                return;

            case BubbleCountStep.GaveUp:
                Finish(run, answer, BubbleCountResolution.Dismissed);
                return;

            case BubbleCountStep.Ignored:
                return;

            default:
                _presence.Update(ContentFor(answer));
                return;
        }
    }

    private void Finish(BubbleCountRun? run, BubbleCountAnswer answer, BubbleCountResolution resolution)
    {
        _presence.Dismiss();
        lock (Gate)
        {
            if (!ReferenceEquals(_answer, answer))
            {
                return;
            }
        }

        if (run is not null)
        {
            Resolve(run, resolution);
        }

        ReSchedule();
    }

    private void Resolve(BubbleCountRun run, BubbleCountResolution resolution)
    {
        lock (Gate)
        {
            if (!ReferenceEquals(_run, run))
            {
                return;
            }

            _run = null;
            _answer = null;
            _playing = false;
            _lastResolution = resolution;
            switch (resolution)
            {
                case BubbleCountResolution.Counted:
                    _countedCount++;
                    break;
                case BubbleCountResolution.Missed:
                    _missedCount++;
                    break;
                case BubbleCountResolution.Abandoned:
                    _abandonedCount++;
                    break;
            }
        }

        Resolved?.Invoke(resolution);
    }

    /// <summary>
    /// Re-pace from the END of a game rather than from its start, which is upstream's own order: its
    /// scheduler re-arms in the tick that TRIGGERED the game and then the game runs for the length of
    /// a clip (<c>Services/BubbleCountService.cs:105-111</c>), so a game that outlives its own
    /// interval must not be followed immediately by the next.
    /// </summary>
    private void ReSchedule()
    {
        RefreshSchedule();
        RaiseChanged();
    }

    private void ArmSafety(TimeSpan due)
    {
        CancelSafety();
        var timer = Clock.Schedule(due, () => Signal.Post(OnSafetyEnd));
        var previous = Interlocked.Exchange(ref _safety, timer);
        previous?.Dispose();
    }

    private void CancelSafety() => Interlocked.Exchange(ref _safety, null)?.Dispose();

    private string DescribeSurface() => _surface.LastPlacement switch
    {
        CapabilityState.Unavailable u => u.Reason.Detail,
        CapabilityState.DependencyMissing m => m.Reason.Detail,
        CapabilityState.Faulted f => f.Reason.Detail,
        CapabilityState.PermissionRequired p => p.Reason.Detail,
        _ => "the video capability reports no display, no compositor or no usable media stack for this process",
    };

    private string DescribeUser() => _presence switch
    {
        UnsupportedInputPresence unsupported => unsupported.Reason.Detail,
        _ => "the OS reports this process cannot put a window in front of a user "
            + $"({DescribeStation(_presence.ObserveStation())})",
    };

    private static string DescribeStation(InputStationObservation station) =>
        $"asked={station.Asked}, window-station-visible={station.WindowStationVisible}, "
        + $"displays={station.DisplayCount}, desktop-reachable={station.DesktopReachable}";

    /// <summary>
    /// What the question card shows. FOUR slots, because that is what
    /// <see cref="InputPromptContent"/> has — see <see cref="BubbleCountAnswer.Progress"/> for the
    /// fold that puts upstream's two separate lines into one of them rather than growing the
    /// capability's content record for its second consumer.
    /// </summary>
    private static InputPromptContent ContentFor(BubbleCountAnswer answer) =>
        new(BubbleCountAnswer.Question, answer.Progress, answer.Typed, GiveUpHint);

    /// <summary>
    /// Where the question card goes: centred on the primary display, through the placement helper
    /// three modules now share (<see cref="PrimaryDisplayPlacement"/>). The minimum-rectangle answer
    /// to "no display at all" is the Lock Card's, for the Lock Card's reason: Compose refuses first
    /// on the station read, and a zero-size request would throw at the boundary rather than refuse.
    /// </summary>
    private static InputBounds DefaultPlacement()
    {
        if (PrimaryDisplayPlacement.PrimaryBounds() is not { } bounds)
        {
            return new InputBounds(0, 0, 1, 1);
        }

        var (x, y, width, height) = PrimaryDisplayPlacement.Centred(
            bounds, CardWidthFraction, CardHeightFraction);
        return new InputBounds(x, y, width, height);
    }
}
