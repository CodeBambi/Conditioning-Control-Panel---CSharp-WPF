using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Video;

namespace CcpClient.Tests;

/// <summary>
/// The one expensive real-desktop run: the operating system's own media stack decodes a
/// synthesised clip, a real <see cref="Win32VideoPresence"/> puts its pictures on a real surface,
/// and the OS is asked back after every frame — by the product, and independently by two
/// instruments the product never uses.
///
/// <para><b>The trap this run exists to defeat.</b> "Frames decoded" is not "frames displayed". So
/// the decoded frame count is measured and REPORTED, never asserted as success, and every claim
/// that a picture reached a surface is a read-back: the OS's own rendering of the window
/// (<see cref="FlashPixelProbe.RenderWindow"/>, which is <c>PrintWindow</c> — a path the product
/// never takes), and the COMPOSITED DESKTOP where the surface is
/// (<see cref="FlashPixelProbe.CaptureDesktop"/>, <c>BitBlt(SRCCOPY|CAPTUREBLT)</c>).</para>
///
/// <para><b>The composited-desktop leg is MACHINE-CONDITIONAL and the condition is measured, not
/// assumed.</b> A foreign topmost window can own the point a surface is at — measured while this
/// packet was being written, the shipping WPF product's own window did exactly that (plan.md §0,
/// Q4) — and <c>RealDesktopCollection</c>'s own doc names that residue as one no in-process
/// mechanism can exclude. So a landed capability is used as the control: a
/// <see cref="Win32OverlayPresence"/> at a disjoint rectangle paints a known colour, and whether
/// the desktop capture can see THAT is what decides whether the subject's desktop leg is asserted.
/// Never a skip: the expectation flips with the machine and the numbers travel in the failure
/// message either way.</para>
/// </summary>
internal static class VideoSurfaceObservations
{
    /// <summary>The clip's picture size. Small on purpose: the run decodes and composes every frame
    /// on the test thread.</summary>
    internal const int ClipWidth = 320;

    internal const int ClipHeight = 240;

    /// <summary>The surface is DELIBERATELY not the clip's aspect: 400x240 against a 4:3 picture
    /// pillarboxes, so the bars the read-back's control point lives in are real rather than the
    /// three-pixel floor.</summary>
    internal const int SurfaceWidth = 400;

    internal const int SurfaceHeight = 240;

    /// <summary>The bar colour. Upstream's video window is black
    /// (<c>Services/Video/VideoService.cs:2626</c>) and so is this.</summary>
    internal const uint Letterbox = 0x000000;

    /// <summary>Three frames, each a vertical split, each different from the last. The split is what
    /// makes a mishandled bottom-up stride visible; the difference between frames is what makes a
    /// frozen picture visible.</summary>
    internal static readonly (byte B, byte G, byte R)[] TopColours =
        [(0x20, 0x30, 0xC0), (0x30, 0xC0, 0x40), (0xC0, 0x40, 0x30)];

    internal static readonly (byte B, byte G, byte R)[] BottomColours =
        [(0x90, 0x18, 0x18), (0x18, 0x90, 0x18), (0x18, 0x18, 0x90)];

    private static readonly Lazy<Measurements> Cached = new(Measure, isThreadSafe: true);

    /// <summary>The one run. Cached: it puts a window on the real desktop and decodes a real file.</summary>
    internal static Measurements Observed => Cached.Value;

    /// <summary>Where this packet's fixture media lives.</summary>
    internal static string MediaFolder =>
        Path.Combine(Path.GetTempPath(), "ccp-sp111-video", $"pid{Environment.ProcessId}");

    /// <summary>The colour a correctly oriented decode puts in the TOP half of frame
    /// <paramref name="index"/>, as a <c>COLORREF</c>.</summary>
    internal static uint ExpectedTop(int index) => ColourRef(TopColours[index]);

    /// <summary>The colour a correctly oriented decode puts in the BOTTOM half.</summary>
    internal static uint ExpectedBottom(int index) => ColourRef(BottomColours[index]);

    /// <summary>Writes the fixture clip and returns its path.</summary>
    internal static string WriteFixtureClip(string name, int frameCount = 3)
    {
        var path = Path.Combine(MediaFolder, name);
        var frames = new List<byte[]>();
        for (var i = 0; i < frameCount; i++)
        {
            frames.Add(TestAvi.VerticalSplit(
                ClipWidth, ClipHeight, TopColours[i % TopColours.Length], BottomColours[i % BottomColours.Length]));
        }

        TestAvi.Write(path, ClipWidth, ClipHeight, frames);
        return path;
    }

