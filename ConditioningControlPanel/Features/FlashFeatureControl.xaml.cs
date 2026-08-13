using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Features
{
    public partial class FlashFeatureControl : UserControl, ISettingsRebindable
    {
        private bool _isLoading = true;

        public FlashFeatureControl()
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
                ChkEnable.IsChecked = s.FlashEnabled;
                SliderFrequency.Value = s.FlashFrequency;
                TxtFrequency.Text = s.FlashFrequency.ToString();
                SliderImages.Value = s.SimultaneousImages;
                TxtImages.Text = s.SimultaneousImages.ToString();
                SliderMaxOnScreen.Value = s.HydraLimit;
                TxtMaxOnScreen.Text = s.HydraLimit.ToString();
                ChkClickable.IsChecked = s.FlashClickable;
                ChkCorruption.IsChecked = s.CorruptionMode;
                ChkHydraLinked.IsChecked = s.HydraLinkedTiming;
                ChkGlow.IsChecked = s.FlashGlowEnabled;
                ChkSolidMode.IsChecked = s.FlashSolidMode;
                ChkFlashGazePop.IsChecked = s.FlashGazePopEnabled;
                ChkFlashGazeLinger.IsChecked = s.FlashGazeLingerEnabled;
                SliderFlashLingerMs.Value = s.FlashGazeLingerExtensionMs;
                TxtFlashLingerMs.Text = $"{s.FlashGazeLingerExtensionMs} ms";
                ChkFlashAvoidCenter.IsChecked = s.FlashAvoidCenter;
                SliderCenterExclusion.Value = s.FlashCenterExclusionPercent;
                TxtCenterExclusion.Text = $"{s.FlashCenterExclusionPercent}%";
            }
            finally { _isLoading = false; }
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Reload on any flash-related property; the set is small.
            if (e.PropertyName == nameof(Models.AppSettings.FlashEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.FlashFrequency) ||
                e.PropertyName == nameof(Models.AppSettings.SimultaneousImages) ||
                e.PropertyName == nameof(Models.AppSettings.HydraLimit) ||
                e.PropertyName == nameof(Models.AppSettings.FlashClickable) ||
                e.PropertyName == nameof(Models.AppSettings.CorruptionMode) ||
                e.PropertyName == nameof(Models.AppSettings.HydraLinkedTiming) ||
                e.PropertyName == nameof(Models.AppSettings.FlashGlowEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.FlashSolidMode) ||
                e.PropertyName == nameof(Models.AppSettings.FlashGazePopEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.FlashGazeLingerEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.FlashGazeLingerExtensionMs) ||
                e.PropertyName == nameof(Models.AppSettings.FlashAvoidCenter) ||
                e.PropertyName == nameof(Models.AppSettings.FlashCenterExclusionPercent))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkFlashGazePop_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FlashGazePopEnabled = ChkFlashGazePop.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void ChkFlashGazeLinger_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FlashGazeLingerEnabled = ChkFlashGazeLinger.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderFlashLingerMs_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtFlashLingerMs.Text = $"{v} ms";
            s.FlashGazeLingerExtensionMs = v;
            App.Settings?.Save();
        }

        /// <summary>
        /// #770/#859 — keeps flashes out of a centered square on every monitor so they never
        /// cover a game's crosshair. Global user preference: sessions and presets never touch
        /// it, and this control is its only UI surface.
        /// </summary>
        private void ChkFlashAvoidCenter_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var on = ChkFlashAvoidCenter.IsChecked ?? false;
            s.FlashAvoidCenter = on;
            App.Logger?.Information("Flash avoid-center toggled: {Enabled} ({Pct}%)",
                on, s.FlashCenterExclusionPercent);
            App.Settings?.Save();
        }

        /// <summary>
        /// #770 — size of the centered no-flash square, as a % of the shorter monitor edge.
        /// AppSettings clamps to 5-60; the slider carries the same range.
        /// </summary>
        private void SliderCenterExclusion_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtCenterExclusion.Text = $"{v}%";
            s.FlashCenterExclusionPercent = v;
            App.Settings?.Save();
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var on = ChkEnable.IsChecked ?? false;
            s.FlashEnabled = on;
            App.Settings?.Save();

            // Live-apply: start/stop flash service if engine is running
            if (App.IsEngineRunning)
            {
                if (on)
                    App.Flash?.Start();
                else
                    App.Flash?.Stop();
            }
        }

        private void SliderFrequency_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtFrequency.Text = v.ToString();
            s.FlashFrequency = v;
            try { App.Flash?.RefreshSchedule(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Flash RefreshSchedule failed"); }
            App.Settings?.Save();
        }

        private void SliderImages_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtImages.Text = v.ToString();
            s.SimultaneousImages = v;
            App.Settings?.Save();
        }

        private void SliderMaxOnScreen_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtMaxOnScreen.Text = v.ToString();
            s.HydraLimit = v;
            App.Settings?.Save();
        }

        private void ChkClickable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FlashClickable = ChkClickable.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void ChkCorruption_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.CorruptionMode = ChkCorruption.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void ChkHydraLinked_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.HydraLinkedTiming = ChkHydraLinked.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void ChkGlow_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FlashGlowEnabled = ChkGlow.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void ChkSolidMode_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FlashSolidMode = ChkSolidMode.IsChecked ?? false;
            App.Settings?.Save();
            // No service bounce needed: each spawn reads the setting, so the next flash uses the
            // new mode. Live flashes finish out on whichever renderer spawned them.
        }

        // =====================================================================================
        //  feature art (mod-aware)
        // =====================================================================================

        /// <summary>
        /// This page's art under <c>Resources/features/</c>. Verbatim the file the XAML already
        /// declares as its pack:// default on both plates - naming it here changes WHICH lookup
        /// runs, never WHICH file is asked for.
        /// </summary>
        private const string FeatureArtPath = "features/flash.png";

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
                App.Logger?.Debug("FlashFeatureControl.ApplyFeatureArt: {E}", ex.Message);
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
