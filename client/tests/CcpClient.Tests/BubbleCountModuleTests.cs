using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Video;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Bubble Count: the first module that consumes capabilities it did not shape, and the
/// first that consumes TWO of them in one firing.
///
/// <para>Nothing here touches a desktop, a decoder or a file: the surface and the input presence are
/// doubles, which is exactly what lets these facts pin the MODULE's decisions rather than the
/// operating system's. Both capabilities' OS-level halves are already pinned by
/// <see cref="VideoCapabilityTests"/> and <see cref="InputCapabilityTests"/>, and this packet adds
/// nothing to either.</para>
/// </summary>
public class BubbleCountModuleTests
{
    // ---------------------------------------------------------------------------------------
    //  THE PACING LAW — upstream's own, and it is NOT the video module's
    // ---------------------------------------------------------------------------------------

    [Theory]
    // 3600/perHour + (roll*variance*2 - variance) with variance = 20 % of the base, floored at 60 s
    // (Services/BubbleCountService.cs:88-96).
    [InlineData(1, 0.0, 2880.0)]
    [InlineData(1, 0.5, 3600.0)]
    [InlineData(1, 1.0, 4320.0)]
    [InlineData(2, 0.0, 1440.0)]
    [InlineData(10, 0.5, 360.0)]
    public void TheIntervalIsUpstreamsOwnArithmetic(int perHour, double roll, double expectedSeconds)
    {
        Assert.Equal(
            expectedSeconds, BubbleCountSchedule.Interval(perHour, roll).TotalSeconds, precision: 6);
    }

    [Fact]
    public void TheDialIsClampedToTENNotTwenty_AndTheSIXTYSECONDFloorBinds()
    {
        // Upstream's own clamp at the point of use is Math.Max(1, Math.Min(10, …)) with the comment
        // "Frequency is games per hour (1-10)" (:88). The VIDEO module's ceiling is twenty
        // (ProgramDefinition.cs:442) — two upstream numbers, ported as two.
        Assert.Equal(10, BubbleCountSchedule.MaxPerHour);
        Assert.Equal(20, MandatoryVideoSchedule.MaxPerHour);
        Assert.Equal(
            BubbleCountSchedule.Interval(BubbleCountSchedule.MaxPerHour, 0.5),
            BubbleCountSchedule.Interval(9999, 0.5));
        Assert.Equal(
            BubbleCountSchedule.Interval(BubbleCountSchedule.MinPerHour, 0.5),
            BubbleCountSchedule.Interval(0, 0.5));

        // AND THE SIXTY-SECOND FLOOR CAN NEVER BIND, which is a fact about upstream's own structure
        // rather than a gap in this port. Upstream clamps the dial to ten AND floors the interval at
        // sixty seconds (:88, :95); at ten an hour with the jitter at its minimum the interval is
        // 3600/10*0.8 = 288 s, so the floor is unreachable through the clamp. It is ported anyway,
        // because it is upstream's line and it sits where a later dial change would meet it — and
        // the mutation that deletes it therefore SURVIVES, which is recorded rather than papered
        // over with a fact that reaches the branch by some route no caller has.
        Assert.True(
            BubbleCountSchedule.Interval(BubbleCountSchedule.MaxPerHour, 0.0)
                > BubbleCountSchedule.MinimumInterval,
            "the clamp no longer keeps the interval above the floor, so the floor has become "
            + "reachable and needs a fact of its own");
    }

    // ---------------------------------------------------------------------------------------
    //  THE COUNTING ARITHMETIC — every number is upstream's
    // ---------------------------------------------------------------------------------------

    [Theory]
    // round(baseRate/30 * seconds ± 20 %), floored at three
    // (Windows/BubbleCountWindow.xaml.cs:1139-1151).
    [InlineData(BubbleCountDifficulty.Easy, 300, 0.5, 30)]
    [InlineData(BubbleCountDifficulty.Medium, 300, 0.5, 50)]
    [InlineData(BubbleCountDifficulty.Hard, 300, 0.5, 80)]
    [InlineData(BubbleCountDifficulty.Medium, 300, 0.0, 40)]
    [InlineData(BubbleCountDifficulty.Medium, 300, 1.0, 60)]
    public void TheTargetIsUpstreamsOwnArithmetic(
        BubbleCountDifficulty difficulty, int seconds, double roll, int expected)
    {
        Assert.Equal(
            expected, BubbleCountArithmetic.Target(difficulty, TimeSpan.FromSeconds(seconds), roll));
    }

    [Fact]
    public void TheTargetFloorIsTHREE_HoweverShortTheClip()
    {
        // Upstream's Math.Max(3, …) (:1150). A two-second clip on Easy scales to 0.2 bubbles, and a
        // game that asked "how many bubbles?" about none would be a question with a cruel answer.
        Assert.Equal(
            BubbleCountArithmetic.MinimumTarget,
            BubbleCountArithmetic.Target(BubbleCountDifficulty.Easy, TimeSpan.FromSeconds(2), 0.0));
        Assert.Equal(3, BubbleCountArithmetic.MinimumTarget);
    }

    [Fact]
    public void AClipTheOSReportsNoLengthForUsesUpstreamsThirtySecondFallback()
    {
        // Upstream starts every game on FallbackDurationSeconds = 30 when its metadata cache has
        // never seen the file (:98, :703-712) and replaces it when the real length arrives.
        Assert.Equal(
            BubbleCountArithmetic.Target(BubbleCountDifficulty.Medium, TimeSpan.FromSeconds(30), 0.5),
            BubbleCountArithmetic.Target(BubbleCountDifficulty.Medium, TimeSpan.Zero, 0.5));
        Assert.Equal(TimeSpan.FromSeconds(30), BubbleCountArithmetic.FallbackDuration);
    }

    [Fact]
    public void TheSpawnIntervalIsSEVENTYPercentOfTheEvenSpacing()
    {
        // Upstream: (durationSeconds * 1000 / target) * 0.7 (:1201). The 0.7 is what lets the
        // 0.7-probability tick below still produce roughly the target over a clip.
        Assert.Equal(
            TimeSpan.FromMilliseconds(300 * 1000.0 / 50 * 0.7),
            BubbleCountArithmetic.SpawnInterval(TimeSpan.FromSeconds(300), 50));

        // A degenerate target cannot produce a zero interval and a spin.
        Assert.True(BubbleCountArithmetic.SpawnInterval(TimeSpan.Zero, 0) > TimeSpan.Zero);
    }

    [Theory]
    // roll < 0.7 || shown < target/2, with upstream's INTEGER division (:1211).
    [InlineData(0, 10, 0.9, true)]   // below half the target: spawns whatever the roll says
    [InlineData(4, 10, 0.9, true)]   // still below 5
    [InlineData(5, 10, 0.9, false)]  // at half: the roll decides, and 0.9 is a miss
    [InlineData(5, 10, 0.69, true)]  // the roll decides, and 0.69 is a hit
    [InlineData(1, 3, 0.9, false)]   // 3/2 == 1 in integer division, so one bubble is the whole window
    public void ATickSpawnsOnUpstreamsOwnRule(int shown, int target, double roll, bool expected)
    {
        Assert.Equal(expected, BubbleCountArithmetic.TickSpawns(shown, target, roll));
    }

