using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
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
        { "PlayLockGaze", "PlayLockFocusGaze", "PlayLockRemote", "PlayLockLockdown", "PlayLockBlink", "PlayLockFyp", "PlayLockBreakout" };

    /// <summary>Decision "Entitlement lapse: startup write deferred": navigation/startup clears a lapsed
    /// flag in memory and leaves settings.json alone; the next entitlement event writes it.</summary>
    [Fact]
    public void LapsePass_ClearsInMemory_AndOnlyAnEntitlementEventWrites()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var oldSeam = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreSettings.ServiceProvider);
            // The shell is built on the suite's settings first: built under the private service below it
            // would carry that profile's mod into later tests.
            var shell = new MainShellWindow();
            shell.Show();
            var service = new ConditioningControlPanel.Services.SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var path = System.IO.Path.Combine(CorePaths.UserData, "settings.json");
            try
            {
                service.Current.AutonomyModeEnabled = true;
                // Opening Play notes it in the dashboard's RECENT rail, a real save (WPF
                // NoteDestinationOpened); already at the head, so this measures the lapse pass alone.
                service.Current.RailRecent.Insert(0, "tab.play");
                service.SaveImmediate();
                var before = System.IO.File.ReadAllText(path);
                (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = (() => false, null);

                shell.ShowTab("play");   // navigation: in memory only
                System.Threading.Thread.Sleep(900);   // past the 500 ms save debounce
                Assert.False(service.Current.AutonomyModeEnabled);
                Assert.Equal(before, System.IO.File.ReadAllText(path));

                shell.RefreshEntitlementVeils(persist: true);   // a tier/day/sign-in event
                // The write is a 500 ms thread-pool debounce; a fixed 900 ms raced it on a loaded runner.
                // Poll (bounded) the write time, a handle-free read, and open the file only when it moved:
                // a reader open on every tick can hold it against the writer's own publish retries on Windows.
                var written = "";
                var seen = System.IO.File.GetLastWriteTimeUtc(path);
                for (var deadline = DateTime.UtcNow.AddSeconds(10); DateTime.UtcNow < deadline; System.Threading.Thread.Sleep(50))
                {
                    var stamp = System.IO.File.GetLastWriteTimeUtc(path);
                    if (stamp == seen) continue;
                    try { written = System.IO.File.ReadAllText(path); } catch (System.IO.IOException) { continue; }
                    seen = stamp;
                    if (written.Contains("\"AutonomyModeEnabled\": false")) break;
                }
                Assert.Contains("\"AutonomyModeEnabled\": false", written);
            }
            finally
            {
                (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreSettings.ServiceProvider) = oldSeam;
                shell.Close();
            }
        });
    }

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
            var s = CoreSettings.Current;
            var oldFlags = (s.AutonomyModeEnabled, s.KeywordTriggersEnabled);
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
                // ...and stamps FREE TODAY over that card's sign only (WPF RefreshPlayFreeStamps).
                bool Stamped(string badge) => shell.Named<Control>("PlayTab")!.FindControl<TierBadge>(badge)!.FreeToday;
                Assert.True(Stamped("PlayBadgeRemote"));
                Assert.False(Stamped("PlayBadgeFyp"));

                // The other three pool keys lift their own veils (WPF RefreshEntitlementVeils).
                CoreEntitlement.IsFreeTodayProvider = k => k is "takeover" or "voice" or "haptics";
                Show("play");
                Assert.False(Veiled("BambiTakeoverTab", "BambiTakeoverGate"));
                Assert.False(Veiled("SheListeningTab", "SheListeningGate"));
                Assert.False(HapticsVeiled());
                Assert.True(Veiled("AwarenessTab", "AwarenessGate"));

                // An account change repaints without a navigation (WPF UpdatePatreonUI).
                CoreEntitlement.IsFreeTodayProvider = null;
                CoreEntitlement.HasPremiumProvider = () => true;
                shell.UpdateQuickLoginUI();
                Assert.False(Veiled("LockdownTab", "LockdownGate"));

                // Tier 1: every premium veil and band down, the Lab bands stay up.
                Show("play");
                foreach (var (t, g) in Veils) Assert.False(Veiled(t, g), g);
                Assert.False(HapticsVeiled());
                foreach (var b in new[] { "PlayLockRemote", "PlayLockLockdown", "PlayLockBlink", "PlayLockFyp" }) Assert.False(Banded(b), b);
                // An owner gets no gift: a free day on a door already open stamps nothing.
                CoreEntitlement.IsFreeTodayProvider = k => k == "remote";
                Show("play");
                Assert.False(Stamped("PlayBadgeRemote"));
                foreach (var b in new[] { "PlayLockGaze", "PlayLockFocusGaze", "PlayLockBreakout" }) Assert.True(Banded(b), b);

                // Tier 2: every band down.
                CoreEntitlement.HasLabProvider = () => true;
                Show("play");
                foreach (var b in Bands) Assert.False(Banded(b), b);

                // Lapse (WPF EnforceEntitlementLapse): a free account's premium flag goes off on the
                // next repaint, and the Awareness master box bounces (WPF MainWindow.Awareness.cs:395).
                (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider) = (() => false, null);
                s.AutonomyModeEnabled = true;
                shell.RefreshEntitlementVeils();
                Assert.False(s.AutonomyModeEnabled);
                s.KeywordTriggersEnabled = false;
                var master = shell.Named<Control>("AwarenessTab")!.FindControl<CheckBox>("ChkAwarenessMaster")!;
                master.IsChecked = true;
                Assert.False(master.IsChecked);
                Assert.False(s.KeywordTriggersEnabled);
            }
            finally
            {
                (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.IsFreeTodayProvider) = old;
                (s.AutonomyModeEnabled, s.KeywordTriggersEnabled) = oldFlags;
                shell.Close();
            }
        });
    }
}
