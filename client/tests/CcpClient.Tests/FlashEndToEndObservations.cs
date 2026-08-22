using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;

namespace CcpClient.Tests;

/// <summary>
/// The whole chain, once: a real <c>.png</c> on disk -> the product's GDI+ frame source -> the
/// product's presenter -> a real overlay surface -> the composited desktop, read back.
///
/// <para>Nothing is stubbed except the CLOCK (so the stagger and the lifetime need no wall-clock
/// wait) and the DISPLAY (taken from the probe's own screen reading rather than from the product's
/// enumeration, which is a thing under test elsewhere and must not be an input here).</para>
///
/// <para><b>Why the check counts pixels of a colour instead of looking at a rectangle.</b> A flash
/// is placed at a RANDOM point (WPF's <c>PickSpawnPoint</c>, <c>FlashService.cs:2360</c>) and the
/// presenter does not publish where it went. Counting how many pixels of the whole desktop are the
/// image's own colour therefore needs no placement knowledge, and the colour is one nothing else on
/// a desktop is: if that count jumps by an image's worth when a flash fires and drops back when it
/// is taken away, the image was on the screen.</para>
///
/// <para>This is also the deterministic trigger the orchestrator's headed capture uses, and it
/// leaves its evidence in <see cref="FlashDrawObservations.EvidenceFolder"/>.</para>
/// </summary>
internal static class FlashEndToEndObservations
{
    /// <summary>The image's colour as a <c>COLORREF</c> (0x00BBGGRR): R=0x1E, G=0x7F, B=0xD2.</summary>
    internal const uint ImageColour = 0xD27F1E;

    private const int SourceWidth = 800;
    private const int SourceHeight = 600;

