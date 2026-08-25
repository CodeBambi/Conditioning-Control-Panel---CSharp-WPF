using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// How a flash reaches a surface, and what happens when there is no surface to reach.
///
/// <para>Everything here runs on an injected clock. The stagger between a flash's images, each
/// surface's lifetime and WPF's topmost cadence are all timings a user can see, so they are ported
/// exactly and driven by hand: not one wall-clock wait anywhere in this file.</para>
///
/// <para>The overlay is a test double in most of these, deliberately — the point of these facts is
/// the ORDER, the COUNT and the LIFECYCLE of the calls the presenter makes, which is not something a
/// real window can be asked about. <see cref="FlashDrawTests"/> owns the other half: real surfaces,
/// real pixels, and the operating system asked what it holds.</para>
/// </summary>
public class FlashSurfacePresenterTests
{
    private static readonly OverlayBounds Display = new(0, 0, 1920, 1080);

    /// <summary>
    /// Upstream's <c>FADE_PER_SEC</c> WRITTEN OUT (<c>Services/Flash/FlashService.cs:2018</c>),
    /// never read from <see cref="FlashFade.RatePerSecond"/>.
    ///
    /// <para>Every expectation below is computed from THIS number, because an expectation computed
    /// from the constant under test moves with the mutation and proves nothing: the reproducing
    /// lane caught exactly that shape in its own first draft of the flash-geometry fact. Halving
    /// the port's rate must red these facts, and it only can if the number they check against is
    /// upstream's.</para>
    /// </summary>
    private const double UpstreamFadePerSecond = 2.4;

    /// <summary>How long a ramp to FULL opacity takes at upstream's rate: 1.0 / 2.4, about
    /// 417 ms — the fade-in, and the fade-out, at the top of the opacity dial.</summary>
    private static readonly TimeSpan FullRamp = TimeSpan.FromSeconds(1.0 / UpstreamFadePerSecond);

    // ---------------------------------------------------------------------------------
    //  one surface per image, WPF's stagger, WPF's lifetime
    // ---------------------------------------------------------------------------------

    [Fact]
    public void AFlashPutsOneSurfaceOnScreenPerImage_StaggeredByWpfsThreeHundredMilliseconds()
    {
        var rig = new Rig();

        rig.Presenter.Show(["a.png", "b.png", "c.png"]);

        // WPF spawns the first window of a flash synchronously and defers the rest by i*300 ms
        // (Services/Flash/FlashService.cs:1110-1121). The flash starts the instant it comes due.
        Assert.Equal(1, rig.Presenter.LiveSurfaces);

        rig.Clock.Advance(TimeSpan.FromMilliseconds(FlashSurfacePresenter.StaggerMilliseconds));
        Assert.Equal(2, rig.Presenter.LiveSurfaces);

        rig.Clock.Advance(TimeSpan.FromMilliseconds(FlashSurfacePresenter.StaggerMilliseconds));
        Assert.Equal(3, rig.Presenter.LiveSurfaces);
        Assert.Equal(3, rig.Presenter.SurfacesShown);
        Assert.Equal(3, rig.Presences.Count);
    }

    [Fact]
    public void EachSurfaceHoldsUntilWpfsLifetimeExpires_AndThenFADESOff_RatherThanVanishing()
    {
        var rig = new Rig();
        rig.Presenter.Show(["a.png"]);

        // WPF's per-window lifetime is FlashDuration seconds plus one (FlashService.cs:1073), and
        // FlashDuration's shipped default is five seconds (AppSettings.cs:926).
        Assert.Equal(TimeSpan.FromSeconds(6), FlashSurfacePresenter.SurfaceLifetime);

        rig.Clock.Advance(FlashSurfacePresenter.SurfaceLifetime - TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, rig.Presenter.LiveSurfaces);
        Assert.Equal(0, rig.Presences[0].WithdrawCalls);

        // THE LIFETIME IS THE DEADLINE, NOT THE END. Upstream's downward ramp only STARTS at
        // ExpiresAt (FlashService.cs:2105-2106), so a flash is on screen for its lifetime plus one
        // ramp — and it is still up, still composited, one tick after the deadline.
        rig.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, rig.Presenter.LiveSurfaces);
        Assert.Equal(0, rig.Presences[0].WithdrawCalls);

