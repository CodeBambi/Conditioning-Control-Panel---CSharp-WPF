using Avalonia;
using CcpClient.Desktop.Features;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-013 Step 2: owner-monitor working-area capping math (pure functions, no display).
/// Screen.WorkingArea/Bounds are physical pixels; Scaling converts to DIP (12.1.0 XML docs).
/// Constants are WPF-parity (fixed 520x640, min 420x360), pending-owner.
/// </summary>
public class PopupPlacementTests
{
    [Theory]
    [InlineData(1040, 1.0, 640.0)] // 1080p working area: WPF fixed height wins (640 < 0.9*1040)
    [InlineData(1040, 1.5, 624.0)] // mixed scale: 0.9 * 1040/1.5 beats the WPF cap
    [InlineData(600, 1.0, 540.0)] // small area: the 0.9 fraction caps below 640
    public void CapHeightDip_CapsByWpfParityAndOwnerWorkingArea(double waHeightPx, double scaling, double expected)
        => Assert.Equal(expected, PopupPlacement.CapHeightDip(waHeightPx, scaling), precision: 6);

    [Theory]
    [InlineData(200, 1040, 1.0, 360.0)] // short content: compact but never below WPF MinHeight
    [InlineData(500, 1040, 1.0, 500.0)] // content-fit between floor and cap
    [InlineData(900, 1040, 1.0, 640.0)] // tall content: capped at WPF parity height
    [InlineData(900, 600, 1.0, 540.0)] // tall content on a small area: capped by the area
    [InlineData(900, 300, 1.0, 360.0)] // tiny area: MinHeight floor wins over the cap
    public void FitHeightDip_ShortCompact_TallCapped(double desired, double waHeightPx, double scaling, double expected)
        => Assert.Equal(expected, PopupPlacement.FitHeightDip(desired, waHeightPx, scaling), precision: 6);

    [Theory]
    [InlineData(1920, 1.0, 520.0)] // default width on a normal area
    [InlineData(3000, 1.0, 520.0)] // ultrawide: still the WPF default
    [InlineData(768, 1.5, 512.0)] // narrow mixed-scale area (WSLg 1.5x shape): clamp to area DIP
    [InlineData(450, 1.0, 450.0)] // below MinWidth only when the area itself is narrower... 450 < 520: clamp
    [InlineData(300, 1.0, 300.0)] // ...and below 420 only when the area is narrower than MinWidth
    public void FitWidthDip_ClampsToWorkingArea(double waWidthPx, double scaling, double expected)
        => Assert.Equal(expected, PopupPlacement.FitWidthDip(waWidthPx, scaling), precision: 6);

    [Fact]
    public void CenteredClampedPosition_CentersOnOwner()
    {
        var owner = new PixelRect(100, 100, 520, 680);
        var wa = new PixelRect(0, 0, 1920, 1040);
        var pos = PopupPlacement.CenteredClampedPosition(owner, new PixelSize(520, 640), wa);
        Assert.Equal(new PixelPoint(100, 120), pos); // 100 + (680-640)/2
    }

    [Fact]
    public void CenteredClampedPosition_ClampsIntoWorkingAreaAtEdges()
    {
        var owner = new PixelRect(1700, 800, 520, 680);
        var wa = new PixelRect(0, 0, 1920, 1040);
        var pos = PopupPlacement.CenteredClampedPosition(owner, new PixelSize(520, 640), wa);
        Assert.Equal(new PixelPoint(1400, 400), pos); // right/bottom clamp
    }

    [Fact]
    public void CenteredClampedPosition_UsesOwnerMonitorNotPrimary()
    {
        // Secondary monitor at x-offset 1920; a primary-by-default implementation would
        // clamp into [0,1920) and fail this.
        var owner = new PixelRect(2100, 100, 520, 680);
        var wa = new PixelRect(1920, 0, 1920, 1040);
        var pos = PopupPlacement.CenteredClampedPosition(owner, new PixelSize(520, 640), wa);
        Assert.Equal(new PixelPoint(2100, 120), pos);
        Assert.InRange(pos.X, wa.X, wa.Right - 520);
    }

    [Fact]
    public void CenteredClampedPosition_PopupLargerThanArea_PinsToOrigin()
    {
        var owner = new PixelRect(100, 100, 520, 680);
        var wa = new PixelRect(0, 0, 1920, 1040);
        var pos = PopupPlacement.CenteredClampedPosition(owner, new PixelSize(2500, 1200), wa);
        Assert.Equal(new PixelPoint(0, 0), pos);
    }
}
