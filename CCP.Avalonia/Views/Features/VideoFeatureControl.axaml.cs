using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
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
        private bool _monitorPopulating; // guards the monitor combo while it is rebuilt

        public VideoFeatureControl()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and everything below reads them.
            InitializeComponent();
            // WPF ApplyFeatureArt: hero + side plates from features/mandatory_videos.png, mod override first, repainted on a mod switch.
            Helpers.ModArt.BindFeaturePlates(this, "features/mandatory_videos.png", HeroArt, SideArt);

            ChkEnable.IsCheckedChanged += ChkEnable_Changed;
            CmbMonitor.DropDownOpened += (_, _) => PopulateMonitors();
            CmbMonitor.SelectionChanged += CmbMonitor_Changed;
            BuildMercyItems();
            ChkMercy.IsCheckedChanged += ChkMercy_Changed;
            CmbMercyAfter.SelectionChanged += CmbMercyAfter_Changed;
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

            // The hero and side plates repaint themselves on a mod switch (ModArt.BindFeaturePlates).
        }

        // ---- settings instance tracking (WPF: SettingsHook + ISettingsRebindable) --------------

        private AppSettings? _hooked;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
            RebindToCurrentSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            Unhook();
            base.OnDetachedFromVisualTree(e);
        }

        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(RebindToCurrentSettings);

        private void RebindToCurrentSettings()
        {
            Unhook();
            _hooked = CoreSettings.Current;
            _hooked.PropertyChanged += OnSettingsPropertyChanged;
            BuildMercyItems();   // WPF MercyRow.Rebind -> BuildItems: the "after N fails" text follows the language
            LoadFromSettings();
        }

        private void OnLanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RebindToCurrentSettings);

        private void BuildMercyItems()
        {
            var was = _isLoading;
            _isLoading = true;
            try
            {
                CmbMercyAfter.Items.Clear();
                for (var n = AppSettings.MercyAfterFailsMin; n <= AppSettings.MercyAfterFailsMax; n++)
                    CmbMercyAfter.Items.Add(new ComboBoxItem { Content = Loc.GetF("setting_mercy_after_n", n), Tag = n });
            }
            finally { _isLoading = was; }
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
                PopulateMonitors();
                ChkMercy.IsChecked = s.MercySystemEnabled;
                CmbMercyAfter.IsVisible = s.MercySystemEnabled;
                foreach (var obj in CmbMercyAfter.Items)
                    if (obj is ComboBoxItem { Tag: int n } it && n == s.MercyAfterFails) { CmbMercyAfter.SelectedItem = it; break; }
                // WPF: never hide a changed setting - open the fold when a row inside is off its default.
                if (s.VideoTargetMonitor != MonitorTarget.FollowGlobal || !s.MercySystemEnabled
                    || s.MercyAfterFails != AppSettings.MercyAfterFailsDefault)
                    FoldMore.IsOpen = true;
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
                e.PropertyName == nameof(AppSettings.VideoGazeClickEnabled) ||
                e.PropertyName == nameof(AppSettings.VideoTargetMonitor) ||
                e.PropertyName == nameof(AppSettings.MercySystemEnabled) ||
                e.PropertyName == nameof(AppSettings.MercyAfterFails))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }

        // -- Display monitor picker (ccp-bugs #1154; WPF VideoFeatureControl.xaml.cs, Pink filter recipe) --

        /// <summary>Rebuild the dropdown and select the saved <see cref="AppSettings.VideoTargetMonitor"/>.
        /// A saved index that no longer exists shows "Default" WITHOUT writing back, so the pick
        /// survives a reconnect.</summary>
        private void PopulateMonitors()
        {
            int saved = CoreSettings.Current.VideoTargetMonitor;
            _monitorPopulating = true;
            try
            {
                CmbMonitor.Items.Clear();
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_default"), Tag = MonitorTarget.FollowGlobal });
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_all"), Tag = MonitorTarget.All });

                var screens = ScreenList.Enumerate(this);
                string monitorLabel = Loc.Get("monitor_label");
                string primaryMarker = Loc.Get("monitor_primary_marker");
                for (int i = 0; i < screens.Count; i++)
                {
                    var b = screens[i].Bounds;
                    string prefix = screens[i].IsPrimary ? primaryMarker + ", " : "";
                    CmbMonitor.Items.Add(new ComboBoxItem { Content = $"{monitorLabel} {i + 1} ({prefix}{b.Width}x{b.Height})", Tag = i });
                }

                ComboBoxItem? match = null;
                foreach (var obj in CmbMonitor.Items)
                    if (obj is ComboBoxItem it && it.Tag is int t && t == saved) { match = it; break; }
                CmbMonitor.SelectedItem = match ?? (CmbMonitor.Items.Count > 0 ? CmbMonitor.Items[0] : null);
            }
            finally { _monitorPopulating = false; }
        }

        // -- Mercy (ccp-bugs #1145; WPF Features/MercyRow.xaml.cs) --

        private void ChkMercy_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            s.MercySystemEnabled = ChkMercy.IsChecked ?? false;
            CmbMercyAfter.IsVisible = s.MercySystemEnabled;
            CoreSettings.Save();
        }

        private void CmbMercyAfter_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (CmbMercyAfter.SelectedItem is not ComboBoxItem { Tag: int n }) return;
            CoreSettings.Current.MercyAfterFails = n;
            CoreSettings.Save();
        }

        private void CmbMonitor_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_monitorPopulating || _isLoading) return;
            if (CmbMonitor.SelectedItem is not ComboBoxItem item || item.Tag is not int target) return;
            var s = CoreSettings.Current;
            if (s.VideoTargetMonitor == target) return;
            // Read at the next video's start; a video already playing stays where it is.
            s.VideoTargetMonitor = target;
            CoreSettings.Save();
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
            CoreEngine.ApplyLive("video", CoreSettings.Current.MandatoryVideosEnabled);
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
            // WPF: a Lockdown forcing Strict Lock holds this toggle (#1282, LockdownStrictHold): put the tick back.
            if (ConditioningControlPanel.Services.LockdownStrictHold.RefusesNow(on))
            {
                _isLoading = true;
                ChkStrict.IsChecked = CoreSettings.Current.StrictLockEnabled;
                _isLoading = false;
                return;
            }
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
            if (await dialog.ShowDialogSafe<bool?>(owner) == true && dialog.ResultData != null)
            {
                s.AttentionPool = dialog.ResultData;
                CoreSettings.Save();
                Log.Information("Attention pool updated: {Count} items", dialog.ResultData.Count);
            }
        }

        private async void BtnAttentionStyle_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            await new AttentionTargetEditorDialog().ShowDialogSafe(owner);
        }

        /// <summary>The fullscreen interaction on screen right now, by its WPF queue name, or null
        /// (WPF InteractionQueue.CurrentInteraction). A seam for tests.</summary>
        internal static Func<string?> OtherInteraction = () =>
            Windows.LockCardWindow.IsAnyOpen() ? "LockCard"
            : Windows.BubbleCountWindow.IsAnyOpen() ? "BubbleCount"
            : Windows.PopQuizWindow.IsAnyOpen() ? "PopQuiz"
            : null;

        private async void BtnTestVideo_Click(object? sender, RoutedEventArgs e)
        {
            // WPF BtnTestVideo_Click -> TriggerVideo(userInitiated: true), after its two prompts.
            if (CoreEngine.Video is not { } video) return;
            if (video.IsPlaying)
            {
                if (TopLevel.GetTopLevel(this) is not Window owner) return;
                if (!await Dialogs.MessageDialog.ConfirmAsync(owner, "Video Playing",
                        "A video appears to be playing.\n\nIf you don't see a video, it may be stuck. Click Yes to force reset and try again.",
                        okText: "Yes")) return;
                Serilog.Log.Warning("User requested force reset of stuck video state");
                video.ForceCleanup();
            }
            // WPF's second prompt (InteractionQueue.CanStart): never fire over an open interaction
            // without asking. This head has no queue, so the question is asked of the windows.
            if (OtherInteraction() is { } other)
            {
                if (TopLevel.GetTopLevel(this) is not Window owner) return;
                if (!await Dialogs.MessageDialog.ConfirmAsync(owner, "Please Wait",
                        $"Another interaction is in progress ({other}).\n\nIf this seems stuck, click Yes to force reset and try again.",
                        okText: "Yes")) return;
                video.ForceCleanup();
            }
            video.Trigger();
        }
    }
}
