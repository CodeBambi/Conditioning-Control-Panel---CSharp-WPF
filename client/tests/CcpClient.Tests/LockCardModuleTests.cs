using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-110 — EIGHT modules under one spine, and this is the first that ASKS.
///
/// <para><b>What is under test here, and what is deliberately NOT.</b> These facts drive Lock Card
/// through a RECORDING input presence, so the pacing, the phrase rotation, the typing rules, the
/// anti-cheat gate, the arm vocabulary, the three-clause dot and every resolution path are decided
/// deterministically with no window anywhere. <b>The claim that a real window really takes the
/// operating system's foreground and keyboard is NOT made here</b> — it is made in
/// <see cref="InputCapabilityTests"/>, against the OS, with a synthesised keystroke and a negative
/// control. The split is on purpose: a fake presence that reported "the user is typing into it"
/// would be exactly the fake this port has been caught by before.</para>
///
/// <para><b>Zero wall-clock.</b> Everything rides the one injected clock the modules share.</para>
/// </summary>
public class LockCardModuleTests
{
    // =================================================================================
    //  the pure arithmetic — every constant is upstream's and every clamp is pinnable
    // =================================================================================

    [Fact]
    public void TheFirstCardIsAnOffsetIntoTheOpeningInterval_NotAWholeGap_WhichIsUpstreamsOwnFix()
    {
        // LockCardService.cs:159-168 with its comment at :144-155: scheduling the first card as a
        // whole inter-arrival gap put the earliest possible card at 1/hour at minute 42, so a
        // 30-minute session could never produce one. The port keeps the fix, so the first card at
        // 1/hour lands somewhere in [0, 60) rather than around 60.
        Assert.Equal(0.0, LockCardSchedule.FirstCardDelayMinutes(1, null, 0.0));
        Assert.Equal(30.0, LockCardSchedule.FirstCardDelayMinutes(1, null, 0.5));
        Assert.Equal(6.0, LockCardSchedule.FirstCardDelayMinutes(10, null, 1.0));

        // The window clamp (:164-165), kept even though the port always passes null: 0.8 of the
        // caller's remaining minutes, leaving the tail free so the card can be completed.
        Assert.Equal(8.0, LockCardSchedule.FirstCardDelayMinutes(1, 10.0, 1.0));
        Assert.Equal(60.0, LockCardSchedule.FirstCardDelayMinutes(1, 1000.0, 1.0));

        // The floor at :161: a frequency below 1 is treated as 1, never as a division by zero.
        Assert.Equal(60.0, LockCardSchedule.FirstCardDelayMinutes(0, null, 1.0));
        Assert.Equal(60.0, LockCardSchedule.FirstCardDelayMinutes(-5, null, 1.0));
    }

    [Fact]
    public void EveryCardAfterTheFirstIsSpacedAtSixtyOverTheDialPlusOrMinusThirtyPercent()
    {
        // LockCardService.cs:127-134, verbatim: interval = 60/perHour, min = 0.7x, max = 1.3x, and
        // the draw is roll * (max - min) + min.
        Assert.Equal(TimeSpan.FromMinutes(21), LockCardSchedule.Interval(2, 0.0));
        Assert.Equal(TimeSpan.FromMinutes(39), LockCardSchedule.Interval(2, 1.0));
        Assert.Equal(TimeSpan.FromMinutes(30), LockCardSchedule.Interval(2, 0.5));

        // The same Math.Max(1, ...) floor as the first card's.
        Assert.Equal(TimeSpan.FromMinutes(42), LockCardSchedule.Interval(0, 0.0));
    }

    [Fact]
    public void TheDialsCarryUpstreamsClampsAndUpstreamsDefaults()
    {
        // CCP.Core/Models/AppSettings.cs:3338 (default 2, clamp 1..10), :3345 (default 3, clamp
        // 1..10), :3331 (ships off) and :3371-3377 (the five built-in phrases, in order).
        var document = new LockCardPresetDocument();

        Assert.False(document.Enabled);
        Assert.Equal(2, document.PerHour);
        Assert.Equal(3, document.Repeats);
        Assert.False(document.Strict);

        document.PerHour = 99;
        Assert.Equal(LockCardSchedule.MaxPerHour, document.PerHour);
        document.PerHour = -4;
        Assert.Equal(LockCardSchedule.MinPerHour, document.PerHour);
        document.Repeats = 99;
        Assert.Equal(LockCardSchedule.MaxRepeats, document.Repeats);
        document.Repeats = 0;
        Assert.Equal(LockCardSchedule.MinRepeats, document.Repeats);

        Assert.Equal(
            ["GOOD GIRLS OBEY", "I LOVE BEING PROGRAMMED", "BAMBI SLEEP", "DROP FOR ME", "EMPTY AND OBEDIENT"],
            document.EnabledPhrases());

        // Upstream null-coalesces the pool on set (AppSettings.cs:3382); a null there would make
        // every later draw throw instead of showing no card.
        document.Phrases = null!;
        Assert.Empty(document.EnabledPhrases());
    }

    // =================================================================================
    //  the typing rules — the half of upstream's anti-cheat that is SEMANTIC
    // =================================================================================

    [Fact]
    public void ARepeatOnlyCountsOnceTheUserHasProducedAsManyCharactersAsThePhraseIsLong()
    {
        // LockCardWindow.xaml.cs:272. This is the rule that survives the port: the port's card has
        // no edit control, so there is no paste route to close, but a full phrase appearing with no
        // keystrokes behind it must still be refused.
        Assert.True(LockCardTyping.HasTypedEnough(5, 5));
        Assert.True(LockCardTyping.HasTypedEnough(6, 5));
        Assert.False(LockCardTyping.HasTypedEnough(4, 5));
        Assert.False(LockCardTyping.HasTypedEnough(0, 5));
    }

