using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics.Core;
using ConditioningControlPanel.Views.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The rest of WPF MainWindow.Haptics.cs: toy cards (#977 shape guard), routing rows, the
    /// Phase F sections (temperament, toy input, FunScript, flash brightness, DSP), DtRH dials,
    /// audio-sync tuning, the 1 Hz live status (visible-only, P01) and the pattern lab.
    /// </summary>
    public partial class HapticsTabView
    {
        private readonly ObservableCollection<HapticProviderChipVm> _providerChips = new(HapticProviderChipVm.BuildDefault());
        private readonly ObservableCollection<HapticToyCardVm> _toyCards = new();
        private readonly HapticRowExpansionScope _rowScope = new();
        private List<HapticRoutingGroupVm> _routingGroups = new();
        private string _toyShape = "";
        private bool _attached;
        internal DispatcherTimer? LiveStatusTimer { get; private set; }

        // Every buzz this page starts (toy Test, pattern Play) hangs off this, so leaving the page ends
        // it. Panic ends it too: PanicSurfaces "haptics" -> HapticService.PanicStop.
        private System.Threading.CancellationTokenSource? _previewCts;
        internal bool PreviewRunning => _previewCts is { IsCancellationRequested: false };

        private System.Threading.CancellationToken NewPreview()
        {
            StopPreview();
            return (_previewCts = new System.Threading.CancellationTokenSource()).Token;
        }

        private void EndPreview(System.Threading.CancellationToken token)
        {
            if (_previewCts is { } cts && cts.Token == token) _previewCts = null;
        }

        /// <summary>Stops what this page started (the page hid or left the tree).</summary>
        internal void StopPreview()
        {
            var cts = _previewCts;
            _previewCts = null;
            try { cts?.Cancel(); } catch { }
        }

        /// <summary>WPF LoadHapticsSettingsToUi :424-445 + the four Phase F load helpers.</summary>
        private void LoadDialsToUi(HapticSettings s)
        {
            var ambient = (int)Math.Round(Math.Clamp(s.DtrhAmbientIntensity, 0, 1) * 100);
            SliderHapticDtrhAmbient.Value = ambient;
            TxtHapticDtrhAmbient.Text = $"{ambient}%";
            CmbHapticDtrhDensity.SelectedIndex = Math.Clamp(s.DtrhDensity, 0, 2);

            var latencyMs = s.AudioSync.ManualLatencyOffsetMs;
            SliderVideoHapticDelay.Value = latencyMs;
            TxtVideoHapticDelay.Text = (latencyMs >= 0 ? "+" : "") + latencyMs + "ms";
            var syncPower = (int)Math.Round(Math.Clamp(s.AudioSync.LiveIntensity, 0, 1) * 100);
            SliderVideoHapticPower.Value = syncPower;
            TxtVideoHapticPower.Text = $"{syncPower}%";

            var temperament = HapticTemperament.FromKey(s.V2.Temperament);
            var chips = TemperamentChips;
            for (int i = 0; i < chips.Length; i++) chips[i].IsChecked = i == (int)temperament.Kind;
            SetKey(TxtHapticTemperamentDesc, temperament.DescriptionKey);

            ChkHapticToyInput.IsChecked = s.ToyInputEnabled;
            ChkHapticToyAttentionCheck.IsChecked = s.AttentionCheckToyButton;
            // A stored 0 means "back-off disabled"; the slider starts at 5, so it shows the minimum.
            var cooldown = Math.Clamp(s.UserOverrideCooldownSec <= 0 ? 5 : s.UserOverrideCooldownSec, 5, 120);
            SliderHapticOverrideCooldown.Value = cooldown;
            TxtHapticOverrideCooldown.Text = $"{cooldown}s";
            ApplyToyInputEnabledState(s.ToyInputEnabled);

            ChkHapticFunScript.IsChecked = s.FunScriptEnabled;
            ChkHapticFunScriptVibe.IsChecked = s.FunScriptToVibeConversion;
            ChkHapticFunScriptVibe.IsEnabled = s.FunScriptEnabled;
            ChkHapticLuminance.IsChecked = s.LuminanceSyncEnabled;
            var lum = (int)Math.Round(Math.Clamp(s.LuminanceSyncIntensity, 0, 1) * 100);
            SliderHapticLuminance.Value = lum;
            TxtHapticLuminance.Text = $"{lum}%";
            SliderHapticLuminance.IsEnabled = s.LuminanceSyncEnabled;

            LoadDspToUi(s.AudioSync);
        }

        private void LoadDspToUi(AudioSyncSettings a)
        {
            ChkHapticBandSplit.IsChecked = a.BandSplit;
            SetDspSlider(SliderDspSensitivity, TxtDspSensitivity, a.Sensitivity * 100, "x");
            SetDspSlider(SliderDspSmoothing, TxtDspSmoothing, a.Smoothing * 100, "%");
            SetDspSlider(SliderDspBass, TxtDspBass, a.BassWeight * 100, "%");
            SetDspSlider(SliderDspRms, TxtDspRms, a.RmsWeight * 100, "%");
            SetDspSlider(SliderDspOnset, TxtDspOnset, a.OnsetWeight * 100, "%");
            SetDspSlider(SliderDspMax, TxtDspMax, a.MaxIntensity * 100, "%");
        }

        private static void SetDspSlider(Slider slider, TextBlock label, double value100, string unit)
        {
            var v = Math.Clamp(value100, slider.Minimum, slider.Maximum);
            slider.Value = v;
            label.Text = FormatDsp(v, unit);
        }

        private static string FormatDsp(double value100, string unit)
            => unit == "x" ? (value100 / 100.0).ToString("0.00") + "x" : ((int)Math.Round(value100)) + "%";

        // ------------------------------------------------------------------ toys

        private void OnHapticDevicesChanged(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(() => RefreshHapticToys());

        /// <summary>WPF RefreshHapticToys :216: rebuild cards only when the device SHAPE changes (#977),
        /// otherwise push the battery into the existing cards so a drag is never torn down.</summary>
        internal void RefreshHapticToys(bool force = false)
        {
            var manager = Haptics?.DeviceManager;
            var devices = manager?.Devices ?? (IReadOnlyList<HapticDevice>)Array.Empty<HapticDevice>();
            var signature = HapticToyCardVm.ShapeSignature(devices);
            if (force || !string.Equals(signature, _toyShape, StringComparison.Ordinal))
            {
                _toyShape = signature;
                _toyCards.Clear();
                if (manager != null)
                    foreach (var device in devices) _toyCards.Add(new HapticToyCardVm(manager, device));
            }
            else
            {
                for (int i = 0; i < _toyCards.Count && i < devices.Count; i++) _toyCards[i].SyncLiveState(devices[i]);
            }

            ToysEmptyState.IsVisible = _toyCards.Count == 0;
            SetText(TxtHapticToyCount, _toyCards.Count == 0 ? "" : Loc.GetF("haptics_toy_count", _toyCards.Count));
            RefreshPatternToyPicker();
            RefreshHapticConnectionUi();
        }

        private async void OnHapticToyTestClicked(object? sender)
        {
            if (Haptics is not { } h || sender is not Button { Tag: string deviceKey }) return;
            var token = NewPreview();
            try
            {
                if (!await h.TestDeviceAsync(deviceKey, token: token)) SetKey(TxtHapticActivity, "haptics_test_toy_failed");
            }
            catch (Exception ex) { Log.Warning(ex, "Haptics toy test failed"); }
            finally { EndPreview(token); }
        }

        /// <summary>The audio-sync tuning sliders (and Advanced) only mean anything while that layer is on.</summary>
        private void RefreshAudioSyncCardVisibility()
        {
            var on = Cfg.AudioSync.Enabled;
            VideoHapticSyncSliders.IsVisible = on;
            TxtAudioSyncDisabledHint.IsVisible = !on;
            HapticAudioAdvanced.IsVisible = on;
        }

        // ------------------------------------------------------------------ live status (P01)

        private void SyncLiveStatusTimer()
        {
            if (_attached && IsVisible)
            {
                RefreshHapticLiveStatus();
                if (LiveStatusTimer != null) return;
                LiveStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                LiveStatusTimer.Tick += (_, _) => RefreshHapticLiveStatus();
                LiveStatusTimer.Start();
            }
            else
            {
                StopPreview();
                LiveStatusTimer?.Stop();
                LiveStatusTimer = null;
            }
        }

        /// <summary>WPF RefreshHapticLiveStatus :354: override badge + loaded-script badge.</summary>
        internal void RefreshHapticLiveStatus()
        {
            bool suppressed = false;
            try { suppressed = Haptics?.Mixer.AreLayersSuppressed == true; } catch { }
            HapticOverrideBadge.IsVisible = suppressed;

            string? path = null;
            try { path = Haptics?.FunScript.LoadedScriptPath; } catch { }
            var loaded = !string.IsNullOrWhiteSpace(path);
            HapticFunScriptLoadedBadge.IsVisible = loaded;
            if (loaded) SetText(TxtHapticFunScriptLoaded, Loc.GetF("haptics_funscript_loaded", System.IO.Path.GetFileName(path!)));
        }

        // ------------------------------------------------------------------ DtRH + sync tuning

        private void OnDtrhAmbientChanged()
        {
            var value = (int)SliderHapticDtrhAmbient.Value;
            TxtHapticDtrhAmbient.Text = $"{value}%";
            if (_loading) return;
            Cfg.DtrhAmbientIntensity = value / 100.0;
            CoreSettings.Save();
        }

        private void OnDtrhDensityChanged()
        {
            if (_loading || CmbHapticDtrhDensity.SelectedIndex < 0) return;
            Cfg.DtrhDensity = CmbHapticDtrhDensity.SelectedIndex;
            CoreSettings.Save();
        }

        private void OnSyncDelayChanged()
        {
            var latencyMs = (int)SliderVideoHapticDelay.Value;
            TxtVideoHapticDelay.Text = (latencyMs >= 0 ? "+" : "") + latencyMs + "ms";
            if (_loading) return;
            Cfg.AudioSync.ManualLatencyOffsetMs = latencyMs;
            CoreSettings.Save();
            Controls.AppSettings.AudioSettingsBinder.RaiseChanged();   // WPF mirrors into Settings · Audio
        }

        private void OnSyncPowerChanged()
        {
            var percent = (int)SliderVideoHapticPower.Value;
            TxtVideoHapticPower.Text = $"{percent}%";
            if (_loading) return;
            Cfg.AudioSync.LiveIntensity = percent / 100.0;
            CoreSettings.Save();
            Controls.AppSettings.AudioSettingsBinder.RaiseChanged();   // WPF mirrors into Settings · Audio
        }

        // ------------------------------------------------------------------ Phase F

        private RadioButton[] TemperamentChips => new[]
            { RbTemperGentle, RbTemperBalanced, RbTemperTease, RbTemperIntense, RbTemperCruel };

        /// <summary>One preset key; the mixer applies it before the master cap, so no preset exceeds it.</summary>
        private void OnTemperamentChecked(object? sender)
        {
            if (sender is not RadioButton { IsChecked: true } chip || !int.TryParse(chip.Tag as string, out var index)) return;
            var temperament = HapticTemperament.FromIndex(index);
            SetKey(TxtHapticTemperamentDesc, temperament.DescriptionKey);
            if (_loading || Cfg.V2.Temperament == temperament.Key) return;
            Cfg.V2.Temperament = temperament.Key;
            CoreSettings.Save();
        }

        private void ApplyToyInputEnabledState(bool on)
        {
            ChkHapticToyAttentionCheck.IsEnabled = on;
            SliderHapticOverrideCooldown.IsEnabled = on;
        }

        private void OnToyInputChanged()
        {
            var on = ChkHapticToyInput.IsChecked == true;
            ApplyToyInputEnabledState(on);
            if (_loading) return;
            Cfg.ToyInputEnabled = on;
            CoreSettings.Save();
        }

        private void OnToyAttentionChanged()
        {
            if (_loading) return;
            Cfg.AttentionCheckToyButton = ChkHapticToyAttentionCheck.IsChecked == true;
            CoreSettings.Save();
        }

        private void OnOverrideCooldownChanged()
        {
            var seconds = (int)SliderHapticOverrideCooldown.Value;
            TxtHapticOverrideCooldown.Text = $"{seconds}s";
            if (_loading) return;
            Cfg.UserOverrideCooldownSec = seconds;
            CoreSettings.Save();
        }

        private void OnFunScriptChanged()
        {
            var on = ChkHapticFunScript.IsChecked == true;
            ChkHapticFunScriptVibe.IsEnabled = on;
            if (_loading) return;
            Cfg.FunScriptEnabled = on;
            CoreSettings.Save();
            // Off mid-video stops the script already following, not at the next clip.
            if (!on) { try { Haptics?.FunScript.OnVideoStopped(); } catch { } }
            RefreshHapticLiveStatus();
        }

        private void OnFunScriptVibeChanged()
        {
            if (_loading) return;
            Cfg.FunScriptToVibeConversion = ChkHapticFunScriptVibe.IsChecked == true;
            CoreSettings.Save();
        }

        private void OnLuminanceChanged()
        {
            var on = ChkHapticLuminance.IsChecked == true;
            SliderHapticLuminance.IsEnabled = on;
            if (_loading) return;
            Cfg.LuminanceSyncEnabled = on;
            CoreSettings.Save();
            // The layer holds its level, so a live flash must not be left buzzing.
            if (!on) { try { Haptics?.SetLayer(HapticLayer.Luminance, 0); } catch { } }
        }

        private void OnLuminanceLevelChanged()
        {
            var percent = (int)SliderHapticLuminance.Value;
            TxtHapticLuminance.Text = $"{percent}%";
            if (_loading) return;
            Cfg.LuminanceSyncIntensity = percent / 100.0;
            CoreSettings.Save();
        }

        private void OnBandSplitChanged()
        {
            if (_loading) return;
            Cfg.AudioSync.BandSplit = ChkHapticBandSplit.IsChecked == true;
            CoreSettings.Save();
        }

        private void OnDspSliderChanged(Slider? slider, TextBlock? label, string unit, Action<double> apply)
        {
            if (slider == null) return;
            if (label != null) label.Text = FormatDsp(slider.Value, unit);
            if (_loading) return;
            apply(slider.Value / 100.0);
            CoreSettings.Save();
        }

        /// <summary>Back to a FRESH AudioSyncSettings' curve, so the model stays the source of "default".</summary>
        private void OnDspReset()
        {
            var d = new AudioSyncSettings();
            var a = Cfg.AudioSync;
            a.Sensitivity = d.Sensitivity;
            a.Smoothing = d.Smoothing;
            a.BassWeight = d.BassWeight;
            a.RmsWeight = d.RmsWeight;
            a.OnsetWeight = d.OnsetWeight;
            a.MaxIntensity = d.MaxIntensity;
            CoreSettings.Save();
            var was = _loading;
            _loading = true;
            try { LoadDspToUi(a); } finally { _loading = was; }
        }

        // ------------------------------------------------------------------ pattern lab

        private VibrationMode SelectedPatternMode => (VibrationMode)Math.Clamp(CmbPatternMode?.SelectedIndex ?? 0, 0, 5);
        private double SelectedPatternIntensity => Math.Clamp((SliderPatternIntensity?.Value ?? 60) / 100.0, 0.05, 1.0);

        /// <summary>WPF UpdateHapticPatternPreview :1110: the SAME renderer the engine plays.</summary>
        private void UpdateHapticPatternPreview()
        {
            if (PatternPreviewCanvas == null || PatternPreviewLine == null) return;
            var width = PatternPreviewCanvas.Bounds.Width;
            var height = PatternPreviewCanvas.Bounds.Height;
            if (width < 8 || height < 8) return;

            const int durationMs = 1500;
            var steps = HapticPatterns.Render(SelectedPatternMode, SelectedPatternIntensity, durationMs, 5);
            var total = Math.Max(durationMs, HapticPatterns.TotalMs(steps));
            const int samples = 160;
            var points = new List<Point>(samples + 1);
            for (int i = 0; i <= samples; i++)
            {
                var v = HapticPatterns.SampleAt(steps, (int)(total * (i / (double)samples)));
                points.Add(new Point(width * (i / (double)samples), height - (height - 4) * v - 2));
            }
            PatternPreviewLine.Points = points;
            SetText(TxtPatternPreviewLabel, Loc.GetF("haptics_pattern_preview_len", total));
        }

        /// <summary>"Play on": All toys + one entry per toy card, keeping the previous choice.</summary>
        private void RefreshPatternToyPicker()
        {
            var previous = (CmbPatternToy.SelectedItem as ComboBoxItem)?.Tag as string;
            CmbPatternToy.Items.Clear();
            CmbPatternToy.Items.Add(new ComboBoxItem { Content = Loc.Get("haptics_pattern_all_toys"), Tag = "" });
            foreach (var toy in _toyCards)
                CmbPatternToy.Items.Add(new ComboBoxItem
                {
                    Content = string.IsNullOrWhiteSpace(toy.Nickname) ? toy.Name : toy.Nickname,
                    Tag = toy.DeviceKey,
                });
            CmbPatternToy.SelectedItem = CmbPatternToy.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => (i.Tag as string) == previous) ?? CmbPatternToy.Items[0];
        }

        private async void OnPatternPlayClicked()
        {
            if (Haptics is not { } h) return;
            if (!h.IsConnected)
            {
                await ShowAsync("label_not_connected", Loc.Get("msg_connect_to_a_device_first"));
                return;
            }
            var deviceKey = (CmbPatternToy.SelectedItem as ComboBoxItem)?.Tag as string;
            var token = NewPreview();
            try
            {
                if (string.IsNullOrEmpty(deviceKey))
                    await h.PlayPatternAsync(SelectedPatternIntensity, 1500, SelectedPatternMode, priority: 5, token: token);
                else
                    await h.TestDeviceAsync(deviceKey, SelectedPatternMode, SelectedPatternIntensity, 1500, token);
            }
            catch (Exception ex) { Log.Warning(ex, "Haptics pattern play failed"); }
            finally { EndPreview(token); }
        }
    }
}
