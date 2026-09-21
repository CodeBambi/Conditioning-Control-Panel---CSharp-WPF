using System.Windows;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1239 part 1 - every game window on the shared WebView2 host went fullscreen on the
/// PRIMARY monitor, whatever screen the player had it on. The reporter's desk is the awkward class:
/// a 1440p primary with two 1080p sides, so the old maths got the position AND the size wrong.
/// </summary>
public class HostWindowBoundsTests
{
    private static readonly Rect Primary1440 = new(0, 0, 2560, 1440);
    private static readonly Rect RightHand1080 = new(2560, 0, 1920, 1080);

    [Fact]
    public void Fullscreen_OnASecondMonitor_KeepsThatMonitorsOrigin()
    {
        var r = HostWindowBounds.Fullscreen(RightHand1080, 1.0);

        // The bug in one assertion: this used to be 0,0 / 2560x1440.
        Assert.Equal(2560, r.Left);
        Assert.Equal(0, r.Top);
        Assert.Equal(1920, r.Width);
        Assert.Equal(1080, r.Height);
    }

    [Fact]
    public void Fullscreen_ConvertsPhysicalPixelsToDipsWithThatMonitorsScale()
    {
        var r = HostWindowBounds.Fullscreen(Primary1440, 1.25);

        Assert.Equal(2048, r.Width);
        Assert.Equal(1152, r.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AFailedDpiProbe_ReadsAsOneHundredPercent(double scale)
    {
        Assert.Equal(1.0, HostWindowBounds.SafeScale(scale));

        var r = HostWindowBounds.Fullscreen(RightHand1080, scale);
        Assert.Equal(1920, r.Width);
        Assert.Equal(1080, r.Height);
    }

    [Fact]
    public void Centred_PutsTheFrameInTheMiddleOfThatMonitor()
    {
        var r = HostWindowBounds.Centred(RightHand1080, 1.0, 1600, 900);

        Assert.Equal(2560 + 160, r.Left);
        Assert.Equal(90, r.Top);
        Assert.Equal(1600, r.Width);
        Assert.Equal(900, r.Height);
    }

    [Fact]
    public void Centred_ClampsADefaultSizedForABiggerMonitor()
    {
        // The host's default windowed size is 85% of the PRIMARY screen, so on the 1080p side it
        // asks for a frame wider than the monitor. It must not hang off the edge.
        double w = 2560 * 0.85, h = 1440 * 0.85;

        var r = HostWindowBounds.Centred(RightHand1080, 1.0, w, h);

        Assert.Equal(1920, r.Width);
        Assert.Equal(1080, r.Height);
        Assert.Equal(2560, r.Left);
        Assert.Equal(0, r.Top);
    }

    [Fact]
    public void Centred_OnAScaledMonitor_WorksInDips()
    {
        var r = HostWindowBounds.Centred(Primary1440, 2.0, 640, 480);

        Assert.Equal((1280 - 640) / 2, r.Left);
        Assert.Equal((720 - 480) / 2, r.Top);
    }

    [Fact]
    public void OwnWindowWins_ThenTheWindowThatLaunchedIt_ThenThePrimary()
    {
        // The fullscreen TOGGLE: the window is on screen and owns the answer.
        Assert.Equal(HostWindowBounds.ScreenChoice.Self,
            HostWindowBounds.ChooseScreen(ownWindowRealized: true, ownerUsable: true));
        Assert.Equal(HostWindowBounds.ScreenChoice.Self,
            HostWindowBounds.ChooseScreen(ownWindowRealized: true, ownerUsable: false));

        // Fullscreen at launch: the layout runs before Show, so the launcher / panel is the only
        // thing that knows which screen the player is looking at.
        Assert.Equal(HostWindowBounds.ScreenChoice.Owner,
            HostWindowBounds.ChooseScreen(ownWindowRealized: false, ownerUsable: true));

        // Nothing to follow: the historic behaviour, now the last resort rather than the rule.
        Assert.Equal(HostWindowBounds.ScreenChoice.Primary,
            HostWindowBounds.ChooseScreen(ownWindowRealized: false, ownerUsable: false));
    }
}
