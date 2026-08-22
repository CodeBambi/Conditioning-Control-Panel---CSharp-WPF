using System.Reflection;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Pointer;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>Bubble Pop's arithmetic, its dot, and the presenter that turns the two into windows
/// the operating system routes clicks to.</b> No desktop is touched in this file: the pointer
/// surface is a double, the clock is injected and advanced by hand, and every number is upstream's
/// with its own citation.
///
/// <para>The OS-level half of this packet lives in <see cref="PointerCapabilityTests"/> and
/// <see cref="PointerCoexistenceTests"/>, which put real windows on the real screen and synthesise
/// real clicks. This file is the other half: whether the module asks for the right rectangles at the
/// right moments, and whether the row's dot can lie.</para>
/// </summary>
public class BubblePopModuleTests
{
    // ---------------------------------------------------------------------------------
    //  THE RACE BOUND — arithmetic over upstream's own constants
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheSpawnIntervalIsUpstreamsOwnDivision_AndTheDialsClampIsAppliedBeforeIt()
    {
        // WPF: 60000.0 / Math.Max(1, frequency) (Services/BubbleService.cs:188), with the dial itself
        // clamped 1..60 (CCP.Core/Models/AppSettings.cs:2743-2747).
        Assert.Equal(TimeSpan.FromMilliseconds(60000.0 / 5), BubblePopField.SpawnInterval(5));
        Assert.Equal(TimeSpan.FromMilliseconds(60000.0 / 60), BubblePopField.SpawnInterval(60));
        Assert.Equal(TimeSpan.FromMilliseconds(60000.0 / 1), BubblePopField.SpawnInterval(1));

        // Out-of-range values are clamped rather than dividing by zero or producing a millisecond
        // spawn storm.
        Assert.Equal(BubblePopField.SpawnInterval(1), BubblePopField.SpawnInterval(0));
        Assert.Equal(BubblePopField.SpawnInterval(1), BubblePopField.SpawnInterval(-40));
        Assert.Equal(BubblePopField.SpawnInterval(60), BubblePopField.SpawnInterval(600));
    }

    [Theory]
    [InlineData(150, 100, 150)]
    [InlineData(249, 100, 249)]
    [InlineData(150, 50, 75)]
    [InlineData(150, 150, 225)]
    [InlineData(60, 50, 60)]          // the clickable floor bites before the user's own range can
    [InlineData(2000, 150, 500)]      // and the playfield ceiling bites at the other end
    public void TheDrawnSizeIsUpstreamsOwnScaleThenItsTwoAbsoluteRails(int baseDip, int percent, int expected)
    {
        // Services/BubbleSizing.cs: band 150..250 (:41, :48), user 50..150 % (:52, :59), floored at
        // ClickableFloorDip = 60 (:70) and ceilinged at PlayfieldCeilingDip = 500 (:82).
        Assert.Equal(expected, BubblePopField.SizeFor(baseDip, percent));
    }

    [Theory]
    [InlineData(0, 6.0, 25.0)]
    [InlineData(1, 7.5, 30.0)]
    [InlineData(2, 5.4, 25.0)]
    public void TheFourWobblesAreUpstreamsOwn_AndTheSTEEPESTOneIsWhatBoundsTheRace(
        int animType, double rate, double amplitude)
    {
        // Services/BubbleService.cs:3460-3463. Cases 0 and 1 are sines, case 2 a cosine, case 3 a sum.
        var t = 0.37;
        var expected = animType == 2 ? Math.Cos(t * rate) * amplitude : Math.Sin(t * rate) * amplitude;
        Assert.Equal(expected, BubblePopField.Wobble(animType, t), 9);
        Assert.Equal((Math.Sin(t * 3) * 30) + (Math.Cos(t * 6) * 15), BubblePopField.Wobble(3, t), 9);

        // The steepest per-step derivative is case 1's, and it is the horizontal half of the bound.
        Assert.Equal(30 * 7.5 * BubblePopField.TimeAlivePerStep, BubblePopField.MaxWobbleStep, 9);
    }

    [Fact]
    public void ONESTEPCannotCarryABubbleOffItsOwnCentre_AndThatInequalityISTheRaceArgument()
    {
        // The Lock Card predicted this packet's central race: a hit test's answer is a function of a
        // position that changes between asking and clicking. The port removed it from the product —
        // nothing hit-tests and then acts on the answer, because each target is its own window and
        // the arbiter is the window manager at the instant of the click — and what remains is that a
        // caller's belief about routing can be one step old. This is the bound on that residue.
        Assert.True(BubblePopField.MaxStepDisplacement < BubblePopField.MinSize / 2.0);

        // The margin, stated as a number so a later change that halves it is visible rather than
        // merely still-passing.
        Assert.True(BubblePopField.MinSize / 2.0 / BubblePopField.MaxStepDisplacement > 2.0,
            $"the bound holds by a factor of "
            + $"{BubblePopField.MinSize / 2.0 / BubblePopField.MaxStepDisplacement:0.###}, which is thinner than "
            + "the 2.3409 this port measured. A change to the speed band, the boost ceiling or the size floor has "
            + "eaten the margin");

        Assert.Equal(12.0, BubblePopField.MaxSpeed * (1 + (BubblePopField.MaxSpeedBoostPercent / 100.0)));
    }

    // ---------------------------------------------------------------------------------
    //  THE FIELD
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ABubbleSpawnsAtTheBottomOfThePlayArea_AndFloatsUPWARDAtItsOwnSpeed()
    {
        var field = new BubblePopField(0, 0, 1920, 1080, new Random(7));
        var born = field.Spawn(100, 0);

        Assert.NotNull(born);
        Assert.Equal(1080, born!.Value.Y);                                  // FloatUp starts at the bottom (:2876)
        Assert.InRange(born.Value.Speed, BubblePopField.MinSpeed, BubblePopField.MaxSpeed);
        Assert.InRange(born.Value.AnimType, 0, 3);

        field.Step();
        var moved = field.Bubbles.Single();
        Assert.Equal(born.Value.Y - born.Value.Speed, moved.Y, 9);          // _posY -= _speed (:3496)
        Assert.Equal(
            born.Value.StartX + BubblePopField.Wobble(born.Value.AnimType, BubblePopField.TimeAlivePerStep),
            moved.X,
            9);                                                              // _posX = _startX + offset (:3497)
    }

