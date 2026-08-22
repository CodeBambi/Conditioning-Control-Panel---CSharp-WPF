using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Glyph;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Pointer;
using CcpClient.Desktop.Video;

namespace CcpClient.Tests;

/// <summary>
/// This capability's real-desktop runs. Three of them, each executed ONCE per suite and cached, in the shape
/// <see cref="OverlayObservations"/> and <see cref="VideoSurfaceObservations"/> established: a real
/// window on the real desktop is expensive and machine-global, so it is created once and every fact
/// reads the recorded run.
///
/// <list type="number">
/// <item><b>Lifecycle</b> — one glyph surface presented, painted, moved, withdrawn and disposed,
/// read at every step through <see cref="GlyphWindowProbe"/>.</item>
/// <item><b>Differential</b> — THE PACKET'S CENTRAL EVIDENCE. The surface composited over a KNOWN
/// background supplied by the landed overlay capability, read back off the COMPOSITED DESKTOP, with
/// a control capture taken with the surface hidden and an occlusion arbitration deciding who owns
/// each sample point.</item>
/// <item><b>Coexistence</b> — the four surfaces that landed before this one, measured through their
/// OWN instruments while a glyph surface is up beside them.</item>
/// </list>
/// </summary>
internal static class GlyphSurfaceObservations
{
    /// <summary>The composited surface's side. Big enough that a quadrant is unambiguous at any
    /// DPI mapping, small enough not to cover the machine.</summary>
    internal const int SurfaceSide = 200;

    /// <summary>The backdrop's size. Deliberately LARGER than the surface on every side, so a
    /// margin of pure background exists inside the same capture — that margin is what proves the
    /// capture is live and the background really reached the screen.</summary>
    internal const int BackdropWidth = 400;

    internal const int BackdropHeight = 300;

    /// <summary>
    /// The background colour, as a <c>COLORREF</c> (<c>0x00BBGGRR</c>): B=200, G=40, R=10.
    ///
    /// <para><b>Chosen so that no channel is 0 and none is 255.</b> A background with a zero channel
    /// could not distinguish "the surface composited black here" from "the background shows
    /// through", which is the exact distinction this run exists to make.</para>
    /// </summary>
    internal const uint BackdropColour = 0x00C8280A;

    /// <summary>The opaque ink colour: magenta, B=255 G=0 R=255.</summary>
    internal const uint InkColour = 0x00FF00FF;

    /// <summary>
    /// How many times the differential re-raises its pair before giving up on owning the sampled
    /// region. A bounded COUNT, never a wall-clock wait - the same ceiling and the same reason as
    /// <c>Win32GlyphSurface.MaxRaiseAttempts</c>: the topmost band is contested on this machine by
    /// the shipping WPF product, which re-asserts it on a cadence
    /// (<c>Services/Flash/FlashService.cs:206-243</c>).
    /// </summary>
    internal const int ArbitrationAttempts = 32;

