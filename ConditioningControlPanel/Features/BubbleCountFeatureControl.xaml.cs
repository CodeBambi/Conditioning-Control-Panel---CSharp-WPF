using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class BubbleCountFeatureControl : UserControl, ISettingsRebindable
    {
        private bool _isLoading = true;

        public BubbleCountFeatureControl()
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
        }

        private void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.BubbleCountEnabled;
                SliderFreq.Value = s.BubbleCountFrequency;
                TxtFreq.Text = s.BubbleCountFrequency.ToString();
                // Select matching ComboBoxItem by Tag
                foreach (ComboBoxItem item in CmbDifficulty.Items)
                {
                    if (item.Tag is string tag && int.TryParse(tag, out var val) && val == s.BubbleCountDifficulty)
                    {
                        CmbDifficulty.SelectedItem = item;
                        break;
                    }
                }
                ChkStrict.IsChecked = s.BubbleCountStrictLock;
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.BubbleCountEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleCountFrequency) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleCountDifficulty) ||
                e.PropertyName == nameof(Models.AppSettings.BubbleCountStrictLock))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var on = ChkEnable.IsChecked ?? false;
            s.BubbleCountEnabled = on;
            App.Settings?.Save();

            // Live-apply: start/stop bubble count service if engine is running
            if (App.IsEngineRunning)
            {
                if (on)
                    App.BubbleCount?.Start();
                else
                    App.BubbleCount?.Stop();
            }
        }

        private void SliderFreq_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtFreq.Text = v.ToString();
            s.BubbleCountFrequency = v;
            try { App.BubbleCount?.RefreshSchedule(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "BubbleCount RefreshSchedule failed"); }
            App.Settings?.Save();
        }

        private void CmbDifficulty_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (CmbDifficulty.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag && int.TryParse(tag, out var difficulty))
            {
                var s = App.Settings?.Current;
                if (s == null) return;
                s.BubbleCountDifficulty = difficulty;
                App.Settings?.Save();
            }
        }

        private void ChkStrict_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;

            var on = ChkStrict.IsChecked ?? false;
            if (on)
            {
                var owner = Application.Current.MainWindow;
                var confirmed = WarningDialog.ShowDoubleWarning(owner,
                    "Strict Bubble Count",
                    "• You will NOT be able to skip the bubble count challenge\n" +
                    "• You MUST answer correctly to dismiss\n" +
                    "• Wrong answers force you to REWATCH the video\n" +
                    "• Mercy system grants escape after 3 retries (if enabled)\n" +
                    "• This can be very restrictive!");

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

            s.BubbleCountStrictLock = on;
            App.Settings?.Save();
        }

        private void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            App.BubbleCount?.TriggerGame(forceTest: true);
        }

        // =====================================================================================
        //  feature art (mod-aware)
        // =====================================================================================

        /// <summary>
        /// This page's art under <c>Resources/features/</c>. Verbatim the file the XAML already
        /// declares as its pack:// default on both plates - naming it here changes WHICH lookup
        /// runs, never WHICH file is asked for.
        /// </summary>
        private const string FeatureArtPath = "features/Bubble_count.png";

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
                App.Logger?.Debug("BubbleCountFeatureControl.ApplyFeatureArt: {E}", ex.Message);
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
