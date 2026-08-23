using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features.Progression;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// One quiz that came due: which number it was, when, and the SHAPE of what was asked.
///
/// <para><b>Not one word of the question, the answers or the affirmation is here</b>, which is the
/// rule <see cref="FlashEvent"/>, <see cref="SubliminalEvent"/>, <see cref="LockCardEvent"/> and
/// <see cref="MandatoryVideoEvent"/> already hold: a subscriber, a diagnostic line and a bug report
/// get a COUNT, never the content. The text travels on <see cref="PopQuizFiring"/>, which only the
/// card ever sees. <b>This is a divergence:</b> upstream logs the question in plain text on every
/// quiz (<c>Services/Quiz/PopQuizService.cs:248</c>). It is second-person conditioning content, and
/// that it happens to be built in rather than typed by the user does not make it a thing to write
/// into a file somebody might attach to a bug report.</para>
/// </summary>
/// <param name="Ordinal">Which quiz this was, from 1.</param>
/// <param name="At">When it went up, on the session clock.</param>
/// <param name="QuestionLength">How long the question was — the <see cref="LockCardEvent"/>'s
/// <c>PhraseLength</c> precedent: a length, never a text and never an index into the pool.</param>
/// <param name="OptionCount">How many answers were offered.</param>
public sealed record PopQuizEvent(int Ordinal, DateTimeOffset At, int QuestionLength, int OptionCount);

/// <summary>One quiz's two halves: the content-free record subscribers get, and the question only
/// the card is handed.</summary>
public sealed record PopQuizFiring(PopQuizEvent Event, PopQuizAsk Ask);

/// <summary>How a quiz ended.</summary>
public enum PopQuizResolution
{
    /// <summary>Still up, or none has been shown.</summary>
    None,

    /// <summary>An answer was given. THE answer — every one of them is
    /// (<c>Services/Quiz/PopQuizService.cs:12</c>).</summary>
    Answered,

    /// <summary>The user pressed Escape. No answer, deliberately given
    /// (<c>Windows/PopQuizWindow.xaml.cs:128-134</c>).</summary>
    Skipped,

    /// <summary>The session stopped, or the module was switched off, with the question still up. No
    /// answer, and the user never chose to withhold one.</summary>
    Withdrawn,

    /// <summary>The operating system would not give the card the input, so it was taken straight
    /// back down rather than left on screen unanswerable.</summary>
    Refused,
}

