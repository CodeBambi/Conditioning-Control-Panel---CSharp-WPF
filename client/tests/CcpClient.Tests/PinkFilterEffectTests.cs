using System.Reflection;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The first module in the port with no schedule.
///
/// <para><b>What is really under test.</b> Not a tint. Every fact below is about whether a module
/// that is simply <i>on</i> can live under the same spine as two modules that fire on a clock — and
/// about the one place where it provably cannot behave like them: its dot. A paced module is
/// <c>Live</c> when a firing is on the CLOCK, which stays true even when every firing will show
/// nothing. This one has no clock, so <c>Live</c> can only mean the SCREEN, and the facts that pin
/// that are the ones that would go red if someone later "simplified" the dot back to the dial.</para>
///
/// <para>No clock is advanced anywhere in this file, because the module has nothing to advance.
/// That is the point.</para>
/// </summary>
public class PinkFilterEffectTests
{
    // ---------------------------------------------------------------------------------
    //  the colour law — WPF's GetFilterRgb / TryParseHexColor, as a pure function
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithNoColourPicked_TheTintIsWpfsOwnHotPink(string? persisted)
    {
        // WPF's chain ends in three literals: 255, 105, 180 (OverlayService.cs:686, and the same
        // triple seeds the parser at :691).
        Assert.Equal((byte)255, PinkFilterColour.Resolve(persisted).Red);
        Assert.Equal((byte)105, PinkFilterColour.Resolve(persisted).Green);
        Assert.Equal((byte)180, PinkFilterColour.Resolve(persisted).Blue);
    }

    [Theory]
    [InlineData("#1A2B3C")]
    [InlineData("1A2B3C")]
    [InlineData("  #1a2b3c  ")]
    public void AUserPickedHexIsHonoured_WithOrWithoutItsHashAndItsWhitespace(string persisted)
    {
        // WPF trims, then TrimStart('#') — so a bare six characters is as valid as a prefixed one
        // (OverlayService.cs:695-696), and the parse is case-insensitive by Convert.ToByte.
        Assert.Equal(((byte)0x1A, (byte)0x2B, (byte)0x3C), PinkFilterColour.Resolve(persisted));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("GGHHII")]
    [InlineData("-12345")]
    [InlineData("#")]
    public void AMalformedColourFallsBackToTheDefault_RatherThanThrowingOrHalfApplying(string persisted)
    {
        // WPF's length gate is exactly 6 (:697) and its parse arm swallows (:704). A half-applied
        // colour would be the worst outcome of the three: a tint nobody chose.
        Assert.False(PinkFilterColour.TryParseHex(persisted, out var rgb));
        Assert.Equal(((byte)255, (byte)105, (byte)180), rgb);
        Assert.Equal(rgb, PinkFilterColour.Resolve(persisted));
    }

    [Fact]
    public void TheOpacityLawIsLINEAR_BecauseWpfSaysSoAtTheLineThatComputesIt()
    {
        // "Linear opacity (no exponential curve)" — OverlayService.cs:1174-1175. A curve here would
        // silently re-mean every existing user's saved number.
        Assert.Equal(0.10, new PinkFilterTint(0, 0, 0, 10).Opacity, precision: 6);
        Assert.Equal(0.50, new PinkFilterTint(0, 0, 0, 50).Opacity, precision: 6);
        Assert.Equal(0.00, new PinkFilterTint(0, 0, 0, 0).Opacity, precision: 6);
        Assert.True(new PinkFilterTint(0, 0, 0, 0).IsInvisible);
        Assert.False(new PinkFilterTint(0, 0, 0, 1).IsInvisible);
    }

    [Fact]
    public void TheDialsCarryWpfsOwnDefaultAndWpfsOwnClamp_AndTheModuleShipsOFF()
    {
        var document = new PinkFilterPresetDocument();

        // AppSettings.cs:3726 (false), :3733 (10), :3737 (Math.Clamp(value, 0, 50)).
        Assert.False(document.Enabled);
        Assert.Equal(10, document.OpacityPercent);

        document.OpacityPercent = 500;
        Assert.Equal(50, document.OpacityPercent);
        document.OpacityPercent = -7;
        Assert.Equal(0, document.OpacityPercent);

        // Zero is INSIDE WPF's range, which is why the module needs an answer for it below.
        Assert.Equal(0, PinkFilterPresetDocument.MinOpacityPercent);
        Assert.Equal(50, PinkFilterPresetDocument.MaxOpacityPercent);
    }

