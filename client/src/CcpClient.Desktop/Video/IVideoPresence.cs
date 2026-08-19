using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Video;

/// <summary>A rectangle on the desktop in PHYSICAL pixels — a Win32 top-level window's own
/// coordinate space, for the same reason <c>Overlay.OverlayBounds</c> and <c>Input.InputBounds</c>
/// are physical.</summary>
public readonly record struct VideoBounds(int X, int Y, int Width, int Height)
{
    /// <summary>The centre point, which is where the hit test is asked.</summary>
    public (int X, int Y) Centre => (X + (Width / 2), Y + (Height / 2));

    public override string ToString() => $"{X},{Y} {Width}x{Height}";
}

/// <summary>
/// Where a video surface goes and what colour the bars around the picture are.
/// </summary>
/// <param name="Bounds">Where the surface goes, in physical pixels.</param>
/// <param name="Letterbox">The bar colour, as a <c>COLORREF</c> (<c>0x00BBGGRR</c>). Upstream's own
/// video window is <c>Background = Brushes.Black</c>
/// (<c>Services/Video/VideoService.cs:2626</c>), and black is this type's default for that reason.
/// <b>It is also the read-back's own control</b>: a point in the bar must come back EXACTLY this
/// colour, which is what proves the fill happened at all.</param>
public sealed record VideoSurfaceRequest(VideoBounds Bounds, uint Letterbox = 0x000000)
{
    public VideoBounds Bounds { get; } = Validate(Bounds);

    private static VideoBounds Validate(VideoBounds bounds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bounds.Width, 0, nameof(bounds));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bounds.Height, 0, nameof(bounds));
        return bounds;
    }
}

/// <summary>
/// What the operating system says about this process's ability to put a video surface in front of a
/// human AT ALL — the necessary condition, asked before anything is shown.
///
/// <para>Separate from <see cref="VideoSurfaceObservation"/> for the same reason
/// <c>Input.InputStationObservation</c> is separate from its capture record: "this process could
/// show a picture" and "this process is showing one and the OS holds its pixels" are different
/// facts with different lifetimes, and a module's dot needs the first every time it repaints while
/// the second only exists while a clip is playing.</para>
/// </summary>
/// <param name="Asked">The read-back was attempted. False on a build with no mechanism — the Linux
/// case — and never dressed up as a zero.</param>
/// <param name="WindowStationVisible">The process's window station carries <c>WSF_VISIBLE</c>
/// (<c>GetUserObjectInformation(UOI_FLAGS)</c>). A service in session 0 does not, and every window
/// it creates is invisible to every human.</param>
/// <param name="DisplayCount">The OS's own monitor count (<c>SM_CMONITORS</c>).</param>
/// <param name="CompositionEnabled"><c>DwmIsCompositionEnabled</c>. The desktop compositor is what
/// puts a window's pixels on a screen; measured <c>True</c> on the machine SP-111 was written on.
/// <b>Kept because it is a documented BOOL out-parameter with no struct behind it</b> — unlike
/// <c>DwmGetCompositionTimingInfo</c>, whose frame counters this capability deliberately does NOT
/// read (plan.md §0, Q4 finding 3: the shipping DLL accepts a 292-byte struct where the SDK header
/// declares 320, and the counters are system-wide anyway).</param>
/// <param name="MediaStackUsable">The OS's media stack started (<c>MFStartup</c> succeeded), so
/// something on this machine can be decoded.</param>
public sealed record VideoDisplayObservation(
    bool Asked,
    bool WindowStationVisible,
    int DisplayCount,
    bool CompositionEnabled,
    bool MediaStackUsable)
{
    /// <summary>Nothing was asked, because this build has no way to ask.</summary>
    public static VideoDisplayObservation NotAsked { get; } = new(false, false, 0, false, false);

    /// <summary>The OS says a video surface this process creates could be put in front of somebody
    /// AND that this machine can decode something to put on it. It does NOT say any frame reached a
    /// surface — only <see cref="VideoSurfaceObservation"/> can.</summary>
    public bool Confirmed =>
        Asked && WindowStationVisible && DisplayCount >= 1 && CompositionEnabled && MediaStackUsable;
}