    [Fact]
    public void BUBBLESSpawnAcrossTheWidthOfThePlayArea_AtUpstreamsOwnBand()
    {
        // _startX = area.X + random.Next(50, max(100, area.Width - _size - 50)) (:2852). A field
        // whose bubbles all appeared at the same x would be a column, not a game.
        var field = new BubblePopField(0, 0, 1920, 1080, new Random(29));
        var xs = new List<double>();
        for (var i = 0; i < BubblePopField.MaxConcurrent; i++)
        {
            xs.Add(field.Spawn(100, 0)!.Value.StartX);
        }

        Assert.All(xs, x => Assert.True(x >= 50, $"a bubble spawned at x={x}, inside the 50 px inset"));
        Assert.True(xs.Distinct().Count() > 1,
            "every bubble spawned at the same x; the horizontal band is not being drawn from");
    }

    [Fact]
    public void ABubbleThatFloatsOffTheTopIsMISSED_AtUpstreamsOwnMargin()
    {
        // _screenTop = area.Y - _size - 50 (:2847); exit at _posY < _screenTop (:3497-3499); OnMiss
        // removes it immediately with no animation (:1194-1200).
        var field = new BubblePopField(0, 0, 1920, 400, new Random(3));
        var born = field.Spawn(100, 0)!.Value;

        var exit = 0 - born.Size - BubblePopField.ExitMargin;
        var steps = 0;
        IReadOnlyList<(BubblePopBubble Bubble, BubblePopExit Exit)> gone = [];
        while (field.Bubbles.Count > 0 && steps < 100_000)
        {
            gone = field.Step();
            steps++;
        }

        Assert.Equal(BubblePopExit.Missed, gone.Single().Exit);
        Assert.Equal(1, field.Missed);
        Assert.Equal(0, field.Popped);
        Assert.True(gone.Single().Bubble.Y - born.Speed < exit + born.Speed);
    }

    [Fact]
    public void AHITStartsThePopAnimation_AndTheBubbleIsNotCountedUntilTheAnimationFINISHES()
    {
        // Upstream defers destruction to the pop animation completing rather than doing it inside
        // the click (BUBBLE_POP_PRIMER §9.6, Services/BubbleService.cs:3225-3231).
        var field = new BubblePopField(0, 0, 1920, 1080, new Random(11));
        var born = field.Spawn(100, 0)!.Value;

        Assert.True(field.Hit(born.Id));
        Assert.Equal(0, field.Popped);
        Assert.Single(field.Bubbles);
        Assert.True(field.Bubbles[0].Popping);

        var steps = 0;
        while (field.Bubbles.Count > 0 && steps < 1000)
        {
            field.Step();
            steps++;
        }

        Assert.Equal(1, field.Popped);
        Assert.Equal(0, field.Missed);

        // fadeAlpha 1.0 falling by 0.066 a step reaches zero on the sixteenth (:3228).
        Assert.Equal((int)Math.Ceiling(1.0 / BubblePopField.PopFadePerStep), steps);
    }

    [Fact]
    public void ASECONDHitOnABubbleAlreadyPoppingDoesNothing_WhichIsWhyADoubleClickCannotScoreTwice()
    {
        // Upstream's own first line: if (!_isAlive || _isPopping) return; (:3990).
        var field = new BubblePopField(0, 0, 1920, 1080, new Random(13));
        var born = field.Spawn(100, 0)!.Value;

        Assert.True(field.Hit(born.Id));
        Assert.False(field.Hit(born.Id));
        Assert.False(field.Hit(born.Id + 999));
    }

    [Fact]
    public void TheFieldRefusesAFourthBubble_WhichIsUpstreamsOwnPerWindowCapAndItsOwnReason()
    {
        var field = new BubblePopField(0, 0, 1920, 1080, new Random(17));

        for (var i = 0; i < BubblePopField.MaxConcurrent; i++)
        {
            Assert.NotNull(field.Spawn(100, 0));
        }

        Assert.Null(field.Spawn(100, 0));
        Assert.Equal(BubblePopField.MaxConcurrent, field.Bubbles.Count);
        Assert.Equal(3, BubblePopField.MaxConcurrent);
    }

    [Fact]
    public void CLEARINGTheFieldCountsNeitherAPopNorAMiss_BecauseNeitherHappened()
    {
        // Upstream's Stop() calls PopAllBubbles() (:725-739), which ENDS a run rather than resolving
        // one. Counting those as pops would inflate the number the panel shows the user.
        var field = new BubblePopField(0, 0, 1920, 1080, new Random(19));
        field.Spawn(100, 0);
        field.Spawn(100, 0);

        var dropped = field.Clear();

        Assert.Equal(2, dropped.Count);
        Assert.Empty(field.Bubbles);
        Assert.Equal(0, field.Popped);
        Assert.Equal(0, field.Missed);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(100, 2.0)]
    [InlineData(500, 6.0)]
    [InlineData(900, 6.0)]      // clamped at upstream's own ceiling
    [InlineData(-40, 1.0)]      // and at its floor
    public void TheSpeedBoostIsUpstreamsOwnMultiplierAndItsOwnClamp(int boost, double multiplier)
    {
        // speed *= 1.0 + Math.Clamp(speedBoost, 0, 500) / 100.0 (:2831-2834).
        var plain = new BubblePopField(0, 0, 1920, 1080, new Random(23)).Spawn(100, 0)!.Value;
        var boosted = new BubblePopField(0, 0, 1920, 1080, new Random(23)).Spawn(100, boost)!.Value;

        Assert.Equal(plain.Speed * multiplier, boosted.Speed, 9);
    }