    /// <summary>
    /// <b>The A-001 ceiling as the byte the operating system is actually handed, not as a constant
    /// anybody asserts.</b>
    ///
    /// <para><c>client/docs/architecture.md</c> A-001 makes "the tint never reaches full opacity" a
    /// hard product rule, and until 2026-08-26 the only thing standing behind it was
    /// <see cref="PinkFilterPresetDocument.MaxOpacityPercent"/> — a number, never a measurement. It
    /// was one of three live hypotheses for the owner's reported full white screen, because
    /// <c>HotPink(255, 105, 180)</c> laid over a whole display IS a very bright wash.</para>
    ///
    /// <para><b>Now measured, headed, on the real product</b>: with the dial seeded above its clamp
    /// and the module armed alone, the live full-monitor overlay read <c>LWA_ALPHA</c> <b>128</b>
    /// with <c>LWA_ALPHA</c> flags and a buffer of exactly (255, 105, 180), and the whole-screen
    /// near-white fraction never left the desktop's own baseline. The hypothesis is dead, and this
    /// fact is the arithmetic that killed it, pinned at the seam where it reaches the OS: the tint's
    /// opacity becomes <see cref="OverlaySurfaceRequest.Alpha"/> and nothing else.</para>
    /// </summary>
    [Fact]
    public void AtTheTopOfItsDial_TheTintAsksTheOsForHalfOpacityAndCanNeverAskForAll()
    {
        // Seeded ABOVE the clamp, so this stays a fact about the CEILING when the ceiling moves.
        var document = new PinkFilterPresetDocument { OpacityPercent = int.MaxValue };
        var tint = new PinkFilterTint(
            PinkFilterColour.HotPink.Red,
            PinkFilterColour.HotPink.Green,
            PinkFilterColour.HotPink.Blue,
            document.OpacityPercent);

        // Linear, and WPF says so at the line that computes it (OverlayService.cs:1174-1175).
        Assert.Equal(0.5, tint.Opacity);

        var request = new OverlaySurfaceRequest(
            new OverlayBounds(0, 0, 2880, 1800), tint.Opacity, ClickThrough: true);

        Assert.Equal((byte)128, request.Alpha);
        Assert.NotEqual(byte.MaxValue, request.Alpha);
    }

    // ---------------------------------------------------------------------------------
    //  the module, with no clock anywhere
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AColdModuleIsOff_AndPressingStartChangesNothingAboutIt()
    {
        using var lab = new Lab();

        Assert.Equal(EffectDotState.Off, lab.Effect.Dot);

        var armed = lab.Effect.Arm();

        var refusal = Assert.IsType<CapabilityState.Unavailable>(armed);
        Assert.Equal(EffectReasonCodes.EffectDialOff, refusal.Reason.Code);
        Assert.Equal(EffectDotState.Off, lab.Effect.Dot);
        Assert.Equal(0, lab.Surface.Engagements);
    }

    [Fact]
    public void WithTheDialOn_ArmingPutsTheTintUpImmediately_WithNoClockInvolvedAtAll()
    {
        using var lab = new Lab();
        lab.Effect.SetEnabled(true);

        var armed = lab.Effect.Arm();

        // THE continuous property, in one assertion: nothing was advanced, nothing was scheduled,
        // and the work is already happening. WPF's own start is synchronous for the same reason
        // (OverlayService.cs:366-395, inside DispatcherHelper.RunOnUISync).
        Assert.IsType<CapabilityState.Available>(armed);
        Assert.Equal(1, lab.Surface.Engagements);
        Assert.True(lab.Surface.Showing);
        Assert.Equal(EffectDotState.Live, lab.Effect.Dot);
    }

    [Fact]
    public void TheTintIsTheModulesOwnDials_ReadAtTheMomentItEngages()
    {
        using var lab = new Lab();
        lab.Preset.Mutate(p =>
        {
            p.Enabled = true;
            p.Colour = "#0A141E";
            p.OpacityPercent = 33;
        });

        lab.Effect.Arm();

        Assert.Equal(new PinkFilterTint(0x0A, 0x14, 0x1E, 33), Assert.Single(lab.Surface.Tints));
    }

