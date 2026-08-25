using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// A surface that has left the screen must not still be holding a monitor's worth of pixels.
///
/// <para><b>The defect these were written for.</b> <c>Withdraw</c> hid the window and nothing else:
/// the two DIB sections and two memory device contexts a paint allocates were released only from
/// <c>Dispose</c> and from <c>EnsureFrameSurfaces</c>'s own size-change path. Presences are pooled
/// and are never removed from the set (<c>Effects/OverlaySurfaceSet.cs:238-258</c>), so a session's
/// last flash stayed resident, per presence, for the rest of the session — and at the image-scale
/// dial's ceiling a flash frame is the whole monitor.</para>
///
/// <para><b>Why the count and not only the bytes.</b> GDI objects are a finite process-wide quota,
/// and a build that exhausts it stops being able to draw with no error a user could read. The count
/// is exact and the bytes are not, so the count carries the load-bearing assertion and the bytes
/// carry the size of what was at stake.</para>
///
/// <para><b>What these do not prove.</b> Nothing about Linux (there is no overlay backend there
/// yet), nothing about frame cadence, CPU or GPU, and nothing about what a human saw. They are one
/// process's own resource counters around a real place/withdraw cycle on a real desktop.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class OverlayFrameSurfaceRetentionTests : RealDesktopFacts
{
    private readonly ITestOutputHelper _output;

    /// <param name="output">The measurement goes into the run's own results, PASSING or failing.
    /// A performance claim is a number taken on a machine, and a number that only appears when the
    /// assertion fails cannot be compared with the next run's.</param>
    public OverlayFrameSurfaceRetentionTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Every GDI object a flash pool's worth of paints took is handed back when the surfaces leave
    /// the screen — at the scale dial's ceiling AND at its default, and while the presences are
    /// still alive and pooled for the next flash.
    ///
    /// <para>The second leg is what stops the first from being vacuous: if painting allocated
    /// nothing, "nothing was retained" would be true of a capability that draws nothing at all.</para>
    /// </summary>
    [Fact]
    public void WithdrawingASurface_HandsBackEveryGdiObjectItsPaintTook()
    {
        var machine = OverlayWindowProbe.MachineHasInteractiveDesktop;
        var all = OverlayFrameSurfaceObservations.DescribeAll();
        _output.WriteLine(all);

        foreach (var cycle in OverlayFrameSurfaceObservations.Cycles)
        {
            Assert.True(
                (cycle.Painted == OverlayFrameSurfaceObservations.PoolSize) == machine,
                $"this session has an interactive desktop = {machine}, and {cycle.Painted} of "
                + $"{OverlayFrameSurfaceObservations.PoolSize} surfaces were confirmed holding their frame. "
                + $"Nothing below is measuring what it claims to measure.\n{all}");

            Assert.True(
                (cycle.GdiTakenByPaint > 0) == machine,
                "painting a whole flash pool took no GDI objects at all, so 'they were handed back' would be "
                + $"true of a capability that allocates nothing.\n{all}");

            Assert.True(
                cycle.GdiRetainedAfterWithdraw == 0,
                $"scale {cycle.ScalePercent}% pass {cycle.Pass}: the paints took {cycle.GdiTakenByPaint} GDI "
                + $"objects and {cycle.GdiRetainedAfterWithdraw} of them were still held after every surface was "
                + "confirmed off screen. A pooled presence keeps its window for the next flash; it must not keep "
                + $"the last flash's pixels.\n{all}");
        }
    }

    /// <summary>
    /// The bytes half, and its own control.
    ///
    /// <para><b>The control is the point.</b> The same source image at the dial's ceiling covers the
    /// whole monitor and at its default covers 16 % of it, so the two arms must commit visibly
    /// different amounts of memory. An instrument that could not tell them apart could not tell a
    /// retention from a flat line either, and every number it produced would be noise.</para>
    ///
    /// <para>The give-back leg is expressed against what THIS run's paints committed rather than
    /// against a byte count computed the way the implementation computes one: a threshold derived
    /// from width x height x 4 x 2 would agree with the code by construction and would keep agreeing
    /// with it if both were wrong.</para>
    /// </summary>
    [Fact]
    public void TheMemoryTheFramesCommit_ScalesWithTheDial_AndComesBackWhenTheSurfacesLeave()
    {
        var machine = OverlayWindowProbe.MachineHasInteractiveDesktop;
        var all = OverlayFrameSurfaceObservations.DescribeAll();
        _output.WriteLine(all);

        var atCeiling = OverlayFrameSurfaceObservations.AtMonitorScale.Min(c => c.PrivateTakenByPaint);
        var atDefault = OverlayFrameSurfaceObservations.AtDefaultScale.Max(c => c.PrivateTakenByPaint);

        Assert.True(
            (atCeiling > atDefault * 2) == machine,
            $"the scale dial's ceiling committed {atCeiling} bytes of private memory per pool and its default "
            + $"committed {atDefault}, on a machine whose interactive-desktop state is {machine}. The two arms are "
            + "supposed to differ by the square of the dial, so an instrument that reads them as the same cannot "
            + $"see a retention either.\n{all}");

        foreach (var cycle in OverlayFrameSurfaceObservations.Cycles)
        {
            Assert.True(
                cycle.PrivateRetainedAfterWithdraw * 4 <= cycle.PrivateTakenByPaint,
                $"scale {cycle.ScalePercent}% pass {cycle.Pass}: the paints committed "
                + $"{cycle.PrivateTakenByPaint} bytes of private memory and {cycle.PrivateRetainedAfterWithdraw} "
                + "of them were still committed after every surface was confirmed off screen.\n" + all);
        }
    }
}
