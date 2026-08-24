using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Video;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>What the video surface's geometry does when the desk has more than one monitor on it</b> —
/// injected topologies, as ARITHMETIC.
///
/// <para><b>What these facts are, and the sentence that bounds every one of them.</b> Not one of
/// them puts a pixel on a second screen, because the machine this port is developed on has ONE
/// display and the only way to give it another is <c>ChangeDisplaySettingsEx</c> on the owner's real
/// desktop, which two earlier lanes correctly declined and this one declines too. So the claim these
/// facts support is exactly "the rectangle arithmetic is right for a topology shaped like this",
/// and the claim they do NOT support is "a picture appeared on that monitor". The second needs a
/// second monitor and a human; <c>client/docs/verification-harness.md</c> governs, and no in-memory
/// rectangle ever discharges a headed gate.</para>
///
/// <para><b>THE LARGER FINDING THESE FACTS SIT INSIDE, stated here so no reader mistakes green for
/// coverage.</b> The unified presentation contract
/// (<c>client/docs/architecture.md</c> A-003, <c>client/docs/capability-inventory.md</c> "Video
/// presentation") requires the same decoded frame on EVERY connected monitor, aspect-fitted
/// independently per monitor, from one decoder and one playback clock, with one audible stream.
/// <b>The port fans out to nothing.</b> <see cref="PrimaryDisplayPlacement.PrimaryBounds"/> returns
/// ONE display's bounds, <see cref="VideoSurfacePresenter.MaxConcurrentSurfaces"/> is 1, and the
/// presenter's whole display seam is a <c>Func&lt;VideoBounds?&gt;</c> — one nullable rectangle, so
/// fan-out is not merely unimplemented, it is unrepresentable without changing that signature.
/// D66/D123 declare that on purpose. These facts therefore prove that the ONE rectangle the port
/// does compute is computed correctly for a monitor anywhere on the virtual desktop; they say
/// nothing about the N-1 monitors that get no surface, because there is no code for those to be
/// right or wrong about.</para>
///
/// <para><b>Upstream's outcome, which is what is being ported.</b> WPF creates one fullscreen window
/// and one <c>WriteableBitmap</c> per screen from its own screen enumeration
/// (<c>Services/Video/DualMonitorVideoService.cs:373-387</c>), takes each window's rectangle
/// straight from that screen's own bounds so a monitor at a negative <c>Left</c>/<c>Top</c> is
/// honoured as-is (<c>Services/Video/DualMonitorVideoService.cs:429-432</c>), and aspect-fits inside
/// it with black around the picture (<c>Stretch.Uniform</c> over a black background,
/// <c>Services/Video/DualMonitorVideoService.cs:407</c> and <c>:415</c>). The letterbox arithmetic
/// itself is <c>Services/Video/VideoService.cs:3193-3211</c> (<c>FitToAspect</c> — "letterbox or
/// pillarbox, never stretch"), which <see cref="VideoLetterbox.Fit"/> already carries.
/// <b>Upstream does NOT fan out unconditionally either</b>: at three or more monitors it fills the
/// secondaries only on an explicit opt-in, and its own comment gives the reason as "N independent
/// decoders on high monitor counts" (<c>Services/Video/VideoService.cs:2035-2045</c>) — which is the
/// cost the contract's one-decoder design exists to remove.</para>
/// </summary>
public class VideoDisplayGeometryTests
{
    /// <summary>
    /// A surface placed for a display lands wholly INSIDE that display, and nowhere near its
    /// neighbour — for a monitor to the left, right, above or below, across a gap, at a different
    /// resolution/scale, and in portrait.
    ///
    /// <para><b>The bug this is aimed at is the classic one, and it is invisible on a single-monitor
    /// desk:</b> centring arithmetic that forgets the display's ORIGIN. <c>(Width - w) / 2</c> is
    /// correct on the primary display and wrong on every other one, and on a desk with one monitor
    /// the two are the same number for ever. Dropping <c>display.X</c> from
    /// <see cref="PrimaryDisplayPlacement.Centred"/> is exactly that mutation and it is what these
    /// rows catch.</para>
    ///
    /// <para>Containment in one display plus disjointness from the other is also what rules out the
    /// contract's named prohibition — "never render one giant video across the virtual-desktop
    /// union" (<c>client/docs/capability-inventory.md</c>, "Per-monitor geometry"): a rectangle
    /// inside one monitor and missing the other cannot be the union of the two.</para>
    /// </summary>
    [Theory]
    [InlineData("a second monitor to the RIGHT", 0, 0, 1920, 1080, 1920, 0, 1920, 1080)]
    [InlineData("a second monitor to the LEFT, at NEGATIVE X", 0, 0, 1920, 1080, -1920, 0, 1920, 1080)]
    [InlineData("a second monitor ABOVE, at NEGATIVE Y", 0, 0, 1920, 1080, 0, -1200, 1920, 1200)]
    [InlineData("a second monitor BELOW", 0, 0, 1920, 1080, 0, 1080, 1920, 1080)]
    [InlineData("a GAP between the two", 0, 0, 1920, 1080, 3000, 0, 1280, 1024)]
    [InlineData("MIXED scaling and resolution", 0, 0, 2560, 1440, -1280, 300, 1280, 800)]
    [InlineData("a PORTRAIT secondary beside a landscape primary", 0, 0, 1920, 1080, 1920, -420, 1080, 1920)]
    [InlineData("a PORTRAIT primary with a landscape secondary", 0, 0, 1080, 1920, 1080, 0, 1920, 1080)]
    public void EachDisplaysSurfaceLandsInsideTHATDisplay_AndMissesItsNeighbourEntirely(
        string topology,
        int firstX, int firstY, int firstWidth, int firstHeight,
        int secondX, int secondY, int secondWidth, int secondHeight)
    {
        var first = new OverlayBounds(firstX, firstY, firstWidth, firstHeight);
        var second = new OverlayBounds(secondX, secondY, secondWidth, secondHeight);

        var (ax, ay, aw, ah) = PrimaryDisplayPlacement.Centred(
            first, VideoSurfacePresenter.WidthFraction, VideoSurfacePresenter.HeightFraction);
        var (bx, by, bw, bh) = PrimaryDisplayPlacement.Centred(
            second, VideoSurfacePresenter.WidthFraction, VideoSurfacePresenter.HeightFraction);

        Assert.True(
            ax >= first.X && ay >= first.Y
            && ax + aw <= first.X + first.Width && ay + ah <= first.Y + first.Height,
            $"[{topology}] the surface for {first} landed at {ax},{ay} {aw}x{ah}, which is not wholly inside "
            + "that display. A surface that spills off its own monitor is what centring arithmetic that forgot "
            + "the display's origin looks like, and on a one-monitor desk it looks like nothing at all");
        Assert.True(
            bx >= second.X && by >= second.Y
            && bx + bw <= second.X + second.Width && by + bh <= second.Y + second.Height,
            $"[{topology}] the surface for {second} landed at {bx},{by} {bw}x{bh}, which is not wholly inside "
            + "that display");

        Assert.False(
            Intersects(ax, ay, aw, ah, second),
            $"[{topology}] the surface computed for {first} lands at {ax},{ay} {aw}x{ah} and overlaps the OTHER "
            + $"display {second}. Upstream takes each window's rectangle from that screen's own bounds "
            + "(Services/Video/DualMonitorVideoService.cs:429-432) precisely so this cannot happen");
        Assert.False(
            Intersects(bx, by, bw, bh, first),
            $"[{topology}] the surface computed for {second} lands at {bx},{by} {bw}x{bh} and overlaps the OTHER "
            + $"display {first}");

        // The bounded fraction is D123's divergence from upstream's fullscreen-per-monitor cover, and
        // it is asserted rather than assumed: a surface that quietly grew to the whole display would
        // satisfy every containment clause above and would be a different product.
        Assert.Equal((int)(first.Width * VideoSurfacePresenter.WidthFraction), aw);
        Assert.Equal((int)(first.Height * VideoSurfacePresenter.HeightFraction), ah);
        Assert.Equal((int)(second.Width * VideoSurfacePresenter.WidthFraction), bw);
        Assert.Equal((int)(second.Height * VideoSurfacePresenter.HeightFraction), bh);
    }