    /// <summary>One presented frame, as the product and both instruments saw it.</summary>
    /// <param name="ShowState">What <see cref="IVideoPresence.Show"/> returned.</param>
    /// <param name="Confirmed">The product's own read-back verdict.</param>
    /// <param name="FrameHeld">The OS's copy carries this picture over a painted bar.</param>
    /// <param name="LetterboxHeld">The bar control point came back exactly the bar colour.</param>
    /// <param name="PictureSampled">How many picture points were read.</param>
    /// <param name="PictureMatched">How many of them matched.</param>
    /// <param name="SurfaceAdvanced">The OS's copy CHANGED from the previous frame.</param>
    /// <param name="PictureIsMoving">The dot's seventh meaning, as the capability answers it.</param>
    /// <param name="RenderedMatchingComposed">How many pixels of the OS's own rendering of the window
    /// (<c>PrintWindow</c>) equal the port's own composition of this frame.</param>
    /// <param name="RenderedPixels">How many pixels that rendering returned at all — the
    /// instrument's own negative control.</param>
    /// <param name="DesktopTop">The composited desktop at the centre of the picture's top half.</param>
    /// <param name="DesktopBottom">The composited desktop at the centre of the picture's bottom half.</param>
    /// <param name="HitTestWinner">What the window manager routes the surface's centre to.</param>
    internal sealed record FrameReading(
        CapabilityState ShowState,
        bool Confirmed,
        bool FrameHeld,
        bool LetterboxHeld,
        int PictureSampled,
        int PictureMatched,
        bool SurfaceAdvanced,
        bool PictureIsMoving,
        int RenderedMatchingComposed,
        int RenderedPixels,
        uint DesktopTop,
        uint DesktopBottom,
        nint HitTestWinner);

    /// <param name="MachineHasInteractiveDesktop">The machine fact every window expectation is compared against.</param>
    /// <param name="DesktopCaptureIsLive">The machine fact every COMPOSITED expectation is compared
    /// against, measured with a landed capability at a disjoint rectangle.</param>
    /// <param name="ControlDesktopColour">What the desktop capture read where the control was.</param>
    /// <param name="OpenState">What the OS said when the clip was opened.</param>
    /// <param name="ClipInfo">What the OS reported about the clip.</param>
    /// <param name="DecodedFrames">How many pictures the DECODER produced. Reported, never asserted
    /// as success — this is the count the whole packet exists to distinguish from the displayed one.</param>
    /// <param name="DecodedTopColour">The top half of the first decoded picture, as a COLORREF.</param>
    /// <param name="DecodedBottomColour">Its bottom half. Different from the top by construction, so
    /// a decoder that ignored the OS's negative stride swaps these two.</param>
    /// <param name="PresentState">What the OS said when the surface was placed.</param>
    /// <param name="Window">The surface's handle.</param>
    /// <param name="ExStyleAfterPresent">The extended style the OS holds for it.</param>
    /// <param name="ZOrderAfterPresent">Where the OS's own z-order walk puts it.</param>
    /// <param name="SurfaceIsForeground">Whether the OS made the surface the foreground window. It
    /// must NOT be: a video that took the foreground would take it from Lock Card.</param>
    /// <param name="HeldBounds">The rectangle the OS holds for it.</param>
    /// <param name="Frames">One reading per presented frame.</param>
    /// <param name="FramesHeldCount">The capability's own count of frames the OS was confirmed to hold.</param>
    /// <param name="FramesAdvancedCount">Of those, how many changed the OS's copy.</param>
    /// <param name="StillFrameShowState">What Show returned for a picture IDENTICAL to the last one.</param>
    /// <param name="StillFrameSurfaceAdvanced">Whether the OS's copy changed for it. It must not.</param>
    /// <param name="StillFramePictureIsMoving">Whether the capability still calls that alive. It must.</param>
    /// <param name="WithdrawState">What the OS said when the surface came down.</param>
    /// <param name="VisibleAfterWithdraw">Whether the OS still reports it visible.</param>
    /// <param name="HitTestAfterWithdraw">Whether the window manager still routes its centre to it.</param>
    /// <param name="MovingAfterWithdraw">Whether the capability still claims a moving picture.</param>
    /// <param name="ShowBeforePresentCode">The refusal for a picture with no surface up.</param>
    /// <param name="ShowAfterWithdrawCode">The refusal for a picture after the surface came down.</param>
    /// <param name="ShowAfterDisposeCode">The refusal for a picture after teardown.</param>
    /// <param name="WindowGoneAfterDispose">The surface cleaned up after itself.</param>
    /// <param name="DisplayObservation">What the OS said about this process's ability to show video at all.</param>
    internal sealed record Measurements(
        bool MachineHasInteractiveDesktop,
        bool DesktopCaptureIsLive,
        uint ControlDesktopColour,
        CapabilityState OpenState,
        VideoClipInfo ClipInfo,
        int DecodedFrames,
        uint DecodedTopColour,
        uint DecodedBottomColour,
        CapabilityState PresentState,
        nint Window,
        uint ExStyleAfterPresent,
        OverlayWindowProbe.ZOrderReading ZOrderAfterPresent,
        bool SurfaceIsForeground,
        (int X, int Y, int Width, int Height) HeldBounds,
        IReadOnlyList<FrameReading> Frames,
        int FramesHeldCount,
        int FramesAdvancedCount,
        CapabilityState StillFrameShowState,
        bool StillFrameSurfaceAdvanced,
        bool StillFramePictureIsMoving,
        CapabilityState WithdrawState,
        bool VisibleAfterWithdraw,
        bool HitTestAfterWithdraw,
        bool MovingAfterWithdraw,
        string ShowBeforePresentCode,
        string ShowAfterWithdrawCode,
        string ShowAfterDisposeCode,
        bool WindowGoneAfterDispose,
        VideoDisplayObservation DisplayObservation);