/// <summary>
/// <b>Pop Quiz</b> — reinforcement questions during a session, WPF's <c>PopQuizService</c>
/// (<c>Services/Quiz/PopQuizService.cs</c>) and its card
/// (<c>Windows/PopQuizWindow.xaml</c> + <c>.xaml.cs</c>), 698 lines across the three.
///
/// <para><b>It is on the ORDINARY engine's rack, and that is why it could be built now.</b>
/// <c>StartEngine</c> starts it at <c>MainWindow/MainWindow.StartStop.cs:255-258</c> (and again,
/// redundantly, at <c>:261-264</c> — two adjacent blocks with the same body, harmless because
/// <c>Start</c> returns at its own <c>_isRunning</c> guard, <c>PopQuizService.cs:104</c>),
/// <c>StopEngineCore</c> stops it at <c>:344</c> and panic drops its window at <c>:373</c>. The
/// scripted engine starts it too (<c>Services/Session/SessionEngine.cs:490</c>,
/// <c>:1407-1414</c>) — but from the USER-level dials, not the session's, and upstream says so on
/// the line above: <i>"Pop quiz is a user-level toggle (AppSettings), not per-session"</i>
/// (<c>:1406</c>). So this module needs nothing from a scripted session and works with or without
/// one.</para>
///
/// <para><b>Every answer is correct.</b> Upstream's own header (<c>PopQuizService.cs:12</c>):
/// <i>"All answers are 'correct' — pure positive reinforcement."</i> There is no score, no streak,
/// no wrong answer and no failure state anywhere in the three upstream files, and none was invented
/// here. What the four choices decide is WHICH affirmation comes back, which is why the shuffle has
/// to keep the mapping straight (<see cref="PopQuizAsk"/>).</para>
///
/// <para><b>It asks through the SAME input capability the Lock Card and Bubble Count ask
/// through</b>, and that is not a convenience. Upstream's <c>ShowPopQuiz</c> holds TWO guards: no
/// second quiz over the first (<c>:183-187</c>) and no quiz over a lock card (<c>:194</c>, its own
/// #763 — both are ownerless topmost covers and stacking them puts one over the other). Here they
/// are ONE read of <see cref="IInputPresence.IsPrompting"/>, because the shared presence is the
/// thing that knows a card is up, and it refuses a second tenant itself
/// (<c>Input/Win32InputPresence.cs</c>'s atomic claim, <see cref="InputReasonCodes.InputAlreadyPrompting"/>).
/// Upstream defers instead of dropping, through an interaction queue this port does not have — and
/// its OWN answer for that case is to drop, in as many words (<c>:212-215</c>: <i>"A lock card is on
/// screen and no interaction queue is available to defer to. Dropping."</i>).</para>
///
/// <para><b>THE DOT.</b> Lock Card's three clauses, with Bubble Count's correction to the third:
/// the prompting read is qualified by <c>of MINE</c>, because the presence is shared and a bare
/// <c>IsPrompting</c> would darken this row for somebody else's card.</para>
///
/// <para><b>What is NOT ported, and each is refused rather than stubbed.</b></para>
/// <list type="bullet">
/// <item><b>The chime</b> (<c>Windows/PopQuizWindow.xaml.cs:192-211</c>): it plays one of
/// <c>Resources/sounds/chime{1,2,3}.mp3</c> — shipped application resources this port does not ship
/// — at <c>(float)Math.Pow(master * 0.5f, 1.5)</c> (<c>:203</c>) where <c>master</c> is the app-wide
/// <c>MasterVolume</c> dial <b>this port does not have at all</b>
/// (<see cref="BrainDrainEffect"/>'s own note, and <see cref="IntensityRampEffect"/>'s). Giving it a
/// user clip folder like the two audio modules have would invent a content dial and a volume dial
/// upstream has no counterpart for. The formula is written down here so the row that ships a sound
/// library does not have to re-derive it.</item>
/// <item><b>The mouse.</b> Upstream's four slots are clickable borders; here they are the keys
/// <c>1</c>-<c>4</c>. See <see cref="PopQuizAsk"/> for why going through the pointer capability
/// instead would have cost the mutual exclusion above.</item>
/// <item><b>The interaction queue</b> (<c>:196-228</c>) and its one-re-defer cap. Absent, as it is
/// for the Lock Card and Bubble Count, and upstream's own no-queue branch applies verbatim.</item>
/// <item><b>The avatar mute</b> (<c>PopQuizWindow.xaml.cs:44-48</c>, <c>:270-275</c>): it silences
/// the avatar window while a quiz is up so her z-order work cannot cover the card. There is no
/// avatar window in this build to mute.</item>
/// <item><b>The 200 ms keep-on-top timer and the deactivation grab-back</b> (<c>:62-113</c>): WPF
/// re-asserts <c>HWND_TOPMOST</c> five times a second and calls <c>SetForegroundWindow</c> whenever
/// focus is stolen. This port asks the OS once, believes the ANSWER, and takes the card back down
/// when the answer is no — the discipline <see cref="IInputPresence"/> exists to enforce.</item>
/// <item><b>The Test button</b> (<c>MainWindow/MainWindow.Lab.cs:646-649</c>) and the
/// <b>"quiz me" voice command</b> (<c>Services/AutonomyService.VoiceCommands.cs:431</c>): one needs
/// a panel and the other a speech capability. Upstream withholds the XP on the test path
/// (<c>PopQuizWindow.xaml.cs:157</c>); with no test path here there is nothing to withhold it
/// on.</item>
/// </list>
///
/// <para><b>The twenty-five XP is NOT refused, and that is a correction to this packet's own
/// premise.</b> The packet said nothing in this build awards XP. It does:
/// <see cref="ProgressionLedger"/> banks from three call sites today
/// (<c>Features/Arcademy/ArcademySession.cs:497</c>, <c>Features/Dtrh/DtrhMeta.cs:833</c>,
/// <c>Features/Intake/IntakeHostWindow.axaml.cs:547</c>). So upstream's own number
/// (<c>PopQuizWindow.xaml.cs:161</c>, <c>AddXP(25, XPSource.Other)</c>) goes to the real ledger
/// through an optional handle, exactly as those three take theirs — <b>no economy is invented, and
/// with no ledger nothing is banked and the card makes no XP claim.</b> This is the first RACK
/// module with a payout to hand over; the Lock Card and Bubble Count both recorded that they had
/// none.</para>
/// </summary>
public sealed class PopQuizEffect : PacedSessionEffect<PopQuizFiring>
{
    /// <summary>This module's key. Upstream has no Studio rack row for it — its dials live on the
    /// Graded Intake door (<c>Views/Tabs/GradedIntakeTabView.xaml:255-292</c>) — but it names itself
    /// this in the one place it is keyed, the season recap
    /// (<c>Models/SeasonRecap.cs:25</c>, <c>PopQuiz = "popquiz"</c>).</summary>
    public const string EffectId = "popquiz";

