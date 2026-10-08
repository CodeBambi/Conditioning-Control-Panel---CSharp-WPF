using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics.Core;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Haptics on this head through <see cref="CoreHaptics"/>, only against in-process fakes
/// (MockProviderV2, an Intiface address nobody listens on) - never a real toy.</summary>
public sealed class HapticsHeadTests
{
    private static void WithHaptics(bool premium, bool mock, Action<HapticService, ConcurrentQueue<string>> body)
    {
        var s = CoreSettings.Current.Haptics;
        s.EnsureV2Migrated();   // before the flags below: migration fans the legacy enum (Mock) out over them
        var old = (CoreHaptics.Service, CoreEntitlement.HasPremiumProvider, MockProviderV2.Toast, s.Enabled, s.ButtplugUrl, s.V2.MasterCap,
            s.V2.Provider("mock").Enabled, s.V2.Provider("buttplug").Enabled, s.V2.Provider("lovense").Enabled);
        var toasts = new ConcurrentQueue<string>();
        MockProviderV2.Toast = toasts.Enqueue;
        CoreEntitlement.HasPremiumProvider = () => premium;
        (s.Enabled, s.ButtplugUrl) = (false, "ws://127.0.0.1:1");
        s.V2.Provider("mock").Enabled = mock;
        s.V2.Provider("buttplug").Enabled = !mock;
        s.V2.Provider("lovense").Enabled = false;
        var haptics = new HapticService(s);
        CoreHaptics.Service = haptics;
        try { body(haptics, toasts); }
        finally
        {
            haptics.PanicStop();
            haptics.Dispose();
            (CoreHaptics.Service, CoreEntitlement.HasPremiumProvider, MockProviderV2.Toast, s.Enabled, s.ButtplugUrl, s.V2.MasterCap) =
                (old.Service, old.HasPremiumProvider, old.Toast, old.Item4, old.ButtplugUrl, old.MasterCap);
            (s.V2.Provider("mock").Enabled, s.V2.Provider("buttplug").Enabled, s.V2.Provider("lovense").Enabled) = (old.Item7, old.Item8, old.Item9);
        }
    }

    private static bool Wait(Func<bool> done, int ms = 3000)
    {
        for (var t = 0; t < ms && !done(); t += 50) Thread.Sleep(50);
        return done();
    }

    /// <summary>WPF BubbleService.cs:1089 and SubliminalService TriggerSubliminalWithHapticPattern :579:
    /// a user pop and a silent subliminal each reach the (virtual) toy.</summary>
    [Fact]
    public void BubblePopAndSilentSubliminalDriveTheToy() => AvaloniaTestDispatcher.Run(() =>
    {
        foreach (var fire in new Action[]
        {
            () => BubbleOverlay.Pop(new AmbientBubble { Clickable = true }),
            () => SubliminalWhisperShow.HapticThenDraw("Relax"),
        })
        {
            WithHaptics(premium: true, mock: true, (h, toasts) =>
            {
                h.Settings.Enabled = true;
                Assert.True(h.ConnectAsync().Result);
                var draw = SubliminalWhisperShow.Draw;
                SubliminalWhisperShow.Draw = _ => { };
                try { fire(); } finally { SubliminalWhisperShow.Draw = draw; }
                Assert.True(Wait(() => toasts.Any(t => t.Contains('%'))), string.Join(" | ", toasts));
            });
        }
    });