    private static readonly Lazy<Run> LazyRun = new(Measure, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static Run Measured => LazyRun.Value;

    /// <param name="DesktopCaptureIsLive">The machine property every composited expectation is compared against.</param>
    /// <param name="DecodedFrameWidth">The frame the product's own decoder produced.</param>
    /// <param name="DecodedFrameHeight">Ditto.</param>
    /// <param name="ExpectedFrameWidth">What WPF's geometry says that size must be.</param>
    /// <param name="ExpectedFrameHeight">Ditto.</param>
    /// <param name="SurfacesShown">How many surfaces the presenter placed.</param>
    /// <param name="DesktopPixelsBefore">Pixels of the image's colour on the desktop before the flash.</param>
    /// <param name="DesktopPixelsDuring">…while it is up.</param>
    /// <param name="DesktopPixelsAfterHide">…after the presenter is told to hide everything.</param>
    /// <param name="DesktopPixelsSampledDuring">
    /// How many pixels the screen read actually RETURNED for the measurement above.
    /// <c>CountOf</c> of an empty capture is 0, which is the same number as "the flash is not on the
    /// screen" — so without this the two are indistinguishable in the failure. A full-screen
    /// CAPTUREBLT allocates a ~20 MB DIB, and three concurrent floor runs are exactly when that
    /// allocation is most likely to fail. This field is diagnostic only; nothing is asserted about
    /// it and nothing is silenced by it.
    /// </param>
    /// <param name="CompositorFenceHeldDuring">
    /// The fifth verdict behind a count of zero. Whether the screen read that produced
    /// <see cref="Run.DesktopPixelsDuring"/> was ordered behind the compositor at all. Without that
    /// edge a layered top-most window this process had just shown and painted was absent from the
    /// read 34 times in 1200 on this machine, with the window owning its own centre point every
    /// time. <b>Captured inside that read rather than read off the static afterwards</b> (review
    /// finding): the evidence write and the after-hide capture each take their own fence, so a
    /// later read of <see cref="FlashPixelProbe.CompositorFenceHeld"/> would report a DIFFERENT
    /// capture's fence under a name that says "During". Diagnostic only; nothing is asserted on it.
    /// </param>
    /// <param name="DesktopUniformPixelsDuring">
    /// How many of those returned pixels equal the first one. A read that came back UNIFORM
    /// is a blank or asleep display, which is a third verdict again — different from "the allocation
    /// failed" and from "the flash was not on the screen". Diagnostic only.
    /// </param>
    /// <param name="DesktopFirstPixelDuring">…and what that first pixel was. Diagnostic only.</param>
    /// <param name="PlacementScreen">
    /// Review finding. <see cref="OverlayWindowProbe.PrimarySize"/> as read ONCE at the top
    /// of <see cref="Measure"/>, which is what every rectangle in this run is derived from.
    /// </param>
    /// <param name="PlacementHorizontal">The virtual/physical horizontal pair at that same moment.</param>
    /// <param name="PlacementVertical">The virtual/physical vertical pair at that same moment.</param>
    /// <param name="CaptureHorizontalDuring">
    /// …and the pair <see cref="FlashPixelProbe.CaptureDesktop"/> re-read on its OWN call, which is
    /// what it maps the requested rectangle through. If these disagree with the placement pair, the
    /// desktop's scale or resolution changed in between and the capture sampled the wrong region —
    /// a full desktop comes back carrying none of the flash's colour, which is exactly the residual
    /// signature and is NOT distinguishable by the returned pixel COUNT, because that count is
    /// physical and therefore scale-invariant.
    /// </param>
    /// <param name="CaptureVerticalDuring">Ditto, vertically.</param>
    /// <param name="TransparentHalfIsBlack">A transparent PNG half composes over black, as WPF's window does.</param>
    /// <param name="OpaqueHalfIsTheImage">…and the opaque half is the image.</param>
    /// <param name="MissingFileDecodes">A path that does not exist must produce no frame and no exception.</param>
    /// <param name="CorruptFileDecodes">Neither must a file that is not an image at all.</param>
    internal sealed record Run(
        bool DesktopCaptureIsLive,
        int DecodedFrameWidth,
        int DecodedFrameHeight,
        int ExpectedFrameWidth,
        int ExpectedFrameHeight,
        int SurfacesShown,
        int DesktopPixelsBefore,
        int DesktopPixelsDuring,
        int DesktopPixelsAfterHide,
        int DesktopPixelsSampledDuring,
        bool CompositorFenceHeldDuring,
        int DesktopUniformPixelsDuring,
        uint DesktopFirstPixelDuring,
        (int Width, int Height) PlacementScreen,
        (int Virtual, int Physical) PlacementHorizontal,
        (int Virtual, int Physical) PlacementVertical,
        (int Virtual, int Physical) CaptureHorizontalDuring,
        (int Virtual, int Physical) CaptureVerticalDuring,
        bool TransparentHalfIsBlack,
        bool OpaqueHalfIsTheImage,
        bool MissingFileDecodes,
        bool CorruptFileDecodes)
    {
        /// <summary>
        /// Review finding: whether the display metrics the flash was PLACED through are
        /// still the ones the screen was READ through. False means the desktop was rescaled or
        /// re-resolved in between, so the capture mapped the requested rectangle by a different
        /// ratio than the placement used and sampled a region the flash was never in. Diagnostic
        /// only — no assertion is conditioned on it.
        /// </summary>
        internal bool DisplayMetricsHeldStill =>
            PlacementHorizontal == CaptureHorizontalDuring && PlacementVertical == CaptureVerticalDuring;
    }

    private static Run Measure()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ccp-sp100-images-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var imagePath = Path.Combine(folder, "flash.png");
        TestPng.WriteSolid(imagePath, SourceWidth, SourceHeight, 0x1E, 0x7F, 0xD2);

        var alphaPath = Path.Combine(folder, "half-transparent.png");
        TestPng.WriteHalfTransparent(alphaPath, 200, 200, 0x1E, 0x7F, 0xD2);

        var corruptPath = Path.Combine(folder, "corrupt.png");
        File.WriteAllBytes(corruptPath, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        try
        {
            // Review finding: the display is read ONCE here and every rectangle below is
            // derived from it, while CaptureDesktop re-reads the resolutions on every call. Both
            // readings are recorded so a change between them names itself instead of arriving as an
            // unexplained "the flash was not on the desktop".
            var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;
            var placementHorizontal = FlashPixelProbe.HorizontalResolutions;
            var placementVertical = FlashPixelProbe.VerticalResolutions;
            var display = new OverlayBounds(0, 0, Math.Max(1, screenWidth), Math.Max(1, screenHeight));
            var frames = new GdiPlusFlashFrameSource();

            var expected = FlashGeometry.Size(
                SourceWidth, SourceHeight, display.Width, display.Height, FlashSurfacePresenter.ImageScalePercent);
            var decoded = frames.Render(
                imagePath,
                (w, h) => FlashGeometry.Size(w, h, display.Width, display.Height, FlashSurfacePresenter.ImageScalePercent));

            var alphaFrame = frames.Render(alphaPath, static (_, _) => (200, 200));
            var transparentIsBlack = alphaFrame is not null && alphaFrame.ColourAt(100, 50) == 0x000000;
            var opaqueIsImage = alphaFrame is not null && alphaFrame.ColourAt(100, 150) == ImageColour;

            var clock = new EndToEndClock();
            using var presenter = new FlashSurfacePresenter(
                clock, action => action(), OverlayPresenceFactory.Create, frames, () => display, new Random(1000));

            var before = CountDesktop(display);
            presenter.Show([imagePath]);
            var (during, sampledDuring, uniformDuring, firstDuring, captureHorizontal, captureVertical,
                fenceDuring) =
                CountDesktopWithSampleSize(display, evidence: "desktop-with-a-real-flash.bmp", display);
            var shown = presenter.SurfacesShown;

            presenter.HideAll();
            var after = CountDesktop(display);

            return new Run(
                DesktopCaptureIsLive: FlashDrawObservations.Run.DesktopCaptureIsLive,
                DecodedFrameWidth: decoded?.Width ?? 0,
                DecodedFrameHeight: decoded?.Height ?? 0,
                ExpectedFrameWidth: expected.Width,
                ExpectedFrameHeight: expected.Height,
                SurfacesShown: shown,
                DesktopPixelsBefore: before,
                DesktopPixelsDuring: during,
                DesktopPixelsAfterHide: after,
                DesktopPixelsSampledDuring: sampledDuring,
                CompositorFenceHeldDuring: fenceDuring,
                DesktopUniformPixelsDuring: uniformDuring,
                DesktopFirstPixelDuring: firstDuring,
                PlacementScreen: (screenWidth, screenHeight),
                PlacementHorizontal: placementHorizontal,
                PlacementVertical: placementVertical,
                CaptureHorizontalDuring: captureHorizontal,
                CaptureVerticalDuring: captureVertical,
                TransparentHalfIsBlack: transparentIsBlack,
                OpaqueHalfIsTheImage: opaqueIsImage,
                MissingFileDecodes: frames.Render(
                    Path.Combine(folder, "not-here.png"), static (_, _) => (10, 10)) is not null,
                CorruptFileDecodes: frames.Render(corruptPath, static (_, _) => (10, 10)) is not null);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static int CountDesktop(OverlayBounds display, string? evidence = null, OverlayBounds? area = null) =>
        CountDesktopWithSampleSize(display, evidence, area).Count;

    /// <summary>
    /// Diagnostic. A screen read that came back UNIFORM is a blank or asleep display, not a
    /// desktop missing a flash. Returns how many of the sampled pixels equal the first one, and what
    /// that first one is, so the failure text can tell those two verdicts apart. Nothing is asserted
    /// about either value.
    /// </summary>
    private static (int Uniform, uint First) Uniformity(uint[] pixels)
    {
        if (pixels.Length == 0)
        {
            return (0, 0);
        }

        var first = pixels[0];
        var same = 0;
        for (var i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] == first)
            {
                same++;
            }
        }

        return (same, first);
    }

    private static (int Count, int Sampled, int Uniform, uint First,
        (int Virtual, int Physical) Horizontal, (int Virtual, int Physical) Vertical,
        bool FenceHeld) CountDesktopWithSampleSize(
        OverlayBounds display, string? evidence = null, OverlayBounds? area = null)
    {
        var rect = area ?? display;
        // Read on THIS call, exactly as CaptureDesktop reads them on its own call, so the pair the
        // capture actually mapped through is what gets recorded.
        var captureHorizontal = FlashPixelProbe.HorizontalResolutions;
        var captureVertical = FlashPixelProbe.VerticalResolutions;
        var pixels = FlashPixelProbe.CaptureDesktop(rect.X, rect.Y, rect.Width, rect.Height);
        // Review finding: read on THIS call, before the evidence write below takes another
        // capture and overwrites the static. A field called "…During" that reports a LATER read's
        // fence would be exactly the kind of true-looking number this packet exists to refuse.
        var fenceHeld = FlashPixelProbe.CompositorFenceHeld;
        if (evidence is not null && pixels.Length > 0)
        {
            var (virtualWidth, physicalWidth) = FlashPixelProbe.HorizontalResolutions;
            var (virtualHeight, physicalHeight) = FlashPixelProbe.VerticalResolutions;
            var width = (int)Math.Round(rect.Width * (physicalWidth / (double)Math.Max(1, virtualWidth)));
            var height = (int)Math.Round(rect.Height * (physicalHeight / (double)Math.Max(1, virtualHeight)));
            FlashPixelProbe.WriteBitmap(
                Path.Combine(FlashDrawObservations.EvidenceFolder, evidence), width, height, pixels);
        }

        var (uniform, first) = Uniformity(pixels);
        return (FlashPixelProbe.CountOf(pixels, ImageColour), pixels.Length, uniform, first,
            captureHorizontal, captureVertical, fenceHeld);
    }

    /// <summary>The smallest clock that satisfies the presenter: this run shows one image, so
    /// nothing here ever needs to fire. Handles are real and disposable so HideAll's teardown is
    /// the real one.</summary>
    private sealed class EndToEndClock : ISessionClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 18, 0, 0, 0, TimeSpan.Zero);

        public IDisposable Schedule(TimeSpan due, Action fire) => new Handle();

        private sealed class Handle : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
