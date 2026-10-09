using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Studio
{
    /// <summary>The card's audio-half wiring (studio#3): WPF BrainDrainFeatureControl ChkEnable_Changed,
    /// SliderIntensity_Changed, ChkHighRefresh_Changed and the clip count, over <see cref="CoreBrainDrain"/>.
    /// Blur, melt and capture stay inert until the visual half lands.</summary>
    public partial class BrainDrainFeatureControl
    {
        private bool _loading;

        private void WireAudioHalf()
        {
            var chk = this.FindControl<CheckBox>("ChkEnable")!;
            var intensity = this.FindControl<Slider>("SliderIntensity")!;
            var high = this.FindControl<CheckBox>("ChkHighRefresh")!;

            _loading = true;
            try
            {
                var s = CoreSettings.Current;
                chk.IsChecked = s.BrainDrainEnabled;
                intensity.Value = s.BrainDrainIntensity;
                high.IsChecked = s.BrainDrainHighRefresh;
            }
            finally { _loading = false; }

            chk.IsCheckedChanged += (_, _) =>
            {
                if (_loading) return;
                var on = chk.IsChecked == true;
                CoreSettings.Current.BrainDrainEnabled = on;
                CoreEngine.ApplyLive("braindrain", on);   // only while the engine runs, as WPF
                Log.Information("Brain Drain toggled: {Enabled}", on);
                CoreSettings.Save();
            };
            intensity.ValueChanged += (_, e) =>
            {
                if (_loading) return;
                CoreSettings.Current.BrainDrainIntensity = (int)e.NewValue;
                if (CoreEngine.IsRunning) CoreBrainDrain.SetIntensity((int)e.NewValue);   // WPF UpdateSettings
                CoreSettings.Save();
            };
            high.IsCheckedChanged += (_, _) =>
            {
                if (_loading) return;
                CoreSettings.Current.BrainDrainHighRefresh = high.IsChecked == true;
                // The tick interval is read at Start, so a running service is bounced.
                if (CoreEngine.IsRunning && CoreBrainDrain.IsRunning) { CoreBrainDrain.Stop(); CoreBrainDrain.Start(); }
                CoreSettings.Save();
            };
        }

        /// <summary>WPF RefreshClipCount: the merged pool count, rescanned.</summary>
        private static int ClipCountNow()
        {
            CoreBrainDrain.ReloadClips();
            return CoreBrainDrain.ClipCount;
        }
    }
}