    /// <summary>Where the subject surface goes: below and right of centre on the primary display,
    /// and deliberately not the rectangle the overlay or flash fixtures use — several suites run in
    /// one process and must not fight over the same point.</summary>
    internal static VideoBounds SubjectBounds
    {
        get
        {
            var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;
            return new VideoBounds(
                Math.Max(0, (screenWidth / 2) - SurfaceWidth - 30),
                Math.Max(0, (screenHeight / 2) - SurfaceHeight - 30),
                SurfaceWidth,
                SurfaceHeight);
        }
    }

    /// <summary>The control's rectangle: same display, well clear of the subject's.</summary>
    internal static OverlayBounds ControlBounds
    {
        get
        {
            var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;
            return new OverlayBounds(
                Math.Max(0, (screenWidth / 2) + 90),
                Math.Max(0, (screenHeight / 4) + 60),
                120,
                90);
        }
    }

    // ---------------------------------------------------------------------------------------
    //  THE EDGES: a surface something is COVERING, and a surface no monitor covers
    // ---------------------------------------------------------------------------------------

    private static readonly Lazy<EdgeRun> LazyEdges = new(RunEdges, isThreadSafe: true);

    /// <summary>The edge run. Cached: it puts two more real windows on the real desktop.</summary>
    internal static EdgeRun Edges => LazyEdges.Value;

    /// <param name="MachineHasInteractiveDesktop">The machine fact.</param>
    /// <param name="OccludedPresentCode">The refusal code for a surface another window is covering.</param>
    /// <param name="OccludedHitTestWinner">Who the window manager routes the point to instead.</param>
    /// <param name="OccludedIsTheThief">That winner is the covering window, not something else.</param>
    /// <param name="OffScreenShowCode">The refusal code for a surface at a rectangle no monitor covers.</param>
    /// <param name="OffScreenFrameHeld">Whether the OS's copy carried the picture there.</param>
    /// <param name="OffScreenLetterboxHeld">Whether the bar control point came back the bar colour there.</param>
    /// <param name="AudioOnlyOpenCode">The refusal code for a file with no video stream.</param>
    /// <param name="AudioOnlyOpenDetail">Its detail, which must name the missing VIDEO STREAM rather
    /// than blaming the pixel format — two different causes reach one code.</param>
    internal sealed record EdgeRun(
        bool MachineHasInteractiveDesktop,
        string OccludedPresentCode,
        nint OccludedHitTestWinner,
        bool OccludedIsTheThief,
        string OffScreenShowCode,
        bool OffScreenFrameHeld,
        bool OffScreenLetterboxHeld,
        string AudioOnlyOpenCode,
        string AudioOnlyOpenDetail);

