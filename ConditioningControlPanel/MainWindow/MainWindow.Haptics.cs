using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics.Core;
using ConditioningControlPanel.Views.Controls;

namespace ConditioningControlPanel
{
    // ============================================================================
    // Haptics tab (Phase E rebuild).
    //
    // The nine copy-pasted feature rows and their nine near-identical handlers are
    // gone: the routing matrix is an ItemsControl over HapticRoutingRowVm, and each
    // row writes straight through to the settings object the engine reads.
    // What remains here is the stuff that genuinely needs the window: connection,
    // panic stop, provider selection, the global dials, the toy registry bridge and
    // the pattern lab.
    //
    // Threading: HapticDeviceManager.DevicesChanged is raised from provider IO
    // threads, so every refresh marshals with DispatcherPriority.Normal —
    // DispatcherPriority.Loaded is starved in this app and would never run.
    // ============================================================================
    public partial class MainWindow
    {
        #region Haptics — state

        private bool _hapticsTabBuilt;
        private readonly ObservableCollection<HapticProviderChipVm> _hapticProviderChips = new();
        private readonly ObservableCollection<HapticRoutingGroupVm> _hapticRoutingGroups = new();
        private readonly ObservableCollection<HapticToyCardVm> _hapticToyCards = new();
        private DispatcherTimer? _hapticSliderDebounce;

        private static HapticSettings HapticCfg => App.Settings.Current.Haptics;

        #endregion

        #region Haptics — build & refresh

        /// <summary>
        /// One-shot construction of the data-driven parts of the tab. Called from the
        /// settings-load pass (MainWindow.Settings.cs) before any value is pushed into a
        /// control, so the routing rows exist by the time the UI is populated.
        /// </summary>
        internal void InitializeHapticsTab()
        {
            if (_hapticsTabBuilt) return;
            _hapticsTabBuilt = true;

            var s = HapticCfg;
            s.EnsureV2Migrated();

            // ---- provider chips -------------------------------------------------
            _hapticProviderChips.Clear();
            _hapticProviderChips.Add(new HapticProviderChipVm("lovense", "Lovense"));
            _hapticProviderChips.Add(new HapticProviderChipVm("buttplug", "Intiface"));
            _hapticProviderChips.Add(new HapticProviderChipVm("mock", Loc.Get("haptics_provider_mock")));
            HapticsTab.ProviderChipsList.ItemsSource = _hapticProviderChips;

            // ---- routing matrix -------------------------------------------------
            HapticRoutingRowVm Ev(HapticEventKind kind, string icon, string labelKey, string hintKey)
            {
                var row = HapticRoutingRowVm.ForEvent(s, kind, icon, labelKey, hintKey);
                row.Changed += OnHapticRoutingRowChanged;
                return row;
            }
            HapticRoutingRowVm Ly(HapticLayer layer, string icon, string labelKey, string hintKey,
                                  HapticRowLegacyBinding legacy)
            {
                var row = HapticRoutingRowVm.ForLayer(s, layer, icon, labelKey, hintKey, legacy);
                row.Changed += OnHapticRoutingRowChanged;
                return row;
            }

            _hapticRoutingGroups.Clear();
            _hapticRoutingGroups.Add(new HapticRoutingGroupVm("🌀", Loc.Get("haptics_group_core"), new[]
            {
                Ev(HapticEventKind.FlashClick, "⚡", "label_flash_click", "haptics_hint_flash_click"),
                Ev(HapticEventKind.FlashDecay, "💥", "label_flash_show", "haptics_hint_flash_decay"),
                Ev(HapticEventKind.SubliminalTrigger, "💬", "tab_subliminals", "haptics_hint_subliminal"),
                Ev(HapticEventKind.KeywordTrigger, "🔑", "haptics_row_keyword", "haptics_hint_keyword"),
                // Blink has had settings since v6.4 and never had a row until now.
                Ev(HapticEventKind.BlinkPulse, "👁", "haptics_row_blink", "haptics_hint_blink"),
            }));
            _hapticRoutingGroups.Add(new HapticRoutingGroupVm("🏆", Loc.Get("haptics_group_rewards"), new[]
            {
                Ev(HapticEventKind.Achievement, "🏆", "tab_achievements", "haptics_hint_achievement"),
                Ev(HapticEventKind.QuestComplete, "📋", "haptics_row_quest", "haptics_hint_quest"),
                Ev(HapticEventKind.LevelUp, "⭐", "label_level_up", "haptics_hint_levelup"),
                Ev(HapticEventKind.GazeReward, "👀", "haptics_row_gaze", "haptics_hint_gaze"),
            }));
            _hapticRoutingGroups.Add(new HapticRoutingGroupVm("🎬", Loc.Get("haptics_group_media"), new[]
            {
                Ly(HapticLayer.Video, "🎬", "haptics_row_video_bg", "haptics_hint_video_bg", HapticRowLegacyBinding.VideoLevel),
                Ev(HapticEventKind.VideoTargetHit, "🎯", "label_target_hit", "haptics_hint_target_hit"),
                Ly(HapticLayer.AudioSync, "🎵", "haptics_row_audio_sync", "haptics_hint_audio_sync", HapticRowLegacyBinding.AudioSync),
                Ev(HapticEventKind.BouncingTextBounce, "🔤", "label_bounce_text", "haptics_hint_bouncing_text"),
            }));
            _hapticRoutingGroups.Add(new HapticRoutingGroupVm("🎮", Loc.Get("haptics_group_games"), new[]
            {
                Ev(HapticEventKind.BubblePop, "🫧", "label_bubbles", "haptics_hint_bubble"),
                Ev(HapticEventKind.DtrhAccent, "🐇", "label_dtrh_haptics", "haptics_hint_dtrh"),
            }));
            HapticsTab.RoutingGroupsList.ItemsSource = _hapticRoutingGroups;

            // ---- toy cards ------------------------------------------------------
            HapticsTab.ToyCardsList.ItemsSource = _hapticToyCards;

            if (App.Haptics != null)
            {
                App.Haptics.DeviceManager.DevicesChanged += OnHapticDevicesChanged;
                App.Haptics.ConnectionChanged += OnHapticConnectionChanged;
                App.Haptics.HapticTriggered += OnHapticActivity;
            }
        }