/// <summary>
/// What the operating system says about the video surface RIGHT NOW. Every field is read back from
/// the platform; not one is remembered from a call this process made.
///
/// <para><b>The central distinction this record exists to hold open.</b> "Frames decoded" is not
/// "frames displayed". A decoder that returns bytes proves a file is readable — the same grade of
/// evidence as a tray method returning, an overlay flag being set, or <c>Play()</c> returning. So
/// nothing here counts decoded buffers. <see cref="PictureMatched"/> is the number of points at
/// which the OPERATING SYSTEM'S OWN COPY of the surface carries the decoded picture's pixels, and
/// <see cref="SurfaceAdvanced"/> is whether that copy CHANGED when a different picture was handed
/// over.</para>
///
/// <para><b><see cref="LetterboxHeld"/> is not optional and its absence is a measured failure
/// mode.</b> SP-110's mutation sweep found that deleting the paint call entirely survived a
/// read-back that merely counted pixels differing from the background — because the window class
/// registers no background brush, so an UNPAINTED window's device context holds whatever the OS left
/// in it. A control point in a bar the painter FILLS and never draws picture into must read back
/// EXACTLY the requested colour, and only then does a matching picture pixel mean anything.</para>
/// </summary>
/// <param name="Asked">The read-back was attempted at all.</param>
/// <param name="Window">The hwnd asked about; 0 when there is none.</param>
/// <param name="WindowExists">The OS recognises the handle as a window.</param>
/// <param name="WindowVisible">The OS reports it visible.</param>
/// <param name="HeldBounds">The rectangle the OS holds for it.</param>
/// <param name="AboveEveryOrdinaryWindow">The OS's own top-level z-order walk puts it before the
/// first visible non-topmost window. The ORDERING, never the <c>WS_EX_TOPMOST</c> flag.</param>
/// <param name="HitTestWinner">What the window manager routes the surface's centre point to. This is
/// the occlusion question, and it BIT while SP-111 was being written: the shipping WPF product's own
/// topmost window owned the point (plan.md §0, Q4).</param>
/// <param name="LetterboxHeld">See the remarks above.</param>
/// <param name="PictureSampled">How many points inside the picture band were read back. The read's
/// own negative control, so a read-back that degenerated into "sampled nothing" is visible rather
/// than reported as a perfect match.</param>
/// <param name="PictureMatched">How many of them carried EXACTLY the decoded picture's colour.</param>
/// <param name="SurfaceSample">A fold of the colours read back off the surface.</param>
/// <param name="PreviousSurfaceSample">The same fold from the previous presented frame, or 0 before
/// there was one.</param>
/// <param name="FrameDiffersFromPrevious">Whether the picture HANDED OVER differs from the one
/// handed over last time. Needed because a video may legitimately hold a still: without it, "the
/// surface did not change" could not be told apart from "the surface is dead".</param>
/// <param name="FramesPresented">How many frames this presence has handed to the OS for this
/// surface. A count of PRESENTS, not of anything displayed, and named so nothing can confuse the
/// two.</param>
public sealed record VideoSurfaceObservation(
    bool Asked,
    nint Window,
    bool WindowExists,
    bool WindowVisible,
    VideoBounds HeldBounds,
    bool AboveEveryOrdinaryWindow,
    nint HitTestWinner,
    bool LetterboxHeld,
    int PictureSampled,
    int PictureMatched,
    uint SurfaceSample,
    uint PreviousSurfaceSample,
    bool FrameDiffersFromPrevious,
    int FramesPresented)
{
    /// <summary>Nothing was asked, because this build has no way to ask.</summary>
    public static VideoSurfaceObservation NotAsked { get; } =
        new(false, 0, false, false, default, false, 0, false, 0, 0, 0, 0, false, 0);

    /// <summary>The window manager routes the surface's centre to the surface. The non-zero test is
    /// not redundant: without it an observation about NO window reports that a hit test routes to
    /// it, because <c>0 == 0</c>.</summary>
    public bool HitTestRoutesHere => Window != 0 && HitTestWinner == Window;

    /// <summary>
    /// <b>The operating system's own copy of this surface carries the decoded picture.</b> A
    /// differential: the painted bar is really there AND every sampled point of the picture band is
    /// exactly the pixel the decoder produced. A blit that never happened fails the second clause;
    /// a paint that never happened fails the first.
    /// </summary>
    public bool FrameHeld => Asked && LetterboxHeld && PictureSampled > 0 && PictureMatched == PictureSampled;

    /// <summary>The OS's copy of the surface CHANGED since the previous presented frame.</summary>
    public bool SurfaceAdvanced => Asked && SurfaceSample != PreviousSurfaceSample;

    /// <summary>
    /// <b>The picture is MOVING — the dot's seventh meaning.</b> The frame is held, and where a
    /// DIFFERENT picture was handed over the OS's copy really changed. A still frame handed over
    /// twice is legitimately not "advancing" in the pixel sense and is still alive, which is why the
    /// second clause is a disjunction and not a bare requirement that the surface differ.
    ///
    /// <para><b>Why this is not "the decoder is fed".</b> Upstream's own worst failure is a window
    /// that stays white while the decoder runs and <c>MediaEnded</c> never fires
    /// (<c>Services/Video/VideoService.cs:2677-2678</c>), and a frozen final frame after a
    /// suspend (<c>:1394-1397</c>) — in both, every call this process makes still succeeds. Upstream
    /// watches the same thing from the other side (<c>_voutLostSinceTicks</c> <c>:104</c>,
    /// <c>_frameReady</c> <c>:3788</c>, <c>StartBlurFrameWatchdog</c> <c>:3265</c>).</para>
    /// </summary>
    public bool PictureIsMoving => FrameHeld && (!FrameDiffersFromPrevious || SurfaceAdvanced);

    /// <summary>
    /// The OS holds this process's picture on a surface a user could be looking at. This — and only
    /// this — is what <see cref="CapabilityState.Available"/> is allowed to rest on.
    /// </summary>
    public bool Confirmed =>
        Asked && WindowExists && WindowVisible && AboveEveryOrdinaryWindow && HitTestRoutesHere && FrameHeld;
}