    /// <summary>
    /// A portrait display gets a portrait surface. The contract's clause is explicit — "Do not
    /// assume width greater than height means landscape"
    /// (<c>client/docs/capability-inventory.md</c>, "Per-monitor geometry") — and the way that is
    /// honoured here is that nothing in the placement inspects the orientation at all: both sides
    /// come from the display's own reported extent. This fact pins the exact numbers on both
    /// shapes, so an orientation branch added later (the tempting "swap the fractions when it is
    /// taller than it is wide") is a red rather than a subtlety.
    /// </summary>
    [Fact]
    public void APortraitDisplayGetsAPortraitSurface_BecauseNothingInThePlacementLooksAtOrientation()
    {
        var landscape = PrimaryDisplayPlacement.Centred(
            new OverlayBounds(0, 0, 1920, 1080),
            VideoSurfacePresenter.WidthFraction, VideoSurfacePresenter.HeightFraction);
        var portrait = PrimaryDisplayPlacement.Centred(
            new OverlayBounds(0, 0, 1080, 1920),
            VideoSurfacePresenter.WidthFraction, VideoSurfacePresenter.HeightFraction);

        Assert.Equal((432, 313, 1056, 453), landscape);
        Assert.Equal((243, 557, 594, 806), portrait);

        Assert.True(
            landscape.Width > landscape.Height,
            $"a 1920x1080 display must produce a wider-than-tall surface; it produced {landscape}");
        Assert.True(
            portrait.Height > portrait.Width,
            $"a 1080x1920 display must produce a taller-than-wide surface; it produced {portrait}. A placement "
            + "that answered the same shape for both is one that decided what 'landscape' means instead of "
            + "reading what the operating system reported");
    }