    // ---------------------------------------------------------------------------------
    //  THE DOT: Live is a claim about the SCREEN here, and about a clock everywhere else
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ADialThatIsOnIsNotEnough_TheDotIsLiveONLYWhileTheSurfaceIsReallyUp()
    {
        // The overlay refuses — every Linux build, by design, and any Windows box the OS reports
        // no display on. Before this module the port had never had to answer the question, because
        // a paced module is Live on the strength of its clock and draws later.
        using var lab = new Lab(surfaceRefusal: "overlay-mechanism-absent");
        lab.Effect.SetEnabled(true);

        var armed = lab.Effect.Arm();

        var refusal = Assert.IsType<CapabilityState.Unavailable>(armed);
        Assert.Equal("overlay-mechanism-absent", refusal.Reason.Code);

        // Armed, NOT Live. The module took the session; nothing is on the user's screen; and the
        // row must not say otherwise. A dot derived from the dial would read Live here.
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
        Assert.True(lab.Effect.Enabled);
    }

    [Fact]
    public void AtZeroOpacity_NothingIsPlaced_TheArmIsDEGRADED_AndTheDotStaysArmed()
    {
        using var lab = new Lab();
        lab.Preset.Mutate(p =>
        {
            p.Enabled = true;
            p.OpacityPercent = 0;
        });

        var armed = lab.Effect.Arm();

        // WPF's clamp allows zero (AppSettings.cs:3737) and WPF at zero still creates a
        // full-screen layered window holding alpha 0 — a window the OS agrees exists, is visible
        // and is on top, that composites nothing. This port refuses to construct that request at
        // all, so the honest answer is Degraded: it really took the session and really shows
        // nothing.
        var degraded = Assert.IsType<CapabilityState.Degraded>(armed);
        Assert.Equal(EffectReasonCodes.PinkFilterTransparent, degraded.Reason.Code);
        Assert.Equal(0, lab.Surface.Engagements);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
    }

    [Fact]
    public void ADegradedArmHereIsNOTTheSameAsSubliminalsDegradedArm_AndTheDotIsWhereTheyDiffer()
    {
        // The control for the fact above, and the sharpest statement of the template finding.
        // Subliminals over an empty pool is ALSO Degraded — and its dot is Live, correctly, because
        // its schedule really is running. This module's Degraded comes with an Armed dot, because
        // it has no schedule to be running. If a later change unified the two, one of these two
        // assertions goes red.
        using var lab = new Lab();
        lab.Preset.Mutate(p =>
        {
            p.Enabled = true;
            p.OpacityPercent = 0;
        });
        lab.Effect.Arm();

        Assert.IsType<CapabilityState.Degraded>(lab.Effect.Arm());
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
        Assert.NotEqual(EffectDotState.Live, lab.Effect.Dot);
    }

    // ---------------------------------------------------------------------------------
    //  stop, idempotence, and the dial mid-session
    // ---------------------------------------------------------------------------------

    [Fact]
    public void Disarm_TakesTheTintOffAtOnce_AndTheDotFallsBack()
    {
        using var lab = new Lab();
        lab.Effect.SetEnabled(true);
        lab.Effect.Arm();

        lab.Effect.Disarm();

        Assert.False(lab.Surface.Showing);
        Assert.Equal(1, lab.Surface.Withdrawals);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
    }

    [Fact]
    public async Task Disarm_TerminatesTheModulesOwnedOperationWithATypedCancelledOutcome()
    {
        using var lab = new Lab();
        lab.Effect.SetEnabled(true);
        lab.Effect.Arm();

        var completion = lab.Effect.Completion;
        Assert.NotNull(completion);

        lab.Effect.Disarm();

        Assert.IsType<OperationOutcome.Cancelled>(await completion);
    }

    [Fact]
    public void ArmingTwiceStartsNoSecondGeneration_ButTheWorkIsReEvaluatedEveryTime()
    {
        using var lab = new Lab();
        lab.Effect.SetEnabled(true);

        lab.Effect.Arm();
        var first = lab.Effect.Completion;
        lab.Effect.Arm();

        // The GENERATION is idempotent (the §5.3 rule at effect granularity) and the WORK is not:
        // arming again re-tints, which is what makes a mid-session switch-on do something.
        Assert.Same(first, lab.Effect.Completion);
        Assert.Equal(2, lab.Surface.Engagements);
    }

