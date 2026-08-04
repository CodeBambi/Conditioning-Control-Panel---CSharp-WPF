using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Deeper-overlay-band depth counter that drives the above-video z-order. Bands nest (a region
/// can open a second band before the first closes), so it is a counter, not a flag. It must floor at
/// zero on an unmatched close and hard-reset so a leaked band can't strand overlays above later videos.
/// Constructs a real OverlayService — the counter methods are safe off the UI thread (their
/// Dispatcher kick is null-guarded when no WPF Application is running).
/// </summary>
public class DeeperBandCounterTests
{
    [Fact]
    public void FreshService_HasNoActiveBand()
    {
        var svc = new OverlayService();
        Assert.False(svc.DeeperOverlayBandActive);
    }

    [Fact]
    public void Begin_ActivatesAboveVideo()
    {
        var svc = new OverlayService();
        svc.BeginDeeperOverlayBand();
        Assert.True(svc.DeeperOverlayBandActive);
    }

    [Fact]
    public void NestedBands_StayActiveUntilAllClose()
    {
        var svc = new OverlayService();
        svc.BeginDeeperOverlayBand();
        svc.BeginDeeperOverlayBand();
        svc.EndDeeperOverlayBand();
        Assert.True(svc.DeeperOverlayBandActive);   // one band still open
        svc.EndDeeperOverlayBand();
        Assert.False(svc.DeeperOverlayBandActive);   // both closed
    }

    [Fact]
    public void UnmatchedEnd_FloorsAtZero_NotNegative()
    {
        var svc = new OverlayService();
        svc.EndDeeperOverlayBand();                   // close with nothing open
        Assert.False(svc.DeeperOverlayBandActive);
        // If the counter had gone to -1, a single Begin would land on 0 (still inactive).
        svc.BeginDeeperOverlayBand();
        Assert.True(svc.DeeperOverlayBandActive);
    }

    [Fact]
    public void Reset_ClearsAllOpenBands()
    {
        var svc = new OverlayService();
        svc.BeginDeeperOverlayBand();
        svc.BeginDeeperOverlayBand();
        svc.ResetDeeperOverlayBands();
        Assert.False(svc.DeeperOverlayBandActive);
    }
}