    // ---------------------------------------------------------------------------------------
    //  THE RUN — which is a painter, and what it puts on the picture
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void NothingSpawnsBeforeUpstreamsLEADIN_AndTheFirstTickSpawnsUNCONDITIONALLY()
    {
        // A roll of 0.99 fails the 0.7 probability, so a lead-in tick that applied it would spawn
        // nothing at all on a target of 3 (where target/2 == 1 covers only the first bubble).
        var run = new BubbleCountRun(BubbleCountDifficulty.Easy, new SequenceRandom([0.99]));
        run.Opening(new VideoClipInfo(true, 320, 240, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(30), false));

        run.Paint(VideoFrame.Solid(320, 240, 0, 0, 0), TimeSpan.FromMilliseconds(1400));
        Assert.Equal(0, run.BubblesShown);

        // Upstream starts its timer after a 1500 ms delay and spawns ONCE outside the probability
        // branch at that moment (:1220, :1231-1233).
        run.Paint(VideoFrame.Solid(320, 240, 0, 0, 0), BubbleCountArithmetic.SpawnLeadIn);
        Assert.Equal(1, run.BubblesShown);
    }

    [Fact]
    public void OPENINGReDerivesTheTargetFromTheOSsOwnLength_WithoutReRollingTheJitter()
    {
        var run = new BubbleCountRun(BubbleCountDifficulty.Medium, new SequenceRandom([0.0, 0.9, 0.9, 0.9]));

        // Before the clip opens the run stands on upstream's 30 s fallback.
        Assert.Equal(BubbleCountArithmetic.FallbackDuration, run.Duration);
        Assert.Equal(BubbleCountArithmetic.Target(BubbleCountDifficulty.Medium, TimeSpan.FromSeconds(30), 0.0), run.Target);

        run.Opening(new VideoClipInfo(true, 320, 240, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(300), false));

        // Upstream re-derives the target and re-spaces the spawn timer the moment the real length
        // arrives (:719-736, AdoptRealDuration).
        Assert.Equal(TimeSpan.FromSeconds(300), run.Duration);
        Assert.Equal(BubbleCountArithmetic.Target(BubbleCountDifficulty.Medium, TimeSpan.FromSeconds(300), 0.0), run.Target);

        // AND THE JITTER ROLL IS NOT RE-DRAWN. It is taken once, in the constructor, so the same run
        // cannot get a different target depending on how many times it was recomputed — the kind of
        // hidden non-determinism a seeded fact cannot see.
        Assert.Equal(40, run.Target);
    }

    [Fact]
    public void ABubbleReallyCHANGESThePicture_AndTheChangeIsGoneOnceItHasPopped()
    {
        var run = new BubbleCountRun(BubbleCountDifficulty.Easy, new SequenceRandom([0.5]));
        run.Opening(new VideoClipInfo(true, 400, 300, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(30), false));

        var frame = VideoFrame.Solid(400, 300, 0x10, 0x20, 0x30);
        var before = frame.ColourAt(200, 150);
        run.Paint(frame, BubbleCountArithmetic.SpawnLeadIn + BubbleCountRun.GrowDuration);
        Assert.Equal(1, run.BubblesShown);

        // The bubble is drawn at the run's own centre for a 0.5 roll — relX 0.5, relY 0.5 — so the
        // picture's middle really changed. A painter that returned without touching the buffer would
        // be a game whose bubbles nobody could count.
        Assert.NotEqual(before, frame.ColourAt(200, 150));

        // And a FRESH picture painted after the bubble has popped is left alone: bubbles are not
        // permanent, so a clip whose pictures kept every bubble ever spawned would be uncountable.
        var late = VideoFrame.Solid(400, 300, 0x10, 0x20, 0x30);
        var bubble = run.Bubbles[0];
        run.Paint(late, bubble.GoneAt + TimeSpan.FromMilliseconds(1));
        Assert.Equal(before, late.ColourAt(200, 150));
    }

    [Fact]
    public void TheBubbleStaysINSIDEThePicture_AndAPictureTooSmallForOneIsLeftAlone()
    {
        // Placement is roll*0.7+0.15 by roll*0.5+0.25 (:1252-1253), so a 1.0 roll puts the centre at
        // 0.85 x 0.75 — close enough to an edge that an unclamped span would run off the buffer. The
        // assertion IS that this does not throw, plus that the picture changed.
        var run = new BubbleCountRun(BubbleCountDifficulty.Easy, new SequenceRandom([1.0]));
        run.Opening(new VideoClipInfo(true, 320, 240, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(30), false));
        var frame = VideoFrame.Solid(320, 240, 0, 0, 0);
        run.Paint(frame, BubbleCountArithmetic.SpawnLeadIn + BubbleCountRun.GrowDuration);
        Assert.NotEqual(0u, frame.ColourAt(319, 239) + frame.ColourAt(272, 180));

        // A picture so small that the bubble is under a pixel across is left untouched rather than
        // scribbled on: a sub-pixel disc is not something anybody could count.
        var tiny = new BubbleCountRun(BubbleCountDifficulty.Easy, new SequenceRandom([0.5]));
        tiny.Opening(new VideoClipInfo(true, 8, 8, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(30), false));
        var small = VideoFrame.Solid(8, 8, 0x11, 0x22, 0x33);
        tiny.Paint(small, BubbleCountArithmetic.SpawnLeadIn);
        Assert.Equal(0x112233u, small.ColourAt(4, 4));
    }

    [Fact]
    public void BubblesShownIsWhatTheUserIsAskedAbout_NEVERTheTarget()
    {
        var run = new BubbleCountRun(BubbleCountDifficulty.Hard, new SequenceRandom([0.5]));
        run.Opening(new VideoClipInfo(true, 320, 240, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(300), false));

        // Eighty bubbles are what this clip WOULD carry if it ran to its end.
        Assert.Equal(80, run.Target);

        // Two pictures in, two bubbles have been drawn — and that, not eighty, is the number the
        // question is about. The distinction is the whole reason the run exposes both.
        run.Paint(VideoFrame.Solid(320, 240, 0, 0, 0), BubbleCountArithmetic.SpawnLeadIn);
        run.Paint(VideoFrame.Solid(320, 240, 0, 0, 0), BubbleCountArithmetic.SpawnLeadIn + run.SpawnInterval);
        Assert.Equal(2, run.BubblesShown);
        Assert.NotEqual(run.Target, run.BubblesShown);
    }

    [Fact]
    public void TheRunNeverSpawnsPastItsTarget()
    {
        var run = new BubbleCountRun(BubbleCountDifficulty.Easy, new SequenceRandom([0.5]));
        run.Opening(new VideoClipInfo(true, 320, 240, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(30), false));

        // One picture, an hour into a thirty-second clip: every tick that could ever come due is
        // processed at once, and the count still stops at the target.
        run.Paint(VideoFrame.Solid(320, 240, 0, 0, 0), TimeSpan.FromHours(1));
        Assert.Equal(run.Target, run.BubblesShown);
    }

    [Fact]
    public void ACLIPTheOSReportsNOLengthForKeepsTheFallback_AndTheTargetFollowsIt()
    {
        // Found by the mutation sweep: nothing pinned Recompute's OWN fallback, because every fact
        // handed Opening a positive duration. A container that reports no length is upstream's
        // ordinary case, not an edge - its metadata cache misses on every unseen file
        // (Windows/BubbleCountWindow.xaml.cs:703-712).
        var run = new BubbleCountRun(BubbleCountDifficulty.Medium, new SequenceRandom([0.5]));
        run.Opening(new VideoClipInfo(true, 320, 240, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, false));

        Assert.Equal(BubbleCountArithmetic.FallbackDuration, run.Duration);
        Assert.Equal(
            BubbleCountArithmetic.Target(BubbleCountDifficulty.Medium, BubbleCountArithmetic.FallbackDuration, 0.5),
            run.Target);
    }

