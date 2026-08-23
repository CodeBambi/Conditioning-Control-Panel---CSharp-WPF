using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// One card that came due: which number it was, when, how many repeats it wanted and whether it was
/// strict.
///
/// <para><b>The phrase is not here</b>, and that is the rule <see cref="FlashEvent"/>,
/// <see cref="SubliminalEvent"/> and <see cref="AudioCueEvent"/> already hold: a subscriber, a log
/// line and a bug report get a COUNT, never the content. It travels on
/// <see cref="LockCardFiring"/>, which only the card window ever sees. <b>This is a divergence:</b>
/// upstream logs the phrase in plain text on every card
/// (<c>Services/LockCard/LockCardService.cs:303</c>).</para>
/// </summary>
public sealed record LockCardEvent(int Ordinal, DateTimeOffset At, int Repeats, bool Strict, int PhraseLength);

/// <summary>One card's two halves: the content-free record subscribers get, and the phrase only the
/// window is handed.</summary>
public sealed record LockCardFiring(LockCardEvent Event, string Phrase);

/// <summary>How a card ended.</summary>
public enum LockCardResolution
{
    /// <summary>Still up, or none has been shown.</summary>
    None,

    /// <summary>Every repeat was typed. THE answer.</summary>
    Solved,

    /// <summary>The user pressed Escape and the card honoured it. NO answer, deliberately given.</summary>
    Dismissed,

    /// <summary>The session stopped, or the module was switched off, with the card still up. No
    /// answer, and the user never chose to give one.</summary>
    Withdrawn,

    /// <summary>The operating system would not give the card the input, so it was taken straight
    /// back down rather than left on screen unanswerable.</summary>
    Refused,
}

/// <summary>
/// <b>Lock Card</b> — WPF's GAMES &amp; CARDS rack row
/// (<c>Views/Tabs/StudioTabView.xaml.cs:503-504</c>), started by <c>StartEngine</c> at
/// <c>MainWindow/MainWindow.StartStop.cs:206-209</c> and stopped by <c>StopEngineCore</c> at
/// <c>:339</c> with its visible card dropped at <c>:367</c>.
///
/// <para><b>Why this module exists in the port, and what makes it different from the seven before
/// it.</b> Every landed module is PASSIVE: it draws, tints, paces, ramps or plays, and none of them
/// ever asks the user for anything. This is the first that does. It puts a window in front of the
/// user, TAKES the keyboard, and waits — and the waiting is the whole point, which is why the
/// capability underneath it earns its <c>Available</c> from the operating system's own foreground
/// and focus rather than from a handler having been attached
/// (<see cref="IInputPresence"/>).</para>
///
/// <para><b>THE DOT, and the sixth thing it has meant.</b>
/// <see cref="OwnedSessionEffect.WorkIsRunning"/> here has three clauses and none is redundant:</para>
/// <code>
/// Live  =  a firing is on the clock
///       &amp;&amp;  the OS says this process can put a window in front of a user
///       &amp;&amp;  ( nothing is being asked  OR  the OS says the card holds foreground + focus )
/// </code>
/// <para>The dot has meant the CLOCK (paced), the SCREEN (continuous), CHANGE (moving), CUSTODY
/// (non-drawing) and REACH (audio — an output channel this process opens and holds). This one is
/// <b>DEMAND: a claim on the user's ATTENTION, which the operating system grants and can revoke
/// without this process doing anything at all.</b> Reach is the closest of the five and it is still
/// a different fact: an audio device, once opened, is held; the foreground is LENT, contested by
/// every other process on the desktop, and taken away by a click on another window.</para>
/// <para><b>And the question the packet asks directly: is this module <c>Live</c> while the prompt
/// is up and unanswered? YES.</b> Waiting for a human IS the work — a module that went <c>Armed</c>
/// the moment it did its job would be the under-claim refused for the audio half-row. But it
/// is <c>Live</c> only because the OS says the question is really in front of them: measured, a card
/// can be visible, topmost and hit-testable while the keyboard belongs to another application
/// entirely (the packet plan.md §0), and a dot lit there claims a question nobody is being asked.</para>
///
/// <para><b>What is NOT ported</b>, and is not pretended: the fullscreen cover on EVERY monitor with
/// its primary/mirror input sync (<c>Windows/LockCardWindow.xaml.cs:1496-1607</c>) — the port shows
/// one card on the primary display; voice-solve mode (<c>:423</c>, an offline Vosk microphone the
/// port has no capability for); the interaction queue that defers a card behind a video or a pop
/// quiz (<c>LockCardService.cs:193-263</c>) — the port has no queue, so upstream's own
/// <c>DropNoQueue</c> answer applies verbatim; the solved-card XP grant
/// (<c>Windows/LockCardWindow.xaml.cs:862</c> — the port has an XP ledger now,
/// <c>Features/Progression/ProgressionLedger</c>, but this effect has no solve payout to hand it)
/// and its achievements; the keep-alive window pool and the dead-man's-switch watchdog
/// (<c>:53-119</c>), both of which exist for WPF render-thread hazards this port's raw window does
/// not have; and the five colour dials. Each is recorded as a divergence rather than stubbed.</para>
///
/// <para><b>The level-35 unlock refuses nobody, here or upstream.</b> No statement of it survives in
/// the shipping tree at all — it is carried as data on <c>Features/Progression/LevelUnlocks</c> from
/// this port's own record — and every level gate the app still has funnels through
/// <c>AppSettings.IsLevelUnlocked</c>, whose body is <c>return true;</c>
/// (<c>Models/AppSettings.cs:5434-5442</c>: <i>"Feature level gating has been removed"</i>).</para>
/// </summary>
public sealed class LockCardEffect : PacedSessionEffect<LockCardFiring>
{
    /// <summary>WPF's rack key for this module (<c>StudioTabView.xaml.cs:503</c>).</summary>
    public const string EffectId = "lockcard";