    // ---------------------------------------------------------------------------------
    //  THE PRESENTER — where the field becomes windows
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ENGAGINGPlacesTheFirstTargetIMMEDIATELY_BecauseUpstreamSpawnsOneBeforeTheTimerRuns()
    {
        // "Spawn first bubble immediately" (Services/BubbleService.cs:200).
        using var lab = new Lab();

        var state = lab.Presenter.Engage(new BubblePopSettings(5, 100, 0));

        Assert.IsType<CapabilityState.Available>(state);
        Assert.Single(lab.Surface.Opened);
        Assert.Equal(1, lab.Presenter.TargetsUp);
        Assert.True(lab.Presenter.Showing);
        Assert.True(lab.Presenter.Running);
    }

    [Fact]
    public void EVERYSTEPMOVESEVERYLiveTarget_WhichIsTheSeamTheInputCapabilityDoesNotHave()
    {
        using var lab = new Lab();
        lab.Presenter.Engage(new BubblePopSettings(5, 100, 0));

        lab.Clock.Advance(BubblePopField.StepInterval);
        lab.Clock.Advance(BubblePopField.StepInterval);
        lab.Clock.Advance(BubblePopField.StepInterval);

        Assert.Equal(3, lab.Surface.Moves.Count);
        Assert.All(lab.Surface.Moves, m => Assert.Equal(lab.Surface.Opened[0].Handle, m.Handle));

        // And the target really travelled UPWARD, which is the direction upstream's ambient motion
        // goes (FloatUp, :3496).
        Assert.True(lab.Surface.Moves[^1].Bounds.Y < lab.Surface.Opened[0].Bounds.Y);
    }

    [Fact]
    public void ASPAWNTIMERTHATCOMESDUEPlacesAnotherTarget_AtUpstreamsOwnInterval()
    {
        using var lab = new Lab();
        lab.Presenter.Engage(new BubblePopSettings(60, 100, 0));

        Assert.Single(lab.Surface.Opened);
        lab.Clock.Advance(BubblePopField.SpawnInterval(60));

        Assert.Equal(2, lab.Surface.Opened.Count);
        Assert.Equal(2, lab.Presenter.TargetsUp);
    }

    [Fact]
    public void APRESSTHEOPERATINGSYSTEMROUTEDPopsTHATBubbleAndNoOther()
    {
        // The module does not decide which bubble was clicked: the window manager did, and the
        // capability hands back the handle it chose. This fact pins the mapping from that handle to
        // the field's own bubble.
        using var lab = new Lab();
        lab.Presenter.Engage(new BubblePopSettings(60, 100, 0));
        lab.Clock.Advance(BubblePopField.SpawnInterval(60));

        var second = lab.Surface.Opened[1].Handle;
        lab.Surface.DeliverPress(second, PointerPressKind.Down);
        lab.Clock.Advance(BubblePopField.StepInterval);

        // The second target's bubble is popping; the first is not.
        for (var i = 0; i < 20; i++)
        {
            lab.Clock.Advance(BubblePopField.StepInterval);
        }

        Assert.Equal(1, lab.Presenter.Popped);
        Assert.Equal(0, lab.Presenter.Missed);
        Assert.Contains(second, lab.Surface.Closed);
        Assert.DoesNotContain(lab.Surface.Opened[0].Handle, lab.Surface.Closed);
    }

    [Fact]
    public void APRESSREACHESTHEFIELDOnlyBecauseTheStepPUMPSItFirst()
    {
        // The OS delivers a press into a message queue; nothing in the module runs until something
        // drains it. StepOnce pumps BEFORE it moves anything, so a click that arrived since the last
        // step belongs to the position the bubble was at when the OS routed it — not to the one it
        // is about to move to.
        using var lab = new Lab();
        lab.Presenter.Engage(new BubblePopSettings(1, 100, 0));
        lab.Surface.DeliverPress(lab.Surface.Opened[0].Handle, PointerPressKind.Down);

        // Exactly the pop animation's own length: 1.0 falling by 0.066 a step reaches zero on the
        // sixteenth. One step later than that and the count would move for the wrong reason.
        var steps = (int)Math.Ceiling(1.0 / BubblePopField.PopFadePerStep);
        for (var i = 0; i < steps; i++)
        {
            lab.Clock.Advance(BubblePopField.StepInterval);
        }

        Assert.Equal(1, lab.Presenter.Popped);
    }

    [Fact]
    public void THEUPHALFOfAClickPopsNothing_BecauseUpstreamPopsOnTheDOWN()
    {
        // Upstream's handler is MouseLeftButtonDown (Services/BubbleService.cs:3113), so a press that
        // is never released still pops — and an UP with no DOWN is not a click at all.
        using var lab = new Lab();
        lab.Presenter.Engage(new BubblePopSettings(5, 100, 0));

        lab.Surface.DeliverPress(lab.Surface.Opened[0].Handle, PointerPressKind.Up);
        for (var i = 0; i < 20; i++)
        {
            lab.Clock.Advance(BubblePopField.StepInterval);
        }

        Assert.Equal(0, lab.Presenter.Popped);
    }

    [Fact]
    public void WITHDRAWCLOSESEVERYTARGETAndStopsBothCadences()
    {
        using var lab = new Lab();
        lab.Presenter.Engage(new BubblePopSettings(60, 100, 0));
        lab.Clock.Advance(BubblePopField.SpawnInterval(60));
        var openedBefore = lab.Surface.Opened.Count;

        lab.Presenter.Withdraw();
        var movesAfterWithdraw = lab.Surface.Moves.Count;

        // Asserted HERE, before anything is advanced. A cadence that was CANCELLED leaves no timer;
        // one that was merely dropped on the floor leaves its handle alive for ever, and advancing
        // first would hide that by letting the orphan fire and retire itself.
        Assert.Equal(0, lab.Clock.PendingCount);

        lab.Clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(openedBefore, lab.Surface.Closed.Count);
        Assert.Equal(openedBefore, lab.Surface.Opened.Count);
        Assert.Equal(movesAfterWithdraw, lab.Surface.Moves.Count);
        Assert.False(lab.Presenter.Showing);
        Assert.False(lab.Presenter.Running);
        Assert.Equal(0, lab.Presenter.TargetsUp);
    }