    /// <summary>WPF PanicStopEverySurface (MainWindow.xaml.cs:1992): the panic key and the tray's
    /// Stop everything both send the (virtual) toy ALL STOP while it is vibrating.</summary>
    [Fact]
    public void PanicKeyAndTrayStopZeroTheToy() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (enabled, key) = (s.PanicKeyEnabled, s.PanicKey);
        (s.PanicKeyEnabled, s.PanicKey) = (true, "F8");
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        shell.Show();
        try
        {
            foreach (var stop in new Action[]
            {
                () => shell.HandlePanicKeyPress(new DateTime(2026, 1, 1)),
                ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.StopEverything,
            })
            {
                WithHaptics(premium: true, mock: true, (h, toasts) =>
                {
                    h.Settings.Enabled = true;
                    Assert.True(h.ConnectAsync().Result);
                    h.SetLayer(ConditioningControlPanel.Services.Haptics.Core.HapticLayer.Manual, 0.8);
                    Assert.True(Wait(() => toasts.Any(t => t.Contains('%'))), string.Join(" | ", toasts));
                    toasts.Clear();
                    stop();
                    Assert.True(Wait(() => toasts.Any(t => t.Contains("ALL STOP")), 1000), string.Join(" | ", toasts));
                });
            }
        }
        finally { shell.Close(); (s.PanicKeyEnabled, s.PanicKey) = (enabled, key); }
    });

    /// <summary>WPF MainWindow.Haptics.cs InitializeHapticsTab/RefreshHapticToys/Phase F handlers: the page
    /// shows the real (virtual) toys, provider chips and routing rows, follows DevicesChanged, writes the
    /// dials through to settings, gates the sync card on the audio-sync row, and ticks its live status
    /// only while visible (P01).</summary>
    [Fact]
    public void TabShowsRealToysRowsAndWritesDialsThrough()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            WithHaptics(premium: true, mock: true, (h, _) =>
            {
                var s = h.Settings;
                var a = s.AudioSync;
                var old = (s.V2.Temperament, s.DtrhDensity, a.Enabled, s.V2.Rule(HapticLayer.AudioSync).Enabled);
                var oldDsp = (a.Sensitivity, a.Smoothing, a.BassWeight, a.RmsWeight, a.OnsetWeight, a.MaxIntensity, a.LiveIntensity);
                var mirrored = 0;
                Action bump = () => mirrored++;
                ConditioningControlPanel.Avalonia.Views.Controls.AppSettings.AudioSettingsBinder.Changed += bump;
                s.Enabled = true;
                Assert.True(h.ConnectAsync().Result);
                var tab = new HapticsTabView();
                var host = new Window { Content = tab };
                host.Show();
                try
                {
                    // toys: one card per device, picker = All + each toy, mock chip lit
                    var cards = tab.ToyCardsList.ItemsSource!.Cast<ConditioningControlPanel.Views.Controls.HapticToyCardVm>().ToList();
                    Assert.Equal(h.DeviceManager.Devices.Select(d => d.DeviceKey), cards.Select(c => c.DeviceKey));
                    Assert.NotEmpty(cards);
                    Assert.False(tab.ToysEmptyState.IsVisible);
                    Assert.Equal(cards.Count + 1, tab.CmbPatternToy.Items.Count);
                    var chips = tab.ProviderChipsList.ItemsSource!.Cast<ConditioningControlPanel.Views.Controls.HapticProviderChipVm>().ToList();
                    Assert.True(chips.Single(c => c.Key == "mock").IsConnected);

                    // routing rows are the real four groups; the audio-sync row gates the tuning card
                    var groups = tab.RoutingGroupsList.ItemsSource!.Cast<ConditioningControlPanel.Views.Controls.HapticRoutingGroupVm>().ToList();
                    Assert.Equal(4, groups.Count);
                    var sync = groups[2].Rows[2];
                    sync.RowEnabled = false;
                    Assert.False(tab.VideoHapticSyncSliders.IsVisible);
                    sync.RowEnabled = true;
                    Assert.True(a.Enabled);
                    Assert.True(tab.VideoHapticSyncSliders.IsVisible);

                    // dials write through; reset restores the model's defaults
                    tab.SliderDspBass.Value = 33;
                    Assert.Equal(0.33, a.BassWeight, 3);
                    Assert.Equal("33%", tab.TxtDspBass.Text);
                    tab.BtnDspReset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(new AudioSyncSettings().BassWeight, a.BassWeight, 3);
                    // sync power writes through and tells Settings · Audio's twin slider (WPF :958 mirror)
                    mirrored = 0;
                    tab.SliderVideoHapticPower.Value = 42;
                    Assert.Equal(0.42, a.LiveIntensity, 3);
                    Assert.True(mirrored > 0);
                    tab.RbTemperCruel.IsChecked = true;
                    Assert.Equal("cruel", s.V2.Temperament);
                    tab.CmbHapticDtrhDensity.SelectedIndex = 2;
                    Assert.Equal(2, s.DtrhDensity);

                    // live status ticks only while the tab is visible
                    Assert.NotNull(tab.LiveStatusTimer);
                    tab.IsVisible = false;
                    Assert.Null(tab.LiveStatusTimer);
                    tab.IsVisible = true;
                    Assert.NotNull(tab.LiveStatusTimer);

                    // a disconnect behind the page (DevicesChanged) empties the cards
                    h.DisconnectAsync().Wait();
                    Dispatcher.UIThread.RunJobs();
                    Assert.Empty(tab.ToyCardsList.ItemsSource!.Cast<object>());
                    Assert.True(tab.ToysEmptyState.IsVisible);
                }
                finally
                {
                    host.Close();
                    (s.V2.Temperament, s.DtrhDensity, a.Enabled, s.V2.Rule(HapticLayer.AudioSync).Enabled) = old;
                    (a.Sensitivity, a.Smoothing, a.BassWeight, a.RmsWeight, a.OnsetWeight, a.MaxIntensity, a.LiveIntensity) = oldDsp;
                    ConditioningControlPanel.Avalonia.Views.Controls.AppSettings.AudioSettingsBinder.Changed -= bump;
                }
            });
        });
    }

    /// <summary>The page loads real settings, gates Enable behind premium as WPF (#1917), and a connect
    /// with Intiface not running ends "Disconnected" with the button usable again.</summary>
    [Fact]
    public void TabLoadsSettingsGatesEnableAndFailsConnectGracefully()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            WithHaptics(premium: false, mock: false, (h, _) =>
            {
                h.Settings.V2.MasterCap = 0.9;
                var tab = new HapticsTabView();
                var host = new Window { Content = tab };
                host.Show();
                try
                {
                    Assert.Equal(90, tab.SliderHapticMaxPower.Value);
                    Assert.True(tab.HapticMaxPowerWarning.IsVisible);
                    Assert.Equal("ws://127.0.0.1:1", tab.TxtHapticIntifaceUrl.Text);
                    Assert.False(tab.ChkHapticsEnabled.IsChecked);

                    tab.ChkHapticsEnabled.IsChecked = true;                 // no premium: refused
                    Dispatcher.UIThread.RunJobs();
                    Assert.False(tab.ChkHapticsEnabled.IsChecked);
                    Assert.False(h.Settings.Enabled);

                    CoreEntitlement.HasPremiumProvider = () => true;
                    tab.ChkHapticsEnabled.IsChecked = true;
                    Assert.True(h.Settings.Enabled);

                    tab.BtnHapticConnect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var deadline = DateTime.UtcNow.AddSeconds(30);
                    do { Dispatcher.UIThread.RunJobs(); Thread.Sleep(50); }
                    while (!tab.BtnHapticConnect.IsEnabled && DateTime.UtcNow < deadline);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(tab.BtnHapticConnect.IsEnabled);
                    Assert.False(h.IsConnected, $"{h.ProviderName} {h.DeviceManager.Devices.Count} {string.Join(",", h.DeviceManager.Providers.Select(p => p.Key + p.IsConnected))}");
                    Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("label_disconnected"), tab.TxtHapticStatus.Text);
                }
                finally
                {
                    foreach (var w in (Application.Current!.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Windows.ToArray() ?? Array.Empty<Window>())
                        if (w != host) w.Close();
                    host.Close();
                }
            });
        });
    }
}
