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
}
