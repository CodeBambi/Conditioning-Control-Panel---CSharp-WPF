using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Studio
{
    /// <summary>
    /// Phase 4 rescue of gap-report G2 — the Studio rack's Brain Drain panel.
    ///
    /// Behaviour is copied verbatim from the dead handlers in
    /// <c>MainWindow/MainWindow.LevelFeatures.cs:282-334</c> (which were never wired to any XAML,
    /// so they have only ever been a specification). The single substitution is
    /// <c>App.IsEngineRunning</c> for MainWindow's private <c>_isRunning</c> — the same field,
    /// exposed as <c>MainWindow.IsEngineRunning</c> and mirrored onto App by StartStop.
    ///
    /// Everything here drives <see cref="Services.BrainDrainService"/> (audio) only. The blur half
    /// is withheld behind <c>OverlayService.BrainDrainWithheld</c>; this panel reads that flag and
    /// nothing else, so it can never disagree with the gate.
    /// </summary>
    public partial class BrainDrainFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public BrainDrainFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplyWithheldPresentation();
            LoadFromSettings();
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged += OnSettingsPropertyChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (App.Settings?.Current is INotifyPropertyChanged inpc)
                inpc.PropertyChanged -= OnSettingsPropertyChanged;
        }

        /// <summary>
        /// The rework notice is the honest half of this panel: while
        /// <c>OverlayService.BrainDrainWithheld</c> is true the screen effect is silently skipped
        /// for every caller, so the copy has to say so. Read once per load rather than cached —
        /// the flag is <c>static readonly</c>, not <c>const</c>, precisely so the surrounding code
        /// stays reachable when it flips.
        /// </summary>
        private void ApplyWithheldPresentation()
        {
            WithheldNotice.Visibility = ConditioningControlPanel.Services.OverlayService.BrainDrainWithheld
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.BrainDrainEnabled;
                SliderIntensity.Value = s.BrainDrainIntensity;
                TxtIntensity.Text = $"{s.BrainDrainIntensity}%";
                ChkHighRefresh.IsChecked = s.BrainDrainHighRefresh;

                // An empty Resources/sounds/braindrain folder makes the whole feature a silent
                // no-op (the service warns to the log and returns). Surface it instead.
                var clips = App.BrainDrain?.AudioFileCount ?? 0;
                NoAudioHint.Visibility = clips == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { _isLoading = false; }
        }

        /// <summary>
        /// Keeps the panel honest when something else moves the dials: DTRH, the Deeper editor,
        /// preset apply (<c>Models/Preset.cs:413</c>), the autonomy voice command, and
        /// <c>MainWindow.EnableBrainDrain</c> / <c>UpdateBrainDrainIntensity</c> all write these
        /// three settings directly.
        /// </summary>
        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.BrainDrainEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BrainDrainIntensity) ||
                e.PropertyName == nameof(Models.AppSettings.BrainDrainHighRefresh))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var isEnabled = ChkEnable.IsChecked ?? false;
            s.BrainDrainEnabled = isEnabled;

            if (App.IsEngineRunning)
            {
                try
                {
                    if (isEnabled)
                        App.BrainDrain?.Start();
                    else
                        App.BrainDrain?.Stop();
                }
                catch (Exception ex) { App.Logger?.Warning(ex, "Brain Drain start/stop failed"); }
                App.Logger?.Information("Brain Drain toggled: {Enabled}", isEnabled);
            }

            MirrorToLegacyProgressionControls(s);
            App.Settings?.Save();
        }

        private void SliderIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var v = (int)e.NewValue;
            TxtIntensity.Text = $"{v}%";
            s.BrainDrainIntensity = v;

            if (App.IsEngineRunning)
            {
                try { App.BrainDrain?.UpdateSettings(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "Brain Drain UpdateSettings failed"); }
            }

            MirrorToLegacyProgressionControls(s);
            App.Settings?.Save();
        }

        private void ChkHighRefresh_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var isHighRefresh = ChkHighRefresh.IsChecked ?? false;
            s.BrainDrainHighRefresh = isHighRefresh;

            // The tick interval is only read at Start(), so a running service has to be bounced
            // for the new interval to take effect. Verbatim from the LevelFeatures.cs spec.
            if (App.IsEngineRunning && (App.BrainDrain?.IsRunning ?? false))
            {
                try
                {
                    App.BrainDrain?.Stop();
                    App.BrainDrain?.Start();
                }
                catch (Exception ex) { App.Logger?.Warning(ex, "Brain Drain restart failed"); }
            }

            App.Logger?.Information("Brain Drain High Refresh toggled: {Enabled}", isHighRefresh);
            MirrorToLegacyProgressionControls(s);
            App.Settings?.Save();
        }

        /// <summary>
        /// TEMPORARY, DIES WITH THE GHOST TAB (Phase 8).
        ///
        /// <c>MainWindow.SaveSettings()</c> still writes all three Brain Drain settings FROM the
        /// dead ProgressionTab controls (<c>MainWindow/MainWindow.Settings.cs:504-506</c>), and it
        /// runs on session start (<c>StartStop.cs:158</c>), on close, and from several settings
        /// dialogs. Those controls are only ever filled at load time, so without this mirror every
        /// change made here would be silently reverted the next time the user pressed Start —
        /// the exact class of bug the Flash exclusion-box rescue avoided by having its legacy
        /// round-trip deleted along with its old markup.
        ///
        /// Writing them back is safe: the ProgressionTab copies carry no Checked/ValueChanged
        /// attributes at all, so nothing re-enters. When Phase 8 deletes ProgressionTabView it must
        /// delete lines 504-506 of Settings.cs and this method together.
        /// </summary>
        private void MirrorToLegacyProgressionControls(Models.AppSettings s)
        {
            try
            {
                // App.MainWindowRef first, per App.xaml.cs:2025 — Application.Current.MainWindow
                // is not reliably MainWindow, and Window.GetWindow(this) would return the pop-out
                // FeaturePopupWindow when this panel is shown there instead of in the rack.
                var mw = App.MainWindowRef ?? (Application.Current?.MainWindow as ConditioningControlPanel.MainWindow);
                if (mw == null) return;
                var prog = mw.ProgressionTab;
                if (prog == null) return;

                if (prog.ChkBrainDrainEnabled != null) prog.ChkBrainDrainEnabled.IsChecked = s.BrainDrainEnabled;
                if (prog.SliderBrainDrainIntensity != null) prog.SliderBrainDrainIntensity.Value = s.BrainDrainIntensity;
                if (prog.ChkBrainDrainHighRefresh != null) prog.ChkBrainDrainHighRefresh.IsChecked = s.BrainDrainHighRefresh;
                if (prog.TxtBrainDrainIntensity != null) prog.TxtBrainDrainIntensity.Text = $"{s.BrainDrainIntensity}%";
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Studio] Brain Drain legacy mirror failed");
            }
        }
    }
}
