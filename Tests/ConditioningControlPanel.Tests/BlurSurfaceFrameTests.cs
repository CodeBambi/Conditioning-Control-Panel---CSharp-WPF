using System.Collections.Generic;
using Xunit;
using static ConditioningControlPanel.Services.VideoService;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #687 (the parked #632/#636 cluster) — the blurred-background surface froze the app because the
/// per-frame blit held the native buffer lock across WriteableBitmap.Lock(), and because a rebuild
/// posted for one frame geometry could land after a newer one and permanently mismatch the buffer.
/// The pure decision helpers behind the fix are exercised here; the threading itself is verified in
/// play-test.
/// </summary>
public class BlurSurfaceFrameTests
{
    // ---- staging buffer sizing ----

    [Fact]
    public void StagingBytes_AreFourPerPixel()
    {
        Assert.Equal(368 * 640 * 4, StagingBytesFor(368, 640));
        Assert.Equal(1080 * 608 * 4, StagingBytesFor(1080, 608));
    }

    [Fact]
    public void StagingBytes_ZeroGeometryIsRejected()
    {
        Assert.Equal(0, StagingBytesFor(0, 640));
        Assert.Equal(0, StagingBytesFor(368, 0));
    }

    [Fact]
    public void StagingBytes_OverflowIsRejectedRatherThanWrapped()
    {
        // A bogus format callback must make the blit SKIP the frame, not copy on a negative length.
        Assert.Equal(0, StagingBytesFor(uint.MaxValue, uint.MaxValue));
        Assert.Equal(0, StagingBytesFor(100000, 100000));
    }

    [Fact]
    public void StagingBuffer_GrowsOnResolutionChange_ThenStops()
    {
        // The observed #687 sequence: 368x640 then 368x642 mid-stream. Grow-only, so the second
        // (larger) frame reallocates once and a return to the smaller size does not.
        int len = 0;
        int allocations = 0;

        foreach (var (w, h) in new List<(uint, uint)> { (368, 640), (368, 642), (368, 640), (368, 642) })
        {
            int need = StagingBytesFor(w, h);
            if (len < need) { len = need; allocations++; }
        }

        Assert.Equal(2, allocations);              // 640 then 642; the repeats reuse
        Assert.Equal(368 * 642 * 4, len);
    }

    // ---- blur-fill snapshot geometry + cadence ----

    [Fact]
    public void SnapshotDims_DivideTheFrame()
    {
        var (w, h) = ComputeSnapshotDims((368, 640), 8);
        Assert.Equal(46, w);
        Assert.Equal(80, h);
    }

    [Fact]
    public void SnapshotDims_NeverDegenerateToZero()
    {
        var (w, h) = ComputeSnapshotDims((16, 16), 8);
        Assert.Equal(8, w);   // floored, not 2
        Assert.Equal(8, h);
    }

    [Fact]
    public void SnapshotDims_DivisorOfOneIsIdentity()
    {
        var (w, h) = ComputeSnapshotDims((640, 368), 1);
        Assert.Equal(640, w);
        Assert.Equal(368, h);
    }

    [Fact]
    public void SnapshotCadence_RefreshesFirstFrameThenEverySixth()
    {
        Assert.True(IsSnapshotFrame(0, 6));   // no blank fill while the first six frames arrive
        for (int i = 1; i < 6; i++)
            Assert.False(IsSnapshotFrame(i, 6));
        Assert.True(IsSnapshotFrame(6, 6));
        Assert.True(IsSnapshotFrame(12, 6));
    }

    [Fact]
    public void SnapshotCadence_EveryFrameWhenNIsOne()
    {
        Assert.True(IsSnapshotFrame(0, 1));
        Assert.True(IsSnapshotFrame(7, 1));
        Assert.True(IsSnapshotFrame(7, 0));
    }

    // ---- format-generation stamping ----

    [Fact]
    public void Rebuild_IsCurrentOnlyForTheNewestGeneration()
    {
        Assert.True(IsRebuildCurrent(3, 3));
        Assert.False(IsRebuildCurrent(2, 3));
    }

    [Fact]
    public void ThreeFormatCallbacksInARow_OnlyTheNewestRebuildApplies()
    {
        // The reported trace: three format callbacks within 15ms (368x640 -> 368x642). Whatever order
        // the posted rebuilds run in, exactly one - the newest - may swap the bitmap in. Before the
        // stamp, an older rebuild running last pinned the bitmap at a dead size and the dimension
        // guard then dropped every frame for the rest of the clip.
        int current = 0;
        var posted = new List<int>();
        for (int i = 0; i < 3; i++) posted.Add(++current);   // three callbacks

        var applied = new List<int>();
        foreach (var gen in new[] { posted[2], posted[0], posted[1] })   // deliberately out of order
            if (IsRebuildCurrent(gen, current)) applied.Add(gen);

        Assert.Single(applied);
        Assert.Equal(3, applied[0]);
    }

    [Fact]
    public void AFurtherFormatCallbackRetiresAnInFlightRebuild()
    {
        int current = 1;
        int postedGen = current;      // rebuild queued for generation 1
        current++;                    // a new format callback lands before it runs

        Assert.False(IsRebuildCurrent(postedGen, current));
    }

    // ---- #786: display aspect vs buffer aspect ----

    [Fact]
    public void DisplayAspect_SquarePixelsIsJustWidthOverHeight()
    {
        Assert.Equal(16.0 / 9.0, DisplayAspectFrom(1920, 1080, 1, 1, false), 5);
        Assert.Equal(640.0 / 368.0, DisplayAspectFrom(640, 368, 1, 1, false), 5);
    }