        rig.Ramp(FullRamp + FlashFade.Cadence);
        Assert.Equal(0, rig.Presenter.LiveSurfaces);
        Assert.Equal(1, rig.Presences[0].WithdrawCalls);
        Assert.Equal(1, rig.Presenter.SurfacesFadedOut);
    }

    [Fact]
    public void SurfacesAreRecycledAcrossFlashes_RatherThanCreatedPerImageForever()
    {
        var rig = new Rig();

        rig.Presenter.Show(["a.png"]);
        rig.RunOutOneSurface();
        rig.Presenter.Show(["b.png"]);
        rig.RunOutOneSurface();
        rig.Presenter.Show(["c.png"]);

        // One presence, three flashes: each carries a registered window class and a top-level
        // window, and creating a pair per image per flash would churn both for a whole session.
        Assert.Single(rig.Presences);
        Assert.Equal(3, rig.Presences[0].PresentCalls);
        Assert.Equal(3, rig.Presenter.SurfacesShown);
    }

    // ---------------------------------------------------------------------------------
    //  the residuals the overlay left, answered
    // ---------------------------------------------------------------------------------

    [Fact]
    public void PresentIsCalledOncePerSurfacePerFlash_AndNeverToChangeContent()
    {
        // Residual 2: Present walks every top-level window and can issue up to 64
        // round-trips. Correct at flash cadence, wrong as a render loop. Content goes through
        // Paint, which walks nothing.
        var rig = new Rig();

        rig.Presenter.Show(["a.png"]);
        // Three cadence ticks, one second apart. They are advanced one at a time because a
        // re-arming timer re-arms from the clock's CURRENT time: jumping three seconds in one
        // step is one tick, not three, on a real clock as much as on this one.
        rig.Clock.Advance(FlashSurfacePresenter.TopmostCadence);
        rig.Clock.Advance(FlashSurfacePresenter.TopmostCadence);
        rig.Clock.Advance(FlashSurfacePresenter.TopmostCadence);

        var presence = rig.Presences[0];
        Assert.Equal(1, presence.PresentCalls);
        Assert.Equal(1, presence.PaintCalls);
        Assert.Equal(3, presence.ReassertCalls);
    }

    [Fact]
    public void IsPresentingIsNeverConsulted_ASurfaceThatLiesAboutItIsStillPresented()
    {
        // Residual 3: IsPresenting is a latch over the last operation's outcome, not a live
        // fact about the screen. A presenter that trusted it would skip the placement of a
        // recycled surface and paint into a window that is no longer showing.
        var rig = new Rig();
        rig.PresenceFactory = () =>
        {
            var presence = new RecordingPresence { AlwaysReportsPresenting = true };
            rig.Presences.Add(presence);
            return presence;
        };

        rig.Presenter.Show(["a.png"]);
        rig.RunOutOneSurface();
        rig.Presenter.Show(["b.png"]);

        Assert.Equal(2, rig.Presences[0].PresentCalls);
        Assert.Equal(2, rig.Presences[0].PaintCalls);
    }

    [Fact]
    public void TopmostIsReassertedOnWpfsCadence_OnlyWhileSomethingIsShowing()
    {
        // Residual 5 / D53: WPF re-raises every live flash window about once a second
        // (FlashService.cs:206-243) because the band is contested — measured on this machine, the
        // window that owned the point was the shipping WPF product itself. The cadence exists for
        // exactly as long as a surface does, so a stopped session leaves no timer behind.
        var rig = new Rig();
        Assert.Equal(TimeSpan.FromSeconds(1), FlashSurfacePresenter.TopmostCadence);

        rig.Presenter.Show(["a.png"]);
        rig.Clock.Advance(FlashSurfacePresenter.TopmostCadence);
        rig.Clock.Advance(FlashSurfacePresenter.TopmostCadence);
        Assert.Equal(2, rig.Presences[0].ReassertCalls);

        rig.Clock.Advance(FlashSurfacePresenter.SurfaceLifetime);
        rig.Ramp(FullRamp + FlashFade.Cadence);
        Assert.Equal(0, rig.Presenter.LiveSurfaces);
        var afterRetirement = rig.Presences[0].ReassertCalls;

        rig.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(afterRetirement, rig.Presences[0].ReassertCalls);
        Assert.Equal(0, rig.Clock.PendingCount);
    }

    // ---------------------------------------------------------------------------------
    //  stop, and the cap
    // ---------------------------------------------------------------------------------

    [Fact]
    public void HideAll_TakesEveryLiveSurfaceOff_AndCancelsTheOnesThatHadNotAppearedYet()
    {
        var rig = new Rig();
        rig.Presenter.Show(["a.png", "b.png", "c.png", "d.png"]);
        Assert.Equal(1, rig.Presenter.LiveSurfaces);

        rig.Presenter.HideAll();

        Assert.Equal(0, rig.Presenter.LiveSurfaces);
        Assert.Equal(1, rig.Presences[0].WithdrawCalls);

        // The three staggered ones must never arrive: WPF's stop closes every flash window
        // (FlashService.cs:3878-3884), and a stop that let three more pictures appear over the
        // next second would not be a stop.
        rig.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(0, rig.Presenter.LiveSurfaces);
        Assert.Equal(1, rig.Presenter.SurfacesShown);
        Assert.Equal(0, rig.Clock.PendingCount);
    }

    [Fact]
    public void TheConcurrentSurfaceCapIsWpfsTen()
    {
        var rig = new Rig();
        Assert.Equal(10, FlashSurfacePresenter.MaxConcurrentSurfaces);

        // WPF's SimultaneousImages dial goes to 20 (AppSettings.cs:832-836) while the per-flash
        // layered-window path — the one this uses — is capped at MAX_CONCURRENT_FLASH = 10
        // (FlashService.cs:50, :1174-1181). The cap wins there and it wins here.
        rig.Presenter.Show(Enumerable.Range(0, 20).Select(i => $"image-{i}.png").ToArray());
        rig.Clock.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(10, rig.Presenter.LiveSurfaces);
        Assert.Equal(10, rig.Presences.Count);
    }

    /// <summary>
    /// <b>Every image a flash is handed ends up in exactly one of three places, and none of them is
    /// nowhere.</b> It is on a surface, or it produced no pixels, or the pool was full — and the
    /// third used to be a bare <c>return</c>: no counter, no state, no reason code.
    ///
    /// <para><b>This is the maximum-settings configuration, not an edge case.</b> The
    /// images-per-flash dial goes to twenty (<c>Persistence/SessionPresetDocument.cs:56</c>) against
    /// a pool of ten, so the flash below is one a user can really ask for, and half of it has
    /// nowhere to go. <c>client/port.txt</c> names exactly that configuration as the performance
    /// contract.</para>
    ///
    /// <para>The accounting identity is asserted rather than the arithmetic: "twenty images, ten
    /// surfaces, so ten were dropped" would be the implementation's own subtraction written twice,
    /// and it would keep agreeing with itself if both halves were wrong. "No image is unaccounted
    /// for" is a property of the outcome and holds whatever the cap is.</para>
    /// </summary>
    [Fact]
    public void EveryImageOfAMaximumFlashIsAccountedFor_IncludingTheOnesTheFullPoolTurnsAway()
    {
        var rig = new Rig();
        var paths = Enumerable.Range(0, SessionPresetDocument.MaxImagesPerFlash)
            .Select(i => $"image-{i}.png")
            .ToArray();

        // Two the decoder cannot read, so the third bucket is live too and "accounted for" cannot be
        // satisfied by a presenter that simply calls everything it did not place undecodable.
        rig.Frames.Undecodable.Add(paths[0]);
        rig.Frames.Undecodable.Add(paths[1]);

        rig.Presenter.Show(paths);

        // Far enough for the whole stagger to have fired (WPF's 300 ms per image), so every image
        // has had its turn.
        rig.Clock.Advance(
            TimeSpan.FromMilliseconds((long)FlashSurfacePresenter.StaggerMilliseconds * (paths.Length - 1)));

        Assert.Equal(
            paths.Length,
            rig.Presenter.SurfacesShown + rig.Presenter.UndecodableImages
                + rig.Presenter.ImagesDroppedWhilePoolFull);
        Assert.Equal(2, rig.Presenter.UndecodableImages);
        Assert.True(
            rig.Presenter.ImagesDroppedWhilePoolFull > 0,
            $"a {paths.Length}-image flash against a pool of {FlashSurfacePresenter.MaxConcurrentSurfaces} placed "
            + $"{rig.Presenter.SurfacesShown} surfaces and reports nothing turned away, so this fact would be "
            + "proving the accounting of a case that never happened");

        // And the refusal is a TYPED outcome a caller can read, in the vocabulary the panel already
        // renders — not a silence. Degraded rather than Unavailable because the ten surfaces that
        // took the slots are on screen: "nothing was drawn" would be false in front of a user
        // watching ten flashes.
        var degraded = Assert.IsType<CapabilityState.Degraded>(rig.Presenter.LastPlacement);
        Assert.Equal(OverlayReasonCodes.OverlaySurfacePoolFull, degraded.Reason.Code);
        Assert.False(
            string.IsNullOrWhiteSpace(degraded.SurvivingSemantics),
            "a Degraded state that cannot name what survives is not degradation "
            + "(runtime-capability-contract.md §1)");
    }

    /// <summary>
    /// The control for the fact above: a flash that FITS turns nothing away and leaves the
    /// placement outcome the ordinary confirmed one. Without this, a presenter that recorded a
    /// pool-full refusal on every image would satisfy the accounting fact just as well.
    /// </summary>
    [Fact]
    public void AFlashThatFitsInThePool_TurnsNothingAway()
    {
        var rig = new Rig();
        var paths = Enumerable.Range(0, FlashSurfacePresenter.MaxConcurrentSurfaces - 1)
            .Select(i => $"image-{i}.png")
            .ToArray();

        rig.Presenter.Show(paths);
        rig.Clock.Advance(
            TimeSpan.FromMilliseconds((long)FlashSurfacePresenter.StaggerMilliseconds * (paths.Length - 1)));

        Assert.Equal(paths.Length, rig.Presenter.SurfacesShown);
        Assert.Equal(0, rig.Presenter.ImagesDroppedWhilePoolFull);
        Assert.IsType<CapabilityState.Available>(rig.Presenter.LastPlacement);
    }

    // ---------------------------------------------------------------------------------
    //  what happens when there is nothing to draw on, or nothing to draw
    // ---------------------------------------------------------------------------------

    [Fact]
    public void WithNoOverlayBackend_NothingIsShown_AndTheRefusalIsKeptVerbatimWithItsManualGate()
    {
        // Every non-Windows build. The flash must not throw, must not retry, must not pretend —
        // and the reason a caller can read must be the backend's own typed refusal, which carries
        // the route and the manual gate that would settle it (D56).
        var rig = new Rig { PresenceFactory = () => OverlayPresenceFactory.CreateFor(OverlayHostPlatform.Linux) };

        rig.Presenter.Show(["a.png", "b.png"]);
        rig.Clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(0, rig.Presenter.LiveSurfaces);
        Assert.Equal(0, rig.Presenter.SurfacesShown);
        var refusal = Assert.IsType<CapabilityState.Unavailable>(rig.Presenter.LastPresent);
        Assert.Equal(OverlayReasonCodes.OverlayMechanismAbsent, refusal.Reason.Code);
        Assert.Contains("MANUAL GATE", refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.Null(rig.Presenter.LastPaint);
    }

    [Fact]
    public void WithNoDisplayReported_NothingIsShown_AndTheReasonIsRecordedRatherThanSwallowed()
    {
        var rig = new Rig { Display = () => null };

        rig.Presenter.Show(["a.png"]);

        Assert.Equal(0, rig.Presenter.SurfacesShown);
        var refusal = Assert.IsType<CapabilityState.Unavailable>(rig.Presenter.LastPresent);
        Assert.Equal(OverlayReasonCodes.OverlayNoDisplay, refusal.Reason.Code);
    }

    [Fact]
    public void AnImageThatCannotBeDecoded_ContributesNoSurface_AndTheRestOfTheFlashStillAppears()
    {
        // WPF's own normal case: "a file is missing, corrupted, or uses an unsupported codec"
        // (FlashService.cs, LoadImagesUntilAsync). It uses whichever candidates decode and shows
        // them; it does not fail the flash.
        var rig = new Rig();
        rig.Frames.Undecodable.Add("broken.png");

        rig.Presenter.Show(["broken.png", "good.png"]);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(FlashSurfacePresenter.StaggerMilliseconds));

        Assert.Equal(1, rig.Presenter.UndecodableImages);
        Assert.Equal(1, rig.Presenter.SurfacesShown);
        Assert.Equal(1, rig.Presenter.LiveSurfaces);
    }

    [Fact]
    public void ASurfaceThatIsOnScreenButDoesNotHoldTheFrame_IsTakenBackOffImmediately()
    {
        // A window the OS confirms is on screen and that holds nothing is worse than no window: it
        // is a rectangle of nothing over the user's work, and it is the exact shape of the first
        // attempt's ghost.
        var rig = new Rig();
        rig.PresenceFactory = () =>
        {
            var presence = new RecordingPresence { PaintRefusal = OverlayReasonCodes.OverlayContentNotHeld };
            rig.Presences.Add(presence);
            return presence;
        };

        rig.Presenter.Show(["a.png"]);

        Assert.Equal(0, rig.Presenter.LiveSurfaces);
        Assert.Equal(0, rig.Presenter.SurfacesShown);
        Assert.Equal(1, rig.Presences[0].PresentCalls);
        Assert.Equal(1, rig.Presences[0].WithdrawCalls);
    }

    // ---------------------------------------------------------------------------------
    //  the geometry a user sees
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(1000, 1000, 1920, 1080, 100, 432, 432)]      // square image: the 40% HEIGHT box binds
    [InlineData(1920, 1080, 1920, 1080, 100, 768, 432)]      // monitor-aspect image: exactly 40%
    [InlineData(1920, 1080, 1920, 1080, 250, 1920, 1080)]    // the scale dial's ceiling
    [InlineData(4000, 10, 1920, 1080, 100, 768, 50)]         // a sliver: the 50px floor catches the short axis
    public void TheFlashIsSizedByWpfsFortyPercentOfMonitorRule(
        int sourceWidth, int sourceHeight, int monitorWidth, int monitorHeight,
        int scalePercent, int expectedWidth, int expectedHeight)
    {
        // WPF: base = 40% of the monitor in each axis, ratio = the aspect-preserving fit into that
        // box multiplied by ImageScale/100, then a 50px floor per axis applied AFTER the multiply
        // (FlashService.cs:2292-2301).
        Assert.Equal(
            (expectedWidth, expectedHeight),
            FlashGeometry.Size(sourceWidth, sourceHeight, monitorWidth, monitorHeight, scalePercent));
    }

    [Fact]
    public void TheFlashLandsInsideTheDisplay_NeverWithinFiftyPixelsOfAnEdge()
    {
        // WPF's SpawnEdgePadding (FlashService.cs:2320-2321): "keep targets away from screen edges
        // so they're fully visible and clickable".
        var random = new Random(20260818);
        Assert.Equal(50, FlashGeometry.EdgePadding);

        for (var i = 0; i < 500; i++)
        {
            var placed = FlashGeometry.Spawn(Display, 400, 300, random);
            Assert.InRange(placed.X, Display.X + 50, Display.X + Display.Width - 400 - 50);
            Assert.InRange(placed.Y, Display.Y + 50, Display.Y + Display.Height - 300 - 50);
        }
    }

    [Fact]
    public void APlacementRePicksAwayFromWhatIsAlreadyOnScreen_ButNeverRefusesToAppear()
    {
        // WPF re-rolls up to ten times and then accepts the overlap (FlashService.cs:1198-1207):
        // a flash that declined to appear because the screen was busy would be a worse outcome
        // than one that overlaps.
        var random = new Random(7);
        var occupied = new[] { new OverlayBounds(0, 0, 1920, 1080) };

        var placed = FlashGeometry.Place(Display, 400, 300, occupied, random);

        Assert.Equal(400, placed.Width);
        Assert.Equal(300, placed.Height);
        Assert.True(FlashGeometry.Overlaps(placed, occupied[0]));
    }

    [Fact]
    public void OverlapIsWpfsThirtyPercentOfTheNewImagesArea_NotAnyTouchAtAll()
    {
        // FlashService.cs:2539-2547. The threshold is the CANDIDATE's area, which is why a small
        // flash may sit on a large one and not the other way round.
        var candidate = new OverlayBounds(100, 100, 200, 200);   // 40 000 px², 30 % = 12 000

        Assert.False(FlashGeometry.Overlaps(candidate, new OverlayBounds(0, 0, 150, 150)));       // 50x50  = 2 500
        Assert.False(FlashGeometry.Overlaps(candidate, new OverlayBounds(0, 0, 200, 200)));       // 100x100 = 10 000
        Assert.True(FlashGeometry.Overlaps(candidate, new OverlayBounds(0, 0, 220, 220)));        // 120x120 = 14 400
        Assert.False(FlashGeometry.Overlaps(candidate, new OverlayBounds(400, 400, 100, 100)));   // disjoint
    }

    // ---------------------------------------------------------------------------------
    //  the effect end to end: the two halves, met
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task AFlashThatComesDue_HandsItsDrawnImagesToTheSurface_OnTheUiThread()
    {
        await using var rig = await EffectRig.StartAsync(imageCount: 3);
        rig.Engine.Start();

        rig.Clock.Advance(rig.LongestInterval);

        Assert.Equal(1, rig.Flash.FlashCount);
        var shown = Assert.Single(rig.Surface.Shows);
        Assert.Equal(SessionPresetDocument.DefaultImagesPerFlash, shown.Count);
        Assert.All(rig.Surface.Shows[0], path => Assert.False(string.IsNullOrEmpty(path)));
    }

    [Fact]
    public async Task TheSurfaceIsDrawnBeforeFiredIsRaised_SoAUiSubscriberCannotHoldTheFlashUp()
    {
        // The claim FlashImagesEffect.Project makes in words, held as a fact. The visible half of a
        // flash is the user's outcome and it must not be hostage to whatever a UI subscriber does,
        // so the draw goes first inside the one posted delegate. Swapping the two lines reds this.
        await using var rig = await EffectRig.StartAsync(imageCount: 2);
        var order = new List<string>();
        rig.Surface.OnShow = () => order.Add("draw");
        rig.Flash.Fired += _ => order.Add("fired");

        rig.Engine.Start();
        rig.Clock.Advance(rig.LongestInterval);

        Assert.Equal(["draw", "fired"], order);
    }

    [Fact]
    public async Task TheSurfacesTeardownGoesThroughTheDispatchBoundary_NeverDownTheShutdownThread()
    {
        // A native window belongs to the thread that made it, and StopAsync does not run there:
        // ShutdownAsync resumes on a thread-pool thread. Disposing the presenter synchronously from
        // here would take Win32OverlayPresence's wrong-thread branch, DestroyWindow would fail, and
        // the diagnostic it honestly records would be read by nobody. So the teardown is POSTED,
        // and this pins that it was — the surface's Dispose must observe that it is running inside
        // a posted delegate.
        var rig = await EffectRig.StartAsync(imageCount: 1);
        rig.Engine.Start();
        rig.Clock.Advance(rig.LongestInterval);

        await rig.Host.ShutdownAsync();

        Assert.True(rig.Surface.Disposed, "the surface was never told to tear down");
        Assert.True(rig.Surface.DisposedInsideAPost,
            "the surface was disposed straight down the shutdown thread instead of through the UI dispatch "
            + "boundary — on a real host that is the thread that cannot destroy the window");
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task StoppingTheSession_TakesTheFlashOffTheScreen()
    {
        await using var rig = await EffectRig.StartAsync(imageCount: 2);
        rig.Engine.Start();
        rig.Clock.Advance(rig.LongestInterval);
        Assert.Single(rig.Surface.Shows);

        rig.Engine.Stop();

        // WPF closes every live flash window when the engine stops (FlashService.cs:3878-3884).
        Assert.Equal(1, rig.Surface.HideAllCalls);
        Assert.Single(rig.Surface.Shows);
    }

    [Fact]
    public async Task WithARefusingOverlay_TheFlashStillComesDue_StillCounts_AndStillStops()
    {
        // The Linux path, driven on this Windows box. Every earlier fact about the schedule must
        // hold with no surface at all: a flash nobody sees is not a crash and not a refusal to run.
        var clock = new ManualClock();
        var presenter = new FlashSurfacePresenter(
            clock,
            action => action(),
            () => OverlayPresenceFactory.CreateFor(OverlayHostPlatform.Linux),
            new StubFrameSource(),
            () => Display,
            new Random(11));

        await using var rig = await EffectRig.StartAsync(imageCount: 3, clock: clock, surface: presenter);
        rig.Engine.Start();

        rig.Clock.Advance(rig.LongestInterval);
        rig.Clock.Advance(rig.LongestInterval);

        Assert.Equal(2, rig.Flash.FlashCount);
        Assert.Equal(2 * SessionPresetDocument.DefaultImagesPerFlash, rig.Pool.TotalDrawn);
        Assert.Equal(0, presenter.SurfacesShown);
        Assert.IsType<CapabilityState.Unavailable>(presenter.LastPresent);

        rig.Engine.Stop();
        Assert.False(rig.Flash.ScheduleArmed);

        for (var i = 0; i < 10; i++)
        {
            rig.Clock.Advance(rig.LongestInterval);
        }

        Assert.Equal(2, rig.Flash.FlashCount);
        Assert.Equal(0, rig.Clock.PendingCount);
    }

    // ---------------------------------------------------------------------------------
    //  the fade: the envelope, both ends of it, and what stops it
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TheFadeRunsAtUPSTREAMSOwnRate_AndAStallCannotMakeItJump()
    {
        // The port's constant IS upstream's literal. Written this way round on purpose: the
        // expectations in every fade fact below are computed from UpstreamFadePerSecond, so this
        // one line is what ties them to the shipping product rather than to the port's own choice.
        Assert.Equal(UpstreamFadePerSecond, FlashFade.RatePerSecond);

        // A step is the rate times the time that really elapsed — upstream's fadeStep
        // (FlashService.cs:2073) over its true render delta (:2050-2063), which is why the ramp is
        // 0.42 s of WALL time whether the ticks are 16 ms or 40 ms.
        Assert.Equal(0.24, FlashFade.StepFor(TimeSpan.FromMilliseconds(100)), 12);
        Assert.Equal(0.024, FlashFade.StepFor(TimeSpan.FromMilliseconds(10)), 12);

        // And upstream's stall clamp (:2060, "clamp after a stall so fades can't jump"): a machine
        // that was away for thirty seconds resumes its ramp rather than completing it in one tick.
        Assert.Equal(FlashFade.MaximumStepSeconds * UpstreamFadePerSecond,
            FlashFade.StepFor(TimeSpan.FromSeconds(30)), 12);
        Assert.Equal(FlashFade.StepFor(TimeSpan.FromMilliseconds(100)),
            FlashFade.StepFor(TimeSpan.FromSeconds(5)), 12);
    }

    [Fact]
    public void AFlashARRIVESOverUpstreamsRamp_RatherThanInASingleFrame()
    {
        // THE DEFECT THIS FACT EXISTS FOR. At the top of the size dial a flash is exactly the
        // monitor (FlashGeometry.BaseFraction 0.4 x MaxImageScalePercent 250 = 1), so a surface
        // that snaps to full alpha in the frame it appears IS "the screen turns white" — measured
        // headed at 80.65 % of the desktop in near-white. Upstream never did that: a flash window
        // is shown at opacity ZERO (FlashService.cs:1505) and ramped up by the heartbeat (:2108-2112).
        var rig = new Rig();
        rig.Presenter.Show(["a.png"]);
        var presence = rig.Presences[0];

        // The surface is PLACED at the onset, which is the smallest alpha the OS will hold — one
        // 255th, because a layered window at alpha zero is the ghost OverlaySurfaceRequest refuses.
        Assert.Equal(1.0 / 255.0, FlashFade.OnsetOpacity);
        var request = Assert.Single(presence.Requests);
        Assert.Equal(FlashFade.OnsetOpacity, request.Opacity);
        Assert.Equal((byte)1, request.Alpha);
        Assert.Empty(presence.Opacities);

        // The SHAPE, sampled tick by tick on the injected clock: opacity is the onset plus the
        // elapsed time times UPSTREAM'S rate, every step, with nothing eased and nothing skipped.
        var elapsed = TimeSpan.Zero;
        for (var tick = 1; tick <= 10; tick++)
        {
            rig.Clock.Advance(FlashFade.Cadence);
            elapsed += FlashFade.Cadence;
            Assert.Equal(tick, presence.Opacities.Count);
            Assert.Equal(
                FlashFade.OnsetOpacity + (elapsed.TotalSeconds * UpstreamFadePerSecond),
                presence.Opacities[^1], 9);
        }

        // Ten ticks in it is nowhere near full — which is the whole point: the brightness arrives
        // over time rather than in the placement frame.
        Assert.InRange(presence.Opacities[^1], 0.3, 0.45);

        // It reaches the dial and STOPS there: no overshoot, and no further alpha writes at all
        // once it has settled, so a flash holding steady costs the OS nothing.
        rig.Ramp(FullRamp);
        Assert.Equal(1.0, presence.Opacities[^1]);
        var settled = presence.Opacities.Count;
        rig.Ramp(FullRamp);
        Assert.Equal(settled, presence.Opacities.Count);
        Assert.All(presence.Opacities, o => Assert.InRange(o, FlashFade.OnsetOpacity, 1.0));

        // And not one of those writes was a re-placement. Present walks the OS's whole top-level
        // z-order and asks the window manager's hit test twice; at ramp cadence that would be a
        // full-screen window catching the user's clicks sixty times a second.
        Assert.Equal(1, presence.PresentCalls);
        Assert.Equal(1, presence.PaintCalls);
    }

    [Fact]
    public void AFlashLEAVESOverTheSameRamp_AndIsWITHDRAWNAtTheFloorRatherThanLeftAtZero()
    {
        var rig = new Rig();
        rig.Presenter.Show(["a.png"]);
        var presence = rig.Presences[0];

        // Up to the dial, then hold to the deadline. Upstream's target is the dial while the window
        // is alive and zero once it is not (FlashService.cs:2105-2106), so nothing moves in between.
        rig.Ramp(FullRamp + FlashFade.Cadence);
        Assert.Equal(1.0, presence.Opacities[^1]);
        var held = presence.Opacities.Count;

        rig.Clock.Advance(FlashSurfacePresenter.SurfaceLifetime);
        Assert.Equal(held, presence.Opacities.Count);
        Assert.Equal(1, rig.Presenter.LiveSurfaces);

        // The way DOWN is the same rate, in the same steps — one constant, both arms.
        var elapsed = TimeSpan.Zero;
        for (var tick = 1; tick <= 10; tick++)
        {
            rig.Clock.Advance(FlashFade.Cadence);
            elapsed += FlashFade.Cadence;
            Assert.Equal(held + tick, presence.Opacities.Count);
            Assert.Equal(1.0 - (elapsed.TotalSeconds * UpstreamFadePerSecond), presence.Opacities[^1], 9);
        }

        Assert.Equal(1, rig.Presenter.LiveSurfaces);
        Assert.Equal(0, presence.WithdrawCalls);

        // At the floor the surface is TAKEN OFF, not held at an invisible alpha: upstream removes
        // and closes a window whose ramp reached zero (:2117-2123), and a surface left composited
        // at nothing is the present-and-invisible state this whole capability refuses.
        rig.Ramp(FullRamp);
        Assert.Equal(0, rig.Presenter.LiveSurfaces);
        Assert.Equal(1, presence.WithdrawCalls);
        Assert.Equal(1, rig.Presenter.SurfacesFadedOut);
        Assert.All(presence.Opacities, o => Assert.True(o >= FlashFade.OnsetOpacity,
            $"the fade wrote {o}, which is below the floor the OS will hold"));
    }

    [Fact]
    public void AStopMidFadeTakesTheSurfaceDownATONCE_AndLeavesNoRampBehind()
    {
        // Upstream's Stop() unsubscribes the heartbeat and closes every live window outright
        // (FlashService.cs:372-376, CloseAllWindows at :3879-3897) — a session stopped mid-ramp
        // does not leave a half-faded rectangle finishing its fade over the user's desktop, and no
        // failure path may leave a surface on screen.
        var rig = new Rig();
        rig.Presenter.Show(["a.png"]);
        rig.Clock.Advance(FlashFade.Cadence);
        rig.Clock.Advance(FlashFade.Cadence);
        var presence = rig.Presences[0];
        Assert.InRange(presence.Opacities[^1], FlashFade.OnsetOpacity, 0.5);

        rig.Presenter.HideAll();

        Assert.Equal(0, rig.Presenter.LiveSurfaces);
        Assert.Equal(1, presence.WithdrawCalls);
        Assert.Equal(0, rig.Clock.PendingCount);

        var written = presence.Opacities.Count;
        rig.Ramp(FullRamp * 2);
        Assert.Equal(written, presence.Opacities.Count);
        Assert.Equal(1, presence.WithdrawCalls);
    }

    [Fact]
    public void AnAlphaTheOSREFUSESTakesTheSurfaceDown_RatherThanLeavingARectangleThisProcessCannotDim()
    {
        // The same rule a failed repaint already has (OverlaySurfaceSet.Repaint) and the same one
        // upstream's heartbeat has for a window that throws (FlashService.cs:2142-2146). A surface
        // the OS confirms is on screen and will not composite at the strength asked for is not a
        // fade that missed a frame.
        var rig = new Rig();
        rig.PresenceFactory = () =>
        {
            var presence = new RecordingPresence { OpacityRefusal = OverlayReasonCodes.OverlayNotComposited };
            rig.Presences.Add(presence);
            return presence;
        };

        rig.Presenter.Show(["a.png"]);
        Assert.Equal(1, rig.Presenter.LiveSurfaces);

        rig.Clock.Advance(FlashFade.Cadence);

        Assert.Equal(0, rig.Presenter.LiveSurfaces);
        Assert.Equal(1, rig.Presences[0].WithdrawCalls);
        var refusal = Assert.IsType<CapabilityState.Unavailable>(rig.Presenter.LastFade);
        Assert.Equal(OverlayReasonCodes.OverlayNotComposited, refusal.Reason.Code);

        // And it is NOT counted as a fade that completed: a surface taken down by a refusal and one
        // that reached the floor are different outcomes and the diagnostics keep them apart.
        Assert.Equal(0, rig.Presenter.SurfacesFadedOut);
    }

    // ---------------------------------------------------------------------------------
    //  rigs and doubles
    // ---------------------------------------------------------------------------------

    private sealed class Rig
    {
        private readonly Lazy<FlashSurfacePresenter> _presenter;

        public Rig()
        {
            PresenceFactory = () =>
            {
                var presence = new RecordingPresence();
                Presences.Add(presence);
                return presence;
            };

            Display = () => FlashSurfacePresenterTests.Display;
            _presenter = new Lazy<FlashSurfacePresenter>(() => new FlashSurfacePresenter(
                Clock, action => action(), () => PresenceFactory(), Frames, () => Display(), new Random(4242)));
        }

        public ManualClock Clock { get; } = new();

        public List<RecordingPresence> Presences { get; } = [];

        public StubFrameSource Frames { get; } = new();

        public Func<IOverlayPresence> PresenceFactory { get; set; }

        public Func<OverlayBounds?> Display { get; set; }

        public FlashSurfacePresenter Presenter => _presenter.Value;

        /// <summary>
        /// Steps the FADE's ramp on the injected clock, for <paramref name="span"/> of clock time,
        /// one <see cref="FlashFade.Cadence"/> at a time. Zero wall-clock.
        ///
        /// <para>One step at a time because a re-arming timer re-arms from the clock's CURRENT
        /// time: advancing a whole ramp in one jump is ONE tick, not twenty-six, on a real clock as
        /// much as on this one — and one tick is credited at most
        /// <see cref="FlashFade.MaximumStepSeconds"/>, which is upstream's own stall clamp.</para>
        /// </summary>
        public void Ramp(TimeSpan span)
        {
            for (var stepped = TimeSpan.Zero; stepped < span; stepped += FlashFade.Cadence)
            {
                Clock.Advance(FlashFade.Cadence);
            }
        }

        /// <summary>
        /// Advances past ONE surface's whole life: the lifetime, and then the fade-out ramp
        /// upstream runs BEYOND it (<c>FlashService.cs:2105-2123</c>).
        ///
        /// <para>A flash is not gone at its lifetime — that is the instant it starts leaving — so a
        /// fact that wants the pool free again has to wait out the ramp as well.</para>
        /// </summary>
        public void RunOutOneSurface()
        {
            Clock.Advance(FlashSurfacePresenter.SurfaceLifetime);
            Ramp(FullRamp + FlashFade.Cadence);
        }
    }

    /// <summary>An overlay that records what it was asked to do and never touches a screen.</summary>
    private sealed class RecordingPresence : IOverlayPresence
    {
        private OverlaySurfaceRequest? _current;

        public int PresentCalls { get; private set; }

        public int PaintCalls { get; private set; }

        public int WithdrawCalls { get; private set; }

        public int ReassertCalls { get; private set; }

        /// <summary>Makes the latch lie, which is the point of the fact that uses it.</summary>
        public bool AlwaysReportsPresenting { get; init; }

        /// <summary>When set, Paint refuses with this code.</summary>
        public string? PaintRefusal { get; init; }

        public bool IsPresenting => AlwaysReportsPresenting || _current is not null;

        /// <summary>Every placement this surface was asked for, in order — where it went, how
        /// opaque it was PLACED (which the fade makes a different number from the dial), and which
        /// way its clicks were routed.</summary>
        public List<OverlaySurfaceRequest> Requests { get; } = [];

        public CapabilityState Present(OverlaySurfaceRequest request)
        {
            PresentCalls++;
            Requests.Add(request);
            _current = request;
            return new CapabilityState.Available("recording presence: placed");
        }

        public CapabilityState Paint(OverlayFrame frame)
        {
            PaintCalls++;
            return PaintRefusal is null
                ? new CapabilityState.Available("recording presence: painted")
                : new CapabilityState.Unavailable(new CapabilityReason(PaintRefusal, "recording presence: refused"));
        }

        public void Reassert() => ReassertCalls++;

        /// <summary>Every opacity the fade has ramped this surface to, in order — the envelope as
        /// the OS seam saw it, which is the only place a test can watch it.</summary>
        public List<double> Opacities { get; } = [];

        /// <summary>When set, the alpha write refuses with this code — a layered window the OS
        /// declines to composite at the strength asked for.</summary>
        public string? OpacityRefusal { get; init; }

        public CapabilityState SetOpacity(double opacity)
        {
            Opacities.Add(opacity);
            return OpacityRefusal is null
                ? new CapabilityState.Available("recording presence: composited")
                : new CapabilityState.Unavailable(
                    new CapabilityReason(OpacityRefusal, "recording presence: refused the alpha"));
        }

        /// <summary>Never called by the presenter: click-through polarity is decided once, in the
        /// request handed to <see cref="Present"/>. A recorded call here would be a finding.</summary>
        public CapabilityState SetClickThrough(bool clickThrough)
        {
            ClickThroughFlips++;
            return new CapabilityState.Available("recording presence: flipped");
        }

        public int ClickThroughFlips { get; private set; }

        public CapabilityState Withdraw()
        {
            WithdrawCalls++;
            _current = null;
            return new CapabilityState.Available("recording presence: withdrawn");
        }

        public void Dispose() => _current = null;
    }

    /// <summary>A frame source with no decoder: it answers the size the caller asks for.</summary>
    private sealed class StubFrameSource : IFlashFrameSource
    {
        public HashSet<string> Undecodable { get; } = new(StringComparer.Ordinal);

        public OverlayFrame? Render(string path, Func<int, int, (int Width, int Height)> targetSize)
        {
            if (Undecodable.Contains(path))
            {
                return null;
            }

            var (width, height) = targetSize(800, 600);
            return OverlayFrame.Solid(width, height, 0x10, 0x20, 0x30);
        }
    }

    /// <summary>
    /// The manual clock, in the established shape: due timers fire in due order inside
    /// <see cref="Advance"/>, and a timer a callback schedules fires in the same pass when it is
    /// already due. Zero wall-clock.
    /// </summary>
    private sealed class ManualClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);

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

        private sealed class Entry
        {
            public DateTimeOffset Due { get; init; }

            public required Action Fire { get; init; }
        }

        private sealed class Handle(ManualClock clock, Entry entry) : IDisposable
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

    /// <summary>The REAL participant, engine, effect and preset, with the clock, the pool and the
    /// surface substituted — and a UI dispatch boundary bound to an inline runner, because the
    /// draw only ever happens inside the effect's one dispatch boundary.</summary>
    private sealed class EffectRig : IAsyncDisposable
    {
        private EffectRig(ApplicationHost host, SessionParticipant session, ManualClock clock,
            CountingPool pool, RecordingSurface surface, string directory)
        {
            Host = host;
            Session = session;
            Clock = clock;
            Pool = pool;
            Surface = surface;
            Directory = directory;
        }

        public ApplicationHost Host { get; }

        public SessionParticipant Session { get; }

        public ManualClock Clock { get; }

        public CountingPool Pool { get; }

        public RecordingSurface Surface { get; }

        public string Directory { get; }

        public SessionEngine Engine => Session.Engine;

        public FlashImagesEffect Flash => Session.Flash;

        public TimeSpan LongestInterval =>
            FlashSchedule.MaximumInterval(Session.Preset.Current.FlashesPerHour) + TimeSpan.FromSeconds(1);

        public static async Task<EffectRig> StartAsync(
            int imageCount, ManualClock? clock = null, IFlashSurface? surface = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), "ccp-sp100-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);

            var registry = new OperationRegistry();
            var log = new NullLog();
            var boundary = new UiDispatchBoundary();
            var dispatch = new InlineDispatch();
            boundary.Bind(dispatch);
            var infra = new ParticipantInfrastructure(registry, boundary, log);
            var manual = clock ?? new ManualClock();
            var pool = new CountingPool(imageCount);
            var recording = surface as RecordingSurface ?? new RecordingSurface(dispatch);
            var session = new SessionParticipant(infra, directory, manual, pool, surface ?? recording);
            var host = new ApplicationHost(log, [session], new StartupTrace(), registry, infra.UiDispatch);

            Assert.IsType<StartupOutcome.Success>(
                await host.StartParticipantsAsync(TestContext.Current.CancellationToken));

            return new EffectRig(host, session, manual, pool, recording, directory);
        }

        public async ValueTask DisposeAsync()
        {
            await Host.ShutdownAsync();
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
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
    /// Runs a posted delegate immediately, and RECORDS that it is inside one. The flag is what lets
    /// a fact tell "this ran through the dispatch boundary" from "this ran on whatever thread
    /// happened to call it", which on a real host is the difference between a window that can be
    /// destroyed and one that cannot.
    /// </summary>
    private sealed class InlineDispatch : IUiDispatch
    {
        public bool InsideAPost { get; private set; }

        public void Post(Action action)
        {
            var outer = InsideAPost;
            InsideAPost = true;
            try
            {
                action();
            }
            finally
            {
                InsideAPost = outer;
            }
        }
    }

    private sealed class NullLog : ILogSink
    {
        public void Log(string message)
        {
        }
    }

    private sealed class RecordingSurface(InlineDispatch dispatch) : IFlashSurface, IDisposable
    {
        public List<IReadOnlyList<string>> Shows { get; } = [];

        public int HideAllCalls { get; private set; }

        public bool Disposed { get; private set; }

        /// <summary>Whether the teardown arrived through the dispatch boundary.</summary>
        public bool DisposedInsideAPost { get; private set; }

        /// <summary>Raised before the paths are recorded, so a fact can pin the draw's ORDER
        /// against the effect's own Fired event.</summary>
        public Action? OnShow { get; set; }

        /// <summary>What this double last "placed". Settable, so a fact can drive the
        /// module panel's surface line through every state a real presenter can report.</summary>
        public CapabilityState? LastPlacement { get; set; }

        public void Show(IReadOnlyList<string> paths)
        {
            OnShow?.Invoke();
            Shows.Add(paths);
        }

        public void HideAll() => HideAllCalls++;

        public void Dispose()
        {
            Disposed = true;
            DisposedInsideAPost = dispatch.InsideAPost;
        }
    }

    private sealed class CountingPool(int population) : IFlashImagePool
    {
        private readonly string[] _images =
            Enumerable.Range(0, population).Select(i => $"image-{i}.png").ToArray();

        public int TotalDrawn { get; private set; }

        public IReadOnlyList<string> Draw(int count)
        {
            if (_images.Length == 0)
            {
                return [];
            }

            var drawn = Enumerable.Range(0, count).Select(i => _images[i % _images.Length]).ToArray();
            TotalDrawn += drawn.Length;
            return drawn;
        }
    }
}
