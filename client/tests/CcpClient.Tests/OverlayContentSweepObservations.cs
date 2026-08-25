using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Overlay;

namespace CcpClient.Tests;

/// <summary>
/// One real surface, painted many times, with the capability asked after every paint WHAT it read
/// back out of the operating system.
///
/// <para><b>Why this run exists.</b> <see cref="Win32OverlayPresence.Paint"/> used to read the
/// WHOLE surface back on every frame. Measured on the running product at maximum settings, that is
/// 4.6 ms of the UI thread per frame at 2880x1800, unconditionally, on every moving surface. The
/// read-back is the port's central doctrine and is NOT removed: what changed is that a frame which
/// differs from the one before it in nothing but CONTENT re-reads one band of the surface instead
/// of all of it, and every event that could have changed the WINDOW — present, click-through,
/// resize, a re-asserted topmost band, a comparison that failed — sends the next paint back to the
/// whole surface. These are the facts that pin that rule to the OS's own answers rather than to a
/// flag this class sets.</para>
///
/// <para><b>What it cannot show.</b> A surface that takes a blit and does NOT hold it is not
/// injectable on a real window — the product blits and then reads the same window back, and on a
/// healthy machine the read agrees. So the "does not hold its frame, therefore withdraw" rule is
/// carried by the CALLER's facts against a refusing presence
/// (<c>SpiralSurfacePresenterTests.cs:362</c>, <c>PinkFilterSurfacePresenterTests.cs:178</c>),
/// which this change does not touch, and by the comparison being the same comparison — restricted
/// to a region — that it always was.</para>
/// </summary>
internal static class OverlayContentSweepObservations
{
    internal const int SurfaceWidth = 240;

    /// <summary>
    /// Tall enough that <see cref="Win32OverlayPresence.ContentBands"/> bands are more than one row
    /// each, and deliberately NOT a multiple of that count: 178 rows in 16 bands is 12 rows a band
    /// with a short last one, so a band height that rounds DOWN leaves the last two rows in no band
    /// at all and this run reads it. A height of 176 would have divided exactly and hidden it.
    /// </summary>
    internal const int SurfaceHeight = 178;

    private static readonly Lazy<Run> LazyRun = new(Measure, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static Run Measured => LazyRun.Value;

    /// <summary>
    /// Every paint's outcome and the words the capability used for what it read back, in order.
    /// </summary>
    /// <param name="MachineHasInteractiveDesktop">The machine property every claim is compared against.</param>
    /// <param name="PresentClaimedAvailable">Whether the surface reached the screen at all.</param>
    /// <param name="SweepProofs">One entry per paint on an unchanged surface, first paint first.</param>
    /// <param name="SweepPaintsAvailable">How many of those paints the OS confirmed.</param>
    /// <param name="ProofAfterReassert">What the paint after <see cref="IOverlayPresence.Reassert"/> read.</param>
    /// <param name="ProofAfterClickThrough">What the paint after a style change read.</param>
    /// <param name="ProofAfterRePresent">What the paint after a second Present read.</param>
    /// <param name="ProofBeforeAnyPaint">What the capability says before anything has been painted.</param>
    internal sealed record Run(
        bool MachineHasInteractiveDesktop,
        bool PresentClaimedAvailable,
        string[] SweepProofs,
        int SweepPaintsAvailable,
        string ProofAfterReassert,
        string ProofAfterClickThrough,
        string ProofAfterRePresent,
        string ProofBeforeAnyPaint,
        string FirstRefusal);

    /// <summary>Top-right of the primary display, deliberately clear of the rectangles the
    /// lifecycle run (centre) and the draw run (below-left of centre) already use: these suites
    /// share one desktop and a hit test at an overlapped centre point is another run's window.</summary>
    private static OverlayBounds Bounds
    {
        get
        {
            var (screenWidth, _) = OverlayWindowProbe.PrimarySize;
            return new OverlayBounds(Math.Max(0, screenWidth - SurfaceWidth - 24), 24, SurfaceWidth, SurfaceHeight);
        }
    }

    private static Run Measure()
    {
        var machine = OverlayWindowProbe.MachineHasInteractiveDesktop;
        var bounds = Bounds;
        var request = new OverlaySurfaceRequest(bounds, 1.0, ClickThrough: true);

        var presence = new Win32OverlayPresence();
        var proofBeforeAnyPaint = presence.LastContentProof;
        string? firstRefusal = null;
        void Note(CapabilityState state, string phase)
        {
            if (state is not CapabilityState.Available && firstRefusal is null)
            {
                firstRefusal = $"{phase}: {OverlayObservations.Describe(state)}";
            }
        }

        try
        {
            var present = presence.Present(request);
            Note(present, "present");

            // The sweep: one more paint than there are bands, so the band after the last one is
            // observed WRAPPING rather than assumed to.
            var proofs = new string[Win32OverlayPresence.ContentBands + 2];
            var available = 0;
            for (var i = 0; i < proofs.Length; i++)
            {
                // A DIFFERENT frame every time, so a build that skipped the blit entirely would
                // still have to explain why the read-back matches the frame it was handed.
                var state = presence.Paint(FrameOf((byte)(20 + (i * 7)), (byte)(200 - (i * 5)), 90));
                Note(state, $"paint #{i}");
                if (state is CapabilityState.Available)
                {
                    available++;
                }

                proofs[i] = presence.LastContentProof;
            }

            // The topmost band, re-asserted. The next paint owes the whole surface again.
            presence.Reassert();
            var afterReassert = presence.Paint(FrameOf(10, 10, 240));
            Note(afterReassert, "paint after reassert");
            var proofAfterReassert = presence.LastContentProof;

            // A style change. Its own outcome is NOT asserted on — a contended desktop can refuse
            // the hit test — but the latch it drops is, because that is this file's subject.
            presence.SetClickThrough(clickThrough: true);
            var afterClickThrough = presence.Paint(FrameOf(240, 10, 10));
            Note(afterClickThrough, "paint after click-through");
            var proofAfterClickThrough = presence.LastContentProof;

            presence.Present(request);
            var afterRePresent = presence.Paint(FrameOf(10, 240, 10));
            Note(afterRePresent, "paint after re-present");
            var proofAfterRePresent = presence.LastContentProof;

            presence.Withdraw();
            return new Run(
                machine,
                present is CapabilityState.Available,
                proofs,
                available,
                proofAfterReassert,
                proofAfterClickThrough,
                proofAfterRePresent,
                proofBeforeAnyPaint,
                firstRefusal ?? "none");
        }
        finally
        {
            presence.Dispose();
        }
    }

    /// <summary>Argument order is <see cref="OverlayFrame.Solid"/>'s own: blue, green, red.</summary>
    private static OverlayFrame FrameOf(byte blue, byte green, byte red) =>
        OverlayFrame.Solid(SurfaceWidth, SurfaceHeight, blue, green, red);
}
