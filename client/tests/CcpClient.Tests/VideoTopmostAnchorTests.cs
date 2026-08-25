using CcpClient.Desktop.Overlay;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The below-video slot rule, as ARITHMETIC. Four inputs, four answers, no window and no desktop.
///
/// <para><b>Why the decision is a pure function at all.</b> Upstream's is
/// (<c>Services/Notifications/OverlayService.cs:2851-2860</c>, <c>ResolveZOrderAction</c>, extracted
/// there for the same reason and with the same comment: "so the ... rule can be unit-tested"). The
/// port's copy is <see cref="VideoTopmostAnchor.Resolve"/>, and what it buys is that the case
/// nobody thinks to drive — <b>no video playing</b> — has an ANSWER here rather than being a branch
/// that only real hardware ever reaches.</para>
///
/// <para><b>These facts deliberately touch no global state.</b> The live claim is process-wide, so
/// every fact that publishes one lives in <see cref="VideoTopmostScopeObservations"/>, inside the
/// serialized real-desktop collection. Nothing here can be perturbed by, or perturb, a run in
/// another collection.</para>
///
/// <para><b>What these do NOT prove.</b> That the window manager honours the slot. That is a
/// presentation claim and it is measured against the OS in
/// <see cref="VideoTopmostScopeObservations"/>.</para>
/// </summary>
public class VideoTopmostAnchorTests
{
    private const nint Surface = 0x1111;
    private const nint Video = 0x2222;

    [Fact]
    public void WithNoVideoUp_EverySelfRaiserStillTakesTheTopOfTheBand()
    {
        // The unchanged case, and the majority of every session: no anchor, so the answer is
        // HWND_TOPMOST and the re-assertion is byte-for-byte what it was before this rule existed.
        // Upstream reaches the same place — with hasVideo false, ResolveZOrderAction falls past the
        // video arm to PinTopmost (OverlayService.cs:2851-2859).
        Assert.Equal(VideoTopmostAnchor.TopOfBand, VideoTopmostAnchor.Resolve(0, Surface, anchorIsOnScreen: true));
        Assert.Equal(VideoTopmostAnchor.TopOfBand, VideoTopmostAnchor.Resolve(0, Surface, anchorIsOnScreen: false));

        // And HWND_TOPMOST is really -1, not some other sentinel: the value is what Win32 reads.
        Assert.Equal(-1, VideoTopmostAnchor.TopOfBand);
    }

    [Fact]
    public void AVideoThatIsUp_PutsTheSurfaceDirectlyBelowIt_NotOutOfTheBand()
    {
        // Upstream's PinBelowVideo is an INSERT-AFTER and not a demotion: SetWindowPos(hwnd,
        // videoHwnd, ...) keeps WS_EX_TOPMOST and the whole topmost band and changes only the slot
        // inside it (OverlayService.cs:2870-2874). The answer here is therefore the anchor's own
        // handle — never HWND_NOTOPMOST (-2), which would drop the surface under every ordinary
        // window on the desktop.
        Assert.Equal(Video, VideoTopmostAnchor.Resolve(Video, Surface, anchorIsOnScreen: true));
        Assert.NotEqual(-2, VideoTopmostAnchor.Resolve(Video, Surface, anchorIsOnScreen: true));
    }

    [Fact]
    public void AnAnchorTheOsNoLongerHolds_FallsThroughToTheTopOfTheBand()
    {
        // The stranding case. A video presence torn down without releasing — a crashed thread, a
        // dispose that never ran — would otherwise leave every yielding module inserting itself
        // after a dead handle forever. SetWindowPos with an hWndInsertAfter that is not a window
        // fails, and the surface would keep whatever slot it had with nothing ever correcting it.
        Assert.Equal(
            VideoTopmostAnchor.TopOfBand,
            VideoTopmostAnchor.Resolve(Video, Surface, anchorIsOnScreen: false));
    }

    [Fact]
    public void NothingEverPinsItselfBelowItself()
    {
        // Upstream's second condition, and it is a real one there: ReassertZOrder walks the
        // compositor hosts as well as the overlay windows and passes hwnd == videoHwnd, so
        // ResolveZOrderAction has to exclude the video window from its own rule
        // (OverlayService.cs:2851-2853). SetWindowPos(h, h, ...) is a no-op at best; a rule that
        // produced it would be a rule that could not be read.
        Assert.Equal(VideoTopmostAnchor.TopOfBand, VideoTopmostAnchor.Resolve(Video, Video, anchorIsOnScreen: true));
    }
}