    [Fact]
    public void SwitchingTheDialOffMidSession_TakesTheTintDownThroughTheSAMEEligibilityGate()
    {
        using var lab = new Lab();
        lab.Effect.SetEnabled(true);
        lab.Effect.Arm();
        Assert.True(lab.Surface.Showing);

        lab.Effect.SetEnabled(false);
        var refreshed = lab.Effect.Refresh();

        // WPF's reconcile stops the layer when the flag is clear (OverlayService.cs:434-437). Here
        // the shared eligibility gate does it, which is the same gate that stops a paced module
        // scheduling — and it reports the same reason code for both.
        var refusal = Assert.IsType<CapabilityState.Unavailable>(refreshed);
        Assert.Equal(EffectReasonCodes.EffectDialOff, refusal.Reason.Code);
        Assert.False(lab.Surface.Showing);
        Assert.Equal(EffectDotState.Off, lab.Effect.Dot);
    }

    [Fact]
    public void MovingTheOpacityDial_ReTintsWhatIsAlreadyOnScreen_RatherThanWaitingForTheNextSession()
    {
        using var lab = new Lab();
        lab.Effect.SetEnabled(true);
        lab.Effect.Arm();

        lab.Effect.SetOpacityPercent(45);

        // WPF's slider writes, saves and calls RefreshOverlays() so the change lands on the live
        // window (Features/PinkFilterFeatureControl.xaml.cs:99-109 -> OverlayService.cs:434-437).
        Assert.Equal(2, lab.Surface.Engagements);
        Assert.Equal(45, lab.Surface.Tints[^1].OpacityPercent);
        Assert.Equal(45, lab.Preset.Current.OpacityPercent);
    }

    [Fact]
    public void MovingTheOpacityDialToAValueItAlreadyHolds_ChangesNothingAndReTintsNothing()
    {
        using var lab = new Lab();
        lab.Effect.SetEnabled(true);
        lab.Effect.Arm();

        // The clamp is applied BEFORE the comparison, so a slider pinned at its ceiling does not
        // re-place the surface on every pixel of travel past it.
        lab.Effect.SetOpacityPercent(PinkFilterPresetDocument.DefaultOpacityPercent);
        lab.Effect.SetOpacityPercent(9999);
        lab.Effect.SetOpacityPercent(50);

        Assert.Equal(2, lab.Surface.Engagements);
        Assert.Equal(50, lab.Preset.Current.OpacityPercent);
    }

    // ---------------------------------------------------------------------------------
    //  the two states a CONTINUOUS module has that a paced one cannot produce
    // ---------------------------------------------------------------------------------

    [Fact]
    public void WithNoUiThreadBound_NothingIsPlaced_AndTheArmSaysExactlyThat()
    {
        // A paced module never meets this: scheduling needs no UI, and its draw is a later posted
        // projection that skip-until-bound silently drops (contract §5.3). Here the arm IS the
        // draw, so "skipped" is the whole outcome and has to be sayable.
        using var lab = new Lab(bindUi: false);
        lab.Effect.SetEnabled(true);

        var armed = lab.Effect.Arm();

        var refusal = Assert.IsType<CapabilityState.Unavailable>(armed);
        Assert.Equal(EffectReasonCodes.EffectNoUiThread, refusal.Reason.Code);
        Assert.Equal(0, lab.Surface.Engagements);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
    }

    [Fact]
    public void WithNoSurfaceComposedAtAll_TheArmNamesTheMissingSurface_AndNothingThrows()
    {
        using var lab = new Lab(composeSurface: false);
        lab.Effect.SetEnabled(true);

        var armed = lab.Effect.Arm();

        var refusal = Assert.IsType<CapabilityState.Unavailable>(armed);
        Assert.Equal(EffectReasonCodes.EffectNoSurface, refusal.Reason.Code);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);

