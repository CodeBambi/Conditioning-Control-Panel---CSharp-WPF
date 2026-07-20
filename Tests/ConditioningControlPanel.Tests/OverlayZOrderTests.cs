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
}