    /// <summary>The row's label as the shipping app shows it, minus the emoji the port's rack does
    /// not render (<c>StudioTabView.xaml.cs:503</c> — "📐 Lock Card").</summary>
    public const string DisplayTitle = "Lock Card";

    /// <summary>How much of the primary display the card covers. <b>A divergence, and a deliberate
    /// one:</b> upstream covers every monitor entirely
    /// (<c>Windows/LockCardWindow.xaml.cs:1556-1584</c>). See the class remarks and D-record.</summary>
    public const double CardWidthFraction = 0.55;

    /// <summary>See <see cref="CardWidthFraction"/>.</summary>
    public const double CardHeightFraction = 0.38;

    private readonly PersistenceStore<LockCardPresetDocument> _preset;
    private readonly ILockCardPhrasePool _pool;
    private readonly IInputPresence _presence;
    private readonly Random _random;
    private readonly Func<InputBounds> _placement;

    private LockCardAttempt? _attempt;
    private CapabilityState? _lastPrompt;
    private LockCardResolution _lastResolution = LockCardResolution.None;
    private int _solvedCount;
    private int _dismissedCount;

    /// <summary>How many times this arm has put a firing on the clock. See
    /// <see cref="NextInterval"/>: the first-card offset belongs to the first schedule of a RUN, and
    /// getting that wrong is a hot loop rather than a nuance. Interlocked because
    /// <see cref="NextInterval"/> runs off the gate, on the clock's thread, in the tail of a
    /// firing.</summary>
    private int _schedulesThisArm;

    public LockCardEffect(
        AsyncOperationOwner owner,
        EffectSignal signal,
        ISessionClock clock,
        ILockCardPhrasePool pool,
        IInputPresence presence,
        PersistenceStore<LockCardPresetDocument> preset,
        Random? random = null,
        Func<InputBounds>? placement = null)
        : base(owner, signal, clock, "lockcard-schedule")
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(preset);
        _pool = pool;
        _presence = presence;
        _preset = preset;
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
    public LockCardPresetDocument Preset => _preset.Current;

    /// <summary>The input capability this module asks through. Public so a panel can render the OS's
    /// own answer rather than a sentence about a platform — the same reason every drawing module
    /// exposes its surface.</summary>
    public IInputPresence Presence => _presence;

    /// <summary>How many phrases are in play. The panel shows it so an empty pool has an answer.</summary>
    public int PhraseCount => _pool.ActiveCount;