        // And a stop over a module with no surface is still a stop, not a null reference.
        lab.Effect.Disarm();
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
    }

    // ---------------------------------------------------------------------------------
    //  the shape tripwire — NOT the anti-fake-timer guard itself
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// A cheap structural tripwire over the module's constructor and base class, and it is named for
    /// what it is rather than for what it protects (the final review's wording).
    ///
    /// <para><b>This is not where the fake timer is caught.</b> Reflection over a constructor is
    /// defeatable in one line — a clock constructed in a field initialiser, or reached through a
    /// static, passes this fact untouched. What actually catches a fake timer is BEHAVIOURAL and
    /// lives in two places:</para>
    /// <list type="bullet">
    /// <item><see cref="ContinuousEffectSpineTests"/>
    /// <c>.PressingStart_ArmsAllThree_AndTheContinuousOneIsAlreadyRunningWithNoClockAtAll</c> —
    /// with all three modules armed, the session clock holds exactly TWO pending callbacks, so any
    /// timer this module smuggled in from anywhere would make that count three.</item>
    /// <item><see cref="ContinuousEffectSpineTests"/>
    /// <c>.NoAmountOfClockChangesTheContinuousModule_BecauseItIsNotPaced</c> — twenty flash
    /// intervals of clock go by and the module neither re-places nor withdraws, so a timer that
    /// paced it would move one of those counters.</item>
    /// </list>
    ///
    /// <para>Kept anyway, because it fails at the line a reader is editing rather than three files
    /// away, and because it pins the OTHER half of the split: that the paced base is a SIBLING of
    /// this module under <see cref="OwnedSessionEffect"/> and not its parent.</para>
    /// </summary>
    [Fact]
    public void TheContinuousModulesConstructorAndBaseClass_StillCarryNoClockAndNoPacedBase()
    {
        var constructors = typeof(PinkFilterEffect).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var clockParameters = constructors
            .SelectMany(c => c.GetParameters())
            .Where(p => typeof(ISessionClock).IsAssignableFrom(p.ParameterType))
            .ToList();

        Assert.Empty(clockParameters);
        Assert.Equal(typeof(OwnedSessionEffect), typeof(PinkFilterEffect).BaseType);

        // And the paced base really is the OTHER implementation of the same spine, not its parent.
        Assert.Equal(typeof(OwnedSessionEffect), typeof(PacedSessionEffect<FlashFiring>).BaseType);
        Assert.True(typeof(ISessionEffect).IsAssignableFrom(typeof(OwnedSessionEffect)));
    }

    // =====================================================================================

    /// <summary>The Pink Filter module alone, with a recording surface and a real persisted store.
    /// No clock: the module has none to take.</summary>
    private sealed class Lab : IDisposable
    {
        private readonly string _path;

        public Lab(string? surfaceRefusal = null, bool bindUi = true, bool composeSurface = true)
        {
            _path = Path.Combine(Path.GetTempPath(), "ccp-sp105-lab-" + Guid.NewGuid().ToString("N") + ".json");
            var registry = new OperationRegistry();
            var boundary = new UiDispatchBoundary();
            if (bindUi)
            {
                boundary.Bind(new InlineDispatch());
            }

            Surface = new RecordingSurface { Refusal = surfaceRefusal };
            Preset = new PersistenceStore<PinkFilterPresetDocument>(
                registry.OwnerFor("LabPinkFilterPreset"), new NullSink(), _path,
                PinkFilterPresetDocument.CurrentSchemaVersion);
            Effect = new PinkFilterEffect(
                registry.OwnerFor("LabPinkFilter"),
                new EffectSignal(boundary, static () => true),
                Preset,
                composeSurface ? Surface : null);
        }

        public RecordingSurface Surface { get; }

        public PersistenceStore<PinkFilterPresetDocument> Preset { get; }

        public PinkFilterEffect Effect { get; }

        public void Dispose()
        {
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

    /// <summary>A surface that records what it was asked for and can refuse the way a real backend
    /// does — with a typed state carrying the backend's own reason code.</summary>
    private sealed class RecordingSurface : IPinkFilterSurface
    {
        public List<PinkFilterTint> Tints { get; } = [];

        public int Engagements => Tints.Count;

        public int Withdrawals { get; private set; }

        public string? Refusal { get; init; }

        public bool Showing { get; private set; }

        public CapabilityState? LastPlacement { get; private set; }

        public CapabilityState Engage(PinkFilterTint tint)
        {
            Tints.Add(tint);
            if (Refusal is not null)
            {
                Showing = false;
                LastPlacement = new CapabilityState.Unavailable(
                    new CapabilityReason(Refusal, "recording surface: the backend refused"));
                return LastPlacement;
            }

            Showing = true;
            LastPlacement = new CapabilityState.Available("recording surface: the tint is up");
            return LastPlacement;
        }

        public void Withdraw()
        {
            Withdrawals++;
            Showing = false;
        }
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
}
