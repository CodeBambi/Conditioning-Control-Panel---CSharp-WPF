using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using E = ConditioningControlPanel.Services.BrainDrainKeepClear.BandEntry;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// "Keep pictures clear": the blur window has to sit under the lowest topmost window CCP owns,
/// so flashes, videos, lock cards, subliminals and bubbles stay sharp above it.
/// </summary>
public class BrainDrainKeepClearTests
{
    private static readonly IntPtr Flash = new(1), Video = new(2), Drain = new(3), Other = new(4), Drain2 = new(5);

    [Fact]
    public void DefaultsOff() => Assert.False(new AppSettings().BrainDrainKeepPicturesClear);

    [Fact]
    public void BlurAboveOurWindows_SinksUnderTheLowestOfThem()
    {
        var band = new[] { new E(Drain, true, true), new E(Flash, true, false), new E(Other, false, false), new E(Video, true, false) };
        Assert.Equal(Video, BrainDrainKeepClear.AnchorIfNeeded(band));
    }

    [Fact]
    public void BlurAlreadyBelowEverythingOfOurs_NoMove()
    {
        var band = new[] { new E(Flash, true, false), new E(Video, true, false), new E(Drain, true, true), new E(Other, false, false) };
        Assert.Equal(IntPtr.Zero, BrainDrainKeepClear.AnchorIfNeeded(band));
    }

    [Fact]
    public void OneOfTwoBlurWindowsDriftedUp_StillMoves()
    {
        var band = new[] { new E(Drain2, true, true), new E(Flash, true, false), new E(Drain, true, true) };
        Assert.Equal(Flash, BrainDrainKeepClear.AnchorIfNeeded(band));
    }

    [Fact]
    public void OtherAppsDoNotCountAsOurs()
    {
        var band = new[] { new E(Drain, true, true), new E(Other, false, false) };
        Assert.Equal(IntPtr.Zero, BrainDrainKeepClear.AnchorIfNeeded(band));
    }

    [Fact]
    public void NoBlurInTheBand_NoMove()
    {
        var band = new[] { new E(Flash, true, false), new E(Video, true, false) };
        Assert.Equal(IntPtr.Zero, BrainDrainKeepClear.AnchorIfNeeded(band));
    }
}