    /// <summary>
    /// Cards this module ASKED FOR — firings that got as far as drawing a phrase and calling the
    /// capability.
    ///
    /// <para><b>Precisely, because a review found the previous wording overclaiming.</b> A firing
    /// that found no phrase, that found a card already up, or that found no desktop to ask on is NOT
    /// counted: <see cref="Compose"/> returns null and the base counts nothing, which is upstream's
    /// own behaviour in the same situations (<c>LockCardService.cs:275-280</c>, <c>:193-199</c>). A
    /// firing whose card the capability then REFUSED, or accepted and could not ink, IS counted —
    /// the module asked, and the answer came back from the operating system afterwards. What the
    /// user actually did with the cards that stayed up is <see cref="SolvedCount"/>,
    /// <see cref="DismissedCount"/> and <see cref="LastResolution"/>, which is where that question
    /// belongs.</para>
    /// </summary>
    public int CardCount => FireCount;

    /// <summary>The most recent card, or null if none has happened yet.</summary>
    public LockCardEvent? Last => LastFiring?.Event;

    /// <summary>Cards the user typed out in full.</summary>
    public int SolvedCount
    {
        get { lock (Gate) { return _solvedCount; } }
    }

    /// <summary>Cards the user pressed Escape on.</summary>
    public int DismissedCount
    {
        get { lock (Gate) { return _dismissedCount; } }
    }

    /// <summary>How the last card ended. <see cref="LockCardResolution.None"/> while one is up.</summary>
    public LockCardResolution LastResolution
    {
        get { lock (Gate) { return _lastResolution; } }
    }

    /// <summary>
    /// What the input capability said about the last card this module put up, verbatim — including
    /// the <c>Degraded</c> case where the OS gave the card the keyboard and holds no ink for it. Null
    /// before anything was asked. The panel renders this, so what the user reads about the card is
    /// the capability's own typed outcome rather than a fixed sentence.
    /// </summary>
    public CapabilityState? LastPrompt
    {
        get { lock (Gate) { return _lastPrompt; } }
    }

    /// <summary>The card that is up right now, or null. Public so a panel and a fact can read the
    /// live progress without the module having to project it.</summary>
    public LockCardAttempt? Attempt
    {
        get { lock (Gate) { return _attempt; } }
    }

    /// <summary>Raised on the UI thread, inside the dispatch boundary, once per card that really went
    /// up.</summary>
    public event Action<LockCardEvent>? Shown;

    /// <summary>Raised on the keystroke's own thread when a card ends, however it ended. <b>Both
    /// answers and non-answers come through here</b>, which is the point: a module that only signalled
    /// success would leave a caller unable to tell "typed it out" from "walked away".</summary>
    public event Action<LockCardResolution>? Resolved;

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

    /// <summary>The frequency dial. Writes and re-evaluates, the port's convention for every module's
    /// dial — and it matters here because upstream's own frequency slider takes effect
    /// at the next tick rather than after the old interval expires.</summary>
    public void SetPerHour(int perHour)
    {
        var clamped = Math.Clamp(perHour, LockCardSchedule.MinPerHour, LockCardSchedule.MaxPerHour);
        if (_preset.Current.PerHour == clamped)
        {
            return;
        }

        _preset.Mutate(p => p.PerHour = perHour);
        Refresh();
    }

    /// <summary>The repeats dial. It changes the NEXT card, never the one on screen: upstream reads
    /// it once, at show time (<c>LockCardService.cs:294</c>), and a card whose target moved under the
    /// user mid-typing would be the cruellest possible live-apply.</summary>
    public void SetRepeats(int repeats)
    {
        var clamped = Math.Clamp(repeats, LockCardSchedule.MinRepeats, LockCardSchedule.MaxRepeats);
        if (_preset.Current.Repeats == clamped)
        {
            return;
        }

        _preset.Mutate(p => p.Repeats = repeats);
        RaiseChanged();
    }

    /// <summary>The strict dial. Same rule as the repeats one: read at show time
    /// (<c>LockCardService.cs:295</c>), so it changes the next card.</summary>
    public void SetStrict(bool strict)
    {
        if (_preset.Current.Strict == strict)
        {
            return;
        }

        _preset.Mutate(p => p.Strict = strict);
        RaiseChanged();
    }