    [Fact]
    public void TheMatchAndTheMistakeCountAreBothCaseInsensitive_AndTheMatchTrims()
    {
        // LockCardWindow.xaml.cs:730 (the prefix comparison) and :740 (the completion test). Both
        // are OrdinalIgnoreCase and the completion one trims — a user who typed a trailing space is
        // not made to delete it.
        Assert.True(LockCardTyping.Matches("good girls obey", "GOOD GIRLS OBEY"));
        Assert.True(LockCardTyping.Matches("  GOOD GIRLS OBEY  ", "GOOD GIRLS OBEY"));
        Assert.False(LockCardTyping.Matches("GOOD GIRLS", "GOOD GIRLS OBEY"));

        Assert.True(LockCardTyping.IsPrefixOf(string.Empty, "GOOD GIRLS OBEY"));
        Assert.True(LockCardTyping.IsPrefixOf("good", "GOOD GIRLS OBEY"));
        Assert.False(LockCardTyping.IsPrefixOf("bad", "GOOD GIRLS OBEY"));

        // Past the end of the phrase is a mistake, not a crash: upstream's Math.Min bounds the
        // substring and the comparison then fails (:729-731).
        Assert.False(LockCardTyping.IsPrefixOf("GOOD GIRLS OBEYY", "GOOD GIRLS OBEY"));
    }

    [Fact]
    public void EscapeAlwaysClosesACardInThisBuild_BecauseUpstreamsOwnRuleFallsOPENWithoutAPanicHook()
    {
        // LockCardWindow.xaml.cs:632 — !strict || !panicEscapeIsLive — with upstream's reason at
        // :610-622: a low-level keyboard hook can be silently un-registered by Windows, so strict
        // mode must never become an inescapable trap and "every failure mode here has to fall open,
        // not shut."
        //
        // THE PORT HAS NO PANIC HOOK AT ALL, so the second clause is always true and every card
        // here closes on Escape. That is upstream's rule applied to this build's facts, not a
        // weakening of strict mode — and it is what stops this packet from shipping a window that
        // takes the keyboard and cannot be escaped. Both arms are pinned so a later build that grows
        // a panic hook has the real rule already here.
        Assert.True(LockCardTyping.EscapeClosesCard(strict: false, panicEscapeIsLive: false));
        Assert.True(LockCardTyping.EscapeClosesCard(strict: false, panicEscapeIsLive: true));
        Assert.True(LockCardTyping.EscapeClosesCard(strict: true, panicEscapeIsLive: false));
        Assert.False(LockCardTyping.EscapeClosesCard(strict: true, panicEscapeIsLive: true));
    }

    [Fact]
    public void TypingThePhraseBanksARepeat_AndTheCreditIsEarnedAgainFromScratchForTheNextOne()
    {
        var attempt = new LockCardAttempt("OBEY", requiredRepeats: 2, strict: false);

        Assert.Equal("1 of 2", attempt.Progress);
        Assert.Equal(LockCardStep.Typed, Type(attempt, "OBE"));
        Assert.Equal(3, attempt.Keystrokes);
        Assert.Equal("OBE", attempt.Answer);

        Assert.Equal(LockCardStep.RepeatAccepted, Type(attempt, "Y"));
        Assert.Equal(1, attempt.CompletedRepeats);
        Assert.Equal(string.Empty, attempt.Answer);
        Assert.Equal(0, attempt.Keystrokes);
        Assert.Equal("2 of 2", attempt.Progress);
        Assert.False(attempt.Solved);

        Assert.Equal(LockCardStep.Solved, Type(attempt, "obey"));
        Assert.True(attempt.Solved);
        Assert.Equal(2, attempt.CompletedRepeats);
        Assert.Equal(0, attempt.Mistakes);
    }

    [Fact]
    public void TheUntypedMatchGateIsUpstreamsAndItIsKept_ButInTHISPortItCanNeverFire_WhichIsSaidRatherThanHidden()
    {
        // A FINDING, pinned rather than papered over. Upstream's gate (LockCardWindow.xaml.cs:743-758)
        // exists because its card is a WPF TextBox, which has a clipboard, an undo stack, a context
        // menu and IME bulk commits — every one of which can fill the box without keystrokes.
        //
        // THIS PORT'S CARD HAS NO EDIT CONTROL. Its only content path is one WM_CHAR at a time, and
        // that path credits exactly one keystroke per appended character, so at the instant the
        // answer equals the phrase the credit is already at least the phrase's length. The gate
        // therefore cannot fire here, and the structural reason is what this fact pins: credit never
        // falls BELOW the answer's length, and backspace pushes it further above.
        //
        // It is kept anyway, and that is a judgement rather than an oversight: it is the rule itself,
        // written where a future keystroke source that appends without crediting would meet it. The
        // predicate is pinned in both directions by the fact above, so it cannot be quietly deleted.
        var attempt = new LockCardAttempt("OBEY", requiredRepeats: 1, strict: false);

        Type(attempt, "OB");
        Assert.True(attempt.Keystrokes >= attempt.Answer.Length,
            "credit fell below the answer's own length, which is the only way an untyped match could exist here");

        attempt.Apply('\0', isCharacter: false, isBackspace: true, isCancel: false);
        Assert.True(attempt.Keystrokes > attempt.Answer.Length,
            "backspace must push credit ABOVE the answer's length, never reduce it");

        Type(attempt, "BEY");
        Assert.True(attempt.Solved);
        Assert.Equal(0, attempt.RefusedUntypedMatches);
    }

