using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-115 — the MODULE, constructed and armed, on the lab pattern
/// <see cref="SpiralOverlayEffectTests"/> established.
///
/// <para><b>Why this file exists, stated plainly: the first draft of this packet shipped the module
/// with no test that ever constructed it.</b> <see cref="BouncingTextModuleTests"/> covers the
/// motion, the raster and the presenter's cadence, and reaches the module only through a reflection
/// tripwire and a colour parser. That left <c>Engage</c>, <c>Ready</c>, <c>ReleaseWork</c> and
/// <c>WorkIsRunning</c> entirely unpinned — six mutation survivors on their own — and it made two of
/// the packet's headline claims PROSE: that <c>Ready</c> narrows every healthy run, and that the
/// dot's eighth meaning rests on <c>Running</c> rather than on <c>Showing</c>. The record's
/// classification of those survivors as "needing a rig the unit project does not have" was FALSE:
/// three sibling modules in this same project build exactly this rig.</para>
///
/// <para><b>No clock is constructed anywhere in this file</b>, because the module has none to take.
/// The surface double can be SHOWING and NOT RUNNING, and ENGAGED and NOT SHOWING, because those
/// two states are what the dot and the teardown respectively turn on.</para>
/// </summary>
public class BouncingTextEffectTests
{
    // ---------------------------------------------------------------------- Engage

    [Fact]
    public void AModuleWhoseDialIsOffPlacesNOTHING_AndItsDotIsOff()
    {
        using var lab = new Lab();

        var outcome = lab.Effect.Arm();

        Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Empty(lab.Surface.Engagements);
        Assert.Equal(EffectDotState.Off, lab.Effect.Dot);
    }

    [Fact]
    public void AHealthyEngagePutsTheLogoUp_AndTheDotIsLive()
    {
        using var lab = new Lab(enabled: true);

        var outcome = lab.Effect.Arm();

        Assert.IsType<CapabilityState.Degraded>(outcome);
        Assert.Single(lab.Surface.Engagements);
        Assert.True(lab.Effect.Showing);
        Assert.Equal(EffectDotState.Live, lab.Effect.Dot);
    }

    [Fact]
    public void AModuleWithNoSurfaceInItsCompositionSaysSO_AndDrawsNothing()
    {
        using var lab = new Lab(enabled: true, composeSurface: false);

        var refusal = Assert.IsType<CapabilityState.Unavailable>(lab.Effect.Arm());

        Assert.Equal(EffectReasonCodes.EffectNoSurface, refusal.Reason.Code);
        Assert.False(lab.Effect.Showing);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
    }

    [Fact]
    public void AModuleWithNoUiThreadBoundSaysSO_BecauseTheArmAndTheDrawAreTheSameAct()
    {
        using var lab = new Lab(enabled: true, bindUi: false);

        var refusal = Assert.IsType<CapabilityState.Unavailable>(lab.Effect.Arm());

        Assert.Equal(EffectReasonCodes.EffectNoUiThread, refusal.Reason.Code);
        Assert.Empty(lab.Surface.Engagements);
    }

    [Fact]
    public void OPACITYZEROIsDegradedTRANSPARENT_EvenWithNoSurfaceAndNoUiThread()
    {
        // THE FALSE-EQUIVALENCE FACT. The record claimed the module's invisible-opacity branch was
        // redundant with the presenter's copy. It is NOT: the presenter's copy is reached only
        // AFTER the null-surface gate and the unbound-signal gate, both of which are first-class
        // states in this module's own constructor contract. Delete the module's branch and an
        // opacity-0 engage composed without a surface answers `effect-no-surface`, and one with an
        // unbound signal answers `effect-no-ui-thread` - neither of which is the truth, which is
        // that the user turned the dial to zero.
        using var healthy = new Lab(enabled: true, opacity: 0);
        using var noSurface = new Lab(enabled: true, opacity: 0, composeSurface: false);
        using var noUi = new Lab(enabled: true, opacity: 0, bindUi: false);

        var labs = new[] { healthy, noSurface, noUi };
        Assert.Equal(3, labs.Length);

        foreach (var lab in labs)
        {
            var degraded = Assert.IsType<CapabilityState.Degraded>(lab.Effect.Arm());
            Assert.Equal(EffectReasonCodes.BouncingTextTransparent, degraded.Reason.Code);
        }

        // And nothing was placed on the one composition that could have placed something.
        Assert.Empty(healthy.Surface.Engagements);
        Assert.Equal(EffectDotState.Armed, healthy.Effect.Dot);
    }

