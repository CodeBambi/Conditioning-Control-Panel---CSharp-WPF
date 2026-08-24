using CcpClient.Desktop.Features.Progression;

namespace CcpClient.Desktop.Features.Mantra;

/// <summary>What one delivered keystroke did to the session.</summary>
public enum MantraStep
{
    /// <summary>Nothing observable — a key with no character, a control character, or a backspace
    /// on an empty box.</summary>
    Ignored,

    /// <summary>The answer changed and the surface should repaint.</summary>
    Typed,

    /// <summary>The mantra was produced, the repetition is banked, and a new mantra is up.</summary>
    Completed,

    /// <summary>The mantra was produced and the repetition was REFUSED by one of the two rate
    /// guards. Upstream leaves the box exactly as the user left it in this case (its
    /// <c>TxtInput.Text = ""</c> is inside the <c>if (TryCompleteMantra())</c> —
    /// <c>Windows/MantraWindow.xaml.cs:225-230</c>), so a fully-typed box just sits there until the
    /// user changes it again. Ported, including that.</summary>
    CompletionRefused,

    /// <summary>The target repetition count was reached. The run is over.</summary>
    SessionComplete,

    /// <summary>The user asked to leave.</summary>
    Cancelled,
}

/// <summary>How much of the mantra the box currently matches, and whether it has gone wrong.
/// Upstream's two locals in <c>UpdateHighlights</c> (<c>Windows/MantraWindow.xaml.cs:118-130</c>),
/// lifted out so the colouring rule can be pinned without a window.</summary>
/// <param name="MatchCount">How many leading characters match, case-insensitively.</param>
/// <param name="HasError">The first character past the match is WRONG — as opposed to simply not
/// typed yet. Upstream breaks out of its loop at the first mismatch, so only ONE character is ever
/// the error character.</param>
public readonly record struct MantraMatch(int MatchCount, bool HasError);

/// <summary>How one character of the mantra should be painted right now.</summary>
public enum MantraCharState
{
    /// <summary>Not typed yet (<c>#353550</c>, <c>Windows/MantraWindow.xaml.cs:29</c>).</summary>
    Dim,

    /// <summary>Matched — painted in the streak-warmed highlight colour
    /// (<see cref="MantraIntensity.Highlight"/>).</summary>
    Matched,

    /// <summary>The character the user got wrong (<c>#FF4444</c>, <c>:30</c>).</summary>
    Wrong,
}

/// <summary>
/// <b>THE TYPED MANTRA MINIGAME</b> — upstream's <c>Services/MantraService.cs</c> plus the typing
/// half of <c>Windows/MantraWindow.xaml.cs</c>, kept pure so every rule is pinnable without a
/// window, a keyboard or a human.
///
/// <para><b>Upstream's window has no caller.</b> <c>MainWindow/MainWindow.PlayTab.cs:262</c> states
/// it in capitals: the Play page's Mantras card came off in the 2026-08-12 relayout, and
/// <c>StartMantraSession</c> (<c>:287</c>) is kept only because it is "the only code in the repo
/// that knows the window needs <c>StartSession(n)</c> to have run before it loads". That ordering
/// hazard does not exist here — a session is constructed already started, and the surface is handed
/// one rather than reaching for a global.</para>
///
/// <para><b>THE MANTRAS ARE THE USER'S OWN WORDS.</b> Upstream's pool is
/// <c>AppSettings.MantraPool</c> (<c>Models/AppSettings.cs:6325</c>), a list the user edits, and
/// <c>Services/PhraseBackupService.cs:44</c> treats it as one of the phrase pools worth rescuing
/// from a bad update. So the rule the media modules already hold applies to it —
/// <c>Effects/MandatoryVideoEffect.cs:9-10</c> ("no path and no file name: the clips are the user's
/// own media and this record reaches event handlers and, one day, a log") and
/// <c>Effects/FlashImagesEffect.cs:8-10</c> ("content-free by construction — a COUNT, never a file
/// name"). <b>Nothing in this type or its surface puts a mantra into a log, a diagnostic or a
/// bark.</b> <see cref="XpSource"/> is a constant for exactly that reason: it is what the ledger
/// prints beside the grant, and a mantra would fit there just as well.</para>
///
/// <para><b>Anti-cheat: the mechanical half is structural here.</b> Upstream's box is a WPF
/// <c>TextBox</c>, so it has to cancel pasting, disable the undo stack, null the context menu and
/// swallow the clipboard gestures (<c>Windows/MantraWindow.xaml.cs:46-52</c>, <c>:234-243</c>,
/// <c>Windows/MantraWindow.xaml:169-171</c>) — and <c>:48-50</c> records exactly why undo had to
/// go: the completed mantra is cleared out of the box, so Ctrl+Z put it straight back and every
/// Ctrl+Z / Ctrl+Y pair counted as another repetition. This session has no edit control at all; it
/// is fed one delivered character at a time, which is the answer this port's lock card already
/// reached and for the same reason (<c>Effects/LockCardTyping.cs:31-40</c>). There is no clipboard
/// to close, no undo stack to disable and no context menu to null. What is left of upstream's
/// hardening is the SEMANTIC half — the two rate guards in <see cref="TryComplete"/>, ported
/// literally — plus the rule that a control character is not typing.</para>
///
/// <para><b>What is NOT here, and why.</b> <c>CreditExternalMantra</c>
/// (<c>Services/MantraService.cs:46-60</c>) credits a mantra the MICROPHONE verified on the spoken
/// path (<c>Services/AutonomyService.cs:1963-1964</c>). The microphone boundary is owner-reserved,
/// so there is no caller for it in this build and inventing one would be inventing a payout. The
/// drone and the three tones are refused with their reason on <see cref="MantraIntensity"/>.</para>
/// </summary>
public sealed class MantraSession
{
    /// <summary>Upstream's clamp on the requested repetition count,
    /// <c>Math.Clamp(targetReps, 1, 100)</c> (<c>Services/MantraService.cs:28</c>).</summary>
    public const int MinTargetReps = 1;

