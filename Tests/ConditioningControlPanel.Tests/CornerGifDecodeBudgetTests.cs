using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The corner-GIF program-day freeze (4 reporters in 5 days, survived #221 and #227).
///
/// <para><b>The bug this suite exists to prevent.</b> No program template sets a CornerGifPath, so
/// every program day that raises a corner GIF fell back to the built-in spiral - 2400x1600, 32
/// frames - and handed it to XamlAnimatedGif, which builds the render thread a WriteableBitmap at
/// the source's NATIVE size and lets WPF resample 3.84 MP down to a 70-300px overlay on EVERY
/// frame, forever, on a layered window. #221/#227 only made the resample filter cheaper. Two
/// things are locked down here: the corner path decodes to the size it draws at, and the oversize
/// warning threshold sits BELOW the default asset (the old 4 MP guard was 4% above the spiral, so
/// the log line meant to name this freeze never fired).</para>
/// </summary>
public class CornerGifDecodeBudgetTests
{
    private const int SpiralWidth = 2400;
    private const int SpiralHeight = 1600;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Asset(string name)
        => Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", name);

    private static long FrameBytes(System.Collections.Generic.List<System.Windows.Media.Imaging.BitmapSource> frames)
    {
        long bytes = 0;
        foreach (var f in frames) bytes += (long)f.PixelWidth * f.PixelHeight * 4;
        return bytes;
    }

    // =====================================================================================
    //  the decode cap (the fix)
    // =====================================================================================

    [Fact]
    public void FullscreenSpiralDecodesDownToTheCornerOverlaySize()
    {
        var path = Asset("spiral.gif");
        Assert.True(File.Exists(path), path);

        // 300 = the default CornerGifSize, i.e. what Presentation day 14 and Takeover day 28 draw.
        var decoded = AnimatedWebp.DecodeFrames(path, maxDim: 300, maxFrames: 48, maxMemoryMb: 24.0);
        Assert.NotNull(decoded);
        var (frames, _) = decoded!.Value;

        Assert.NotEmpty(frames);
        foreach (var frame in frames)
            Assert.True(Math.Max(frame.PixelWidth, frame.PixelHeight) <= 300,
                $"frame decoded at {frame.PixelWidth}x{frame.PixelHeight} - the corner overlay draws 300px");

        // Before: the render thread held (and resampled, every frame) the source at native size.
        long nativePerFrame = (long)SpiralWidth * SpiralHeight * 4;
        long after = FrameBytes(frames);
        Assert.True(after < nativePerFrame,
            $"the whole capped frame set ({after / 1048576.0:F1} MB) must cost less than ONE native frame ({nativePerFrame / 1048576.0:F1} MB)");
    }

    [Fact]
    public void SmallestProgramOverlaySizeDecodesSmaller()
    {
        // Kept days 19-28 and Firmware days 12-14 draw at 70px - the worst source:target ratio in
        // the app (2400 -> 70) and the one the old 4 MP guard also stayed silent about.
        var decoded = AnimatedWebp.DecodeFrames(Asset("spiral.gif"), maxDim: 70, maxFrames: 48, maxMemoryMb: 24.0);
        Assert.NotNull(decoded);
        var (frames, _) = decoded!.Value;

        foreach (var frame in frames)
            Assert.True(Math.Max(frame.PixelWidth, frame.PixelHeight) <= 70,
                $"frame decoded at {frame.PixelWidth}x{frame.PixelHeight} for a 70px overlay");
        Assert.True(FrameBytes(frames) < 2L * 1024 * 1024, "a 70px corner overlay must not retain megabytes of frames");
    }

    // =====================================================================================
    //  the default asset (why no program template needed a data edit)
    // =====================================================================================

    [Fact]
    public void CornerDefaultAssetShipsAtCornerSize()
    {
        var path = Asset("spiral_corner.gif");
        Assert.True(File.Exists(path), "the corner overlay's default art is missing: " + path);

        var decoded = AnimatedWebp.DecodeFrames(path, maxDim: 4096, maxFrames: 64, maxMemoryMb: 128.0);
        Assert.NotNull(decoded);
        var (frames, delay) = decoded!.Value;

        Assert.True(Math.Max(frames[0].PixelWidth, frames[0].PixelHeight) <= CornerGifMedia.MaxDecodeDimension,
            $"the corner asset is {frames[0].PixelWidth}x{frames[0].PixelHeight} - it is meant to be corner-sized art, not the fullscreen spiral");
        Assert.True(frames.Count > 1, "the corner asset must still animate");
        Assert.True(delay.TotalMilliseconds is >= 20 and <= 200, $"corner asset frame delay {delay.TotalMilliseconds}ms");
    }

    [Fact]
    public void CornerAssetIsPackagedAsAResource()
    {
        var csproj = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "ConditioningControlPanel.csproj"));
        Assert.Contains(@"<Resource Include=""Resources\spiral_corner.gif"" />", csproj);
    }

    // =====================================================================================
    //  the guard that never fired
    // =====================================================================================

    [Fact]
    public void OversizeGuardFiresOnTheAssetThatCausedTheFreeze()
    {
        long spiralPixels = (long)SpiralWidth * SpiralHeight;   // 3,840,000 - the old guard was 4,000,000
        Assert.True(CornerGifMedia.OversizeSourcePixels < spiralPixels,
            "the oversize warning must fire on the default fullscreen spiral, or corner-GIF hang reports stay silent about it");
    }
}
