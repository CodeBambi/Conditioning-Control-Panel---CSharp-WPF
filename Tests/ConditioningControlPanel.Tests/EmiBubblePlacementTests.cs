using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Emi's speech bubble stays inside the monitor work area (ticket 2026-09-24, the line ran off the
/// left screen edge with her docked there). All numbers are window-local DIPs.
/// </summary>
public class EmiBubblePlacementTests
{
    // A 600 DIP window, the bubble 300 wide, her-right position 250, her-left position 50.
    private const double Window = 600, Width = 300, RightLeft = 250, FlippedLeft = 50;

    [Fact]
    public void RoomEverywhere_KeepsTheDefaultRightSide()
    {
        var (left, flip) = EmiBubblePlacement.Resolve(RightLeft, FlippedLeft, Width, false, Window, -1000, 5000);
        Assert.False(flip);
        Assert.Equal(RightLeft, left);
    }

    [Fact]
    public void DockedAtTheLeftEdge_WithTheBookOnHerRight_StaysOnScreen()
    {
        // Work area starts 150 DIPs into the window: the flipped side would start off screen.
        var (left, flip) = EmiBubblePlacement.Resolve(RightLeft, FlippedLeft, Width, true, Window, 150, 5000);
        Assert.False(flip);
        Assert.True(left >= 150, $"bubble starts at {left}, left of the work area");
    }

    [Fact]
    public void RightSideSpills_FlipsToHerLeft()
    {
        var (left, flip) = EmiBubblePlacement.Resolve(RightLeft, FlippedLeft, Width, false, Window, -1000, 400);
        Assert.True(flip);
        Assert.True(left + Width <= 400);
    }

    [Fact]
    public void NeitherSideFits_ClampsAndTheStartOfTheLineWins()
    {
        // Work area only 200 wide: the bubble cannot fit, its left edge must still be on screen.
        var (left, _) = EmiBubblePlacement.Resolve(RightLeft, FlippedLeft, Width, false, Window, 120, 320);
        Assert.True(left >= 120);
    }

    [Fact]
    public void UnknownWorkArea_OnlyTheWindowClampApplies()
    {
        var (left, flip) = EmiBubblePlacement.Resolve(500, FlippedLeft, Width, false, Window,
            double.NegativeInfinity, double.PositiveInfinity);
        Assert.False(flip);
        Assert.Equal(Window - Width - EmiBubblePlacement.EdgeGap, left);
    }
}