    [Fact]
    public void ADialChangeReAppliesToALiveSurface_RatherThanWaitingForTheNextSession()
    {
        using var lab = new Lab(enabled: true);
        lab.Effect.Arm();
        Assert.Single(lab.Surface.Engagements);

        lab.Effect.SetOpacityPercent(40);
        lab.Effect.SetSpeed(9);
        lab.Effect.SetSizePercent(220);

        Assert.Equal(4, lab.Surface.Engagements.Count);
        var last = lab.Surface.Engagements[^1];
        Assert.Equal(40, last.OpacityPercent);
        Assert.Equal(9, last.SpeedSetting);
        Assert.Equal(220, last.SizePercent);
    }

    // ---------------------------------------------------------------------- Ready

    [Fact]
    public void READYONAHEALTHYENGAGEIsDegradedWithTRANSFORMSABSENT_OnEveryRunHoweverHealthy()
    {
        // THE CLAIM THAT WAS PROSE. Every other module in the rack reports a clean Available when
        // everything works; this one must not, because the six missing transform effects are a
        // property of the BUILD rather than of the run. The notice is asserted VERBATIM against the
        // module's own constant, which is the same string the panel renders - so the two cannot
        // drift into two accounts of one absence.
        using var lab = new Lab(enabled: true);

        var degraded = Assert.IsType<CapabilityState.Degraded>(lab.Effect.Arm());

        Assert.Equal(EffectReasonCodes.BouncingTextTransformsAbsent, degraded.Reason.Code);
        Assert.Equal(BouncingTextEffect.TransformsAbsentNotice, degraded.Reason.Detail);

        // The surviving half is the SURFACE's own words, not a summary of them.
        Assert.Equal(lab.Surface.LastPlacementDetail, degraded.SurvivingSemantics);

        // And the dot is still Live: the absence narrows the CLAIM, it does not stop the work.
        Assert.Equal(EffectDotState.Live, lab.Effect.Dot);
    }