    [Fact]
    public void BUBBLESDoNotAllPopTogether_AndAPoppingOneREALLYFades()
    {
        // Two sweep survivors in one fact. First: the lifetime's random span. Upstream's bubbles
        // live 1000 + rand(500) ms (:1734) so they pop RAGGEDLY; a fixed lifetime would make every
        // bubble spawned on the same tick vanish in lockstep, which is a different thing to count.
        var run = new BubbleCountRun(BubbleCountDifficulty.Hard, new SequenceRandom([0.0, 0.5, 0.9]));
        run.Opening(new VideoClipInfo(true, 320, 240, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(300), false));
        for (var i = 0; i < 6; i++)
        {
            run.Paint(
                VideoFrame.Solid(320, 240, 0, 0, 0),
                BubbleCountArithmetic.SpawnLeadIn + (run.SpawnInterval * i));
        }

        Assert.True(run.BubblesShown >= 2);
        Assert.True(
            run.Bubbles.Select(b => b.Lifetime).Distinct().Count() > 1,
            "every bubble was given the same lifetime, so they pop in lockstep");
        Assert.All(run.Bubbles, b => Assert.InRange(
            b.Lifetime, BubbleCountRun.MinLifetime, BubbleCountRun.MinLifetime + BubbleCountRun.LifetimeSpan));

        // Second: the pop itself. Upstream fades opacity by 0.12 and grows scale by 0.08 per 30 ms
        // tick (:1755-1762). A pop that neither faded nor grew would be a bubble that simply
        // vanished, and the animation would be decoration rather than the thing that tells a user
        // it POPPED.
        var bubble = run.Bubbles[0];
        var (midScale, midOpacity) = BubbleCountRun.Animation(bubble, bubble.PopsAt + (BubbleCountRun.PopDuration / 2));
        Assert.True(midOpacity is > 0 and < 1, $"a popping bubble's opacity was {midOpacity}");
        Assert.True(midScale > 1.0, $"a popping bubble's scale was {midScale}");

        // And before it pops it is fully opaque, growing from upstream's own birth scale.
        var (bornScale, bornOpacity) = BubbleCountRun.Animation(bubble, bubble.BornAt);
        Assert.Equal(1.0, bornOpacity);
        Assert.Equal(BubbleCountRun.BirthScale, bornScale, precision: 10);
    }