    /// <summary>The other end of <c>Services/MantraService.cs:28</c>'s clamp — and the same ceiling
    /// <c>AppSettings.MantraDefaultCount</c> holds (<c>Models/AppSettings.cs:6335</c>).</summary>
    public const int MaxTargetReps = 100;

    /// <summary>Upstream's default repetition count, <c>_mantraDefaultCount = 10</c>
    /// (<c>Models/AppSettings.cs:6331</c>).</summary>
    public const int DefaultTargetReps = 10;

    /// <summary>Anti-cheat, first guard: a repetition completed sooner than this after the previous
    /// one does not count (<c>Services/MantraService.cs:66-68</c>). Upstream measures it on a
    /// <c>Stopwatch</c> started at <c>StartSession</c> and restarted on every banked repetition
    /// (<c>:35</c>, <c>:98</c>); here it is measured on the injected clock.</summary>
    public static readonly TimeSpan MinimumTimePerMantra = TimeSpan.FromSeconds(1.5);

    /// <summary>Anti-cheat, second guard: at most twenty banked repetitions per window
    /// (<c>Services/MantraService.cs:76</c>).</summary>
    public const int MaxCompletionsPerWindow = 20;

    /// <summary>The rate window the count above is measured over, and it ROLLS rather than slides:
    /// upstream zeroes the counter and re-bases the window the first time a completion is attempted
    /// sixty seconds or more after the window opened (<c>Services/MantraService.cs:71-75</c>).
    /// </summary>
    public static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(60);

    /// <summary>Upstream's base payout, <c>AddXP(30 + bonusXP, XPSource.Mantra)</c>
    /// (<c>Services/MantraService.cs:86</c>).</summary>
    public const double BaseXp = 30;

    /// <summary>The streak bonus's slope, <c>Math.Min(Streak * 5, 50)</c>
    /// (<c>Services/MantraService.cs:85</c>).</summary>
    public const double StreakXpPerRepetition = 5;

    /// <summary>The streak bonus's cap, the <c>50</c> in <c>Services/MantraService.cs:85</c>.
    /// Reached at a streak of ten and flat from there.</summary>
    public const double MaxStreakBonusXp = 50;

    /// <summary>What the ledger records this grant as. A CONSTANT, never the mantra — see the type
    /// remarks. Upstream's own tag is <c>XPSource.Mantra</c>
    /// (<c>Services/MantraService.cs:86</c>).</summary>
    public const string XpSource = "typed mantra";

    /// <summary>Inactivity that breaks a streak. Upstream runs a <c>DispatcherTimer</c> at this
    /// interval and restarts it on every change to the box
    /// (<c>Windows/MantraWindow.xaml.cs:77-80</c>, <c>:213-214</c>, <c>:200-206</c>).</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Upstream's mantra of last resort, used verbatim when the pool is null or empty
    /// (<c>Services/MantraService.cs:127</c>). Note that upstream does NOT record it as the last
    /// mantra, so an empty pool repeats it forever — which is the only thing it can do.</summary>
    public const string FallbackMantra = "I am deeply relaxed";