    [Fact]
    public void BackspaceEditsTheBoxAndEarnsNoCredit_WhichIsUpstreamsOwnAccounting()
    {
        // Upstream's keystroke counter only ever counts GROWTH (CreditFailSafeGrowth, :289-290) and
        // its PreviewTextInput never fires for backspace. So deleting does not un-type: a user who
        // types four characters, deletes two and retypes them has typed six.
        var attempt = new LockCardAttempt("OBEY", requiredRepeats: 1, strict: false);

        Type(attempt, "OBEX");
        Assert.Equal(4, attempt.Keystrokes);
        Assert.Equal(1, attempt.Mistakes);

        Assert.Equal(LockCardStep.Typed, attempt.Apply('\0', false, true, false));
        Assert.Equal("OBE", attempt.Answer);
        Assert.Equal(4, attempt.Keystrokes);

        // And backspace on an empty box does nothing at all rather than throwing.
        var empty = new LockCardAttempt("OBEY", 1, false);
        Assert.Equal(LockCardStep.Ignored, empty.Apply('\0', false, true, false));
    }

    [Fact]
    public void AMistakeIsCountedOncePerKeystrokeThatLeavesThePhrasesPrefix()
    {
        // LockCardWindow.xaml.cs:727-733 counts a mistake on every changed box that is not a prefix,
        // which means a user who keeps typing while wrong keeps accruing them. Ported as-is.
        var attempt = new LockCardAttempt("OBEY", requiredRepeats: 1, strict: false);

        Type(attempt, "OB");
        Assert.Equal(0, attempt.Mistakes);
        Type(attempt, "XY");
        Assert.Equal(2, attempt.Mistakes);
    }

    [Fact]
    public void AKeyThatProducedNoCharacterIsNotTyping_AndACancelIsNeverTyping()
    {
        var attempt = new LockCardAttempt("OBEY", requiredRepeats: 1, strict: false);

        Assert.Equal(LockCardStep.Ignored, attempt.Apply('\0', false, false, false));
        Assert.Equal(0, attempt.Keystrokes);
        Assert.Equal(string.Empty, attempt.Answer);

        Assert.Equal(LockCardStep.Cancelled, attempt.Apply('\0', false, false, true));
        Assert.Equal(0, attempt.Keystrokes);
    }

    // =================================================================================
    //  the phrase pool — upstream's rotation, including both of its fallbacks
    // =================================================================================

    [Fact]
    public async Task ThePoolNeverShowsTheSamePhraseTwiceRunning_AndNeverStarvesOnASmallPool()
    {
        // LockCardService.cs:324-346. Both fallbacks are the point: a filter that emptied the pool
        // would loop forever, and a single enabled phrase would be filtered out on every draw.
        await using var rig = await Rig.StartAsync();
        rig.SetPhrases("A", "B", "C", "D");
        var pool = new LockCardPhrasePool(rig.Session.LockCardPreset, new SequenceRandom([0, 0, 0, 0, 0, 0, 0, 0]));

        var drawn = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            drawn.Add(pool.Draw()!);
        }

        Assert.All(drawn, d => Assert.Contains(d, new[] { "A", "B", "C", "D" }));
        for (var i = 1; i < drawn.Count; i++)
        {
            Assert.NotEqual(drawn[i - 1], drawn[i]);
        }

