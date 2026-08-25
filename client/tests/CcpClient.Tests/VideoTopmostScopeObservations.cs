using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Glyph;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Video;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>THE REPRODUCTION, AND IT SURVIVES ITS AUTHOR.</b> Three real top-level windows on the real
/// desktop — a real video surface, a real tint, a real flash — one advance of one injected clock,
/// and the OPERATING SYSTEM'S OWN Z-ORDER as the only oracle.
///
/// <para><b>The defect.</b> Every native surface this port owns re-pins itself with
/// <c>SetWindowPos(HWND_TOPMOST)</c> — the TOP of the topmost band — on its own cadence, while the
/// mandatory video window is placed topmost once and does not re-raise
/// (<c>Video/Win32VideoPresence.cs:138</c>). The last module to tick therefore wins, and since the
/// video surface is opaque (<c>Video/Win32VideoPresence.cs:130</c>) and typically fills the display,
/// the user watches a mandatory clip through a full-screen tint for its whole length.</para>
///
/// <para><b>The fix is upstream's rule AND upstream's SCOPE, and the scope is the part a run has to
/// prove.</b> WPF's below-video pin reaches exactly three window lists — pink filter, spiral,
/// brain-drain blur (<c>Services/Notifications/OverlayService.cs:2793-2801</c>) — and deliberately
/// not flash, which <c>Services/Flash/FlashService.cs:203-224</c> calls "the top attention layer by
/// design" and force-raises with no video test at all, nor bouncing text, which re-asserts a bare
/// <c>HWND_TOPMOST</c> every ~500 ms precisely because it competes with
/// "flash/video/overlay windows" (<c>Services/Subliminal/BouncingTextService.cs:390-398</c>,
/// <c>:1048-1052</c>). A port that pinned all four below the video would make every flash and every
/// bouncing logo invisible for the whole clip — nothing in
/// <c>Session/SessionParticipant.cs</c> suppresses them while a video plays.</para>
///
/// <para><b>Why the differential is the fact.</b> The tint and the flash are the SAME
/// <see cref="Win32OverlayPresence"/> class, driven through the SAME
/// <see cref="OverlaySurfaceSet"/>, over the SAME real anchor, in the SAME advance of the SAME
/// clock. The only thing that differs is which module constructed the set. So a run in which both
/// move the same way is a run in which the scoping does not exist, whichever way they moved.</para>
///
/// <para><b>The ordering is read from the OS, never from an extended style.</b> A below-video pin
/// KEEPS <c>WS_EX_TOPMOST</c> and changes only the slot inside the band, so the style read-back that
/// every other overlay fact here uses is blind to it by construction
/// (<see cref="OverlayWindowProbe.IsAbove"/> walks <c>GetTopWindow</c>/<c>GetWindow</c> instead).
/// The style IS read, once, to prove the surface was parked rather than demoted out of the band
/// altogether.</para>
///
/// <para><b>What this does NOT prove.</b> That a human saw the clip stop being tinted: composited
/// pixels depend on DWM, exclusive-fullscreen applications, Magnifier, RDP and mirror drivers, and
/// every query here can answer yes while a screen shows nothing. Sustained contention with FOREIGN
/// topmost windows, which no rule in this process can win. Multi-monitor. And every part of Linux,
/// where none of these three surfaces exists at all.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class VideoTopmostScopeObservations : RealDesktopFacts
{
    /// <summary>
    /// The one machine question this file asks, asked once per fact as a GATE and never as a key.
    /// Held as a constant so every fact refuses with identical text and none can drift into a
    /// weaker reason.
    /// </summary>
    private const string RefusalReason =
        "the below-video scope run puts a REAL Win32 video surface, a REAL layered overlay tint, a REAL flash "
        + "surface and a REAL per-pixel-alpha glyph surface on the interactive desktop and then asks USER32's own "
        + "z-order walk which one is in front of which. None of those windows exists off Windows "
        + "(VideoPresenceFactory and OverlayPresenceFactory hand back typed refusals, and the glyph backend "
        + "refuses in type), and none of them can be created in a Windows session with no desktop. The probe "
        + "folds that in and answers false for every ordering question (OverlayWindowProbe.IsAbove returns false "
        + "when either handle is absent from the walk), so every reading here would be false == false about "
        + "windows that were never created — a PASS with nothing behind it. This refuses by name instead. Linux "
        + "z-order, where the route would be _NET_WM_STATE_ABOVE and a stacking order X11 hands out, is measured "
        + "by nothing in this port and this refusal is where that shows.";

    private static readonly Lazy<ScopeRun> Cached = new(Measure, isThreadSafe: true);

    private static ScopeRun Run => Cached.Value;

    // ------------------------------------------------------------------ the anti-vacuity control

    /// <summary>
    /// <b>The vacuous case, closed first.</b> Every ordering fact below is a claim about three
    /// windows, and three windows that never reached the screen would satisfy all of them by being
    /// absent. So: the video surface really earned <see cref="CapabilityState.Available"/> from the
    /// operating system, both overlay surfaces really went up, and — the leg that makes the rest
    /// mean anything — the run really established the PRE-FIX ordering before it ticked the clock.
    /// The tint really was above the video and the flash really was below it, so neither half of the
    /// differential can pass on an order the surfaces happened to be in already.
    ///
    /// <para>And the product wiring: the anchor the yielding modules read is published by the REAL
    /// <see cref="Win32VideoPresence"/> itself, not by this test.</para>
    ///
    /// <para><b>Mutation that reds it:</b> delete the <c>VideoTopmostAnchor.Claim</c> in
    /// <c>Video/Win32VideoPresence.cs</c>. The anchor reads 0 while a video surface is on screen and
    /// this fact names it.</para>
    /// </summary>
    [Fact]
    public void AllThreeSurfacesReallyWentUp_AndTheVideoItselfPublishedTheAnchor()
    {
        Assert.SkipUnless(OverlayWindowProbe.MachineHasInteractiveDesktop, RefusalReason);

        var run = Run;

        Assert.True(run.VideoPresent is CapabilityState.Available,
            $"the video surface did not reach the screen: {Describe(run.VideoPresent)}. {run.Trace}");
        Assert.True(run.TintPlaced, $"the tint did not reach the screen: {Describe(run.TintPlacement)}. {run.Trace}");
        Assert.True(run.FlashPlaced, $"the flash did not reach the screen: {Describe(run.FlashPlacement)}. {run.Trace}");

        Assert.NotEqual(0, run.VideoWindow);
        Assert.NotEqual(0, run.TintWindow);
        Assert.NotEqual(0, run.FlashWindow);

        // THE PRODUCT PUBLISHED IT. Nothing in this file called Claim.
        Assert.Equal(run.VideoWindow, run.AnchorWhileVideoUp);

        Assert.True(run.TintStartedAboveTheVideo,
            "the tint was not put above the video before the clock ticked, so the parking fact below would prove "
            + $"nothing. {run.Trace}");
        Assert.True(run.FlashStartedBelowTheVideo,
            "the flash was not put below the video before the clock ticked, so the top-attention-layer fact below "
            + $"would prove nothing. {run.Trace}");
    }

    // ------------------------------------------------------------------ the rule

    /// <summary>
    /// <b>THE TINT PARKS UNDER THE CLIP, AND STAYS IN THE BAND.</b> One tick of the pink filter's
    /// own 5 s cadence (<c>Effects/PinkFilterSurfacePresenter.cs:80</c>, WPF's
    /// <c>OverlayService.cs:666-671</c>) moves a tint that was ABOVE the video to BELOW it, and the
    /// window still carries <c>WS_EX_TOPMOST</c> — because upstream's rule is an INSERT-AFTER, not a
    /// demotion (<c>OverlayService.cs:2870-2874</c>). A tint that had been dropped out of the
    /// topmost band would satisfy the ordering half and be a different, worse bug: a full-screen
    /// click-through window under every ordinary window on the desktop.
    ///
    /// <para><b>Mutation that reds it:</b> change <c>Win32OverlayPresence.ReassertBelowVideo</c> back
    /// to an unconditional <c>Raise()</c> — the pre-fix behaviour. The tint holds the top of the band
    /// and the message reads "a mandatory video would be playing under a full-screen tint".</para>
    /// </summary>
    [Fact]
    public void TheTintParksBelowTheVideoOnItsOwnCadence_WithoutLeavingTheTopmostBand()
    {
        Assert.SkipUnless(OverlayWindowProbe.MachineHasInteractiveDesktop, RefusalReason);

        var run = Run;

        Assert.True(run.VideoAboveTintAfterCadence,
            "the re-assertion did not park the tint under the video; a mandatory video would be playing under a "
            + $"full-screen tint for the whole clip. {run.Trace}");
        Assert.True(run.TintStillCarriesTheTopmostBit,
            "the tint was parked below the video by DROPPING it out of the topmost band rather than by inserting "
            + "it after the video, which would put a full-screen click-through window under every ordinary window "
            + $"on the desktop. {run.Trace}");
    }

    // ------------------------------------------------------------------ the scope

    /// <summary>
    /// <b>THE REGRESSION THIS REWORK EXISTS TO PREVENT.</b> In the same advance of the same clock,
    /// over the same anchor, through the same <see cref="Win32OverlayPresence"/> class, the FLASH
    /// goes the other way: it starts below the video and ends above it.
    ///
    /// <para>Upstream's <c>RaiseAllToFront</c> force-raises every live flash window with no video
    /// test at all and calls them "the top attention layer by design"
    /// (<c>Services/Flash/FlashService.cs:203-224</c>, <c>ForceTopmost</c> at <c>:3865-3877</c>);
    /// only the compositor HOST branch stands down while a video plays, and <c>:230-235</c> says why
    /// — that host is the thing <c>OverlayService.ReassertZOrder</c> already pins below the video.
    /// This port's video surface is opaque and typically fills the display, so a flash pinned under
    /// it is not "behind" the clip, it is GONE for the length of the clip.</para>
    ///
    /// <para><b>Mutation that reds it:</b> pass <c>yieldToVideo: true</c> at the
    /// <c>OverlaySurfaceSet</c> the flash presenter builds (<c>Effects/FlashSurfacePresenter.cs</c>)
    /// — which is exactly the over-broad fix this rework replaced. The flash sinks under the clip
    /// and this fact names it.</para>
    /// </summary>
    [Fact]
    public void FlashDoesNotYield_TheTopAttentionLayerStaysAboveTheVideo()
    {
        Assert.SkipUnless(OverlayWindowProbe.MachineHasInteractiveDesktop, RefusalReason);

        var run = Run;

        Assert.True(run.FlashAboveVideoAfterCadence,
            "the flash sank below the mandatory video. The video surface is opaque and typically fills the "
            + "display, and nothing suppresses flashes while a clip plays, so every flash would be invisible for "
            + $"the whole clip. {run.Trace}");

        // The differential itself, stated as one thing: the two surfaces did NOT move together.
        Assert.NotEqual(run.VideoAboveTintAfterCadence, run.VideoAboveFlashAfterCadence);
    }

    /// <summary>
    /// <b>BOUNCING TEXT DOES NOT YIELD EITHER, and it is not even wired to.</b> The glyph surface's
    /// <c>Reassert</c> is a bare <c>SetWindowPos(HWND_TOPMOST)</c> and was left exactly as it was —
    /// upstream's is too (<c>Services/Subliminal/BouncingTextService.cs:1048-1052</c>, driven every
    /// ~500 ms at <c>:390-398</c> because the module "will lose topmost when competing with
    /// flash/video/overlay windows").
    ///
    /// <para>This is asserted rather than left to the absence of a code change, because the absence
    /// of a code change is invisible to a reader and to a future edit. The anchor is REALLY
    /// published here (a live topmost window this fact owns), so a glyph surface that consulted it
    /// would park below and this fact would see it.</para>
    ///
    /// <para><b>Mutation that reds it:</b> route <c>Win32GlyphSurface.Raise</c> through
    /// <c>VideoTopmostAnchor.InsertAfter(_window)</c> — the refused branch's change, verbatim.</para>
    /// </summary>
    [Fact]
    public void BouncingTextDoesNotYieldEither_ItsReassertionIsStillTheTopOfTheBand()
    {
        Assert.SkipUnless(OverlayWindowProbe.MachineHasInteractiveDesktop, RefusalReason);

        var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;
        var x = Math.Max(0, (screenWidth / 2) + 300);
        var y = Math.Max(0, (screenHeight / 2) - 320);

        using var glyph = new Win32GlyphSurface();
        // Fully opaque: the glyph capability refuses a frame with no provable ink, because a
        // composite that reads back as nothing is indistinguishable from a window that never
        // composited (glyph-frame-carries-no-provable-ink).
        var frame = GlyphFrame.Solid(GlyphSide, GlyphSide, 0x40, 0xC0, 0xF0, 0xFF);
        var present = glyph.Present(new GlyphSurfaceRequest(new GlyphBounds(x, y, GlyphSide, GlyphSide), 1.0, ClickThrough: true), frame);
        Assert.True(present is CapabilityState.Available, $"the glyph surface did not go up: {Describe(present)}");

        var glyphWindow = glyph.NativeHandles.Window;
        using var anchor = OverlayWindowProbe.ScratchWindow.Create(
            "CcpVideoAnchorStandIn", OverlayWindowProbe.ToolwindowNoactivate, x, y, GlyphSide, GlyphSide, alpha: null);
        Assert.True(OverlayWindowProbe.WindowExists(anchor.Handle));

        try
        {
            VideoTopmostAnchor.Claim(anchor.Handle);

            // The stand-in takes the top of the band, so "the glyph is above it" cannot be an order
            // it was already in.
            OverlayWindowProbe.RaiseTopmost(anchor.Handle);
            Assert.True(OverlayWindowProbe.IsAbove(anchor.Handle, glyphWindow),
                "the stand-in did not get above the glyph surface, so this fact would prove nothing");

            glyph.Reassert();

            Assert.True(OverlayWindowProbe.IsAbove(glyphWindow, anchor.Handle),
                "bouncing text yielded to the video anchor. Upstream's bouncing text re-asserts a bare "
                + "HWND_TOPMOST every ~500 ms and is deliberately outside the below-video rule, so a logo that "
                + "parks under an opaque full-display clip is gone for the whole clip");
        }
        finally
        {
            VideoTopmostAnchor.Release(anchor.Handle);
        }
    }

    // ------------------------------------------------------------------ the two edges

    /// <summary>
    /// <b>A CLIP THAT STARTS UNDER A TINT THAT IS ALREADY UP.</b> The tint's own kick is every 5 s,
    /// which would leave a clip playing under it for up to five seconds at every video start.
    /// Upstream does not wait: its video rule is resolved BEFORE its "did the band get lost" test
    /// (<c>OverlayService.cs:2851-2853</c> ahead of <c>:2856</c>), so its 500 ms reconcile pass
    /// re-pins a HELD band while a video plays. This drives that arm and nothing else: the clock is
    /// advanced by exactly one <see cref="OverlaySurfaceSet.ReconcileCadence"/>, which is far short
    /// of the 5 s cadence.
    ///
    /// <para>The band is deliberately NOT lost here — the tint is freshly presented and holds
    /// <c>WS_EX_TOPMOST</c> — so the old loop would have done nothing at all on this tick.</para>
    ///
    /// <para><b>Mutation that reds it:</b> delete the <c>else if (videoUp)</c> arm in
    /// <c>Effects/OverlaySurfaceSet.cs</c>'s reconcile tick. The tint stays over the clip until its
    /// 5 s kick, and this fact reads it there.</para>
    /// </summary>
    [Fact]
    public void AClipStartingUnderALiveTint_IsInFrontWithinOneReconcileTick()
    {
        Assert.SkipUnless(OverlayWindowProbe.MachineHasInteractiveDesktop, RefusalReason);

        var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;
        var rect = new OverlayBounds(
            Math.Max(0, (screenWidth / 2) + 300), Math.Max(0, (screenHeight / 2) + 40), TintSide, TintSide);

        var clock = new ManualClock();
        Win32OverlayPresence? presence = null;
        using var tint = new PinkFilterSurfacePresenter(
            clock, action => action(), () => presence = new Win32OverlayPresence(), () => rect);

        var engaged = tint.Engage(new PinkFilterTint(0xFF, 0x60, 0xB0, 50));
        Assert.True(engaged is CapabilityState.Available, $"the tint did not go up: {Describe(engaged)}");
        var tintWindow = presence!.NativeHandles.Window;

        using var anchor = OverlayWindowProbe.ScratchWindow.Create(
            "CcpVideoAnchorStandIn", OverlayWindowProbe.ToolwindowNoactivate,
            rect.X, rect.Y, rect.Width, rect.Height, alpha: null);
        Assert.True(OverlayWindowProbe.WindowExists(anchor.Handle));

        try
        {
            // The tint is up and holding the band; THEN the clip starts.
            OverlayWindowProbe.RaiseTopmost(tintWindow);
            Assert.True(OverlayWindowProbe.IsAbove(tintWindow, anchor.Handle),
                "the tint did not start above the stand-in, so this fact would prove nothing");
            Assert.True(OverlaySurfaceSet.TopmostHeldByOs(presence),
                "the band was already lost, so this would drive the LOSS arm rather than the video arm");

            VideoTopmostAnchor.Claim(anchor.Handle);
            clock.Advance(OverlaySurfaceSet.ReconcileCadence);

            Assert.True(OverlayWindowProbe.IsAbove(anchor.Handle, tintWindow),
                "a clip that started under a tint already on screen was still under it one reconcile tick later; "
                + "upstream has it in front within 500 ms and this port would have made the user wait out a 5 s "
                + "cadence period");
        }
        finally
        {
            VideoTopmostAnchor.Release(anchor.Handle);
        }
    }

    /// <summary>
    /// <b>THE CLIP ENDS AND THE TINT GETS THE TOP OF THE BAND BACK.</b> Withdrawing the video
    /// releases the anchor (<c>Video/Win32VideoPresence.cs</c>, before the hide, so nothing can pin
    /// itself below a window on its way off the screen), and the yielding module's next cadence tick
    /// resolves to <c>HWND_TOPMOST</c> again — measured as the tint climbing back over the FLASH,
    /// which had been above it while the clip played.
    ///
    /// <para>Deliberately measured against the flash rather than against the video: a hidden window
    /// is not in the z-order walk at all, so "above the video" after a withdrawal is a question with
    /// no content.</para>
    ///
    /// <para><b>Mutation that reds it:</b> make <c>VideoTopmostAnchor.Release</c> a no-op. The claim
    /// outlives the clip, the tint keeps resolving to a hidden anchor, and it never comes back
    /// up.</para>
    /// </summary>
    [Fact]
    public void AWithdrawnVideoReleasesTheAnchor_AndTheTintClimbsBack()
    {
        Assert.SkipUnless(OverlayWindowProbe.MachineHasInteractiveDesktop, RefusalReason);

        var run = Run;

        Assert.True(run.VideoWithdraw is CapabilityState.Available,
            $"the video surface did not come down: {Describe(run.VideoWithdraw)}. {run.Trace}");
        Assert.Equal(0, run.AnchorAfterWithdraw);

        // While the clip played the flash was above the tint (flash took the top of the band; the
        // tint parked under the video, which was under the flash). After the release, one more
        // cadence puts the tint back on top of it.
        Assert.True(run.FlashAboveTintWhileVideoWasUp,
            $"the flash was not above the tint while the clip played, so the flip below is not one. {run.Trace}");
        Assert.True(run.TintAboveFlashAfterRelease,
            "a released anchor did not hand the top of the band back: the tint is still parked where a clip that "
            + $"has finished playing used to be. {run.Trace}");
    }

    /// <summary>
    /// <b>ONLY THE WINDOW THAT HOLDS THE CLAIM CAN CLEAR IT.</b> Two video surfaces exist in this
    /// port's lifetime — Mandatory Video and Bubble Count share one presence
    /// (<c>Session/SessionParticipant.cs:474-492</c>) — and a presence torn down after a newer one
    /// claimed must not unpin the live clip. That is a compare-and-swap, not a store.
    ///
    /// <para>This fact publishes a claim, so it lives here in the serialized real-desktop collection
    /// rather than beside the pure decision facts in <see cref="VideoTopmostAnchorTests"/>: the
    /// claim is process-global and nothing in another collection may observe it half-written.</para>
    ///
    /// <para><b>Mutation that reds it:</b> make <c>Release</c> an unconditional
    /// <c>Volatile.Write(ref _anchor, 0)</c>. The stale release clears the live claim and every
    /// yielding module takes the top of the band back over a clip that is still playing.</para>
    /// </summary>
    [Fact]
    public void AStaleReleaseCannotUnpinTheLiveClaim()
    {
        const nint older = 0x0DD1;
        const nint newer = 0x0DD2;

        var restore = VideoTopmostAnchor.Current;
        try
        {
            VideoTopmostAnchor.Claim(older);
            Assert.Equal(older, VideoTopmostAnchor.Current);
            Assert.True(VideoTopmostAnchor.IsClaimed);

            VideoTopmostAnchor.Claim(newer);
            VideoTopmostAnchor.Release(older);
            Assert.Equal(newer, VideoTopmostAnchor.Current);

            VideoTopmostAnchor.Release(newer);
            Assert.Equal(0, VideoTopmostAnchor.Current);
            Assert.False(VideoTopmostAnchor.IsClaimed);

            // And a release of nothing is not a release of everything.
            VideoTopmostAnchor.Claim(newer);
            VideoTopmostAnchor.Release(0);
            Assert.Equal(newer, VideoTopmostAnchor.Current);
        }
        finally
        {
            VideoTopmostAnchor.Claim(restore);
        }
    }

    // ------------------------------------------------------------------ the run

    private const int GlyphSide = 160;

    private const int TintSide = 200;

    private static string Describe(CapabilityState? state) => state switch
    {
        CapabilityState.Available available => $"Available({available.Detail})",
        CapabilityState.Degraded degraded => $"Degraded({degraded.Reason.Code})",
        CapabilityState.Unavailable unavailable =>
            $"Unavailable({unavailable.Reason.Code}: {unavailable.Reason.Detail})",
        _ => "(nothing was attempted)",
    };

    /// <summary>
    /// One rig, three real windows, one clock. The video goes up first, then the tint over it, then
    /// the flash over that; the pre-fix ordering is then forced deliberately (flash at the bottom,
    /// video, tint on top) so that BOTH halves of the differential have somewhere to move from. One
    /// <see cref="PinkFilterSurfacePresenter.TopmostCadence"/> advance brings the flash's 1 s kick
    /// and the tint's 5 s kick due together, and the OS is asked what it did with them.
    /// </summary>
    private static ScopeRun Measure()
    {
        if (!OverlayWindowProbe.MachineHasInteractiveDesktop)
        {
            return ScopeRun.Absent;
        }

        var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;

        // Deliberately overlapping, and deliberately clear of the fixed rectangles the other
        // real-desktop rigs in this collection use: the failure being reproduced IS a tint over a
        // clip, so the two rectangles are the same one.
        var x = Math.Max(0, (screenWidth / 2) + 260);
        var y = Math.Max(0, (screenHeight / 2) - 40);
        const int width = 380;
        const int height = 260;

        var clock = new ManualClock();
        var clips = VideoPresenceFactory.CreateClipSourceFor(VideoHostPlatform.Windows);

        using var video = new Win32VideoPresence(clips);
        var videoPresent = video.Present(new VideoSurfaceRequest(new VideoBounds(x, y, width, height)));
        var videoWindow = video.LastObservation.Window;
        var anchorWhileVideoUp = VideoTopmostAnchor.Current;

        Win32OverlayPresence? tintPresence = null;
        Win32OverlayPresence? flashPresence = null;

        using var tint = new PinkFilterSurfacePresenter(
            clock, action => action(), () => tintPresence = new Win32OverlayPresence(),
            () => new OverlayBounds(x, y, width, height));
        var tintPlacement = tint.Engage(new PinkFilterTint(0xFF, 0x60, 0xB0, 50));

        // The Visuals row's own MAXIMUM flash duration (VisualsPresetDocument.MaxFlashDurationSeconds),
        // so one surface outlives the whole ten injected seconds this run spends. At the shipped
        // default the flash retires at t=6 s — mid-run — and "the tint climbed back over the flash"
        // would then read false because a hidden window is not in the z-order walk at all. Nothing
        // else about the flash path changes: it is a setting a user can pick.
        using var flash = new FlashSurfacePresenter(
            clock, action => action(), () => flashPresence = new Win32OverlayPresence(),
            new SolidFrames(), () => new OverlayBounds(x, y, width, height), new Random(4242),
            () => new FlashDraw(
                FlashSurfacePresenter.ImageScalePercent,
                FlashSurfacePresenter.OpacityPercent,
                VisualsPresetDocument.MaxFlashDurationSeconds));
        flash.Show(["one.png"]);
        var flashPlacement = flash.LastPlacement;

        var tintWindow = tintPresence?.NativeHandles.Window ?? 0;
        var flashWindow = flashPresence?.NativeHandles.Window ?? 0;

        // THE PRE-FIX ORDERING, forced with the probe's own declarations so neither half of the
        // differential can pass on an order the surfaces were already in.
        OverlayWindowProbe.RaiseTopmost(flashWindow);
        OverlayWindowProbe.RaiseTopmost(videoWindow);
        OverlayWindowProbe.RaiseTopmost(tintWindow);
        var tintStartedAbove = OverlayWindowProbe.IsAbove(tintWindow, videoWindow);
        var flashStartedBelow = OverlayWindowProbe.IsAbove(videoWindow, flashWindow);

        // ONE advance. The flash's 1 s cadence and the tint's 5 s cadence both come due in it, in
        // that order, so both modules act on the same anchor in the same pass.
        clock.Advance(PinkFilterSurfacePresenter.TopmostCadence);

        var videoAboveTint = OverlayWindowProbe.IsAbove(videoWindow, tintWindow);
        var videoAboveFlash = OverlayWindowProbe.IsAbove(videoWindow, flashWindow);
        var flashAboveVideo = OverlayWindowProbe.IsAbove(flashWindow, videoWindow);
        var flashAboveTint = OverlayWindowProbe.IsAbove(flashWindow, tintWindow);
        var tintStillTopmost =
            (OverlayWindowProbe.ExStyleOf(tintWindow) & OverlayWindowProbe.TopmostBit) != 0;

        // The clip ends.
        var withdraw = video.Withdraw();
        var anchorAfterWithdraw = VideoTopmostAnchor.Current;
        clock.Advance(PinkFilterSurfacePresenter.TopmostCadence);
        var tintAboveFlashAfterRelease = OverlayWindowProbe.IsAbove(tintWindow, flashWindow);

        var trace =
            $"video 0x{videoWindow:X}, tint 0x{tintWindow:X}, flash 0x{flashWindow:X} at "
            + $"({x},{y},{width}x{height}); before the tick tint>video={tintStartedAbove}, "
            + $"video>flash={flashStartedBelow}; after it video>tint={videoAboveTint}, "
            + $"flash>video={flashAboveVideo}, flash>tint={flashAboveTint}, "
            + $"tint keeps WS_EX_TOPMOST={tintStillTopmost}; after the withdrawal anchor="
            + $"0x{anchorAfterWithdraw:X}, tint>flash={tintAboveFlashAfterRelease}";

        return new ScopeRun(
            MachineHasInteractiveDesktop: true,
            VideoPresent: videoPresent,
            VideoWindow: videoWindow,
            AnchorWhileVideoUp: anchorWhileVideoUp,
            TintPlacement: tintPlacement,
            TintWindow: tintWindow,
            FlashPlacement: flashPlacement,
            FlashWindow: flashWindow,
            TintStartedAboveTheVideo: tintStartedAbove,
            FlashStartedBelowTheVideo: flashStartedBelow,
            VideoAboveTintAfterCadence: videoAboveTint,
            VideoAboveFlashAfterCadence: videoAboveFlash,
            FlashAboveVideoAfterCadence: flashAboveVideo,
            FlashAboveTintWhileVideoWasUp: flashAboveTint,
            TintStillCarriesTheTopmostBit: tintStillTopmost,
            VideoWithdraw: withdraw,
            AnchorAfterWithdraw: anchorAfterWithdraw,
            TintAboveFlashAfterRelease: tintAboveFlashAfterRelease,
            Trace: trace);
    }

    /// <param name="AnchorWhileVideoUp">Read straight after the video's own Present. This test never
    /// calls Claim; if this is 0 the product is not publishing.</param>
    /// <param name="TintStillCarriesTheTopmostBit">The band, not the slot: an insert-after keeps it,
    /// a demotion does not.</param>
    private sealed record ScopeRun(
        bool MachineHasInteractiveDesktop,
        CapabilityState? VideoPresent,
        nint VideoWindow,
        nint AnchorWhileVideoUp,
        CapabilityState? TintPlacement,
        nint TintWindow,
        CapabilityState? FlashPlacement,
        nint FlashWindow,
        bool TintStartedAboveTheVideo,
        bool FlashStartedBelowTheVideo,
        bool VideoAboveTintAfterCadence,
        bool VideoAboveFlashAfterCadence,
        bool FlashAboveVideoAfterCadence,
        bool FlashAboveTintWhileVideoWasUp,
        bool TintStillCarriesTheTopmostBit,
        CapabilityState? VideoWithdraw,
        nint AnchorAfterWithdraw,
        bool TintAboveFlashAfterRelease,
        string Trace)
    {
        internal static ScopeRun Absent { get; } = new(
            false, null, 0, 0, null, 0, null, 0,
            false, false, false, false, false, false, false, null, 0, false,
            "no interactive desktop: nothing was placed");

        internal bool TintPlaced => TintPlacement is CapabilityState.Available;

        internal bool FlashPlaced => FlashPlacement is CapabilityState.Available;
    }

    /// <summary>A frame source with no decoder: the flash's pixels are irrelevant to a z-order
    /// fact, and a real image file would put a codec inside one.</summary>
    private sealed class SolidFrames : IFlashFrameSource
    {
        public OverlayFrame? Render(string path, Func<int, int, (int Width, int Height)> targetSize)
        {
            var (width, height) = targetSize(240, 180);
            return OverlayFrame.Solid(width, height, 0x20, 0x80, 0x20);
        }
    }

    /// <summary>The manual clock this project's module facts share. Zero wall-clock: the five
    /// seconds above are five seconds of an injected clock and this run takes microseconds of
    /// them.</summary>
    private sealed class ManualClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);

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
}