    private static EdgeRun RunEdges()
    {
        var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;
        var source = VideoPresenceFactory.CreateClipSourceFor(VideoHostPlatform.Windows);

        // ---- a file with no video stream in it -------------------------------------------
        Directory.CreateDirectory(MediaFolder);
        var wavPath = TestWav.Write(Path.Combine(MediaFolder, "audio-only.wav"), seconds: 0.05);
        var audioOnly = source.Open(wavPath, out var audioClip);
        audioClip?.Dispose();

        // ---- a surface something is COVERING ---------------------------------------------
        var occludedBounds = new VideoBounds(
            Math.Max(0, (screenWidth / 2) + 40),
            Math.Max(0, (screenHeight / 2) - 420),
            SurfaceWidth,
            SurfaceHeight);
        var (thiefX, thiefY) = occludedBounds.Centre;

        using var occluded = new Win32VideoPresence(source);
        occluded.Present(new VideoSurfaceRequest(occludedBounds, Letterbox));

        // An OPAQUE topmost scratch window placed over the surface's own centre, raised AFTER it —
        // the exact situation the shipping WPF product produced on this machine unaided.
        using var thief = OverlayWindowProbe.ScratchWindow.Create(
            "ccp.sp111.thief", 0x00000080 | 0x08000000, thiefX - 40, thiefY - 40, 120, 120, null);
        OverlayWindowProbe.RaiseTopmost(thief.Handle);
        var occludedState = occluded.Present(new VideoSurfaceRequest(occludedBounds, Letterbox));
        var occludedObservation = occluded.LastObservation;

        // ---- a surface at a rectangle NO MONITOR COVERS ----------------------------------
        var offScreenBounds = new VideoBounds(-8000, -8000, SurfaceWidth, SurfaceHeight);
        using var offScreen = new Win32VideoPresence(source);
        offScreen.Present(new VideoSurfaceRequest(offScreenBounds, Letterbox));
        var offScreenShow = offScreen.Show(VideoFrame.Solid(ClipWidth, ClipHeight, 0x11, 0x22, 0x33));
        var offScreenObservation = offScreen.LastObservation;

        return new EdgeRun(
            MachineHasInteractiveDesktop: OverlayWindowProbe.MachineHasInteractiveDesktop,
            OccludedPresentCode: CodeOf(occludedState),
            OccludedHitTestWinner: occludedObservation.HitTestWinner,
            OccludedIsTheThief: occludedObservation.HitTestWinner == thief.Handle,
            OffScreenShowCode: CodeOf(offScreenShow),
            OffScreenFrameHeld: offScreenObservation.FrameHeld,
            OffScreenLetterboxHeld: offScreenObservation.LetterboxHeld,
            AudioOnlyOpenCode: CodeOf(audioOnly),
            AudioOnlyOpenDetail: audioOnly is CapabilityState.Unavailable u ? u.Reason.Detail : "(not a refusal)");
    }

    // ---------------------------------------------------------------------------------------
    //  TRAP 1: the overlay AND the card must both survive a video surface
    // ---------------------------------------------------------------------------------------

    private static readonly Lazy<CoexistenceRun> LazyCoexistence = new(RunCoexistence, isThreadSafe: true);

    /// <summary>The coexistence run. Cached: it puts three real windows on the real desktop.</summary>
    internal static CoexistenceRun Coexistence => LazyCoexistence.Value;

    /// <summary>The overlay's state at one moment, read entirely through
    /// <see cref="OverlayWindowProbe"/> — the overlay's own instrument, unmodified.</summary>
    /// <param name="PointPassesThrough">The window manager routes the overlay's own centre to
    /// something that is NOT the overlay.</param>
    /// <param name="AboveEveryOrdinaryWindow">The OS's z-order walk still puts it above every
    /// ordinary window. Not "above every window": a video surface is topmost too and legitimately
    /// contests the band, which is the same wording and the same reason as the overlay's own fact.</param>
    /// <param name="Alpha">The layered alpha the OS still holds, or -1 for none.</param>
    /// <param name="TransparentStyleHeld"><c>WS_EX_TRANSPARENT</c> is still set.</param>
    /// <param name="IsForeground">Whether the overlay became the foreground window.</param>
    internal readonly record struct OverlayReading(
        bool PointPassesThrough,
        bool AboveEveryOrdinaryWindow,
        int Alpha,
        bool TransparentStyleHeld,
        bool IsForeground);

    /// <summary>The card's state at one moment, read entirely through
    /// <see cref="InputWindowProbe"/> — the input capability's own instrument, unmodified.</summary>
    /// <param name="Visible">The OS reports the card visible.</param>
    /// <param name="IsForeground"><c>GetForegroundWindow()</c> is the card.</param>
    /// <param name="HoldsSystemKeyboardFocus"><c>GetGUIThreadInfo(0).hwndFocus</c> is the card — the
    /// SYSTEM read, never the thread-local one that answers for a window nobody can type into.</param>
    internal readonly record struct CardReading(bool Visible, bool IsForeground, bool HoldsSystemKeyboardFocus);

