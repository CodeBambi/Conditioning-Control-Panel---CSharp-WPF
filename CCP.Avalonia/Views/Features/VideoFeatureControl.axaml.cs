using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Mandatory Video panel, ported from the WPF head, settings logic restored against
    /// <see cref="CoreSettings"/>. Every toggle and slider round-trips the AppSettings property
    /// the WPF original wrote and saves, including the min/max duration clamp that keeps the
    /// queue from being trapped empty.
    ///
    /// <para>The WPF <c>SettingsHook</c>/<c>ISettingsRebindable</c> pair is inlined: a cloud
    /// restore SWAPS the settings instance, so the PropertyChanged subscription is tracked by
    /// instance and re-pointed on <c>SettingsService.CurrentReplaced</c>.</para>
    ///
    /// <para>The three dialogs are real, against this head's ports: the strict-lock double
    /// confirm (<see cref="WarningDialog.ShowDoubleWarningAsync"/>), the attention-pool editor
    /// (<see cref="TextEditorDialog"/>) and the target-style editor
    /// (<see cref="AttentionTargetEditorDialog"/>). Avalonia's ShowDialog is async and needs an
    /// owner Window, so the two handlers that show one are <c>async void</c> and no-op when the
    /// control has no window (the headless render path).</para>
    /// </summary>
    public partial class VideoFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public VideoFeatureControl()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and everything below reads them.
            InitializeComponent();

            ChkEnable.IsCheckedChanged += ChkEnable_Changed;
            SliderPerHour.ValueChanged += SliderPerHour_Changed;
            ChkStrict.IsCheckedChanged += ChkStrict_Changed;
            SliderVideoMinDur.ValueChanged += SliderVideoMinDur_Changed;
            SliderVideoMaxDur.ValueChanged += SliderVideoMaxDur_Changed;
            ChkMiniGame.IsCheckedChanged += ChkMiniGame_Changed;
            SliderTargets.ValueChanged += SliderTargets_Changed;
            ChkRandomize.IsCheckedChanged += ChkRandomize_Changed;
            SliderDuration.ValueChanged += SliderDuration_Changed;
            SliderTargetSize.ValueChanged += SliderTargetSize_Changed;
            ChkVideoGazeClick.IsCheckedChanged += ChkVideoGazeClick_Changed;
            BtnManageAttention.Click += BtnManageAttention_Click;
            BtnAttentionStyle.Click += BtnAttentionStyle_Click;
            BtnTestVideo.Click += BtnTestVideo_Click;

            LoadFromSettings();

            // ponytail: WPF also repaints the hero and side art plates here (ApplyFeatureArt +
            // App.Mods.ModChanged). The mod-override half of ResolveImageDecoded is portable now
            // (CoreModArt.OverridePath), but the plate still needs a named ImageBrush in the
            // .axaml, which Avalonia rejects (x:Name on a brush is AVLN2000); the port draws a
            // static wash instead, so there is nothing here to repaint.
        }

        // ---- settings instance tracking (WPF: SettingsHook + ISettingsRebindable) --------------

        private AppSettings? _hooked;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            RebindToCurrentSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            Unhook();
            base.OnDetachedFromVisualTree(e);
        }

        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(RebindToCurrentSettings);

        private void RebindToCurrentSettings()
        {
            Unhook();
            _hooked = CoreSettings.Current;
            _hooked.PropertyChanged += OnSettingsPropertyChanged;
            LoadFromSettings();
        }

        private void Unhook()
        {
            if (_hooked != null) _hooked.PropertyChanged -= OnSettingsPropertyChanged;
            _hooked = null;
        }

        private void LoadFromSettings()
        {
            var s = CoreSettings.Current;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.MandatoryVideosEnabled;
                SliderPerHour.Value = s.VideosPerHour;
                TxtPerHour.Text = s.VideosPerHour.ToString();
                ChkStrict.IsChecked = s.StrictLockEnabled;
                SliderVideoMinDur.Value = s.VideoMinDurationSeconds;
                TxtVideoMinDur.Text = FormatDuration(s.VideoMinDurationSeconds);
                SliderVideoMaxDur.Value = s.VideoMaxDurationSeconds;
                TxtVideoMaxDur.Text = FormatDuration(s.VideoMaxDurationSeconds);
                ChkMiniGame.IsChecked = s.AttentionChecksEnabled;
                SliderTargets.Value = s.AttentionDensity;
                TxtTargets.Text = s.AttentionDensity.ToString();
                ChkRandomize.IsChecked = s.RandomizeAttentionTargets;
                SliderDuration.Value = s.AttentionLifespan;
                TxtDuration.Text = s.AttentionLifespan.ToString();
                SliderTargetSize.Value = s.AttentionSize;
                TxtTargetSize.Text = s.AttentionSize.ToString();
                ChkVideoGazeClick.IsChecked = s.VideoGazeClickEnabled;
            }
            finally { _isLoading = false; }
        }

        private static string FormatDuration(int seconds)
        {
            if (seconds <= 0) return "off";
            if (seconds < 60) return $"{seconds}s";
            var m = seconds / 60;
            var rem = seconds % 60;
            return rem == 0 ? $"{m}m" : $"{m}m {rem}s";
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.MandatoryVideosEnabled) ||
                e.PropertyName == nameof(AppSettings.VideosPerHour) ||
                e.PropertyName == nameof(AppSettings.StrictLockEnabled) ||
                e.PropertyName == nameof(AppSettings.VideoMinDurationSeconds) ||
                e.PropertyName == nameof(AppSettings.VideoMaxDurationSeconds) ||
                e.PropertyName == nameof(AppSettings.AttentionChecksEnabled) ||
                e.PropertyName == nameof(AppSettings.AttentionDensity) ||
                e.PropertyName == nameof(AppSettings.RandomizeAttentionTargets) ||
                e.PropertyName == nameof(AppSettings.AttentionLifespan) ||
                e.PropertyName == nameof(AppSettings.AttentionSize) ||
                e.PropertyName == nameof(AppSettings.VideoGazeClickEnabled))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }

        private void ChkVideoGazeClick_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.VideoGazeClickEnabled = ChkVideoGazeClick.IsChecked ?? false;
            CoreSettings.Save();
        }

        private void SliderVideoMinDur_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)e.NewValue;
            TxtVideoMinDur.Text = FormatDuration(v);
            s.VideoMinDurationSeconds = v;
            // Keep max >= min when both are non-zero, so the user can't trap the queue empty.
            if (s.VideoMaxDurationSeconds > 0 && v > 0 && s.VideoMaxDurationSeconds < v)
            {
                s.VideoMaxDurationSeconds = v;
                SliderVideoMaxDur.Value = v;
                TxtVideoMaxDur.Text = FormatDuration(v);
            }
            CoreSettings.Save();
        }

        private void SliderVideoMaxDur_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)e.NewValue;
            TxtVideoMaxDur.Text = FormatDuration(v);
            s.VideoMaxDurationSeconds = v;
            // Keep min <= max when both are non-zero.
            if (s.VideoMinDurationSeconds > 0 && v > 0 && s.VideoMinDurationSeconds > v)
            {
                s.VideoMinDurationSeconds = v;
                SliderVideoMinDur.Value = v;
                TxtVideoMinDur.Text = FormatDuration(v);
            }
            CoreSettings.Save();
        }

        private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.MandatoryVideosEnabled = ChkEnable.IsChecked ?? false;
            CoreSettings.Save();

            // Live-apply: start/stop the video service if the engine is running.
            if (CoreSession.IsEngineRunning)
            {
                // ponytail: App.Video.Start()/Stop() - VideoService
                // (ConditioningControlPanel/Services/Video/VideoService.cs), still in the WPF head.
            }
        }

        private void SliderPerHour_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtPerHour.Text = v.ToString();
            CoreSettings.Current.VideosPerHour = v;
            CoreSettings.Save();
        }

        /// <summary>
        /// Strict lock is the one setting on this panel that can trap the user, so enabling it
        /// costs a double confirm. A refusal reverts the box under the loading guard, as on WPF -
        /// the revert is itself a programmatic set and must not re-enter this handler.
        /// </summary>
        private async void ChkStrict_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var on = ChkStrict.IsChecked ?? false;
            if (on && TopLevel.GetTopLevel(this) is Window owner)
            {
                var confirmed = await WarningDialog.ShowDoubleWarningAsync(owner,
                    "Strict Lock",
                    "• You will NOT be able to skip or close videos\n" +
                    "• Videos MUST be watched to completion\n" +
                    "• The only way out is the panic key (if enabled)\n" +
                    "• This can be very intense and restrictive");

                if (!confirmed)
                {
                    _isLoading = true;
                    ChkStrict.IsChecked = false;
                    _isLoading = false;
                    return;
                }
            }

            CoreSettings.Current.StrictLockEnabled = on;
            CoreSettings.Save();
        }

        private void ChkMiniGame_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.AttentionChecksEnabled = ChkMiniGame.IsChecked ?? false;
            CoreSettings.Save();
        }

        private void SliderTargets_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtTargets.Text = v.ToString();
            CoreSettings.Current.AttentionDensity = v;
            CoreSettings.Save();
        }

        private void ChkRandomize_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.RandomizeAttentionTargets = ChkRandomize.IsChecked ?? false;
            CoreSettings.Save();
        }

        private void SliderDuration_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtDuration.Text = v.ToString();
            CoreSettings.Current.AttentionLifespan = v;
            CoreSettings.Save();
        }

        private void SliderTargetSize_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtTargetSize.Text = v.ToString();
            CoreSettings.Current.AttentionSize = v;
            CoreSettings.Save();
        }

        private async void BtnManageAttention_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var s = CoreSettings.Current;
            var dialog = new TextEditorDialog("Attention Targets", s.AttentionPool);
            if (await dialog.ShowDialog<bool?>(owner) == true && dialog.ResultData != null)
            {
                s.AttentionPool = dialog.ResultData;
                CoreSettings.Save();
                Log.Information("Attention pool updated: {Count} items", dialog.ResultData.Count);
            }
        }

        private async void BtnAttentionStyle_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            await new AttentionTargetEditorDialog().ShowDialog(owner);
        }

        private void BtnTestVideo_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs VideoService (IsPlaying / ForceCleanup / TriggerVideo),
            // InteractionQueue (CanStart / CurrentInteraction / ForceReset) and
            // AutonomyService.ForceEndWebVideoTakeover, all under
            // ConditioningControlPanel/Services/ and still in the WPF head. The two "looks stuck,
            // force reset?" prompts around them go with it.
        }
    }
}