    [Fact]
    public void DisplayAspect_AppliesTheSampleAspectRatio()
    {
        // The #786 shape: an anamorphic clip whose coded frame is nothing like its display frame.
        // Reading the buffer's own pixel aspect (1.739) drew it far too wide.
        Assert.Equal(0.87, DisplayAspectFrom(640, 368, 1, 2, false), 2);
        // Classic DVD: 720x480 storage, 32:27 pixels -> 16:9 on screen.
        Assert.Equal(16.0 / 9.0, DisplayAspectFrom(720, 480, 32, 27, false), 3);
    }

    [Fact]
    public void DisplayAspect_ZeroSarIsTreatedAsSquare()
    {
        // libvlc reports 0:0 for "unspecified"; that must mean square pixels, not a divide by zero.
        Assert.Equal(640.0 / 368.0, DisplayAspectFrom(640, 368, 0, 0, false), 5);
    }

    [Fact]
    public void DisplayAspect_RotatedTracksAreTransposed()
    {
        // A 90-degree-rotated 1920x1080 phone clip presents as 1080x1920; LibVLC rotates the vmem
        // buffer but the track still reports the pre-rotation size.
        Assert.Equal(1080.0 / 1920.0, DisplayAspectFrom(1920, 1080, 1, 1, true), 5);
    }

    [Fact]
    public void DisplayAspect_GarbageIsRejectedSoTheBufferAspectWins()
    {
        Assert.Equal(0, DisplayAspectFrom(0, 368, 1, 1, false));
        Assert.Equal(0, DisplayAspectFrom(640, 0, 1, 1, false));
        Assert.Equal(0, DisplayAspectFrom(640, 368, 100, 1, false));   // 174:1, not a real clip
        Assert.Equal(0, DisplayAspectFrom(640, 368, 1, 100, false));   // 1:57
    }

    // ---- #786: the fit rect (letterbox / pillarbox, never stretch) ----

    [Fact]
    public void Fit_PillarboxesAClipNarrowerThanTheScreen()
    {
        var (w, h) = FitToAspect(1920, 1080, 9.0 / 16.0);
        Assert.Equal(1080, h, 3);            // height-limited
        Assert.Equal(607.5, w, 3);
        Assert.Equal(9.0 / 16.0, w / h, 5);  // aspect preserved exactly
    }

    [Fact]
    public void Fit_LetterboxesAClipWiderThanTheScreen()
    {
        var (w, h) = FitToAspect(1920, 1080, 2.39);
        Assert.Equal(1920, w, 3);            // width-limited
        Assert.Equal(1920 / 2.39, h, 3);
        Assert.True(h < 1080);
    }

    [Fact]
    public void Fit_NeverExceedsTheContainerAndNeverStretches()
    {
        foreach (var aspect in new[] { 0.5, 0.75, 1.0, 1.333, 1.739, 1.778, 2.35 })
        {
            var (w, h) = FitToAspect(1920, 1080, aspect);
            Assert.True(w <= 1920.001 && h <= 1080.001);
            Assert.Equal(aspect, w / h, 5);
        }
    }

    [Fact]
    public void Fit_NoContainerYetYieldsNothingRatherThanABogusRect()
    {
        Assert.Equal((0d, 0d), FitToAspect(0, 1080, 1.778));
        Assert.Equal((0d, 0d), FitToAspect(1920, 0, 1.778));
        Assert.Equal((0d, 0d), FitToAspect(1920, 1080, 0));
        Assert.Equal((0d, 0d), FitToAspect(1920, 1080, double.NaN));
    }

    // ---- #786: the blur-fill decision is a pure function of the two aspects ----

    [Fact]
    public void BlurFill_ArmsOnlyWhenBarsWouldShow()
    {
        double screen = 16.0 / 9.0;
        Assert.False(NeedsBlurFill(1.778, screen));           // exact match
        Assert.False(NeedsBlurFill(640.0 / 368.0, screen));   // 1.739, inside the 3% tolerance
        Assert.True(NeedsBlurFill(640.0 / 386.0, screen));    // 1.658, outside it
        Assert.True(NeedsBlurFill(9.0 / 16.0, screen));       // vertical clip
    }

    [Fact]
    public void BlurFill_UnknownAspectDoesNotArmTheGaussian()
    {
        Assert.False(NeedsBlurFill(0, 16.0 / 9.0));
        Assert.False(NeedsBlurFill(1.778, 0));
    }

    [Fact]
    public void RepeatedFormatCallbacks_EachOneFullyDeterminesTheLayout()
    {
        // The #786 trace: 640x368 four times, then 640x386 twice, for one playback. Because the fit
        // is recomputed from the LATEST dims every time, replaying the sequence in any order lands
        // the same rect for the same dims - no stale layout survives a callback.
        double screen = 16.0 / 9.0;
        var seen = new List<(double W, double H)>();
        foreach (var (vw, vh) in new List<(double, double)> { (640, 368), (640, 368), (640, 368), (640, 368), (640, 386), (640, 386) })
            seen.Add(FitToAspect(1920, 1080, vw / vh));

        Assert.Equal(seen[0], seen[3]);   // same dims -> identical layout, idempotent
        Assert.Equal(seen[4], seen[5]);
        Assert.NotEqual(seen[0], seen[4]);

        // And neither one is ever a stretch: both fit inside the screen at their own aspect.
        Assert.Equal(640.0 / 368.0, seen[0].W / seen[0].H, 5);
        Assert.Equal(640.0 / 386.0, seen[4].W / seen[4].H, 5);
        Assert.False(NeedsBlurFill(640.0 / 368.0, screen));
        Assert.True(NeedsBlurFill(640.0 / 386.0, screen));
    }
}