    [Fact]
    public void WHEREBOTHCAUSESAreTrue_BOTHTravel_AndTheRunLevelOneFIRST()
    {
        // SP-111's rule, after SP-109 shipped the defect once: a run-level cause the user can act on
        // must not be replaced by the build-level one that is always present.
        using var lab = new Lab(enabled: true, opacity: 0);

        var degraded = Assert.IsType<CapabilityState.Degraded>(lab.Effect.Arm());

        Assert.Equal(EffectReasonCodes.BouncingTextTransparent, degraded.Reason.Code);
        Assert.Contains("0 %", degraded.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains(BouncingTextEffect.TransformsAbsentNotice, degraded.Reason.Detail,
            StringComparison.Ordinal);
        Assert.True(
            degraded.Reason.Detail.IndexOf("0 %", StringComparison.Ordinal)
                < degraded.Reason.Detail.IndexOf(
                    BouncingTextEffect.TransformsAbsentNotice, StringComparison.Ordinal),
            "the build-level cause is printed before the run-level one, so the user reads what they cannot fix "
            + "before what they can");
    }

    [Fact]
    public void ASURFACETHATWENTUPANDSTOPPEDHOLDINGItsComposite_NarrowsTheArmResult()
    {
        // The state only this capability can produce: the OS confirmed the placement and then
        // stopped returning the surface's content.
        using var lab = new Lab(enabled: true);
        lab.Surface.RunningAfterEngage = false;

        var degraded = Assert.IsType<CapabilityState.Degraded>(lab.Effect.Arm());

        Assert.Equal(EffectReasonCodes.BouncingTextNoRaster, degraded.Reason.Code);
        Assert.Contains("no longer returns its content", degraded.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains(BouncingTextEffect.TransformsAbsentNotice, degraded.Reason.Detail,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AREFUSEDPLACEMENTIsPassedThroughUNNARROWED()
    {
        // Ready narrows an Available and adds to a Degraded; it must not dress an Unavailable up as
        // something partly working.
        using var lab = new Lab(enabled: true);
        lab.Surface.Refusal = EffectReasonCodes.BouncingTextNoDisplay;

        var refusal = Assert.IsType<CapabilityState.Unavailable>(lab.Effect.Arm());

        Assert.Equal(EffectReasonCodes.BouncingTextNoDisplay, refusal.Reason.Code);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
    }

    // ---------------------------------------------------------------------- the dot

    [Fact]
    public void THEDOTISLIVEONLYWHILETHEOSSTILLHOLDSTHEINK_NotMerelyWhileTheSurfaceIsUp()
    {
        // THE EIGHTH MEANING, and the fact that separates it from the SECOND. The surface is
        // SHOWING at every point below; the only thing that changes is whether the operating
        // system's own copy of it still carries the frame. A module whose dot read Showing would
        // stay Live through all of it, which is the confident half-truth EffectDotState exists to
        // refuse.
        using var lab = new Lab(enabled: true);
        lab.Effect.Arm();

        Assert.True(lab.Surface.Showing);
        Assert.True(lab.Surface.Running);
        Assert.Equal(EffectDotState.Live, lab.Effect.Dot);

        lab.Surface.Running = false;

        Assert.True(lab.Surface.Showing);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);

        lab.Surface.Running = true;
        Assert.Equal(EffectDotState.Live, lab.Effect.Dot);
    }

    [Fact]
    public void ADisarmedModuleIsNeverLive_HoweverAliveItsSurfaceClaimsToBe()
    {
        using var lab = new Lab(enabled: true);
        lab.Effect.Arm();
        Assert.Equal(EffectDotState.Live, lab.Effect.Dot);

        lab.Effect.Disarm();

        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
    }

    // ---------------------------------------------------------------------- ReleaseWork

    [Fact]
    public void RELEASEWORKWITHDRAWSASURFACEThatIsENGAGEDButNoLongerSHOWING()
    {
        // The teardown guard tests ENGAGED, not SHOWING, and the difference is a leak. A surface
        // whose composite failed is no longer showing and still owns a window AND a cadence timer;
        // a Showing guard would walk past both and leave them for the life of the process.
        using var lab = new Lab(enabled: true);
        lab.Effect.Arm();

        lab.Surface.GoDarkButStayEngaged();
        Assert.False(lab.Surface.Showing);
        Assert.True(lab.Surface.Engaged);

        lab.Effect.Disarm();

        Assert.Equal(1, lab.Surface.Withdrawals);
        Assert.False(lab.Surface.Engaged);
    }

    [Fact]
    public void ReleaseWorkOnAModuleThatOwnsNothing_WithdrawsNothing()
    {
        using var lab = new Lab(enabled: true);

        lab.Effect.Disarm();

        Assert.Equal(0, lab.Surface.Withdrawals);
    }

    // ---------------------------------------------------------------------- identity

    [Fact]
    public void THEROWSTITLENAMESTHEPORTEDHALF_AndTheIdIsTheDispatchKey()
    {
        using var lab = new Lab();

        Assert.Equal("bouncing-text", lab.Effect.Id);
        Assert.Equal(BouncingTextEffect.EffectId, lab.Effect.Id);
        Assert.Equal("Bouncing Text (motion half)", lab.Effect.Title);
        Assert.Contains("motion half", lab.Effect.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEnableDialRoundTripsThroughTheRealPersistedStore()
    {
        using var lab = new Lab();

        Assert.False(lab.Effect.Enabled);
        lab.Effect.SetEnabled(true);

        Assert.True(lab.Effect.Enabled);
        Assert.True(lab.Preset.Current.Enabled);
    }

    // =====================================================================================

    /// <summary>The Bouncing Text module alone, with a recording surface and a real persisted
    /// store. No clock: the module has none to take.</summary>
    private sealed class Lab : IDisposable
    {
        private readonly string _path;

        public Lab(bool enabled = false, bool bindUi = true, bool composeSurface = true, int opacity = 100)
        {
            _path = Path.Combine(Path.GetTempPath(), "ccp-sp115-lab-" + Guid.NewGuid().ToString("N") + ".json");
            var registry = new OperationRegistry();
            var boundary = new UiDispatchBoundary();
            if (bindUi)
            {
                boundary.Bind(new InlineDispatch());
            }

            Surface = new RecordingSurface();
            Preset = new PersistenceStore<BouncingTextPresetDocument>(
                registry.OwnerFor("LabBouncingTextPreset"), new NullSink(), _path,
                BouncingTextPresetDocument.CurrentSchemaVersion);
            Preset.Mutate(p =>
            {
                p.Enabled = enabled;
                p.OpacityPercent = opacity;
            });

            Effect = new BouncingTextEffect(
                registry.OwnerFor("LabBouncingText"),
                new EffectSignal(boundary, static () => true),
                Preset,
                composeSurface ? Surface : null);
        }

        public RecordingSurface Surface { get; }

        public PersistenceStore<BouncingTextPresetDocument> Preset { get; }

        public BouncingTextEffect Effect { get; }

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

    /// <summary>
    /// A surface that records what it was asked for, can refuse the way a real backend does, and —
    /// the two parts this module needed — can be SHOWING and NOT RUNNING at the same time, and
    /// ENGAGED and NOT SHOWING at the same time.
    /// </summary>
    private sealed class RecordingSurface : IBouncingTextSurface
    {
        public List<BouncingTextPresentation> Engagements { get; } = [];

        public int Withdrawals { get; private set; }

        public string? Refusal { get; set; }

        public bool Showing { get; private set; }

        /// <summary>Settable so a test can stop the composite without touching the window.</summary>
        public bool Running { get; set; }

        /// <summary>What Running becomes after a successful engage. False is the surface that went
        /// up and then stopped holding its composite.</summary>
        public bool RunningAfterEngage { get; set; } = true;

        /// <summary>True while there is a window or a cadence to release. Deliberately NOT
        /// <see cref="Showing"/>: see <see cref="GoDarkButStayEngaged"/>.</summary>
        public bool Engaged { get; private set; }

        public int Bounces => Showing ? 7 : 0;

        public CapabilityState? LastPlacement { get; private set; }

        public string LastPlacementDetail { get; private set; } = string.Empty;

        public CapabilityState Engage(BouncingTextPresentation presentation)
        {
            Engagements.Add(presentation);
            if (Refusal is not null)
            {
                Showing = false;
                Running = false;
                Engaged = false;
                LastPlacement = new CapabilityState.Unavailable(
                    new CapabilityReason(Refusal, "recording surface: the backend refused"));
                return LastPlacement;
            }

            Showing = true;
            Engaged = true;
            Running = RunningAfterEngage;
            LastPlacementDetail = "recording surface: the logo is composited";
            LastPlacement = new CapabilityState.Available(LastPlacementDetail);
            return LastPlacement;
        }

        /// <summary>The state a real presenter reaches when a frame stops being held: the window and
        /// the timer are still owned and nothing is on screen.</summary>
        public void GoDarkButStayEngaged()
        {
            Showing = false;
            Running = false;
        }

        public void Withdraw()
        {
            Withdrawals++;
            Showing = false;
            Running = false;
            Engaged = false;
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
