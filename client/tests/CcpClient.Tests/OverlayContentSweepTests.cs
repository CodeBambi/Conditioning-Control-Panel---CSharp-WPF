using System.Text.RegularExpressions;
using CcpClient.Desktop.Overlay;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The steady-state content check: what the operating system is asked for on a frame that changed
/// nothing but its pixels, and what it is asked for on a frame that followed something else.
///
/// <para><b>The rule these facts pin.</b> The read-back is not removed from any frame — every paint
/// still blits, then asks the OS for the surface's content BACK, then compares it against the frame
/// it was handed. What changed is the REGION: the whole surface after any event that could have
/// changed the window, one sweeping band otherwise. So a surface that stops holding what was drawn
/// into it is still caught — immediately for anything in the band being read, and within
/// <see cref="Win32OverlayPresence.ContentBands"/> frames for anything at all — and its caller
/// still takes it down (<c>Effects/OverlaySurfaceSet.cs:344-352</c>).</para>
///
/// <para><b>Every claim is compared against a MACHINE property</b>, the same discipline
/// <c>FlashDrawTests</c> uses: a session with no interactive desktop cannot present, so its paints
/// refuse and its proofs are empty, and that is an equality here rather than a skip.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class OverlayContentSweepTests : RealDesktopFacts
{
    private static readonly Regex BandProof = new(
        @"rows (\d+)-(\d+) of (\d+), band (\d+) of (\d+)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Why a machine with no interactive desktop gets a NotExecuted rather than a green fact.
    /// Every reading below comes from a REAL layered window that was presented and painted; with no
    /// desktop there is no window, every paint refuses, and each comparison would be a pair of
    /// empty strings agreeing with each other. That is the shape fact 7 of
    /// <c>RealDesktopCollectionGuardTests</c> exists to refuse, so the machine question is asked
    /// once, as a gate, and every reading after it is unconditional.
    /// </summary>
    private const string RefusalReason =
        "this session has no interactive desktop with a display on it, so no overlay surface can be presented "
        + "and nothing can be read back out of one";

    [Fact]
    public void ThePaintAfterAPresent_ReadsTheWHOLESurfaceBackAndSaysSo()
    {
        Assert.SkipUnless(OverlayWindowProbe.MachineHasInteractiveDesktop, RefusalReason);

        // The first frame on a surface the OS has just placed is exactly the event the read-back
        // was built for, and it is the one this change must not weaken: a flash, a subliminal and
        // the pink tint each paint ONCE, so for them nothing at all has changed.
        var run = OverlayContentSweepObservations.Measured;

        Assert.True(run.PresentClaimedAvailable, $"the surface never reached the screen: {run.FirstRefusal}");
        Assert.Contains("the WHOLE", run.SweepProofs[0], StringComparison.Ordinal);
        Assert.DoesNotContain("nothing has been painted", run.SweepProofs[0], StringComparison.Ordinal);
        Assert.Contains("nothing has been painted", run.ProofBeforeAnyPaint, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryLaterPaintReadsOneBandBack_AndTheSweepCoversEveryROWOfTheSurface()
    {
        Assert.SkipUnless(OverlayWindowProbe.MachineHasInteractiveDesktop, RefusalReason);

        // THE FACT THAT REPLACES "the whole surface, every frame". Not "a band was read" — that a
        // band was read says nothing about the rows nobody reads. The rows the capability itself
        // NAMES over one full sweep are unioned here, and they have to be every row there is.
        var run = OverlayContentSweepObservations.Measured;

        var covered = new bool[OverlayContentSweepObservations.SurfaceHeight];
        for (var i = 1; i <= Win32OverlayPresence.ContentBands; i++)
        {
            var match = BandProof.Match(run.SweepProofs[i]);
            if (!match.Success)
            {
                continue;
            }

            var from = int.Parse(match.Groups[1].Value);
            var to = int.Parse(match.Groups[2].Value);
            Assert.Equal(OverlayContentSweepObservations.SurfaceHeight, int.Parse(match.Groups[3].Value));
            for (var row = from; row <= to && row < covered.Length; row++)
            {
                covered[row] = true;
            }
        }

        var unread = Array.IndexOf(covered, false);
        Assert.True(
            unread < 0,
            $"row {unread} of {covered.Length} is never read back by any band of a full sweep. A row no band "
            + $"reads is a row a surface could stop holding forever. Proofs: {string.Join(" | ", run.SweepProofs)}");
    }

    [Fact]
    public void TheSweepWraps_SoAnUnchangedSurfaceIsReProvedEndToEndForever()
    {
        Assert.SkipUnless(OverlayWindowProbe.MachineHasInteractiveDesktop, RefusalReason);

        // One more paint than there are bands was taken, so the wrap is OBSERVED. Without it a
        // build whose cursor stopped at the last band would keep re-reading one band for the rest
        // of the session and every other fact here would still pass.
        var run = OverlayContentSweepObservations.Measured;

        var first = BandProof.Match(run.SweepProofs[1]);
        var wrapped = BandProof.Match(run.SweepProofs[1 + Win32OverlayPresence.ContentBands]);

        Assert.True(first.Success, $"the second paint did not read a band back: '{run.SweepProofs[1]}'");
        Assert.True(wrapped.Success, "the paint one full sweep later did not read a band back: "
            + $"'{run.SweepProofs[1 + Win32OverlayPresence.ContentBands]}'");
        Assert.Equal(first.Value, wrapped.Value);
    }

    [Fact]
    public void ARaisedTopmostBand_SendsTheNextPaintBackToTheWholeSurface()
    {
        Assert.SkipUnless(OverlayWindowProbe.MachineHasInteractiveDesktop, RefusalReason);

        // Reassert is one SetWindowPos into the topmost band and the caller drives it on a cadence
        // while anything is up (Effects/OverlaySurfaceSet.cs:466-473). It is the OS being asked to
        // move this window relative to every other one, which is not "nothing but content" — and
        // it is also what gives the steady state a bounded full re-proof rate with no timer of the
        // presence's own.
        var run = OverlayContentSweepObservations.Measured;

        Assert.Contains("the WHOLE", run.ProofAfterReassert, StringComparison.Ordinal);
    }

    [Fact]
    public void AStyleChangeOrARePresent_SendsTheNextPaintBackToTheWholeSurface()
    {
        Assert.SkipUnless(OverlayWindowProbe.MachineHasInteractiveDesktop, RefusalReason);

        // The two calls that write the window's style, geometry and alpha. Both land on the one
        // place the current request is recorded, which is why neither can be followed by a
        // band-only confirmation.
        var run = OverlayContentSweepObservations.Measured;

        Assert.Contains("the WHOLE", run.ProofAfterClickThrough, StringComparison.Ordinal);
        Assert.Contains("the WHOLE", run.ProofAfterRePresent, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPaintOfTheSweepIsStillConfirmedByTheOperatingSystem()
    {
        Assert.SkipUnless(OverlayWindowProbe.MachineHasInteractiveDesktop, RefusalReason);

        // The whole point of the change is that it is a smaller READ, not a skipped one. Every
        // paint in the sweep still ends in an Available the OS earned, or the machine has no
        // desktop and none of them do.
        var run = OverlayContentSweepObservations.Measured;

        Assert.Equal(run.SweepProofs.Length, run.SweepPaintsAvailable);
        Assert.DoesNotContain("nothing has been painted", run.SweepProofs[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(178)]
    [InlineData(1080)]
    [InlineData(1800)]
    [InlineData(1801)]
    public void TheBandArithmeticCoversEveryRow_AtEverySurfaceHeight(int height)
    {
        // The arithmetic on its own, with no window: a band height that ROUNDS DOWN leaves the last
        // rows of the surface in no band at all, and on a 1800-row monitor that is eight rows the
        // capability would never look at again.
        var bandHeight = Win32OverlayPresence.BandHeight(height);
        var covered = new bool[height];

        for (var band = 0; band < Win32OverlayPresence.ContentBands; band++)
        {
            var top = Math.Min(height - 1, band * bandHeight);
            var rows = Math.Min(bandHeight, height - top);
            for (var row = top; row < top + rows; row++)
            {
                covered[row] = true;
            }
        }

        Assert.DoesNotContain(false, covered);
        Assert.True(bandHeight >= 1, "a band of no rows reads nothing back on any frame");
    }
}