    /// <summary>The label the shipping app puts over the dials
    /// (<c>GradedIntakeTabView.xaml:256</c>, <c>label_pop_quiz</c>; <c>en.json:2059</c> =
    /// "Pop Quiz"), which is also its window title (<c>Windows/PopQuizWindow.xaml:5</c>).</summary>
    public const string DisplayTitle = "Pop Quiz";

    /// <summary>
    /// How much of the primary display the card covers. <b>A divergence, and the same one the Lock
    /// Card recorded:</b> upstream's window is a fixed 500x420 centred on screen
    /// (<c>Windows/PopQuizWindow.xaml:6-7</c>), and this port centres a FRACTION of the primary
    /// display instead, so the card scales with the screen rather than shrinking to a stamp on a 4K
    /// one. Taller than the Lock Card's because this card carries five lines rather than one.
    /// </summary>
    public const double CardWidthFraction = 0.45;

    /// <summary>See <see cref="CardWidthFraction"/>.</summary>
    public const double CardHeightFraction = 0.5;

    /// <summary>Upstream's payout (<c>Windows/PopQuizWindow.xaml.cs:161</c>,
    /// <c>App.Progression?.AddXP(25, Services.XPSource.Other)</c>), and the number its own card
    /// prints (<c>Windows/PopQuizWindow.xaml:124</c>, <c>label_25_xp</c>).</summary>
    public const double AnswerXp = 25;

    /// <summary>What the ledger records this grant as. Upstream's own <c>XPSource.Other</c>
    /// (<c>PopQuizWindow.xaml.cs:161</c>) — which matters beyond bookkeeping: upstream suppresses
    /// idle XP for <c>Flash</c>, <c>Subliminal</c> and <c>BouncingText</c> only
    /// (<c>Services/Progression/ProgressionService.cs:59</c>), so a quiz answered by a real person
    /// is never anti-cheat suppressed.</summary>
    public const string XpSource = "pop quiz";

    /// <summary>How long the answer stands before the affirmation replaces it — upstream's
    /// <c>await Task.Delay(300)</c> between highlighting the choice and swapping the panels
    /// (<c>Windows/PopQuizWindow.xaml.cs:170-173</c>).</summary>
    public static readonly TimeSpan AffirmationDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>How long the affirmation stays before the card closes itself — upstream's
    /// <c>await Task.Delay(1500)</c> and its own comment, "Auto-dismiss after 1.5s"
    /// (<c>Windows/PopQuizWindow.xaml.cs:175-177</c>).</summary>
    public static readonly TimeSpan AffirmationDwell = TimeSpan.FromMilliseconds(1500);

    private readonly PersistenceStore<PopQuizPresetDocument> _preset;
    private readonly IInputPresence _presence;
    private readonly ProgressionLedger? _xp;
    private readonly Random _random;
    private readonly Func<InputBounds> _placement;

