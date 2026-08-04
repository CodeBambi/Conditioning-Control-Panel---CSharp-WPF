using ConditioningControlPanel.Services;
using Xunit;
using static ConditioningControlPanel.Services.OverlayService;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Deeper overlays invisible over the mandatory video (compositor-era regression). The z-order
/// reconciler pins every overlay just BELOW a playing video (#497 anti-bury). But a live Deeper
/// enhancement band IS the video's own effect and must sit ABOVE it — the pre-compositor behavior
/// that the shared always-on host broke. These tests lock both rules in place.
/// </summary>
public class OverlayZOrderTests
{
    [Fact]
    public void DeeperBandActive_AlwaysPinsAboveVideo()
    {
        // aboveVideo wins even when a video is present and the window already looks topmost.
        var action = ResolveZOrderAction(hasVideo: true, isVideoWindow: false, aboveVideo: true,
            needsPin: false, force: false);
        Assert.Equal(ZOrderAction.PinTopmost, action);
    }

    [Fact]
    public void NoBand_WithVideo_PinsBelowVideo_Preserving497()
    {
        // The regression guard: without a Deeper band, an overlay co-existing with a playing video
        // must still be pinned below it so the mandatory video is never buried (#497).
        var action = ResolveZOrderAction(hasVideo: true, isVideoWindow: false, aboveVideo: false,
            needsPin: true, force: false);
        Assert.Equal(ZOrderAction.PinBelowVideo, action);
    }

    [Fact]
    public void TheVideoWindowItself_IsNotPinnedBelowItself()
    {
        // hwnd == videoHwnd: the below-video branch is skipped; it just re-pins topmost if needed.
        var action = ResolveZOrderAction(hasVideo: true, isVideoWindow: true, aboveVideo: false,
            needsPin: true, force: false);
        Assert.Equal(ZOrderAction.PinTopmost, action);
    }

    [Fact]
    public void NoVideo_NeedsPin_PinsTopmost()
    {
        var action = ResolveZOrderAction(hasVideo: false, isVideoWindow: false, aboveVideo: false,
            needsPin: true, force: false);
        Assert.Equal(ZOrderAction.PinTopmost, action);
    }

    [Fact]
    public void NoVideo_Force_PinsTopmost()
    {
        var action = ResolveZOrderAction(hasVideo: false, isVideoWindow: false, aboveVideo: false,
            needsPin: false, force: true);
        Assert.Equal(ZOrderAction.PinTopmost, action);
    }

    [Fact]
    public void NoVideo_AlreadyTopmost_NoForce_IsNoOp()
    {
        var action = ResolveZOrderAction(hasVideo: false, isVideoWindow: false, aboveVideo: false,
            needsPin: false, force: false);
        Assert.Equal(ZOrderAction.None, action);
    }

    [Fact]
    public void BandAboveVideo_BeatsTheBelowVideoRule()
    {
        // Same inputs that would otherwise pin below the video, but the band flips it above.
        var below = ResolveZOrderAction(hasVideo: true, isVideoWindow: false, aboveVideo: false,
            needsPin: true, force: false);
        var above = ResolveZOrderAction(hasVideo: true, isVideoWindow: false, aboveVideo: true,
            needsPin: true, force: false);
        Assert.Equal(ZOrderAction.PinBelowVideo, below);
        Assert.Equal(ZOrderAction.PinTopmost, above);
    }

    // ---------------------------------------------------------------------------------------
    // #776 — the detached avatar tube yields to the pink tint.
    //
    // Two independent self-raising topmost windows were fighting: the tube re-pinned itself to the
    // FRONT of the topmost band every 500ms, the compositor host (which renders the pink tint) every
    // 5s. Last raiser won, so the tint blinked on and off the companion. The product decision is that
    // the tint wins, so the tube resolves to PinBelowCompositorHost and inserts directly under the
    // host instead — still topmost, still above ordinary app windows.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TubeYieldsToCompositorHost_PinsBelowIt_NotTopmost()
    {
        // The tube always asks unconditionally (needsPin + force), exactly as before the fix — the
        // yield flag, not a lost WS_EX_TOPMOST, is what redirects it.
        var action = ResolveZOrderAction(hasVideo: false, isVideoWindow: false, aboveVideo: false,
            needsPin: true, force: true, yieldToCompositorHost: true);
        Assert.Equal(ZOrderAction.PinBelowCompositorHost, action);
    }

    [Fact]
    public void TubeWithNoHostOnItsMonitor_StillPinsTopmost()
    {
        // No visible host over the tube (hidden after the idle grace, or a monitor the compositor
        // isn't covering): the widget keeps its original front-of-the-topmost-band raise.
        var action = ResolveZOrderAction(hasVideo: false, isVideoWindow: false, aboveVideo: false,
            needsPin: true, force: true, yieldToCompositorHost: false);
        Assert.Equal(ZOrderAction.PinTopmost, action);
    }

    [Fact]
    public void YieldFlagDefaultsOff_SoOverlayWindowsAreUnaffected()
    {
        // Every existing caller omits the parameter; the legacy 5-arg decisions must be identical.
        Assert.Equal(
            ResolveZOrderAction(hasVideo: false, isVideoWindow: false, aboveVideo: false, needsPin: false, force: false),
            ResolveZOrderAction(hasVideo: false, isVideoWindow: false, aboveVideo: false, needsPin: false, force: false, yieldToCompositorHost: false));
        Assert.Equal(ZOrderAction.None,
            ResolveZOrderAction(hasVideo: false, isVideoWindow: false, aboveVideo: false, needsPin: false, force: false));
    }

    [Fact]
    public void VideoRuleOutranksTheYield_So497CannotBeTradedAway()
    {
        // A window that both co-exists with a playing video and asks to yield must still land BELOW
        // the video: burying the mandatory video is the one outcome that is never acceptable (#497).
        var action = ResolveZOrderAction(hasVideo: true, isVideoWindow: false, aboveVideo: false,
            needsPin: true, force: true, yieldToCompositorHost: true);
        Assert.Equal(ZOrderAction.PinBelowVideo, action);
    }

    [Fact]
    public void YieldDoesNotOutrankADeeperBandAboveVideo()
    {
        // aboveVideo means the overlay IS the enhanced video's effect. It is a compositor layer, so it
        // never yields; if a caller ever passed both, the band must not be demoted under the host.
        var action = ResolveZOrderAction(hasVideo: true, isVideoWindow: false, aboveVideo: true,
            needsPin: false, force: false, yieldToCompositorHost: false);
        Assert.Equal(ZOrderAction.PinTopmost, action);
    }

    [Fact]
    public void YieldIsIndependentOfNeedsPinAndForce()
    {
        // Whatever the tube's current WS_EX_TOPMOST state, a visible host over it means "go below".
        foreach (var needsPin in new[] { false, true })
            foreach (var force in new[] { false, true })
                Assert.Equal(ZOrderAction.PinBelowCompositorHost,
                    ResolveZOrderAction(hasVideo: false, isVideoWindow: false, aboveVideo: false,
                        needsPin: needsPin, force: force, yieldToCompositorHost: true));
    }
}