    /// <summary>
    /// <b>The dot's sixth meaning, in one expression.</b> See the class remarks for what each clause
    /// is for and why none of them is redundant. It NARROWS
    /// <see cref="PacedSessionEffect{TFiring}.WorkIsRunning"/> — <see cref="ScheduleArmed"/> is kept
    /// as a conjunct, which is the one-directional contract that member's own remarks set.
    /// </summary>
    protected override bool WorkIsRunning =>
        ScheduleArmed
        && _presence.CanReachAUser
        && (!_presence.IsPrompting || _presence.HoldsTheInput);

    /// <summary>
    /// Upstream's spacing (<c>LockCardService.cs:127-134</c>), with its first-card offset
    /// (<c>:159-168</c>) applied to the FIRST schedule of an arm — see <see cref="LockCardSchedule"/>
    /// for why the first one is not spaced like the others.
    ///
    /// <para><b>"First schedule of this arm", and NOT "nothing has fired yet".</b> Those two differ
    /// exactly when a firing came due and produced nothing — an empty phrase pool, a card already up,
    /// a desktop that cannot take the input — and the difference is a hot loop rather than a nuance:
    /// the offset is <c>roll * interval</c>, which can legitimately be ZERO, so a module that
    /// re-armed at the offset after every unproductive firing would re-fire immediately, forever.
    /// Found by writing the empty-pool fact, which hung. Upstream cannot have the bug because its
    /// offset is computed once in <c>Start</c> (<c>LockCardService.cs:74</c>) while every
    /// <c>Timer_Tick</c> recomputes the ±30 % interval (<c>:127-134</c>) whether or not the tick
    /// showed anything — and this counter is how that distinction is expressed against a base class
    /// that re-arms in the tail of every firing.</para>
    /// </summary>
    protected override TimeSpan NextInterval()
    {
        var perHour = _preset.Current.PerHour;
        return Interlocked.Increment(ref _schedulesThisArm) == 1
            ? TimeSpan.FromMinutes(LockCardSchedule.FirstCardDelayMinutes(
                perHour, windowMinutes: null, _random.NextDouble()))
            : LockCardSchedule.Interval(perHour, _random.NextDouble());
    }

    /// <summary>
    /// One card comes due. Upstream's show path, in upstream's order and with upstream's answers to
    /// each refusal.
    /// </summary>
    protected override LockCardFiring? Compose()
    {
        // (1) Nothing can be asked at all — no interactive station, or a platform whose capture this
        // build cannot earn. The audio pair's "endpoint down: stay quiet, don't spin"
        // (MindWipeService.cs:771) in the input medium: not counted, not shown, and the schedule
        // keeps running so a session that regains a desktop is picked up at the next card.
        if (!_presence.CanReachAUser)
        {
            return null;
        }

        // (2) A card is already up. Upstream's ResolveBlockedCardAction with
        // (cardAlreadyOpen: true, isDeferredReplay: false, hasInteractionQueue: false) returns
        // DropNoQueue (:193-199) — drop it, log it, show nothing. That is exactly this port's
        // situation, permanently: there is no interaction queue to defer to (D-record).
        if (_presence.IsPrompting)
        {
            return null;
        }

        // (3) No phrase is enabled: upstream returns without showing a card (:275-280).
        var phrase = _pool.Draw();
        if (phrase is null)
        {
            return null;
        }

        var preset = _preset.Current;
        return new LockCardFiring(
            new LockCardEvent(0, default, preset.Repeats, preset.Strict, phrase.Length),
            phrase);
    }

    /// <inheritdoc/>
    protected override LockCardFiring Stamp(LockCardFiring firing, int ordinal, DateTimeOffset at) =>
        firing with { Event = firing.Event with { Ordinal = ordinal, At = at } };