    private static readonly Lazy<LifecycleRun> LazyLifecycle =
        new(RunLifecycle, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<DifferentialRun> LazyDifferential =
        new(RunDifferential, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<CoexistenceRun> LazyCoexistence =
        new(RunCoexistence, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<GlyphWindowProbe.NegativeControl> LazyControl =
        new(GlyphWindowProbe.RunNegativeControl, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<UniformModeRun> LazyUniformMode =
        new(RunUniformMode, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static LifecycleRun Lifecycle => LazyLifecycle.Value;

    internal static DifferentialRun Differential => LazyDifferential.Value;

    internal static CoexistenceRun Coexistence => LazyCoexistence.Value;

    /// <summary>The instrument's own negative control, re-run on every suite execution.</summary>
    internal static GlyphWindowProbe.NegativeControl Control => LazyControl.Value;

    /// <summary>The staged uniform-alpha hazard: a real surface, poisoned mid-life.</summary>
    internal static UniformModeRun UniformMode => LazyUniformMode.Value;

    internal static string Describe(CapabilityState? state) => state switch
    {
        null => "(nothing was attempted)",
        CapabilityState.Available available => $"Available({available.Detail})",
        CapabilityState.Unavailable unavailable =>
            $"Unavailable({unavailable.Reason.Code}: {unavailable.Reason.Detail})",
        CapabilityState.Degraded degraded => $"Degraded({degraded.Reason.Code}: {degraded.Reason.Detail})",
        _ => state.ToString() ?? "(unprintable)",
    };

    /// <summary>
    /// The four-quadrant test frame. Every quadrant answers a different question and the four
    /// together are the packet's central trap in one bitmap:
    /// <list type="bullet">
    /// <item>top-left FULLY TRANSPARENT — must show the background behind it;</item>
    /// <item>top-right OPAQUE BLACK — must NOT show the background, which is what separates it from
    /// the transparent quadrant;</item>
    /// <item>bottom-left OPAQUE MAGENTA — a glyph pixel, distinguished from the background;</item>
    /// <item>bottom-right HALF-ALPHA WHITE — the blend, which is what makes the alpha per-PIXEL
    /// rather than uniform.</item>
    /// </list>
    /// </summary>
    internal static GlyphFrame Quadrants(int side)
    {
        var pixels = new byte[side * side * GlyphFrame.BytesPerPixel];
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var offset = ((y * side) + x) * GlyphFrame.BytesPerPixel;
                var right = x >= side / 2;
                var bottom = y >= side / 2;

                (byte B, byte G, byte R, byte A) pixel = (right, bottom) switch
                {
                    (false, false) => (0, 0, 0, 0),
                    (true, false) => (0, 0, 0, 255),
                    (false, true) => (255, 0, 255, 255),
                    (true, true) => (128, 128, 128, 128),
                };

                pixels[offset] = pixel.B;
                pixels[offset + 1] = pixel.G;
                pixels[offset + 2] = pixel.R;
                pixels[offset + 3] = pixel.A;
            }
        }

        return new GlyphFrame(side, side, pixels);
    }

    /// <summary>The five sample points inside the BACKDROP's coordinate space, in the order the
    /// facts read them: the pure-background margin, then one point in each quadrant.</summary>
    internal static (int X, int Y)[] SamplePoints(int offsetX, int offsetY, int side) =>
    [
        (8, 8),
        (offsetX + (side / 4), offsetY + (side / 4)),
        (offsetX + (3 * side / 4), offsetY + (side / 4)),
        (offsetX + (side / 4), offsetY + (3 * side / 4)),
        (offsetX + (3 * side / 4), offsetY + (3 * side / 4)),
    ];

    // ---------------------------------------------------------------- lifecycle

    /// <param name="MachineHasInteractiveDesktop">Whether there was anywhere for a surface to go.</param>
    /// <param name="PresentState">What the capability said when asked to composite.</param>
    /// <param name="Window">The handle, for the probe.</param>
    /// <param name="ExistsAfterPresent">The OS recognises it.</param>
    /// <param name="VisibleAfterPresent">The OS reports it visible.</param>
    /// <param name="RectAfterPresent">The rectangle the OS holds.</param>
    /// <param name="RequestedRect">The rectangle that was asked for.</param>
    /// <param name="ExStyleAfterPresent">The extended-style read-back.</param>
    /// <param name="UniformAlphaAfterPresent">-1 is the CORRECT answer: a per-pixel surface holds no
    /// uniform alpha, and asserting the -1 is what pins that this is not the overlay's mechanism.</param>
    /// <param name="ZOrderAfterPresent">Where the OS puts it.</param>
    /// <param name="ForegroundAfterPresent">Whether it stole the foreground.</param>
    /// <param name="PointPassesThroughWhileClickThrough">The hit test in the requested polarity.</param>
    /// <param name="CatchesItsOwnPointWhenOpaque">The hit test in the other one — the differential.</param>
    /// <param name="SurfaceNonZeroPixels">Non-zero pixels the OS returns for the surface.</param>
    /// <param name="SurfaceSampledPixels">How many were read.</param>
    /// <param name="InkMatchesAfterPresent">Ink points that read back their exact colour.</param>
    /// <param name="InkPoints">How many ink points the frame carries.</param>
    /// <param name="TransparentReadsZero">Whether the transparent quadrant reads back as nothing.</param>
    /// <param name="PaintState">A second, different frame composited onto the live surface.</param>
    /// <param name="SecondInkMatches">Ink points of the SECOND frame that read back exactly.</param>
    /// <param name="SecondInkPoints">How many the second frame carries.</param>
    /// <param name="SurfaceChangedBetweenFrames">Whether the OS's copy actually CHANGED.</param>
    /// <param name="MoveState">What a move said.</param>
    /// <param name="RectAfterMove">Where the OS put it.</param>
    /// <param name="RequestedRectAfterMove">Where it was asked to go.</param>
    /// <param name="ContentSurvivedTheMove">Whether the composite is still there afterwards.</param>
    /// <param name="ResizingMoveState">A move that also resizes must be refused.</param>
    /// <param name="InklessPaintState">An all-transparent frame must be refused.</param>
    /// <param name="MismatchedFrameState">A wrong-sized frame must be refused.</param>
    /// <param name="WithdrawState">What withdrawal said.</param>
    /// <param name="VisibleAfterWithdraw">The OS no longer reports it visible.</param>
    /// <param name="PointRoutesAwayAfterWithdraw">The hit test no longer routes to it.</param>
    /// <param name="ContentSurvivedTheWithdraw">The composite is kept for the next Present.</param>
    /// <param name="PaintAfterWithdrawState">Painting a withdrawn surface must be refused.</param>
    /// <param name="ExistsAfterDispose">No top-level window is left behind.</param>
    /// <param name="TeardownDiagnostic">Null after a clean teardown.</param>
    internal sealed record LifecycleRun(
        bool MachineHasInteractiveDesktop,
        CapabilityState PresentState,
        nint Window,
        bool ExistsAfterPresent,
        bool VisibleAfterPresent,
        (int X, int Y, int Width, int Height) RectAfterPresent,
        (int X, int Y, int Width, int Height) RequestedRect,
        uint ExStyleAfterPresent,
        int UniformAlphaAfterPresent,
        GlyphWindowProbe.ZOrderReading ZOrderAfterPresent,
        bool ForegroundAfterPresent,
        bool PointPassesThroughWhileClickThrough,
        bool CatchesItsOwnPointWhenOpaque,
        int SurfaceNonZeroPixels,
        int SurfaceSampledPixels,
        int InkMatchesAfterPresent,
        int InkPoints,
        bool TransparentReadsZero,
        CapabilityState PaintState,
        int SecondInkMatches,
        int SecondInkPoints,
        bool SurfaceChangedBetweenFrames,
        CapabilityState MoveState,
        (int X, int Y, int Width, int Height) RectAfterMove,
        (int X, int Y, int Width, int Height) RequestedRectAfterMove,
        bool ContentSurvivedTheMove,
        CapabilityState ResizingMoveState,
        CapabilityState InklessPaintState,
        CapabilityState MismatchedFrameState,
        CapabilityState WithdrawState,
        bool VisibleAfterWithdraw,
        bool PointRoutesAwayAfterWithdraw,
        bool ContentSurvivedTheWithdraw,
        CapabilityState PaintAfterWithdrawState,
        bool ExistsAfterDispose,
        string? TeardownDiagnostic)
    {
        /// <summary>The OS holds exactly the rectangle that was asked for. As ONE boolean so a fact
        /// can assert it at statement depth 0 against
        /// <see cref="MachineHasInteractiveDesktop"/> rather than returning early on a machine with
        /// no desktop.</summary>
        internal bool RectMatchesRequest => RectAfterPresent == RequestedRect;

        /// <summary>Same, for the rectangle after a move.</summary>
        internal bool MoveRectMatches => RectAfterMove == RequestedRectAfterMove;

        /// <summary>The extended-style read-back carries every bit that was written.</summary>
        internal bool ExStyleCarriesEveryBit =>
            (ExStyleAfterPresent & 0x08080008u) == 0x08080008u
            && (ExStyleAfterPresent & 0x000000A0u) == 0x000000A0u;

        /// <summary>The OS's own copy of the surface carries the frame: something was sampled, some
        /// of it is non-zero, and every opaque ink point is exactly its own colour.</summary>
        internal bool SurfaceCarriesTheFrame =>
            SurfaceSampledPixels > 0 && SurfaceNonZeroPixels > 0 && InkPoints > 0
            && InkMatchesAfterPresent == InkPoints;

        /// <summary>A DIFFERENT frame was composited onto the live surface, every one of ITS ink
        /// points reads back exactly, and the OS's bytes really changed.</summary>
        internal bool SecondFrameHeldAndChanged =>
            SecondInkPoints > 0 && SecondInkMatches == SecondInkPoints && SurfaceChangedBetweenFrames;

        /// <summary>The Present claim's own words, or empty when nothing was claimed.</summary>
        internal string PresentDetail =>
            PresentState is CapabilityState.Available available ? available.Detail : string.Empty;

        /// <summary>The move claim's own words, or empty.</summary>
        internal string MoveDetail =>
            MoveState is CapabilityState.Available available ? available.Detail : string.Empty;
    }

    private static LifecycleRun RunLifecycle()
    {
        var (screenWidth, screenHeight) = GlyphWindowProbe.PrimarySize;
        var bounds = new GlyphBounds(
            Math.Max(0, (screenWidth / 2) - 620),
            Math.Max(0, (screenHeight / 2) + 60),
            SurfaceSide,
            SurfaceSide);

        var frame = Quadrants(SurfaceSide);
        var second = GlyphFrame.Solid(SurfaceSide, SurfaceSide, 0x10, 0xE0, 0x40, 0xFF);

        var surface = new Win32GlyphSurface();
        var request = new GlyphSurfaceRequest(bounds, 1.0, ClickThrough: true);
        var presentState = surface.Present(request, frame);
        var window = surface.NativeHandles.Window;

        var existsAfterPresent = GlyphWindowProbe.WindowExists(window);
        var visibleAfterPresent = GlyphWindowProbe.WindowIsVisible(window);
        var rectAfterPresent = GlyphWindowProbe.RectOf(window);
        var exStyle = GlyphWindowProbe.ExStyleOf(window);
        var uniformAlpha = GlyphWindowProbe.LayeredAlphaOf(window);
        var zOrder = GlyphWindowProbe.ReadZOrder(window);
        var foreground = GlyphWindowProbe.IsForeground(window);

        var (centreX, centreY) = bounds.Centre;
        GlyphWindowProbe.HitTestExpecting(centreX, centreY, window, expectSurface: false, out _);
        var passesThrough = GlyphWindowProbe.HitTest(centreX, centreY) != window;

        // The differential leg, taken HERE and not in the record's argument list: the arguments are
        // evaluated after Dispose, and a destroyed window wins no hit test. The first draft made
        // exactly that mistake and the fact caught it.
        var catchesItsOwnPoint = CatchesOwnPoint(window, centreX, centreY);

        var firstSurface = GlyphWindowProbe.ReadSurface(window, SurfaceSide, SurfaceSide);
        var nonZero = GlyphWindowProbe.NonZero(firstSurface);
        var inkMatches = CountInkMatches(firstSurface, frame);
        var transparentReadsZero = firstSurface.Length == SurfaceSide * SurfaceSide
            && firstSurface[((SurfaceSide / 4) * SurfaceSide) + (SurfaceSide / 4)] == 0;

        var paintState = surface.Paint(second);
        var secondSurface = GlyphWindowProbe.ReadSurface(window, SurfaceSide, SurfaceSide);
        var secondInkMatches = CountInkMatches(secondSurface, second);
        var changed = firstSurface.Length > 0 && secondSurface.Length == firstSurface.Length
            && !firstSurface.AsSpan().SequenceEqual(secondSurface);

        var moved = new GlyphBounds(bounds.X + 24, bounds.Y + 16, bounds.Width, bounds.Height);
        var moveState = surface.MoveTo(moved);
        var rectAfterMove = GlyphWindowProbe.RectOf(window);
        var afterMoveSurface = GlyphWindowProbe.ReadSurface(window, SurfaceSide, SurfaceSide);
        var contentSurvivedMove = CountInkMatches(afterMoveSurface, second) == second.ProvableInk.Count
            && second.ProvableInk.Count > 0;

        var resizingMoveState = surface.MoveTo(new GlyphBounds(moved.X, moved.Y, moved.Width + 10, moved.Height));
        var inklessPaintState = surface.Paint(GlyphFrame.Solid(SurfaceSide, SurfaceSide, 255, 255, 255, 0));
        var mismatchedFrameState = surface.Paint(GlyphFrame.Solid(SurfaceSide - 4, SurfaceSide, 10, 20, 30, 255));

        var withdrawState = surface.Withdraw();
        var visibleAfterWithdraw = GlyphWindowProbe.WindowIsVisible(window);
        var (movedCentreX, movedCentreY) = moved.Centre;
        var routesAway = GlyphWindowProbe.HitTest(movedCentreX, movedCentreY) != window;
        var withdrawnSurface = GlyphWindowProbe.ReadSurface(window, SurfaceSide, SurfaceSide);
        var contentSurvivedWithdraw = CountInkMatches(withdrawnSurface, second) == second.ProvableInk.Count
            && second.ProvableInk.Count > 0;
        var paintAfterWithdrawState = surface.Paint(second);

        surface.Dispose();
        var existsAfterDispose = GlyphWindowProbe.WindowExists(window);

        return new LifecycleRun(
            MachineHasInteractiveDesktop: GlyphWindowProbe.MachineHasInteractiveDesktop,
            PresentState: presentState,
            Window: window,
            ExistsAfterPresent: existsAfterPresent,
            VisibleAfterPresent: visibleAfterPresent,
            RectAfterPresent: rectAfterPresent,
            RequestedRect: (bounds.X, bounds.Y, bounds.Width, bounds.Height),
            ExStyleAfterPresent: exStyle,
            UniformAlphaAfterPresent: uniformAlpha,
            ZOrderAfterPresent: zOrder,
            ForegroundAfterPresent: foreground,
            PointPassesThroughWhileClickThrough: passesThrough,
            CatchesItsOwnPointWhenOpaque: catchesItsOwnPoint,
            SurfaceNonZeroPixels: nonZero,
            SurfaceSampledPixels: firstSurface.Length,
            InkMatchesAfterPresent: inkMatches,
            InkPoints: frame.ProvableInk.Count,
            TransparentReadsZero: transparentReadsZero,
            PaintState: paintState,
            SecondInkMatches: secondInkMatches,
            SecondInkPoints: second.ProvableInk.Count,
            SurfaceChangedBetweenFrames: changed,
            MoveState: moveState,
            RectAfterMove: rectAfterMove,
            RequestedRectAfterMove: (moved.X, moved.Y, moved.Width, moved.Height),
            ContentSurvivedTheMove: contentSurvivedMove,
            ResizingMoveState: resizingMoveState,
            InklessPaintState: inklessPaintState,
            MismatchedFrameState: mismatchedFrameState,
            WithdrawState: withdrawState,
            VisibleAfterWithdraw: visibleAfterWithdraw,
            PointRoutesAwayAfterWithdraw: routesAway,
            ContentSurvivedTheWithdraw: contentSurvivedWithdraw,
            PaintAfterWithdrawState: paintAfterWithdrawState,
            ExistsAfterDispose: existsAfterDispose,
            TeardownDiagnostic: surface.TeardownDiagnostic);
    }

    /// <summary>
    /// The differential leg of the input fact, taken with the probe's OWN style write so the
    /// capability is never asked to certify itself: clear <c>WS_EX_TRANSPARENT</c>, ask the window
    /// manager, restore it. Without this leg "the point went elsewhere" would be equally true of a
    /// surface that was never created.
    /// </summary>
    private static bool CatchesOwnPoint(nint surfaceWindow, int x, int y)
    {
        if (!GlyphWindowProbe.WindowExists(surfaceWindow))
        {
            return false;
        }

        var style = GlyphWindowProbe.ExStyleOf(surfaceWindow);
        GlyphWindowProbe.SetExStyle(surfaceWindow, style & ~0x00000020u);
        var winner = GlyphWindowProbe.HitTestExpecting(x, y, surfaceWindow, expectSurface: true, out _);
        GlyphWindowProbe.SetExStyle(surfaceWindow, style);
        return winner == surfaceWindow;
    }

    private static int CountInkMatches(uint[] surface, GlyphFrame frame)
    {
        if (surface.Length != frame.Width * frame.Height)
        {
            return -1;
        }

        var matches = 0;
        foreach (var (x, y) in frame.ProvableInk)
        {
            if (surface[(y * frame.Width) + x] == frame.PremultipliedColourAt(x, y))
            {
                matches++;
            }
        }

        return matches;
    }

    // ---------------------------------------------------------------- uniform-alpha mode

    /// <param name="MachineHasInteractiveDesktop">Whether there was anywhere for a surface to go.</param>
    /// <param name="FirstPresent">The surface really composited before it was poisoned.</param>
    /// <param name="PoisonApplied">The probe's own SetLayeredWindowAttributes returned TRUE.</param>
    /// <param name="UniformAlphaAfterPoison">What the OS now holds for it. 200 when poisoned.</param>
    /// <param name="RePresentAfterPoison">What the capability says when asked to composite again.</param>
    /// <param name="PaintAfterPoison">And what a content-only call says.</param>
    internal sealed record UniformModeRun(
        bool MachineHasInteractiveDesktop,
        CapabilityState FirstPresent,
        bool PoisonApplied,
        int UniformAlphaAfterPoison,
        CapabilityState RePresentAfterPoison,
        CapabilityState PaintAfterPoison);

    /// <summary>
    /// Stages THIS PACKET'S CENTRAL HAZARD on a real surface and asks the capability what it says.
    ///
    /// <para>A dedicated, throwaway surface, because the poison is permanent: once a window holds
    /// uniform layered attributes, <c>UpdateLayeredWindow</c> refuses it forever (measured, err 87).
    /// The surface is disposed immediately afterwards and nothing else in this file uses it.</para>
    ///
    /// <para><b>Why it is worth staging rather than classing as unreachable.</b> The
    /// <c>glyph-uniform-alpha-mode</c> refusal is the product's own guard for the exact conversion
    /// the overlay recorded, and a first draft of this packet classified it as a branch nothing could
    /// reach. It is reachable with instruments this suite already shipped: the surface exposes its
    /// own handle, and the probe already declares the call.</para>
    /// </summary>
    private static UniformModeRun RunUniformMode()
    {
        var (screenWidth, screenHeight) = GlyphWindowProbe.PrimarySize;
        var bounds = new GlyphBounds(
            Math.Max(0, (screenWidth / 2) - 900),
            Math.Max(0, (screenHeight / 2) + 60),
            SurfaceSide,
            SurfaceSide);

        var frame = Quadrants(SurfaceSide);
        using var surface = new Win32GlyphSurface();
        var request = new GlyphSurfaceRequest(bounds, 1.0, ClickThrough: true);

        var first = surface.Present(request, frame);
        var window = surface.NativeHandles.Window;

        // The conversion, applied from OUTSIDE the capability with the probe's own declaration -
        // the capability itself has no such call to make, which is the point.
        var poisoned = GlyphWindowProbe.PoisonWithUniformAlpha(window, 200);
        var alphaAfter = GlyphWindowProbe.LayeredAlphaOf(window);

        var rePresent = surface.Present(request, frame);
        var paint = surface.Paint(GlyphFrame.Solid(SurfaceSide, SurfaceSide, 0x10, 0xE0, 0x40, 0xFF));

        return new UniformModeRun(
            MachineHasInteractiveDesktop: GlyphWindowProbe.MachineHasInteractiveDesktop,
            FirstPresent: first,
            PoisonApplied: poisoned,
            UniformAlphaAfterPoison: alphaAfter,
            RePresentAfterPoison: rePresent,
            PaintAfterPoison: paint);
    }

    // ---------------------------------------------------------------- differential

    /// <param name="MachineHasInteractiveDesktop">Whether there was anywhere for a surface to go.</param>
    /// <param name="BackdropPresented">The landed overlay really put the known background up.</param>
    /// <param name="BackdropPainted">And really holds the known colour, by its own read-back.</param>
    /// <param name="BackdropState">That state, for failure messages.</param>
    /// <param name="GlyphPresented">The glyph surface really earned Available over it.</param>
    /// <param name="GlyphState">That state, for failure messages.</param>
    /// <param name="Intruders">
    /// THE ARBITRATION. Visible windows strictly between the glyph surface and the backdrop whose
    /// own rectangles intersect the sampled area. Empty means every sample point below belongs to
    /// exactly the two windows this run put there; non-empty NAMES whoever else owns it, and every
    /// pixel claim is conditioned on emptiness rather than assumed.
    /// </param>
    /// <param name="CaptureTaken">Whether the composited-desktop read returned anything at all.</param>
    /// <param name="WithGlyph">The five sample points with the surface up at FULL opacity.</param>
    /// <param name="WithoutGlyph">The same five with it hidden. The control.</param>
    /// <param name="ExpectedWithGlyph">What premultiplied source-over predicts for those points.</param>
    /// <param name="HalfOpacity">
    /// The same five points with the SAME frame composited at opacity 0.5. This is what proves the
    /// dial reaches the compositor at all: with the surface's constant alpha pinned at 255 these
    /// would be byte-identical to <paramref name="WithGlyph"/>.
    /// </param>
    /// <param name="ExpectedHalfOpacity">What the same arithmetic predicts at constant alpha 128.</param>
    /// <param name="HalfOpacityPresented">Whether the half-opacity placement earned Available.</param>
    /// <param name="HalfOpacityState">That state, for failure messages.</param>
    internal sealed record DifferentialRun(
        bool MachineHasInteractiveDesktop,
        bool BackdropPresented,
        bool BackdropPainted,
        CapabilityState BackdropState,
        bool GlyphPresented,
        CapabilityState GlyphState,
        IReadOnlyList<string> Intruders,
        bool CaptureTaken,
        uint[] WithGlyph,
        uint[] WithoutGlyph,
        uint[] ExpectedWithGlyph,
        uint[] HalfOpacity,
        uint[] ExpectedHalfOpacity,
        bool HalfOpacityPresented,
        CapabilityState HalfOpacityState)
    {
        /// <summary>True when the machine really hosted the run: a desktop, both surfaces up, a
        /// capture taken, and NOBODY between them over the sampled area.</summary>
        internal bool ArbitrationHeld =>
            MachineHasInteractiveDesktop && BackdropPresented && BackdropPainted && GlyphPresented
            && CaptureTaken && Intruders.Count == 0;

        /// <summary>The margin outside the surface reads the background's own colour in BOTH
        /// captures, which is what proves the capture is live and the background reached the
        /// screen.</summary>
        internal bool MarginIsBackgroundBothTimes =>
            ArbitrationHeld && WithGlyph[0] == BackdropColour && WithoutGlyph[0] == BackdropColour;

        /// <summary>With the surface hidden, all four covered points read the background. The
        /// CONTROL half of the differential.</summary>
        internal bool ControlReadsBackgroundEverywhere =>
            ArbitrationHeld && WithoutGlyph.Skip(1).All(pixel => pixel == BackdropColour);

        /// <summary>A fully transparent pixel shows the background BEHIND it.</summary>
        internal bool TransparentShowsBackground => ArbitrationHeld && WithGlyph[1] == BackdropColour;

        /// <summary>An opaque BLACK pixel is not transparent: it reads black, it differs from the
        /// transparent point in the SAME capture, and it changed when the surface came up.</summary>
        internal bool OpaqueBlackIsNotTransparent =>
            ArbitrationHeld && WithGlyph[2] == 0x000000u && WithGlyph[2] != WithGlyph[1]
            && WithGlyph[2] != WithoutGlyph[2];

        /// <summary>A glyph pixel is distinguished from the background behind it.</summary>
        internal bool InkIsDistinguishedFromBackground =>
            ArbitrationHeld && WithGlyph[3] == InkColour && WithGlyph[3] != BackdropColour;

        /// <summary>A half-alpha pixel reads exactly premultiplied source-over of the frame over the
        /// measured background - neither the frame's colour nor the background's.</summary>
        internal bool BlendIsPerPixel =>
            ArbitrationHeld && WithGlyph[4] == ExpectedWithGlyph[4]
            && WithGlyph[4] != BackdropColour && WithGlyph[4] != 0x00FFFFFFu;

        /// <summary>One window, one capture, four sample points, four DISTINCT values - which a
        /// surface composited at one uniform alpha over an opaque frame cannot produce.</summary>
        internal bool AllFourQuadrantsDiffer =>
            ArbitrationHeld && WithGlyph.Skip(1).Distinct().Count() == 4;

        /// <summary>Every sampled point equals the predicted composite, not just the ones chosen to
        /// be easy.</summary>
        internal bool EveryPointMatchesThePrediction =>
            ArbitrationHeld && WithGlyph.SequenceEqual(ExpectedWithGlyph);

        /// <summary>The half-opacity leg really happened: the surface earned Available at 0.5 and a
        /// capture came back.</summary>
        internal bool HalfOpacityHeld =>
            ArbitrationHeld && HalfOpacityPresented && HalfOpacity.Length == WithGlyph.Length;

        /// <summary>
        /// <b>The uniform multiplier really reaches the compositor.</b> The same frame at half
        /// opacity reads differently at every point the frame is not transparent at - which a
        /// surface that ignored the dial could not produce, because it would hand the compositor
        /// exactly the same bytes and the same constant.
        /// </summary>
        internal bool TheDialReachesTheCompositor =>
            HalfOpacityHeld
            && HalfOpacity[2] != WithGlyph[2]
            && HalfOpacity[3] != WithGlyph[3]
            && HalfOpacity[4] != WithGlyph[4];

        /// <summary>At half opacity an OPAQUE BLACK pixel is no longer black: it is black composited
        /// over the background at 50 %, which is neither.</summary>
        internal bool HalfOpacityBlackIsNeitherBlackNorBackground =>
            HalfOpacityHeld && HalfOpacity[2] != 0x000000u && HalfOpacity[2] != BackdropColour;

        /// <summary>A fully TRANSPARENT pixel is unchanged by the dial, because zero times anything
        /// is zero. The control on the leg above.</summary>
        internal bool HalfOpacityLeavesTheTransparentPixelAlone =>
            HalfOpacityHeld && HalfOpacity[1] == BackdropColour && HalfOpacity[0] == BackdropColour;

        /// <summary>
        /// Every half-opacity point equals the same arithmetic at constant alpha 128, EXACTLY.
        ///
        /// <para><b>There is no tolerance here, and an earlier draft had one.</b> That draft's
        /// oracle rounded each of the three terms of the composite separately, missed the screen by
        /// one unit in one channel of one point, and absorbed the miss with a
        /// <c>±1</c> allowance — which would have silently swallowed a real one-unit-per-channel
        /// regression at every non-255 setting of the dial. The formula was not wrong; the number of
        /// roundings was. Rounding once, at the end, reproduces all eight measured values at both
        /// opacities, so equality is asserted at 128 exactly as it is at 255.</para>
        /// </summary>
        internal bool EveryHalfOpacityPointMatchesThePrediction =>
            HalfOpacityHeld && HalfOpacity.SequenceEqual(ExpectedHalfOpacity);

        internal string Why =>
            $"desktop={MachineHasInteractiveDesktop} backdropPresented={BackdropPresented} "
            + $"backdropPainted={BackdropPainted} glyphPresented={GlyphPresented} capture={CaptureTaken} "
            + $"intruders=[{string.Join(" | ", Intruders)}] backdrop={Describe(BackdropState)} "
            + $"glyph={Describe(GlyphState)} halfOpacity={Describe(HalfOpacityState)}";
    }

    private static DifferentialRun RunDifferential()
    {
        var (screenWidth, screenHeight) = GlyphWindowProbe.PrimarySize;
        var backdropX = Math.Max(0, (screenWidth / 2) - 200);
        var backdropY = Math.Max(0, (screenHeight / 2) - 150);
        var offsetX = (BackdropWidth - SurfaceSide) / 2;
        var offsetY = (BackdropHeight - SurfaceSide) / 2;

        var frame = Quadrants(SurfaceSide);

        // The background is the LANDED overlay capability, consumed unmodified: a full-opacity
        // layered window holding one known colour, confirmed by the overlay's own Paint read-back.
        // Using a landed capability rather than a scratch window is deliberate — it makes the
        // control a thing the suite already proves, and it is what makes the intruder walk
        // meaningful (both windows are topmost, so the ONLY thing that can come between them is a
        // third topmost window).
        using var backdrop = new Win32OverlayPresence();
        var backdropBounds = new OverlayBounds(backdropX, backdropY, BackdropWidth, BackdropHeight);
        var backdropState = backdrop.Present(new OverlaySurfaceRequest(backdropBounds, 1.0, ClickThrough: true));
        var backdropPaint = backdrop.Paint(OverlayFrame.Solid(BackdropWidth, BackdropHeight, 200, 40, 10));
        var backdropWindow = backdrop.NativeHandles.Window;

        var glyphBounds = new GlyphBounds(backdropX + offsetX, backdropY + offsetY, SurfaceSide, SurfaceSide);
        using var glyph = new Win32GlyphSurface();
        var glyphState = glyph.Present(new GlyphSurfaceRequest(glyphBounds, 1.0, ClickThrough: true), frame);
        var glyphWindow = glyph.NativeHandles.Window;

        // Raise the pair back to back so nothing foreign can slip between them. Measured: with an
        // ordinary interval between the two raises the shipping WPF product sat in the gap and the
        // sampled "background" pixels were its own. This is not helping the fact pass — it is the
        // same bounded re-raise the product itself does — and whether it WORKED is measured next.
        // Bounded by a COUNT and never by a wall-clock wait, exactly as the product's own
        // raise-and-ask loop is: raising removes contention and cannot manufacture an answer,
        // because the z-order walk is still the only thing that produces one. A run that never wins
        // reports WHO owns the region rather than reading its pixels.
        IReadOnlyList<string> intruders = [];
        for (var attempt = 0; attempt < ArbitrationAttempts; attempt++)
        {
            GlyphWindowProbe.RaiseTopmost(backdropWindow);
            GlyphWindowProbe.RaiseTopmost(glyphWindow);
            intruders = GlyphWindowProbe.Intruders(
                glyphWindow, backdropWindow, backdropX, backdropY, BackdropWidth, BackdropHeight);
            if (intruders.Count == 0)
            {
                break;
            }
        }

        var points = SamplePoints(offsetX, offsetY, SurfaceSide);
        var withGlyph = SampleDesktop(backdropX, backdropY, points);

        // THE SAME FRAME AT HALF OPACITY. Nothing about the bytes handed over changes; only the
        // surface's uniform multiplier does. Without this leg the dial could be pinned at 255 inside
        // the backend and no reading anywhere would notice - which was a live mutation survivor, on
        // the one control the product actually ships to a user.
        var halfState = glyph.Present(
            new GlyphSurfaceRequest(glyphBounds, 0.5, ClickThrough: true), frame);
        for (var attempt = 0; attempt < ArbitrationAttempts; attempt++)
        {
            GlyphWindowProbe.RaiseTopmost(backdropWindow);
            GlyphWindowProbe.RaiseTopmost(glyphWindow);
            if (GlyphWindowProbe.Intruders(
                    glyphWindow, backdropWindow, backdropX, backdropY, BackdropWidth,
                    BackdropHeight).Count == 0)
            {
                break;
            }
        }

        var halfOpacity = SampleDesktop(backdropX, backdropY, points);

        glyph.Withdraw();
        var withoutGlyph = SampleDesktop(backdropX, backdropY, points);

        var expected = new uint[points.Length];
        var expectedHalf = new uint[points.Length];
        expected[0] = BackdropColour;
        expectedHalf[0] = BackdropColour;
        for (var i = 1; i < points.Length; i++)
        {
            var (x, y) = points[i];
            expected[i] = frame.CompositeOver(x - offsetX, y - offsetY, BackdropColour, 255);
            expectedHalf[i] = frame.CompositeOver(x - offsetX, y - offsetY, BackdropColour, 128);
        }

        return new DifferentialRun(
            MachineHasInteractiveDesktop: GlyphWindowProbe.MachineHasInteractiveDesktop,
            BackdropPresented: backdropState is CapabilityState.Available,
            BackdropPainted: backdropPaint is CapabilityState.Available,
            BackdropState: backdropState,
            GlyphPresented: glyphState is CapabilityState.Available,
            GlyphState: glyphState,
            Intruders: intruders,
            CaptureTaken: withGlyph.Length == points.Length && withoutGlyph.Length == points.Length,
            WithGlyph: withGlyph,
            WithoutGlyph: withoutGlyph,
            ExpectedWithGlyph: expected,
            HalfOpacity: halfOpacity,
            ExpectedHalfOpacity: expectedHalf,
            HalfOpacityPresented: halfState is CapabilityState.Available,
            HalfOpacityState: halfState);
    }

    /// <summary>
    /// Reads the composited desktop over the backdrop and returns the sample points.
    ///
    /// <para>The capture goes through <see cref="FlashPixelProbe.CaptureDesktop"/>, which is the flash
    /// instrument and carries the DPI mapping this test host needs: USER32 virtualises window
    /// coordinates while the screen device context is physical, so reading the desktop at a window's
    /// own coordinates samples the WRONG POINT. The mapping is derived from the OS itself.</para>
    /// </summary>
    private static uint[] SampleDesktop(int originX, int originY, (int X, int Y)[] points)
    {
        var pixels = FlashPixelProbe.CaptureDesktop(originX, originY, BackdropWidth, BackdropHeight);
        if (pixels.Length == 0)
        {
            return [];
        }

        var (virtualWidth, physicalWidth) = FlashPixelProbe.HorizontalResolutions;
        var (virtualHeight, physicalHeight) = FlashPixelProbe.VerticalResolutions;
        if (virtualWidth <= 0 || virtualHeight <= 0)
        {
            return [];
        }

        var scaleX = physicalWidth / (double)virtualWidth;
        var scaleY = physicalHeight / (double)virtualHeight;
        var width = Math.Max(1, (int)Math.Round(BackdropWidth * scaleX));
        var height = Math.Max(1, (int)Math.Round(BackdropHeight * scaleY));
        if (pixels.Length < width * height)
        {
            return [];
        }

        var sampled = new uint[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var x = Math.Clamp((int)Math.Round(points[i].X * scaleX), 0, width - 1);
            var y = Math.Clamp((int)Math.Round(points[i].Y * scaleY), 0, height - 1);
            sampled[i] = pixels[(y * width) + x];
        }

        return sampled;
    }

    // ---------------------------------------------------------------- coexistence

    /// <param name="PointPassesThrough">The overlay's own centre still routes past it.</param>
    /// <param name="AboveEveryOrdinaryWindow">The overlay is still above every ordinary window.</param>
    /// <param name="Alpha">The overlay's UNIFORM alpha, which a glyph surface must never have.</param>
    /// <param name="TransparentStyleHeld">WS_EX_TRANSPARENT survived.</param>
    /// <param name="IsForeground">The overlay never becomes the foreground.</param>
    internal readonly record struct OverlayReading(
        bool PointPassesThrough,
        bool AboveEveryOrdinaryWindow,
        int Alpha,
        bool TransparentStyleHeld,
        bool IsForeground);

    /// <param name="Visible">The card is on screen.</param>
    /// <param name="IsForeground">It holds the foreground.</param>
    /// <param name="HoldsSystemKeyboardFocus">And the system keyboard focus.</param>
    internal readonly record struct CardReading(bool Visible, bool IsForeground, bool HoldsSystemKeyboardFocus);

    /// <param name="MachineHasInteractiveDesktop">Whether there was anywhere for anything to go.</param>
    /// <param name="OverlayPresented">The overlay really reached the desktop.</param>
    /// <param name="CardTookTheInput">The card really took the foreground and the keyboard.</param>
    /// <param name="VideoShowedAPicture">The video surface really held a decoded picture.</param>
    /// <param name="PointerOpened">The pointer target really opened.</param>
    /// <param name="GlyphEarnedAvailableBesideThem">And the glyph surface really composited beside them.</param>
    /// <param name="GlyphState">That state, for failure messages.</param>
    /// <param name="OverlayBefore">The overlay before the glyph surface existed.</param>
    /// <param name="OverlayDuringPresent">While it was up.</param>
    /// <param name="OverlayDuringMove">While it was being moved.</param>
    /// <param name="OverlayAfter">After it was gone.</param>
    /// <param name="CardBefore">The card, same four moments.</param>
    /// <param name="CardDuringPresent">…</param>
    /// <param name="CardDuringMove">…</param>
    /// <param name="CardAfter">…</param>
    /// <param name="VideoHeldPictureDuring">The video capability's own oracle, re-asked.</param>
    /// <param name="VideoShowStateDuring">That state.</param>
    /// <param name="PointerStillOwnsItsPoint">The pointer target's own point still routes to it.</param>
    /// <param name="GlyphMoveState">The move, taken beside all four.</param>
    /// <param name="OverlayCatchesItsOwnPointWhenMadeOpaque">The overlay's own differential, after.</param>
    /// <param name="OverlayStillEarnsAvailable">The overlay's own oracle, re-asked.</param>
    /// <param name="OverlayRePresentState">That state.</param>
    /// <param name="GlyphSurfaceSharesNoRectangle">
    /// Whether the glyph surface's rectangle is disjoint from all four landed ones. It IS, and the
    /// value is recorded rather than assumed — the deliberate overlap this capability's evidence
    /// needs lives in the differential run, over a background this run does not contain.
    /// </param>
    internal sealed record CoexistenceRun(
        bool MachineHasInteractiveDesktop,
        bool OverlayPresented,
        bool CardTookTheInput,
        bool VideoShowedAPicture,
        bool PointerOpened,
        bool GlyphEarnedAvailableBesideThem,
        CapabilityState GlyphState,
        OverlayReading OverlayBefore,
        OverlayReading OverlayDuringPresent,
        OverlayReading OverlayDuringMove,
        OverlayReading OverlayAfter,
        CardReading CardBefore,
        CardReading CardDuringPresent,
        CardReading CardDuringMove,
        CardReading CardAfter,
        bool VideoHeldPictureDuring,
        CapabilityState VideoShowStateDuring,
        bool PointerStillOwnsItsPoint,
        CapabilityState GlyphMoveState,
        bool OverlayCatchesItsOwnPointWhenMadeOpaque,
        bool OverlayStillEarnsAvailable,
        CapabilityState OverlayRePresentState,
        bool GlyphSurfaceSharesNoRectangle);

    private static CoexistenceRun RunCoexistence()
    {
        var (screenWidth, screenHeight) = GlyphWindowProbe.PrimarySize;

        // The four landed rectangles keep their own positions, so this run is a strict extension
        // of the arrangement already proved rather than a rearrangement of it.
        var overlayBounds = new OverlayBounds(
            Math.Max(0, (screenWidth / 2) - 660), Math.Max(0, (screenHeight / 2) - 150), 200, 150);
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

        var cardBounds = new InputBounds(
            Math.Max(0, (screenWidth / 2) + 200), Math.Max(0, (screenHeight / 2) + 160), 360, 180);
        using var card = new Win32InputPresence();
        card.Prompt(new InputPromptRequest(
            cardBounds,
            new InputPromptContent("say this", "1 of 1", string.Empty, "Press Esc to close"),
            _ => { }));
        var cardWindow = card.NativeHandles.Window;

        CardReading ReadCard() => new(
            Visible: InputWindowProbe.WindowIsVisible(cardWindow),
            IsForeground: InputWindowProbe.Foreground() == cardWindow,
            HoldsSystemKeyboardFocus: InputWindowProbe.SystemKeyboardFocus() == cardWindow);

        var cardTookTheInput = ReadCard() is { Visible: true, IsForeground: true, HoldsSystemKeyboardFocus: true };

        var path = VideoSurfaceObservations.WriteFixtureClip("glyph-coexistence.avi");
        var source = VideoPresenceFactory.CreateClipSourceFor(VideoHostPlatform.Windows);
        source.Open(path, out var clip);
        var videoFrame = clip?.ReadFrame();
        var videoBounds = new VideoBounds(
            Math.Max(0, (screenWidth / 2) - 200), Math.Max(0, (screenHeight / 2) - 340),
            VideoSurfaceObservations.SurfaceWidth, VideoSurfaceObservations.SurfaceHeight);
        var video = new Win32VideoPresence(source);
        video.Present(new VideoSurfaceRequest(videoBounds, VideoSurfaceObservations.Letterbox));
        var firstShow = videoFrame is null
            ? new CapabilityState.Unavailable(new CapabilityReason("(no frame)", "nothing decoded"))
            : video.Show(videoFrame);

        var pointerBounds = new PointerBounds(
            Math.Max(0, (screenWidth / 2) + 260), Math.Max(0, (screenHeight / 2) - 320),
            PointerSurfaceObservations.TargetSide, PointerSurfaceObservations.TargetSide);
        var pointer = new Win32PointerSurface();
        var pointerOpen = pointer.Open(new PointerTargetRequest(pointerBounds, 0x00201020, 0x00E0C0FF), out var target);

        var overlayBefore = ReadOverlay();
        var cardBefore = ReadCard();

        // THE FIFTH SURFACE. Its rectangle is disjoint from all four above — the deliberate overlap
        // this capability's evidence needs is in the DIFFERENTIAL run, over a background this run
        // does not contain, so nothing here is contending for another surface's hit-test point.
        var glyphBounds = new GlyphBounds(
            Math.Max(0, (screenWidth / 2) - 660), Math.Max(0, (screenHeight / 2) + 60), SurfaceSide, SurfaceSide);
        var glyph = new Win32GlyphSurface();
        var glyphState = glyph.Present(
            new GlyphSurfaceRequest(glyphBounds, 1.0, ClickThrough: true), Quadrants(SurfaceSide));

        var overlayDuringPresent = ReadOverlay();
        var cardDuringPresent = ReadCard();

        var glyphMoveState = glyph.MoveTo(new GlyphBounds(
            glyphBounds.X, glyphBounds.Y + 18, glyphBounds.Width, glyphBounds.Height));
        var overlayDuringMove = ReadOverlay();
        var cardDuringMove = ReadCard();

        var showDuring = videoFrame is null
            ? new CapabilityState.Unavailable(new CapabilityReason("(no frame)", "nothing decoded"))
            : video.Show(videoFrame);

        var pointerWindow = pointer.NativeHandlesFor(target).Window;
        var pointerCentreX = pointerBounds.X + (pointerBounds.Width / 2);
        var pointerCentreY = pointerBounds.Y + (pointerBounds.Height / 2);
        var pointerOwnsItsPoint =
            PointerWindowProbe.HitTestAfterRaising(pointerWindow, pointerCentreX, pointerCentreY) == pointerWindow;

        glyph.Withdraw();
        glyph.Dispose();

        pointer.Close(target);
        pointer.Dispose();
        video.Withdraw();
        video.Dispose();
        clip?.Dispose();

        overlay.Reassert();
        var overlayAfter = ReadOverlay();
        var cardAfter = ReadCard();

        card.Dismiss();

        overlay.SetClickThrough(false);
        var overlayCatchesItsOwnPoint = OverlayWindowProbe.HitTest(overlayX, overlayY) == overlayWindow;
        overlay.SetClickThrough(true);

        var rePresent = overlay.Present(new OverlaySurfaceRequest(overlayBounds, 0.6, ClickThrough: true));

        var glyphRect = new GlyphBounds(glyphBounds.X, glyphBounds.Y, glyphBounds.Width, glyphBounds.Height);
        var disjoint =
            !glyphRect.Intersects(new GlyphBounds(
                overlayBounds.X, overlayBounds.Y, overlayBounds.Width, overlayBounds.Height))
            && !glyphRect.Intersects(new GlyphBounds(
                cardBounds.X, cardBounds.Y, cardBounds.Width, cardBounds.Height))
            && !glyphRect.Intersects(new GlyphBounds(
                videoBounds.X, videoBounds.Y, videoBounds.Width, videoBounds.Height))
            && !glyphRect.Intersects(new GlyphBounds(
                pointerBounds.X, pointerBounds.Y, pointerBounds.Width, pointerBounds.Height));

        return new CoexistenceRun(
            MachineHasInteractiveDesktop: GlyphWindowProbe.MachineHasInteractiveDesktop,
            OverlayPresented: presented is CapabilityState.Available,
            CardTookTheInput: cardTookTheInput,
            VideoShowedAPicture: firstShow is CapabilityState.Available,
            PointerOpened: pointerOpen is CapabilityState.Available,
            GlyphEarnedAvailableBesideThem: glyphState is CapabilityState.Available,
            GlyphState: glyphState,
            OverlayBefore: overlayBefore,
            OverlayDuringPresent: overlayDuringPresent,
            OverlayDuringMove: overlayDuringMove,
            OverlayAfter: overlayAfter,
            CardBefore: cardBefore,
            CardDuringPresent: cardDuringPresent,
            CardDuringMove: cardDuringMove,
            CardAfter: cardAfter,
            VideoHeldPictureDuring: showDuring is CapabilityState.Available,
            VideoShowStateDuring: showDuring,
            PointerStillOwnsItsPoint: pointerOwnsItsPoint,
            GlyphMoveState: glyphMoveState,
            OverlayCatchesItsOwnPointWhenMadeOpaque: overlayCatchesItsOwnPoint,
            OverlayStillEarnsAvailable: rePresent is CapabilityState.Available,
            OverlayRePresentState: rePresent,
            GlyphSurfaceSharesNoRectangle: disjoint);
    }
}
