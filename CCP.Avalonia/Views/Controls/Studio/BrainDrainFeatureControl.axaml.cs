using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Studio
{
    /// <summary>
    /// Brain Drain panel, ported from the WPF head. Every row reads its setting on load and writes it
    /// on change (WPF LoadFromSettings and the *_Changed handlers). The audio half drives
    /// <see cref="CoreBrainDrain"/> (BrainDrainFeatureControl.Audio.cs); the visual rows (blur, melt,
    /// keep clear, capture) write the setting, which on WPF is the whole live update too
    /// (OverlayService follows the setting). The rework notice stays hidden: this head has no
    /// withheld flag.
    /// </summary>
    public partial class BrainDrainFeatureControl : UserControl
    {
        public BrainDrainFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);

            SliderLabel.Wire(this, "SliderIntensity", "TxtIntensity", v => $"{(int)v}%");
            SliderLabel.Wire(this, "SliderBlurStrength", "TxtBlurStrength", v => $"{(int)v}%");
            SliderLabel.Wire(this, "SliderVolume", "TxtVolume", v => $"{(int)v}%");

            WireAudioHalf();    // BrainDrainFeatureControl.Audio.cs
            WireVisualHalf();
            RefreshClipCount();

            this.FindControl<Button>("BtnRefreshAudio")!.Click += (_, _) => RefreshClipCount();
            // One handler for the library button and the empty-state banner's copy of it (WPF).
            this.FindControl<Button>("BtnOpenAudioFolder")!.Click += (_, _) => OpenAudioFolder();
            this.FindControl<Button>("BtnOpenAudioFolderEmpty")!.Click += (_, _) => OpenAudioFolder();
        }

        /// <summary>WPF SliderBlurStrength_Changed, ChkMelt_Changed, ChkKeepClear_Changed and
        /// ChkAllowCapture_Changed: write the setting and save.</summary>
        private void WireVisualHalf()
        {
            var blur = this.FindControl<Slider>("SliderBlurStrength")!;
            var melt = this.FindControl<CheckBox>("ChkMelt")!;
            var keep = this.FindControl<CheckBox>("ChkKeepClear")!;
            var capture = this.FindControl<CheckBox>("ChkAllowCapture")!;

            _loading = true;
            try
            {
                var s = CoreSettings.Current;
                blur.Value = s.BrainDrainBlurStrength;
                melt.IsChecked = s.BrainDrainMeltEnabled;
                keep.IsChecked = s.BrainDrainKeepPicturesClear;
                capture.IsChecked = s.AllowOverlayCapture;
            }
            finally { _loading = false; }

            // not ported: the haze on Linux (no screen grab that leaves the app's own overlays out).
            // The four rows that only steer the haze are greyed with the reason; the audio half runs.
            if (!Overlays.BrainDrainOverlay.IsSupported)
            {
                foreach (var c in new Control[] { blur, melt, keep, capture })
                {
                    c.IsEnabled = false;
                    if (c.Parent is Control row)
                        row.Bind(ToolTip.TipProperty, new global::Avalonia.Data.Binding("[exclusives_not_on_this_build]") { Source = LocalizationManager.Instance });
                }
            }

            // The overlay follows these settings by itself (BrainDrainOverlay.OnSettingChanged), as
            // WPF OverlayService follows them: the handlers below only write and save.
            blur.ValueChanged += (_, e) =>
            {
                if (_loading) return;
                CoreSettings.Current.BrainDrainBlurStrength = (int)e.NewValue;
                CoreSettings.Save();
            };
            melt.IsCheckedChanged += (_, _) =>
            {
                if (_loading) return;
                CoreSettings.Current.BrainDrainMeltEnabled = melt.IsChecked == true;
                Log.Information("Brain Drain melt toggled: {Enabled}", melt.IsChecked == true);
                CoreSettings.Save();
            };
            keep.IsCheckedChanged += (_, _) =>
            {
                if (_loading) return;
                CoreSettings.Current.BrainDrainKeepPicturesClear = keep.IsChecked == true;
                Log.Information("Brain Drain keep pictures clear toggled: {Enabled}", keep.IsChecked == true);
                CoreSettings.Save();
            };
            capture.IsCheckedChanged += (_, _) =>
            {
                if (_loading) return;
                CoreSettings.Current.AllowOverlayCapture = capture.IsChecked == true;
                Log.Information("Brain Drain capture visibility toggled: {Allow}", capture.IsChecked == true);
                CoreSettings.Save();
            };
        }

        /// <summary>
        /// Repaint the clip-library readout (WPF RefreshClipCount). An empty pool makes the whole
        /// audio half a silent no-op, so the count is shown even when it is fine - "0 clips" is the
        /// answer to "why is nothing happening?". The paths come from the one definition of them.
        /// </summary>
        private void RefreshClipCount()
        {
            var clips = ClipCountNow();   // CoreBrainDrain (0 while no player is seeded)
            this.FindControl<TextBlock>("TxtClipCount")!.Text = Loc.GetF("st4_braindrain_clips_loaded_0", clips);
            this.FindControl<Border>("NoAudioHint")!.IsVisible = clips == 0;

            var path = this.FindControl<TextBlock>("TxtAudioFolderPath")!;
            try { path.Text = BrainDrainSchedule.AudioFolderPath; }
            catch { path.Text = string.Empty; }

            // The legacy install-directory folder gets a line ONLY while it still holds clips.
            var note = this.FindControl<TextBlock>("TxtLegacyFolderNote")!;
            var legacyPath = this.FindControl<TextBlock>("TxtLegacyFolderPath")!;
            var legacy = 0;
            try
            {
                var dir = BrainDrainSchedule.LegacyAudioFolderPath;
                legacy = LegacyClipCount(dir);
                if (legacy > 0) legacyPath.Text = dir;
            }
            catch { legacy = 0; }
            note.IsVisible = legacy > 0;
            legacyPath.IsVisible = legacy > 0;
        }

        private static readonly string[] ClipExtensions = { ".mp3", ".wav", ".ogg" };

        /// <summary>WPF BrainDrainService.LegacyAudioFileCount: the clips still in the old folder.</summary>
        internal static int LegacyClipCount(string dir) =>
            Directory.Exists(dir)
                ? Directory.GetFiles(dir).Count(f => ClipExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                : 0;

        /// <summary>WPF BtnOpenAudioFolder_Click: the PRIMARY (assets) clip folder, created first so
        /// the file manager never opens onto nothing.</summary>
        private async void OpenAudioFolder()
        {
            try
            {
                var folder = BrainDrainSchedule.AudioFolderPath;
                Directory.CreateDirectory(folder);
                await Platform.ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), folder);
            }
            catch (Exception ex) { Log.Warning(ex, "Brain Drain: open clip folder failed"); }
        }
    }
}