    [Fact]
    public void ASURFACEWITHNOPOINTERCHANNELIsRefusedInType_AndNothingIsEverPlaced()
    {
        using var lab = new Lab(reachable: false);

        var state = lab.Presenter.Engage(new BubblePopSettings(5, 100, 0));

        var reason = Assert.IsType<CapabilityState.Unavailable>(state).Reason;
        Assert.Equal(EffectReasonCodes.PointerSurfaceUnavailable, reason.Code);
        Assert.Empty(lab.Surface.Opened);
        Assert.False(lab.Presenter.Showing);
        Assert.False(lab.Presenter.Running);
    }

    [Fact]
    public void NODISPLAYATALLIsRefusedInType_RatherThanPlacingATargetNobodyCanSee()
    {
        using var lab = new Lab(hasDisplay: false);

        var state = lab.Presenter.Engage(new BubblePopSettings(5, 100, 0));

        Assert.Equal(EffectReasonCodes.PointerSurfaceUnavailable,
            Assert.IsType<CapabilityState.Unavailable>(state).Reason.Code);
        Assert.Empty(lab.Surface.Opened);
    }

    [Fact]
    public void AFIELDNOTHINGROUTESTOIsNOTRunning_EvenThoughItsTargetsAreOnTheDesktop()
    {
        // THE DOT'S THIRD CLAUSE, and the state this row invented: bubbles are up, drawn and
        // visible, and the window manager routes a click at none of them here.
        using var lab = new Lab();
        lab.Surface.Refusal = PointerReasonCodes.PointerTargetNotRoutable;
        lab.Presenter.Engage(new BubblePopSettings(5, 100, 0));

        Assert.True(lab.Presenter.Showing);
        Assert.Equal(1, lab.Presenter.TargetsUp);
        Assert.Equal(0, lab.Presenter.RoutableTargets);
        Assert.False(lab.Presenter.Running);

        // And it stays false ACROSS A STEP: the routable count is re-derived from every Move's own
        // typed answer, not carried forward from the placement.
        lab.Clock.Advance(BubblePopField.StepInterval);
        Assert.Equal(0, lab.Presenter.RoutableTargets);
        Assert.False(lab.Presenter.Running);
    }

    [Fact]
    public void ANEMPTYFieldBetweenSpawnsIsSTILLRunning_BecauseTheGapIsMostOfEverySession()
    {
        // At the bottom of the dial the gap is fifty-nine seconds in every sixty
        // (60000/1 ms, Services/BubbleService.cs:188). A dot that went dark there would report the
        // module broken for almost the whole session, which is the opposite lie.
        using var lab = new Lab();
        lab.Presenter.Engage(new BubblePopSettings(1, 100, 0));

        lab.Surface.DeliverPress(lab.Surface.Opened[0].Handle, PointerPressKind.Down);
        for (var i = 0; i < 20; i++)
        {
            lab.Clock.Advance(BubblePopField.StepInterval);
        }

        Assert.Equal(0, lab.Presenter.TargetsUp);
        Assert.True(lab.Presenter.Showing);
        Assert.True(lab.Presenter.Running);
    }

    [Fact]
    public void ONELIVEROUTABLETARGETIsEnoughToBeRunning_BecauseUpstreamsBubblesOverlapFreely()
    {
        // Upstream's spawn band is a random x per bubble with no separation rule (:2852), so one
        // bubble covering another's centre is an ordinary state of the game rather than a failure of
        // the channel. A field with one hittable bubble in it is a game.
        using var lab = new Lab();
        lab.Presenter.Engage(new BubblePopSettings(60, 100, 0));

        // The SECOND target only — the state where one bubble sits over another's centre.
        lab.Surface.Refusal = PointerReasonCodes.PointerTargetNotRoutable;
        lab.Surface.RefuseTargets.Add(2);
        lab.Clock.Advance(BubblePopField.SpawnInterval(60));

        Assert.Equal(2, lab.Presenter.TargetsUp);
        Assert.Equal(1, lab.Presenter.RoutableTargets);
        Assert.True(lab.Presenter.Running);
    }

    // ---------------------------------------------------------------------------------
    //  THE MODULE
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ADialThatIsOffMeansTheModuleIsOFF_SessionOrNoSession()
    {
        using var lab = new Lab();

        Assert.False(lab.Effect.Enabled);
        Assert.Equal(EffectDotState.Off, lab.Effect.Dot);

        var refusal = lab.Effect.Arm();

        Assert.Equal(EffectReasonCodes.EffectDialOff,
            Assert.IsType<CapabilityState.Unavailable>(refusal).Reason.Code);
        Assert.Equal(EffectDotState.Off, lab.Effect.Dot);
        Assert.Empty(lab.Surface.Opened);
    }

    [Fact]
    public void ANARMEDMODULEWithARoutableFieldIsLIVE_AndItsTargetsAreReallyOnTheSurface()
    {
        using var lab = new Lab();
        lab.Effect.SetEnabled(true);

        var armed = lab.Effect.Arm();

        Assert.IsType<CapabilityState.Available>(armed);
        Assert.Equal(EffectDotState.Live, lab.Effect.Dot);
        Assert.Equal(1, lab.Effect.Targets.Up);
        Assert.Equal(1, lab.Effect.Targets.Routable);
    }

    [Fact]
    public void AFIELDNOTHINGROUTESTOArmsDEGRADEDAndItsDotGoesARMED_WhichIsTheThirdKindOfDegradation()
    {
        using var lab = new Lab();
        lab.Surface.Refusal = PointerReasonCodes.PointerTargetNotRoutable;
        lab.Effect.SetEnabled(true);

        var armed = lab.Effect.Arm();

        var degraded = Assert.IsType<CapabilityState.Degraded>(armed);
        Assert.Equal(EffectReasonCodes.PointerFieldNotRoutable, degraded.Reason.Code);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);

