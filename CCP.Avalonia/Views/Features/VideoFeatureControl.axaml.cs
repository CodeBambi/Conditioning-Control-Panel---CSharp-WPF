using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Mandatory Video panel, ported from the WPF head. The read-outs and the min/max duration
    /// clamp are real; the settings writes, VideoService start/stop/test, the strict-lock
    /// double-confirm (WarningDialog) and the two editor dialogs (TextEditorDialog over
    /// s.AttentionPool, AttentionTargetEditorDialog) are WPF-head services or need App.Settings.
    /// </summary>
    public partial class VideoFeatureControl : UserControl
    {
        public VideoFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);

            SliderLabel.Wire(this, "SliderPerHour", "TxtPerHour", v => ((int)v).ToString());
            SliderLabel.Wire(this, "SliderTargets", "TxtTargets", v => ((int)v).ToString());
            SliderLabel.Wire(this, "SliderDuration", "TxtDuration", v => ((int)v).ToString());
            SliderLabel.Wire(this, "SliderTargetSize", "TxtTargetSize", v => ((int)v).ToString());
            var min = SliderLabel.Wire(this, "SliderVideoMinDur", "TxtVideoMinDur", v => FormatDuration((int)v));
            var max = SliderLabel.Wire(this, "SliderVideoMaxDur", "TxtVideoMaxDur", v => FormatDuration((int)v));

            // Keep max >= min (and min <= max) when both are non-zero, so the user can't trap the
            // queue empty. Same rule as WPF; the paired slider's own ValueChanged repaints its label.
            min.ValueChanged += (_, e) => { if (max.Value > 0 && e.NewValue > 0 && max.Value < e.NewValue) max.Value = e.NewValue; };
            max.ValueChanged += (_, e) => { if (min.Value > 0 && e.NewValue > 0 && min.Value > e.NewValue) min.Value = e.NewValue; };

            // ponytail: needs App.Settings / App.Video / WarningDialog / AttentionTargetEditorDialog,
            // wired when they move to Core. ChkEnable, ChkStrict, ChkMiniGame, ChkRandomize,
            // ChkVideoGazeClick, BtnManageAttention, BtnAttentionStyle, BtnTestVideo are inert.
        }

        private static string FormatDuration(int seconds)
        {
            if (seconds <= 0) return "off";
            if (seconds < 60) return $"{seconds}s";
            var m = seconds / 60;
            var rem = seconds % 60;
            return rem == 0 ? $"{m}m" : $"{m}m {rem}s";
        }
    }
}
