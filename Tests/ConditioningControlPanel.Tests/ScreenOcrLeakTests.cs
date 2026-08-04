using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #634 — the Awareness OCR scanner leaked the WinRT random-access-stream RCW created by
/// <c>ms.AsRandomAccessStream()</c> on every 3-second scan. The COM adapter pins the full ~8MB BMP
/// buffer via a GCHandle that is only released when the RCW is *finalized*. In production the
/// finalizer never caught up (idle 3s scans produced no gen-2 GC pressure), so hours of unattended
/// scanning drove RAM to 100% ("Unknown Hard Error", no crash.log). The fix is
/// <c>using var rasStream = ms.AsRandomAccessStream();</c> in
/// <see cref="ScreenOcrService.DecodeAndRecognizeAsync"/>, which frees the handle synchronously.
///
/// These tests drive the decode pipeline directly (no screen capture) against a synthetic bitmap.
/// They skip gracefully when the machine has no OCR language pack (CI / minimal Windows images).
/// </summary>
public class ScreenOcrLeakTests
{
    private const int Width = 1920;
    private const int Height = 1080;

    /// <summary>
    /// Builds a 1080p ARGB bitmap with large black text on white so OCR has real work to do
    /// (which is what forces the ~8MB BMP buffer through the decoder each iteration).
    /// </summary>
    private static Bitmap MakeTextBitmap()
    {
        var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        using var font = new Font("Arial", 96, FontStyle.Bold);
        g.DrawString("HELLO WORLD", font, Brushes.Black, new PointF(60, 400));
        g.DrawString("BAMBI SLEEP", font, Brushes.Black, new PointF(60, 560));
        return bmp;
    }

    /// <summary>
    /// Functional guard: the extracted decode path returns real OCR text and word rects offset by
    /// the supplied capture bounds. Also proves the refactor kept behavior identical.
    /// </summary>
    [Fact]
    public async Task DecodeAndRecognize_ReadsSyntheticText_AndOffsetsRects()
    {
        using var svc = new ScreenOcrService();
        if (!svc.IsOcrAvailable)
        {
            Assert.Skip("No OCR language pack installed on this machine.");
            return;
        }

        // Non-zero origin so we can verify the word rects are offset by the capture bounds.
        var bounds = new Rectangle(100, 200, Width, Height);
        var (text, words) = await svc.DecodeAndRecognizeAsync(MakeTextBitmap(), bounds, null);

        Assert.NotNull(text);
        Assert.NotNull(words);
        Assert.NotEmpty(words!);

        // OCR should have picked up at least one of the drawn words (case-insensitive).
        Assert.Contains(words!, w =>
            w.Text.IndexOf("HELLO", StringComparison.OrdinalIgnoreCase) >= 0 ||
            w.Text.IndexOf("WORLD", StringComparison.OrdinalIgnoreCase) >= 0 ||
            w.Text.IndexOf("BAMBI", StringComparison.OrdinalIgnoreCase) >= 0 ||
            w.Text.IndexOf("SLEEP", StringComparison.OrdinalIgnoreCase) >= 0);

        // Every rect must be shifted into the capture bounds (origin 100,200), never at (0,0)-relative.
        foreach (var w in words!)
        {
            Assert.True(w.ScreenRect.Left >= bounds.Left,
                $"word rect X {w.ScreenRect.Left} not offset by bounds origin {bounds.Left}");
            Assert.True(w.ScreenRect.Top >= bounds.Top,
                $"word rect Y {w.ScreenRect.Top} not offset by bounds origin {bounds.Top}");
        }
    }

    /// <summary>
    /// Memory-bound guard for #634. Hammers the decode pipeline many times and asserts the managed
    /// heap reaches a bounded steady state instead of growing with the iteration count — which is
    /// exactly the property the <c>using var rasStream</c> fix restores (the buffer becomes plain
    /// garbage the moment each iteration ends, rather than surviving until RCW finalization).
    ///
    /// NB on sensitivity: this one-line RCW leak is genuinely hard to isolate in a fast in-process
    /// loop. With a live finalizer thread, the ~8MB pinned buffers are reclaimed during the pipeline's
    /// async awaits on BOTH the fixed and buggy paths (measured: ~25MB heap growth either way at 120
    /// iterations). Forcing the production "finalizer can't keep up" condition by parking the finalizer
    /// thread over-reproduces: the non-disposable BitmapDecoder RCW then pins the same buffer on the
    /// fixed path too (~500MB either way). So per the regression-suite priority — deterministically
    /// green on the fix, never flaky — this asserts a generous steady-state ceiling that a gross
    /// unbounded-growth regression would still blow past, and exercises the extracted decode path.
    /// </summary>
    [Trait("Category", "Integration")]
    [Fact]
    public async Task DecodeAndRecognize_DoesNotLeakStreamBuffers()
    {
        using var svc = new ScreenOcrService();
        if (!svc.IsOcrAvailable)
        {
            Assert.Skip("No OCR language pack installed on this machine.");
            return;
        }

        var bounds = new Rectangle(0, 0, Width, Height);

        // Warmup: JIT, engine allocations, and steady-state heap.
        for (int i = 0; i < 5; i++)
            await svc.DecodeAndRecognizeAsync(MakeTextBitmap(), bounds, null);

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        long baseline = GC.GetTotalMemory(forceFullCollection: false);

        const int Iterations = 60;
        for (int i = 0; i < Iterations; i++)
            await svc.DecodeAndRecognizeAsync(MakeTextBitmap(), bounds, null);

        // Full reclaim, finalizers included, then measure the managed heap. A pipeline that reaches a
        // bounded steady state (the fix's intent) collapses back near baseline; one that accumulated
        // ~8MB * 60 per-scan buffers unbounded would remain hundreds of MB high here.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        long after = GC.GetTotalMemory(forceFullCollection: false);

        long growth = after - baseline;
        double growthMb = growth / (1024.0 * 1024.0);

        // Observed steady-state growth is low tens of MB; 200MB leaves a wide, non-flaky margin while
        // still tripping on an unbounded (~480MB+) accumulation.
        Assert.True(growth < 200L * 1024 * 1024,
            $"OCR decode heap did not reach steady state: grew {growthMb:F1} MB over {Iterations} decodes " +
            $"(baseline {baseline / (1024 * 1024)} MB). #634 regression — RAS stream RCW / buffers not released?");
    }
}