/// <summary>
/// A video presence: this process's ability to put a MOVING PICTURE in front of the user — and, the
/// part that makes it a capability rather than a wrapper, its ability to ASK THE OPERATING SYSTEM
/// whether the picture is really there and really changing.
///
/// <para><b>The trap this interface exists to close.</b> "Frames decoded" is not "frames displayed".
/// A decoder that returns bytes proves a file is readable, and this port has rejected that grade of
/// evidence four times under other names: a tray method returning, an overlay flag being set,
/// <c>Play()</c> returning, and a fake dial re-imposing its own clamp under test. So
/// <see cref="CapabilityState.Available"/> here is downstream of five things the OS said — the
/// window exists and is visible, it holds the requested rectangle, the OS's own z-order walk puts it
/// above every ordinary window, the window manager routes a point inside it TO it (nothing is
/// covering it), and <b>the OS's own copy of the surface carries the decoded picture over a bar that
/// reads back exactly the colour the painter filled it with</b>.</para>
///
/// <para><b>What Available still cannot mean.</b> That a human WATCHED anything — that is
/// <c>watched-verified</c>, a named manual gate, and no automated step on any platform discharges
/// it. That the frames arrived in time, in order or at the right rate: nothing here measures
/// cadence. That there was sound: this capability has none (see
/// <c>Effects.MandatoryVideoEffect</c>). And that the composited desktop carried it — a window's own
/// device context is what the OS holds FOR the window, and a foreign topmost window can own the same
/// point on screen; that stronger leg is the test harness's, it is machine-conditional, and it is
/// measured rather than assumed (SP-111 record §5).</para>
///
/// <para><b>Thread affinity.</b> A native window belongs to the thread that created it. Call
/// <see cref="Present"/>/<see cref="Show"/>/<see cref="Withdraw"/>/<see cref="IDisposable.Dispose"/>
/// on one thread — the surface thread in the app.</para>
/// </summary>
public interface IVideoPresence : IDisposable
{
    /// <summary>
    /// Put the surface up at the requested rectangle (or move the one that is already up) and fill
    /// it with the letterbox colour. <see cref="CapabilityState.Available"/> means the OS confirmed
    /// the window is on screen, at those bounds, above every ordinary window, that the window
    /// manager routes a point inside it to it, and that the bar it was told to paint is really
    /// there.
    ///
    /// <para>It deliberately makes no claim about a PICTURE: there is not one yet. That is
    /// <see cref="Show"/>'s.</para>
    /// </summary>
    CapabilityState Present(VideoSurfaceRequest request);

