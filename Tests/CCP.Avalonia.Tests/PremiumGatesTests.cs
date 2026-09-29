using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF RefreshEntitlementVeils + RefreshPlayCards on this head: flipping the CoreEntitlement
/// seam lifts every tab veil and Play lockband, and the ? box's free day lifts only its own.</summary>
public sealed class PremiumGatesTests
{
    private static readonly (string Tab, string Gate)[] Veils =
    {
        ("BambiTakeoverTab", "BambiTakeoverGate"), ("RemoteControlTab", "RemoteControlGate"),
        ("AwarenessTab", "AwarenessGate"), ("LockdownTab", "LockdownGate"), ("SheListeningTab", "SheListeningGate"),
    };

    private static readonly string[] Bands =
        { "PlayLockGaze", "PlayLockFocusGaze", "PlayLockRemote", "PlayLockLockdown", "PlayLockBlink", "PlayLockFyp", "PlayLockDtrh", "PlayLockArcademy" };

    [Fact]
    public void FlippingTheSeam_UnlocksEveryVeilAndLockband()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var old = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.IsFreeTodayProvider);
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                bool Veiled(string tab, string gate) => shell.Named<Control>(tab)!.FindControl<Control>(gate)!.IsVisible;
                bool HapticsVeiled() => shell.StudioRack!.HapticsPanel.FindControl<Control>("HapticsGate")!.IsVisible;
                bool Banded(string band) => shell.Named<Control>("PlayTab")!.FindControl<Control>(band)!.IsVisible;
                void Show(string tab) { shell.ShowTab(tab); Dispatcher.UIThread.RunJobs(); }

                // Free: every veil and band up.
                (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.IsFreeTodayProvider) = (null, null, null);
                Show("play");
                foreach (var (t, g) in Veils) Assert.True(Veiled(t, g), g);
                Assert.True(HapticsVeiled());
                foreach (var b in Bands) Assert.True(Banded(b), b);

                // The free day lifts its own veil and band, nothing else.
                CoreEntitlement.IsFreeTodayProvider = k => k == "awareness" || k == "remote";
                Show("awareness");
                Assert.False(Veiled("AwarenessTab", "AwarenessGate"));
                Assert.False(Veiled("RemoteControlTab", "RemoteControlGate"));
                Assert.False(Banded("PlayLockRemote"));
                Assert.True(Veiled("LockdownTab", "LockdownGate"));
                Assert.True(Banded("PlayLockFyp"));

                // Tier 1: every premium veil and band down, the Lab bands stay up.
                CoreEntitlement.IsFreeTodayProvider = null;
                CoreEntitlement.HasPremiumProvider = () => true;
                Show("play");
                foreach (var (t, g) in Veils) Assert.False(Veiled(t, g), g);
                Assert.False(HapticsVeiled());
                foreach (var b in new[] { "PlayLockRemote", "PlayLockLockdown", "PlayLockBlink", "PlayLockFyp" }) Assert.False(Banded(b), b);
                foreach (var b in new[] { "PlayLockGaze", "PlayLockFocusGaze", "PlayLockDtrh", "PlayLockArcademy" }) Assert.True(Banded(b), b);

                // Tier 2: every band down.
                CoreEntitlement.HasLabProvider = () => true;
                Show("play");
                foreach (var b in Bands) Assert.False(Banded(b), b);
            }
            finally
            {
                (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.IsFreeTodayProvider) = old;
                shell.Close();
            }
        });
    }
}
