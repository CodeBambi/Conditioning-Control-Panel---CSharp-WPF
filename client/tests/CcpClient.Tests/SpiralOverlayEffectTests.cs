using System.Reflection;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-106 — the first module in the port that has to keep changing while it is on.
///
/// <para><b>What is really under test.</b> Not a spiral. Every fact below is about whether a module
/// whose work is PER-FRAME can live under the same spine as two paced modules and one static one —
/// and about the two places it provably differs from all three: it must carry no scheduler of its
/// own, and its dot must be able to say "on screen and stopped".</para>
///
/// <para><b>No clock is constructed anywhere in this file</b>, because the module has none to take.
/// That is the finding, and <see cref="TheMovingModulesConstructorAndBaseClass_CarryNoClockAndNoPacedBase"/>
/// is the tripwire that says so at the line a future author would edit.</para>
/// </summary>
public class SpiralOverlayEffectTests
{
    // ---------------------------------------------------------------------------------
    //  the opacity law — WPF's own x0.1, as a pure function
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(10, 0.01)]
    [InlineData(50, 0.05)]
    [InlineData(100, 0.10)]
    public void TheDialIsReducedByNinetyPercentBeforeItReachesTheScreen(int dial, double expected)
    {
        // WPF: `var actualOpacity = (opacity / 100.0) * 0.1;` under the comment "Very subtle
        // opacity - 90% reduction" (OverlayService.cs:1689-1690). The factor is behaviour: dropping
        // it would multiply every existing user's spiral by ten.
        Assert.Equal(expected, new SpiralPresentation(dial).Opacity, precision: 9);
        Assert.Equal(0.1, SpiralPresentation.SubtletyFactor);
    }

    [Fact]
    public void AZeroDialIsInvisible_AndTheCeilingIsStillVisible()
    {
        Assert.True(new SpiralPresentation(0).IsInvisible);
        Assert.False(new SpiralPresentation(1).IsInvisible);
        Assert.False(new SpiralPresentation(SpiralPresetDocument.MaxOpacityPercent).IsInvisible);
    }

    [Theory]
    [InlineData(-40, SpiralPresetDocument.MinOpacityPercent)]
    [InlineData(0, 0)]
    [InlineData(37, 37)]
    [InlineData(100, 100)]
    [InlineData(4000, SpiralPresetDocument.MaxOpacityPercent)]
    public void ThePersistedOpacityIsClampedWhereWpfClampsIt(int written, int expected)
    {
        // Math.Clamp(value, 0, 100) (CCP.Core/Models/AppSettings.cs:2675). The ceiling is 100 here
        // and 50 for the pink tint, because this module's own x0.1 already keeps it subtle.
        var document = new SpiralPresetDocument { OpacityPercent = written };
        Assert.Equal(expected, document.OpacityPercent);
    }

    [Fact]
    public void TheModuleShipsON_WhichIsTrueOfNoneOfTheOtherThree()
    {
        // AppSettings.cs:2645 — `private bool _spiralEnabled = true;`. Flash ships on too but the
        // other two ship off; this is the one whose DIAL is on out of the box. It costs nothing on
        // a fresh install because there is no spiral to draw, which is WPF's own second condition
        // (OverlayService.cs:377).
        Assert.True(new SpiralPresetDocument().Enabled);
        Assert.Equal(SpiralPresetDocument.DefaultOpacityPercent, new SpiralPresetDocument().OpacityPercent);
        Assert.Equal(10, SpiralPresetDocument.DefaultOpacityPercent);
        Assert.Equal(string.Empty, new SpiralPresetDocument().Path);
    }

    // ---------------------------------------------------------------------------------
    //  the frame-delay law — WPF's clamp, which FALLS BACK rather than clamping
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(2, 20)]      // exactly the floor: kept
    [InlineData(5, 50)]      // the usual GIF
    [InlineData(50, 500)]    // exactly the ceiling: kept
    [InlineData(0, 50)]      // "as fast as you can" -> the default, NOT the floor
    [InlineData(1, 50)]      // below the floor -> the default
    [InlineData(51, 50)]     // above the ceiling -> the default
    [InlineData(3000, 50)]   // a 30-second frame -> the default
    [InlineData(-4, 50)]     // nonsense -> the default
    public void TheFrameDelayIsWpfsOwnArithmeticAndItsOwnFallback(int hundredths, int expectedMs)
    {
        // `frameDelayMs = value * 10; if (frameDelayMs < 20 || frameDelayMs > 500) frameDelayMs = 50;`
        // (OverlayService.cs:1548-1549). CLAMPING instead would turn a 0-hundredths GIF — which is
        // most of the web's — into 20 ms rather than 50, and a 30-second frame into 500 ms.
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), SpiralFrameDelay.FromHundredths(hundredths));
    }

    // ---------------------------------------------------------------------------------
    //  where the spiral comes from
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AConfiguredSpiralWins_AndAMissingOneFallsThroughToTheLibrary()
    {
        using var lab = new Folders();
        var configured = lab.WriteSpiral("chosen.gif");
        lab.WriteSpiral("aaa-first.gif");

        Assert.Equal(configured, SpiralLibrary.Resolve(lab.AssetsRoot, configured));

        // WPF re-tests File.Exists rather than trusting the setting (OverlayService.cs:302-304):
        // the file the user picked last month may be gone, and that must fall through rather than
        // show nothing.
        Assert.Equal(
            Path.Combine(SpiralLibrary.Folder(lab.AssetsRoot), "aaa-first.gif"),
            SpiralLibrary.Resolve(lab.AssetsRoot, Path.Combine(lab.AssetsRoot, "deleted-last-month.gif")));
    }

    [Fact]
    public void AnEmptyLibraryIsNull_NotAnException_AndSoIsOneWithNothingThisBuildCanDecode()
    {
        using var lab = new Folders();

        // No folder at all: the ordinary first-run state, because this port bundles no spiral (D86).
        Assert.Null(SpiralLibrary.Resolve(lab.AssetsRoot, null));

        lab.WriteSpiral("notes.txt");
        lab.WriteSpiral("clip.webp");
        Directory.CreateDirectory(Path.Combine(SpiralLibrary.Folder(lab.AssetsRoot), "a-subfolder"));

        // .webp is in WPF's own extension list (OverlayService.cs:205) and is NOT in the port's,
        // because GDI+ has no WebP codec — the same hole the flash decoder records (D58). Offering
        // it would be offering a file that silently draws nothing.
        Assert.DoesNotContain(".webp", SpiralLibrary.Extensions);
        Assert.Null(SpiralLibrary.Resolve(lab.AssetsRoot, null));
    }

    [Fact]
    public void TheLibraryPickIsOrdinalAndDeterministic_BecauseThePortHasNoRandomiser()
    {
        using var lab = new Folders();
        lab.WriteSpiral("zebra.gif");
        lab.WriteSpiral("Apple.png");
        lab.WriteSpiral("banana.jpg");

        // Ordinal order puts upper case first, and the SAME machine gives the same answer twice.
        // WPF's enumeration order feeds a randomiser (:311-316) so it does not matter upstream;
        // here it IS the choice, and a hidden random pick a user cannot see or turn off would be
        // worse than a deterministic one (D87).
        var first = SpiralLibrary.Resolve(lab.AssetsRoot, null);
        Assert.Equal(Path.Combine(SpiralLibrary.Folder(lab.AssetsRoot), "Apple.png"), first);
        Assert.Equal(first, SpiralLibrary.Resolve(lab.AssetsRoot, null));
    }

    // ---------------------------------------------------------------------------------
    //  arming: four answers, in the order that keeps each honest
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ArmingWithADialOff_StartsNothing_AndSaysSoInType()
    {
        using var lab = new Lab();
        lab.Preset.Mutate(p => p.Enabled = false);

        var outcome = lab.Effect.Arm();

        var refusal = Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Equal(EffectReasonCodes.EffectDialOff, refusal.Reason.Code);
        Assert.Empty(lab.Surface.Engagements);
        Assert.Equal(EffectDotState.Off, lab.Effect.Dot);
    }

    [Fact]
    public void ArmingWithNoSpiralInTheLibrary_IsDegraded_NotARefusalAndNotASuccess()
    {
        using var lab = new Lab(spiral: null);

        var outcome = lab.Effect.Arm();

        // WPF's own second condition (OverlayService.cs:377). The module really took the session and
        // really shows nothing — the Subliminals-with-an-empty-pool shape, one module later.
        var degraded = Assert.IsType<CapabilityState.Degraded>(outcome);
        Assert.Equal(EffectReasonCodes.SpiralNoImage, degraded.Reason.Code);
        Assert.Empty(lab.Surface.Engagements);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
    }

    [Fact]
    public void ArmingAtZeroOpacity_IsDegradedAndPlacesNothing_TheSameAnswerTheTintGives()
    {
        using var lab = new Lab();
        lab.Preset.Mutate(p => p.OpacityPercent = 0);

        var outcome = lab.Effect.Arm();

        // WPF at zero still puts a full-screen always-on-top window on the desktop holding a fully
        // transparent image; this port refuses to construct one (D78's shape, second module).
        var degraded = Assert.IsType<CapabilityState.Degraded>(outcome);
        Assert.Equal(EffectReasonCodes.SpiralTransparent, degraded.Reason.Code);
        Assert.Empty(lab.Surface.Engagements);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
    }

    [Fact]
    public void ArmingWithNoSurfaceComposed_RefusesInType_RatherThanPretending()
    {
        using var lab = new Lab(composeSurface: false);

        var refusal = Assert.IsType<CapabilityState.Unavailable>(lab.Effect.Arm());

        Assert.Equal(EffectReasonCodes.EffectNoSurface, refusal.Reason.Code);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
    }

    [Fact]
    public void ArmingWithNoUiThreadBound_RefusesInType_BecauseTheArmAndTheDrawAreTheSameAct()
    {
        using var lab = new Lab(bindUi: false);

        var refusal = Assert.IsType<CapabilityState.Unavailable>(lab.Effect.Arm());

        // SP-105's finding, unchanged for a fourth module: it is a property of DRAWING at arm time,
        // not of being static. A paced module schedules on a clock and its draw is a later posted
        // projection that skip-until-bound silently drops (contract 5.3); here "skipped" is the
        // whole outcome and has to be sayable.
        Assert.Equal(EffectReasonCodes.EffectNoUiThread, refusal.Reason.Code);
        Assert.Empty(lab.Surface.Engagements);
    }

    [Fact]
    public void ArmingWithASpiralAndADial_PutsItUpAtOnce_AndTheDotGoesLive()
    {
        using var lab = new Lab();

        var outcome = lab.Effect.Arm();

        Assert.IsType<CapabilityState.Available>(outcome);
        var (path, presentation) = Assert.Single(lab.Surface.Engagements);
        Assert.Equal(lab.SpiralPath, path);
        Assert.Equal(0.01, presentation.Opacity, precision: 9);
        Assert.Equal(EffectDotState.Live, lab.Effect.Dot);
    }

    // ---------------------------------------------------------------------------------
    //  THE DOT'S THIRD MEANING, at the module
    // ---------------------------------------------------------------------------------

    [Fact]
    public void ASurfaceThatIsUpButFrozen_IsNOTLive_ThoughASurfaceThatIsUpAndTurningIs()
    {
        using var lab = new Lab();
        lab.Effect.Arm();
        Assert.Equal(EffectDotState.Live, lab.Effect.Dot);

        // The picture stops. Nothing else changes: the window is still on screen, the OS is still
        // perfectly happy, and Showing is still true.
        lab.Surface.Running = false;

        Assert.True(lab.Effect.Showing);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
    }

    [Fact]
    public void ADotDerivedFromShowingWouldReadLiveHere_WhichIsWhyItIsNot()
    {
        using var lab = new Lab();
        lab.Effect.Arm();
        lab.Surface.Running = false;

        // The two properties DISAGREE, deliberately. If WorkIsRunning were `Showing` — the answer
        // the previous continuous module gives, correctly, because it promises no motion — this
        // module would report a frozen picture as healthy.
        Assert.True(lab.Effect.Showing);
        Assert.NotEqual(EffectDotState.Live, lab.Effect.Dot);
    }

    [Fact]
    public void AnArmWhoseLayerWentUpAndThenStopped_IsNarrowedToDegraded_RatherThanClaimingAvailable()
    {
        using var lab = new Lab();
        lab.Surface.RunningAfterEngage = false;

        var outcome = lab.Effect.Arm();

        // The OS's Available is true ABOUT THE PLACEMENT and false about the module. Ready() is the
        // seat OwnedSessionEffect provides for exactly this narrowing.
        var degraded = Assert.IsType<CapabilityState.Degraded>(outcome);
        Assert.Equal(EffectReasonCodes.SpiralNotDecoded, degraded.Reason.Code);
        Assert.Contains("frozen", degraded.Reason.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ABackendThatRefusesToPresent_IsReportedVerbatim_AndTheDotNeverReadsLive()
    {
        using var lab = new Lab(surfaceRefusal: "overlay-mechanism-absent");

        var outcome = lab.Effect.Arm();

        // The Linux path: the overlay backend refuses by design, so the module arms, repeats the
        // refusal word for word, and shows nothing. Asserted as a REFUSAL path, never as support.
        var refusal = Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Equal("overlay-mechanism-absent", refusal.Reason.Code);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
        Assert.False(lab.Effect.Showing);
    }

    // ---------------------------------------------------------------------------------
    //  stop, and the dial
    // ---------------------------------------------------------------------------------

    [Fact]
    public void Disarming_TakesTheLayerDownAndStopsTheFrames_InOneAct()
    {
        using var lab = new Lab();
        lab.Effect.Arm();

        lab.Effect.Disarm();

        // For a MOVING module "release the work" is two things at once, and the presenter does both
        // inside Withdraw so a caller cannot separate them.
        Assert.Equal(1, lab.Surface.Withdrawals);
        Assert.False(lab.Surface.Showing);
        Assert.Equal(EffectDotState.Armed, lab.Effect.Dot);
    }

    [Fact]
    public async Task DisarmReleasesTheWorkUNCONDITIONALLY_EvenWhenItThoughtItWasNotArmed()
    {
        using var lab = new Lab();
        lab.Effect.Arm();
        var stopped = lab.Effect.Completion!;
        lab.Effect.Disarm();

        // SP-116. THE DISARM ABOVE IS NOT FINISHED WHEN IT RETURNS, and this is the only fact in
        // the project that can see it, because it is the only one that puts something back on the
        // surface behind the module's back. The parked operation's tail calls ReleaseWork a third
        // time from a thread-pool continuation (OwnedSessionEffect.cs:345,357 — the TCS carries
        // RunContinuationsAsynchronously), the guard at :417 lets it through because Disarm does
        // not clear _generation, and this rig's InlineDispatch then runs the withdraw ON that pool
        // thread instead of marshalling it to a UI thread the way the product's
        // Dispatcher.UIThread.Post does. So the re-engagement below and the tail race, and
        // Assert.True(Showing) is decided by the scheduler.
        //
        // Awaiting the module's own published completion is the ordering edge, and it is the same
        // one MovingEffectSpineTests already takes for the same reason (:196-197, and the drained
        // note at :171-174). It is not a wait for an assertion to pass: the operation is finished
        // or the window fails loudly, and the sequence below is then single-threaded.
        // ATailThatLandsAfterSomethingWasPutBackUp_TAKESITDOWN in MovingEffectSpineTests forces
        // that ordering deterministically, so the hazard is pinned rather than merely avoided.
        await TestWait.Until(
            stopped,
            "the spiral module's parked operation to finish, so its own teardown tail cannot withdraw "
            + "the surface this test re-engages after the disarm");
        Assert.IsType<OperationOutcome.Cancelled>(await stopped);

        // Something is on screen and the module does not believe it is armed. Contrived here, and
        // upstream's services all guard against it in as many words — WPF's bouncing text stop says
        // "Always close and clear windows, even if we thought we weren't running"
        // (Services/Subliminal/BouncingTextService.cs:213), and the spiral's own StopSpiral is
        // called unconditionally from OverlayService.Stop (:398-409) rather than under an
        // if-running.
        lab.Surface.Engage(lab.SpiralPath!, lab.Effect.Presentation);
        Assert.True(lab.Surface.Showing);

        lab.Effect.Disarm();

        // The SHARED body's first line is what makes this true: Disarm releases the work before it
        // decides whether there is a generation to cancel, so the not-armed early return cannot
        // leave a layer up. This is also the fact that reds for THIS module when a reviewer removes
        // that line — the other three have their own.
        Assert.False(lab.Surface.Showing);
        Assert.Equal(2, lab.Surface.Withdrawals);
    }

    [Fact]
    public void SwitchingTheDialOffMidSession_TakesTheLayerDownThroughTheSameEligibilityGate()
    {
        using var lab = new Lab();
        lab.Effect.Arm();
        Assert.True(lab.Surface.Showing);

        lab.Effect.SetEnabled(false);
        var outcome = lab.Effect.Refresh();

        // Upstream's own reconcile: the flag goes off and StopSpiral runs (OverlayService.cs:448-450).
        var refusal = Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Equal(EffectReasonCodes.EffectDialOff, refusal.Reason.Code);
        Assert.False(lab.Surface.Showing);
        Assert.Equal(EffectDotState.Off, lab.Effect.Dot);
    }

    [Fact]
    public void MovingTheOpacityDialMidSession_ReEngagesAtOnce_RatherThanAtTheNextSession()
    {
        using var lab = new Lab();
        lab.Effect.Arm();

        lab.Effect.SetOpacityPercent(80);

        // WPF's slider writes and then calls RefreshOverlays() so the change lands on the LIVE
        // window (-> OverlayService.cs:446, UpdateSpiralOpacity).
        Assert.Equal(2, lab.Surface.Engagements.Count);
        Assert.Equal(0.08, lab.Surface.Engagements[1].Presentation.Opacity, precision: 9);
        Assert.Equal(80, lab.Preset.Current.OpacityPercent);
    }

    [Fact]
    public void SettingTheSameOpacityAgain_DoesNothingAtAll()
    {
        using var lab = new Lab();
        lab.Effect.Arm();

        lab.Effect.SetOpacityPercent(lab.Preset.Current.OpacityPercent);

        Assert.Single(lab.Surface.Engagements);
    }

    // ---------------------------------------------------------------------------------
    //  THE ANTI-SCHEDULER TRIPWIRE
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheMovingModulesConstructorAndBaseClass_CarryNoClockAndNoPacedBase()
    {
        // SP-105 wrote this shape for the STATIC module and named what it is worth: reflection over
        // constructors and base types is defeated by a clock built in a field in one line, so the
        // guard really lives in the behavioural facts (MovingEffectSpineTests' PendingCount facts,
        // and SpiralSurfacePresenterTests' two-cadence facts). It earns its keep by failing at the
        // line a future author is editing rather than three files away.
        //
        // For a MOVING module the claim is stronger and less obvious than it was for a static one:
        // this module genuinely HAS a periodic thing to do, and it still must not own the timer,
        // because a cadence that keeps a SURFACE correct belongs to the surface. The presenter
        // takes the clock; the module does not.
        var clockParameters = typeof(SpiralOverlayEffect)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters())
            .Where(p => typeof(ISessionClock).IsAssignableFrom(p.ParameterType))
            .ToList();

        Assert.Empty(clockParameters);
        Assert.Equal(typeof(OwnedSessionEffect), typeof(SpiralOverlayEffect).BaseType);

        // And the paced base really is a SIBLING of this module under the shared body, not a parent.
        Assert.Equal(typeof(OwnedSessionEffect), typeof(PacedSessionEffect<FlashFiring>).BaseType);
        Assert.Equal(typeof(OwnedSessionEffect), typeof(PinkFilterEffect).BaseType);

        // The PRESENTER is where the clock legitimately is, and this pins which side of the seam it
        // sits on: moving it into the module is the change this fact exists to catch.
        Assert.Contains(
            typeof(SpiralSurfacePresenter).GetConstructors().SelectMany(c => c.GetParameters()),
            p => typeof(ISessionClock).IsAssignableFrom(p.ParameterType));
    }

    // =====================================================================================

    /// <summary>The Spiral Overlay module alone, with a recording surface and a real persisted
    /// store. No clock: the module has none to take.</summary>
    private sealed class Lab : IDisposable
    {
        private readonly string _path;

        public Lab(
            string? surfaceRefusal = null,
            bool bindUi = true,
            bool composeSurface = true,
            string? spiral = @"C:\spirals\classic.gif")
        {
            _path = Path.Combine(Path.GetTempPath(), "ccp-sp106-lab-" + Guid.NewGuid().ToString("N") + ".json");
            SpiralPath = spiral;
            var registry = new OperationRegistry();
            var boundary = new UiDispatchBoundary();
            if (bindUi)
            {
                boundary.Bind(new InlineDispatch());
            }

            Surface = new RecordingSurface { Refusal = surfaceRefusal };
            Preset = new PersistenceStore<SpiralPresetDocument>(
                registry.OwnerFor("LabSpiralPreset"), new NullSink(), _path,
                SpiralPresetDocument.CurrentSchemaVersion);
            Effect = new SpiralOverlayEffect(
                registry.OwnerFor("LabSpiral"),
                new EffectSignal(boundary, static () => true),
                Preset,
                () => SpiralPath,
                composeSurface ? Surface : null);
        }

        public string? SpiralPath { get; }

        public RecordingSurface Surface { get; }

        public PersistenceStore<SpiralPresetDocument> Preset { get; }

        public SpiralOverlayEffect Effect { get; }

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

    /// <summary>A real temp assets root, for the library facts only.</summary>
    private sealed class Folders : IDisposable
    {
        public Folders() =>
            AssetsRoot = Path.Combine(Path.GetTempPath(), "ccp-sp106-assets-" + Guid.NewGuid().ToString("N"));

        public string AssetsRoot { get; }

        public string WriteSpiral(string name)
        {
            var folder = SpiralLibrary.Folder(AssetsRoot);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            File.WriteAllBytes(path, [0x00]);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(AssetsRoot, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>A surface that records what it was asked for, can refuse the way a real backend
    /// does, and — the part this module needed — can be SHOWING and NOT RUNNING at the same time.</summary>
    private sealed class RecordingSurface : ISpiralSurface
    {
        public List<(string Path, SpiralPresentation Presentation)> Engagements { get; } = [];

        public int Withdrawals { get; private set; }

        public string? Refusal { get; init; }

        public bool Showing { get; private set; }

        /// <summary>Settable so a test can stop the picture without touching the window.</summary>
        public bool Running { get; set; }

        /// <summary>What Running becomes after a successful engage. False is the layer that went up
        /// and then did not move.</summary>
        public bool RunningAfterEngage { get; set; } = true;

        public bool Engaged => Showing;

        public int FrameCount => Showing ? 8 : 0;

        public CapabilityState? LastPlacement { get; private set; }

        public CapabilityState Engage(string spiralPath, SpiralPresentation presentation)
        {
            Engagements.Add((spiralPath, presentation));
            if (Refusal is not null)
            {
                Showing = false;
                Running = false;
                LastPlacement = new CapabilityState.Unavailable(
                    new CapabilityReason(Refusal, "recording surface: the backend refused"));
                return LastPlacement;
            }

            Showing = true;
            Running = RunningAfterEngage;
            LastPlacement = new CapabilityState.Available("recording surface: the spiral is up");
            return LastPlacement;
        }

        public void Withdraw()
        {
            Withdrawals++;
            Showing = false;
            Running = false;
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