        // A LONE phrase is never tracked (:337), or the filter would empty on every draw and the
        // module would go silent.
        rig.SetPhrases("ONLY");
        var lone = new LockCardPhrasePool(rig.Session.LockCardPreset, new SequenceRandom([0, 0, 0]));
        Assert.Equal("ONLY", lone.Draw());
        Assert.Equal("ONLY", lone.Draw());
        Assert.Equal("ONLY", lone.Draw());
    }

    [Fact]
    public async Task AnEmptyPoolDrawsNothing_RatherThanThrowingOrInventingAPhrase()
    {
        await using var rig = await Rig.StartAsync();
        rig.SetPhrases();
        var pool = new LockCardPhrasePool(rig.Session.LockCardPreset);

        Assert.Equal(0, pool.ActiveCount);
        Assert.Null(pool.Draw());
    }

    // =================================================================================
    //  the module: one card, and BOTH ways it can end
    // =================================================================================

    [Fact]
    public async Task ADueCardIsPutInFrontOfTheUser_AndTypingItOutInFullResolvesItAsSOLVED()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableLockCard(perHour: 10, repeats: 2);
        rig.Engine.Start();

        Assert.Equal(EffectDotState.Live, rig.LockCard.Dot);
        Assert.IsType<CapabilityState.Available>(rig.Engine.ArmOutcomes[LockCardEffect.EffectId]);

        rig.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(1, rig.LockCard.CardCount);
        var prompt = Assert.Single(rig.Input.Prompts);
        Assert.Equal("GOOD GIRLS OBEY", prompt.Content.Question);
        Assert.Equal("1 of 2", prompt.Content.Progress);
        Assert.Equal(LockCardResolution.None, rig.LockCard.LastResolution);
        Assert.True(rig.Input.IsPrompting);

        // The user types it. Twice.
        rig.Input.TypeCharacters("GOOD GIRLS OBEY");
        Assert.Equal(1, rig.LockCard.Attempt!.CompletedRepeats);
        rig.Input.TypeCharacters("good girls obey");

        Assert.Equal(LockCardResolution.Solved, rig.LockCard.LastResolution);
        Assert.Equal(1, rig.LockCard.SolvedCount);
        Assert.Equal(0, rig.LockCard.DismissedCount);
        Assert.Null(rig.LockCard.Attempt);
        Assert.False(rig.Input.IsPrompting);
        Assert.Equal(1, rig.Input.Dismissals);
        Assert.Contains(LockCardResolution.Solved, rig.Resolutions);
    }

    [Fact]
    public async Task PressingEscapeResolvesTheCardAsDISMISSED_WhichIsTheOtherHalfOfAskingSomething()
    {
        // "Handles both an answer and no answer" is the packet's own criterion, and this is the no
        // answer the user CHOSE to give. It is not a failure: upstream treats Esc as the ordinary
        // dismiss on every non-strict card (LockCardWindow.xaml.cs:655-663).
        await using var rig = await Rig.StartAsync();
        rig.EnableLockCard(perHour: 10);
        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromHours(1));

        rig.Input.TypeCharacters("GOOD");
        rig.Input.PressEscape();

        Assert.Equal(LockCardResolution.Dismissed, rig.LockCard.LastResolution);
        Assert.Equal(0, rig.LockCard.SolvedCount);
        Assert.Equal(1, rig.LockCard.DismissedCount);
        Assert.Null(rig.LockCard.Attempt);
        Assert.Equal(1, rig.Input.Dismissals);
    }

    [Fact]
    public async Task StoppingTheSessionWithACardUp_TakesItDownAndResolvesItAsWITHDRAWN()
    {
        // The third ending, and the one the user never chose: the session stopped under them.
        // Upstream's panic and stop paths force-close every visible card
        // (MainWindow/MainWindow.StartStop.cs:367, LockCardService.cs:107-112) precisely so a card
        // cannot outlive the thing that produced it.
        await using var rig = await Rig.StartAsync();
        rig.EnableLockCard(perHour: 10);
        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromHours(1));
        Assert.True(rig.Input.IsPrompting);

        rig.Engine.Stop();

        Assert.Equal(LockCardResolution.Withdrawn, rig.LockCard.LastResolution);
        Assert.False(rig.Input.IsPrompting);
        Assert.Equal(1, rig.Input.Dismissals);
        Assert.Equal(EffectDotState.Armed, rig.LockCard.Dot);
    }

    [Fact]
    public async Task ACardTheOperatingSystemWouldNotFocus_IsTakenStraightBackDownRatherThanLeftOnScreen()
    {
        // The safety property. A window that holds the user's screen, answers nothing and cannot be
        // dismissed is strictly worse than no window, which is why upstream's own error path
        // force-closes every card it may have half-shown (LockCardService.cs:305-313).
        await using var rig = await Rig.StartAsync();
        rig.EnableLockCard(perHour: 10);
        rig.Input.NextPromptOutcome = new CapabilityState.Unavailable(new CapabilityReason(
            InputReasonCodes.InputNotCaptured, "double: the OS refused the foreground"));
        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Single(rig.Input.Prompts);
        Assert.Equal(LockCardResolution.Refused, rig.LockCard.LastResolution);
        Assert.Null(rig.LockCard.Attempt);
        Assert.Empty(rig.Shown);
        Assert.Contains(LockCardResolution.Refused, rig.Resolutions);
        Assert.IsType<CapabilityState.Unavailable>(rig.LockCard.LastPrompt);
        Assert.Equal(1, rig.Input.Dismissals);
        Assert.False(rig.Input.IsPrompting);
    }

    [Fact]
    public async Task ACardThatIsFocusedAndBLANK_IsAlsoTakenBackDown_BecauseAQuestionNobodyCanReadIsNotAQuestion()
    {
        // THE HARMFUL CASE, and the one a code review caught this packet shipping. Degraded is NOT
        // Unavailable: the OS confirmed the card holds the foreground and the keyboard focus, and the
        // presence therefore leaves the window UP — only the ink read-back said no. The module has to
        // take it down itself.
        //
        // What the missing Dismiss produced: a topmost, blank window holding the user's keyboard,
        // with Resolve having cleared _attempt so OnKeystroke discards every key INCLUDING ESCAPE,
        // and Compose's already-prompting guard then dropping every future card for the rest of the
        // session while the dot still read Live. The two assertions the fact was missing are the two
        // that see it.
        await using var rig = await Rig.StartAsync();
        rig.EnableLockCard(perHour: 10);
        rig.Input.NextPromptOutcome = new CapabilityState.Degraded(
            "the card holds the keyboard",
            new CapabilityReason(InputReasonCodes.InputPromptNotInked, "double: no ink"));
        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(LockCardResolution.Refused, rig.LockCard.LastResolution);
        Assert.Empty(rig.Shown);
        Assert.Equal(1, rig.Input.Dismissals);
        Assert.False(rig.Input.IsPrompting,
            "a Degraded card was left on screen. It is topmost, blank, holds the user's keyboard, and every key "
            + "they press — Escape included — is discarded by a module that has already stopped tracking it");
        Assert.Null(rig.LockCard.Attempt);

        // AND THE SESSION IS NOT POISONED. Compose refuses while a card is up, so a card left behind
        // would silently end the module for the rest of the run.
        rig.Input.NextPromptOutcome = null;
        rig.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(2, rig.Input.Prompts.Count);
        Assert.Equal(LockCardResolution.None, rig.LockCard.LastResolution);
        Assert.NotNull(rig.LockCard.Attempt);

        // TWO, not one: CardCount is cards this module ASKED FOR, and it asked twice — the first
        // answer just came back blank. What the user did with them is LastResolution's business, and
        // the assertions above are where that is checked.
        Assert.Equal(2, rig.LockCard.CardCount);
    }

    [Fact]
    public async Task ASecondCardComingDueWhileOneIsUp_IsDropped_WhichIsUpstreamsOwnAnswerWithNoQueue()
    {
        // ResolveBlockedCardAction(cardAlreadyOpen: true, isDeferredReplay: false,
        // hasInteractionQueue: false) => DropNoQueue (LockCardService.cs:193-199). The port has no
        // interaction queue, permanently, so that is the branch it takes — and it is upstream's own
        // answer for that input rather than a port simplification.
        await using var rig = await Rig.StartAsync();
        rig.EnableLockCard(perHour: 10);
        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Single(rig.Input.Prompts);

        rig.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Single(rig.Input.Prompts);
        Assert.Equal(1, rig.LockCard.CardCount);
        Assert.True(rig.Input.IsPrompting);
    }

    [Fact]
    public async Task AFiringThatShowedNothing_ReArmsAtTheORDINARYInterval_NotAtTheFirstCardOffset()
    {
        // A REGRESSION PIN FOR A BUG THIS FILE FOUND BY HANGING. The first-card offset is
        // roll * interval (LockCardService.cs:167) and can legitimately be ZERO. The first draft
        // chose the offset whenever nothing had fired yet, so a firing that produced nothing — an
        // empty pool, a card already up, a desktop that cannot take the input — re-armed at zero and
        // re-fired immediately, forever. Upstream cannot have it: its offset is computed once in
        // Start (:74) while every tick recomputes the ±30% interval (:127-134).
        //
        // Before the fix this advance never returned. It is the whole assertion; the counts below
        // only confirm the loop did the work rather than being skipped.
        await using var rig = await Rig.StartAsync();
        rig.EnableLockCard(perHour: 10);
        rig.SetPhrases();

        rig.Clock.Advance(TimeSpan.Zero);
        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, rig.LockCard.CardCount);
        Assert.Empty(rig.Input.Prompts);
        Assert.True(rig.LockCard.ScheduleArmed,
            "the schedule must survive an unproductive firing — upstream's timer keeps running when a tick shows "
            + "nothing");
    }

    [Fact]
    public async Task ARE_ArmedRunGetsItsFirstCardOffsetBACK_BecauseEveryRunIsANewFirstCard()
    {
        // The other half of the schedule counter, and it had no coverage until a review said so:
        // deleting the reset in OnDisarmed reddened nothing. Upstream recomputes the first-card
        // offset in Start() EVERY time (LockCardService.cs:74), so a second run of a session is a
        // first card again — not a continuation of the previous run's spacing.
        //
        // Measured through the CLOCK rather than through a field: with the roll pinned at 0 the
        // offset is zero minutes, so a run that gets it fires a card immediately, and a run that
        // does not waits the ordinary 4.2-minute interval. The second Start must behave like the
        // first.
        await using var rig = await Rig.StartAsync();
        rig.EnableLockCard(perHour: 10);

        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.Zero);
        Assert.Single(rig.Input.Prompts);

        rig.Engine.Stop();
        Assert.False(rig.Input.IsPrompting);

        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.Zero);

        Assert.Equal(2, rig.Input.Prompts.Count);
        Assert.Equal(2, rig.LockCard.CardCount);
    }

    [Fact]
    public async Task EachKeystrokeRepaintsTheCard_SoTheProgressAndTheEchoAreWhatTheUserJustDid()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableLockCard(perHour: 10, repeats: 2);
        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromHours(1));

        rig.Input.TypeCharacters("GOO");

        Assert.Equal(3, rig.Input.Updates.Count);
        Assert.Equal("GOO", rig.Input.Updates[^1].Answer);
        Assert.Equal("1 of 2", rig.Input.Updates[^1].Progress);
        Assert.Equal("GOOD GIRLS OBEY", rig.Input.Updates[^1].Question);
        Assert.Equal("Press Esc to close", rig.Input.Updates[^1].Hint);
    }

    // =================================================================================
    //  THE DOT — the sixth meaning, and all three clauses moved independently
    // =================================================================================

    [Fact]
    public async Task WithNoDesktopToAskOn_TheModuleIsARMED_NotLive_EvenWithAFiringOnTheClock()
    {
        // Clause 2. This is the Linux case and the session-0 case: the schedule is perfectly armed
        // and not one card can ever be shown, so a lit dot would claim the module is waiting for an
        // answer from a user it has no way to reach.
        await using var rig = await Rig.StartAsync(canReachAUser: false);
        rig.EnableLockCard(perHour: 10);

        rig.Engine.Start();

        Assert.True(rig.LockCard.ScheduleArmed,
            "the first clause must still hold, or this fact would be pinning the wrong thing: the point is that a "
            + "firing IS on the clock and the dot is dark anyway");
        Assert.Equal(EffectDotState.Armed, rig.LockCard.Dot);

        var outcome = Assert.IsType<CapabilityState.Unavailable>(rig.Engine.ArmOutcomes[LockCardEffect.EffectId]);
        Assert.Equal(EffectReasonCodes.InputCaptureUnavailable, outcome.Reason.Code);

        // AND NOTHING IS EVER SHOWN. Found by mutation: the arm result alone left Compose free to
        // draw a phrase and put a card up on a desktop that cannot take input, which is upstream's
        // "endpoint down — stay quiet, don't spin" (MindWipeService.cs:771) transposed to this
        // medium. The schedule keeps running so a session that regains a desktop is picked up at the
        // next card rather than at the next session.
        rig.Clock.Advance(TimeSpan.FromHours(2));

        Assert.Empty(rig.Input.Prompts);
        Assert.Equal(0, rig.LockCard.CardCount);
        Assert.True(rig.LockCard.ScheduleArmed,
            "the schedule must survive a firing that could not ask anything");
    }

    [Fact]
    public async Task WithACardUpThatTheOsHasTakenTheKeyboardFrom_TheModuleIsARMED_NotLive()
    {
        // Clause 3, and it is the one this whole packet is about. A card can be visible, topmost and
        // hit-testable while the operating system routes the keyboard somewhere else entirely —
        // measured, not imagined (SP-110 plan.md §0, run 1). Nobody is being asked anything, and the
        // dot must say so.
        await using var rig = await Rig.StartAsync();
        rig.EnableLockCard(perHour: 10);
        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(EffectDotState.Live, rig.LockCard.Dot);

        rig.Input.HoldsTheInput = false;

        Assert.True(rig.LockCard.ScheduleArmed);
        Assert.True(rig.Input.IsPrompting);
        Assert.Equal(EffectDotState.Armed, rig.LockCard.Dot);
    }

    [Fact]
    public async Task WithNoCardUpAtAll_TheModuleIsLIVE_BecauseWaitingForTheNextOneIsTheWork()
    {
        // The other side of clause 3, and it is why the clause is a DISJUNCTION rather than a plain
        // conjunct: for almost all of a session no card is up, and a dot that went dark then would be
        // dark essentially always.
        await using var rig = await Rig.StartAsync();
        rig.EnableLockCard(perHour: 10);
        rig.Input.HoldsTheInput = false;

        rig.Engine.Start();

        Assert.False(rig.Input.IsPrompting);
        Assert.Equal(EffectDotState.Live, rig.LockCard.Dot);
    }

    [Fact]
    public async Task WhileTheCardIsUpAndUnansweredTheModuleIsLIVE_BecauseWaitingForAHumanIsTheWork()
    {
        // The packet's direct question, answered directly. A module that went Armed the moment it
        // did its job would be the under-claim SP-109 refused for the audio half-row.
        await using var rig = await Rig.StartAsync();
        rig.EnableLockCard(perHour: 10);
        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromHours(1));

        Assert.True(rig.Input.IsPrompting);
        Assert.Equal(LockCardResolution.None, rig.LockCard.LastResolution);
        Assert.Equal(EffectDotState.Live, rig.LockCard.Dot);
    }

    [Fact]
    public async Task WithNoFiringOnTheClock_TheModuleIsARMED_HoweverHealthyEverythingElseIs()
    {
        // Clause 1, the inherited one. Pinned here as well as in the paced base's own facts, because
        // the override could have dropped it — and the base's contract says an override may only
        // NARROW, never widen.
        await using var rig = await Rig.StartAsync();
        rig.EnableLockCard(perHour: 10);
        rig.Engine.Start();
        Assert.Equal(EffectDotState.Live, rig.LockCard.Dot);

        rig.Engine.Stop();

        Assert.False(rig.LockCard.ScheduleArmed);
        Assert.True(rig.Input.CanReachAUser);
        Assert.Equal(EffectDotState.Armed, rig.LockCard.Dot);
    }

    [Fact]
    public async Task AnEmptyPhrasePoolIsDegradedButSTILLLive_TheSubliminalsAnswerNotThePinkFilterAnswer()
    {
        // The line between a missing CHANNEL and missing CONTENT, drawn here exactly where SP-109
        // drew it for a clip folder: enabling a phrase mid-session starts producing cards with no
        // re-arm, so the module really is running and the dot really is honest.
        await using var rig = await Rig.StartAsync();
        rig.EnableLockCard(perHour: 10);
        rig.SetPhrases();

        rig.Engine.Start();

        var outcome = Assert.IsType<CapabilityState.Degraded>(rig.Engine.ArmOutcomes[LockCardEffect.EffectId]);
        Assert.Equal(EffectReasonCodes.InputNoPhrase, outcome.Reason.Code);
        Assert.Contains(LockCardPresetDocument.FileName, outcome.Reason.Detail, StringComparison.Ordinal);
        Assert.Equal(EffectDotState.Live, rig.LockCard.Dot);

        rig.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Empty(rig.Input.Prompts);
        Assert.Equal(0, rig.LockCard.CardCount);
    }

    [Fact]
    public async Task ADisabledModuleIsOFF_AndNoCardIsEverShown()
    {
        await using var rig = await Rig.StartAsync();

        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(EffectDotState.Off, rig.LockCard.Dot);
        Assert.Empty(rig.Input.Prompts);
        var outcome = Assert.IsType<CapabilityState.Unavailable>(rig.Engine.ArmOutcomes[LockCardEffect.EffectId]);
        Assert.Equal(EffectReasonCodes.EffectDialOff, outcome.Reason.Code);
    }

    // =================================================================================
    //  the dials, and what each one is allowed to change
    // =================================================================================

    [Fact]
    public async Task TheRepeatsDialChangesTheNEXTCard_NeverTheOneOnScreen()
    {
        // Upstream reads the repeat count once, at show time (LockCardService.cs:294). A card whose
        // target moved under the user mid-typing would be the cruellest live-apply on the page.
        await using var rig = await Rig.StartAsync();
        rig.EnableLockCard(perHour: 10, repeats: 1);
        rig.Engine.Start();
        rig.Clock.Advance(TimeSpan.FromHours(1));

        rig.LockCard.SetRepeats(5);

        Assert.Equal(1, rig.LockCard.Attempt!.RequiredRepeats);
        rig.Input.TypeCharacters("GOOD GIRLS OBEY");
        Assert.Equal(LockCardResolution.Solved, rig.LockCard.LastResolution);

        rig.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(5, rig.LockCard.Attempt!.RequiredRepeats);
    }

    [Fact]
    public async Task TheDialsWriteThroughTheModuleAndClampLikeUpstream()
    {
        await using var rig = await Rig.StartAsync();

        rig.LockCard.SetPerHour(999);
        Assert.Equal(LockCardSchedule.MaxPerHour, rig.LockCard.Preset.PerHour);
        rig.LockCard.SetRepeats(-3);
        Assert.Equal(LockCardSchedule.MinRepeats, rig.LockCard.Preset.Repeats);
        rig.LockCard.SetStrict(true);
        Assert.True(rig.LockCard.Preset.Strict);
        rig.LockCard.SetEnabled(true);
        Assert.True(rig.LockCard.Enabled);
    }

    // =================================================================================
    //  helpers
    // =================================================================================

    private static LockCardStep Type(LockCardAttempt attempt, string text)
    {
        var step = LockCardStep.Ignored;
        foreach (var c in text)
        {
            step = attempt.Apply(c, isCharacter: true, isBackspace: false, isCancel: false);
        }

        return step;
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _directory;

        private Rig(
            ApplicationHost host, SessionParticipant session, ManualSessionClock clock,
            RecordingInputPresence input, string directory)
        {
            Host = host;
            Session = session;
            Clock = clock;
            Input = input;
            _directory = directory;
            session.LockCard.Shown += e => Shown.Add(e);
            session.LockCard.Resolved += r => Resolutions.Add(r);
        }

        public ApplicationHost Host { get; }

        public SessionParticipant Session { get; }

        public ManualSessionClock Clock { get; }

        public RecordingInputPresence Input { get; }

        public List<LockCardEvent> Shown { get; } = [];

        public List<LockCardResolution> Resolutions { get; } = [];

        public SessionEngine Engine => Session.Engine;

        public LockCardEffect LockCard => Session.LockCard;

        public static async Task<Rig> StartAsync(bool canReachAUser = true)
        {
            var dir = Path.Combine(Path.GetTempPath(), "ccp-sp110m-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var spirals = SpiralLibrary.Folder(SessionParticipant.AssetsRootFor(dir));
            Directory.CreateDirectory(spirals);
            File.WriteAllBytes(Path.Combine(spirals, "classic.gif"), [0x00]);

            var registry = new OperationRegistry();
            var log = new NullSink();
            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());
            var infra = new ParticipantInfrastructure(registry, boundary, log);
            var clock = new ManualSessionClock();
            var input = new RecordingInputPresence(canReachAUser);

            var session = new SessionParticipant(
                infra, dir, clock, new StubPool(3), new NullFlashSurface(), new NullSubliminalSurface(),
                static () => true, new NullPinkFilterSurface(), new NullSpiralSurface(),
                new NullAudioPresence(), new StubCuePool(), new StubCuePool(), null, null,
                input,
                lockCardPhrases: null,
                // The SPACING is what a fact makes deterministic; the arithmetic it feeds stays the
                // module's own, so no double here re-derives an interval.
                lockCardRandom: new SequenceRandom([0.0]),
                lockCardPlacement: () => new InputBounds(0, 0, 400, 300));

            var host = new ApplicationHost(log, [session], new StartupTrace(), registry, infra.UiDispatch);
            Assert.IsType<StartupOutcome.Success>(
                await host.StartParticipantsAsync(TestContext.Current.CancellationToken));

            return new Rig(host, session, clock, input, dir);
        }

        public void EnableLockCard(int perHour = LockCardSchedule.DefaultPerHour, int repeats = 1) =>
            Session.LockCardPreset.Mutate(p =>
            {
                p.Enabled = true;
                p.PerHour = perHour;
                p.Repeats = repeats;
                p.Phrases = new Dictionary<string, bool>(StringComparer.Ordinal) { ["GOOD GIRLS OBEY"] = true };
            });

        public void SetPhrases(params string[] phrases) =>
            Session.LockCardPreset.Mutate(p =>
                p.Phrases = phrases.ToDictionary(x => x, _ => true, StringComparer.Ordinal));

        public async ValueTask DisposeAsync()
        {
            await Host.ShutdownAsync();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// An input presence that records what it was asked to do and never touches a window.
    ///
    /// <para><b>It deliberately decides nothing the product should decide.</b> It reports whatever
    /// station and focus state the fact asked for, hands back whatever outcome the fact set, and
    /// delivers exactly the keystrokes the fact injects — it never re-derives a clamp, never turns a
    /// prompt into a success on its own, and never invents a keystroke. A double that made those
    /// decisions would be the fake dial this port has already been caught by twice.</para>
    /// </summary>
    private sealed class RecordingInputPresence : IInputPresence
    {
        private Action<InputKeystroke>? _onKeystroke;

        public RecordingInputPresence(bool canReachAUser)
        {
            CanReachAUser = canReachAUser;
            HoldsTheInput = canReachAUser;
        }

        public List<InputPromptRequest> Prompts { get; } = [];

        public List<InputPromptContent> Updates { get; } = [];

        public int Dismissals { get; private set; }

        public CapabilityState? NextPromptOutcome { get; set; }

        public bool CanReachAUser { get; set; }

        public bool HoldsTheInput { get; set; }

        public bool IsPrompting { get; private set; }

        public CapabilityState? LastPrompt { get; private set; }

        public InputCaptureObservation LastObservation { get; private set; } = InputCaptureObservation.NotAsked;

        public CapabilityState Prompt(InputPromptRequest request)
        {
            Prompts.Add(request);
            var outcome = NextPromptOutcome
                ?? new CapabilityState.Available("recording presence: the OS gave the card the keyboard");

            // MIRRORS THE PRODUCT, and the difference is where a real user-harm bug lived. The real
            // presence sets its prompting flag from the OS's CONFIRMATION (Win32InputPresence:218,
            // `_prompting = observation.Confirmed`), and Confirmed does NOT include ink — so a card
            // the OS focused but that has nothing written on it comes back Degraded WITH THE WINDOW
            // STILL UP. A double that flipped the flag only on Available reported the opposite of the
            // product in exactly the state that traps the user, and no fact built on it could see the
            // card being left behind.
            if (outcome is not CapabilityState.Unavailable)
            {
                IsPrompting = true;
                _onKeystroke = request.OnKeystroke;
            }

            LastObservation = new InputCaptureObservation(
                true, 1, true, IsPrompting, request.Bounds, true, IsPrompting, IsPrompting,
                HitTestWinner: 1,
                InkedPixels: outcome is CapabilityState.Available ? 10 : 0,
                SampledPixels: 100,
                BackgroundHeld: true,
                KeystrokesSeen: 0);
            return LastPrompt = outcome;
        }

        public CapabilityState Update(InputPromptContent content)
        {
            Updates.Add(content);
            return new CapabilityState.Available("recording presence: repainted");
        }

        public CapabilityState Dismiss()
        {
            Dismissals++;
            IsPrompting = false;
            _onKeystroke = null;
            return new CapabilityState.Available("recording presence: dismissed");
        }

        public InputCaptureObservation Observe() => LastObservation;

        public InputStationObservation ObserveStation() =>
            new(true, CanReachAUser, CanReachAUser ? 1 : 0, CanReachAUser);

        public int Pump(int maxMessages) => 0;

        /// <summary>Deliver characters the way the OS would, one <c>WM_CHAR</c> at a time.</summary>
        public void TypeCharacters(string text)
        {
            foreach (var c in text)
            {
                _onKeystroke?.Invoke(new InputKeystroke(InputKeystrokeKind.Character, c, c));
            }
        }

        public void PressEscape() =>
            _onKeystroke?.Invoke(new InputKeystroke(InputKeystrokeKind.Cancel, '\0', 0x1B));

        public void Dispose()
        {
        }
    }

    /// <summary>A <see cref="Random"/> whose draws are a script, so a fact can say "the first card
    /// is due immediately" without pinning a seed or re-deriving an interval.</summary>
    private sealed class SequenceRandom(double[] values) : Random
    {
        private int _index;

        public override double NextDouble()
        {
            var value = values[_index % values.Length];
            _index++;
            return value;
        }

        public override int Next(int maxValue) => (int)(NextDouble() * maxValue) % Math.Max(1, maxValue);
    }

    // ---- the local doubles for the seven modules this file is NOT about ----

    private sealed class NullAudioPresence : IAudioPresence
    {
        public bool IsRendering => false;

        public CapabilityState? LastOpen => null;

        public AudioRenderObservation LastObservation => AudioRenderObservation.NotAsked;

        public CapabilityState Open() =>
            new CapabilityState.Unavailable(new CapabilityReason(AudioReasonCodes.NoRenderEndpoint, "null"));

        public CapabilityState Cue(AudioCue cue) =>
            new CapabilityState.Unavailable(new CapabilityReason(AudioReasonCodes.NoRenderEndpoint, "null"));

        public CapabilityState Silence(string slot) =>
            new CapabilityState.Unavailable(new CapabilityReason(AudioReasonCodes.NoRenderEndpoint, "null"));

        public AudioRenderObservation Observe() => AudioRenderObservation.NotAsked;

        public void Dispose()
        {
        }
    }

    private sealed class StubCuePool : IAudioCuePool
    {
        public int ActiveCount => 0;

        public string Folder => "none";

        public string? Draw() => null;
    }

    private sealed class NullFlashSurface : IFlashSurface
    {
        public CapabilityState? LastPlacement => null;

        public void Show(IReadOnlyList<string> images)
        {
        }

        public void HideAll()
        {
        }
    }

    private sealed class NullSubliminalSurface : ISubliminalSurface
    {
        public CapabilityState? LastPlacement => null;

        public void Show(SubliminalCard card)
        {
        }

        public void HideAll()
        {
        }
    }

    private sealed class NullPinkFilterSurface : IPinkFilterSurface
    {
        public bool Showing { get; private set; }

        public CapabilityState? LastPlacement { get; private set; }

        public CapabilityState Engage(PinkFilterTint tint)
        {
            Showing = true;
            return LastPlacement = new CapabilityState.Available("null surface: engaged");
        }

        public void Withdraw() => Showing = false;
    }

    private sealed class NullSpiralSurface : ISpiralSurface
    {
        public bool Showing { get; private set; }

        public bool Running => Showing;

        public bool Engaged => Showing;

        public int FrameCount => Showing ? 1 : 0;

        public CapabilityState? LastPlacement { get; private set; }

        public CapabilityState Engage(string spiralPath, SpiralPresentation presentation)
        {
            Showing = true;
            return LastPlacement = new CapabilityState.Available("null surface: engaged");
        }

        public void Withdraw() => Showing = false;
    }

    private sealed class StubPool(int population) : IFlashImagePool
    {
        public IReadOnlyList<string> Draw(int count) =>
            [.. Enumerable.Range(0, Math.Min(count, population)).Select(i => $"image-{i}.png")];
    }

    private sealed class InlineDispatch : IUiDispatch
    {
        public void Post(Action action) => action();
    }

    private sealed class NullSink : ILogSink
    {
        public void Log(string message)
        {
        }
    }

    /// <summary>The manual clock, SP-098's shape. Zero wall-clock.</summary>
    private sealed class ManualSessionClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            lock (_timers)
            {
                _timers.Add(entry);
            }

            return new Handle(this, entry);
        }

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers.Where(t => t.Due <= UtcNow).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is null)
                    {
                        return;
                    }

                    _timers.Remove(next);
                }

                next.Fire();
            }
        }

        private void Cancel(Entry entry)
        {
            lock (_timers)
            {
                _timers.Remove(entry);
            }
        }

        private sealed class Entry
        {
            public DateTimeOffset Due { get; init; }

            public Action Fire { get; init; } = static () => { };
        }

        private sealed class Handle(ManualSessionClock clock, Entry entry) : IDisposable
        {
            public void Dispose() => clock.Cancel(entry);
        }
    }
}
