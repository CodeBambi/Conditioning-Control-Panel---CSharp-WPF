using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Features
{
    public partial class VideoFeatureControl : UserControl, ISettingsRebindable
    {
        private bool _isLoading = true;
        private bool _monitorPopulating; // guards the monitor combo while it is rebuilt

        public VideoFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // Tracks WHICH AppSettings instance the hook is attached to, so a cloud restore - which
        // SWAPS the instance - can be followed instead of leaving this permanently-mounted rack
        // panel listening to, and displaying, the discarded object. See ISettingsRebindable.
        private SettingsHook? _settingsHook;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RebindToCurrentSettings();
            // The hero and side plates are mod art; the rack hosts this control permanently, so a
            // mod switch must repaint them (a popup instance never lived long enough to care).
            ApplyFeatureArt();
            if (App.Mods != null) App.Mods.ModChanged += OnModChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _settingsHook?.Unhook();
            if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
        }

        /// <inheritdoc/>
        public void RebindToCurrentSettings()
        {
            (_settingsHook ??= new SettingsHook(OnSettingsPropertyChanged)).Rebind();
            LoadFromSettings();
            MercyRow.Rebind();
        }

        private void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
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
            }
            finally { _isLoading = false; }
        }

        // -- Display monitor picker (ccp-bugs #1154, same recipe as the Pink filter's #639) --

        /// <summary>Rebuild the monitor dropdown and select the saved
        /// <see cref="Models.AppSettings.VideoTargetMonitor"/>. A saved index that no longer exists
        /// shows "Default" WITHOUT writing back, so the pick survives a reconnect.</summary>
        private void PopulateMonitors()
        {
            if (CmbMonitor == null) return;
            int saved = App.Settings?.Current?.VideoTargetMonitor ?? App.MonitorTargetFollowGlobal;
            _monitorPopulating = true;
            try
            {
                CmbMonitor.Items.Clear();
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_default"), Tag = App.MonitorTargetFollowGlobal });
                CmbMonitor.Items.Add(new ComboBoxItem { Content = Loc.Get("monitor_target_all"), Tag = App.MonitorTargetAll });

                var screens = App.GetAllScreensCached();
                string monitorLabel = Loc.Get("monitor_label");
                string primaryMarker = Loc.Get("monitor_primary_marker");
                for (int i = 0; i < screens.Length; i++)
                {
                    var b = screens[i].Bounds;
                    string prefix = screens[i].Primary ? primaryMarker + ", " : "";
                    CmbMonitor.Items.Add(new ComboBoxItem
                    {
                        Content = $"{monitorLabel} {i + 1} ({prefix}{b.Width}x{b.Height})",
                        Tag = i
                    });
                }

                ComboBoxItem? match = null;
                foreach (ComboBoxItem it in CmbMonitor.Items)
                    if (it.Tag is int t && t == saved) { match = it; break; }
                CmbMonitor.SelectedItem = match ?? (CmbMonitor.Items.Count > 0 ? CmbMonitor.Items[0] : null);
            }
            finally { _monitorPopulating = false; }
        }

        // Re-enumerate on open so a monitor plugged in since load appears without reopening the card.
        private void CmbMonitor_DropDownOpened(object sender, EventArgs e)
        {
            App.InvalidateScreenCache();
            PopulateMonitors();
        }

        private void CmbMonitor_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_monitorPopulating || _isLoading) return;
            if (CmbMonitor.SelectedItem is not ComboBoxItem item || item.Tag is not int target) return;

            var s = App.Settings?.Current;
            if (s == null) return;
            if (s.VideoTargetMonitor == target) return;

            // Read at the next video's start; a video already playing stays where it is.
            s.VideoTargetMonitor = target;
            App.Settings?.Save();
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
            if (e.PropertyName == nameof(Models.AppSettings.MandatoryVideosEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.VideosPerHour) ||
                e.PropertyName == nameof(Models.AppSettings.StrictLockEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.VideoMinDurationSeconds) ||
                e.PropertyName == nameof(Models.AppSettings.VideoMaxDurationSeconds) ||
                e.PropertyName == nameof(Models.AppSettings.AttentionChecksEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.AttentionDensity) ||
                e.PropertyName == nameof(Models.AppSettings.RandomizeAttentionTargets) ||
                e.PropertyName == nameof(Models.AppSettings.AttentionLifespan) ||
                e.PropertyName == nameof(Models.AppSettings.AttentionSize) ||
                e.PropertyName == nameof(Models.AppSettings.VideoGazeClickEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.VideoTargetMonitor))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkVideoGazeClick_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.VideoGazeClickEnabled = ChkVideoGazeClick.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderVideoMinDur_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
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
            App.Settings?.Save();
        }

        private void SliderVideoMaxDur_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
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
            App.Settings?.Save();
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var on = ChkEnable.IsChecked ?? false;
            s.MandatoryVideosEnabled = on;
            App.Settings?.Save();

            // Live-apply: start/stop video service if engine is running
            if (App.IsEngineRunning)
            {
                if (on)
                    App.Video?.Start();
                else
                    App.Video?.Stop();
            }
        }

        private void SliderPerHour_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtPerHour.Text = v.ToString();
            s.VideosPerHour = v;
            App.Settings?.Save();
        }

        private void ChkStrict_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var on = ChkStrict.IsChecked ?? false;

            // Lockdown greys this toggle; the rule is checked here too so nothing slips past it.
            if (Services.LockdownStrictHold.RefusesNow(on))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _isLoading = true;
                    ChkStrict.IsChecked = s.StrictLockEnabled;
                    _isLoading = false;
                }));
                return;
            }

            if (on)
            {
                var owner = Application.Current.MainWindow;
                var confirmed = WarningDialog.ShowDoubleWarning(owner,
                    "Strict Lock",
                    "• You will NOT be able to skip or close videos\n" +
                    "• Videos MUST be watched to completion\n" +
                    "• The only way out is the panic key (if enabled)\n" +
                    "• This can be very intense and restrictive");

                if (!confirmed)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isLoading = true;
                        ChkStrict.IsChecked = false;
                        _isLoading = false;
                    }));
                    return;
                }
            }

            s.StrictLockEnabled = on;
            App.Settings?.Save();
        }

        private void ChkMiniGame_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.AttentionChecksEnabled = ChkMiniGame.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderTargets_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtTargets.Text = v.ToString();
            s.AttentionDensity = v;
            App.Settings?.Save();
        }

        private void ChkRandomize_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.RandomizeAttentionTargets = ChkRandomize.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtDuration.Text = v.ToString();
            s.AttentionLifespan = v;
            App.Settings?.Save();
        }

        private void SliderTargetSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtTargetSize.Text = v.ToString();
            s.AttentionSize = v;
            App.Settings?.Save();
        }

        private void BtnManageAttention_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            var dialog = new TextEditorDialog("Attention Targets", s.AttentionPool)
            {
                Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
            };
            if (dialog.ShowDialog() == true && dialog.ResultData != null)
            {
                s.AttentionPool = dialog.ResultData;
                App.Settings?.Save();
                App.Logger?.Information("Attention pool updated: {Count} items", dialog.ResultData.Count);
            }
        }

        private void BtnAttentionStyle_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AttentionTargetEditorDialog
            {
                Owner = Window.GetWindow(this) ?? Application.Current.MainWindow
            };
            dialog.ShowDialog();
        }

        private void BtnTestVideo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (App.Video?.IsPlaying == true)
                {
                    var result = MessageBox.Show(
                        "A video appears to be playing.\n\nIf you don't see a video, it may be stuck. Click Yes to force reset and try again.",
                        "Video Playing",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        App.Logger?.Warning("User requested force reset of stuck video state");
                        App.Video?.ForceCleanup();
                        // Also tear down any web-video takeover: ForceReset alone frees the
                        // queue slot but leaves the browser video playing, and the test
                        // video would then stack on top of it.
                        App.Autonomy?.ForceEndWebVideoTakeover();
                        App.InteractionQueue?.ForceReset();
                    }
                    else return;
                }

                if (App.InteractionQueue != null && !App.InteractionQueue.CanStart)
                {
                    var result = MessageBox.Show(
                        $"Another interaction is in progress ({App.InteractionQueue.CurrentInteraction}).\n\nIf this seems stuck, click Yes to force reset and try again.",
                        "Please Wait",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        App.Video?.ForceCleanup();
                        App.Autonomy?.ForceEndWebVideoTakeover();
                        App.InteractionQueue.ForceReset();
                    }
                    else return;
                }

                // userInitiated: a guard that stands this video down (today the For You feed) must
                // say so rather than leave the button looking broken (#1239).
                App.Video?.TriggerVideo(userInitiated: true);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Error in BtnTestVideo_Click");
                MessageBox.Show($"Error triggering video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // =====================================================================================
        //  feature art (mod-aware)
        // =====================================================================================

        /// <summary>
        /// This page's art under <c>Resources/features/</c>. Verbatim the file the XAML already
        /// declares as its pack:// default on both plates - naming it here changes WHICH lookup
        /// runs, never WHICH file is asked for.
        /// </summary>
        private const string FeatureArtPath = "features/mandatory_videos.png";

        /// <summary>
        /// Pushes the (possibly mod-overridden) feature art into the 72px hero plate and the tall
        /// side plate. Both plates author a pack:// default in XAML, so a null resolve here leaves
        /// the built-in art standing rather than blanking the plate - the same degrade rule
        /// <c>RemoteControlTabView.ApplyFeatureArt</c> follows.
        ///
        /// <para>Two widths, not one: the hero is 240px wide and the side plate is a full-height
        /// column, and <see cref="Services.ModResourceResolver.ResolveImageDecoded"/> keys its cache on the
        /// width, so each is decoded once for the whole session per mod.</para>
        ///
        /// <para>The brushes are mutated in place. Swapping the <c>Border.Background</c> object
        /// would work too and would throw away the XAML-declared Stretch/AlignmentX/Opacity with
        /// it; a frozen brush would silently never repaint at all, which is why they are named
        /// rather than declared inline as literals.</para>
        /// </summary>
        private void ApplyFeatureArt()
        {
            try
            {
                var hero = Services.ModResourceResolver.ResolveImageDecoded(FeatureArtPath, 480);
                if (hero != null && HeroArtBrush is { IsFrozen: false }) HeroArtBrush.ImageSource = hero;

                var side = Services.ModResourceResolver.ResolveImageDecoded(FeatureArtPath, 800);
                if (side != null && SideArtBrush is { IsFrozen: false }) SideArtBrush.ImageSource = side;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("VideoFeatureControl.ApplyFeatureArt: {E}", ex.Message);
            }
        }

        /// <summary>
        /// ModChanged can be raised off the UI thread, so every body it reaches is marshalled.
        /// Subscribed on Loaded and dropped on Unloaded: the rack hosts this control
        /// PERMANENTLY, so an unbalanced hook would accumulate one dead handler per re-host.
        /// </summary>
        private void OnModChanged(object? sender, Models.ModPackage mod)
        {
            Dispatcher.BeginInvoke(new Action(ApplyFeatureArt));
        }

    }
}
