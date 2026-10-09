using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF DoNotDisturbGuard parity: a listed foreground app suppresses only the kinds the user ticked.</summary>
public sealed class DoNotDisturbGuardTests
{
    [Fact]
    public void ListedForegroundSuppressesOnlyTickedKinds()
    {
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var fg = "obs64";
        DoNotDisturbGuard.ForegroundForTest = () => fg;
        try
        {
            var s = CoreSettings.Current;
            s.DndProcessList = new List<string> { "obs64", "zoom" };
            s.DndSuppressFlashes = true;
            s.DndSuppressVideos = false;
            Assert.True(DoNotDisturbGuard.ShouldSuppressFlashes());
            Assert.False(DoNotDisturbGuard.ShouldSuppressVideos());
            fg = "notepad";
            Assert.False(DoNotDisturbGuard.ShouldSuppressFlashes());
            fg = "";
            Assert.False(DoNotDisturbGuard.IsPrivilegedAppForeground());
            s.DndProcessList = new List<string>();
            fg = "obs64";
            Assert.False(DoNotDisturbGuard.ShouldSuppressFlashes());
        }
        finally { DoNotDisturbGuard.ForegroundForTest = null; CoreSettings.ServiceProvider = oldProvider; }
    }
}