    /// <summary>
    /// Put one decoded picture on the surface, letterboxed to its own aspect, and then ASK THE OS
    /// what the surface holds. <see cref="CapabilityState.Available"/> means the OS's own copy
    /// carries that picture. It NEVER means the blit returned.
    ///
    /// <para>Touches no style, walks no z-order at placement cost and asks for no activation: those
    /// are <see cref="Present"/>'s and are right once per placement, not once per frame. It DOES
    /// re-ask the hit test and the z-order, cheaply, because occlusion is the one window fact that
    /// changes under a video without this process doing anything.</para>
    /// </summary>
    CapabilityState Show(VideoFrame frame);

    /// <summary>
    /// Take the surface off the screen, keeping the window for the next <see cref="Present"/>.
    /// <see cref="CapabilityState.Available"/> means the OS confirmed it is no longer visible and
    /// the window manager no longer routes its centre to it.
    /// </summary>
    CapabilityState Withdraw();

    /// <summary>Ask the OS what it believes about the surface, RIGHT NOW. A live round trip; never
    /// throws, and a build with no mechanism answers <see cref="VideoSurfaceObservation.NotAsked"/>
    /// rather than a shaped zero.</summary>
    VideoSurfaceObservation Observe();

    /// <summary>Ask the OS whether this process could put a video surface in front of anybody at
    /// all, and whether this machine can decode anything to put on it.</summary>
    VideoDisplayObservation ObserveDisplay();

    /// <summary>
    /// A live, cheap read: is the OS holding a picture on this surface that is really moving? This
    /// is what a row's dot is entitled to call "a video is playing". Deliberately NOT a remembered
    /// answer — a compositor can stop, and a decoder can wedge, with this process doing nothing at
    /// all.
    /// </summary>
    bool PictureIsMoving { get; }

    /// <summary>
    /// A live, cheap read of the necessary condition: the OS says this process has an interactive
    /// window station, at least one display, a running compositor and a usable media stack. False on
    /// a build with no mechanism, which is what stops a module's dot from lighting where no picture
    /// can ever be shown.
    /// </summary>
    bool CanReachADisplay { get; }

    /// <summary>True only while a surface this presence put up is confirmed on screen.</summary>
    bool IsPresenting { get; }

    /// <summary>The last state <see cref="Show"/> produced, or null before it was ever called. What
    /// a panel renders, so a user reads the OS's own answer rather than a sentence about a
    /// platform.</summary>
    CapabilityState? LastShow { get; }

    /// <summary>The most recent read-back this presence actually took. <b>A remembered ANSWER, never
    /// a remembered REQUEST.</b></summary>
    VideoSurfaceObservation LastObservation { get; }

    /// <summary>
    /// How many presented frames the OS was afterwards confirmed to be HOLDING. The number this
    /// capability exists to be able to report, and it is deliberately not the same number as
    /// <c>IVideoClip.DecodedFrames</c> — a run where those two diverge is the failure the whole
    /// packet is about.
    /// </summary>
    int FramesHeld { get; }

    /// <summary>Of those, how many CHANGED the OS's copy of the surface. Zero over a clip whose
    /// pictures differ is a frozen picture, whatever the decoder reports.</summary>
    int FramesAdvanced { get; }
}
