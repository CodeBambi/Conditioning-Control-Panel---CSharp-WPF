using System.Reflection;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The Intensity Ramp module on its own, with fake dials and a manual clock.
///
/// <para><b>What makes these facts different from the four modules before it.</b> Not one of them
/// needs a surface, a presence, a display or a dispatcher, because the module under test draws
/// nothing at all. Its whole output is a number belonging to another module, so every fact below is
/// an assertion about a value the user could read on a different panel.</para>
///
/// <para><b>Zero wall-clock.</b> The ramp's 2-second sample rides an injected clock advanced by
/// hand.</para>
/// </summary>
public class IntensityRampEffectTests
{
    // ---------------------------------------------------------------------------------
    //  the curve — behaviour-visible arithmetic, ported formula for formula
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(RampCurve.Linear)]
    [InlineData(RampCurve.EaseIn)]
    [InlineData(RampCurve.EaseOut)]
    [InlineData(RampCurve.SCurve)]
    [InlineData(RampCurve.Exponential)]
    public void EveryCurveKeepsTheEndpoints_SoAnyRampStillReachesExactlyItsConfiguredMaximum(RampCurve curve)
    {
        // Upstream states this property in as many words at Helpers/RampCurves.cs:39-42: only the
        // path between the endpoints changes. A curve that failed here would make the chosen shape
        // change WHERE a ramp ends, not just how it gets there.
        Assert.Equal(0.0, RampCurves.ApplyCurve(0.0, curve), 10);
        Assert.Equal(1.0, RampCurves.ApplyCurve(1.0, curve), 10);
    }

    [Fact]
    public void TheCurvesShapeThePathBetweenTheEndpoints_AndTheirOrderAtHalfTimeIsUpstreams()
    {
        // Half-way through the clock, ranked by how much of the climb has been spent.
        var easeIn = RampCurves.ApplyCurve(0.5, RampCurve.EaseIn);
        var exponential = RampCurves.ApplyCurve(0.5, RampCurve.Exponential);
        var linear = RampCurves.ApplyCurve(0.5, RampCurve.Linear);
        var sCurve = RampCurves.ApplyCurve(0.5, RampCurve.SCurve);
        var easeOut = RampCurves.ApplyCurve(0.5, RampCurve.EaseOut);

        Assert.Equal(0.125, easeIn, 10);          // p³ (Helpers/RampCurves.cs:53)
        Assert.Equal(0.5, linear, 10);
        Assert.Equal(0.5, sCurve, 10);            // smoothstep is symmetric about the middle
        Assert.Equal(0.875, easeOut, 10);         // 1 - (1-p)³ (Helpers/RampCurves.cs:56-58)
        Assert.True(exponential < linear, "the exponential curve is back-loaded");
        Assert.True(easeIn < exponential, "and it is still ahead of the cube at half time");
    }

    [Fact]
    public void ProgressOutsideZeroToOneIsClamped_SoACallerThatForgotCannotDriveACurvePastItsEnds()
    {
        // Math.Clamp on the way in (Helpers/RampCurves.cs:49). The ramp clamps its own progress too, so this
        // is belt and braces — and it is the belt, because the helper is public.
        Assert.Equal(0.0, RampCurves.ApplyCurve(-4.0, RampCurve.Exponential), 10);
        Assert.Equal(1.0, RampCurves.ApplyCurve(9.0, RampCurve.EaseIn), 10);
    }

    [Fact]
    public void AnUnknownCurveOrdinalIsTreatedAsLinear_AndIsNotCorrectedAway()
    {
        // Upstream's own default arm (Helpers/RampCurves.cs:69-71) and its combo box's `_ => 0`
        // (IntensityRampFeatureControl.xaml.cs:54). A document written by a newer build survives a
        // round trip through this one rather than being rewritten to Linear on load, which is the
        // persistence contract's unknown-member rule applied to a VALUE.
        var future = (RampCurve)99;
        Assert.Equal(0.4, RampCurves.ApplyCurve(0.4, future), 10);

        var rig = Rig.Create();
        rig.Preset.Mutate(p => p.Curve = future);
        Assert.Equal(future, rig.Ramp.Preset.Curve);
    }

    // ---------------------------------------------------------------------------------
    //  custody — the fourth meaning of the dot
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AColdArmTakesCustodyOfTheLINKEDDialsAndOfNothingElse()
    {
        var rig = Rig.Create();
        rig.Enable();
        rig.Link(rig.Spiral);
        // The multiplier ships at its 1.0 floor (AppSettings.cs:2467), which is its own typed
        // Degraded state; this fact is about custody, so the gain is moved off neutral first.
        rig.Preset.Mutate(p => p.Multiplier = 2.0);

        Assert.IsType<CapabilityState.Available>(rig.Ramp.Arm());

        Assert.Equal([rig.Spiral.Id], rig.Ramp.HeldDials);
        Assert.Equal(10, rig.Ramp.BaseValueFor(rig.Spiral.Id));
        Assert.Null(rig.Ramp.BaseValueFor(rig.Pink.Id));

        // The dial it did NOT take is untouched: not read into custody, not written, not re-applied.
        Assert.Equal(0, rig.Pink.Writes);
        Assert.Equal(0, rig.Pink.Reapplies);
    }

    [Fact]
    public void TheDotIsLiveONLYWhileItHoldsSomething_ARampWithNothingLinkedIsArmedForever()
    {
        // THE NEGATIVE CONTROL FOR THE WHOLE PACKET. This module has a real 2-second timer, so a dot
        // derived the way a PACED module derives it — "a callback is on the clock" — would read Live
        // here. Nothing is linked, so nothing will ever change anywhere in the app, and a dot that
        // said "running" would be exactly the confident half-truth EffectDotState exists to refuse.
        var rig = Rig.Create();
        rig.Enable();

        var armed = Assert.IsType<CapabilityState.Degraded>(rig.Ramp.Arm());
        Assert.Equal(EffectReasonCodes.RampNoLinkedDial, armed.Reason.Code);

        Assert.Equal(1, rig.Clock.PendingCount);            // the cadence really is running
        Assert.Empty(rig.Ramp.HeldDials);                   // and it is holding nothing
        Assert.Equal(EffectDotState.Armed, rig.Ramp.Dot);

        for (var i = 0; i < 200; i++)
        {
            rig.Clock.Advance(IntensityRampEffect.TickInterval);
        }

        Assert.Equal(EffectDotState.Armed, rig.Ramp.Dot);
        Assert.Equal(0, rig.Spiral.Writes);
        Assert.Equal(0, rig.Pink.Writes);
    }

    [Fact]
    public void ARampAtNeutralGainIsLIVE_BecauseItReallyHoldsDialsAndReallyOwesThemBack()
    {
        // The other half of the finding, and the case that separates this module's dot from Pink
        // Filter's. A tint at 0 % opacity has NO WINDOW, so it is Degraded and reads Armed. A ramp at
        // 1.0x is a live machine holding real state: moving the multiplier makes the dials climb with
        // no re-arm, and a stop still gives them back. So: Degraded arm, LIVE dot.
        var rig = Rig.Create();
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Preset.Mutate(p => p.Multiplier = IntensityRampPresetDocument.MinMultiplier);

        var armed = Assert.IsType<CapabilityState.Degraded>(rig.Ramp.Arm());
        Assert.Equal(EffectReasonCodes.RampMultiplierFlat, armed.Reason.Code);
        Assert.Equal(EffectDotState.Live, rig.Ramp.Dot);

        // And it is not a frozen module: the multiplier moves and the dial climbs with no re-arm.
        rig.Ramp.SetMultiplier(3.0);
        rig.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.True(rig.Spiral.Value > 10, "the held dial climbed as soon as the gain moved");
    }

    [Fact]
    public void ADialThatIsOffMakesTheDotOff_WhateverTheModuleIsHolding()
    {
        var rig = Rig.Create();
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Ramp.Arm();
        Assert.Equal(EffectDotState.Live, rig.Ramp.Dot);

        rig.Ramp.SetEnabled(false);

        // Off beats everything, as it does for every module: a dial that is off will do nothing
        // whether or not a session is running.
        Assert.Equal(EffectDotState.Off, rig.Ramp.Dot);
    }

    [Fact]
    public void ACompositionWithNoDialsAtAllRefusesInType_RatherThanRunningATimerThatWritesNothing()
    {
        var rig = Rig.Create(dials: []);
        rig.Enable();

        var armed = Assert.IsType<CapabilityState.Unavailable>(rig.Ramp.Arm());
        Assert.Equal(EffectReasonCodes.RampNoDial, armed.Reason.Code);

        // Nothing is even scheduled: there is no reason to sample progress nobody can act on.
        Assert.Equal(0, rig.Clock.PendingCount);
        Assert.Equal(EffectDotState.Armed, rig.Ramp.Dot);
    }

    // ---------------------------------------------------------------------------------
    //  the arithmetic
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheDialClimbsOnWpfsOwnArithmetic_AndTheIntegerIsTRUNCATEDNotRounded()
    {
        // duration 60, multiplier 3.0, linear, base 10.
        //   progress   = 7.5 / 60          = 0.125
        //   currentMult= 1 + (3 - 1)*0.125 = 1.25
        //   value      = (int)(10 * 1.25)  = (int)12.5 = 12,  NOT 13.
        // WPF: `(int)Math.Min(spiralBase * currentMult, 100)` (MainWindow.StartStop.cs:517). The cast
        // is a truncation and a round here would be visibly wrong at every fractional step.
        //
        // THE SECOND STEP IS THE ONE THAT DISCRIMINATES, and the first alone did not: .NET's
        // Math.Round is MidpointRounding.ToEven, so 12.5 rounds to 12 as well and a rounding
        // implementation would have passed. 12.6 does not: truncation says 12 and BOTH rounding modes
        // say 13. Found by mutating the cast and watching the suite stay green.
        var rig = Rig.Create();
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Preset.Mutate(p =>
        {
            p.DurationMinutes = 60;
            p.Multiplier = 3.0;
            p.Curve = RampCurve.Linear;
        });

        rig.Ramp.Arm();
        Assert.Equal(10, rig.Spiral.Value);

        rig.Clock.Advance(TimeSpan.FromMinutes(7.5));
        Assert.Equal(12, rig.Spiral.Value);

        // progress 7.8/60 = 0.13 -> currentMult 1.26 -> 10 x 1.26 = 12.6 -> truncated, 12.
        rig.Clock.Advance(TimeSpan.FromMinutes(0.3));
        Assert.Equal(1.26, rig.Ramp.CurrentMultiplier, 6);
        Assert.Equal(12, rig.Spiral.Value);
    }

    [Fact]
    public void TheCeilingIsTheOWNINGDialsOwn_AndTheRampCannotDriveItPast()
    {
        // The pink tint's cap is 50, not 100 (MainWindow.StartStop.cs:523; AppSettings.cs:3737), and
        // it is the dial's own clamp maximum — so the ramp can never ask for a value the owning
        // document would silently correct behind it.
        var rig = Rig.Create();
        rig.Enable();
        rig.Link(rig.Pink);
        rig.Pink.Set(40);
        rig.Preset.Mutate(p =>
        {
            p.DurationMinutes = 60;
            p.Multiplier = 3.0;
        });

        rig.Ramp.Arm();
        rig.Clock.Advance(TimeSpan.FromMinutes(60));

        // ASSERTED AGAINST THE REQUEST, NOT THE STORED VALUE, and that is the whole point of this
        // fact. 40 x 3.0 is 120; the MODULE's `Math.Min(baseValue * multiplier, dial.Ceiling)` is
        // what has to turn that into 50. An assertion on rig.Pink.Value alone cannot see it — every
        // real dial clamps on the way in too (PinkFilterPresetDocument.OpacityPercent does), so
        // deleting the module's clamp would leave the stored value at 50 and the fact still green.
        // Found by mutation, after the same class of hole was found once already (record §5.1).
        Assert.NotEmpty(rig.Pink.Requested);
        Assert.All(rig.Pink.Requested, requested => Assert.InRange(requested, 0, 50));
        Assert.Equal(50, rig.Pink.Requested[^1]);
        Assert.Equal(50, rig.Pink.Value);
    }

    [Fact]
    public void TheClimbStopsAtFullProgress_NoMatterHowMuchLongerTheSessionRuns()
    {
        var rig = Rig.Create();
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Preset.Mutate(p =>
        {
            p.DurationMinutes = 30;
            p.Multiplier = 2.0;
        });

        rig.Ramp.Arm();
        rig.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(20, rig.Spiral.Value);
        Assert.Equal(1.0, rig.Ramp.Progress, 6);

        var writes = rig.Spiral.Writes;
        rig.Clock.Advance(TimeSpan.FromHours(5));

        // `Math.Min(elapsed / duration, 1.0)` (MainWindow.StartStop.cs:493): a session that outlives
        // its ramp does not keep climbing, and nothing is rewritten either.
        Assert.Equal(1.0, rig.Ramp.Progress, 6);
        Assert.Equal(20, rig.Spiral.Value);
        Assert.Equal(writes, rig.Spiral.Writes);
    }

    [Fact]
    public void ADialIsWrittenOnlyWhenItsIntegerReallyMoves_WhichIsWhatKeepsAReTintOffTheHotPath()
    {
        // D96. Upstream writes and reconciles every 2 s; here a re-apply reaches OverlaySurfaceSet.Place
        // -> Present, which walks the OS z-order and toggles click-through in both polarities.
        // Over an hour that is 1800 of them for a number that changed about twenty times.
        var rig = Rig.Create();
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Preset.Mutate(p =>
        {
            p.DurationMinutes = 60;
            p.Multiplier = 3.0;
        });

        rig.Ramp.Arm();

        // Sampled at the module's real cadence for a full hour, one 2-second step at a time — which
        // is the only way to tell "wrote once per sample" from "wrote once per integer".
        for (var i = 0; i < 1800; i++)
        {
            rig.Clock.Advance(IntensityRampEffect.TickInterval);
        }

        // 10 -> 30 is twenty integers, and 1800 samples got there in twenty writes.
        Assert.Equal(30, rig.Spiral.Value);
        Assert.Equal(20, rig.Spiral.Writes);
        Assert.Equal(20, rig.Spiral.Reapplies);
        Assert.Equal(1800, rig.Clock.FiredCount);
    }

    [Fact]
    public void TheShapeOfTheClimbIsTheCurvesAndTheFINISHLINEIsTheClocks()
    {
        // Upstream shapes the VALUE with the eased progress and tests COMPLETION against the raw
        // linear progress, and says why at MainWindow.StartStop.cs:495-497: "the completion check
        // below still uses the raw linear progress so the ramp ends at its configured duration
        // regardless of curve". Two inputs, one clock.
        var rig = Rig.Create();
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Preset.Mutate(p =>
        {
            p.DurationMinutes = 60;
            p.Multiplier = 3.0;
            p.Curve = RampCurve.Exponential;
            p.EndSessionOnComplete = true;
        });

        var completions = 0;
        rig.Ramp.Completed += () => completions++;
        rig.Ramp.Arm();

        rig.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(0, completions);
        // Back-loaded: half the clock has gone and barely a fifth of the climb has (10 -> 13, where
        // linear would be 20).
        Assert.InRange(rig.Spiral.Value, 11, 15);

        rig.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(1, completions);
        Assert.Equal(30, rig.Spiral.Value);
    }

    // ---------------------------------------------------------------------------------
    //  giving it back — the half no module before this one had
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AStopGivesEveryHeldDialBackEXACTLY_EvenAfterTheClimbFinished()
    {
        // WPF's StopRampTimer writes every captured base value back (MainWindow.StartStop.cs:437-479).
        // This is the half a paced or a continuous module has no counterpart for: releasing this
        // module's work means UNDOING a change it made to somebody else's state.
        var rig = Rig.Create();
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Link(rig.Pink);
        rig.Spiral.Set(17);
        rig.Pink.Set(23);
        rig.Preset.Mutate(p => p.Multiplier = 2.0);

        rig.Ramp.Arm();
        rig.Clock.Advance(TimeSpan.FromHours(3));
        Assert.True(rig.Spiral.Value > 17 && rig.Pink.Value > 23, "both climbed");

        rig.Ramp.Disarm();

        Assert.Equal(17, rig.Spiral.Value);
        Assert.Equal(23, rig.Pink.Value);
        Assert.Empty(rig.Ramp.HeldDials);
        Assert.Equal(0, rig.Clock.PendingCount);
        Assert.Equal(EffectDotState.Armed, rig.Ramp.Dot);
    }

    [Fact]
    public void SwitchingTheDialOffMidSessionGivesTheDialsBack_ThroughTheSAMEEligibilityGate()
    {
        var rig = Rig.Create();
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Preset.Mutate(p => p.Multiplier = 2.0);
        rig.Ramp.Arm();
        rig.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(20, rig.Spiral.Value);

        // The dial goes off and the module re-evaluates. OwnedSessionEffect's shared gate releases
        // the work, and for THIS module releasing the work means handing the dial back — the same
        // shape found for the tint, in a currency with no pixels in it.
        rig.Ramp.SetEnabled(false);
        rig.Ramp.Refresh();

        Assert.Equal(10, rig.Spiral.Value);
        Assert.Empty(rig.Ramp.HeldDials);
        Assert.Equal(0, rig.Clock.PendingCount);
        Assert.Equal(EffectDotState.Off, rig.Ramp.Dot);
    }

    [Fact]
    public void TheWriteIsSYNCHRONOUSAndTheReApplyIsDISPATCHED_SoARestoreSurvivesADispatcherThatIsDown()
    {
        // D95, and it is a PORT-SIDE hazard rather than an upstream one. On this port's teardown path
        // a post is never delivered — the dispatcher is already down — so a combined operation would
        // leave the ramped value in the document for PersistenceStore.FlushAsync to write over the
        // user's own. Upstream has no such hole: every exit path stops the engine before it saves,
        // and StopEngine -> StopRampTimer IS the restore (MainWindow.StartStop.cs:388 -> :437-479).
        var dropped = 0;
        var rig = Rig.Create(dispatch: _ => dropped++);
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Preset.Mutate(p => p.Multiplier = 2.0);

        rig.Ramp.Arm();
        rig.Clock.Advance(TimeSpan.FromHours(2));

        // The VALUE moved even though nothing dispatched ran.
        Assert.Equal(20, rig.Spiral.Value);
        Assert.Equal(0, rig.Spiral.Reapplies);
        Assert.True(dropped > 0, "the re-apply really was routed through the dispatch");

        rig.Ramp.Disarm();

        // And it came back, on a path where the dispatch delivers nothing at all.
        Assert.Equal(10, rig.Spiral.Value);
        Assert.Equal(0, rig.Spiral.Reapplies);
    }

    // ---------------------------------------------------------------------------------
    //  the clock is a CADENCE, not a schedule
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheClimbIsNotRestartedByARefresh_SoMovingASliderMidSessionDoesNotRewindTheRamp()
    {
        // WPF starts its ramp clock in StartRampTimer and nowhere else (MainWindow.StartStop.cs:424).
        // A user twenty minutes into a session who moves the duration slider is still twenty minutes
        // in; a re-engage that reset the clock would hand them their ramp back from the beginning.
        var rig = Rig.Create();
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Preset.Mutate(p =>
        {
            p.DurationMinutes = 60;
            p.Multiplier = 3.0;
        });

        rig.Ramp.Arm();
        rig.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(0.5, rig.Ramp.Progress, 6);

        rig.Ramp.SetMultiplier(2.0);
        rig.Ramp.Refresh();
        rig.Ramp.Refresh();

        Assert.Equal(0.5, rig.Ramp.Progress, 6);
        Assert.Equal(15, rig.Spiral.Value);      // 10 x (1 + 1x0.5)
    }

    [Fact]
    public void ArmingTwiceStartsNoSecondCadence_AndTheDialIsNotTakenTwice()
    {
        var rig = Rig.Create();
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Ramp.Arm();
        rig.Ramp.Arm();
        rig.Ramp.Arm();

        Assert.Equal(1, rig.Clock.PendingCount);
        Assert.Equal([rig.Spiral.Id], rig.Ramp.HeldDials);
        Assert.Equal(10, rig.Ramp.BaseValueFor(rig.Spiral.Id));
    }

    [Fact]
    public void NoAmountOfClockAfterAStopMovesADial_BecauseTheCadenceDiedWithTheGeneration()
    {
        var rig = Rig.Create();
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Preset.Mutate(p => p.Multiplier = 3.0);
        rig.Ramp.Arm();
        rig.Clock.Advance(TimeSpan.FromMinutes(10));
        rig.Ramp.Disarm();

        var writes = rig.Spiral.Writes;
        rig.Clock.Advance(TimeSpan.FromHours(10));

        Assert.Equal(writes, rig.Spiral.Writes);
        Assert.Equal(10, rig.Spiral.Value);
        Assert.Equal(0, rig.Clock.PendingCount);
    }

    // ---------------------------------------------------------------------------------
    //  the two divergences a reader should be able to see
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ADialLinkedMIDSESSIONIsCapturedAtTheValueTheUserLeftItAt_NotAtSessionStart()
    {
        // D97. WPF captures all five base values once, in StartRampTimer (:417-421), so a dial the
        // user moved by hand mid-session is later restored to a value from BEFORE they moved it.
        // Here a dial's base is taken the first time it is LINKED, which is the same number at
        // session start and a truer one afterwards.
        var rig = Rig.Create();
        rig.Enable();
        rig.Preset.Mutate(p => p.Multiplier = 2.0);
        rig.Ramp.Arm();

        rig.Clock.Advance(TimeSpan.FromMinutes(20));
        rig.Pink.Set(30);                              // the user moves the tint by hand
        rig.Link(rig.Pink);                            // and only then links it
        rig.Ramp.Refresh();

        Assert.Equal(30, rig.Ramp.BaseValueFor(rig.Pink.Id));

        rig.Clock.Advance(TimeSpan.FromHours(2));
        // Climbed, and the ceiling was applied by the MODULE — 30 x 2.0 is 60 and nothing above 50
        // was ever asked for. Asserted against the request for the reason in
        // TheCeilingIsTheOWNINGDialsOwn_AndTheRampCannotDriveItPast.
        Assert.All(rig.Pink.Requested, requested => Assert.InRange(requested, 0, 50));
        Assert.Equal(50, rig.Pink.Value);

        rig.Ramp.Disarm();
        Assert.Equal(30, rig.Pink.Value);              // back to what the USER last chose
    }

    [Fact]
    public void UnlinkingMidRampLeavesTheDialWhereItIs_AndTheStopSTILLPutsItBack()
    {
        // Upstream's shape: unlinking stops the tick writing that dial (the `if` at :514) but the
        // captured base is not dropped, so StopRampTimer still restores it (:461-464). Kept, because
        // the alternative — forgetting custody on an untick — silently loses the user's value.
        var rig = Rig.Create();
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Preset.Mutate(p => p.Multiplier = 2.0);
        rig.Ramp.Arm();
        rig.Clock.Advance(TimeSpan.FromMinutes(30));
        var parked = rig.Spiral.Value;
        Assert.True(parked > 10);

        rig.Preset.Mutate(p => p.LinkSpiralOpacity = false);
        rig.Ramp.Refresh();
        rig.Clock.Advance(TimeSpan.FromHours(3));

        Assert.Equal(parked, rig.Spiral.Value);                 // frozen where the ramp left it
        Assert.Equal([rig.Spiral.Id], rig.Ramp.HeldDials);      // and still owed back

        rig.Ramp.Disarm();
        Assert.Equal(10, rig.Spiral.Value);
    }

    // ---------------------------------------------------------------------------------
    //  the stand-down while a SCRIPTED session runs (MainWindow/MainWindow.StartStop.cs:492)
    // ---------------------------------------------------------------------------------

    [Fact]
    public void StandingDownIsAboutWRITES_TheCadenceAndTheClimbAndTheCUSTODYAllCarryOn()
    {
        // Upstream computes progress and the multiplier ABOVE its guard and unconditionally
        // (:495-503), keeps the five base values it captured at StartRampTimer (:420-424), and only
        // skips the three visual WRITES (:509, :515, :523). So a wholesale "the ramp is off during a
        // session" would be a different feature: the ramp that comes back when the session ends
        // would be a ramp that had not been running.
        var active = false;
        var rig = Rig.Create(scriptedSessionActive: () => active);
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Preset.Mutate(p =>
        {
            p.DurationMinutes = 10;
            p.Multiplier = 3.0;
            p.Curve = RampCurve.Linear;
        });

        rig.Ramp.Arm();
        rig.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(12, rig.Spiral.Value);
        Assert.Equal([SpiralOverlayEffect.EffectId], rig.Ramp.HeldDials);

        active = true;
        var writes = rig.Spiral.Writes;
        rig.Clock.Advance(TimeSpan.FromMinutes(4));

        // Not one write in four minutes of ticks — and the ramp is still a running machine that
        // still owes this dial back.
        Assert.Equal(writes, rig.Spiral.Writes);
        Assert.Equal(12, rig.Spiral.Value);
        Assert.Equal([SpiralOverlayEffect.EffectId], rig.Ramp.HeldDials);
        Assert.Equal(10, rig.Ramp.BaseValueFor(SpiralOverlayEffect.EffectId));
        Assert.Equal(EffectDotState.Live, rig.Ramp.Dot);

        // The climb kept its own place while it stood down: 5/10 linear is multiplier 2.0, which is
        // where the dial resumes rather than where it left off.
        Assert.Equal(0.5, rig.Ramp.Progress, 6);
        Assert.Equal(2.0, rig.Ramp.CurrentMultiplier, 6);

        active = false;
        rig.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(20, rig.Spiral.Value);

        // And the base it gives back is the USER's 10, never the session's anything.
        rig.Ramp.Disarm();
        Assert.Equal(10, rig.Spiral.Value);
    }

    [Fact]
    public void ADialLinkedWHILEASessionRunsIsNotCaptured_BecauseTheValueThereIsTheSESSIONS()
    {
        // THE ONE PLACE THIS PORT MUST BE STRICTER THAN UPSTREAM. Upstream captures all five bases
        // once, at StartRampTimer (:420-424), from the user's own settings before any session
        // exists. This port captures a base the first time a dial is LINKED (D97), so a tick during
        // a session would capture the SESSION's imposed opacity as if the user had chosen it — and
        // hand that back at STOP. That is upstream's #471/#476 defect, and the stand-down skips the
        // dial WHOLE (no read, no capture, no write) rather than only its write.
        var active = true;
        var rig = Rig.Create(scriptedSessionActive: () => active);
        rig.Enable();
        rig.Preset.Mutate(p =>
        {
            p.DurationMinutes = 10;
            p.Multiplier = 3.0;
        });

        rig.Ramp.Arm();

        // The session has imposed its own value on the dial, and the user ticks the link now.
        rig.Spiral.Set(80);
        rig.Link(rig.Spiral);
        rig.Clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Empty(rig.Ramp.HeldDials);
        Assert.Null(rig.Ramp.BaseValueFor(SpiralOverlayEffect.EffectId));
        Assert.Equal(80, rig.Spiral.Value);
        Assert.Equal(0, rig.Spiral.Writes);

        // A stop mid-session therefore hands NOTHING back — the session's own restore owns that
        // dial — and the value the ramp would otherwise have frozen into the user's document is
        // never in its custody at all.
        rig.Ramp.Disarm();
        Assert.Equal(80, rig.Spiral.Value);
    }

    [Fact]
    public void TheRefusalNamesTheSESSIONRatherThanClaimingNothingIsLinked()
    {
        // Both causes land on ramp-no-linked-dial because the operational state is the same — no
        // dial is held — but the sentence a user is shown must not say "you linked nothing" to
        // someone who linked two things and is watching a session drive both.
        var active = true;
        var rig = Rig.Create(scriptedSessionActive: () => active);
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Link(rig.Pink);

        var standingDown = Assert.IsType<CapabilityState.Degraded>(rig.Ramp.Arm());
        Assert.Equal(EffectReasonCodes.RampNoLinkedDial, standingDown.Reason.Code);
        Assert.Contains("scripted session owns the dials", standingDown.Reason.Detail, StringComparison.Ordinal);
        Assert.Equal(EffectDotState.Armed, rig.Ramp.Dot);

        // The other cause keeps its own sentence: nothing linked, no session anywhere.
        var nothingLinked = Rig.Create();
        nothingLinked.Enable();
        var idle = Assert.IsType<CapabilityState.Degraded>(nothingLinked.Ramp.Arm());
        Assert.Equal(EffectReasonCodes.RampNoLinkedDial, idle.Reason.Code);
        Assert.Contains("no effect is linked", idle.Reason.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ARampThatFINISHESDuringASessionEndsNothing_AndStopsTheEngineOnTheFirstTickAfterIt()
    {
        // The FOURTH guarded site (:549) and the one the board row did not name. Upstream states
        // the reason at :544-548: a shorter ramp calling StopEngine() here "tore down the manual
        // services but left the preset timer counting down (#444)". The port's own Completed
        // handler is literally Engine.Stop() (Session/SessionParticipant.cs).
        var active = true;
        var completions = 0;
        var rig = Rig.Create(scriptedSessionActive: () => active);
        rig.Enable();
        rig.Link(rig.Spiral);
        rig.Preset.Mutate(p =>
        {
            p.DurationMinutes = 10;
            p.Multiplier = 2.0;
            p.EndSessionOnComplete = true;
        });
        rig.Ramp.Completed += () => completions++;

        rig.Ramp.Arm();
        rig.Clock.Advance(TimeSpan.FromMinutes(30));

        // Full progress, three times over, and not one stop while the session holds the floor.
        Assert.Equal(1.0, rig.Ramp.Progress, 6);
        Assert.Equal(0, completions);

        // AND IT IS NOT LATCHED. Upstream has no "already announced" flag and re-tests the whole
        // condition every tick, so the stop it owed arrives on the first tick after the session
        // lets go rather than never.
        active = false;
        rig.Clock.Advance(IntensityRampEffect.TickInterval);
        Assert.Equal(1, completions);

        // Once, though — the announcement latches for real once it is allowed to happen.
        rig.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, completions);
    }

    // ---------------------------------------------------------------------------------
    //  the structural claim: no surface, and not paced
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheNonDrawingModuleTakesNOSurface_AndIsNotAPacedEffect()
    {
        // What reflection is worth here is the earlier modules' wording, kept: the real guard is the
        // counting facts above, and this one earns its keep by failing at the line a future author is
        // editing. Two claims, and the FIRST is the packet's second trap — a module that needs no
        // surface must not acquire one for symmetry.
        var constructor = Assert.Single(typeof(IntensityRampEffect).GetConstructors());
        var parameters = constructor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.DoesNotContain(parameters, t => t.Namespace == "CcpClient.Desktop.Overlay");
        Assert.DoesNotContain(parameters, t => t.Name.Contains("Surface", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(IntensityRampEffect)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Select(f => f.FieldType),
            t => t.Name.Contains("Surface", StringComparison.Ordinal)
                || t.Namespace == "CcpClient.Desktop.Overlay");

        // It IS an OwnedSessionEffect and it is NOT a PacedSessionEffect — which is the packet's
        // third trap answered: having a timer is not sufficient to be paced.
        Assert.True(typeof(OwnedSessionEffect).IsAssignableFrom(typeof(IntensityRampEffect)));
        for (var type = typeof(IntensityRampEffect).BaseType; type is not null; type = type.BaseType)
        {
            Assert.False(
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PacedSessionEffect<>),
                "the ramp must not derive from the paced base: its Live would become a clock claim");
        }

        // And it DOES take a clock, in the module rather than in a presenter, because there is no
        // presenter to put one in.
        Assert.Contains(parameters, t => t == typeof(ISessionClock));
    }

    // =====================================================================================

    private sealed class Rig
    {
        private Rig(
            IntensityRampEffect ramp,
            PersistenceStore<IntensityRampPresetDocument> preset,
            ManualSessionClock clock,
            FakeDial spiral,
            FakeDial pink)
        {
            Ramp = ramp;
            Preset = preset;
            Clock = clock;
            Spiral = spiral;
            Pink = pink;
        }

        public IntensityRampEffect Ramp { get; }

        public PersistenceStore<IntensityRampPresetDocument> Preset { get; }

        public ManualSessionClock Clock { get; }

        /// <summary>Stands in for the Spiral Overlay's opacity: ceiling 100, starting at its
        /// document default of 10 % (AppSettings.cs:2671).</summary>
        public FakeDial Spiral { get; }

        /// <summary>Stands in for the Pink Filter's tint opacity: ceiling <b>50</b>, starting at 10 %
        /// (AppSettings.cs:3733,3737).</summary>
        public FakeDial Pink { get; }

        public static Rig Create(
            IReadOnlyList<IIntensityDial>? dials = null,
            Action<Action>? dispatch = null,
            Func<bool>? scriptedSessionActive = null)
        {
            var registry = new OperationRegistry();
            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());
            var clock = new ManualSessionClock();
            var spiral = new FakeDial(SpiralOverlayEffect.EffectId, ceiling: 100, initial: 10);
            var pink = new FakeDial(PinkFilterEffect.EffectId, ceiling: 50, initial: 10);

            // Never started, so nothing touches a disk: Current serves the defaults and Mutate moves
            // them, which is every persisted read this module makes.
            var preset = new PersistenceStore<IntensityRampPresetDocument>(
                registry.OwnerFor("RampPreset"), new NullSink(),
                Path.Combine(Path.GetTempPath(), "ccp-sp108-unused.json"),
                IntensityRampPresetDocument.CurrentSchemaVersion);

            var ramp = new IntensityRampEffect(
                registry.OwnerFor("IntensityRamp"),
                new EffectSignal(boundary, static () => true),
                clock,
                preset,
                dials ?? [spiral, pink],
                dispatch ?? (action => action()),
                scriptedSessionActive ?? (static () => false));

            return new Rig(ramp, preset, clock, spiral, pink);
        }

        public void Enable() => Preset.Mutate(p => p.Enabled = true);

        public void Link(FakeDial dial) => Preset.Mutate(p =>
        {
            if (dial.Id == SpiralOverlayEffect.EffectId)
            {
                p.LinkSpiralOpacity = true;
            }
            else
            {
                p.LinkPinkFilterOpacity = true;
            }
        });
    }

    /// <summary>
    /// A dial with no module behind it: it records what the ramp asked for and touches nothing. That
    /// it can exist at all is the point — the ramp knows nothing about spirals or tints, which is why
    /// none of these facts needs a surface.
    ///
    /// <para><b>It keeps the RAW request beside the stored value, and that is not decoration.</b>
    /// A real dial clamps on the way in — <c>PinkFilterPresetDocument.OpacityPercent</c> does
    /// (<c>Session/PinkFilterPresetDocument.cs:67</c>) and so does this double — so an assertion made
    /// only against <see cref="Value"/> cannot tell the MODULE's ceiling clamp from the DIAL's. That
    /// is exactly the shape of the fact that survived a mutation in this packet (record §5.1), and
    /// <see cref="Requested"/> is what closes it.</para>
    /// </summary>
    private sealed class FakeDial(string id, int ceiling, int initial) : IIntensityDial
    {
        public string Id => id;

        public string Label => $"{id} opacity";

        public int Ceiling => ceiling;

        public int Value { get; private set; } = initial;

        public int Writes { get; private set; }

        public int Reapplies { get; private set; }

        /// <summary>Every value the ramp ASKED for, before this dial clamped anything.</summary>
        public List<int> Requested { get; } = [];

        /// <summary>A hand move by the user, which the ramp neither made nor counts.</summary>
        public void Set(int value) => Value = value;

        public int Read() => Value;

        public void Write(int percent)
        {
            Requested.Add(percent);
            Value = Math.Clamp(percent, 0, ceiling);
            Writes++;
        }

        public void Reapply() => Reapplies++;
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

    /// <summary>The manual clock, in the shape every module test shares, with a fired counter so
    /// a fact can tell "sampled often" from "wrote often". Zero wall-clock.</summary>
    private sealed class ManualSessionClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);

        public int FiredCount { get; private set; }

        public int PendingCount
        {
            get
            {
                lock (_timers)
                {
                    return _timers.Count;
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

            return new CancelHandle(this, entry);
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

                FiredCount++;
                next.Fire();
            }
        }

        private sealed class Entry
        {
            public DateTimeOffset Due;

            public required Action Fire;
        }

        private sealed class CancelHandle(ManualSessionClock clock, Entry entry) : IDisposable
        {
            public void Dispose()
            {
                lock (clock._timers)
                {
                    clock._timers.Remove(entry);
                }
            }
        }
    }
}
