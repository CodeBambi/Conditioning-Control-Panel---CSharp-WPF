using System.Reflection;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Features.Progression;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>Pop Quiz</b> — the reinforcement questions, driven through a RECORDING input presence and a
/// manual clock, so the pacing, the draw, the shuffle, the key mapping, the two card delays, the XP
/// and every resolution path are decided deterministically with no window anywhere.
///
/// <para><b>What is deliberately NOT claimed here.</b> That a real window takes the operating
/// system's foreground and keyboard is <see cref="InputCapabilityTests"/>'s claim, made against the
/// OS with a synthesised keystroke and a negative control. That the card is LEGIBLE — that the
/// question and its four answers really fit the wrapped band the port draws them in — is a
/// presentation claim and is made NOWHERE yet: this module has no composition and no panel, so no
/// headed run has ever put it on a screen. Both are named in the packet report rather than implied
/// by a green suite.</para>
///
/// <para><b>Zero wall-clock.</b> Everything rides the one injected clock.</para>
/// </summary>
public class PopQuizModuleTests
{
    // =================================================================================
    //  the pure arithmetic — every constant is upstream's and every clamp is pinnable
    // =================================================================================

    [Fact]
    public void EveryQuestionIsSpacedAtSixtyOverTheDialPlusOrMinusThirtyPercent()
    {
        // Services/Quiz/PopQuizService.cs:113-122 and its recomputation at :163-171, verbatim:
        // interval = 60/perHour, min = 0.7x, max = 1.3x, and the draw is roll * (max - min) + min.
        Assert.Equal(TimeSpan.FromMinutes(21), PopQuizSchedule.Interval(2, 0.0));
        Assert.Equal(TimeSpan.FromMinutes(39), PopQuizSchedule.Interval(2, 1.0));
        Assert.Equal(TimeSpan.FromMinutes(30), PopQuizSchedule.Interval(2, 0.5));

        // One an hour and the top of the reachable dial, so the band is pinned at both ends of what
        // a user can actually set (GradedIntakeTabView.xaml:286, Minimum="1" Maximum="100").
        Assert.Equal(TimeSpan.FromMinutes(60), PopQuizSchedule.Interval(1, 0.5));
        Assert.Equal(TimeSpan.FromMinutes(0.6), PopQuizSchedule.Interval(100, 0.5));
    }

    [Fact]
    public void TheScheduleFloorsADialOfZeroAtOneRatherThanDividingByZero()
    {
        // THE PORT'S GUARD, NOT UPSTREAM'S. Upstream divides by the dial unguarded (:114, :164) and
        // survives only because AppSettings clamped it first; here Math.Max(1, ...) stops this
        // function depending on its caller's clamp — the case EffectSchedule.BaseIntervalSeconds
        // already argues. Without it these are TimeSpan.FromMinutes(infinity), which throws.
        Assert.Equal(TimeSpan.FromMinutes(60), PopQuizSchedule.Interval(0, 0.5));
        Assert.Equal(TimeSpan.FromMinutes(42), PopQuizSchedule.Interval(-3, 0.0));
    }

    [Fact]
    public void TheDialsCarryUpstreamsDefaultAndUpstreamsClamp_WhichIsAHundredAndNotTen()
    {
        // Models/AppSettings.cs:3575 (ships off), :3582 (default 2), :3586 (Math.Clamp(value, 1, 100)).
        var document = new PopQuizPresetDocument();

        Assert.False(document.Enabled);
        Assert.Equal(2, document.PerHour);

        // The stale comment one line above the clamp says "(1-10)". The clamp says a hundred and the
        // slider the user drags agrees (Views/Tabs/GradedIntakeTabView.xaml:286), so sixty is a
        // setting a shipping user can already have and a port that capped at ten would silently
        // refuse it.
        document.PerHour = 60;
        Assert.Equal(60, document.PerHour);

        document.PerHour = 250;
        Assert.Equal(PopQuizSchedule.MaxPerHour, document.PerHour);
        document.PerHour = 0;
        Assert.Equal(PopQuizSchedule.MinPerHour, document.PerHour);
    }

