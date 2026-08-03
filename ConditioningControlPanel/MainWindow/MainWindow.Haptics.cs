using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Haptics tab: device handlers and intensity controls.
    public partial class MainWindow
    {
        #region Haptics Handlers

        internal void ChkHapticsEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var isEnabled = HapticsTab.ChkHapticsEnabled.IsChecked == true;

            // Check Patreon access when enabling
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

            App.Settings.Current.Haptics.Enabled = isEnabled;
            App.Settings.Save();
        }

        internal void ChkHapticAudioSync_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var isEnabled = HapticsTab.ChkHapticAudioSync.IsChecked == true;

            App.Settings.Current.Haptics.AudioSync.Enabled = isEnabled;
            App.Settings.Save();

            // Show/hide the sliders panel (new enhanced UI)
            if (HapticsTab.VideoHapticSyncSliders != null)
                HapticsTab.VideoHapticSyncSliders.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Show/hide the latency slider panel (legacy, above browser)
            if (SettingsTab.AudioSyncLatencyPanel != null)
                SettingsTab.AudioSyncLatencyPanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        internal void SliderAudioSyncLatency_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;

            var latencyMs = (int)SettingsTab.SliderAudioSyncLatency.Value;
            App.Settings.Current.Haptics.AudioSync.ManualLatencyOffsetMs = latencyMs;
            App.Settings.Save();

            // Update display text
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
            App.Settings.Current.Haptics.AudioSync.LiveIntensity = intensityPercent / 100.0;
            // Safe to call per tick: SettingsService.Save() is itself debounced (500 ms of quiet
            // before one disk write), so a slider drag coalesces into a single save.
            App.Settings.Save();

            // Update display text (live feedback)
            if (SettingsTab.TxtAudioSyncIntensity != null)
            {
                SettingsTab.TxtAudioSyncIntensity.Text = $"{intensityPercent}%";
            }
        }

        internal void SliderVideoHapticDelay_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;

            var latencyMs = (int)HapticsTab.SliderVideoHapticDelay.Value;
            App.Settings.Current.Haptics.AudioSync.ManualLatencyOffsetMs = latencyMs;
            App.Settings.Save();

            // Update display text
            if (HapticsTab.TxtVideoHapticDelay != null)
            {
                var sign = latencyMs >= 0 ? "+" : "";
                HapticsTab.TxtVideoHapticDelay.Text = $"{sign}{latencyMs}ms";
            }

            // Sync with legacy slider if it exists
            if (SettingsTab.SliderAudioSyncLatency != null)
                SettingsTab.SliderAudioSyncLatency.Value = latencyMs;
        }

        internal void SliderVideoHapticPower_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;

            var intensityPercent = (int)HapticsTab.SliderVideoHapticPower.Value;
            App.Settings.Current.Haptics.AudioSync.LiveIntensity = intensityPercent / 100.0;
            App.Settings.Save(); // debounced inside SettingsService - one write per drag

            // Update display text (live feedback)
            if (HapticsTab.TxtVideoHapticPower != null)
            {
                HapticsTab.TxtVideoHapticPower.Text = $"{intensityPercent}%";
            }

            // Sync with legacy slider if it exists
            if (SettingsTab.SliderAudioSyncIntensity != null)
                SettingsTab.SliderAudioSyncIntensity.Value = intensityPercent;
        }

        internal void AlgorithmCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // For now, only AudioReactive is enabled - future algorithms will be added here
            if (sender is Border card && card.Tag is string algorithmName)
            {
                if (algorithmName == "AudioReactive")
                {
                    // Already selected, nothing to do
                    // Future: App.Settings.Current.Haptics.AudioSync.Algorithm = algorithmName;
                }
            }
        }

        internal void CmbHapticProvider_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isLoading || HapticsTab.CmbHapticProvider.SelectedItem == null) return;

            var item = HapticsTab.CmbHapticProvider.SelectedItem as System.Windows.Controls.ComboBoxItem;
            var tag = item?.Tag?.ToString();

            App.Settings.Current.Haptics.Provider = tag switch
            {
                "Mock" => Services.Haptics.HapticProviderType.Mock,
                "Lovense" => Services.Haptics.HapticProviderType.Lovense,
                "Buttplug" => Services.Haptics.HapticProviderType.Buttplug,
                _ => Services.Haptics.HapticProviderType.Mock
            };

            // Load the saved URL for the selected provider (or use default)
            if (HapticsTab.TxtHapticUrl != null)
            {
                var url = tag switch
                {
                    "Lovense" => App.Settings.Current.Haptics.LovenseUrl,
                    "Buttplug" => App.Settings.Current.Haptics.ButtplugUrl,
                    _ => ""
                };
                HapticsTab.TxtHapticUrl.Text = url;
            }

            // Update hint text based on provider
            if (HapticsTab.TxtHapticUrlHint != null)
            {
                HapticsTab.TxtHapticUrlHint.Text = tag switch
                {
                    "Lovense" => Loc.Get("label_lovense_hint"),
                    "Buttplug" => Loc.Get("label_buttplug_hint"),
                    _ => ""
                };
            }

            App.Settings.Save();
        }

        internal void BtnHapticsHelp_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new HapticsSetupWindow
            {
                Owner = this
            };
            helpWindow.ShowDialog();
        }

        internal async void BtnHapticConnect_Click(object sender, RoutedEventArgs e)
        {
            // Check Patreon access
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
                HapticsTab.BtnHapticConnect.Content = Loc.Get("btn_connect");
                HapticsTab.TxtHapticStatus.Text = Loc.Get("label_disconnected");
                HapticsTab.TxtHapticStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B));
                HapticsTab.TxtHapticDevices.Text = Loc.Get("label_no_devices");
            }
            else
            {
                HapticsTab.BtnHapticConnect.Content = Loc.Get("login_connecting");
                HapticsTab.BtnHapticConnect.IsEnabled = false;

                try
                {
                    var success = await App.Haptics.ConnectAsync();

                    if (success)
                    {
                        HapticsTab.BtnHapticConnect.Content = Loc.Get("btn_disconnect");
                        HapticsTab.TxtHapticStatus.Text = Loc.Get("label_connected");
                        HapticsTab.TxtHapticStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x00, 0xE6, 0x76));

                        var devices = App.Haptics.ConnectedDevices;
                        HapticsTab.TxtHapticDevices.Text = devices.Count > 0
                            ? string.Join(", ", devices)
                            : Loc.Get("label_no_devices_found");
                    }
                    else
                    {
                        HapticsTab.BtnHapticConnect.Content = Loc.Get("btn_connect");
                        HapticsTab.TxtHapticStatus.Text = Loc.Get("label_failed");
                        HapticsTab.TxtHapticStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B));
                    }
                }
                catch (Exception ex)
                {
                    HapticsTab.BtnHapticConnect.Content = Loc.Get("btn_connect");
                    HapticsTab.TxtHapticStatus.Text = Loc.Get("label_error");
                    HapticsTab.TxtHapticDevices.Text = ex.Message;
                }
                finally
                {
                    HapticsTab.BtnHapticConnect.IsEnabled = true;
                }
            }

            // FX (PR-4a): the status dot breathes only while a device is genuinely connected.
            // Read back off the service rather than off which branch we took, so a disconnect that
            // failed, or a connect that reported success but dropped, still tells the truth.
            SetHapticsStatusPulse(App.Haptics.IsConnected);
        }

        private System.Windows.Threading.DispatcherTimer? _hapticSliderDebounce;

        internal void SliderHapticIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || HapticsTab.TxtHapticIntensity == null) return;

            var value = (int)HapticsTab.SliderHapticIntensity.Value;
            HapticsTab.TxtHapticIntensity.Text = $"{value}%";
            App.Settings.Current.Haptics.GlobalIntensity = value / 100.0;
            App.Settings.Save(); // debounced inside SettingsService - one write per drag

            // Debounce: wait 150ms after slider stops moving before sending command
            _hapticSliderDebounce?.Stop();
            _hapticSliderDebounce = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _hapticSliderDebounce.Tick += (s, args) =>
            {
                _hapticSliderDebounce?.Stop();
                if (App.Haptics != null && App.Haptics.IsConnected && App.Settings.Current.Haptics.Enabled)
                {
                    _ = App.Haptics.LiveIntensityUpdateAsync(value / 100.0);
                }
            };
            _hapticSliderDebounce.Start();
        }

        internal async void BtnHapticTest_Click(object sender, RoutedEventArgs e)
        {
            if (App.Haptics == null) return;

            if (!App.Haptics.IsConnected)
            {
                MessageBox.Show(Loc.Get("msg_connect_to_a_device_first"), Loc.Get("label_not_connected"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = await App.Haptics.TestAsync();
            if (result == Services.HapticTestResult.Unreachable)
            {
                MessageBox.Show(
                    Loc.Get("msg_haptic_test_failed_vpn"),
                    Loc.Get("label_test_failed"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else if (result == Services.HapticTestResult.NotConnected)
            {
                MessageBox.Show(Loc.Get("msg_connect_to_a_device_first"), Loc.Get("label_not_connected"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        internal void TxtHapticUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoading || HapticsTab.TxtHapticUrl == null) return;

            // Save to the appropriate URL based on current provider
            var provider = App.Settings.Current.Haptics.Provider;
            if (provider == Services.Haptics.HapticProviderType.Lovense)
                App.Settings.Current.Haptics.LovenseUrl = HapticsTab.TxtHapticUrl.Text;
            else if (provider == Services.Haptics.HapticProviderType.Buttplug)
                App.Settings.Current.Haptics.ButtplugUrl = HapticsTab.TxtHapticUrl.Text;
            else
                return; // Mock has no URL - nothing to persist

            // Debounced inside SettingsService, so typing a URL is one write, not one per keystroke.
            App.Settings.Save();
        }

        internal void ChkHapticAutoConnect_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            App.Settings.Current.Haptics.AutoConnect = HapticsTab.ChkHapticAutoConnect.IsChecked == true;
            App.Settings.Save();
        }

        internal void ChkHapticFeature_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var checkbox = sender as CheckBox;
            if (checkbox == null) return;

            var tag = checkbox.Tag?.ToString();
            var isEnabled = checkbox.IsChecked == true;
            var haptics = App.Settings.Current.Haptics;

            switch (tag)
            {
                case "Bubble":
                    haptics.BubblePopEnabled = isEnabled;
                    break;
                case "FlashDisplay":
                    haptics.FlashDisplayEnabled = isEnabled;
                    break;
                case "FlashClick":
                    haptics.FlashClickEnabled = isEnabled;
                    break;
                case "Video":
                    haptics.VideoEnabled = isEnabled;
                    break;
                case "TargetHit":
                    haptics.TargetHitEnabled = isEnabled;
                    break;
                case "Subliminal":
                    haptics.SubliminalEnabled = isEnabled;
                    break;
                case "LevelUp":
                    haptics.LevelUpEnabled = isEnabled;
                    break;
                case "Achievement":
                    haptics.AchievementEnabled = isEnabled;
                    break;
                case "BouncingText":
                    haptics.BouncingTextEnabled = isEnabled;
                    break;
                case "Dtrh":
                    haptics.DtrhEnabled = isEnabled;
                    break;
            }

            App.Settings.Save();
        }

        internal void CmbHapticDtrhDensity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading || HapticsTab.CmbHapticDtrhDensity == null) return;
            App.Settings.Current.Haptics.DtrhDensity = HapticsTab.CmbHapticDtrhDensity.SelectedIndex;
            App.Settings.Save();
        }

        private System.Windows.Threading.DispatcherTimer? _hapticFeatureDebounce;

        internal void SliderHapticFeature_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var slider = sender as Slider;
            if (slider == null) return;

            var tag = slider.Tag?.ToString();
            var value = slider.Value / 100.0;
            var haptics = App.Settings.Current.Haptics;

            // Update setting and text label
            switch (tag)
            {
                case "Bubble":
                    haptics.BubblePopIntensity = value;
                    if (HapticsTab.TxtHapticBubble != null) HapticsTab.TxtHapticBubble.Text = $"{(int)slider.Value}%";
                    break;
                case "FlashDisplay":
                    haptics.FlashDisplayIntensity = value;
                    if (HapticsTab.TxtHapticFlashDisplay != null) HapticsTab.TxtHapticFlashDisplay.Text = $"{(int)slider.Value}%";
                    break;
                case "FlashClick":
                    haptics.FlashClickIntensity = value;
                    if (HapticsTab.TxtHapticFlashClick != null) HapticsTab.TxtHapticFlashClick.Text = $"{(int)slider.Value}%";
                    break;
                case "Video":
                    haptics.VideoIntensity = value;
                    if (HapticsTab.TxtHapticVideo != null) HapticsTab.TxtHapticVideo.Text = $"{(int)slider.Value}%";
                    break;
                case "TargetHit":
                    haptics.TargetHitIntensity = value;
                    if (HapticsTab.TxtHapticTargetHit != null) HapticsTab.TxtHapticTargetHit.Text = $"{(int)slider.Value}%";
                    break;
                case "Subliminal":
                    haptics.SubliminalIntensity = value;
                    if (HapticsTab.TxtHapticSubliminal != null) HapticsTab.TxtHapticSubliminal.Text = $"{(int)slider.Value}%";
                    break;
                case "LevelUp":
                    haptics.LevelUpIntensity = value;
                    if (HapticsTab.TxtHapticLevelUp != null) HapticsTab.TxtHapticLevelUp.Text = $"{(int)slider.Value}%";
                    break;
                case "Achievement":
                    haptics.AchievementIntensity = value;
                    if (HapticsTab.TxtHapticAchievement != null) HapticsTab.TxtHapticAchievement.Text = $"{(int)slider.Value}%";
                    break;
                case "BouncingText":
                    haptics.BouncingTextIntensity = value;
                    if (HapticsTab.TxtHapticBouncingText != null) HapticsTab.TxtHapticBouncingText.Text = $"{(int)slider.Value}%";
                    break;
                case "Dtrh":
                    haptics.DtrhIntensity = value;
                    if (HapticsTab.TxtHapticDtrh != null) HapticsTab.TxtHapticDtrh.Text = $"{(int)slider.Value}%";
                    break;
                case "DtrhAmbient":
                    haptics.DtrhAmbientIntensity = value;
                    if (HapticsTab.TxtHapticDtrhAmbient != null) HapticsTab.TxtHapticDtrhAmbient.Text = $"{(int)slider.Value}%";
                    break;
            }

            // Persist. SettingsService.Save() debounces internally (500 ms of quiet -> one disk
            // write), so a full drag across the slider still costs exactly one save.
            App.Settings.Save();

            // Debounce: wait 150ms after slider stops moving before sending live vibration
            _hapticFeatureDebounce?.Stop();
            _hapticFeatureDebounce = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _hapticFeatureDebounce.Tick += (s, args) =>
            {
                _hapticFeatureDebounce?.Stop();
                if (App.Haptics != null && App.Haptics.IsConnected && App.Settings.Current.Haptics.Enabled)
                {
                    // Live preview at this intensity level
                    _ = App.Haptics.LiveIntensityUpdateAsync(value);
                }
            };
            _hapticFeatureDebounce.Start();
        }

        internal void CmbHapticMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var combo = sender as ComboBox;
            if (combo == null) return;

            var tag = combo.Tag?.ToString();
            var mode = (Models.VibrationMode)combo.SelectedIndex;
            var haptics = App.Settings.Current.Haptics;

            switch (tag)
            {
                case "Bubble":
                    haptics.BubblePopMode = mode;
                    break;
                case "FlashDisplay":
                    haptics.FlashDisplayMode = mode;
                    break;
                case "FlashClick":
                    haptics.FlashClickMode = mode;
                    break;
                case "Video":
                    haptics.VideoMode = mode;
                    break;
                case "TargetHit":
                    haptics.TargetHitMode = mode;
                    break;
                case "Subliminal":
                    haptics.SubliminalMode = mode;
                    break;
                case "LevelUp":
                    haptics.LevelUpMode = mode;
                    break;
                case "Achievement":
                    haptics.AchievementMode = mode;
                    break;
                case "BouncingText":
                    haptics.BouncingTextMode = mode;
                    break;
            }

            App.Settings.Save();
        }

        #endregion
    }
}
