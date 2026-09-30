using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics.Core;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// HapticService in Core, driven only through in-process fakes: MockProviderV2 (virtual toys) and
/// a Buttplug address nobody listens on. Never real hardware.
/// </summary>
public sealed class HapticsCoreTests
{
    private static HapticSettings Settings(bool mock, bool buttplug, string url = "ws://127.0.0.1:1")
    {
        var s = new HapticSettings { Enabled = true, ButtplugUrl = url };
        s.EnsureV2Migrated();
        s.V2.Provider("lovense").Enabled = false;
        s.V2.Provider("mock").Enabled = mock;
        s.V2.Provider("buttplug").Enabled = buttplug;
        return s;
    }

    /// <summary>WPF HapticMixer.IsGateOpen: premium OR the "haptics" free day, and the master toggle.</summary>
    [Fact]
    public void GateFollowsPremiumAndTheHapticsFreeDay()
    {
        var old = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
        using var haptics = new HapticService(Settings(mock: true, buttplug: false));
        try
        {
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            Assert.False(haptics.Mixer.IsGateOpen);
            CoreEntitlement.IsFreeTodayProvider = key => key == "haptics";
            Assert.True(haptics.Mixer.IsGateOpen);
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            CoreEntitlement.HasPremiumProvider = () => true;
            Assert.True(haptics.Mixer.IsGateOpen);
            haptics.Settings.Enabled = false;
            Assert.False(haptics.Mixer.IsGateOpen);
        }
        finally { (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = old; }
    }

    /// <summary>A bubble pop reaches the (virtual) toy through the mixer; with the gate shut it does not.</summary>
    [Fact]
    public async Task BubblePopDrivesTheMockToyOnlyThroughTheGate()
    {
        var old = (CoreEntitlement.HasPremiumProvider, MockProviderV2.Toast);
        var toasts = new ConcurrentQueue<string>();
        MockProviderV2.Toast = toasts.Enqueue;
        using var haptics = new HapticService(Settings(mock: true, buttplug: false));
        try
        {
            CoreEntitlement.HasPremiumProvider = () => false;
            Assert.True(await haptics.ConnectAsync());
            toasts.Clear();
            _ = haptics.BubblePopAsync();
            await Task.Delay(600);
            Assert.DoesNotContain(toasts, t => t.Contains('%'));

            CoreEntitlement.HasPremiumProvider = () => true;
            await Task.Delay(300);   // past the mock's toast throttle and the 2 s combo is irrelevant here
            _ = haptics.BubblePopAsync();
            for (var i = 0; i < 20 && !toasts.Any(t => t.Contains('%')); i++) await Task.Delay(100);
            Assert.Contains(toasts, t => t.Contains('%'));
        }
        finally
        {
            haptics.PanicStop();
            (CoreEntitlement.HasPremiumProvider, MockProviderV2.Toast) = old;
        }
    }

    /// <summary>Intiface not running: connect reports false, never throws, stays disconnected.</summary>
    [Fact]
    public async Task ConnectToAnAbsentIntifaceFailsGracefully()
    {
        using var haptics = new HapticService(Settings(mock: false, buttplug: true));
        var connect = haptics.ConnectAsync();
        Assert.Same(connect, await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(30))));
        Assert.False(await connect);
        Assert.False(haptics.IsConnected);
    }

    /// <summary>WPF App.AutoConnectHapticsAsync: never for the mock alone (the legacy default).</summary>
    [Fact]
    public async Task AutoConnectSkipsAMockOnlySetup()
    {
        var s = Settings(mock: true, buttplug: false);
        s.AutoConnect = true;
        using var haptics = new HapticService(s);
        await haptics.AutoConnectOnStartupAsync(delayMs: 0);
        Assert.False(haptics.IsConnected);
    }
}