    /// <summary>Upstream's built-in pool, verbatim (<c>Models/AppSettings.cs:6318-6322</c>). It is
    /// the pool a fresh install starts with; a user who has edited theirs is handed their own.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultPool =
    [
        "I am deeply relaxed",
        "My mind is open and receptive",
        "I feel calm and peaceful",
        "I surrender to the process",
        "Every breath takes me deeper",
    ];

    private readonly string[] _pool;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Random _random;
    private readonly ProgressionLedger? _xp;

    private string? _lastMantra;
    private DateTimeOffset _mantraStartedAt;
    private DateTimeOffset _windowStartedAt;
    private DateTimeOffset _lastInputAt;
    private int _completionsThisWindow;

    /// <param name="targetReps">How many repetitions this run asks for, clamped to upstream's
    /// 1..100 (<c>Services/MantraService.cs:28</c>).</param>
    /// <param name="pool">The user's mantras. Null or empty falls back the way upstream's
    /// <c>NextMantra</c> does (<c>:124-129</c>).</param>
    /// <param name="xp">The XP ledger, or null. Optional for the reason every other XP caller in
    /// this port takes it that way (<c>Effects/PopQuizEffect.cs:212</c>): with no ledger nothing is
    /// banked and the surface makes no XP claim.</param>
    /// <param name="clock">The injected clock. Both rate guards and the idle timeout are measured
    /// on it, so no rule in here needs a wall-clock wait to prove.</param>
    /// <param name="random">The pool draw. Injectable for the reason every other module's is: the
    /// DRAW is what a fact makes deterministic, and the rule it feeds stays this type's own.</param>
    public MantraSession(
        int targetReps = DefaultTargetReps,
        IReadOnlyList<string>? pool = null,
        ProgressionLedger? xp = null,
        Func<DateTimeOffset>? clock = null,
        Random? random = null)
    {
        _pool = pool is { Count: > 0 } ? [.. pool] : [];
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        _random = random ?? Random.Shared;
        _xp = xp;

        TargetCount = Math.Clamp(targetReps, MinTargetReps, MaxTargetReps);   // :28
        IsActive = true;                                                      // :32

        var now = _clock();
        _windowStartedAt = now;                                               // :34
        _mantraStartedAt = now;                                               // :35
        _lastInputAt = now;
        NextMantra();                                                         // :36
    }

    /// <summary>The mantra on screen, or null once the run has ended
    /// (<c>Services/MantraService.cs:118</c>).</summary>
    public string? CurrentMantra { get; private set; }

    /// <summary>What the user has produced toward <see cref="CurrentMantra"/>.</summary>
    public string Answer { get; private set; } = string.Empty;

    /// <summary>Repetitions banked this run (<c>Services/MantraService.cs:17</c>).</summary>
    public int Completions { get; private set; }

    /// <summary>The live streak (<c>Services/MantraService.cs:15</c>).</summary>
    public int Streak { get; private set; }

    /// <summary>The best streak this run reached (<c>Services/MantraService.cs:16</c>).</summary>
    public int BestStreak { get; private set; }

    /// <summary>How many repetitions this run asks for
    /// (<c>Services/MantraService.cs:18</c>).</summary>
    public int TargetCount { get; }

    /// <summary>Whether the run is still taking input
    /// (<c>Services/MantraService.cs:19</c>).</summary>
    public bool IsActive { get; private set; }

    /// <summary>True when this session has a ledger to bank into. False is not a fault: it is the
    /// honest state of a host that opened none, and the surface then makes no XP claim.</summary>
    public bool BanksXp => _xp is not null;

    /// <summary>The most recent grant, or null before anything banked and with no ledger. A typed
    /// OUTCOME, so a surface renders whether the XP really reached the file rather than assuming
    /// it.</summary>
    public XpGrant? LastGrant { get; private set; }

    /// <summary>How much a banked repetition pays: upstream's
    /// <c>30 + Math.Min(Streak * 5, 50)</c> evaluated AFTER the increment
    /// (<c>Services/MantraService.cs:81-86</c> — <c>Streak++</c> happens first, so the very first
    /// repetition of a run pays 35 and the cap is reached at a streak of ten).</summary>
    public static double XpFor(int streakAfterCompletion) =>
        BaseXp + Math.Min(streakAfterCompletion * StreakXpPerRepetition, MaxStreakBonusXp);