    // =================================================================================
    //  the question pool — upstream's twenty-five, and the rule that every answer is right
    // =================================================================================

    [Fact]
    public void ThePoolIsUpstreamsTwentyFiveQuestions_EachWithFourAnswersAndFourAffirmations()
    {
        // Services/Quiz/PopQuizService.cs:23-100.
        Assert.Equal(25, PopQuizQuestions.Pool.Count);
        Assert.Equal("How does obedience feel?", PopQuizQuestions.Pool[0].Text);
        Assert.Equal("Right now, your mind is...", PopQuizQuestions.Pool[24].Text);
        Assert.Equal("Welcome home.", PopQuizQuestions.Pool[0].Affirmations[3]);

        var malformed = PopQuizQuestions.Pool
            .Where(q => q.Answers.Count != PopQuizQuestion.AnswerCount
                || q.Affirmations.Count != PopQuizQuestion.AnswerCount
                || string.IsNullOrWhiteSpace(q.Text)
                || q.Answers.Any(string.IsNullOrWhiteSpace)
                || q.Affirmations.Any(string.IsNullOrWhiteSpace))
            .Select(q => q.Text)
            .ToList();
        Assert.Empty(malformed);
    }

    [Fact]
    public void EveryAnswerIsCorrect_ThereIsNoWrongOneAndNoScoreAnywhere()
    {
        // PopQuizService.cs:12 — "All answers are 'correct' — pure positive reinforcement." Every
        // slot of every question is pickable and every pick returns a reply; nothing anywhere reports
        // a right or a wrong one.
        var replies = new List<string>();
        foreach (var question in PopQuizQuestions.Pool)
        {
            for (var slot = 0; slot < PopQuizQuestion.AnswerCount; slot++)
            {
                var ask = new PopQuizAsk(question, new SequenceRandom([0.0]));
                Assert.Equal(PopQuizStep.Picked, Pick(ask, slot));
                replies.Add(Assert.IsType<string>(ask.Affirmation));
            }
        }

        Assert.Equal(25 * PopQuizQuestion.AnswerCount, replies.Count);
        Assert.DoesNotContain(replies, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void TheAnswersAreShuffledForDisplayAndTheAffirmationStillFollowsTheAnswerThatWasPicked()
    {
        // Windows/PopQuizWindow.xaml.cs:54-60 shuffles the four slots; :122-125 keeps the ORIGINAL
        // index on each slot; :171 looks the affirmation up by that index. Get it wrong and every
        // user of every question is answered about something they did not say.
        var question = PopQuizQuestions.Pool[2];
        var ask = new PopQuizAsk(question, new SequenceRandom([0.5]));

        // Fisher-Yates over [0,1,2,3] with Next(4)=2, Next(3)=1, Next(2)=1.
        Assert.Equal([0, 3, 1, 2], ask.Order);
        Assert.NotEqual(question.Answers, ask.Options);
        Assert.Equal(question.Answers[3], ask.Options[1]);

        Assert.Equal(PopQuizStep.Picked, Pick(ask, 1));
        Assert.Equal(1, ask.PickedSlot);
        Assert.Equal(question.Affirmations[3], ask.Affirmation);
    }

    [Fact]
    public void OnlyTheFourAnswerKeysPick_EscapeSkips_AndNothingCountsOnceTheQuestionIsAnswered()
    {
        var ask = new PopQuizAsk(PopQuizQuestions.Pool[0], new SequenceRandom([0.0]));

        Assert.Equal(PopQuizStep.Ignored, ask.Apply('0', isCharacter: true, isCancel: false));
        Assert.Equal(PopQuizStep.Ignored, ask.Apply('5', isCharacter: true, isCancel: false));
        Assert.Equal(PopQuizStep.Ignored, ask.Apply('a', isCharacter: true, isCancel: false));
        Assert.Equal(PopQuizStep.Ignored, ask.Apply('\0', isCharacter: false, isCancel: false));
        Assert.Null(ask.Affirmation);

        // Escape, unanswered: upstream closes the window and awards nothing (:128-134).
        Assert.Equal(PopQuizStep.Skipped, ask.Apply('\0', isCharacter: false, isCancel: true));
        Assert.False(ask.Answered);

        // And once an answer is in, upstream's own first line in both handlers refuses everything
        // else (:130 for Escape, :138 for a click) — the window is already on its way out and a
        // second answer would re-award and re-affirm.
        Assert.Equal(PopQuizStep.Picked, Pick(ask, 2));
        Assert.Equal(PopQuizStep.Ignored, ask.Apply('\0', isCharacter: false, isCancel: true));
        Assert.Equal(PopQuizStep.Ignored, Pick(ask, 0));
        Assert.Equal(2, ask.PickedSlot);
    }

    [Fact]
    public void TheCardsQuestionFaceCarriesTheQuestionAndAllFourAnswersWithTheKeyThatPicksEach()
    {
        // The wrapped slot is the only multi-line one the capability draws
        // (Input/Win32InputPresence.cs:817), so an answer put anywhere else could be ellipsised away
        // — and an option nobody can read is one nobody can pick.
        var ask = new PopQuizAsk(PopQuizQuestions.Pool[3], new SequenceRandom([0.5]));
        var lines = ask.Face.Split('\n');

        Assert.Equal("What do good girls do?", lines[0]);
        Assert.Equal(
            [$"1  {ask.Options[0]}", $"2  {ask.Options[1]}", $"3  {ask.Options[2]}", $"4  {ask.Options[3]}"],
            lines.Where(l => l.Length > 0).Skip(1).ToArray());
    }

    // =================================================================================
    //  the module — one shared presence, one injected clock
    // =================================================================================

    [Fact]
    public void AQuestionComesDueOnTheInjectedClockAndGoesUpOnTheSharedInputCapability()
    {
        using var rig = new Rig();
        rig.Enable();
        rig.Effect.Arm();

        Assert.Empty(rig.Presence.Prompts);
        rig.Clock.Advance(PopQuizSchedule.Interval(PopQuizSchedule.DefaultPerHour, 0.5));

        var prompt = Assert.Single(rig.Presence.Prompts);
        var ask = Assert.IsType<PopQuizAsk>(rig.Effect.Ask);
        Assert.Equal(ask.Face, prompt.Content.Question);
        Assert.Equal(PopQuizAsk.Hint, prompt.Content.Hint);
        Assert.Equal(1, rig.Effect.QuizCount);

        var shown = Assert.Single(rig.Shown);
        Assert.Equal(1, shown.Ordinal);
        Assert.Equal(ask.Question.Text.Length, shown.QuestionLength);
        Assert.Equal(PopQuizQuestion.AnswerCount, shown.OptionCount);
    }

    [Fact]
    public void TheEventCarriesNoQuestionNoAnswerAndNoAffirmation()
    {
        // The media-logging rule FlashEvent, LockCardEvent and MandatoryVideoEvent already hold: a
        // subscriber, a diagnostic line and a bug report get a COUNT, never the content. It is a
        // DIVERGENCE — upstream logs the question in plain text on every quiz
        // (Services/Quiz/PopQuizService.cs:248).
        var stringly = typeof(PopQuizEvent)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToList();
        Assert.Empty(stringly);

        using var rig = new Rig();
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.Advance(PopQuizSchedule.Interval(PopQuizSchedule.DefaultPerHour, 0.5));

        var ask = Assert.IsType<PopQuizAsk>(rig.Effect.Ask);
        var rendered = Assert.Single(rig.Shown).ToString();
        Assert.DoesNotContain(ask.Question.Text, rendered, StringComparison.Ordinal);
        foreach (var option in ask.Options)
        {
            Assert.DoesNotContain(option, rendered, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoSecondCardIsStackedOverOneAlreadyUp_WhichIsBothOfUpstreamsGuardsInOneRead()
    {
        // Upstream refuses a second quiz (PopQuizService.cs:183-187) and refuses to draw over a lock
        // card (:194, its own #763). Here the SHARED presence is what knows a card is up, so one read
        // covers both — and upstream's own no-queue branch is to drop (:212-215), which is this
        // port's situation permanently.
        using var rig = new Rig();
        rig.Enable();
        rig.Effect.Arm();
        rig.Presence.SimulateForeignCard();

        rig.Clock.Advance(PopQuizSchedule.Interval(PopQuizSchedule.DefaultPerHour, 0.5));

        Assert.Empty(rig.Presence.Prompts);
        Assert.Equal(0, rig.Effect.QuizCount);
        Assert.Empty(rig.Shown);

        // And the schedule keeps running, so the quiz after the card comes down is asked normally.
        rig.Presence.Dismiss();
        rig.Clock.Advance(PopQuizSchedule.Interval(PopQuizSchedule.DefaultPerHour, 0.5));
        Assert.Single(rig.Presence.Prompts);
    }

    [Fact]
    public void NothingIsAskedWhereTheOperatingSystemSaysNobodyCanBeAsked()
    {
        using var rig = new Rig();
        rig.Enable();
        rig.Presence.CanReachAUser = false;
        rig.Effect.Arm();

        rig.Clock.Advance(PopQuizSchedule.Interval(PopQuizSchedule.DefaultPerHour, 0.5));
        Assert.Empty(rig.Presence.Prompts);
        Assert.Equal(0, rig.Effect.QuizCount);

        // The desktop comes back mid-session and the next question is asked, with no re-arm — the
        // answer the audio pair set for a lost endpoint, in the input medium.
        rig.Presence.CanReachAUser = true;
        rig.Clock.Advance(PopQuizSchedule.Interval(PopQuizSchedule.DefaultPerHour, 0.5));
        Assert.Single(rig.Presence.Prompts);
    }

    [Fact]
    public void AnsweringRevealsTheAffirmationAfterThreeHundredMillisecondsAndClosesFifteenHundredAfterThat()
    {
        // Windows/PopQuizWindow.xaml.cs:170-177 — await Task.Delay(300), swap the panels, await
        // Task.Delay(1500), close. Both on the INJECTED clock here; there is no Task.Delay in this
        // port's session paths at all.
        using var rig = new Rig();
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.Advance(PopQuizSchedule.Interval(PopQuizSchedule.DefaultPerHour, 0.5));

        var ask = Assert.IsType<PopQuizAsk>(rig.Effect.Ask);
        rig.Presence.TypeCharacters("2");
        Assert.Empty(rig.Presence.Updates);
        Assert.Equal(PopQuizResolution.None, rig.Effect.LastResolution);

        rig.Clock.Advance(PopQuizEffect.AffirmationDelay - TimeSpan.FromMilliseconds(1));
        Assert.Empty(rig.Presence.Updates);

        rig.Clock.Advance(TimeSpan.FromMilliseconds(1));
        var affirmation = Assert.Single(rig.Presence.Updates);
        Assert.Equal(ask.Question.Affirmations[ask.Order[1]], affirmation.Question);
        Assert.Equal(0, rig.Presence.Dismissals);

        rig.Clock.Advance(PopQuizEffect.AffirmationDwell - TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, rig.Presence.Dismissals);

        rig.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, rig.Presence.Dismissals);
        Assert.Equal(PopQuizResolution.Answered, rig.Effect.LastResolution);
        Assert.Equal(1, rig.Effect.AnsweredCount);
        Assert.Null(rig.Effect.Ask);
    }

    [Fact]
    public void EscapeTakesTheCardDownAtOnceWithNoAffirmationAndNoXp()
    {
        using var rig = new Rig(withLedger: true);
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.Advance(PopQuizSchedule.Interval(PopQuizSchedule.DefaultPerHour, 0.5));

        rig.Presence.PressEscape();

        Assert.Equal(1, rig.Presence.Dismissals);
        Assert.Empty(rig.Presence.Updates);
        Assert.Equal(PopQuizResolution.Skipped, rig.Effect.LastResolution);
        Assert.Equal(1, rig.Effect.SkippedCount);
        Assert.Equal(0, rig.Effect.AnsweredCount);
        Assert.Null(rig.Effect.LastGrant);
        Assert.Equal(0.0, rig.Ledger!.XpIntoLevel);
    }

    // =================================================================================
    //  the consequence — upstream's twenty-five, into the ledger this build really has
    // =================================================================================

    [Fact]
    public void AnAnsweredQuestionBanksUpstreamsTwentyFiveIntoTheRealLedgerAtThePickAndNotAtTheClose()
    {
        // Windows/PopQuizWindow.xaml.cs:157-167 awards 25 at the CLICK, before the 300 ms wait and
        // before the affirmation is drawn (:170-173). The packet's premise that nothing in this build
        // awards XP is stale: Features/Progression/ProgressionLedger banks from three call sites
        // already, so upstream's own number goes to the real store rather than being refused.
        using var rig = new Rig(withLedger: true);
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.Advance(PopQuizSchedule.Interval(PopQuizSchedule.DefaultPerHour, 0.5));

        rig.Presence.TypeCharacters("1");

        var grant = Assert.IsType<XpGrant>(rig.Effect.LastGrant);
        Assert.True(grant.Banked);
        Assert.Equal(PopQuizEffect.AnswerXp, grant.Amount);
        Assert.Equal(PopQuizEffect.AnswerXp, rig.Ledger!.XpIntoLevel);

        // ...and the card then says so, in upstream's own words (Windows/PopQuizWindow.xaml:124).
        rig.Clock.Advance(PopQuizEffect.AffirmationDelay);
        Assert.Equal(PopQuizAsk.XpLine, Assert.Single(rig.Presence.Updates).Answer);
    }

    [Fact]
    public void WithNoLedgerNothingIsBankedAndTheCardMakesNoXpClaimAtAll()
    {
        // The refusal is the seam, not a stub: a host that opened no ledger banks nothing, and
        // printing "+25 XP" over a grant that never happened would be the confident half-truth this
        // port refuses everywhere else. Upstream cannot reach this state — its own award sits in a
        // try/catch that logs at Debug (:159-167) — so the card's XP line is conditional here and
        // unconditional there.
        using var rig = new Rig();
        Assert.False(rig.Effect.BanksXp);

        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.Advance(PopQuizSchedule.Interval(PopQuizSchedule.DefaultPerHour, 0.5));
        rig.Presence.TypeCharacters("3");
        rig.Clock.Advance(PopQuizEffect.AffirmationDelay);

        Assert.Null(rig.Effect.LastGrant);
        Assert.Equal(string.Empty, Assert.Single(rig.Presence.Updates).Answer);

        // The quiz still completes — the XP is a consequence of the answer, never a condition of it.
        rig.Clock.Advance(PopQuizEffect.AffirmationDwell);
        Assert.Equal(PopQuizResolution.Answered, rig.Effect.LastResolution);
    }

    // =================================================================================
    //  the refusals, the dot and the stop
    // =================================================================================

    [Fact]
    public void ARefusedPromptTakesTheCardStraightBackDownRatherThanLeavingItUnanswerable()
    {
        // Including Degraded, which is the trap: the OS gave the card the keyboard and only the ink
        // read-back said no, so the window is still up. Without the dismiss it stays there blank,
        // holding the user's keyboard, and Compose's already-prompting guard then drops every later
        // question for the rest of the session. Upstream force-closes on its own error path for the
        // same reason (Services/Quiz/PopQuizService.cs:250-254).
        using var rig = new Rig();
        rig.Enable();
        rig.Presence.NextPromptOutcome = new CapabilityState.Degraded(
            "the OS holds the card and no ink for it",
            new CapabilityReason(InputReasonCodes.InputPromptNotInked, "nothing is drawn"));
        rig.Effect.Arm();

        rig.Clock.Advance(PopQuizSchedule.Interval(PopQuizSchedule.DefaultPerHour, 0.5));

        Assert.Single(rig.Presence.Prompts);
        Assert.Equal(1, rig.Presence.Dismissals);
        Assert.Empty(rig.Shown);
        Assert.Equal(PopQuizResolution.Refused, rig.Effect.LastResolution);
        Assert.Null(rig.Effect.Ask);
    }

    [Fact]
    public void TheRowRefusesInTypeWhereNothingCanEverBeAsked()
    {
        using var rig = new Rig();
        rig.Enable();
        rig.Presence.CanReachAUser = false;

        var refusal = Assert.IsType<CapabilityState.Unavailable>(rig.Effect.Arm());
        Assert.Equal(EffectReasonCodes.InputCaptureUnavailable, refusal.Reason.Code);
        Assert.Contains("can never ask anybody anything", refusal.Reason.Detail, StringComparison.Ordinal);

        // And with a desktop the OS confirms, the same arm is Available — so the refusal above is the
        // station read-back and not a permanent verdict about this module.
        rig.Presence.CanReachAUser = true;
        Assert.IsType<CapabilityState.Available>(rig.Effect.Refresh());
    }

    [Fact]
    public void TheDotIsLiveOnlyWhileTheClockIsArmedAndTheOsWillTakeTheQuestion()
    {
        using var rig = new Rig();
        Assert.Equal(EffectDotState.Off, rig.Effect.Dot);

        rig.Enable();
        rig.Effect.Arm();
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);

        // Clause two: the OS says this process cannot put a window in front of anybody.
        rig.Presence.CanReachAUser = false;
        Assert.Equal(EffectDotState.Armed, rig.Effect.Dot);
        rig.Presence.CanReachAUser = true;

        // Clause three, and its "of MINE" qualifier. Somebody ELSE's card on the shared presence
        // must not darken this row: it is idle and healthy.
        rig.Presence.SimulateForeignCard();
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);
        rig.Presence.Dismiss();

        // This module's own card, with the OS holding the input, is Live — waiting for a human IS
        // the work. With the foreground taken away it is not.
        rig.Clock.Advance(PopQuizSchedule.Interval(PopQuizSchedule.DefaultPerHour, 0.5));
        Assert.NotNull(rig.Effect.Ask);
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);
        rig.Presence.HoldsTheInput = false;
        Assert.Equal(EffectDotState.Armed, rig.Effect.Dot);
    }

    [Fact]
    public void AStopTakesThisModulesQuestionDown_AndLeavesSomebodyElsesCardAlone()
    {
        // WPF drops its own visible card on a stop (PopQuizService.cs:139 → :144-155) and panic
        // force-closes it (MainWindow/MainWindow.StartStop.cs:373). The guard is Bubble Count's
        // finding: the presence is single-tenant and shared, so an unconditional Dismiss here would
        // take down a Lock Card that has nothing to do with this row.
        using var rig = new Rig();
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.Advance(PopQuizSchedule.Interval(PopQuizSchedule.DefaultPerHour, 0.5));
        Assert.NotNull(rig.Effect.Ask);

        rig.Effect.Disarm();
        Assert.Equal(1, rig.Presence.Dismissals);
        Assert.Equal(PopQuizResolution.Withdrawn, rig.Effect.LastResolution);
        Assert.Null(rig.Effect.Ask);
        Assert.Equal(0, rig.Effect.AnsweredCount);

        // A second stop with a foreign card up touches nothing.
        rig.Presence.SimulateForeignCard();
        rig.Effect.Disarm();
        Assert.Equal(1, rig.Presence.Dismissals);
        Assert.True(rig.Presence.IsPrompting);
    }

    [Fact]
    public void AStopDropsTheCardsPendingDelaySoNothingRepaintsAfterTheSessionEnded()
    {
        // The two card delays are real timers on the clock. A stop that left the dwell running would
        // repaint or dismiss the shared presence after the session ended — on somebody else's card,
        // by then. Bubble Count's CancelSafety shape, applied to this module's follow-up.
        using var rig = new Rig();
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.Advance(PopQuizSchedule.Interval(PopQuizSchedule.DefaultPerHour, 0.5));
        rig.Presence.TypeCharacters("4");

        Assert.Equal(2, rig.Clock.LiveTimers); // the next question, and the card's pending reveal

        rig.Effect.Disarm();
        var dismissals = rig.Presence.Dismissals;

        // NOT ONE live one-shot survives the stop. This is the half no observable of the presence can
        // see: the identity guard inside the delay already makes a stale callback inert, so a fact
        // that only watched for a repaint would stay green with the timer still on the clock.
        Assert.Equal(0, rig.Clock.LiveTimers);

        rig.Clock.Advance(PopQuizEffect.AffirmationDelay + PopQuizEffect.AffirmationDwell);
        Assert.Empty(rig.Presence.Updates);
        Assert.Equal(dismissals, rig.Presence.Dismissals);
        Assert.Equal(PopQuizResolution.Withdrawn, rig.Effect.LastResolution);
    }

    [Fact]
    public void TheFrequencyDialTakesEffectNowRatherThanAfterTheOldIntervalExpires()
    {
        // Upstream recomputes the interval from the CURRENT setting inside every tick
        // (Services/Quiz/PopQuizService.cs:163-171), so a slider move is felt at the next question
        // rather than after the old gap. The port's convention writes and re-evaluates.
        using var rig = new Rig();
        rig.Enable();
        rig.Effect.Arm();

        rig.Effect.SetPerHour(60);
        Assert.Equal(60, rig.Effect.Preset.PerHour);

        // One an hour would have put the next question 30 minutes out; sixty an hour puts it inside
        // one, and the fact advances by exactly that.
        rig.Clock.Advance(PopQuizSchedule.Interval(60, 0.5));
        Assert.Single(rig.Presence.Prompts);
    }

    // =================================================================================
    //  fixtures
    // =================================================================================

    private static PopQuizStep Pick(PopQuizAsk ask, int slot) =>
        ask.Apply((char)(PopQuizAsk.FirstAnswerKey + slot), isCharacter: true, isCancel: false);

    private sealed class Rig : IDisposable
    {
        private readonly OperationRegistry _registry = new();
        private readonly string _directory;
        private readonly PersistenceStore<ProgressionDocument>? _xpStore;

        public Rig(bool withLedger = false)
        {
            _directory = Path.Combine(Path.GetTempPath(), "ccp-popquiz-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());
            Clock = new ManualClock();
            Presence = new RecordingInputPresence();
            Preset = new PersistenceStore<PopQuizPresetDocument>(
                _registry.OwnerFor("PopQuizPreset"), new NullLog(),
                Path.Combine(_directory, PopQuizPresetDocument.FileName),
                PopQuizPresetDocument.CurrentSchemaVersion);

            if (withLedger)
            {
                _xpStore = new PersistenceStore<ProgressionDocument>(
                    _registry.OwnerFor("PopQuizProgression"), new NullLog(),
                    Path.Combine(_directory, ProgressionDocument.FileName),
                    ProgressionDocument.CurrentSchemaVersion);
                _xpStore.StartAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult(); // wallclock-allow: PersistenceStore.StartAsync loads on the calling thread and hands back an already-complete task (pinned by PersistenceStoreTests) — this bridge waits on nothing
                // ownsStore: this rig opened it, so the ledger's own flush-then-stop is the teardown
                // (ProgressionLedger.Dispose, contract §11: a stop is not a flush).
                Ledger = new ProgressionLedger(_xpStore, static _ => { }, ownsStore: true);
            }

            Effect = new PopQuizEffect(
                _registry.OwnerFor("PopQuiz"),
                new EffectSignal(boundary, static () => true),
                Clock,
                Presence,
                Preset,
                Ledger,
                // A scripted draw: the SPACING, the QUESTION and the SHUFFLE are what these facts
                // make deterministic, and the arithmetic they feed stays the module's own.
                new SequenceRandom([0.5]),
                () => new InputBounds(0, 0, 400, 300));

            Effect.Shown += e => Shown.Add(e);
            Effect.Resolved += r => Resolutions.Add(r);
        }

        public ManualClock Clock { get; }

        public RecordingInputPresence Presence { get; }

        public PersistenceStore<PopQuizPresetDocument> Preset { get; }

        public ProgressionLedger? Ledger { get; }

        public PopQuizEffect Effect { get; }

        public List<PopQuizEvent> Shown { get; } = [];

        public List<PopQuizResolution> Resolutions { get; } = [];

        public void Enable(int perHour = PopQuizSchedule.DefaultPerHour) =>
            Preset.Mutate(p =>
            {
                p.Enabled = true;
                p.PerHour = perHour;
            });

        public void Dispose()
        {
            Effect.Disarm();
            Ledger?.Dispose();

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
    /// <para>It MIRRORS the product where the product's own transitions matter: the real presence
    /// sets its prompting flag from the OS's CONFIRMATION (<c>Win32InputPresence:218</c>), and
    /// <c>Confirmed</c> does NOT include ink — so a card the OS focused with nothing written on it
    /// comes back Degraded WITH THE WINDOW STILL UP. A double that flipped the flag only on
    /// Available would report the opposite of the product in exactly the state that traps the
    /// user.</para>
    /// </summary>
    private sealed class RecordingInputPresence : IInputPresence
    {
        private Action<InputKeystroke>? _onKeystroke;

        public List<InputPromptRequest> Prompts { get; } = [];

        public List<InputPromptContent> Updates { get; } = [];

        public int Dismissals { get; private set; }

        public CapabilityState? NextPromptOutcome { get; set; }

        public bool CanReachAUser { get; set; } = true;

        public bool HoldsTheInput { get; set; } = true;

        public bool IsPrompting { get; private set; }

        public CapabilityState? LastPrompt { get; private set; }

        public InputCaptureObservation LastObservation { get; private set; } = InputCaptureObservation.NotAsked;

        /// <summary>Somebody ELSE's card is up on this shared presence — a Lock Card, in the product.
        /// The keystroke callback is not this module's, which is exactly why prompting over it would
        /// strand it.</summary>
        public void SimulateForeignCard()
        {
            IsPrompting = true;
            _onKeystroke = null;
        }

        public CapabilityState Prompt(InputPromptRequest request)
        {
            Prompts.Add(request);
            var outcome = NextPromptOutcome
                ?? new CapabilityState.Available("recording presence: the OS gave the card the keyboard");

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

    /// <summary>A <see cref="Random"/> whose draws are a script, so a fact can pin a permutation or
    /// an interval without pinning a seed.</summary>
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

    /// <summary>The manual clock, in the shape every module test shares. Zero wall-clock.</summary>
    private sealed class ManualClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);

        /// <summary>How many one-shots are still live on this clock. A stop must leave none — the
        /// rule <see cref="PacedSessionEffect{TFiring}"/> states for its own pending firing, and the
        /// only way to pin a module's EXTRA timers, which no observable of the presence can see once
        /// the identity guard has already made them inert.</summary>
        public int LiveTimers
        {
            get
            {
                lock (_timers)
                {
                    return _timers.Count(t => !t.Cancelled);
                }
            }
        }

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            lock (_timers)
            {
                _timers.Add(entry);
            }

            return new Handle(entry);
        }

        public void Advance(TimeSpan by)
        {
            var target = UtcNow + by;
            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers.Where(t => !t.Cancelled && t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is not null)
                    {
                        _timers.Remove(next);
                    }
                }

                if (next is null)
                {
                    UtcNow = target;
                    return;
                }

                UtcNow = next.Due;
                next.Fire();
            }
        }

        private sealed class Entry
        {
            public DateTimeOffset Due { get; init; }

            public required Action Fire { get; init; }

            public bool Cancelled { get; set; }
        }

        private sealed class Handle(Entry entry) : IDisposable
        {
            public void Dispose() => entry.Cancelled = true;
        }
    }

    private sealed class NullLog : ILogSink
    {
        public void Log(string message)
        {
        }
    }

    private sealed class InlineDispatch : IUiDispatch
    {
        public void Post(Action action) => action();
    }
}