    /// <param name="MachineHasInteractiveDesktop">The machine fact every expectation is compared against.</param>
    /// <param name="OverlayPresented">The real overlay presence claimed Available before anything else existed.</param>
    /// <param name="CardTookTheInput">The real card really took the foreground and the keyboard —
    /// without this leg every reading below is a test of nothing happening.</param>
    /// <param name="OverlayBefore">The overlay before any video surface existed.</param>
    /// <param name="OverlayDuring">The overlay while a video surface was up and holding a picture.</param>
    /// <param name="OverlayAfter">The overlay after the video surface came down and was disposed.</param>
    /// <param name="CardBefore">The card before any video surface existed.</param>
    /// <param name="CardDuring">The card while the video surface was up. <b>This is the trap-1
    /// fact:</b> the video surface must not take the foreground or the keyboard from it.</param>
    /// <param name="CardAfter">The card after the video surface came down.</param>
    /// <param name="VideoEarnedAvailableBesideThem">The video surface's own Show earned Available
    /// while BOTH of the others were up — so this is coexistence rather than the video failing
    /// politely.</param>
    /// <param name="VideoShowState">That state, for failure messages.</param>
    /// <param name="OverlayCatchesItsOwnPointWhenMadeOpaque">The differential, run after the video is
    /// gone: with <c>WS_EX_TRANSPARENT</c> cleared, the same point routes TO the overlay. Without it,
    /// "the point went elsewhere" is also true of a window that is not there.</param>
    /// <param name="OverlayStillEarnsAvailable">The overlay's own <c>Present</c> still returns
    /// Available after the whole video lifecycle — the capability's own oracle, re-asked.</param>
    /// <param name="OverlayRePresentState">That state, for failure messages.</param>
    internal sealed record CoexistenceRun(
        bool MachineHasInteractiveDesktop,
        bool OverlayPresented,
        bool CardTookTheInput,
        OverlayReading OverlayBefore,
        OverlayReading OverlayDuring,
        OverlayReading OverlayAfter,
        CardReading CardBefore,
        CardReading CardDuring,
        CardReading CardAfter,
        bool VideoEarnedAvailableBesideThem,
        CapabilityState VideoShowState,
        bool OverlayCatchesItsOwnPointWhenMadeOpaque,
        bool OverlayStillEarnsAvailable,
        CapabilityState OverlayRePresentState);

