using ConditioningControlPanel.Avalonia.Views.Games.BackRoom;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Owner, 10 Oct 2026: the Back Room's full-screen overlays and the gif cascade respect the
/// single-screen setting, as WPF. Dual-monitor on: every screen. Off: only the screen the room window
/// is on (the primary when that cannot be told).</summary>
public sealed class RoomOverlayScreensTests
{
    [Theory]
    [InlineData(3, true, 1, 0, new[] { 0, 1, 2 })]    // dual on: every screen
    [InlineData(3, false, 1, 0, new[] { 1 })]         // dual off: the room's screen, not the primary
    [InlineData(3, false, 2, 0, new[] { 2 })]
    [InlineData(3, false, -1, 1, new[] { 1 })]        // room unknown: the primary
    [InlineData(3, false, 7, 1, new[] { 1 })]         // a stale index is unknown
    [InlineData(2, false, -1, -1, new[] { 0 })]       // nothing known: the first
    [InlineData(1, false, 0, 0, new[] { 0 })]
    [InlineData(1, true, 0, 0, new[] { 0 })]
    [InlineData(0, true, -1, -1, new int[0])]
    public void TheRoomCoversEveryScreenOnlyWithDualMonitorOn(int count, bool dual, int room, int primary, int[] want) =>
        Assert.Equal(want, BackRoomOverlays.RoomScreenIndices(count, dual, room, primary));
}
