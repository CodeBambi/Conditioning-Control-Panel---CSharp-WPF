using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// SETTINGS · AUDIO, ported from the WPF head. The global dials, the output picker and the
    /// diagnostics button are bound by <see cref="AudioSettingsBinder"/>, the same binder the
    /// dashboard's audio card uses; the audio-sync tuning pair is WPF's MainWindow.Haptics.cs:1049-1074.
    /// </summary>
    public partial class AudioSettingsSection : UserControl
    {
        private bool _syncing;

        public AudioSettingsSection()
        {
            InitializeComponent();
            _ = new AudioSettingsBinder(this, SliderMaster, TxtMaster, SliderVideoVolume, TxtVideoVolume,
                ChkAudioDuck, SliderDuck, TxtDuck, ChkExcludeBambiCloudDucking, CmbAudioOutputDevice,
                BtnAudioOutputRefresh, BtnTestAudio);
            HelpPopover.Attach(HelpBtnAudio, HelpContentService.GetContent("Audio")); // WPF MainWindow.Presets.cs:54
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            AudioSettingsBinder.Changed += SyncAudioSync;
            SyncAudioSync();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            AudioSettingsBinder.Changed -= SyncAudioSync;
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>WPF RefreshAudioSyncCardVisibility: the pair shows only while the Haptics
        /// audio-sync layer is on, and mirrors that layer's delay/power.</summary>
        private void SyncAudioSync()
        {
            var a = CoreSettings.Current.Haptics.AudioSync;
            _syncing = true;
            try
            {
                AudioSyncLatencyPanel.IsVisible = a.Enabled;
                SliderAudioSyncLatency.Value = a.ManualLatencyOffsetMs;
                SliderAudioSyncIntensity.Value = a.LiveIntensity * 100;
                PaintAudioSync();
            }
            finally { _syncing = false; }
        }

        private void PaintAudioSync()
        {
            var ms = (int)SliderAudioSyncLatency.Value;
            TxtAudioSyncLatency.Text = $"{(ms >= 0 ? "+" : "")}{ms}ms";
            TxtAudioSyncIntensity.Text = $"{(int)SliderAudioSyncIntensity.Value}%";
        }

        private void SliderAudioSyncLatency_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_syncing || TxtAudioSyncLatency is null) return;
            CoreSettings.Current.Haptics.AudioSync.ManualLatencyOffsetMs = (int)e.NewValue;
            CoreSettings.Save();
            PaintAudioSync();
        }

        private void SliderAudioSyncIntensity_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_syncing || TxtAudioSyncIntensity is null) return;
            CoreSettings.Current.Haptics.AudioSync.LiveIntensity = (int)e.NewValue / 100.0;
            CoreSettings.Save();
            PaintAudioSync();
        }

        private void BtnAudioLayers_Click(object? sender, RoutedEventArgs e)
            => Windows.LayeredAudioWindow.Open(this);
    }
}