    private static CoexistenceRun RunCoexistence()
    {
        var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;
        const int overlayWidth = 200;
        const int overlayHeight = 150;

        // DISJOINT from the video surface's and the card's rectangles on purpose: the overlay's
        // hit-test point must never be occluded by the things under test, or "the point went past
        // the overlay" would be measuring one of them instead of the overlay.
        var overlayBounds = new OverlayBounds(
            Math.Max(0, (screenWidth / 2) - overlayWidth - 460),
            Math.Max(0, (screenHeight / 2) - overlayHeight),
            overlayWidth,
            overlayHeight);
        var (overlayX, overlayY) = overlayBounds.Centre;

        using var overlay = new Win32OverlayPresence();
        var presented = overlay.Present(new OverlaySurfaceRequest(overlayBounds, 0.6, ClickThrough: true));
        var overlayWindow = overlay.NativeHandles.Window;

        OverlayReading ReadOverlay() => new(
            PointPassesThrough: OverlayWindowProbe.HitTest(overlayX, overlayY) != overlayWindow,
            AboveEveryOrdinaryWindow: OverlayWindowProbe.ReadZOrder(overlayWindow).AboveEveryOrdinaryWindow,
            Alpha: OverlayWindowProbe.LayeredAlphaOf(overlayWindow),
            TransparentStyleHeld: (OverlayWindowProbe.ExStyleOf(overlayWindow) & 0x00000020) != 0,
            IsForeground: OverlayWindowProbe.IsForeground(overlayWindow));

        // A real card, taking the real foreground, at a rectangle disjoint from both others.
        var cardBounds = new InputBounds(
            Math.Max(0, (screenWidth / 2) + 140),
            Math.Max(0, (screenHeight / 2) + 120),
            360,
            180);
        using var card = new Win32InputPresence();
        card.Prompt(new InputPromptRequest(
            cardBounds,
            new InputPromptContent("say this", "1 of 1", string.Empty, "Press Esc to close"),
            _ => { }));
        var cardWindow = card.NativeHandles.Window;

        // TAKEN FROM THE PROBE, not from the presence under test's sibling: sourcing "a card really
        // was up" from a capability would let one that lied turn this whole run into a test of
        // nothing happening. Same rule, and the same wording, as the input capability's own coexistence run.
        CardReading ReadCard() => new(
            Visible: InputWindowProbe.WindowIsVisible(cardWindow),
            IsForeground: InputWindowProbe.Foreground() == cardWindow,
            HoldsSystemKeyboardFocus: InputWindowProbe.SystemKeyboardFocus() == cardWindow);

        var cardTookTheInput = ReadCard() is { Visible: true, IsForeground: true, HoldsSystemKeyboardFocus: true };
        var overlayBefore = ReadOverlay();
        var cardBefore = ReadCard();

        // The subject: a real video surface, opened, given a real decoded picture, and closed.
        var path = WriteFixtureClip("coexistence.avi");
        var source = VideoPresenceFactory.CreateClipSourceFor(VideoHostPlatform.Windows);
        source.Open(path, out var clip);
        var frame = clip?.ReadFrame();

        var videoBounds = new VideoBounds(
            Math.Max(0, (screenWidth / 2) - 200),
            Math.Max(0, (screenHeight / 2) - 320),
            SurfaceWidth,
            SurfaceHeight);
        var video = new Win32VideoPresence(source);
        video.Present(new VideoSurfaceRequest(videoBounds, Letterbox));
        var show = frame is null
            ? new CapabilityState.Unavailable(new CapabilityReason("(no frame)", "nothing decoded"))
            : video.Show(frame);

        var overlayDuring = ReadOverlay();
        var cardDuring = ReadCard();

        video.Withdraw();
        video.Dispose();
        clip?.Dispose();

        // The overlay's own re-assertion, exactly as a live session would: WPF re-raises its topmost
        // windows on a cadence (Services/Flash/FlashService.cs:206-243) and the port's overlay owns
        // the same call. Asking it here is not helping the overlay pass — it is what the product
        // already does, and the z-order reading is what would fail if the video had permanently
        // taken the band.
        overlay.Reassert();
        var overlayAfter = ReadOverlay();
        var cardAfter = ReadCard();

        card.Dismiss();

        // The differential, deliberately taken on the OVERLAY rather than on a scratch window: it
        // proves this specific surface WOULD have caught the point, so "the point went elsewhere"
        // cannot be satisfied by an overlay that quietly stopped existing.
        overlay.SetClickThrough(false);
        var overlayCatchesItsOwnPoint = OverlayWindowProbe.HitTest(overlayX, overlayY) == overlayWindow;
        overlay.SetClickThrough(true);

        var rePresent = overlay.Present(new OverlaySurfaceRequest(overlayBounds, 0.6, ClickThrough: true));

        return new CoexistenceRun(
            MachineHasInteractiveDesktop: OverlayWindowProbe.MachineHasInteractiveDesktop,
            OverlayPresented: presented is CapabilityState.Available,
            CardTookTheInput: cardTookTheInput,
            OverlayBefore: overlayBefore,
            OverlayDuring: overlayDuring,
            OverlayAfter: overlayAfter,
            CardBefore: cardBefore,
            CardDuring: cardDuring,
            CardAfter: cardAfter,
            VideoEarnedAvailableBesideThem: show is CapabilityState.Available,
            VideoShowState: show,
            OverlayCatchesItsOwnPointWhenMadeOpaque: overlayCatchesItsOwnPoint,
            OverlayStillEarnsAvailable: rePresent is CapabilityState.Available,
            OverlayRePresentState: rePresent);
    }

    private static uint ColourRef((byte B, byte G, byte R) colour) =>
        (uint)((colour.B << 16) | (colour.G << 8) | colour.R);

    private static string CodeOf(CapabilityState state) =>
        state is CapabilityState.Unavailable unavailable ? unavailable.Reason.Code : "(not a refusal)";

