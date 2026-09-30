using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Localization;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics;
using ConditioningControlPanel.Services.Haptics.Core;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The connection bar and the two global dials of the Haptics page, ported from WPF
    /// MainWindow.Haptics.cs (LoadHapticsSettingsToUi :396, RefreshHapticConnectionUi :277, the
    /// connect / panic / test handlers :546-810). Lives on the view, not the shell partial: this
    /// view calls InitializeComponent, so its x:Name fields are real (CLAUDE.md trap 7).
    /// Talks to <see cref="CoreHaptics.Service"/>; null (tests) = the bar reads "disconnected".
    /// </summary>
    public partial class HapticsTabView
    {
        /// <summary>True while settings are pushed into the controls: Avalonia raises the change
        /// events on a programmatic set too, and the markup default must never be saved (WPF _isLoading).</summary>
        private bool _loading = true;

        private static HapticSettings Cfg => CoreSettings.Current.Haptics;
        private static HapticService? Haptics => CoreHaptics.Service;

        private static Binding Str(string key) => (Binding)new StrExtension(key).ProvideValue(null!);
        private static void SetKey(TextBlock? tb, string key) => tb?.Bind(TextBlock.TextProperty, Str(key));
        private static void SetText(TextBlock? tb, string text) => tb?.Bind(TextBlock.TextProperty, new Binding { Source = text });

        /// <summary>WPF LoadHapticsSettingsToUi, for the controls this page wires today.</summary>
        internal void LoadHapticsSettingsToUi()
        {
            _loading = true;
            try
            {
                var s = Cfg;
                s.EnsureV2Migrated();
                ChkHapticsEnabled.IsChecked = s.Enabled;
                ChkHapticAutoConnect.IsChecked = s.AutoConnect;
                ChkHapticProviderLovense.IsChecked = s.V2.Provider("lovense").Enabled;
                ChkHapticProviderIntiface.IsChecked = s.V2.Provider("buttplug").Enabled;
                ChkHapticProviderMock.IsChecked = s.V2.Provider("mock").Enabled;
                TxtHapticUrl.Text = s.LovenseUrl ?? "";
                TxtHapticIntifaceUrl.Text = s.ButtplugUrl ?? "";

                var master = (int)Math.Round(Math.Clamp(s.GlobalIntensity, 0, 1) * 100);
                SliderHapticIntensity.Value = master;
                TxtHapticIntensity.Text = $"{master}%";
                var cap = (int)Math.Round(Math.Clamp(s.V2.MasterCap, 0.05, 1.0) * 100);
                SliderHapticMaxPower.Value = cap;
                TxtHapticMaxPower.Text = $"{cap}%";
                HapticMaxPowerWarning.IsVisible = cap > (int)Math.Round(HapticMixer.DefaultMasterCap * 100);
            }
            finally { _loading = false; }
            RefreshHapticConnectionUi();
        }

        /// <summary>WPF RefreshHapticConnectionUi: status text + dot, button label, device line.</summary>
        internal void RefreshHapticConnectionUi()
        {
            var haptics = Haptics;
            var connected = haptics?.IsConnected == true;
            SetKey(TxtHapticStatus, connected ? "label_connected" : "label_disconnected");
            var color = connected ? Color.FromRgb(0x00, 0xE6, 0x76) : Color.FromRgb(0xFF, 0x6B, 0x6B);
            TxtHapticStatus.Foreground = new SolidColorBrush(color);
            HapticStatusDot.Fill = new SolidColorBrush(color);
            SetKey(BtnHapticConnect.Content as TextBlock, connected ? "btn_disconnect" : "btn_connect");

            var count = haptics?.DeviceManager.Devices.Count ?? 0;
            if (count == 0) SetKey(TxtHapticDevices, "label_no_devices");
            else SetText(TxtHapticDevices, Loc.GetF("haptics_devices_merged", count, haptics?.ProviderName ?? ""));

            (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.SetHapticsStatusPulse(connected);
        }

        private void OnHapticConnectionChanged(object? sender, bool connected) =>
            Dispatcher.UIThread.Post(RefreshHapticConnectionUi);

        private void OnHapticActivity(object? sender, string message) =>
            Dispatcher.UIThread.Post(() => SetText(TxtHapticActivity, message ?? ""));

        private void HookHapticService(bool on)
        {
            if (Haptics is not { } h) return;
            h.ConnectionChanged -= OnHapticConnectionChanged;
            h.HapticTriggered -= OnHapticActivity;
            if (!on) return;
            h.ConnectionChanged += OnHapticConnectionChanged;
            h.HapticTriggered += OnHapticActivity;
        }

        /// <summary>WPF: HapticMixer.IsGateOpen ORs the "haptics" free day in, so the page asks the same.</summary>
        private static bool HasHapticsAccess => CoreEntitlement.HasPremium || CoreEntitlement.IsFreeToday("haptics");

        private async System.Threading.Tasks.Task ShowAsync(string titleKey, string message)
        {
            if (TopLevel.GetTopLevel(this) is Window owner) await MessageDialog.ShowAsync(owner, Loc.Get(titleKey), message);
        }

        private async void OnHapticsEnabledChanged()
        {
            var on = ChkHapticsEnabled.IsChecked == true;
            if (_loading || on == Cfg.Enabled) return;   // compare-before-write: never save an unchanged value
            if (on && !HasHapticsAccess)
            {
                _loading = true;
                try { ChkHapticsEnabled.IsChecked = false; } finally { _loading = false; }
                await ShowAsync("title_patreon_feature", Loc.Get("msg_haptic_feedback_patreon_only"));
                return;
            }
            Cfg.Enabled = on;
            CoreSettings.Save();
        }

        /// <summary>WPF ChkHapticProvider_Changed: v2 flag, legacy enum mirrored in preference order.</summary>
        private void OnHapticProviderChanged(object? sender)
        {
            if (_loading || sender is not CheckBox box || box.Tag is not string key) return;
            var v2 = Cfg.V2;
            if (v2.Provider(key).Enabled == (box.IsChecked == true)) return;
            v2.Provider(key).Enabled = box.IsChecked == true;
            Cfg.SetLegacyProviderMirror(
                v2.Provider("lovense").Enabled ? HapticProviderType.Lovense :
                v2.Provider("buttplug").Enabled ? HapticProviderType.Buttplug :
                HapticProviderType.Mock);
            CoreSettings.Save();
            RefreshHapticConnectionUi();
        }

        private async void OnHapticConnectClicked()
        {
            if (!HasHapticsAccess)
            {
                await ShowAsync("title_patreon_feature", Loc.Get("msg_haptic_feedback_patreon_only"));
                return;
            }
            if (Haptics is not { } haptics) return;
            if (haptics.IsConnected)
            {
                await haptics.DisconnectAsync();
                RefreshHapticConnectionUi();
                return;
            }
            if (!new[] { "lovense", "buttplug", "mock" }.Any(k => Cfg.V2.Provider(k).Enabled))
            {
                await ShowAsync("tab_haptics", Loc.Get("haptics_no_provider_enabled"));
                return;
            }
            // #1310: fix a fixable Intiface address in the box, refuse an unusable one clearly.
            if (Cfg.V2.Provider("buttplug").Enabled)
            {
                var typed = Cfg.ButtplugUrl ?? "";
                var fixedUrl = ButtplugUrl.Normalize(typed, out _);
                if (fixedUrl == null)
                {
                    await ShowAsync("tab_haptics", Loc.GetF("haptics_intiface_url_invalid", typed.Trim()));
                    return;
                }
                if (!string.IsNullOrWhiteSpace(typed) && fixedUrl != typed.Trim())
                {
                    Cfg.ButtplugUrl = fixedUrl;
                    CoreSettings.Save();
                    _loading = true;
                    try { TxtHapticIntifaceUrl.Text = fixedUrl; } finally { _loading = false; }
                }
            }

            SetKey(BtnHapticConnect.Content as TextBlock, "login_connecting");
            BtnHapticConnect.IsEnabled = false;
            // WPF writes label_failed + haptics_connect_failed_hint here, and its own finally-refresh
            // repaints them straight away, so what the user sees is "Disconnected" + "no devices".
            try
            {
                if (!await haptics.ConnectAsync()) Log.Information("Haptics connect failed");
            }
            catch (Exception ex) { Log.Warning(ex, "Haptics connect error"); }
            finally
            {
                BtnHapticConnect.IsEnabled = true;
                RefreshHapticConnectionUi();
            }
        }

        private void OnHapticPanicClicked()
        {
            try { Haptics?.PanicStop(); }
            catch (Exception ex) { Log.Warning(ex, "Haptics panic stop failed"); }
            SetKey(TxtHapticActivity, "haptics_panic_done");
        }

        private async void OnHapticTestClicked()
        {
            if (Haptics is not { } haptics) return;
            if (!haptics.IsConnected)
            {
                await ShowAsync("label_not_connected", Loc.Get("msg_connect_to_a_device_first"));
                return;
            }
            var result = await haptics.TestAsync();
            if (result == HapticTestResult.Unreachable)
                await ShowAsync("label_test_failed", Loc.Get("msg_haptic_test_failed_vpn"));
            else if (result == HapticTestResult.NotConnected)
                await ShowAsync("label_not_connected", Loc.Get("msg_connect_to_a_device_first"));
        }

        private DispatcherTimer? _intensityDebounce;

        private void OnHapticIntensityChanged()
        {
            var value = (int)SliderHapticIntensity.Value;
            TxtHapticIntensity.Text = $"{value}%";
            if (_loading || (int)Math.Round(Cfg.GlobalIntensity * 100) == value) return;
            Cfg.GlobalIntensity = value / 100.0;
            CoreSettings.Save();
            // Live preview, 150 ms after the drag settles (WPF _hapticSliderDebounce).
            _intensityDebounce?.Stop();
            _intensityDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _intensityDebounce.Tick += (_, _) =>
            {
                _intensityDebounce?.Stop();
                if (Haptics is { IsConnected: true } h && Cfg.Enabled) _ = h.LiveIntensityUpdateAsync(value / 100.0);
            };
            _intensityDebounce.Start();
        }

        /// <summary>SAFETY dial: hard ceiling after the master multiplier; past the default warns.</summary>
        private void OnHapticMaxPowerChanged()
        {
            var value = (int)SliderHapticMaxPower.Value;
            TxtHapticMaxPower.Text = $"{value}%";
            HapticMaxPowerWarning.IsVisible = value > (int)Math.Round(HapticMixer.DefaultMasterCap * 100);
            if (_loading || (int)Math.Round(Cfg.V2.MasterCap * 100) == value) return;
            Cfg.V2.MasterCap = Math.Clamp(value / 100.0, 0.05, 1.0);
            CoreSettings.Save();
        }
    }
}
