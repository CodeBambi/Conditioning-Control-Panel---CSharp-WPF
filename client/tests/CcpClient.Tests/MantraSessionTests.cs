using CcpClient.Desktop.Features.Mantra;
using CcpClient.Desktop.Features.Progression;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// THE TYPED MANTRA MINIGAME's rules — upstream's <c>Services/MantraService.cs</c> and the typing
/// half of <c>Windows/MantraWindow.xaml.cs</c>, driven with no window, no keyboard and no human.
///
/// <para><b>Everything with a time in it runs on the injected clock</b>, so the 1.5-second floor,
/// the sixty-second rate window and the five-second idle break are all proved instantly and none of
/// them is a wall-clock wait. The clock is driven by LITERAL offsets that are deliberately not the
/// constants under test — a fact that advances the clock by <c>MinimumTimePerMantra</c> and then
/// asserts <c>MinimumTimePerMantra</c> proves only that a field equals itself.</para>
///
/// <para><b>WHAT THESE FACTS DO NOT PROVE.</b> Pure logic and one temp-file ledger. Nothing here
/// renders, composites, takes real keyboard input, opens a window, plays a sound, or runs on Linux.
/// The window that plays these rules is proved separately and only to draw level; nothing in this
/// port proves the game is REACHABLE, because in this build it is not — see
/// <see cref="MantraLaunch"/>.</para>
/// </summary>
public sealed class MantraSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-mantra-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    /// <summary>One phrase, and one nobody would write by accident. It stands in for the text the
    /// user wrote into their own pool.</summary>
    private const string Secret = "zubrowka lantern quietly folds";

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    private sealed class SinkAdapter(List<string> lines) : ILogSink
    {
        public void Log(string message) => lines.Add(message);
    }

    /// <summary>
    /// A hand-cranked clock. Nothing in this file reads the wall clock.
    ///
    /// <para>It advances in TICKS rather than through <c>AddSeconds</c>, because a fact about a
    /// 1.5-second boundary must not be at the mercy of a double-to-tick rounding rule: measured
    /// here, <c>AddSeconds(1.4)</c> followed by <c>AddSeconds(0.1)</c> lands ONE TICK short of a
    /// second and a half, which silently turned the boundary case into the refusal case.</para>
    /// </summary>
    private sealed class Clock
    {
        private DateTimeOffset _now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset Now() => _now;

        public void Advance(double seconds) =>
            _now = _now.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
    }

    /// <summary>A real ledger over a real temp file. Awaited rather than bridged: the facts that
    /// need one are <c>async</c> so this file contains no blocking wait at all.</summary>
    private async Task<(PersistenceStore<ProgressionDocument> Store, ProgressionLedger Ledger)> NewLedgerAsync()
    {
        var dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var store = new PersistenceStore<ProgressionDocument>(
            new OperationRegistry().OwnerFor("MantraSessionTests-" + Guid.NewGuid().ToString("N")),
            new SinkAdapter(_log),
            Path.Combine(dir, ProgressionDocument.FileName),
            ProgressionDocument.CurrentSchemaVersion);
        await store.StartAsync(TestContext.Current.CancellationToken);
        return (store, new ProgressionLedger(store, _log.Add));
    }

    /// <summary>Type the whole of the mantra that is up, one character at a time, and return the
    /// step the last one produced. The real path: no session method is called directly.</summary>
    private static MantraStep TypeCurrentMantra(MantraSession session)
    {
        var mantra = session.CurrentMantra ?? throw new InvalidOperationException("no mantra is up");
        var step = MantraStep.Ignored;
        foreach (var c in mantra)
        {
            step = session.Apply(c, isCharacter: true, isBackspace: false, isCancel: false);
        }

        return step;
    }

    private static MantraStep Type(MantraSession session, string text)
    {
        var step = MantraStep.Ignored;
        foreach (var c in text)
        {
            step = session.Apply(c, isCharacter: true, isBackspace: false, isCancel: false);
        }

        return step;
    }

    // ==================================================================================
    // The payout
    // ==================================================================================

    /// <summary>
    /// <b>30 + min(streak * 5, 50)</b>, banked into a real ledger, evaluated AFTER the increment —
    /// so the first repetition of a run pays 35, not 30, and the cap bites at a streak of ten
    /// (<c>Services/MantraService.cs:81-86</c>).
    ///
    /// <para>The ladder is asserted as literals against the ledger's own running total, which is
    /// what a user's level is actually built from. Reds on changing the base, the slope, the cap, or
    /// the order of <c>Streak++</c> against the payout.</para>
    /// </summary>
    [Fact]
    public async Task ThePayoutLadder_IsThirtyPlusFivePerStreak_CappedAtFifty()
    {
        var clock = new Clock();
        var (store, ledger) = await NewLedgerAsync();
        _ = store;

        // A one-entry pool so the mantra is the same every repetition and the ladder is the only
        // thing moving.
        var session = new MantraSession(20, ["a"], ledger, clock.Now);

        double[] expected = [35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 80, 80];
        double running = 0;

        for (var i = 0; i < expected.Length; i++)
        {
            clock.Advance(2);                 // literal, comfortably over the 1.5s floor
            Assert.Equal(MantraStep.Completed, TypeCurrentMantra(session));
            running += expected[i];
            Assert.Equal(i + 1, session.Streak);
            Assert.Equal(running, ledger.XpIntoLevel);
            Assert.Equal(XpGrantState.Granted, session.LastGrant!.State);
            Assert.Equal(expected[i], session.LastGrant.Amount);
        }

        // 35 + 40 + ... + 80 + 80 + 80 = 735, and it is still inside level 1 (800 XP), so the
        // running total above is a direct read of what banked rather than a level-spend artefact.
        Assert.Equal(735, ledger.XpIntoLevel);
        Assert.Equal(1, ledger.Level);
    }

    // ==================================================================================
    // The two rate guards
    // ==================================================================================

    /// <summary>
    /// <b>A repetition produced sooner than a second and a half after the last one does not
    /// count</b> (<c>Services/MantraService.cs:66-68</c>) — and upstream's refusal LEAVES THE BOX
    /// FULL, because the clear is inside its <c>if (TryCompleteMantra())</c>
    /// (<c>Windows/MantraWindow.xaml.cs:225-230</c>).
    ///
    /// <para>1.4 seconds is refused, 1.5 exactly is taken. Both are written as literals so the guard
    /// cannot be widened without this reddening, and the boundary pins <c>&lt;</c> against
    /// <c>&lt;=</c>.</para>
    /// </summary>
    [Fact]
    public void ARepetitionInsideOnePointFiveSeconds_IsRefused_AndTheBoxKeepsIt()
    {
        var clock = new Clock();
        var session = new MantraSession(5, ["good girl"], clock: clock.Now);

        clock.Advance(1.4);
        Assert.Equal(MantraStep.CompletionRefused, TypeCurrentMantra(session));
        Assert.Equal(0, session.Completions);
        Assert.Equal(0, session.Streak);
        Assert.Equal("good girl", session.Answer);        // the box still holds it (:225-230)

        // 0.1 more takes the elapsed time to exactly 1.5. Nothing re-triggers on its own upstream
        // either — the user has to change the box — so a backspace and a retype is the real path.
        clock.Advance(0.1);
        Assert.Equal(MantraStep.Typed, session.Apply('\0', isCharacter: false, isBackspace: true, isCancel: false));
        Assert.Equal(MantraStep.Completed, session.Apply('l', isCharacter: true, isBackspace: false, isCancel: false));
        Assert.Equal(1, session.Completions);
        Assert.Equal(string.Empty, session.Answer);
    }

    /// <summary>
    /// <b>Twenty banked repetitions per window, and the window ROLLS</b>
    /// (<c>Services/MantraService.cs:70-77</c>): the twenty-first inside the same minute is refused,
    /// and the first attempt sixty seconds or more after the window opened re-bases it and is taken.
    ///
    /// <para>Reds on raising the ceiling (the twenty-first banks), on removing the roll (the
    /// post-roll repetition stays refused) and on making the window slide instead of roll.</para>
    /// </summary>
    [Fact]
    public void TwentyRepetitionsPerWindow_IsTheCeiling_AndTheWindowRolls()
    {
        var clock = new Clock();
        // 100 target reps so the ceiling, not the target, is what stops the run.
        var session = new MantraSession(100, ["ok"], clock: clock.Now);

        // 20 x 2s = 40s, inside the window.
        for (var i = 0; i < 20; i++)
        {
            clock.Advance(2);
            Assert.Equal(MantraStep.Completed, TypeCurrentMantra(session));
        }

        Assert.Equal(20, session.Completions);

        // 42s in: past the per-repetition floor, still inside the same minute.
        clock.Advance(2);
        Assert.Equal(MantraStep.CompletionRefused, TypeCurrentMantra(session));
        Assert.Equal(20, session.Completions);
        Assert.Equal(20, session.Streak);       // a refusal is not a mistake; the streak survives

        // 61s from the window's start: the roll re-bases it and the next one is taken.
        clock.Advance(19);
        Assert.Equal(MantraStep.Typed, session.Apply('\0', isCharacter: false, isBackspace: true, isCancel: false));
        Assert.Equal(MantraStep.Completed, session.Apply('k', isCharacter: true, isBackspace: false, isCancel: false));
        Assert.Equal(21, session.Completions);
    }

    // ==================================================================================
    // The match
    // ==================================================================================

    /// <summary>
    /// <b>The match is a case-insensitive PREFIX that stops dead at the first wrong character</b>
    /// (<c>Windows/MantraWindow.xaml.cs:121-130</c>), and exactly one character is ever painted as
    /// the error (<c>:133-144</c>). Everything behind a mistake goes dim again, which is what makes
    /// the game readable.
    ///
    /// <para>Reds on continuing past a mismatch, on making the comparison case-sensitive, and on
    /// painting the whole tail red instead of one character.</para>
    /// </summary>
    [Fact]
    public void AWrongCharacterStopsTheMatch_AndExactlyOneCharacterIsTheError()
    {
        Assert.Equal(new MantraMatch(0, false), MantraSession.Match("", "deeper"));
        Assert.Equal(new MantraMatch(3, false), MantraSession.Match("dee", "deeper"));
        Assert.Equal(new MantraMatch(3, false), MantraSession.Match("DEE", "deeper"));   // case-insensitive
        Assert.Equal(new MantraMatch(1, true), MantraSession.Match("dxeper", "deeper")); // stops at index 1
        Assert.Equal(new MantraMatch(2, true), MantraSession.Match("deXper", "deeper")); // stops at index 2
        Assert.Equal(new MantraMatch(6, false), MantraSession.Match("deeper", "deeper"));

        // Past the end of the mantra is NOT an error — upstream's loop simply stops looking.
        Assert.Equal(new MantraMatch(6, false), MantraSession.Match("deeperrr", "deeper"));

        var wrong = MantraSession.Match("deXper", "deeper");
        Assert.Equal(MantraCharState.Matched, MantraSession.StateOf(0, wrong));
        Assert.Equal(MantraCharState.Matched, MantraSession.StateOf(1, wrong));
        Assert.Equal(MantraCharState.Wrong, MantraSession.StateOf(2, wrong));
        Assert.Equal(MantraCharState.Dim, MantraSession.StateOf(3, wrong));
        Assert.Equal(MantraCharState.Dim, MantraSession.StateOf(5, wrong));

        var clean = MantraSession.Match("dee", "deeper");
        Assert.Equal(MantraCharState.Matched, MantraSession.StateOf(2, clean));
        Assert.Equal(MantraCharState.Dim, MantraSession.StateOf(3, clean));   // not typed yet, not wrong
    }

    /// <summary>
    /// <b>A completion needs the box to be exactly as long as the mantra</b>, not merely to contain
    /// it — upstream tests <c>matchCount == target.Length &amp;&amp; input.Length ==
    /// target.Length</c> (<c>Windows/MantraWindow.xaml.cs:223</c>). The only way to hold a box
    /// longer than the mantra is through a refused completion, which is how this drives it.
    ///
    /// <para>Reds on dropping the length half of that test (the overlong box completes), and on
    /// clearing the box after a refusal (there is nothing overlong to type into).</para>
    /// </summary>
    [Fact]
    public void AFullMatchWithATrailingCharacter_DoesNotComplete()
    {
        var clock = new Clock();
        var session = new MantraSession(5, ["obey"], clock: clock.Now);

        // Instantly: refused by the 1.5s floor, and the box keeps "obey".
        Assert.Equal(MantraStep.CompletionRefused, TypeCurrentMantra(session));
        Assert.Equal("obey", session.Answer);

        // Now well past the floor. A fifth character makes the box longer than the mantra: the
        // prefix still matches in full, and it still must not complete.
        clock.Advance(9);
        Assert.Equal(MantraStep.Typed, session.Apply('!', isCharacter: true, isBackspace: false, isCancel: false));
        Assert.Equal("obey!", session.Answer);
        Assert.Equal(4, session.CurrentMatch.MatchCount);
        Assert.False(session.CurrentMatch.HasError);
        Assert.Equal(0, session.Completions);

        // Take the extra character back off and it completes.
        Assert.Equal(MantraStep.Completed, session.Apply('\0', isCharacter: false, isBackspace: true, isCancel: false));
        Assert.Equal(1, session.Completions);
    }

    /// <summary>
    /// <b>A control character is not typing.</b> The one piece of upstream's mechanical anti-cheat
    /// that survives a surface with no edit control — upstream needs a whole gesture table
    /// (<c>Windows/MantraWindow.xaml.cs:234-243</c> through
    /// <c>Windows/LockCardWindow.xaml.cs:256-275</c>) because a <c>TextBox</c> turns Ctrl+V into
    /// text; here the same keystroke arrives as <c>0x16</c> and is ignored
    /// (<c>Effects/LockCardTyping.cs:36-40</c>).
    ///
    /// <para>Reds on dropping the <c>char.IsControl</c> guard: the control character lands in the
    /// box, becomes a mismatch, and the mantra stops being completable.</para>
    /// </summary>
    [Fact]
    public void AControlCharacterIsNotTyping()
    {
        var clock = new Clock();
        var session = new MantraSession(5, ["ok"], clock: clock.Now);

        // 0x16 is what Ctrl+V arrives as through a platform's own key-to-text translation, and
        // 0x03 is Ctrl+C. Upstream has to name them in a gesture table because its TextBox would
        // otherwise turn them into a clipboard operation.
        Assert.Equal(MantraStep.Ignored, session.Apply((char)0x16, isCharacter: true, isBackspace: false, isCancel: false));
        Assert.Equal(MantraStep.Ignored, session.Apply((char)0x03, isCharacter: true, isBackspace: false, isCancel: false));
        Assert.Equal(MantraStep.Ignored, session.Apply('\t', isCharacter: true, isBackspace: false, isCancel: false));
        Assert.Equal(MantraStep.Ignored, session.Apply((char)0x1A, isCharacter: true, isBackspace: false, isCancel: false));
        Assert.Equal(string.Empty, session.Answer);

        // A key that produced no character at all is ignored too, and so is a backspace on an
        // empty box.
        Assert.Equal(MantraStep.Ignored, session.Apply('x', isCharacter: false, isBackspace: false, isCancel: false));
        Assert.Equal(MantraStep.Ignored, session.Apply('\0', isCharacter: false, isBackspace: true, isCancel: false));
        Assert.Equal(string.Empty, session.Answer);

        clock.Advance(3);
        Assert.Equal(MantraStep.Completed, TypeCurrentMantra(session));
    }

    // ==================================================================================
    // The streak
    // ==================================================================================

    /// <summary>
    /// <b>Five seconds without touching the box breaks a live streak, and every keystroke refreshes
    /// the window</b> (<c>Windows/MantraWindow.xaml.cs:77-80</c>, <c>:200-206</c>,
    /// <c>:213-214</c>) — including a keystroke that produced a MISTAKE, because upstream refreshes
    /// at the top of the handler before it looks at what was typed.
    ///
    /// <para>4.9 seconds is safe, 5.0 breaks. Both literals. Reds on changing the timeout, and on
    /// dropping the refresh (the streak dies while the user is typing).</para>
    /// </summary>
    [Fact]
    public void FiveSecondsIdle_BreaksTheStreak_AndAnyKeystrokeRefreshesTheWindow()
    {
        var clock = new Clock();
        var session = new MantraSession(10, ["ok"], clock: clock.Now);

        clock.Advance(3);
        Assert.Equal(MantraStep.Completed, TypeCurrentMantra(session));
        Assert.Equal(1, session.Streak);

        clock.Advance(4.9);
        Assert.False(session.BreakStreakIfIdle());
        Assert.Equal(1, session.Streak);

        // A WRONG character. It still refreshes the window (:213-214 runs before the match).
        Assert.Equal(MantraStep.Typed, session.Apply('z', isCharacter: true, isBackspace: false, isCancel: false));
        Assert.True(session.CurrentMatch.HasError);

        clock.Advance(4.9);
        Assert.False(session.BreakStreakIfIdle());
        Assert.Equal(1, session.Streak);

        clock.Advance(0.1);
        Assert.True(session.BreakStreakIfIdle());
        Assert.Equal(0, session.Streak);

        // Nothing to break twice.
        Assert.False(session.BreakStreakIfIdle());
    }

    /// <summary>
    /// <b>A broken streak costs the streak and NOTHING else</b> (<c>Services/MantraService.cs:106-112</c>):
    /// the best streak stands, the banked repetitions stand, and the XP already granted is not
    /// clawed back. It also does not reset the rate window or the per-repetition timer, because
    /// neither of those is something the user did wrong.
    ///
    /// <para>Reds on any of those being reset by <c>BreakStreak</c>, and on the best streak being
    /// recomputed from the live streak rather than remembered.</para>
    /// </summary>
    [Fact]
    public async Task ABrokenStreak_KeepsTheBestStreakTheRepetitionsAndTheXp()
    {
        var clock = new Clock();
        var (store, ledger) = await NewLedgerAsync();
        _ = store;
        var session = new MantraSession(10, ["ok"], ledger, clock.Now);

        for (var i = 0; i < 3; i++)
        {
            clock.Advance(2);
            Assert.Equal(MantraStep.Completed, TypeCurrentMantra(session));
        }

        Assert.Equal(3, session.Streak);
        Assert.Equal(3, session.BestStreak);
        Assert.Equal(120d, ledger.XpIntoLevel);      // 35 + 40 + 45

        clock.Advance(6);
        Assert.True(session.BreakStreakIfIdle());

        Assert.Equal(0, session.Streak);
        Assert.Equal(3, session.BestStreak);
        Assert.Equal(3, session.Completions);
        Assert.Equal(120d, ledger.XpIntoLevel);

        // The next repetition restarts the ladder at 35, which is what a broken streak COSTS.
        clock.Advance(2);
        Assert.Equal(MantraStep.Completed, TypeCurrentMantra(session));
        Assert.Equal(1, session.Streak);
        Assert.Equal(3, session.BestStreak);
        Assert.Equal(155d, ledger.XpIntoLevel);
    }

    // ==================================================================================
    // The end of a run
    // ==================================================================================

    /// <summary>
    /// <b>The run ends at the target and the mantra just finished stays on screen</b>
    /// (<c>Services/MantraService.cs:89-96</c> deliberately does not draw a new one) and input stops
    /// counting.
    ///
    /// <para><b>And <c>EndSession</c> is a NO-OP on a run that already finished</b>, because
    /// upstream's first line is <c>if (!IsActive) return;</c> (<c>:116</c>) and a completed run has
    /// already cleared <c>IsActive</c> (<c>:91</c>). That is upstream's behaviour rather than a
    /// convenience: the mantra the user finished on stays readable behind the completion overlay
    /// right up to the moment the window goes away. The clearing path (<c>:118</c>) is the one that
    /// matters — a run ABANDONED mid-way — and is driven here separately.</para>
    ///
    /// <para>Reds on drawing a fresh mantra at the end (the completion overlay would sit over a
    /// mantra the user never typed), on the run continuing to take repetitions past its target, and
    /// on <c>EndSession</c> losing either half of its behaviour.</para>
    /// </summary>
    [Fact]
    public void TheRunEndsAtItsTarget_AndTheLastMantraStaysUp()
    {
        var clock = new Clock();
        var session = new MantraSession(2, ["ok"], clock: clock.Now);

        clock.Advance(2);
        Assert.Equal(MantraStep.Completed, TypeCurrentMantra(session));
        clock.Advance(2);
        Assert.Equal(MantraStep.SessionComplete, TypeCurrentMantra(session));

        Assert.False(session.IsActive);
        Assert.Equal(2, session.Completions);
        Assert.Equal("ok", session.CurrentMantra);      // still up, behind the overlay (:89-96)
        Assert.Equal(string.Empty, session.Answer);

        // Nothing more counts.
        clock.Advance(9);
        Assert.Equal(MantraStep.Ignored, Type(session, "ok"));
        Assert.Equal(2, session.Completions);
        Assert.Equal(MantraStep.Ignored, session.TryComplete());

        // :116 — a finished run is already inactive, so this returns without touching anything.
        session.EndSession();
        Assert.Equal("ok", session.CurrentMantra);

        // The clearing path: a run the user walked out of while it was still live.
        var abandoned = new MantraSession(9, ["ok"], clock: clock.Now);
        Assert.Equal("ok", abandoned.CurrentMantra);
        abandoned.EndSession();
        Assert.False(abandoned.IsActive);               // :117
        Assert.Null(abandoned.CurrentMantra);           // :118
        abandoned.EndSession();                         // idempotent (:116)
        Assert.Null(abandoned.CurrentMantra);
    }

    /// <summary>Upstream's clamp on the requested repetition count,
    /// <c>Math.Clamp(targetReps, 1, 100)</c> (<c>Services/MantraService.cs:28</c>), and its default
    /// of ten (<c>Models/AppSettings.cs:6331</c>). Reds on either bound moving.</summary>
    [Fact]
    public void TheRepetitionCountIsClampedToUpstreamsOneToOneHundred()
    {
        Assert.Equal(1, new MantraSession(0).TargetCount);
        Assert.Equal(1, new MantraSession(-40).TargetCount);
        Assert.Equal(1, new MantraSession(1).TargetCount);
        Assert.Equal(37, new MantraSession(37).TargetCount);
        Assert.Equal(100, new MantraSession(100).TargetCount);
        Assert.Equal(100, new MantraSession(500).TargetCount);
        Assert.Equal(10, new MantraSession().TargetCount);
    }

    // ==================================================================================
    // The pool
    // ==================================================================================

    /// <summary>
    /// <b>The draw never repeats the mantra just typed</b> (<c>Services/MantraService.cs:137-142</c>),
    /// and it still uses the whole pool. An empty pool falls back to upstream's single line
    /// (<c>:124-129</c>) and a one-entry pool simply repeats it (<c>:131-135</c>).
    ///
    /// <para>Reds on dropping the exclusion — over a hundred draws from a three-entry pool a naive
    /// uniform draw repeats itself with overwhelming probability. The "all three appear" clause is
    /// this fact's own negative control: without it, a draw that got stuck alternating between two
    /// entries would pass.</para>
    ///
    /// <para>The four-second spacing is not decoration: at two seconds a run of this length would
    /// hit the twenty-per-minute ceiling a third of the way in and start refusing, which is a
    /// different fact getting in the way of this one.</para>
    /// </summary>
    [Fact]
    public void TheDrawNeverRepeatsTheMantraJustTyped()
    {
        var clock = new Clock();
        var session = new MantraSession(100, ["one", "two", "three"], clock: clock.Now, random: new Random(20260824));

        var seen = new List<string>();
        for (var i = 0; i < 100; i++)
        {
            seen.Add(session.CurrentMantra!);
            clock.Advance(4);
            Assert.Equal(i == 99 ? MantraStep.SessionComplete : MantraStep.Completed, TypeCurrentMantra(session));
        }

        for (var i = 1; i < seen.Count; i++)
        {
            Assert.NotEqual(seen[i - 1], seen[i]);
        }

        Assert.Equal(3, seen.Distinct().Count());

        Assert.Equal("I am deeply relaxed", new MantraSession(3, []).CurrentMantra);        // :124-129
        Assert.Equal("I am deeply relaxed", new MantraSession(3).CurrentMantra);            // null pool
        Assert.Equal("only one", new MantraSession(3, ["only one"]).CurrentMantra);         // :131-135
    }

    /// <summary>The built-in pool is still the shipping product's, byte for byte
    /// (<c>Models/AppSettings.cs:6316-6323</c>). A source-level pin, so a change made upstream is
    /// REPORTED here rather than silently diverged from — the shape
    /// <c>ProgressionLedgerTests.TheCurvesLiterals_AreStillTheShippingSourcesLiterals</c>
    /// established.</summary>
    [Fact]
    public void TheBuiltInPool_IsStillTheShippingSourcesPool()
    {
        var settings = ReadRepoFile("ConditioningControlPanel/Models/AppSettings.cs");

        Assert.Equal(5, MantraSession.DefaultPool.Count);
        foreach (var mantra in MantraSession.DefaultPool)
        {
            Assert.Contains($"\"{mantra}\"", settings, StringComparison.Ordinal);
        }

        Assert.Contains("private List<string> _mantraPool = new()", settings, StringComparison.Ordinal);
        Assert.Contains("private int _mantraDefaultCount = 10;", settings, StringComparison.Ordinal);
        Assert.Contains($"\"{MantraSession.FallbackMantra}\"",
            ReadRepoFile("ConditioningControlPanel/Services/MantraService.cs"), StringComparison.Ordinal);
    }

    // ==================================================================================
    // The XP seam
    // ==================================================================================

    /// <summary>
    /// <b>With no ledger the game is still the game.</b> Repetitions count, streaks climb, the run
    /// ends — and nothing is banked and nothing is CLAIMED: <see cref="MantraSession.BanksXp"/> is
    /// false and <see cref="MantraSession.LastGrant"/> stays null, so a surface has a typed answer
    /// to render instead of a "+35 XP" over a grant that never happened. The shape
    /// <c>Effects/PopQuizEffect.cs:246-248</c> already set.
    ///
    /// <para>Reds on the completion becoming conditional on a ledger, and on a null grant being
    /// dressed up as a granted one.</para>
    /// </summary>
    [Fact]
    public void WithNoLedger_TheRunStillCounts_AndNoXpIsClaimed()
    {
        var clock = new Clock();
        var session = new MantraSession(3, ["ok"], xp: null, clock: clock.Now);

        Assert.False(session.BanksXp);

        clock.Advance(2);
        Assert.Equal(MantraStep.Completed, TypeCurrentMantra(session));
        Assert.Equal(1, session.Completions);
        Assert.Equal(1, session.Streak);
        Assert.Null(session.LastGrant);
    }

    /// <summary>
    /// <b>A ledger that could not be read refuses the grant and the run carries on.</b> The refusal
    /// is <c>ProgressionLedger</c>'s own (<c>:266-272</c>) — a stopped store is not
    /// <c>Known</c> — and what this pins is that the minigame does not fall over, does not retry,
    /// and does not silently report a payout: the grant comes back typed
    /// <c>RefusedLedgerUnknown</c>.
    ///
    /// <para>Reds on the session asserting a banked grant, and on a refusal aborting the
    /// repetition.</para>
    /// </summary>
    [Fact]
    public async Task AnUnreadableLedger_RefusesTheGrant_AndTheRunCarriesOn()
    {
        var clock = new Clock();
        var (store, ledger) = await NewLedgerAsync();
        await store.StopAsync();

        var session = new MantraSession(3, ["ok"], ledger, clock.Now);

        clock.Advance(2);
        Assert.Equal(MantraStep.Completed, TypeCurrentMantra(session));
        Assert.Equal(1, session.Completions);
        Assert.True(session.BanksXp);
        Assert.Equal(XpGrantState.RefusedLedgerUnknown, session.LastGrant!.State);
        Assert.False(session.LastGrant.Banked);
        Assert.Null(ledger.Level);
    }

    // ==================================================================================
    // Privacy
    // ==================================================================================

    /// <summary>
    /// <b>THE MANTRAS ARE THE USER'S OWN WORDS AND THEY DO NOT REACH A LOG.</b> Upstream's pool is a
    /// list the user writes (<c>Models/AppSettings.cs:6325</c>). This runs a whole session over a
    /// phrase nobody would produce by accident, through a REAL ledger with a real log sink, and
    /// requires that the XP genuinely banked while not one captured line — the ledger's grant lines
    /// and the store's own — contains the phrase or any four-character run of it.
    ///
    /// <para>The rule is the media modules' (<c>Effects/MandatoryVideoEffect.cs:9-10</c>,
    /// <c>Effects/FlashImagesEffect.cs:8-10</c>). The mutation it exists to catch is one character
    /// wide: <c>Grant(XpFor(Streak), XpSource)</c> becoming <c>Grant(XpFor(Streak),
    /// CurrentMantra)</c> — the source string is a free-text field printed straight into the
    /// diagnostic, and a mantra fits there just as well as a label does. The banked assertion is
    /// what stops this passing by doing nothing at all.</para>
    /// </summary>
    [Fact]
    public async Task TheMantrasNeverReachTheLog()
    {
        var clock = new Clock();
        var (store, ledger) = await NewLedgerAsync();
        _ = store;
        var session = new MantraSession(3, [Secret], ledger, clock.Now);

        for (var i = 0; i < 3; i++)
        {
            clock.Advance(2);
            _ = TypeCurrentMantra(session);
        }

        // It really ran, and the XP really landed: 35 + 40 + 45.
        Assert.Equal(3, session.Completions);
        Assert.Equal(120d, ledger.XpIntoLevel);
        Assert.NotEmpty(_log);

        var captured = string.Join("\n", _log);
        Assert.DoesNotContain(Secret, captured, StringComparison.OrdinalIgnoreCase);

        // Not just the whole phrase — no four-character window of it either, which is what catches
        // a truncated or word-sliced leak.
        for (var i = 0; i + 4 <= Secret.Length; i++)
        {
            var window = Secret.Substring(i, 4);
            if (window.Contains(' '))
            {
                continue;
            }

            Assert.DoesNotContain(window, captured, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ==================================================================================
    // The ramp
    // ==================================================================================

    /// <summary>
    /// <b>The streak's visual ramp</b> — upstream's <c>UpdateVisualIntensity</c>
    /// (<c>Windows/MantraWindow.xaml.cs:310-351</c>) and its <c>LerpColor</c> (<c>:353-360</c>).
    /// <c>t = min(streak / 15, 1)</c>, and every channel is a TRUNCATING cast, not a rounded one.
    ///
    /// <para>The mid-ramp colours are the fact. At a streak of seven the highlight is
    /// <c>#C879C9</c>; a rounding lerp would produce <c>#C97ACA</c> — all three channels differ, so
    /// the truncation cannot be "cleaned up" without this reddening. Reds equally on moving the
    /// ceiling off fifteen, on either end colour changing, and on the border ramp being made to move
    /// its colour channels instead of only its alpha.</para>
    /// </summary>
    [Fact]
    public void TheStreakRamp_IsUpstreamsTruncatingLerp()
    {
        // Cold: a run that has not banked anything yet.
        var cold = MantraIntensity.For(0);
        Assert.Equal(0d, cold.T);
        Assert.Equal(new MantraColour(0xFF, 0x99, 0x88, 0xDD), cold.Highlight);
        Assert.Equal(0d, cold.WashOpacity);
        Assert.Equal(20d, cold.GlowBlurRadius);
        Assert.Equal(0.6d, cold.GlowOpacity);
        Assert.Equal(new MantraColour(0x40, 0xFF, 0x69, 0xB4), cold.InputBorder);
        Assert.Equal(new MantraColour(0xFF, 0x1A, 0x0A, 0x2E), cold.BaseCentre);

        // Mid-ramp: t = 7/15. TRUNCATED, not rounded.
        var mid = MantraIntensity.For(7);
        Assert.Equal(new MantraColour(0xFF, 0xC8, 0x79, 0xC9), mid.Highlight);
        Assert.Equal(new MantraColour(0xFF, 0xC8, 0x67, 0xC0), mid.GlowColour);
        Assert.Equal(34d, mid.GlowBlurRadius, 12);
        Assert.Equal(new MantraColour(0x99, 0xFF, 0x69, 0xB4), mid.InputBorder);   // alpha only
        Assert.Equal(new MantraColour(0xFF, 0x23, 0x0A, 0x2E), mid.BaseCentre);

        // Hot, and clamped there. Fifteen is the ceiling; thirty is not hotter.
        var hot = MantraIntensity.For(15);
        Assert.Equal(1d, hot.T);
        Assert.Equal(new MantraColour(0xFF, 0xFF, 0x69, 0xB4), hot.Highlight);
        Assert.Equal(0.8d, hot.WashOpacity);
        Assert.Equal(50d, hot.GlowBlurRadius);
        Assert.Equal(1d, hot.GlowOpacity);
        Assert.Equal(new MantraColour(0xFF, 0xFF, 0x69, 0xB4), hot.InputBorder);
        Assert.Equal(hot, MantraIntensity.For(30));
        Assert.Equal(cold, MantraIntensity.For(-4));

        // The two fixed colours the ramp never touches.
        Assert.Equal(new MantraColour(0xFF, 0x35, 0x35, 0x50), MantraIntensity.Dim);
        Assert.Equal(new MantraColour(0xFF, 0xFF, 0x44, 0x44), MantraIntensity.Wrong);
    }

    /// <summary>
    /// <b>The drone and the tones are REFUSED, and the refusal is load-bearing rather than a note.</b>
    /// Upstream's window is an instrument (<c>Windows/MantraWindow.xaml.cs:362-393</c>,
    /// <c>:409-438</c>); this build's audio seam takes a FILE
    /// (<c>Audio/IAudioPresence.cs</c>'s <c>AudioCue(Slot, Path, Volume)</c>) and there is no
    /// oscillator in the tree. This fact holds the two halves together: the number upstream ramps is
    /// still computed exactly (<c>:350</c>), and it is still consumed by nobody.
    ///
    /// <para>Reds if someone wires a sound: the moment <c>DroneGain</c> acquires a consumer, the
    /// grep below finds it and this fact fails, which is the prompt to record a real audio decision
    /// rather than a quiet one.</para>
    /// </summary>
    [Fact]
    public void TheDroneAndTheTones_AreRefusedForANamedReason()
    {
        Assert.Equal(0.05d, MantraIntensity.For(0).DroneGain);
        Assert.Equal(0.4d, MantraIntensity.For(15).DroneGain, 12);
        Assert.Equal(0.05d + (7 / 15d * 0.35d), MantraIntensity.For(7).DroneGain, 12);

        // The seam this build actually has takes a path, not a frequency.
        var audio = ReadRepoFile("client/src/CcpClient.Desktop/Audio/IAudioPresence.cs");
        Assert.Contains("AudioCue(string Slot, string Path, float Volume)", audio, StringComparison.Ordinal);

        // And nothing in the product consumes the gain.
        var consumers = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "client", "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.EndsWith("MantraIntensity.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("DroneGain", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(consumers);
    }

    // ==================================================================================

    private static string ReadRepoFile(string relative)
    {
        var path = Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{relative} is missing at {path} — this guard never skips");
        return File.ReadAllText(path);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "client", "CcpClient.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail($"repo root not found walking up from {AppContext.BaseDirectory} (anchor client/CcpClient.sln) — this guard never skips");
        return string.Empty;
    }
}
