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
/// enumeration, which is a thing under test in SP-099 and must not be an input here).</para>
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
    /// SP-107: how many pixels the screen read actually RETURNED for the measurement above.
    /// <c>CountOf</c> of an empty capture is 0, which is the same number as "the flash is not on the
    /// screen" — so without this the two are indistinguishable in the failure. A full-screen
    /// CAPTUREBLT allocates a ~20 MB DIB, and three concurrent floor runs are exactly when that
    /// allocation is most likely to fail. This field is diagnostic only; nothing is asserted about
    /// it and nothing is silenced by it.
    /// </param>
    /// <param name="DesktopUniformPixelsDuring">
    /// SP-107: how many of those returned pixels equal the first one. A read that came back UNIFORM
    /// is a blank or asleep display, which is a third verdict again — different from "the allocation
    /// failed" and from "the flash was not on the screen". Diagnostic only.
    /// </param>
    /// <param name="DesktopFirstPixelDuring">…and what that first pixel was. Diagnostic only.</param>
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
        int DesktopUniformPixelsDuring,
        uint DesktopFirstPixelDuring,
        bool TransparentHalfIsBlack,
        bool OpaqueHalfIsTheImage,
        bool MissingFileDecodes,
        bool CorruptFileDecodes);

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
            var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;
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
            var (during, sampledDuring, uniformDuring, firstDuring) = CountDesktopWithSampleSize(
                display, evidence: "desktop-with-a-real-flash.bmp", display);
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
                DesktopUniformPixelsDuring: uniformDuring,
                DesktopFirstPixelDuring: firstDuring,
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
    /// SP-107 diagnostic. A screen read that came back UNIFORM is a blank or asleep display, not a
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

    private static (int Count, int Sampled, int Uniform, uint First) CountDesktopWithSampleSize(
        OverlayBounds display, string? evidence = null, OverlayBounds? area = null)
    {
        var rect = area ?? display;
        var pixels = FlashPixelProbe.CaptureDesktop(rect.X, rect.Y, rect.Width, rect.Height);
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
        return (FlashPixelProbe.CountOf(pixels, ImageColour), pixels.Length, uniform, first);
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