    // ---------------------------------------------------------------------------------------
    //  THE ANSWER MACHINE
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData('4', true)]
    [InlineData('0', true)]
    [InlineData('a', false)]
    [InlineData(' ', false)]
    [InlineData('-', false)]
    public void DIGITSOnly_AndEveryOtherPrintableCharacterIsIgnored(char character, bool accepted)
    {
        // Upstream refuses everything else at its TextBox's PreviewTextInput (:98-101), so a letter
        // never reaches its box; here the capability delivers every printable character and this is
        // the refusal.
        var answer = new BubbleCountAnswer(7);
        var step = answer.Apply(character, isCharacter: true, isBackspace: false, isCancel: false, virtualKey: character);

        Assert.Equal(accepted ? BubbleCountStep.Typed : BubbleCountStep.Ignored, step);
        Assert.Equal(accepted ? character.ToString() : string.Empty, answer.Typed);
    }

    [Fact]
    public void ENTERSubmits_AndItArrivesAsTheCapabilitysUNNAMEDKeyKind()
    {
        // THE SEAM FINDING, pinned as a fact. InputKeystrokeKind names Character, Backspace, Cancel
        // and Key — the three keys the LOCK CARD cared about, plus a catch-all. A second consumer
        // needs a SUBMIT key, and it arrives as Key with a raw virtual-key code; this module reads
        // it rather than growing the capability with a kind per caller.
        Assert.Equal(0x0D, BubbleCountAnswer.SubmitVirtualKey);

        var answer = new BubbleCountAnswer(7);
        Type(answer, "7");
        var step = answer.Apply(
            '\0', isCharacter: false, isBackspace: false, isCancel: false,
            virtualKey: BubbleCountAnswer.SubmitVirtualKey);

        Assert.Equal(BubbleCountStep.Correct, step);
        Assert.True(answer.Solved);

        // And any OTHER unnamed key is still nothing at all.
        var other = new BubbleCountAnswer(7);
        Assert.Equal(
            BubbleCountStep.Ignored,
            other.Apply('\0', isCharacter: false, isBackspace: false, isCancel: false, virtualKey: 0x70));
    }

    [Fact]
    public void AnEmptyEnterSpendsNOAttempt_BecauseUpstreamReturnsBeforeItsCounter()
    {
        var answer = new BubbleCountAnswer(7);
        var step = Submit(answer);

        // Upstream shows "Please enter a number!", clears the box and RETURNS — above the attempt
        // counter (:190-195). A user who leans on Enter must not lose the game.
        Assert.Equal(BubbleCountStep.NotANumber, step);
        Assert.Equal(BubbleCountAnswer.DefaultAttempts, answer.AttemptsRemaining);
        Assert.Equal(0, answer.GuessesMade);
        Assert.Equal(BubbleCountAnswer.NotANumberMessage, answer.Feedback);
    }

    [Theory]
    [InlineData("3", 7, true)]
    [InlineData("9", 7, false)]
    public void AWrongGuessSpendsAnAttemptAndSaysWHICHWay(string typed, int correct, bool tooLow)
    {
        var answer = new BubbleCountAnswer(correct);
        Type(answer, typed);
        var step = Submit(answer);

        Assert.Equal(BubbleCountStep.Missed, step);
        Assert.Equal(BubbleCountAnswer.DefaultAttempts - 1, answer.AttemptsRemaining);
        Assert.Equal(
            tooLow ? BubbleCountAnswer.TooLowMessage : BubbleCountAnswer.TooHighMessage, answer.Feedback);

        // Upstream clears the box after a wrong guess (:249).
        Assert.Equal(string.Empty, answer.Typed);
    }

    [Fact]
    public void ThreeWrongGuessesEXHAUSTTheCard()
    {
        var answer = new BubbleCountAnswer(7);
        for (var i = 0; i < 2; i++)
        {
            Type(answer, "1");
            Assert.Equal(BubbleCountStep.Missed, Submit(answer));
        }

        Type(answer, "1");
        Assert.Equal(BubbleCountStep.Exhausted, Submit(answer));
        Assert.Equal(0, answer.AttemptsRemaining);
        Assert.False(answer.Solved);
    }

    [Fact]
    public void BackspaceEdits_AndClearsTheFeedbackTheLastGuessLeft()
    {
        var answer = new BubbleCountAnswer(7);
        Type(answer, "12");
        Assert.Equal(BubbleCountStep.Missed, Submit(answer));
        Assert.NotEqual(string.Empty, answer.Feedback);

        // Backspace on an empty box is nothing at all.
        Assert.Equal(
            BubbleCountStep.Ignored,
            answer.Apply('\0', isCharacter: false, isBackspace: true, isCancel: false, virtualKey: 0x08));

        Type(answer, "45");
        Assert.Equal(
            BubbleCountStep.Typed,
            answer.Apply('\0', isCharacter: false, isBackspace: true, isCancel: false, virtualKey: 0x08));
        Assert.Equal("4", answer.Typed);
        Assert.Equal(string.Empty, answer.Feedback);
    }

    [Fact]
    public void EscapeALWAYSGivesUp_BecauseUpstreamsOwnFallOpenRuleAppliesHereToo()
    {
        // Upstream gates Escape on strict mode alone (:171-178) and then needs a 120 s inactivity
        // watchdog to rescue the user it strands — its own #633 comment says exactly that (:28-32).
        // This build has no panic-key hook, so D112's fall-open rule leaves Escape live on every
        // card, and with Escape live the watchdog has no user to rescue. Absent rather than ported
        // into a build where its cause cannot occur.
        var answer = new BubbleCountAnswer(7);
        Assert.Equal(
            BubbleCountStep.GaveUp,
            answer.Apply('\0', isCharacter: false, isBackspace: false, isCancel: true, virtualKey: 0x1B));
    }

    [Fact]
    public void THEFOLD_TheProgressSlotCarriesBOTHOfUpstreamsLines_BecauseTheCapabilityHasFOUR()
    {
        // THE SEAM FINDING, pinned as a fact. InputPromptContent has four slots — question,
        // progress, answer, hint — and upstream's result window has five lines: title, attempts,
        // answer, feedback and the Esc hint. This module FOLDS the feedback into the attempts slot
        // rather than growing the capability's content record for its second consumer, and the fold
        // is honest because the two change together.
        var answer = new BubbleCountAnswer(7);
        Assert.Equal("Attempts remaining: 3", answer.Progress);

        Type(answer, "1");
        Submit(answer);
        Assert.Contains(BubbleCountAnswer.TooLowMessage, answer.Progress, StringComparison.Ordinal);
        Assert.Contains("Attempts remaining: 2", answer.Progress, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------
    //  THE MODULE'S ARM OUTCOMES — TWO channels, so TWO Unavailable codes
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void WithNoDISPLAYTheModuleIsUnavailable_AndCarriesTheVIDEOCode()
    {
        using var rig = new Rig();
        rig.Surface.CanReachADisplay = false;
        rig.Surface.LastPlacement = new CapabilityState.Unavailable(new CapabilityReason(
            VideoReasonCodes.VideoMechanismAbsent, "the video capability's own words, carried verbatim"));
        rig.Enable();

        var state = Assert.IsType<CapabilityState.Unavailable>(rig.Effect.Arm());
        Assert.Equal(EffectReasonCodes.VideoSurfaceUnavailable, state.Reason.Code);
        Assert.Contains("carried verbatim", state.Reason.Detail, StringComparison.Ordinal);
        Assert.Equal(EffectDotState.Armed, rig.Effect.Dot);
    }

    [Fact]
    public void WithNoDESKTOPTheModuleIsUnavailable_AndCarriesTheINPUTCode()
    {
        using var rig = new Rig();
        rig.Presence.CanReachAUser = false;
        rig.Enable();

        var state = Assert.IsType<CapabilityState.Unavailable>(rig.Effect.Arm());

        // A DIFFERENT code from the one above, because a game that can show its clip and cannot ask
        // its question is not the same failure as one that has nowhere to play.
        Assert.Equal(EffectReasonCodes.InputCaptureUnavailable, state.Reason.Code);
        Assert.Contains("window-station-visible=False", state.Reason.Detail, StringComparison.Ordinal);
        Assert.Equal(EffectDotState.Armed, rig.Effect.Dot);
    }

    [Fact]
    public void WithNEITHERChannel_BOTHCausesTravel()
    {
        using var rig = new Rig();
        rig.Surface.CanReachADisplay = false;
        rig.Presence.CanReachAUser = false;
        rig.Enable();

        var state = Assert.IsType<CapabilityState.Unavailable>(rig.Effect.Arm());

        // The established rule, after its opposite shipped once: where both causes are true, BOTH
        // travel. The CODE is the video one because the game cannot even begin without a picture.
        Assert.Equal(EffectReasonCodes.VideoSurfaceUnavailable, state.Reason.Code);
        Assert.Contains("reach a display", state.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("reach a user", state.Reason.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyPoolDEGRADES_AndTheDotStaysLIVE()
    {
        using var rig = new Rig();
        rig.Enable();

        var state = Assert.IsType<CapabilityState.Degraded>(rig.Effect.Arm());
        Assert.Equal(EffectReasonCodes.VideoNoClip, state.Reason.Code);
        Assert.Contains("no video in", state.Reason.Detail, StringComparison.Ordinal);

        // A pool is CONTENT, not a channel: dropping a clip in mid-session is picked up at the next
        // game with no re-arm. The Subliminals answer.
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);
    }

    [Fact]
    public void AHealthyRunIsAVAILABLE_UnlikeTheTwoHalfRowsBesideIt()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("clip.mp4");
        rig.Enable();

        // Bubble Count is a WHOLE row: the port has both capabilities it needs. Brain Drain and
        // Mandatory Video are Degraded on every run because they are halves of upstream's rows, and
        // this one must not copy that shape just because it is nearby.
        Assert.IsType<CapabilityState.Available>(rig.Effect.Arm());
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);
    }

    [Fact]
    public void ADisabledModuleSaysSoInTypeRatherThanArmingSilently()
    {
        using var rig = new Rig();
        var state = Assert.IsType<CapabilityState.Unavailable>(rig.Effect.Arm());
        Assert.Equal(EffectReasonCodes.EffectDialOff, state.Reason.Code);
        Assert.Equal(EffectDotState.Off, rig.Effect.Dot);
    }

    // ---------------------------------------------------------------------------------------
    //  THE DOT — two existing meanings, no eighth
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ALLFIVEClausesOfTheDotAreLoadBearing()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("clip.mp4");
        rig.Enable();
        rig.Effect.Arm();

        // Clause 1 — a firing on the clock.
        Assert.True(rig.Effect.ScheduleArmed);
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);

        // Clause 2 — the OS says a picture could reach somebody.
        rig.Surface.CanReachADisplay = false;
        Assert.Equal(EffectDotState.Armed, rig.Effect.Dot);
        rig.Surface.CanReachADisplay = true;

        // Clause 3 — the OS says a window could reach somebody.
        rig.Presence.CanReachAUser = false;
        Assert.Equal(EffectDotState.Armed, rig.Effect.Dot);
        rig.Presence.CanReachAUser = true;
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);

        // Clause 4 — MOTION (the dot's seventh meaning), while THIS module's clip is up.
        rig.Clock.AdvanceToNextDue();
        Assert.True(rig.Effect.Playing);
        rig.Surface.Running = false;
        Assert.Equal(EffectDotState.Armed, rig.Effect.Dot);
        rig.Surface.Running = true;
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);

        // Clause 5 — DEMAND (the dot's sixth meaning), while THIS module's question is up. The clip
        // has to have really shown bubbles first: a clip that showed none is abandoned and never
        // asked about, which is its own fact below.
        rig.Surface.PaintFrames(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(100));
        rig.Surface.RaiseEnded();
        Assert.True(rig.Effect.Asking);
        rig.Presence.HoldsTheInput = false;
        Assert.Equal(EffectDotState.Armed, rig.Effect.Dot);
        rig.Presence.HoldsTheInput = true;
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);

        // AND BOTH PHASE CLAUSES ARE DISJUNCTIONS. With nothing of this module's up, the dot is Live
        // even though nothing is moving and nothing holds the input — a session spends almost all of
        // its time between games, and requiring either unconditionally would darken the dot for
        // nearly all of it.
        rig.Presence.PressEscape();
        Assert.False(rig.Effect.Playing);
        Assert.False(rig.Effect.Asking);
        rig.Surface.Running = false;
        rig.Presence.HoldsTheInput = false;
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);
    }

    [Fact]
    public void ANOTHERRowsClipOrCardNeverDarkensTHISRowsDot()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("clip.mp4");
        rig.Enable();
        rig.Effect.Arm();

        // THE SINGLE-TENANCY FINDING, REACHING THE DOT. Both capabilities are SHARED — the surface
        // with Mandatory Video, the presence with the Lock Card — so a dot that read the
        // capability's state alone would report this module as broken whenever a NEIGHBOUR was
        // working. Here the surface is showing somebody else's clip and the presence is holding
        // somebody else's card, and this module is idle and healthy.
        rig.Surface.Showing = true;
        rig.Surface.Running = false;
        rig.Presence.SimulateForeignCard();
        Assert.False(rig.Effect.Playing);
        Assert.False(rig.Effect.Asking);
        Assert.Equal(EffectDotState.Live, rig.Effect.Dot);
    }

    // ---------------------------------------------------------------------------------------
    //  THE GAME, END TO END
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void AGamePlaysADrawnClipWithABubblePainterOnIt_AndTheEVENTCarriesNoPathAndNoCOUNT()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable(difficulty: BubbleCountDifficulty.Hard);
        rig.Effect.Arm();

        BubbleCountEvent? started = null;
        rig.Effect.Started += e => started = e;
        rig.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(["only.mp4"], rig.Surface.Begun);
        Assert.Equal(1, rig.Effect.PlayedCount);
        Assert.NotNull(started);
        Assert.Equal(1, started!.Value.Ordinal);
        Assert.Equal(BubbleCountDifficulty.Hard, started.Value.Difficulty);

        // THE PAINTER REALLY REACHES THE CAPABILITY. This is the seam this packet added, and without it
        // the clip would play with nothing on it to count.
        var painter = Assert.IsType<BubbleCountRun>(rig.Surface.LastPainter);
        Assert.Equal(BubbleCountDifficulty.Hard, painter.Difficulty);

        // No path and no COUNT on the event: the clips are the user's own media, and the number of
        // bubbles is the ANSWER — an event that carried it would put the answer in every log line
        // the day one is written.
        var text = started.Value.ToString();
        Assert.DoesNotContain("only.mp4", text, StringComparison.Ordinal);
        Assert.Equal(
            ["Ordinal", "At", "Difficulty"],
            typeof(BubbleCountEvent).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void THEFIRSTGameIsDueAtTHISMODULESOwnInterval_NotAtSomeOtherRowsPace()
    {
        // Found by the mutation sweep: replacing the module's NextInterval with a constant survived,
        // because every other fact advances to whatever the next due moment happens to be. The
        // interval is upstream's arithmetic on this module's own dial, and a fact that never says
        // which number it is cannot tell a re-paced module from a mis-paced one.
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable(perHour: 4);
        var armedAt = rig.Clock.UtcNow;
        rig.Effect.Arm();

        // The rig's scripted roll is 0.5, so the interval is exactly the base: 3600/4 = 900 s.
        Assert.Equal(armedAt + BubbleCountSchedule.Interval(4, 0.5), rig.Clock.NextDue);
        Assert.Equal(armedAt + TimeSpan.FromSeconds(900), rig.Clock.NextDue);

        // And it is NOT the video module's law. The JITTER arithmetic is identical — upstream writes
        // it two ways that reduce to the same expression, which BubbleCountSchedule's own remarks
        // say out loud — so the difference a user feels is the CLAMP: the same dial of 20 is ten
        // games an hour here and twenty clips an hour there (BubbleCountService.cs:88 against
        // VideoService.cs:2225 with ProgramDefinition.cs:442).
        Assert.Equal(BubbleCountSchedule.Interval(1, 0.0), MandatoryVideoSchedule.Interval(1, 0.0));
        Assert.NotEqual(BubbleCountSchedule.Interval(20, 0.5), MandatoryVideoSchedule.Interval(20, 0.5));
        Assert.Equal(TimeSpan.FromSeconds(360), BubbleCountSchedule.Interval(20, 0.5));
        Assert.Equal(TimeSpan.FromSeconds(180), MandatoryVideoSchedule.Interval(20, 0.5));
    }

    [Theory]
    [InlineData("a clip is already showing")]
    [InlineData("a card is already up")]
    [InlineData("no display")]
    [InlineData("no desktop")]
    [InlineData("empty pool")]
    public void AGameThatCanRunNOTHINGCountsNothing_AndKeepsTheScheduleRunning(string situation)
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();

        switch (situation)
        {
            case "a clip is already showing":
                rig.Surface.Showing = true;
                break;
            case "a card is already up":
                rig.Presence.SimulateForeignCard();
                break;
            case "no display":
                rig.Surface.CanReachADisplay = false;
                break;
            case "no desktop":
                rig.Presence.CanReachAUser = false;
                break;
            case "empty pool":
                rig.Pool.Clips.Clear();
                break;
        }

        rig.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Empty(rig.Surface.Begun);
        Assert.Equal(0, rig.Effect.PlayedCount);
        Assert.True(
            rig.Effect.ScheduleArmed,
            "a game that showed nothing must still re-arm: upstream's scheduler keeps running through "
            + "every one of these (BubbleCountService.cs:105-111)");
    }

    [Fact]
    public void WhenTheClipENDSTheQuestionIsAsked_AboutTheBubblesTHATWEREREALLYSHOWN()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.AdvanceToNextDue();

        // Paint some pictures the way the presenter would, then end the clip.
        var run = Assert.IsType<BubbleCountRun>(rig.Surface.LastPainter);
        rig.Surface.PaintFrames(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(100));
        var shown = run.BubblesShown;
        Assert.True(shown > 0);

        rig.Surface.RaiseEnded();

        // THE QUESTION IS ABOUT THE BUBBLES THE OPERATING SYSTEM WAS HOLDING, never about the
        // target the clip would have carried had it run to its end.
        var prompt = Assert.Single(rig.Presence.Prompts);
        Assert.Equal(BubbleCountAnswer.Question, prompt.Content.Question);
        Assert.Equal(shown, rig.Effect.LastAskedAbout);
        Assert.NotEqual(run.Target, rig.Effect.LastAskedAbout);
        Assert.True(rig.Effect.Asking);

        // And the answer the card is judging is that same number.
        Assert.Equal(shown, rig.Effect.Answer!.CorrectAnswer);
    }

    [Fact]
    public void ACLIPTheSURFACEREFUSEDOutrightIsABANDONEDToo_AndTheUserIsToldSomethingHappened()
    {
        // Found by the mutation sweep: nothing pinned the resolution on Deliver's OWN refusal path.
        // Without it the run is orphaned - LastResolution stays None, AbandonedCount never moves,
        // and the panel tells a user nothing at all about a game that came due and could not play.
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Surface.BeginResult = new CapabilityState.Unavailable(new CapabilityReason(
            VideoReasonCodes.VideoClipUnreadable, "the operating system would not open it"));

        var resolutions = new List<BubbleCountResolution>();
        rig.Effect.Resolved += resolutions.Add;
        rig.Clock.AdvanceToNextDue();

        Assert.Equal([BubbleCountResolution.Abandoned], resolutions);
        Assert.Equal(BubbleCountResolution.Abandoned, rig.Effect.LastResolution);
        Assert.Equal(1, rig.Effect.AbandonedCount);

        // The capability's own typed outcome is REMEMBERED verbatim, which is the only place a user
        // or a bug report learns WHICH refusal it was.
        var remembered = Assert.IsType<CapabilityState.Unavailable>(rig.Effect.LastPlayback);
        Assert.Equal(VideoReasonCodes.VideoClipUnreadable, remembered.Reason.Code);

        // Nothing was asked, and the schedule keeps running.
        Assert.Empty(rig.Presence.Prompts);
        Assert.True(rig.Effect.ScheduleArmed);
    }

    [Fact]
    public void AKeystrokeForAQuestionTHISMODULEHasFinishedWith_IsDiscarded()
    {
        // Found by the mutation sweep: the identity guard had no fact, because the double stops
        // feeding the callback the moment it is dismissed. The OS does not: it delivers what was
        // already in its queue, and the Lock Card found the same hazard from the other side - a stale
        // keystroke applied to a finished attempt would count a guess nobody made.
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.AdvanceToNextDue();
        rig.Surface.PaintFrames(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(100));
        rig.Surface.RaiseEnded();

        // Keep the capability's own callback, exactly as a message already in the OS's queue is
        // kept, then end the game.
        var stillQueued = rig.Presence.Prompts[0].OnKeystroke;
        rig.Presence.PressEscape();
        Assert.Equal(BubbleCountResolution.Dismissed, rig.Effect.LastResolution);
        var dismissals = rig.Presence.Dismissals;

        // The late keystroke must do nothing at all: no repaint, no second resolution, no counter.
        var updates = rig.Presence.Updates.Count;
        stillQueued(new InputKeystroke(InputKeystrokeKind.Character, '5', '5'));
        stillQueued(new InputKeystroke(InputKeystrokeKind.Key, '\0', BubbleCountAnswer.SubmitVirtualKey));

        Assert.Equal(updates, rig.Presence.Updates.Count);
        Assert.Equal(dismissals, rig.Presence.Dismissals);
        Assert.Equal(BubbleCountResolution.Dismissed, rig.Effect.LastResolution);
        Assert.Equal(0, rig.Effect.CountedCount);
        Assert.Equal(0, rig.Effect.MissedCount);
    }

    [Fact]
    public void ASurfaceThatSTOPPEDHoldingThePictureABANDONSTheGame_AndAsksNOTHING()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.AdvanceToNextDue();
        rig.Surface.PaintFrames(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(100));

        // The presenter ends a clip whose surface stopped holding the picture, and its end-callback
        // is the SAME callback a clean ending uses. The capability's own last typed outcome is what
        // tells the two apart.
        rig.Surface.LastPlacement = new CapabilityState.Unavailable(new CapabilityReason(
            VideoReasonCodes.VideoFrameNotHeld, "the OS's copy stopped carrying it"));
        rig.Surface.RaiseEnded();

        // Upstream's own answer to a game it could not really show: skip it OUTRIGHT rather than let
        // it land as a failure (:224-233). Nothing is asked, nothing is scored.
        Assert.Empty(rig.Presence.Prompts);
        Assert.Equal(BubbleCountResolution.Abandoned, rig.Effect.LastResolution);
        Assert.Equal(1, rig.Effect.AbandonedCount);
        Assert.Equal(0, rig.Effect.MissedCount);
        Assert.True(rig.Effect.ScheduleArmed);
    }

    [Fact]
    public void AClipThatShowedNOBubbleIsABANDONED_RatherThanAskedAbout()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.AdvanceToNextDue();

        // The clip ended before the 1500 ms lead-in, so not one bubble was ever drawn into a
        // picture. "How many bubbles?" about a clip that carried none is a question with a cruel
        // answer, and upstream cannot produce this state at all — its bubbles ride a wall clock
        // rather than the pictures.
        rig.Surface.PaintFrames(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(100));
        Assert.Equal(0, ((BubbleCountRun)rig.Surface.LastPainter!).BubblesShown);

        rig.Surface.RaiseEnded();

        Assert.Empty(rig.Presence.Prompts);
        Assert.Equal(BubbleCountResolution.Abandoned, rig.Effect.LastResolution);
    }

    [Fact]
    public void AREFUSEDQuestionTakesItselfBackDown_AndTheGameIsREFUSEDRatherThanMissed()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.AdvanceToNextDue();
        rig.Surface.PaintFrames(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(100));

        // DEGRADED, which is the case that traps a user: the OS gave the card the keyboard and only
        // the ink read-back said no, so the window is still up. The Lock Card's load-bearing dismiss.
        rig.Presence.NextPromptOutcome = new CapabilityState.Degraded(
            "the OS gave it the keyboard",
            new CapabilityReason(InputReasonCodes.InputPromptNotInked, "and nothing legible reached it"));
        rig.Surface.RaiseEnded();

        Assert.Single(rig.Presence.Prompts);
        Assert.Equal(1, rig.Presence.Dismissals);
        Assert.False(rig.Presence.IsPrompting);
        Assert.Equal(BubbleCountResolution.Refused, rig.Effect.LastResolution);

        // REFUSED, not Missed: the user did not get it wrong, they were never asked. The counters
        // keep those apart because a panel that reported one as the other would be telling a user
        // they failed a game the operating system refused to show them.
        Assert.Equal(0, rig.Effect.MissedCount);
        Assert.Equal(0, rig.Effect.CountedCount);

        // And the capability's own typed outcome is REMEMBERED, verbatim — the only place a user or
        // a bug report can learn which of the two opposite refusals happened.
        Assert.IsType<CapabilityState.Degraded>(rig.Effect.LastPrompt);
    }

    [Fact]
    public void ASECONDModulesCardBlocksTheQuestion_AndTheGameStandsDownRatherThanSTEALINGIt()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.AdvanceToNextDue();
        rig.Surface.PaintFrames(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(100));

        // THE SINGLE-TENANCY RESIDUE, closed in this module's own direction. Prompting over a live
        // card would silently replace its content AND its keystroke callback inside the shared
        // presence, stranding the other module's card for the rest of the session.
        rig.Presence.SimulateForeignCard();
        rig.Surface.RaiseEnded();

        Assert.Empty(rig.Presence.Prompts);
        Assert.Equal(0, rig.Presence.Dismissals);
        Assert.Equal(BubbleCountResolution.Abandoned, rig.Effect.LastResolution);
    }

    [Fact]
    public void TheSAFETYEndForcesTheClipDownAtItsOwnLengthPlusFive_AndSTILLAsks()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.AdvanceToNextDue();

        var run = Assert.IsType<BubbleCountRun>(rig.Surface.LastPainter);
        rig.Surface.PaintFrames(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(100));
        Assert.True(rig.Effect.Playing);

        // Upstream's safety timer, armed once the clip's length is known and firing at length + 5 s
        // (Windows/BubbleCountWindow.xaml.cs:1179-1195, :611). A clip that neither ends nor faults
        // would otherwise hold the user for ever — upstream's own worst video failure
        // (VideoService.cs:2677-2678).
        rig.Clock.Advance(run.Duration + BubbleCountArithmetic.SafetyMargin);

        Assert.Equal(1, rig.Surface.Ends);
        Assert.False(rig.Effect.Playing);

        // AND IT STILL ASKS: the bubbles that were drawn were really drawn, and the count is about
        // them. Upstream forces the video end and shows its result window too (:1185-1192).
        Assert.Single(rig.Presence.Prompts);
        Assert.Equal(run.BubblesShown, rig.Effect.LastAskedAbout);
    }

    [Fact]
    public void GettingItRightCOUNTS_AndGettingItWrongThreeTimesMISSES()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.AdvanceToNextDue();
        rig.Surface.PaintFrames(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(100));
        rig.Surface.RaiseEnded();

        var resolutions = new List<BubbleCountResolution>();
        rig.Effect.Resolved += resolutions.Add;
        var correct = rig.Effect.LastAskedAbout.ToString(System.Globalization.CultureInfo.InvariantCulture);

        rig.Presence.TypeCharacters(correct);
        rig.Presence.PressEnter();

        Assert.Equal([BubbleCountResolution.Counted], resolutions);
        Assert.Equal(1, rig.Effect.CountedCount);
        Assert.Equal(1, rig.Presence.Dismissals);
        Assert.False(rig.Effect.Asking);

        // The card repaints on every keystroke that changed something — the echo and the attempt
        // line are what the user reads.
        Assert.NotEmpty(rig.Presence.Updates);

        // And a second game answered wrongly three times MISSES rather than counting.
        rig.Clock.AdvanceToNextDue();
        rig.Surface.PaintFrames(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(100));
        rig.Surface.RaiseEnded();
        for (var i = 0; i < 3; i++)
        {
            rig.Presence.TypeCharacters("99999");
            rig.Presence.PressEnter();
        }

        Assert.Equal(BubbleCountResolution.Missed, rig.Effect.LastResolution);
        Assert.Equal(1, rig.Effect.MissedCount);
        Assert.Equal(1, rig.Effect.CountedCount);
    }

    [Fact]
    public void EscapeAtTheQuestionDISMISSESTheGame_AndTheCardComesDown()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.AdvanceToNextDue();
        rig.Surface.PaintFrames(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(100));
        rig.Surface.RaiseEnded();

        rig.Presence.PressEscape();

        Assert.Equal(BubbleCountResolution.Dismissed, rig.Effect.LastResolution);
        Assert.Equal(1, rig.Presence.Dismissals);
        Assert.False(rig.Presence.IsPrompting);
        Assert.Equal(0, rig.Effect.CountedCount);
        Assert.Equal(0, rig.Effect.MissedCount);
    }

    [Fact]
    public void TheGameREPACESFromTheEND_SoALongClipIsNotFollowedImmediately()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable(perHour: 1);
        // A long clip, so the SAFETY end cannot land inside the twenty minutes below — this fact is
        // about the ordinary ending's re-pace and nothing else.
        rig.Surface.ClipDuration = TimeSpan.FromMinutes(30);
        rig.Effect.Arm();
        rig.Clock.AdvanceToNextDue();
        Assert.Single(rig.Surface.Begun);

        var dueAfterStart = rig.Clock.NextDue;
        rig.Clock.Advance(TimeSpan.FromMinutes(20));
        rig.Surface.PaintFrames(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(100));
        rig.Surface.RaiseEnded();
        rig.Presence.PressEscape();

        Assert.True(
            rig.Clock.NextDue > dueAfterStart,
            $"the game ending must push the next one out; it was due {dueAfterStart} and is now due "
            + $"{rig.Clock.NextDue}");
    }

    // ---------------------------------------------------------------------------------------
    //  TEARDOWN — and every call is GUARDED, which is this packet's finding in code
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void DisarmTakesDownOnlyWHATTHISMODULEPutUp()
    {
        using var rig = new Rig();
        rig.Enable();
        rig.Effect.Arm();

        // BOTH capabilities are SHARED. With no game of this module's running, a disarm that called
        // End() and Dismiss() unconditionally would tear down MANDATORY VIDEO's clip and take down
        // a LOCK CARD — neither of which is this module's work.
        rig.Surface.Showing = true;
        rig.Presence.SimulateForeignCard();

        rig.Effect.Disarm();

        Assert.Equal(0, rig.Surface.Ends);
        Assert.Equal(0, rig.Presence.Dismissals);
        Assert.True(rig.Surface.Showing);
        Assert.True(rig.Presence.IsPrompting);
    }

    [Fact]
    public void DisarmWithdrawsThisModulesOwnClipAndItsOwnQuestion()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.AdvanceToNextDue();

        // A clip of this module's IS up, so disarm ends it — the positive control for the fact
        // above, without which "never ends anything" would satisfy both.
        rig.Effect.Disarm();
        Assert.Equal(1, rig.Surface.Ends);
        Assert.Equal(BubbleCountResolution.Withdrawn, rig.Effect.LastResolution);
        Assert.False(rig.Effect.ScheduleArmed);

        // And a live QUESTION comes down the same way.
        using var second = new Rig();
        second.Pool.Clips.Add("only.mp4");
        second.Enable();
        second.Effect.Arm();
        second.Clock.AdvanceToNextDue();
        second.Surface.PaintFrames(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(100));
        second.Surface.RaiseEnded();
        Assert.True(second.Effect.Asking);

        second.Effect.Disarm();
        Assert.Equal(1, second.Presence.Dismissals);
        Assert.Equal(BubbleCountResolution.Withdrawn, second.Effect.LastResolution);
    }

    [Fact]
    public void TheSAFETYEndIsCancelledByAClipThatEndsProperly_SoNoStrayEndArrivesLater()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable();
        rig.Effect.Arm();
        rig.Clock.AdvanceToNextDue();
        rig.Surface.PaintFrames(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(100));
        rig.Surface.RaiseEnded();
        Assert.True(rig.Effect.Asking);

        // A safety end that survived a clean ending would land in the middle of the QUESTION and
        // take the surface down under a second game later in the session.
        rig.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, rig.Surface.Ends);
        Assert.True(rig.Effect.Asking);
    }

    // ---------------------------------------------------------------------------------------
    //  THE DIALS
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TheDialsClampsAreUpstreamsOwn_AndStrictModeIsABSENTRatherThanInert()
    {
        var document = new BubbleCountPresetDocument();
        Assert.False(document.Enabled);
        Assert.Equal(BubbleCountSchedule.DefaultPerHour, document.PerHour);
        Assert.Equal(BubbleCountDifficulty.Medium, document.Difficulty);

        document.PerHour = 0;
        Assert.Equal(BubbleCountSchedule.MinPerHour, document.PerHour);
        document.PerHour = 9999;
        Assert.Equal(BubbleCountSchedule.MaxPerHour, document.PerHour);

        // A difficulty outside the three falls to Medium, which is upstream's own fall-through arm
        // (Windows/BubbleCountWindow.xaml.cs:1144) rather than a clamp this port invented.
        document.Difficulty = (BubbleCountDifficulty)77;
        Assert.Equal(BubbleCountDifficulty.Medium, document.Difficulty);

        // STRICT MODE IS NOT HERE. Upstream's strict lock does two things — remove Escape and start
        // the WRONG!/WATCH AGAIN retry loop — and this build has neither, so a strict switch would
        // move nothing. D93's rule: absent rather than present-and-inert.
        Assert.DoesNotContain(
            "strict",
            string.Join(
                ",", typeof(BubbleCountPresetDocument).GetProperties().Select(p => p.Name)),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheDifficultyDialChangesTheNEXTGameAndNeverTheOneOnScreen()
    {
        using var rig = new Rig();
        rig.Pool.Clips.Add("only.mp4");
        rig.Enable(difficulty: BubbleCountDifficulty.Easy);
        rig.Effect.Arm();
        rig.Clock.AdvanceToNextDue();

        var run = Assert.IsType<BubbleCountRun>(rig.Surface.LastPainter);
        Assert.Equal(BubbleCountDifficulty.Easy, run.Difficulty);

        // Upstream reads the setting once, at trigger time (Services/BubbleCountService.cs:243). A
        // target that moved under a user mid-clip would be a count nobody could get right.
        rig.Effect.SetDifficulty(BubbleCountDifficulty.Hard);
        Assert.Equal(BubbleCountDifficulty.Easy, run.Difficulty);
        Assert.Equal(BubbleCountDifficulty.Hard, rig.Effect.Preset.Difficulty);
    }

    // ---------------------------------------------------------------------------------------
    //  THE SHARED PLACEMENT — an earlier finding, arriving a second time
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ALLTHREEPlacementsShareOneHelper_RatherThanCarryingAThirdCopyOfIt()
    {
        // The centring arithmetic is one function now. The Lock Card was given a private copy and
        // the video surface another; this packet was about to write the third, which is
        // exactly the shape refused earlier for the effect template.
        var display = new CcpClient.Desktop.Overlay.OverlayBounds(100, 200, 1000, 800);

        var card = PrimaryDisplayPlacement.Centred(
            display, BubbleCountEffect.CardWidthFraction, BubbleCountEffect.CardHeightFraction);
        Assert.Equal((100 + ((1000 - 550) / 2), 200 + ((800 - 304) / 2), 550, 304), card);

        // What is NOT shared is each caller's own fractions, and that is deliberate: they are
        // per-module divergences with their own D-records (D110 for the card, D123 for the surface).
        var surface = PrimaryDisplayPlacement.Centred(
            display, VideoSurfacePresenter.WidthFraction, VideoSurfacePresenter.HeightFraction);
        Assert.NotEqual(card, surface);

        // A side floored at one pixel, because a zero-width rectangle is an exception at every
        // request boundary in this port rather than a small window.
        var tiny = PrimaryDisplayPlacement.Centred(
            new CcpClient.Desktop.Overlay.OverlayBounds(0, 0, 1, 1), 0.001, 0.001);
        Assert.Equal((0, 0, 1, 1), tiny);
    }

    // ---------------------------------------------------------------------------------------
    //  fixtures
    // ---------------------------------------------------------------------------------------

    private static void Type(BubbleCountAnswer answer, string digits)
    {
        foreach (var c in digits)
        {
            answer.Apply(c, isCharacter: true, isBackspace: false, isCancel: false, virtualKey: c);
        }
    }

    private static BubbleCountStep Submit(BubbleCountAnswer answer) =>
        answer.Apply(
            '\0', isCharacter: false, isBackspace: false, isCancel: false,
            virtualKey: BubbleCountAnswer.SubmitVirtualKey);

    private sealed class Rig : IDisposable
    {
        private readonly OperationRegistry _registry = new();
        private readonly string _directory;

        public Rig()
        {
            _directory = Path.Combine(Path.GetTempPath(), "ccp-sp112-mod-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());
            Clock = new ManualClock();
            Surface = new RecordingVideoSurface();
            Presence = new RecordingInputPresence();
            Pool = new StubVideoPool(Path.Combine(_directory, "videos"));
            Preset = new PersistenceStore<BubbleCountPresetDocument>(
                _registry.OwnerFor("BubbleCountPreset"), new NullLog(),
                Path.Combine(_directory, BubbleCountPresetDocument.FileName),
                BubbleCountPresetDocument.CurrentSchemaVersion);

            Effect = new BubbleCountEffect(
                _registry.OwnerFor("BubbleCount"),
                new EffectSignal(boundary, static () => true),
                Clock,
                Preset,
                Pool,
                Surface,
                Presence,
                // A scripted draw: the SPACING and the PLACEMENT are what these facts make
                // deterministic, and the arithmetic they are fed into stays the module's own.
                new SequenceRandom([0.5]),
                () => new InputBounds(0, 0, 400, 300));
        }

        public ManualClock Clock { get; }

        public RecordingVideoSurface Surface { get; }

        public RecordingInputPresence Presence { get; }

        public StubVideoPool Pool { get; }

        public PersistenceStore<BubbleCountPresetDocument> Preset { get; }

        public BubbleCountEffect Effect { get; }

        public void Enable(
            int perHour = BubbleCountSchedule.DefaultPerHour,
            BubbleCountDifficulty difficulty = BubbleCountDifficulty.Medium) =>
            Preset.Mutate(p =>
            {
                p.Enabled = true;
                p.PerHour = perHour;
                p.Difficulty = difficulty;
            });

        public void Dispose()
        {
            Effect.Disarm();
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
    /// A video surface that records what it was asked to do and never touches a window or a decoder.
    ///
    /// <para>It MIRRORS the product where the product's own transitions matter — <c>Begin</c> marks
    /// the surface showing only when it succeeded, hands the painter its <c>Opening</c> exactly as
    /// <see cref="VideoSurfacePresenter"/> does, and leaves <c>LastPlacement</c> holding the last
    /// outcome after a clip ends. The Lock Card shipped a double that diverged from the product in
    /// precisely the state a defect lived in, and this is that lesson kept.</para>
    /// </summary>
    private sealed class RecordingVideoSurface : IVideoSurface
    {
        private Action? _onEnded;

        public List<string> Begun { get; } = [];

        public int Ends { get; private set; }

        public IVideoFramePainter? LastPainter { get; private set; }

        public CapabilityState BeginResult { get; set; } = new CapabilityState.Available("the double allowed it");

        /// <summary>What the OS says a clip's length is. The painter is told this at Begin, exactly
        /// as the presenter tells it.</summary>
        public TimeSpan ClipDuration { get; set; } = TimeSpan.FromSeconds(30);

        public bool Showing { get; set; }

        public bool Running { get; set; } = true;

        public bool Engaged => Showing;

        public bool CanReachADisplay { get; set; } = true;

        public int FramesDecoded { get; set; }

        public int FramesHeld { get; set; }

        public int FramesAdvanced { get; set; }

        public string? PlayingClip { get; private set; }

        public CapabilityState? LastPlacement { get; set; }

        public VideoSurfaceObservation LastObservation { get; set; } = VideoSurfaceObservation.NotAsked;

        public CapabilityState Begin(
            string clipPath, TimeSpan maxLength, Action onEnded, IVideoFramePainter? painter = null)
        {
            Begun.Add(clipPath);
            _onEnded = onEnded;
            LastPainter = painter;
            LastPlacement = BeginResult;
            if (BeginResult is CapabilityState.Available)
            {
                Showing = true;
                PlayingClip = clipPath;
                painter?.Opening(new VideoClipInfo(
                    true, 320, 240, TimeSpan.FromMilliseconds(100), ClipDuration, false));
            }

            return BeginResult;
        }

        public void End()
        {
            Ends++;
            Showing = false;
            PlayingClip = null;
        }

        /// <summary>Drive the painter the way the presenter's cadence would, one picture per frame
        /// interval, on real <see cref="VideoFrame"/>s.</summary>
        public void PaintFrames(TimeSpan through, TimeSpan interval)
        {
            for (var at = TimeSpan.Zero; at <= through; at += interval)
            {
                LastPainter?.Paint(VideoFrame.Solid(320, 240, 0x10, 0x20, 0x30), at);
            }
        }

        /// <summary>What the presenter does when the clip finishes: end, then tell the caller.</summary>
        public void RaiseEnded()
        {
            var ended = _onEnded;
            End();
            ended?.Invoke();
        }
    }

    /// <summary>
    /// An input presence that records what it was asked and delivers keystrokes the way the OS
    /// would. It mirrors the product's own prompting transition (set from the OS's CONFIRMATION,
    /// which excludes ink, so a Degraded card is still up) for the reason the Lock Card's review found.
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

        /// <summary>Somebody ELSE's card is up on this shared presence — a Lock Card, in the
        /// product. The keystroke callback is not this module's, which is exactly why prompting over
        /// it would strand it.</summary>
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

        public void TypeCharacters(string text)
        {
            foreach (var c in text)
            {
                _onKeystroke?.Invoke(new InputKeystroke(InputKeystrokeKind.Character, c, c));
            }
        }

        /// <summary>Enter, as the capability really delivers it: a <c>Key</c> with a raw virtual-key
        /// code, because the capability names only Backspace, Escape and characters.</summary>
        public void PressEnter() =>
            _onKeystroke?.Invoke(new InputKeystroke(InputKeystrokeKind.Key, '\0', BubbleCountAnswer.SubmitVirtualKey));

        public void PressEscape() =>
            _onKeystroke?.Invoke(new InputKeystroke(InputKeystrokeKind.Cancel, '\0', 0x1B));

        public void Dispose()
        {
        }
    }

    private sealed class StubVideoPool(string folder) : IVideoClipPool
    {
        public List<string> Clips { get; } = [];

        public int ActiveCount => Clips.Count;

        public string Folder => folder;

        public string? Draw() => Clips.Count == 0 ? null : Clips[0];
    }

    /// <summary>A <see cref="Random"/> whose draws are a script, so a fact can pin a placement or an
    /// interval without pinning a seed.</summary>
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

    private sealed class ManualClock : ISessionClock
    {
        private sealed class Entry
        {
            public DateTimeOffset Due;
            public required Action Fire;
            public bool Cancelled;
        }

        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset? NextDue
        {
            get
            {
                lock (_timers)
                {
                    return _timers.Where(t => !t.Cancelled).Select(t => (DateTimeOffset?)t.Due).Min();
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

        /// <summary>Move forward to the soonest live timer and run exactly it — so a fact can fire ONE
        /// game without also running the safety end that game arms.</summary>
        public void AdvanceToNextDue()
        {
            if (NextDue is { } due)
            {
                Advance(due - UtcNow);
            }
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