    /// <summary>
    /// <b>Aspect fit is independent per surface shape</b>: one 16:9 clip, two differently shaped
    /// monitors, and the SAME picture pillarboxes on one and letterboxes on the other — each keeping
    /// the clip's own aspect, neither cropped, neither stretched.
    ///
    /// <para>This is the arithmetic half of the contract's "Preserve the video's original aspect
    /// ratio independently on every monitor" and of upstream's <c>Stretch.Uniform</c> over black
    /// (<c>Services/Video/DualMonitorVideoService.cs:407</c>, <c>:415</c>). <b>It is arithmetic and
    /// not presentation</b>: what is proved is that the two shapes yield two different correct
    /// boxes, not that two monitors ever showed them.</para>
    /// </summary>
    [Fact]
    public void OneClipFitsTwoMonitorShapesINDEPENDENTLY_PillarboxingOneAndLetterboxingTheOther()
    {
        // The two surfaces the placement above produces for a landscape and a portrait display.
        var onLandscape = VideoLetterbox.Fit(1056, 453, 1280, 720);
        var onPortrait = VideoLetterbox.Fit(594, 806, 1280, 720);

        Assert.Equal(new PictureBox(130, 3, 795, 447), onLandscape);
        Assert.Equal(new PictureBox(3, 237, 588, 331), onPortrait);

        // Independent, which is the whole word in the contract clause: the same clip does not get
        // the same box on two differently shaped targets.
        Assert.NotEqual(onLandscape, onPortrait);

        // Neither is a stretch: 16:9 survives on both to within a pixel of rounding.
        Assert.True(
            Math.Abs((onLandscape.Width / (double)onLandscape.Height) - (1280 / 720.0)) < 0.02,
            $"{onLandscape} has aspect {onLandscape.Width / (double)onLandscape.Height:0.###} against the clip's 1.778");
        Assert.True(
            Math.Abs((onPortrait.Width / (double)onPortrait.Height) - (1280 / 720.0)) < 0.02,
            $"{onPortrait} has aspect {onPortrait.Width / (double)onPortrait.Height:0.###} against the clip's 1.778");

        // And the BARS fall on different sides, which is what "independently" buys the user: the
        // wide surface gets pillars, the tall one gets letterbox bands, and each is floored at the
        // read-back's control margin on the other axis.
        Assert.True(
            onLandscape.X > VideoLetterbox.Margin && onLandscape.Y == VideoLetterbox.Margin,
            $"a 16:9 clip in a 1056x453 surface must PILLARBOX — bars left and right — and {onLandscape} does not");
        Assert.True(
            onPortrait.Y > VideoLetterbox.Margin && onPortrait.X == VideoLetterbox.Margin,
            $"the same clip in a 594x806 surface must LETTERBOX — bars top and bottom — and {onPortrait} does not");

        // Never a crop: the picture stays inside the inner box on both.
        Assert.True(onLandscape.X + onLandscape.Width <= 1056 - VideoLetterbox.Margin);
        Assert.True(onLandscape.Y + onLandscape.Height <= 453 - VideoLetterbox.Margin);
        Assert.True(onPortrait.X + onPortrait.Width <= 594 - VideoLetterbox.Margin);
        Assert.True(onPortrait.Y + onPortrait.Height <= 806 - VideoLetterbox.Margin);
    }

    /// <summary>Whether a placed surface touches a display it was not placed on.</summary>
    private static bool Intersects(int x, int y, int width, int height, OverlayBounds display) =>
        x < display.X + display.Width && x + width > display.X
        && y < display.Y + display.Height && y + height > display.Y;
}
