using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// WS0 lot 3: single-display confinement (AppSettings.DualMonitorEnabled) is centralized in
/// <see cref="ScreenProviderExtensions.GetEffectScreens"/> and shared by the compositor
/// engine, the flash monitor pick, and the bubble engine spawn sites. WPF parity: every
/// effect surface confines to the primary monitor when the setting is off.
/// </summary>
public class ScreenSelectionTests
{
    private static readonly ScreenInfo Primary =
        new("primary", new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1400), 1.0);

    private static readonly ScreenInfo Secondary =
        new("secondary", new PixelRect(2560, 0, 1920, 1080), new PixelRect(2560, 0, 1920, 1040), 1.5);

    private sealed class FakeScreenProvider : IScreenProvider
    {
        public IReadOnlyList<ScreenInfo> Screens { get; set; } = Array.Empty<ScreenInfo>();
        public ScreenInfo? Primary { get; set; }

        public IReadOnlyList<ScreenInfo> GetAllScreens() => Screens;
        public ScreenInfo? GetPrimaryScreen() => Primary;
        public event EventHandler? ScreensChanged { add { } remove { } }
    }

    [Fact]
    public void DualMonitorOn_ReturnsAllScreens()
    {
        var provider = new FakeScreenProvider { Screens = new[] { Primary, Secondary }, Primary = Primary };

        var result = provider.GetEffectScreens(dualMonitorEnabled: true);

        Assert.Equal(new[] { Primary, Secondary }, result);
    }

    [Fact]
    public void DualMonitorOff_ReturnsPrimaryOnly()
    {
        var provider = new FakeScreenProvider { Screens = new[] { Primary, Secondary }, Primary = Primary };

        var result = provider.GetEffectScreens(dualMonitorEnabled: false);

        var screen = Assert.Single(result);
        Assert.Equal(Primary, screen);
    }

    [Fact]
    public void DualMonitorOff_NoPrimaryReported_FallsBackToFirstScreen()
    {
        var provider = new FakeScreenProvider { Screens = new[] { Secondary, Primary }, Primary = null };

        var result = provider.GetEffectScreens(dualMonitorEnabled: false);

        var screen = Assert.Single(result);
        Assert.Equal(Secondary, screen);
    }

    [Fact]
    public void NoScreens_ReturnsEmpty_BothModes()
    {
        var provider = new FakeScreenProvider();

        Assert.Empty(provider.GetEffectScreens(dualMonitorEnabled: true));
        Assert.Empty(provider.GetEffectScreens(dualMonitorEnabled: false));
    }

    [Fact]
    public void DualMonitorOff_SingleScreen_ReturnsThatScreen()
    {
        var provider = new FakeScreenProvider { Screens = new[] { Primary }, Primary = Primary };

        var result = provider.GetEffectScreens(dualMonitorEnabled: false);

        var screen = Assert.Single(result);
        Assert.Equal(Primary, screen);
    }
}