    /// <summary>
    /// Put the card in front of the user, then raise <see cref="Shown"/> — that order, for the same
    /// reason every other module places its output before it notifies: the user's outcome must not be
    /// hostage to whatever a UI subscriber does.
    ///
    /// <para><b>A refusal takes the card straight back down and says so.</b> Upstream's own error
    /// path does the same thing for the same reason (<c>LockCardService.cs:305-313</c>): a card that
    /// is up and cannot be answered is worse for the user than no card at all. That includes
    /// <c>Degraded</c> — a focused, blank window is a question nobody can read.</para>
    /// </summary>
    protected override void Deliver(LockCardFiring firing)
    {
        var attempt = new LockCardAttempt(firing.Phrase, firing.Event.Repeats, firing.Event.Strict);
        lock (Gate)
        {
            _attempt = attempt;
            _lastResolution = LockCardResolution.None;
        }

        var outcome = _presence.Prompt(new InputPromptRequest(
            _placement(),
            ContentFor(attempt),
            keystroke => OnKeystroke(attempt, keystroke)));

        lock (Gate)
        {
            _lastPrompt = outcome;
        }

        if (outcome is not CapabilityState.Available)
        {
            // THE DISMISS IS LOAD-BEARING AND IT IS NOT REDUNDANT. An Unavailable outcome has
            // already hidden the window inside the presence (Win32InputPresence.RefuseAndWithdraw),
            // but a DEGRADED one has not: the OS confirmed the card holds the foreground and the
            // keyboard focus, and only the ink read-back said no. Without this call that card stays
            // on screen — topmost, blank, holding the user's keyboard — while Resolve below clears
            // _attempt, which makes OnKeystroke discard EVERY key including Escape, and
            // Compose's already-prompting guard then drops every future card for the rest of the
            // session. Upstream force-closes on its own error path for exactly this reason
            // (Services/LockCard/LockCardService.cs:305-313).
            _presence.Dismiss();
            Resolve(attempt, LockCardResolution.Refused);
            return;
        }

        Shown?.Invoke(firing.Event);
    }

    /// <summary>
    /// One delivered keystroke. Runs on the thread whose message loop delivered it — the UI thread in
    /// the product — and inside the presence's own catch, so nothing here may assume it can throw
    /// usefully.
    /// </summary>
    private void OnKeystroke(LockCardAttempt attempt, InputKeystroke keystroke)
    {
        lock (Gate)
        {
            // A keystroke for a card this module has already finished with. It can happen: the OS
            // delivers what was already in the queue.
            if (!ReferenceEquals(_attempt, attempt))
            {
                return;
            }
        }

        var step = attempt.Apply(
            keystroke.Character,
            keystroke.Kind == InputKeystrokeKind.Character,
            keystroke.Kind == InputKeystrokeKind.Backspace,
            keystroke.Kind == InputKeystrokeKind.Cancel);

        switch (step)
        {
            case LockCardStep.Solved:
                _presence.Dismiss();
                Resolve(attempt, LockCardResolution.Solved);
                return;

            case LockCardStep.Cancelled:
                _presence.Dismiss();
                Resolve(attempt, LockCardResolution.Dismissed);
                return;

            case LockCardStep.Ignored:
            case LockCardStep.CancelRefused:
                return;

            default:
                _presence.Update(ContentFor(attempt));
                return;
        }
    }

    private void Resolve(LockCardAttempt attempt, LockCardResolution resolution)
    {
        lock (Gate)
        {
            if (!ReferenceEquals(_attempt, attempt))
            {
                return;
            }

            _attempt = null;
            _lastResolution = resolution;
            if (resolution == LockCardResolution.Solved)
            {
                _solvedCount++;
            }
            else if (resolution == LockCardResolution.Dismissed)
            {
                _dismissedCount++;
            }
        }

        Resolved?.Invoke(resolution);
    }

    /// <summary>
    /// Take any card back down. Called from <see cref="OwnedSessionEffect.Disarm"/> and nowhere else,
    /// which is where WPF drops its own visible card on a stop
    /// (<c>MainWindow/MainWindow.StartStop.cs:367</c>, <c>LockCardService.cs:107-112</c>) — and
    /// unconditionally, so a panic can always reach the card.
    /// </summary>
    protected override void OnDisarmed()
    {
        // The next arm is a new RUN and gets the first-card offset again — WPF recomputes it in
        // Start() every time (LockCardService.cs:74). Reset here rather than in Arm because Disarm
        // is the only place that runs exactly once per stop.
        Interlocked.Exchange(ref _schedulesThisArm, 0);

        var attempt = Attempt;
        _presence.Dismiss();
        if (attempt is not null)
        {
            Resolve(attempt, LockCardResolution.Withdrawn);
        }
    }