    private static Measurements Measure()
    {
        var (captureIsLive, controlColour) = RunDesktopCaptureControl();

        var path = WriteFixtureClip("subject.avi");
        var source = VideoPresenceFactory.CreateClipSourceFor(VideoHostPlatform.Windows);
        var openState = source.Open(path, out var clip);

        var decodedFrames = new List<VideoFrame>();
        if (clip is not null)
        {
            for (var i = 0; i < TopColours.Length + 2 && !clip.Ended; i++)
            {
                var frame = clip.ReadFrame();
                if (frame is not null)
                {
                    decodedFrames.Add(frame);
                }
            }
        }

        var decodedCount = clip?.DecodedFrames ?? 0;
        var decodedTop = decodedFrames.Count > 0 ? decodedFrames[0].ColourAt(ClipWidth / 2, ClipHeight / 4) : 0u;
        var decodedBottom = decodedFrames.Count > 0
            ? decodedFrames[0].ColourAt(ClipWidth / 2, ClipHeight * 3 / 4)
            : 0u;

        var bounds = SubjectBounds;
        var request = new VideoSurfaceRequest(bounds, Letterbox);

        // The two refusals that need a presence with nothing on screen.
        var idle = new Win32VideoPresence(source);
        var showBeforePresent = idle.Show(FallbackFrame(decodedFrames));
        idle.Dispose();
        var showAfterDispose = idle.Show(FallbackFrame(decodedFrames));

        var presence = new Win32VideoPresence(source);
        var present = presence.Present(request);
        var window = presence.Observe().Window;
        var exStyle = OverlayWindowProbe.ExStyleOf(window);
        var zOrder = OverlayWindowProbe.ReadZOrder(window);
        var isForeground = OverlayWindowProbe.IsForeground(window);
        var held = OverlayWindowProbe.RectOf(window);

        var readings = new List<FrameReading>();
        foreach (var frame in decodedFrames)
        {
            var show = presence.Show(frame);
            // The answer SHOW ITSELF got, not a later one. The topmost band is contested (the
            // presence re-asserts it, MaxBandAttempts), so a fresh read taken a moment afterwards is
            // a different question about a different instant.
            var observation = presence.LastObservation;
            var moving = observation.PictureIsMoving;

            // The OS's own rendering of the window, through a call the product never makes, compared
            // against the port's own composition of the same frame.
            var expected = VideoLetterbox.Compose(
                frame, SurfaceWidth, SurfaceHeight,
                VideoLetterbox.Fit(SurfaceWidth, SurfaceHeight, frame.Width, frame.Height), Letterbox);
            var rendered = FlashPixelProbe.RenderWindow(window, SurfaceWidth, SurfaceHeight);
            var matching = CountMatching(rendered, expected);

            var box = VideoLetterbox.Fit(SurfaceWidth, SurfaceHeight, frame.Width, frame.Height);
            var desktop = FlashPixelProbe.CaptureDesktop(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            var (desktopTop, desktopBottom) = SamplePictureHalves(desktop, box);

            readings.Add(new FrameReading(
                ShowState: show,
                Confirmed: observation.Confirmed,
                FrameHeld: observation.FrameHeld,
                LetterboxHeld: observation.LetterboxHeld,
                PictureSampled: observation.PictureSampled,
                PictureMatched: observation.PictureMatched,
                SurfaceAdvanced: observation.SurfaceAdvanced,
                PictureIsMoving: moving,
                RenderedMatchingComposed: matching,
                RenderedPixels: rendered.Length,
                DesktopTop: desktopTop,
                DesktopBottom: desktopBottom,
                HitTestWinner: observation.HitTestWinner));
        }

        // The STILL control: hand over the picture that is already up. Its read-back must NOT
        // advance and the capability must still call it alive — without this clause a legitimate
        // freeze-frame would be reported as a dead surface.
        var stillShow = decodedFrames.Count > 0
            ? presence.Show(decodedFrames[^1])
            : new CapabilityState.Unavailable(new CapabilityReason("(no frame)", "nothing decoded"));
        var stillObservation = presence.LastObservation;

        var framesHeld = presence.FramesHeld;
        var framesAdvanced = presence.FramesAdvanced;

        var withdraw = presence.Withdraw();
        var visibleAfterWithdraw = OverlayWindowProbe.WindowIsVisible(window);
        var hitAfterWithdraw = window != 0
            && OverlayWindowProbe.HitTest(bounds.Centre.X, bounds.Centre.Y) == window;
        var movingAfterWithdraw = presence.PictureIsMoving;
        var showAfterWithdraw = presence.Show(FallbackFrame(decodedFrames));
        var display = presence.ObserveDisplay();

        presence.Dispose();
        var windowGone = !OverlayWindowProbe.WindowExists(window);
        clip?.Dispose();

        return new Measurements(
            MachineHasInteractiveDesktop: OverlayWindowProbe.MachineHasInteractiveDesktop,
            DesktopCaptureIsLive: captureIsLive,
            ControlDesktopColour: controlColour,
            OpenState: openState,
            ClipInfo: clip?.Info ?? VideoClipInfo.NotAsked,
            DecodedFrames: decodedCount,
            DecodedTopColour: decodedTop,
            DecodedBottomColour: decodedBottom,
            PresentState: present,
            Window: window,
            ExStyleAfterPresent: exStyle,
            ZOrderAfterPresent: zOrder,
            SurfaceIsForeground: isForeground,
            HeldBounds: held,
            Frames: readings,
            FramesHeldCount: framesHeld,
            FramesAdvancedCount: framesAdvanced,
            StillFrameShowState: stillShow,
            StillFrameSurfaceAdvanced: stillObservation.SurfaceAdvanced,
            StillFramePictureIsMoving: stillObservation.PictureIsMoving,
            WithdrawState: withdraw,
            VisibleAfterWithdraw: visibleAfterWithdraw,
            HitTestAfterWithdraw: hitAfterWithdraw,
            MovingAfterWithdraw: movingAfterWithdraw,
            ShowBeforePresentCode: CodeOf(showBeforePresent),
            ShowAfterWithdrawCode: CodeOf(showAfterWithdraw),
            ShowAfterDisposeCode: CodeOf(showAfterDispose),
            WindowGoneAfterDispose: windowGone,
            DisplayObservation: display);
    }

    /// <summary>A frame to hand a presence that must refuse it. Never the subject of a claim.</summary>
    private static VideoFrame FallbackFrame(List<VideoFrame> decoded) =>
        decoded.Count > 0 ? decoded[0] : VideoFrame.Solid(8, 8, 0x10, 0x20, 0x30);

    /// <summary>
    /// The control, and it is a LANDED capability rather than the subject: a click-through overlay
    /// at a disjoint rectangle, painted a known colour, read back out of the composited desktop. If
    /// the machine cannot show THAT to a screen read, the subject's desktop leg is not evidence and
    /// the facts say so in their own failure text rather than skipping.
    /// </summary>
    private static (bool Live, uint Colour) RunDesktopCaptureControl()
    {
        if (!OperatingSystem.IsWindows())
        {
            return (false, 0);
        }

        var bounds = ControlBounds;
        using var presence = new Win32OverlayPresence();
        var present = presence.Present(new OverlaySurfaceRequest(bounds, 1.0, ClickThrough: true));
        if (present is not CapabilityState.Available)
        {
            return (false, 0);
        }

        var frame = OverlayFrame.Solid(bounds.Width, bounds.Height, 0x7B, 0x2C, 0x4D);
        presence.Paint(frame);

        var capture = FlashPixelProbe.CaptureDesktop(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var colour = capture.Length > 0 ? capture[capture.Length / 2] : 0u;
        presence.Withdraw();
        return (colour == frame.ColourAt(bounds.Width / 2, bounds.Height / 2), colour);
    }

    private static int CountMatching(uint[] rendered, byte[] composed)
    {
        if (rendered.Length == 0)
        {
            return 0;
        }

        var matching = 0;
        for (var i = 0; i < rendered.Length && (i * VideoFrame.BytesPerPixel) + 2 < composed.Length; i++)
        {
            var offset = i * VideoFrame.BytesPerPixel;
            var expected = (uint)((composed[offset] << 16) | (composed[offset + 1] << 8) | composed[offset + 2]);
            if (rendered[i] == expected)
            {
                matching++;
            }
        }

        return matching;
    }

    /// <summary>
    /// The composited desktop at the centre of the picture's top and bottom halves. The capture is
    /// PHYSICAL and the surface's coordinates are virtual, so the sample points are scaled by the
    /// same ratio <see cref="FlashPixelProbe"/> derives from the OS.
    /// </summary>
    private static (uint Top, uint Bottom) SamplePictureHalves(uint[] capture, PictureBox box)
    {
        if (capture.Length == 0 || !box.HasArea)
        {
            return (0, 0);
        }

        var (virtualWidth, physicalWidth) = FlashPixelProbe.HorizontalResolutions;
        var (virtualHeight, physicalHeight) = FlashPixelProbe.VerticalResolutions;
        if (virtualWidth <= 0 || virtualHeight <= 0)
        {
            return (0, 0);
        }

        var scaleX = physicalWidth / (double)virtualWidth;
        var scaleY = physicalHeight / (double)virtualHeight;
        var width = Math.Max(1, (int)Math.Round(SurfaceWidth * scaleX));
        var height = Math.Max(1, (int)Math.Round(SurfaceHeight * scaleY));

        var centreX = (int)Math.Round((box.X + (box.Width / 2.0)) * scaleX);
        var topY = (int)Math.Round((box.Y + (box.Height / 4.0)) * scaleY);
        var bottomY = (int)Math.Round((box.Y + (box.Height * 3.0 / 4.0)) * scaleY);

        return (Read(capture, width, height, centreX, topY), Read(capture, width, height, centreX, bottomY));
    }

    private static uint Read(uint[] capture, int width, int height, int x, int y)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return 0;
        }

        var index = (y * width) + x;
        return index >= 0 && index < capture.Length ? capture[index] : 0;
    }
}