    /// <summary>
    /// How much of <see cref="CurrentMantra"/> the box matches. Upstream's loop verbatim
    /// (<c>Windows/MantraWindow.xaml.cs:121-130</c>): compare case-insensitively character by
    /// character and STOP at the first mismatch, so a wrong character hides everything behind it and
    /// only one character is ever the error character. Characters typed past the end of the mantra
    /// are not examined at all and are therefore not an error — upstream's loop bounds are
    /// <c>i &lt; mantra.Length &amp;&amp; i &lt; input.Length</c>.
    /// </summary>
    public static MantraMatch Match(string answer, string mantra)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(mantra);

        var matchCount = 0;
        for (var i = 0; i < mantra.Length && i < answer.Length; i++)
        {
            if (char.ToLowerInvariant(answer[i]) == char.ToLowerInvariant(mantra[i]))
            {
                matchCount = i + 1;
            }
            else
            {
                return new MantraMatch(matchCount, HasError: true);
            }
        }

        return new MantraMatch(matchCount, HasError: false);
    }

    /// <summary>
    /// How character <paramref name="index"/> of the mantra should be painted, given
    /// <paramref name="match"/>. Upstream's three-way choice verbatim
    /// (<c>Windows/MantraWindow.xaml.cs:133-144</c>).
    /// </summary>
    public static MantraCharState StateOf(int index, MantraMatch match) =>
        index < match.MatchCount ? MantraCharState.Matched
        : match.HasError && index == match.MatchCount ? MantraCharState.Wrong
        : MantraCharState.Dim;

    /// <summary>The live match against the mantra on screen. Empty once the run has ended.</summary>
    public MantraMatch CurrentMatch =>
        CurrentMantra is null ? default : Match(Answer, CurrentMantra);

    /// <summary>
    /// Apply one delivered keystroke and say what it did — upstream's <c>TxtInput_TextChanged</c>
    /// (<c>Windows/MantraWindow.xaml.cs:208-232</c>) and its Escape branch (<c>:440-447</c>), in
    /// upstream's order: refresh the idle window FIRST (<c>:213-214</c>, before anything else the
    /// handler does), then re-match, then test for completion.
    /// </summary>
    /// <param name="character">The character the platform's keyboard translation produced.</param>
    /// <param name="isCharacter">Whether <paramref name="character"/> is one. A control character is
    /// not typing — the one piece of upstream's mechanical hardening that survives a surface with no
    /// edit control (<c>Effects/LockCardTyping.cs:36-40</c>).</param>
    /// <param name="isBackspace">Upstream's box supports it, and it is not a completion route.</param>
    /// <param name="isCancel">The user pressed Escape (<c>:442</c>).</param>
    public MantraStep Apply(char character, bool isCharacter, bool isBackspace, bool isCancel)
    {
        if (isCancel)
        {
            return MantraStep.Cancelled;
        }

        // ":210" — the handler's own first line. Nothing below it runs on a finished or ended run,
        // including the idle refresh.
        if (!IsActive || CurrentMantra is null)
        {
            return MantraStep.Ignored;
        }

        if (isBackspace)
        {
            if (Answer.Length == 0)
            {
                return MantraStep.Ignored;
            }

            Answer = Answer[..^1];
            _lastInputAt = _clock();                                          // :213-214
            return MantraStep.Typed;
        }

        if (!isCharacter || char.IsControl(character))
        {
            return MantraStep.Ignored;
        }

        Answer += character;
        _lastInputAt = _clock();                                              // :213-214

        var match = Match(Answer, CurrentMantra);

        // ":223" — verbatim, and both halves are load-bearing: the match must cover the whole
        // mantra AND the box must be exactly that long, so characters past the end do not complete
        // it. Upstream does NOT trim here (its lock card does, LockCardWindow.xaml.cs:740).
        if (match.MatchCount != CurrentMantra.Length || Answer.Length != CurrentMantra.Length)
        {
            return MantraStep.Typed;
        }

        return TryComplete();
    }

    /// <summary>
    /// Upstream's <c>TryCompleteMantra</c> (<c>Services/MantraService.cs:62-104</c>), guard for
    /// guard and in its order. Public because upstream's is: it is the seam the spoken path calls
    /// too (<c>Services/AutonomyService.cs:1963</c>), even though this build has no microphone to
    /// call it from.
    /// </summary>
    public MantraStep TryComplete()
    {
        if (!IsActive || CurrentMantra is null)
        {
            return MantraStep.Ignored;                                        // :64
        }

        var now = _clock();

        // :66-68 — the minimum. Strictly less than, so a repetition landing exactly on the floor
        // counts.
        if (now - _mantraStartedAt < MinimumTimePerMantra)
        {
            return MantraStep.CompletionRefused;
        }

        // :70-75 — the window ROLLS. It is re-based here, on the attempt, rather than by a timer,
        // so a run left alone for ten minutes still gets exactly one fresh window on its next
        // attempt rather than ten.
        if (now - _windowStartedAt >= RateWindow)
        {
            _completionsThisWindow = 0;
            _windowStartedAt = now;
        }

        if (_completionsThisWindow >= MaxCompletionsPerWindow)
        {
            return MantraStep.CompletionRefused;                              // :76-77
        }

        _completionsThisWindow++;                                             // :79
        Completions++;                                                        // :80
        Streak++;                                                             // :81
        if (Streak > BestStreak)
        {
            BestStreak = Streak;                                              // :82
        }

        // :84-86. The amount is upstream's; nothing here scales it.
        LastGrant = _xp?.Grant(XpFor(Streak), XpSource);

        Answer = string.Empty;                                                // Windows/MantraWindow.xaml.cs:228

        if (Completions >= TargetCount)
        {
            // :89-96 — the run ends and NextMantra is deliberately not called, so the mantra the
            // user just finished is still the one on screen behind the completion overlay. The
            // repetition timer is not restarted either.
            IsActive = false;
            return MantraStep.SessionComplete;
        }

        _mantraStartedAt = now;                                               // :98
        NextMantra();                                                         // :99
        return MantraStep.Completed;
    }

    /// <summary>
    /// The idle rule, on the injected clock: five seconds without a change to the box breaks a live
    /// streak (<c>Windows/MantraWindow.xaml.cs:77-80</c>, <c>:200-206</c>). Returns true when it
    /// actually broke one, so a surface repaints only when something moved.
    ///
    /// <para>Upstream expresses this as a <c>DispatcherTimer</c> restarted on every change; a clock
    /// comparison is the same outcome and, unlike a timer, is a fact a test can prove without
    /// waiting five seconds. It is also strictly safer: a tick that arrives early — which a
    /// dispatcher timer can — cannot take a streak the user has just fed.</para>
    /// </summary>
    public bool BreakStreakIfIdle()
    {
        if (!IsActive || Streak == 0)
        {
            return false;                                                     // :202
        }

        if (_clock() - _lastInputAt < IdleTimeout)
        {
            return false;
        }

        BreakStreak();
        return true;
    }

    /// <summary>Upstream's <c>BreakStreak</c> (<c>Services/MantraService.cs:106-112</c>). It zeroes
    /// the streak and nothing else: the repetition timer keeps running and the rate window keeps its
    /// count, because neither of those is a thing the user did wrong.</summary>
    public void BreakStreak()
    {
        if (!IsActive || Streak == 0)
        {
            return;                                                           // :108
        }

        Streak = 0;                                                           // :109
    }

    /// <summary>Upstream's <c>EndSession</c> (<c>Services/MantraService.cs:114-120</c>): the run
    /// stops taking input and the mantra comes off. Idempotent, as upstream's is.</summary>
    public void EndSession()
    {
        if (!IsActive)
        {
            return;                                                           // :116
        }

        IsActive = false;                                                     // :117
        CurrentMantra = null;                                                 // :118
    }

    /// <summary>
    /// Draw the next mantra (<c>Services/MantraService.cs:122-146</c>): the fallback when the pool
    /// is empty, the single entry when there is only one, and otherwise a draw that never repeats
    /// the previous one.
    ///
    /// <para><b>One deliberate divergence, and it is a HANG upstream.</b> Upstream rejection-samples
    /// — <c>do { next = pool[_random.Next(pool.Count)]; } while (next == _lastMantra &amp;&amp;
    /// pool.Count &gt; 1);</c> (<c>:138-142</c>) — and a pool whose entries are all the SAME string
    /// (two copies of one mantra is enough) never leaves that loop. Drawing from the pool with the
    /// previous mantra excluded produces exactly the same distribution for every pool where
    /// upstream terminates, and terminates for the ones where it does not.</para>
    /// </summary>
    private void NextMantra()
    {
        if (_pool.Length == 0)
        {
            CurrentMantra = FallbackMantra;                                   // :124-129
            return;
        }

        if (_pool.Length == 1)
        {
            CurrentMantra = _pool[0];                                         // :131-135
            return;
        }

        var candidates = Array.FindAll(_pool, m => m != _lastMantra);
        var next = candidates.Length == 0
            ? _pool[_random.Next(_pool.Length)]
            : candidates[_random.Next(candidates.Length)];

        _lastMantra = next;                                                   // :144
        CurrentMantra = next;                                                 // :145
    }
}