        private void OnHapticRoutingRowChanged(object? sender, EventArgs e)
        {
            // The audio-sync row gates its own tuning card.
            RefreshAudioSyncCardVisibility();
        }

        private void OnHapticDevicesChanged(object? sender, EventArgs e)
            => HapticsRunOnUi(RefreshHapticToys);

        private void OnHapticConnectionChanged(object? sender, bool connected)
            => HapticsRunOnUi(RefreshHapticConnectionUi);

        private void OnHapticActivity(object? sender, string message)
        {
            HapticsRunOnUi(() =>
            {
                if (HapticsTab?.TxtHapticActivity == null) return;
                HapticsTab.TxtHapticActivity.Text = message ?? "";
            });
        }

        /// <summary>Marshal to the UI thread at Normal priority (Loaded is starved here) and
        /// never let a background event fault the app during shutdown.</summary>
        private void HapticsRunOnUi(Action action)
        {
            // (MainWindow.TakeoverUi.cs owns the plain RunOnUi; this one is explicit about
            // DispatcherPriority.Normal because Loaded is starved in this app.)
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                if (dispatcher.CheckAccess()) { SafeRun(action); return; }
                dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => SafeRun(action)));
            }
            catch { }
        }

        private static void SafeRun(Action action)
        {
            try { action(); }
            catch (Exception ex) { App.Logger?.Debug("Haptics UI refresh failed: {E}", ex.Message); }
        }

        /// <summary>Rebuild the toy cards from the merged device registry.</summary>
        internal void RefreshHapticToys()
        {
            if (HapticsTab?.ToyCardsList == null) return;

            var manager = App.Haptics?.DeviceManager;
            _hapticToyCards.Clear();
            if (manager != null)
            {
                foreach (var device in manager.Devices)
                    _hapticToyCards.Add(new HapticToyCardVm(manager, device));
            }

            HapticsTab.ToysEmptyState.Visibility = _hapticToyCards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            HapticsTab.TxtHapticToyCount.Text = _hapticToyCards.Count == 0
                ? ""
                : Loc.GetF("haptics_toy_count", _hapticToyCards.Count);

            RefreshPatternToyPicker();
            RefreshHapticConnectionUi();
        }

        /// <summary>Status dot, status text, device summary, connect button and provider chips.</summary>
        internal void RefreshHapticConnectionUi()
        {
            if (HapticsTab?.TxtHapticStatus == null) return;

            var haptics = App.Haptics;
            var connected = haptics?.IsConnected == true;

            HapticsTab.TxtHapticStatus.Text = Loc.Get(connected ? "label_connected" : "label_disconnected");
            var statusColor = connected ? Color.FromRgb(0x00, 0xE6, 0x76) : Color.FromRgb(0xFF, 0x6B, 0x6B);
            HapticsTab.TxtHapticStatus.Foreground = new SolidColorBrush(statusColor);
            HapticsTab.HapticStatusDot.Fill = new SolidColorBrush(statusColor);
            HapticsTab.BtnHapticConnect.Content = Loc.Get(connected ? "btn_disconnect" : "btn_connect");

            var count = haptics?.DeviceManager.Devices.Count ?? 0;
            HapticsTab.TxtHapticDevices.Text = count == 0
                ? Loc.Get("label_no_devices")
                : Loc.GetF("haptics_devices_merged", count, haptics?.ProviderName ?? "");

            var v2 = HapticCfg.V2;
            foreach (var chip in _hapticProviderChips)
            {
                chip.IsEnabledForConnect = v2.Provider(chip.Key).Enabled;
                chip.IsConnected = haptics?.DeviceManager.Providers
                    .Any(p => string.Equals(p.Key, chip.Key, StringComparison.OrdinalIgnoreCase) && p.IsConnected) == true;
            }

            SetHapticsStatusPulse(connected);
        }

        /// <summary>The audio-sync tuning sliders only mean anything while that layer is on.</summary>
        internal void RefreshAudioSyncCardVisibility()
        {
            if (HapticsTab?.VideoHapticSyncSliders == null) return;
            var on = HapticCfg.AudioSync.Enabled;
            HapticsTab.VideoHapticSyncSliders.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            HapticsTab.TxtAudioSyncDisabledHint.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            if (SettingsTab?.AudioSyncLatencyPanel != null)
                SettingsTab.AudioSyncLatencyPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Push every routing row's value back out of settings (post-load, post-import).</summary>
        internal void RefreshHapticRoutingRows()
        {
            foreach (var group in _hapticRoutingGroups)
                foreach (var row in group.Rows)
                    row.Refresh();
        }

        /// <summary>
        /// The whole haptics load pass. Called by MainWindow.Settings.cs (inside its _isLoading
        /// window) and again after the setup wizard closes, since the wizard writes provider
        /// flags and the Lovense address behind our back.
        /// </summary>
        internal void LoadHapticsSettingsToUi()
        {
            InitializeHapticsTab();

            var s = HapticCfg;
            s.EnsureV2Migrated();

            HapticsTab.ChkHapticsEnabled.IsChecked = s.Enabled;
            HapticsTab.ChkHapticAutoConnect.IsChecked = s.AutoConnect;

            // Per-provider enables replace the old single-choice combo (which had no Mock row and
            // therefore rendered blank on a fresh install). The v2 flags are what the device
            // manager actually reads; the legacy enum is kept in step by ChkHapticProvider_Changed.
            HapticsTab.ChkHapticProviderLovense.IsChecked = s.V2.Provider("lovense").Enabled;
            HapticsTab.ChkHapticProviderIntiface.IsChecked = s.V2.Provider("buttplug").Enabled;
            HapticsTab.ChkHapticProviderMock.IsChecked = s.V2.Provider("mock").Enabled;
            HapticsTab.TxtHapticUrl.Text = s.LovenseUrl ?? "";

            var master = (int)Math.Round(Math.Clamp(s.GlobalIntensity, 0, 1) * 100);
            HapticsTab.SliderHapticIntensity.Value = master;
            HapticsTab.TxtHapticIntensity.Text = $"{master}%";

            var cap = (int)Math.Round(Math.Clamp(s.V2.MasterCap, 0.05, 1.0) * 100);
            HapticsTab.SliderHapticMaxPower.Value = cap;
            HapticsTab.TxtHapticMaxPower.Text = $"{cap}%";
            HapticsTab.HapticMaxPowerWarning.Visibility =
                cap > (int)Math.Round(HapticMixer.DefaultMasterCap * 100) ? Visibility.Visible : Visibility.Collapsed;

            var ambient = (int)Math.Round(Math.Clamp(s.DtrhAmbientIntensity, 0, 1) * 100);
            HapticsTab.SliderHapticDtrhAmbient.Value = ambient;
            HapticsTab.TxtHapticDtrhAmbient.Text = $"{ambient}%";
            HapticsTab.CmbHapticDtrhDensity.SelectedIndex = Math.Clamp(s.DtrhDensity, 0, 2);

            var latencyMs = s.AudioSync.ManualLatencyOffsetMs;
            HapticsTab.SliderVideoHapticDelay.Value = latencyMs;
            HapticsTab.TxtVideoHapticDelay.Text = (latencyMs >= 0 ? "+" : "") + latencyMs + "ms";
            var syncPower = (int)Math.Round(Math.Clamp(s.AudioSync.LiveIntensity, 0, 1) * 100);
            HapticsTab.SliderVideoHapticPower.Value = syncPower;
            HapticsTab.TxtVideoHapticPower.Text = $"{syncPower}%";

            RefreshAudioSyncCardVisibility();
            RefreshHapticRoutingRows();
            RefreshHapticToys();
            UpdateHapticPatternPreview();
        }

        #endregion

        #region Haptics — connection handlers

        internal void ChkHapticsEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var isEnabled = HapticsTab.ChkHapticsEnabled.IsChecked == true;

            if (isEnabled && App.Patreon?.HasPremiumAccess != true)
            {
                HapticsTab.ChkHapticsEnabled.IsChecked = false;
                MessageBox.Show(
                    Loc.Get("msg_haptic_feedback_patreon_only"),
                    Loc.Get("title_patreon_feature"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            HapticCfg.Enabled = isEnabled;
            App.Settings.Save();
        }

        /// <summary>
        /// Per-provider enable. The v2 device manager connects every enabled provider
        /// CONCURRENTLY, so these are checkboxes, not a single-choice combo. The legacy
        /// <c>Provider</c> enum is still written (old settings files and a few call sites read
        /// it) using the same preference order the device manager uses for de-duplication.
        /// </summary>
        internal void ChkHapticProvider_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not CheckBox box || box.Tag is not string key) return;

            var v2 = HapticCfg.V2;
            v2.Provider(key).Enabled = box.IsChecked == true;

            HapticCfg.Provider =
                v2.Provider("lovense").Enabled ? Services.Haptics.HapticProviderType.Lovense :
                v2.Provider("buttplug").Enabled ? Services.Haptics.HapticProviderType.Buttplug :
                Services.Haptics.HapticProviderType.Mock;

            App.Settings.Save();
            RefreshHapticConnectionUi();
        }

        /// <summary>
        /// One URL box, one property. The old code switched the box between LovenseUrl and
        /// ButtplugUrl depending on the provider combo, so the value you typed could land on the
        /// wrong setting when the combo changed underneath you (flagged in the Phase D notes).
        /// Intiface's address is not user-editable here — it is the Intiface default and the
        /// provider owns it.
        /// </summary>
        internal void TxtHapticUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoading || HapticsTab.TxtHapticUrl == null) return;
            // Mirrors into V2.Provider("lovense").Url via HapticSettings.OnPropertyChanged.
            HapticCfg.LovenseUrl = HapticsTab.TxtHapticUrl.Text;
            App.Settings.Save();   // debounced in SettingsService: one write per typing burst
        }

        internal void ChkHapticAutoConnect_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            HapticCfg.AutoConnect = HapticsTab.ChkHapticAutoConnect.IsChecked == true;
            App.Settings.Save();
        }

        internal async void BtnHapticConnect_Click(object sender, RoutedEventArgs e)
        {
            if (App.Patreon?.HasPremiumAccess != true)
            {
                MessageBox.Show(
                    Loc.Get("msg_haptic_feedback_patreon_only"),
                    Loc.Get("title_patreon_feature"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (App.Haptics == null) return;

            if (App.Haptics.IsConnected)
            {
                await App.Haptics.DisconnectAsync();
                RefreshHapticToys();
                return;
            }

            var anyProvider = new[] { "lovense", "buttplug", "mock" }
                .Any(k => HapticCfg.V2.Provider(k).Enabled);
            if (!anyProvider)
            {
                MessageBox.Show(Loc.Get("haptics_no_provider_enabled"), Loc.Get("tab_haptics"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            HapticsTab.BtnHapticConnect.Content = Loc.Get("login_connecting");
            HapticsTab.BtnHapticConnect.IsEnabled = false;
            try
            {
                var success = await App.Haptics.ConnectAsync();
                RefreshHapticToys();
                if (!success)
                {
                    HapticsTab.TxtHapticStatus.Text = Loc.Get("label_failed");
                    HapticsTab.TxtHapticStatus.Foreground =
                        new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
                    HapticsTab.TxtHapticDevices.Text = Loc.Get("haptics_connect_failed_hint");
                }
            }
            catch (Exception ex)
            {
                HapticsTab.TxtHapticStatus.Text = Loc.Get("label_error");
                HapticsTab.TxtHapticDevices.Text = ex.Message;
            }
            finally
            {
                HapticsTab.BtnHapticConnect.IsEnabled = true;
                RefreshHapticConnectionUi();
            }
        }

        /// <summary>Everything off NOW. Bypasses throttles, gates and unchanged-send suppression.</summary>
        internal void BtnHapticPanic_Click(object sender, RoutedEventArgs e)
        {
            try { App.Haptics?.PanicStop(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Haptics panic stop failed"); }

            if (HapticsTab?.TxtHapticActivity != null)
                HapticsTab.TxtHapticActivity.Text = Loc.Get("haptics_panic_done");
        }

        internal async void BtnHapticTest_Click(object sender, RoutedEventArgs e)
        {
            if (App.Haptics == null) return;

            if (!App.Haptics.IsConnected)
            {
                MessageBox.Show(Loc.Get("msg_connect_to_a_device_first"), Loc.Get("label_not_connected"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = await App.Haptics.TestAsync();
            if (result == HapticTestResult.Unreachable)
            {
                MessageBox.Show(Loc.Get("msg_haptic_test_failed_vpn"), Loc.Get("label_test_failed"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else if (result == HapticTestResult.NotConnected)
            {
                MessageBox.Show(Loc.Get("msg_connect_to_a_device_first"), Loc.Get("label_not_connected"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>Per-toy test pulse, straight at that one device.</summary>
        internal async void BtnHapticToyTest_Click(object sender, RoutedEventArgs e)
        {
            if (App.Haptics == null) return;
            if (sender is not Button btn || btn.Tag is not string deviceKey) return;

            var ok = await App.Haptics.TestDeviceAsync(deviceKey);
            if (!ok && HapticsTab?.TxtHapticActivity != null)
                HapticsTab.TxtHapticActivity.Text = Loc.Get("haptics_test_toy_failed");
        }

        internal void BtnHapticsHelp_Click(object sender, RoutedEventArgs e)
        {
            var wizard = new HapticsSetupWindow { Owner = this };
            wizard.ShowDialog();

            // The wizard writes provider flags / the Lovense IP and may connect, so re-read.
            _isLoading = true;
            try { LoadHapticsSettingsToUi(); }
            finally { _isLoading = false; }
            RefreshHapticToys();
        }

        #endregion

        #region Haptics — global dials

        internal void SliderHapticIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (HapticsTab?.TxtHapticIntensity == null) return;
            var value = (int)HapticsTab.SliderHapticIntensity.Value;
            HapticsTab.TxtHapticIntensity.Text = $"{value}%";
            if (_isLoading) return;

            HapticCfg.GlobalIntensity = value / 100.0;
            App.Settings.Save();

            // Live preview, 150 ms after the drag settles.
            _hapticSliderDebounce?.Stop();
            _hapticSliderDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _hapticSliderDebounce.Tick += (s, args) =>
            {
                _hapticSliderDebounce?.Stop();
                if (App.Haptics != null && App.Haptics.IsConnected && HapticCfg.Enabled)
                    _ = App.Haptics.LiveIntensityUpdateAsync(value / 100.0);
            };
            _hapticSliderDebounce.Start();
        }

        /// <summary>
        /// SAFETY dial: the hard ceiling every actuator is clamped to after the master
        /// multiplier. Default 0.70 — full power is opt-in, so raising it past the default
        /// shows a warning instead of quietly doubling what the last session felt like.
        /// </summary>
        internal void SliderHapticMaxPower_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (HapticsTab?.TxtHapticMaxPower == null) return;
            var value = (int)HapticsTab.SliderHapticMaxPower.Value;
            HapticsTab.TxtHapticMaxPower.Text = $"{value}%";
            HapticsTab.HapticMaxPowerWarning.Visibility =
                value > (int)Math.Round(HapticMixer.DefaultMasterCap * 100) ? Visibility.Visible : Visibility.Collapsed;
            if (_isLoading) return;

            HapticCfg.V2.MasterCap = Math.Clamp(value / 100.0, 0.05, 1.0);
            App.Settings.Save();
        }

        internal void SliderHapticDtrhAmbient_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (HapticsTab?.TxtHapticDtrhAmbient == null) return;
            var value = (int)HapticsTab.SliderHapticDtrhAmbient.Value;
            HapticsTab.TxtHapticDtrhAmbient.Text = $"{value}%";
            if (_isLoading) return;

            HapticCfg.DtrhAmbientIntensity = value / 100.0;
            App.Settings.Save();
        }

        internal void CmbHapticDtrhDensity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || HapticsTab?.CmbHapticDtrhDensity == null) return;
            HapticCfg.DtrhDensity = HapticsTab.CmbHapticDtrhDensity.SelectedIndex;
            App.Settings.Save();
        }

        #endregion

        #region Haptics — audio sync tuning

        internal void SliderVideoHapticDelay_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (HapticsTab?.TxtVideoHapticDelay == null) return;
            var latencyMs = (int)HapticsTab.SliderVideoHapticDelay.Value;
            var sign = latencyMs >= 0 ? "+" : "";
            HapticsTab.TxtVideoHapticDelay.Text = $"{sign}{latencyMs}ms";
            if (_isLoading) return;

            HapticCfg.AudioSync.ManualLatencyOffsetMs = latencyMs;
            App.Settings.Save();

            if (SettingsTab?.SliderAudioSyncLatency != null)
                SettingsTab.SliderAudioSyncLatency.Value = latencyMs;
        }

        internal void SliderVideoHapticPower_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (HapticsTab?.TxtVideoHapticPower == null) return;
            var intensityPercent = (int)HapticsTab.SliderVideoHapticPower.Value;
            HapticsTab.TxtVideoHapticPower.Text = $"{intensityPercent}%";
            if (_isLoading) return;

            HapticCfg.AudioSync.LiveIntensity = intensityPercent / 100.0;
            App.Settings.Save();

            if (SettingsTab?.SliderAudioSyncIntensity != null)
                SettingsTab.SliderAudioSyncIntensity.Value = intensityPercent;
        }

        internal void SliderAudioSyncLatency_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;

            var latencyMs = (int)SettingsTab.SliderAudioSyncLatency.Value;
            HapticCfg.AudioSync.ManualLatencyOffsetMs = latencyMs;
            App.Settings.Save();

            if (SettingsTab.TxtAudioSyncLatency != null)
            {
                var sign = latencyMs >= 0 ? "+" : "";
                SettingsTab.TxtAudioSyncLatency.Text = $"{sign}{latencyMs}ms";
            }
        }

        internal void SliderAudioSyncIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;

            var intensityPercent = (int)SettingsTab.SliderAudioSyncIntensity.Value;
            HapticCfg.AudioSync.LiveIntensity = intensityPercent / 100.0;
            // Safe per tick: SettingsService.Save() is debounced (500 ms of quiet -> one write).
            App.Settings.Save();

            if (SettingsTab.TxtAudioSyncIntensity != null)
                SettingsTab.TxtAudioSyncIntensity.Text = $"{intensityPercent}%";
        }

        #endregion

        #region Haptics — pattern lab

        internal void CmbPatternMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateHapticPatternPreview();

        internal void SliderPatternIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (HapticsTab?.TxtPatternIntensity == null) return;
            HapticsTab.TxtPatternIntensity.Text = $"{(int)HapticsTab.SliderPatternIntensity.Value}%";
            UpdateHapticPatternPreview();
        }

        internal void PatternPreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
            => UpdateHapticPatternPreview();

        private VibrationMode SelectedPatternMode =>
            (VibrationMode)Math.Clamp(HapticsTab?.CmbPatternMode?.SelectedIndex ?? 0, 0, 5);

        private double SelectedPatternIntensity =>
            Math.Clamp((HapticsTab?.SliderPatternIntensity?.Value ?? 60) / 100.0, 0.05, 1.0);

        /// <summary>
        /// Draw the selected mode's rendered envelope. This is the SAME renderer the engine
        /// uses (Services/Haptics/Core/HapticPatterns.cs), so the curve on screen is what the
        /// toy will feel — not a decorative approximation.
        ///
        /// TODO (Deeper unification): HapticPatterns is the plug-in point for a full keyframe
        /// designer. Merging the six stock modes with Deeper's authored StockHapticPatterns
        /// means adding a Custom mode whose points are stored per row; the preview strip and
        /// play path below already accept an arbitrary envelope.
        /// </summary>
        private void UpdateHapticPatternPreview()
        {
            var canvas = HapticsTab?.PatternPreviewCanvas;
            var line = HapticsTab?.PatternPreviewLine;
            if (canvas == null || line == null) return;

            var width = canvas.ActualWidth;
            var height = canvas.ActualHeight;
            if (width < 8 || height < 8) return;

            const int durationMs = 1500;
            var steps = HapticPatterns.Render(SelectedPatternMode, SelectedPatternIntensity, durationMs, 5);
            var total = Math.Max(durationMs, HapticPatterns.TotalMs(steps));

            var points = new PointCollection();
            const int samples = 160;
            for (int i = 0; i <= samples; i++)
            {
                var t = (int)(total * (i / (double)samples));
                var v = HapticPatterns.SampleAt(steps, t);
                points.Add(new Point(width * (i / (double)samples), height - (height - 4) * v - 2));
            }
            line.Points = points;

            if (HapticsTab?.TxtPatternPreviewLabel != null)
                HapticsTab.TxtPatternPreviewLabel.Text = Loc.GetF("haptics_pattern_preview_len", total);
        }

        /// <summary>Rebuild the "play on" picker: All toys + one entry per connected toy.</summary>
        private void RefreshPatternToyPicker()
        {
            var combo = HapticsTab?.CmbPatternToy;
            if (combo == null) return;

            var previous = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
            combo.Items.Clear();
            combo.Items.Add(new ComboBoxItem { Content = Loc.Get("haptics_pattern_all_toys"), Tag = "" });
            foreach (var toy in _hapticToyCards)
            {
                var label = string.IsNullOrWhiteSpace(toy.Nickname) ? toy.Name : toy.Nickname;
                combo.Items.Add(new ComboBoxItem { Content = label, Tag = toy.DeviceKey });
            }

            var match = combo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => (i.Tag as string) == previous);
            combo.SelectedItem = match ?? combo.Items[0];
        }

        internal async void BtnPatternPlay_Click(object sender, RoutedEventArgs e)
        {
            if (App.Haptics == null) return;
            if (!App.Haptics.IsConnected)
            {
                MessageBox.Show(Loc.Get("msg_connect_to_a_device_first"), Loc.Get("label_not_connected"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var deviceKey = (HapticsTab.CmbPatternToy.SelectedItem as ComboBoxItem)?.Tag as string;
            var mode = SelectedPatternMode;
            var intensity = SelectedPatternIntensity;

            if (string.IsNullOrEmpty(deviceKey))
                await App.Haptics.PlayPatternAsync(intensity, 1500, mode, priority: 5);
            else
                await App.Haptics.TestDeviceAsync(deviceKey, mode, intensity, 1500);
        }

        #endregion
    }
}