    private PopQuizAsk? _ask;
    private CapabilityState? _lastPrompt;
    private XpGrant? _lastGrant;
    private PopQuizResolution _lastResolution = PopQuizResolution.None;
    private int _answeredCount;
    private int _skippedCount;
    private IDisposable? _followUp;

    /// <param name="owner">The module's operation owner: one generation per armed schedule.</param>
    /// <param name="signal">Where <c>Changed</c> and the UI projection are allowed to arrive.</param>
    /// <param name="clock">The injected clock the schedule and both card delays pace on.</param>
    /// <param name="presence">The ONE shared input capability. See the type remarks.</param>
    /// <param name="preset">This module's persisted dials.</param>
    /// <param name="xp">The XP ledger, or null. Optional for the reason the other three XP callers
    /// make it optional: a build or a host without one banks nothing rather than inventing a
    /// store.</param>
    /// <param name="random">Injectable for the reason every module's is: the SPACING, the DRAW and
    /// the SHUFFLE are what a fact makes deterministic, and the arithmetic they feed stays the
    /// module's own rather than a number a test double re-derives.</param>
    /// <param name="placement">Where the card goes; the primary display's centre by default.</param>
    public PopQuizEffect(
        AsyncOperationOwner owner,
        EffectSignal signal,
        ISessionClock clock,
        IInputPresence presence,
        PersistenceStore<PopQuizPresetDocument> preset,
        ProgressionLedger? xp = null,
        Random? random = null,
        Func<InputBounds>? placement = null)
        : base(owner, signal, clock, "popquiz-schedule")
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(preset);
        _presence = presence;
        _preset = preset;
        _xp = xp;
        _random = random ?? new Random();
        _placement = placement ?? DefaultPlacement;
    }

    /// <inheritdoc/>
    public override string Id => EffectId;

    /// <inheritdoc/>
    public override string Title => DisplayTitle;

    /// <inheritdoc/>
    public override bool Enabled => _preset.Current.Enabled;

    /// <summary>This module's persisted dials (public so a panel reads the real document).</summary>
    public PopQuizPresetDocument Preset => _preset.Current;

    /// <summary>The input capability this module asks through. Public so a panel can render the OS's
    /// own answer rather than a sentence about a platform.</summary>
    public IInputPresence Presence => _presence;

    /// <summary>How many questions are in play — always upstream's twenty-five, because the pool is
    /// built in (<see cref="PopQuizQuestions"/>).</summary>
    public int QuestionCount => PopQuizQuestions.Pool.Count;

    /// <summary>True when this module has an XP ledger to bank into. False is not a fault: it is the
    /// honest state of a host that opened none, and the card then makes no XP claim.</summary>
    public bool BanksXp => _xp is not null;

    /// <summary>
    /// Quizzes this module ASKED FOR — firings that got as far as drawing a question and calling the
    /// capability. A firing that found a card already up, or found no desktop to ask on, is NOT
    /// counted: <see cref="Compose"/> returns null and the base counts nothing, which is upstream's
    /// own behaviour in the same situations (<c>Services/Quiz/PopQuizService.cs:183-187</c>,
    /// <c>:194</c>). A firing the capability then REFUSED IS counted — the module asked, and the
    /// answer came back from the operating system afterwards.
    /// </summary>
    public int QuizCount => FireCount;

    /// <summary>The most recent quiz, or null if none has happened yet.</summary>
    public PopQuizEvent? Last => LastFiring?.Event;

    /// <summary>Questions the user answered.</summary>
    public int AnsweredCount
    {
        get { lock (Gate) { return _answeredCount; } }
    }

    /// <summary>Questions the user pressed Escape on.</summary>
    public int SkippedCount
    {
        get { lock (Gate) { return _skippedCount; } }
    }

    /// <summary>How the last quiz ended. <see cref="PopQuizResolution.None"/> while one is up.</summary>
    public PopQuizResolution LastResolution
    {
        get { lock (Gate) { return _lastResolution; } }
    }

    /// <summary>What the input capability said about the last card this module put up, verbatim —
    /// including the <c>Degraded</c> case. Null before anything was asked.</summary>
    public CapabilityState? LastPrompt
    {
        get { lock (Gate) { return _lastPrompt; } }
    }

    /// <summary>What the ledger said about the last answer's twenty-five, or null if nothing has
    /// been answered or there is no ledger. A typed OUTCOME, so a panel renders whether the XP
    /// really banked rather than assuming it did.</summary>
    public XpGrant? LastGrant
    {
        get { lock (Gate) { return _lastGrant; } }
    }

    /// <summary>The question that is up right now, or null. Public so a panel and a fact can read
    /// the live card without the module having to project it.</summary>
    public PopQuizAsk? Ask
    {
        get { lock (Gate) { return _ask; } }
    }

    /// <summary>Raised on the UI thread, inside the dispatch boundary, once per quiz that really
    /// went up.</summary>
    public event Action<PopQuizEvent>? Shown;

    /// <summary>Raised when a quiz ends, however it ended. <b>Both answers and non-answers come
    /// through here</b>: a module that only signalled an answer would leave a caller unable to tell
    /// "picked one" from "walked away".</summary>
    public event Action<PopQuizResolution>? Resolved;

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
    /// dial — and it is upstream's behaviour too: its tick recomputes the interval from the CURRENT
    /// setting every time (<c>Services/Quiz/PopQuizService.cs:163-171</c>).</summary>
    public void SetPerHour(int perHour)
    {
        var clamped = Math.Clamp(perHour, PopQuizSchedule.MinPerHour, PopQuizSchedule.MaxPerHour);
        if (_preset.Current.PerHour == clamped)
        {
            return;
        }

        _preset.Mutate(p => p.PerHour = perHour);
        Refresh();
    }

    /// <summary>
    /// <b>The dot.</b> Lock Card's three clauses — a firing is on the clock, the OS says this process
    /// can put a window in front of a user, and nothing of MINE is being asked unless the OS says the
    /// card really holds the input.
    ///
    /// <para><b>The <c>of MINE</c> qualifier is Bubble Count's correction and it is load-bearing
    /// here for the same reason.</b> The presence is shared with two other rows, so a bare
    /// <c>_presence.IsPrompting</c> would darken this row's dot while a Lock Card is up — a lie about
    /// a module that is idle and healthy.</para>
    /// </summary>
    protected override bool WorkIsRunning =>
        ScheduleArmed
        && _presence.CanReachAUser
        && (!CardIsUp || _presence.HoldsTheInput);

    /// <summary>
    /// Upstream's spacing (<c>Services/Quiz/PopQuizService.cs:163-171</c>). <b>No first-quiz
    /// offset</b>: unlike the Lock Card, this scheduler paces its very first question exactly like
    /// every other one (<c>:113-122</c>), so there is nothing here to count arms for.
    /// </summary>
    protected override TimeSpan NextInterval() =>
        PopQuizSchedule.Interval(_preset.Current.PerHour, _random.NextDouble());

    /// <summary>
    /// One quiz comes due. Upstream's guards, in upstream's order, with upstream's own answer to each
    /// refusal.
    /// </summary>
    protected override PopQuizFiring? Compose()
    {
        // (1) Nothing can be asked at all — no interactive station, or a platform whose input this
        // build cannot take. Upstream has no analogue because a WPF window on a WPF desktop is not in
        // doubt. Not counted, not shown, and the schedule keeps running so a session that regains a
        // desktop is picked up at the next question.
        if (!_presence.CanReachAUser)
        {
            return null;
        }

        // (2) A card is already up — this module's own (PopQuizService.cs:183-187, "A pop quiz is
        // already open. Skipping.") or the Lock Card's (:194, its #763 cross-check). ONE read,
        // because the presence is what knows. Upstream defers through an interaction queue; with no
        // queue its own branch drops (:212-215), which is exactly this port's situation permanently.
        if (_presence.IsPrompting)
        {
            return null;
        }

        // (3) The pool is built in and never empty, so unlike every phrase- or clip-backed module
        // there is no content refusal to make here (PopQuizService.cs:242 cannot come back empty).
        var ask = new PopQuizAsk(PopQuizQuestions.Draw(_random), _random);
        return new PopQuizFiring(
            new PopQuizEvent(0, default, ask.Question.Text.Length, ask.Options.Count),
            ask);
    }

    /// <inheritdoc/>
    protected override PopQuizFiring Stamp(PopQuizFiring firing, int ordinal, DateTimeOffset at) =>
        firing with { Event = firing.Event with { Ordinal = ordinal, At = at } };

    /// <summary>
    /// Put the question in front of the user, then raise <see cref="Shown"/> — that order, for the
    /// same reason every other module places its output before it notifies: the user's outcome must
    /// not be hostage to whatever a UI subscriber does.
    ///
    /// <para><b>A refusal takes the card straight back down and says so</b>, including the
    /// <c>Degraded</c> case where the OS gave the card the keyboard and holds no ink for it: a
    /// topmost blank window holding the user's keyboard is strictly worse than no question at all,
    /// and without the dismiss it would also leave <see cref="Compose"/>'s already-prompting guard
    /// dropping every later quiz for the rest of the session. Upstream force-closes on its own error
    /// path for the same reason (<c>Services/Quiz/PopQuizService.cs:250-254</c>).</para>
    /// </summary>
    protected override void Deliver(PopQuizFiring firing)
    {
        var ask = firing.Ask;
        lock (Gate)
        {
            _ask = ask;
            _lastGrant = null;
            _lastResolution = PopQuizResolution.None;
        }

        var outcome = _presence.Prompt(new InputPromptRequest(
            _placement(),
            QuestionFace(ask),
            keystroke => OnKeystroke(ask, keystroke)));

        lock (Gate)
        {
            _lastPrompt = outcome;
        }

        if (outcome is not CapabilityState.Available)
        {
            _presence.Dismiss();
            Resolve(ask, PopQuizResolution.Refused);
            return;
        }

        Shown?.Invoke(firing.Event);
    }

    /// <summary>
    /// One delivered keystroke. Runs on the thread whose message loop delivered it — the UI thread in
    /// the product — and inside the presence's own catch, so nothing here may assume it can throw
    /// usefully.
    /// </summary>
    private void OnKeystroke(PopQuizAsk ask, InputKeystroke keystroke)
    {
        lock (Gate)
        {
            // A keystroke for a card this module has already finished with. It can happen: the OS
            // delivers what was already in the queue.
            if (!ReferenceEquals(_ask, ask))
            {
                return;
            }
        }

        var step = ask.Apply(
            keystroke.Character,
            keystroke.Kind == InputKeystrokeKind.Character,
            keystroke.Kind == InputKeystrokeKind.Cancel);

        switch (step)
        {
            case PopQuizStep.Picked:
                // Upstream's order, and it is observable: the XP is banked at the CLICK
                // (Windows/PopQuizWindow.xaml.cs:157-167), before the 300 ms wait and before the
                // affirmation is drawn (:170-173). A user who kills the app in that window still has
                // their twenty-five.
                Bank();
                ArmFollowUp(ask, AffirmationDelay, () => Reveal(ask));
                return;

            case PopQuizStep.Skipped:
                // Escape, unanswered: upstream closes the window and awards nothing (:128-134).
                _presence.Dismiss();
                Resolve(ask, PopQuizResolution.Skipped);
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// Bank upstream's twenty-five. Best-effort by upstream's own design — its call sits inside a
    /// <c>try</c> whose <c>catch</c> logs at Debug and lets the affirmation happen anyway
    /// (<c>Windows/PopQuizWindow.xaml.cs:159-167</c>) — and here the ledger returns a typed refusal
    /// instead of throwing, so the outcome is RECORDED rather than swallowed.
    /// </summary>
    private void Bank()
    {
        var grant = _xp?.Grant(AnswerXp, XpSource);
        lock (Gate)
        {
            _lastGrant = grant;
        }
    }

    /// <summary>
    /// Swap the question for its affirmation, then start the dwell that closes the card. Upstream's
    /// two waits, chained in upstream's order (<c>Windows/PopQuizWindow.xaml.cs:170-177</c>), on the
    /// INJECTED clock — there is no <c>Task.Delay</c> anywhere in this port's session paths.
    /// </summary>
    private void Reveal(PopQuizAsk ask)
    {
        lock (Gate)
        {
            if (!ReferenceEquals(_ask, ask))
            {
                return;
            }
        }

        _presence.Update(AffirmationFace(ask));
        ArmFollowUp(ask, AffirmationDwell, () => Close(ask));
        RaiseChanged();
    }

    private void Close(PopQuizAsk ask)
    {
        lock (Gate)
        {
            if (!ReferenceEquals(_ask, ask))
            {
                return;
            }
        }

        _presence.Dismiss();
        Resolve(ask, PopQuizResolution.Answered);
    }

    /// <summary>
    /// Put one of the card's two delays on the clock.
    ///
    /// <para><b>Through <see cref="EffectSignal.Post"/>, not straight off the clock's thread</b>: the
    /// callback touches the native card, and <see cref="IInputPresence"/> is thread-affine to the
    /// thread that created the window. The same route Bubble Count's safety end takes
    /// (<c>Effects/BubbleCountEffect.cs:815</c>). A post is dropped when no UI is bound — which
    /// cannot strand a card, because a card can only be up if <c>Deliver</c> ran, and
    /// <c>Deliver</c> only runs from a projection that already required the binding.</para>
    /// </summary>
    private void ArmFollowUp(PopQuizAsk ask, TimeSpan due, Action work)
    {
        var timer = Clock.Schedule(due, () => Signal.Post(() =>
        {
            lock (Gate)
            {
                if (!ReferenceEquals(_ask, ask))
                {
                    return;
                }
            }

            work();
        }));

        Interlocked.Exchange(ref _followUp, timer)?.Dispose();
    }

    /// <summary>
    /// Drop the outstanding delay, or dispose the handle of the one that has just been spent —
    /// the Bubble Count <c>CancelSafety</c> shape (<c>Effects/BubbleCountEffect.cs:820</c>),
    /// lock-free and idempotent.
    ///
    /// <para><b>It is called from <see cref="Resolve"/> and NOWHERE ELSE, and that is a
    /// deliberate narrowing rather than an oversight.</b> Every path a card can end on goes
    /// through <see cref="Resolve"/> — answered, skipped, refused, withdrawn — so a copy in
    /// <see cref="Deliver"/> or <see cref="OnDisarmed"/> can never be the call that does the
    /// work. Both were written, and a mutation sweep proved neither could be made to fail:
    /// deleting either left every fact green. Unprovable defence is code nobody can maintain,
    /// so it is gone and the one call that IS load-bearing is pinned
    /// (<c>AStopDropsTheCardsPendingDelay…</c>).</para>
    ///
    /// <para>It also disposes a SPENT handle, which is not tidying: a fired one-shot is still an
    /// undisposed OS timer, the leak <see cref="PacedSessionEffect{TFiring}"/> already records
    /// for its own pending firing.</para>
    /// </summary>
    private void CancelFollowUp() => Interlocked.Exchange(ref _followUp, null)?.Dispose();

    private void Resolve(PopQuizAsk ask, PopQuizResolution resolution)
    {
        CancelFollowUp();
        lock (Gate)
        {
            if (!ReferenceEquals(_ask, ask))
            {
                return;
            }

            _ask = null;
            _lastResolution = resolution;
            if (resolution == PopQuizResolution.Answered)
            {
                _answeredCount++;
            }
            else if (resolution == PopQuizResolution.Skipped)
            {
                _skippedCount++;
            }
        }

        Resolved?.Invoke(resolution);
    }

    /// <summary>
    /// Take any question back down. Called from <see cref="OwnedSessionEffect.Disarm"/> and nowhere
    /// else, which is where WPF drops its own visible card on a stop
    /// (<c>Services/Quiz/PopQuizService.cs:139</c> → <c>:144-155</c>, and panic's
    /// <c>PopQuizWindow.ForceCloseAll()</c> at <c>MainWindow/MainWindow.StartStop.cs:373</c>).
    ///
    /// <para><b>The dismiss is guarded on this module having a card up</b>, which is Bubble Count's
    /// finding: the presence is single-tenant and shared, so an unconditional <c>Dismiss()</c> here
    /// would take down a Lock Card that has nothing to do with this row.</para>
    /// </summary>
    protected override void OnDisarmed()
    {
        var ask = Ask;
        if (ask is null)
        {
            return;
        }

        _presence.Dismiss();
        Resolve(ask, PopQuizResolution.Withdrawn);
    }

    /// <summary>
    /// Narrow the arm result to what this module can honestly claim.
    ///
    /// <para><b>There is only one narrowing, and the absence of the second is the point.</b> Every
    /// other asking module has a CONTENT refusal too — no phrase, no clip — because their pools are
    /// the user's. This module's pool is upstream's twenty-five built-in questions
    /// (<c>Services/Quiz/PopQuizService.cs:23-100</c>), so a quiz that comes due always has something
    /// to ask and there is no empty-pool state to report.</para>
    ///
    /// <para><b>A missing XP ledger is NOT a narrowing either.</b> The row still does its whole job —
    /// question, answer, affirmation — and upstream's own XP call is best-effort inside a
    /// <c>try</c>/<c>catch</c> (<c>Windows/PopQuizWindow.xaml.cs:159-167</c>). Reporting Degraded
    /// there would claim a harm the user does not have; <see cref="LastGrant"/> and
    /// <see cref="BanksXp"/> are where that question is answered.</para>
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

        return scheduled;
    }

    /// <summary>True while a card THIS module put up is up: its own intent AND the capability's own
    /// live answer. See <see cref="WorkIsRunning"/>.</summary>
    private bool CardIsUp
    {
        get
        {
            lock (Gate)
            {
                return _ask is not null && _presence.IsPrompting;
            }
        }
    }

    private static string DescribeStation(InputStationObservation station) =>
        $"asked={station.Asked}, window-station-visible={station.WindowStationVisible}, "
        + $"displays={station.DisplayCount}, desktop-reachable={station.DesktopReachable}";

    /// <summary>
    /// The card while the question stands. The wrapped slot carries the question and its four keyed
    /// answers (see <see cref="PopQuizAsk.Face"/>); the two single-line slots stay empty because this
    /// card has no counter to show and nothing typed to echo, and the foot carries upstream's own
    /// exit line.
    /// </summary>
    private static InputPromptContent QuestionFace(PopQuizAsk ask) =>
        new(ask.Face, string.Empty, string.Empty, PopQuizAsk.Hint);

    /// <summary>
    /// The card after an answer. Upstream collapses the question panel entirely and shows the
    /// affirmation alone, big and pink, with "+25 XP" under it
    /// (<c>Windows/PopQuizWindow.xaml.cs:171-173</c>, <c>Windows/PopQuizWindow.xaml:116-126</c>) —
    /// so the affirmation takes the wrapped pink slot the question had, and the XP line goes in the
    /// slot below it.
    ///
    /// <para><b>The XP line appears only when the XP really banked.</b> Upstream prints it
    /// unconditionally, because its award cannot silently fail into a state worth reporting; here it
    /// can (no ledger, or a ledger whose file could not be read), and printing "+25 XP" over a grant
    /// that was refused would be the confident half-truth this port refuses everywhere else.</para>
    /// </summary>
    private InputPromptContent AffirmationFace(PopQuizAsk ask) =>
        new(
            ask.Affirmation ?? string.Empty,
            string.Empty,
            LastGrant?.Banked == true ? PopQuizAsk.XpLine : string.Empty,
            string.Empty);

    /// <summary>
    /// Where the card goes when nobody says otherwise: centred on the primary display at a fraction
    /// of it, through the helper three modules already share
    /// (<c>Effects/PrimaryDisplayPlacement.cs</c>). Its own fractions and its own answer to "no
    /// display at all" stay here — a minimum legal rectangle rather than a null, because
    /// <see cref="Compose"/> refuses first on the station read and a zero-size request would throw at
    /// the boundary rather than refuse.
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