        // NOT the Pink Filter answer (the channel is gone) and NOT the Subliminals answer (the pool
        // is empty). The channel is intact, the content is there, and the user cannot reach it.
        Assert.NotEqual(EffectReasonCodes.PointerSurfaceUnavailable, degraded.Reason.Code);
    }

    [Fact]
    public void NOPOINTERCHANNELATALLTakesThePinkFilterAnswer_UnavailableArmedAndNoLitDot()
    {
        using var lab = new Lab(reachable: false);
        lab.Effect.SetEnabled(true);

        var armed = lab.Effect.Arm();

        Assert.Equal(EffectReasonCodes.PointerSurfaceUnavailable,
            Assert.IsType<CapabilityState.Unavailable>(armed).Reason.Code);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
    }

    [Fact]
    public void WITHNOUITHREADBOUNDTheModuleRefusesRatherThanTouchingANativeWindowOffThread()
    {
        using var lab = new Lab(bindUi: false);
        lab.Effect.SetEnabled(true);

        var armed = lab.Effect.Arm();

        Assert.Equal(EffectReasonCodes.EffectNoUiThread,
            Assert.IsType<CapabilityState.Unavailable>(armed).Reason.Code);
        Assert.Empty(lab.Surface.Opened);
    }

    [Fact]
    public void WITHNOSURFACEINTHECOMPOSITIONTheModuleRefusesInType()
    {
        using var lab = new Lab(composeSurface: false);
        lab.Effect.SetEnabled(true);

        var armed = lab.Effect.Arm();

        Assert.Equal(EffectReasonCodes.EffectNoSurface,
            Assert.IsType<CapabilityState.Unavailable>(armed).Reason.Code);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
    }

    [Fact]
    public void RELEASINGAModuleWithNoSurfaceDoesNothing_RatherThanDereferencingOne()
    {
        // THE GUARD IN ReleaseWork IS ALSO THE NULL GUARD, and this is the fact that says so.
        // OwnedSessionEffect reaches ReleaseWork WITHOUT screening: unconditionally from Disarm
        // before its own wasArmed return, and from the eligibility gate on the dial-off and
        // dead-generation paths. A composition with no pointer surface is a real construction — the
        // spine's own "compose the module with nowhere to place anything" case — so a module that
        // dereferenced there would throw on a stop it never even started.
        //
        // An earlier draft of the record discharged the mutation that deletes that guard as an
        // EQUIVALENT MUTANT on the ground that Withdraw is idempotent. That was true and beside the
        // point: idempotence says nothing about a null receiver. The claim is withdrawn and this
        // fact replaces it.
        using var lab = new Lab(composeSurface: false);
        lab.Effect.SetEnabled(true);

        var armed = lab.Effect.Arm();

        // Every path that reaches ReleaseWork, on a module that has no surface at all.
        lab.Effect.Disarm();
        lab.Effect.Refresh();
        lab.Effect.SetEnabled(false);
        lab.Effect.Refresh();
        lab.Effect.Disarm();

        Assert.Equal(EffectReasonCodes.EffectNoSurface,
            Assert.IsType<CapabilityState.Unavailable>(armed).Reason.Code);
        Assert.Equal(EffectDotState.Off, lab.Effect.Dot);
        Assert.Equal(0, lab.Effect.Targets.Up);
        Assert.Equal(0, lab.Effect.Popped);
        Assert.Null(lab.Effect.LastPlacement);
    }

    [Fact]
    public void DISARMTakesEveryBubbleOffTheDesktop_BecauseForAContinuousModuleThoseAreTheSameAct()
    {
        using var lab = new Lab();
        lab.Effect.SetEnabled(true);
        lab.Effect.Arm();
        var opened = lab.Surface.Opened.Count;

        lab.Effect.Disarm();

        Assert.Equal(opened, lab.Surface.Closed.Count);
        Assert.False(lab.Surface.Engaged);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
    }

    [Fact]
    public void TURNINGTHEDIALOFFMIDSESSIONClearsTheDesktopToo()
    {
        using var lab = new Lab();
        lab.Effect.SetEnabled(true);
        lab.Effect.Arm();

        lab.Effect.SetEnabled(false);
        lab.Effect.Refresh();

        Assert.False(lab.Surface.Engaged);
        Assert.Equal(EffectDotState.Off, lab.Effect.Dot);
    }

    [Fact]
    public void MOVINGTHEFREQUENCYDIALRetimesTheLiveField_RatherThanWaitingOutTheOldInterval()
    {
        // Upstream's frequency slider calls RefreshFrequency() for exactly this reason
        // (Features/BubblePopFeatureControl.xaml.cs:116).
        using var lab = new Lab();
        lab.Effect.SetEnabled(true);
        lab.Effect.SetPerMinute(1);
        lab.Effect.Arm();

        // At one a minute nothing more is due for another 59 seconds.
        var afterArm = lab.Surface.Opened.Count;
        lab.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(afterArm, lab.Surface.Opened.Count);

        // Moving the dial RE-TIMES the live field rather than letting the old interval run out.
        lab.Effect.SetPerMinute(60);
        lab.Clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(60, lab.Effect.Settings.PerMinute);
        Assert.True(lab.Surface.Opened.Count > afterArm,
            "the frequency dial did not re-time the live spawn timer, so the change lands only after the OLD "
            + "interval expires — which at one a minute is most of a minute the user spends wondering");
    }

    [Theory]
    [InlineData(0, BubblePopField.MinPerMinute)]
    [InlineData(600, BubblePopField.MaxPerMinute)]
    public void THEFREQUENCYDIALISCLAMPEDToUpstreamsOwnRange(int asked, int expected)
    {
        using var lab = new Lab();
        lab.Effect.SetPerMinute(asked);
        Assert.Equal(expected, lab.Effect.Settings.PerMinute);
    }

    [Fact]
    public void DRAGGINGTHEDIALPASTITSOWNCEILINGDoesNotRetimeTheLiveField()
    {
        // The setter clamps BEFORE comparing, so a drag past the end is a no-op rather than a
        // re-engage. Without that the spawn timer would restart on every pixel of a drag the dial
        // cannot honour, and the next bubble would keep receding.
        using var lab = new Lab();
        lab.Effect.SetEnabled(true);
        lab.Effect.SetPerMinute(BubblePopField.MaxPerMinute);
        lab.Effect.Arm();
        var afterArm = lab.Surface.Opened.Count;

        lab.Clock.Advance(TimeSpan.FromMilliseconds(600));
        lab.Effect.SetPerMinute(600);
        lab.Clock.Advance(TimeSpan.FromMilliseconds(400));

        Assert.Equal(BubblePopField.MaxPerMinute, lab.Effect.Settings.PerMinute);
        Assert.True(lab.Surface.Opened.Count > afterArm,
            "the spawn due at one second never came: dragging the dial past its own ceiling re-timed the live "
            + "field, so the next bubble recedes for as long as the user keeps dragging");
    }

    [Theory]
    [InlineData(0, BubblePopField.MinSizePercent)]
    [InlineData(400, BubblePopField.MaxSizePercent)]
    public void THESIZEDIALISCLAMPEDToUpstreamsOwnRange(int asked, int expected)
    {
        using var lab = new Lab();
        lab.Effect.SetSizePercent(asked);
        Assert.Equal(expected, lab.Effect.Settings.SizePercent);
    }

    [Theory]
    [InlineData(-40, BubblePopField.MinSpeedBoostPercent)]
    [InlineData(9000, BubblePopField.MaxSpeedBoostPercent)]
    public void THESPEEDDIALISCLAMPEDToUpstreamsOwnRange(int asked, int expected)
    {
        using var lab = new Lab();
        lab.Effect.SetSpeedBoostPercent(asked);
        Assert.Equal(expected, lab.Effect.Settings.SpeedBoostPercent);
    }

    [Fact]
    public void THEMODULETAKESNOCLOCKATALL_BecauseBothCadencesKeepASURFACECorrect()
    {
        // The interval-ownership rule, applied a fourth time: an interval that decides when a MODULE is due
        // belongs to PacedSessionEffect; a cadence that keeps a SURFACE correct is the surface's.
        var clockParameters = typeof(BubblePopEffect)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters())
            .Where(p => typeof(ISessionClock).IsAssignableFrom(p.ParameterType))
            .ToList();

        Assert.Empty(clockParameters);
        Assert.Equal(typeof(OwnedSessionEffect), typeof(BubblePopEffect).BaseType);
    }

    [Fact]
    public void THEEFFECTIDISUPSTREAMSOWNRACKKEY_AndItIsNotTheOneAReaderWouldGuess()
    {
        // Add("bubbles", "🫧", "Bubble_pop.png", "Bubble Pop", ...) at StudioTabView.xaml.cs:499,
        // and case "bubbles" at MainWindow/MainWindow.Presets.cs:1256. A-004: the dispatch identity
        // is upstream's key and never a display string.
        Assert.Equal("bubbles", BubblePopEffect.EffectId);
        Assert.Equal("Bubble Pop", BubblePopEffect.DisplayTitle);
        Assert.NotEqual(BubblePopEffect.EffectId, BubbleCountEffect.EffectId);
    }

    // ---------------------------------------------------------------------------------
    //  THE PANEL'S SENTENCES
    // ---------------------------------------------------------------------------------

    [Fact]
    public void THELIVELINEHasAClauseNoOtherRowsHas_AndItIsTheOneAboutAFieldNobodyCanClick()
    {
        var unhittable = PointerPanelNotices.DescribeFieldState(
            EffectDotState.Armed, sessionRunning: true, canReachAPointer: true,
            targetsUp: 3, routable: 0, popped: 4, missed: 1);

        Assert.Contains("CANNOT hit", unhittable, StringComparison.Ordinal);
        Assert.Contains("3 bubble(s) are on screen", unhittable, StringComparison.Ordinal);
        Assert.Contains("Popped 4, missed 1", unhittable, StringComparison.Ordinal);

        var playable = PointerPanelNotices.DescribeFieldState(
            EffectDotState.Live, sessionRunning: true, canReachAPointer: true,
            targetsUp: 3, routable: 2, popped: 4, missed: 1);

        Assert.DoesNotContain("CANNOT hit", playable, StringComparison.Ordinal);
        Assert.Contains("2 of 3", playable, StringComparison.Ordinal);
    }

    [Fact]
    public void THELIVELINEDistinguishesTheFOURStatesAModuleCanReallyBeIn()
    {
        Assert.Contains("is off", PointerPanelNotices.DescribeFieldState(
            EffectDotState.Off, true, true, 0, 0, 0, 0), StringComparison.Ordinal);
        Assert.Contains("cannot put a clickable window in front of you", PointerPanelNotices.DescribeFieldState(
            EffectDotState.Armed, true, false, 0, 0, 0, 0), StringComparison.Ordinal);
        Assert.Contains("waiting for a session", PointerPanelNotices.DescribeFieldState(
            EffectDotState.Armed, false, true, 0, 0, 0, 0), StringComparison.Ordinal);
        Assert.Contains("on the spawn timer", PointerPanelNotices.DescribeFieldState(
            EffectDotState.Live, true, true, 0, 0, 0, 0), StringComparison.Ordinal);
    }

    [Fact]
    public void THEDELIVERYLINEIsWordedAsEvidenceAndNeverAsAClaim()
    {
        // A field nobody has clicked has zero of both numbers, and that is the ordinary state of a
        // healthy row — so the sentence must not read as a fault.
        var none = PointerPanelNotices.DescribeDelivery(0, 0);
        Assert.Contains("not a fault", none, StringComparison.Ordinal);

        var some = PointerPanelNotices.DescribeDelivery(6, 3);
        Assert.Contains("6 mouse message(s)", some, StringComparison.Ordinal);
        Assert.Contains("refused activation 3 time(s)", some, StringComparison.Ordinal);
    }

    [Fact]
    public void THEPANELQUOTESTHECAPABILITYVERBATIM_BecauseAParaphraseIsThePortsOpinionOfWhatTheOSSaid()
    {
        Assert.Contains("has not been asked for anything yet", PointerPanelNotices.DescribeCapability(null),
            StringComparison.Ordinal);

        var refusal = new CapabilityState.Unavailable(
            new CapabilityReason(PointerReasonCodes.PointerTargetNotRoutable, "something is above them"));
        var text = PointerPanelNotices.DescribeCapability(refusal);
        Assert.Contains(PointerReasonCodes.PointerTargetNotRoutable, text, StringComparison.Ordinal);
        Assert.Contains("something is above them", text, StringComparison.Ordinal);
    }

    [Fact]
    public void THESCOPENOTICENamesEveryUnportedHalfInTheUsersOwnWords()
    {
        // Spelled out one per line rather than looped: a loop's assertions are inside a body the
        // vacuous-shape detector cannot see past, and a scope notice that quietly stopped naming one
        // of these is exactly the silently-missing half this port refuses.
        var notice = PointerPanelNotices.ScopeNotice;
        Assert.Contains("sound", notice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("XP", notice, StringComparison.Ordinal);
        Assert.Contains("achievement", notice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("haptic", notice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trigger Bubbles", notice, StringComparison.Ordinal);
        Assert.Contains("Chaos Mode", notice, StringComparison.Ordinal);
        Assert.Contains("stare-to-pop", notice, StringComparison.Ordinal);
        Assert.Contains("primary display only", notice, StringComparison.Ordinal);
    }

    [Fact]
    public void THEEVIDENCENOTICESaysWhatWasAskedANDWhatNoCheckCanEverShow()
    {
        Assert.Contains("routed to it", PointerPanelNotices.EvidenceNotice, StringComparison.Ordinal);
        Assert.Contains("foreground window is still exactly what it was", PointerPanelNotices.EvidenceNotice,
            StringComparison.Ordinal);
        Assert.Contains("manual step", PointerPanelNotices.EvidenceNotice, StringComparison.Ordinal);
    }

    [Fact]
    public void THEINTERRUPTIONNOTICEPromisesTheOneThingThisRowCanPromise()
    {
        Assert.Contains("never take the keyboard or the foreground", PointerPanelNotices.InterruptionNotice,
            StringComparison.Ordinal);
        Assert.Contains("goes to the bubble and not to what is underneath", PointerPanelNotices.InterruptionNotice,
            StringComparison.Ordinal);
    }

    [Fact]
    public void THESPEEDLINEStatesTheRaceBoundInTheSameBreathAsTheDial()
    {
        var text = PointerPanelNotices.DescribeSpeed(500);
        Assert.Contains("12 px", text, StringComparison.Ordinal);
        Assert.Contains("30 ms", text, StringComparison.Ordinal);
        Assert.Contains("30 px", text, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------
    //  THE LINUX REFUSAL
    // ---------------------------------------------------------------------------------

    [Fact]
    public void LINUXREFUSESINTYPE_AndTheGateNamesTheFiveStepsAndWhyWaylandIsDifferent()
    {
        var linux = PointerSurfaceFactory.CreateFor(PointerHostPlatform.Linux);
        var refusal = Assert.IsType<UnsupportedPointerSurface>(linux);

        Assert.Equal(PointerReasonCodes.PointerMechanismAbsent, refusal.Reason.Code);
        Assert.False(refusal.CanReachAPointer);
        Assert.Equal(0, refusal.OpenTargets);
        Assert.Equal(PointerStationObservation.NotAsked, refusal.ObserveStation());
        Assert.Equal(PointerTargetObservation.NotAsked, refusal.Observe(1));
        Assert.Equal(0, refusal.Pump(64));

        // Every operation refuses, including the take-down: an Available from a close that closed
        // nothing would let a caller's teardown pin read green on a build that never placed anything.
        Assert.IsType<CapabilityState.Unavailable>(
            refusal.Open(new PointerTargetRequest(new PointerBounds(0, 0, 120, 120), 1, 2), out var target));
        Assert.Equal(0, target);
        Assert.IsType<CapabilityState.Unavailable>(refusal.Move(1, new PointerBounds(0, 0, 120, 120)));
        Assert.IsType<CapabilityState.Unavailable>(refusal.Close(1));

        // The gate travels WITH the refusal, and it names the step most likely to fail.
        Assert.Contains(PointerSurfaceFactory.LinuxManualGate, refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("_NET_ACTIVE_WINDOW", PointerSurfaceFactory.LinuxManualGate, StringComparison.Ordinal);
        Assert.Contains("WM_HINTS", PointerSurfaceFactory.LinuxManualGate, StringComparison.Ordinal);
        Assert.Contains("click-to-focus is the window manager's policy", PointerSurfaceFactory.LinuxManualGate,
            StringComparison.Ordinal);
        Assert.Contains("HUMAN clicks", PointerSurfaceFactory.LinuxManualGate, StringComparison.Ordinal);
        Assert.Contains("WSLg", PointerSurfaceFactory.LinuxManualGate, StringComparison.Ordinal);
        Assert.Contains("xdg-activation-v1", PointerSurfaceFactory.WaylandNote, StringComparison.Ordinal);
    }

    [Fact]
    public void MACOSANDUNKNOWNAlsoRefuseInType_AndNoBranchOfTheFactoryCanProduceAvailable()
    {
        Assert.IsType<UnsupportedPointerSurface>(PointerSurfaceFactory.CreateFor(PointerHostPlatform.MacOs));
        Assert.IsType<UnsupportedPointerSurface>(PointerSurfaceFactory.CreateFor(PointerHostPlatform.Unknown));

        // The WINDOWS branch is asserted in PointerCapabilityTests instead: naming the Win32 backend
        // here would make this pure-logic file a real-desktop class in the census's eyes, and the
        // census is right to be lexical about that.
        //
        // Selection by platform is allowed; AVAILABILITY by platform is not
        // (runtime-capability-contract §2 rule 2). Nothing in the factory produces Available — only
        // Open and Move do, and only after the OS answered.
        var source = File.ReadAllText(FactorySourcePath());
        Assert.DoesNotContain("CapabilityState.Available", source, StringComparison.Ordinal);
    }

    private static string FactorySourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "client", "CcpClient.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(
            directory!.FullName, "client", "src", "CcpClient.Desktop", "Pointer", "PointerSurfaceFactory.cs");
    }

    // =====================================================================================

    /// <summary>The Bubble Pop module and its presenter over a recording pointer surface, an
    /// injected clock and a real persisted store. Nothing here touches a desktop.</summary>
    private sealed class Lab : IDisposable
    {
        private readonly string _path;

        public Lab(
            bool bindUi = true,
            bool composeSurface = true,
            bool reachable = true,
            bool hasDisplay = true)
        {
            _path = Path.Combine(Path.GetTempPath(), "ccp-sp113-lab-" + Guid.NewGuid().ToString("N") + ".json");
            var registry = new OperationRegistry();
            var boundary = new UiDispatchBoundary();
            if (bindUi)
            {
                boundary.Bind(new InlineDispatch());
            }

            Surface = new RecordingPointerSurface { Reachable = reachable };
            Clock = new ManualClock();
            var area = new PointerBounds(0, 0, 1920, 1080);
            Presenter = new BubblePopSurfacePresenter(
                Clock,
                static action => action(),
                () => Surface,
                () => hasDisplay ? area : null,
                () => new Random(101));

            Preset = new PersistenceStore<BubblePopPresetDocument>(
                registry.OwnerFor("LabBubblePopPreset"), new NullSink(), _path,
                BubblePopPresetDocument.CurrentSchemaVersion);
            Effect = new BubblePopEffect(
                registry.OwnerFor("LabBubblePop"),
                new EffectSignal(boundary, static () => true),
                Preset,
                composeSurface ? Presenter : null);
        }

        public RecordingPointerSurface Surface { get; }

        public ManualClock Clock { get; }

        public BubblePopSurfacePresenter Presenter { get; }

        public PersistenceStore<BubblePopPresetDocument> Preset { get; }

        public BubblePopEffect Effect { get; }

        public void Dispose()
        {
            Presenter.Dispose();
            try
            {
                File.Delete(_path);
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
    /// A pointer surface that records what it was asked for and can refuse the way a real backend
    /// does — with a typed state carrying the backend's own reason code.
    ///
    /// <para><b>It mirrors the product's refusal shape rather than a convenient one</b>, which is
    /// the Lock Card's §8b lesson: a double that reported success where the product reports a refusal makes
    /// every fact built on it blind in exactly the state that traps the user. So a refused OPEN still
    /// creates a handle and still counts as a target that is UP — because that is what the real
    /// surface does when the window exists and the routing answer is somebody else's.</para>
    /// </summary>
    private sealed class RecordingPointerSurface : IPointerSurface
    {
        private readonly Queue<PointerPress> _queued = new();

        private int _next = 1;

        public List<(int Handle, PointerBounds Bounds)> Opened { get; } = [];

        public List<(int Handle, PointerBounds Bounds)> Moves { get; } = [];

        public List<int> Closed { get; } = [];

        public int Engagements { get; private set; }

        public bool Engaged => Opened.Count > Closed.Count;

        /// <summary>When set, placements refuse with this code — every target's, unless
        /// <see cref="RefuseTargets"/> names which.</summary>
        public string? Refusal { get; set; }

        /// <summary>Which handles <see cref="Refusal"/> applies to. Empty means all of them, which is
        /// the whole-field case; naming one is the ordinary state where a bubble sits over another's
        /// centre.</summary>
        public HashSet<int> RefuseTargets { get; } = [];

        public bool Reachable { get; init; } = true;

        public Action<PointerPress>? OnPress { get; set; }

        public bool CanReachAPointer => Reachable;

        public int OpenTargets => Opened.Count - Closed.Count;

        public CapabilityState? LastPlacement { get; private set; }

        public int MouseActivateQueries { get; private set; }

        public int MouseActivateRefusals { get; private set; }

        public int PressesSeen { get; private set; }

        public PointerStationObservation ObserveStation() => Reachable
            ? new PointerStationObservation(true, true, 1, true)
            : new PointerStationObservation(true, false, 0, false);

        public CapabilityState Open(PointerTargetRequest request, out int target)
        {
            Engagements++;
            target = _next++;
            Opened.Add((target, request.Bounds));
            return LastPlacement = Result(target);
        }

        public CapabilityState Move(int target, PointerBounds bounds)
        {
            Moves.Add((target, bounds));
            return LastPlacement = Result(target);
        }

        public CapabilityState Close(int target)
        {
            Closed.Add(target);
            return new CapabilityState.Available("closed");
        }

        public PointerTargetObservation Observe(int target) => PointerTargetObservation.NotAsked;

        public int Pump(int max)
        {
            var dispatched = 0;
            while (dispatched < max && _queued.TryDequeue(out var press))
            {
                OnPress?.Invoke(press);
                dispatched++;
            }

            return dispatched;
        }

        public void Dispose()
        {
        }

        /// <summary>
        /// Hand the surface a press the way the OS would: naming the target IT chose, and leaving it
        /// in a QUEUE until somebody pumps.
        ///
        /// <para><b>The queue mirrors the product rather than being convenient.</b> A real press
        /// arrives in a message queue and nothing in the module runs until the surface's own pump
        /// drains it; a double that invoked the callback inline would make the presenter's
        /// pump-before-you-move ordering invisible, which is exactly the blindness the Lock Card's §8b review
        /// found in a double that diverged from the product where the bug lived.</para>
        /// </summary>
        public void DeliverPress(int target, PointerPressKind kind)
        {
            PressesSeen++;
            MouseActivateQueries++;
            MouseActivateRefusals++;
            _queued.Enqueue(new PointerPress(target, kind, 4, 4));
        }

        private CapabilityState Result(int target) =>
            Refusal is null || (RefuseTargets.Count > 0 && !RefuseTargets.Contains(target))
                ? new CapabilityState.Available("placed")
                : new CapabilityState.Unavailable(new CapabilityReason(Refusal, "the double refused"));
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

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

        /// <summary>How many live timers are on this clock. A cadence that was cancelled leaves
        /// none; one that was merely dropped on the floor leaves its handle behind for ever.</summary>
        public int PendingCount
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

        private sealed class Handle(Entry entry) : IDisposable
        {
            public void Dispose() => entry.Cancelled = true;
        }
    }

    private sealed class NullSink : ILogSink
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