    /// <summary>
    /// Narrow the arm result to what this module can honestly claim, and the two narrowings are
    /// deliberately DIFFERENT states because a missing channel and missing content are not the same
    /// failure — the same split the audio modules made for a device against a clip folder.
    /// </summary>
    protected override CapabilityState Ready(CapabilityState scheduled)
    {
        if (scheduled is not CapabilityState.Available)
        {
            return scheduled;
        }

        if (!_presence.CanReachAUser)
        {
            // Nothing can ever be asked here. Unavailable, not Degraded: there is no surviving half.
            // The presence's own reason is carried through verbatim where it has one, so a Linux run
            // reads the manual gate and a session-0 run reads the station read-back — never a
            // sentence this module made up about a platform.
            var detail = _presence switch
            {
                UnsupportedInputPresence unsupported => unsupported.Reason.Detail,
                _ => $"the OS reports this process cannot put a window in front of a user "
                    + $"({DescribeStation(_presence.ObserveStation())})",
            };

            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.InputCaptureUnavailable,
                $"the '{Id}' module is armed and can never ask anybody anything: {detail}"));
        }

        if (_pool.ActiveCount == 0)
        {
            // A pool is CONTENT, not a channel: enabling a phrase mid-session starts producing cards
            // with no re-arm, so this takes the Subliminals answer — Degraded here, Live in the dot.
            return new CapabilityState.Degraded(
                "the schedule is armed on a desktop that can take the input, and every card that comes due "
                + "will have nothing to ask",
                new CapabilityReason(
                    EffectReasonCodes.InputNoPhrase,
                    $"no phrase is enabled in {LockCardPresetDocument.FileName}, so no card will be shown and "
                    + "none counted. Enabling one is picked up at the next card, without restarting the session"));
        }

        return scheduled;
    }

    private static string DescribeStation(InputStationObservation station) =>
        $"asked={station.Asked}, window-station-visible={station.WindowStationVisible}, "
        + $"displays={station.DisplayCount}, desktop-reachable={station.DesktopReachable}";

    private static InputPromptContent ContentFor(LockCardAttempt attempt) =>
        new(
            attempt.Phrase,
            attempt.Progress,
            attempt.Answer,
            // Live rather than snapshotted, exactly as upstream repaints its own exit hint
            // (LockCardWindow.xaml.cs:639-647), and here it is always the same line because the port
            // has no panic hook and the fall-open rule therefore always keeps Escape live.
            LockCardTyping.EscapeClosesCard(attempt.Strict, panicEscapeIsLive: false)
                ? "Press Esc to close"
                : "Only the panic key closes this card");

    /// <summary>
    /// Where the card goes when nobody says otherwise: centred on the primary display at a fraction
    /// of it. <b>The divergence from upstream's every-monitor fullscreen cover lives here</b>, in one
    /// function, so it is one place to change if the port ever grows the multi-monitor mirror sync.
    /// The display comes from the overlay capability's own enumeration, which is CONSUMED and never
    /// edited (<c>Overlay/OverlayDisplays.cs</c>).
    /// </summary>
    private static InputBounds DefaultPlacement()
    {
        // Extracted the display walk and the centring into PrimaryDisplayPlacement, which
        // three modules now share. What stays HERE is what is genuinely this module's: its own
        // fractions (D110) and its own answer to "no display at all" — a minimum legal rectangle
        // rather than the video surface's null, because Compose refuses first on the station read
        // and a zero-size request would throw at the boundary rather than refuse.
        if (PrimaryDisplayPlacement.PrimaryBounds() is not { } bounds)
        {
            return new InputBounds(0, 0, 1, 1);
        }

        var (x, y, width, height) = PrimaryDisplayPlacement.Centred(
            bounds, CardWidthFraction, CardHeightFraction);
        return new InputBounds(x, y, width, height);
    }
}
