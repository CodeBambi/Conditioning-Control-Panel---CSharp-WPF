using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Services.Prizes;

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
            // The picker's rows come from ownership, so build them before the settings load
            // selects one. GrantsChanged rebuilds them (a purchase, a sync, a logout).
            PrizeGrants.GrantsChanged += OnGrantsChanged;
            BuildMotionPicker();
            RebindToCurrentSettings();
            // The hero and side plates are mod art; the rack hosts this control permanently, so a
            // mod switch must repaint them (a popup instance never lived long enough to care).
            ApplyFeatureArt();
            if (App.Mods != null) App.Mods.ModChanged += OnModChanged;
            RowGetFlashesV2.Configure(V2PurchaseRule.FlashesPrizeId, "v2_get_flash_blurb", "section_flash_v2");
            RowGetFlashesV2.RowChanged += OnGetRowChanged;
            RefreshV2Box();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            PrizeGrants.GrantsChanged -= OnGrantsChanged;
            _settingsHook?.Unhook();
            if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
            RowGetFlashesV2.RowChanged -= OnGetRowChanged;
        }

        // The Get it row decides its own visibility; the box only needs to know whether anything is
        // left in it. It repaints on its own schedule (a counter read landing, a sign-in), which is
        // why the box is re-measured from the row rather than from ownership alone.
        private void OnGetRowChanged(object? sender, EventArgs e) => RefreshV2Box();

        // The Flashes v2 box and its rows: visibility is ownership, never settings (the dashboard
        // flash card's own v2 pill counts these prizes too - that check lives in
        // SettingsTabView.RefreshV2Badges). Each row hides on its own grant and the box collapses
        // once no row is left, so an account with nothing v2 sees the flash options unchanged.
        // BuildMotionPicker owns RowMotion's visibility; it runs alongside this on every refresh.
        private void RefreshV2Box()
        {
            bool remix = PrizeGrants.IsGranted(PrizeGrants.JackpotRemix);
            bool motion = PrizeGrants.IsGranted(PrizeGrants.FlashDriftBounce)
                          || PrizeGrants.IsGranted(PrizeGrants.FlashPendulum);
            RowJackpotRemix.Visibility = remix ? Visibility.Visible : Visibility.Collapsed;
            // Rounded corners dresses the picture the motion prizes animate and dragging replaces
            // the way it moves, so both ride those grants rather than Jackpot Remix.
            RowRoundedCorners.Visibility = motion ? Visibility.Visible : Visibility.Collapsed;
            RowDraggable.Visibility = motion ? Visibility.Visible : Visibility.Collapsed;
            // Shatter dresses the way that same picture leaves, so it rides those grants too.
            RowShatter.Visibility = motion ? Visibility.Visible : Visibility.Collapsed;
            // The box no longer collapses on ownership alone: with nothing owned it holds the offer
            // to buy the prize, which is the whole point of that row being there.
            BoxFlashV2.Visibility = (remix || motion || !RowGetFlashesV2.IsRowHidden)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // One hook for both ownership-driven pieces of this control: the motion picker's rows
        // (Flashes v2) and the Jackpot Remix toggle row. Each lane arrived with its own handler
        // of this name; they are folded together here so the subscription stays single.
        private void OnGrantsChanged() => Dispatcher.BeginInvoke(new Action(() =>
        {
            BuildMotionPicker();
            RefreshV2Box();
        }));

        // Rounded corners: every render path reads the setting at spawn, so live flashes finish
        // out square and the next one is round. No service bounce.
        private void ChkFlashRoundedCorners_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FlashRoundedCorners = ChkFlashRoundedCorners.IsChecked ?? false;
            App.Settings?.Save();
        }

        // Draggable GIFs: the flash heartbeat re-reads this every tick to decide whether a press
        // grabs or pops, so the switch takes effect on flashes that are already up. No bounce.
        private void ChkFlashDraggable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FlashDraggable = ChkFlashDraggable.IsChecked ?? false;
            App.Settings?.Save();
        }

        // Shatter: read at the dismiss, so the switch takes effect on flashes already on screen.
        // No service bounce.
        private void ChkFlashShatter_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.FlashShatterEnabled = ChkFlashShatter.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void ChkJackpotRemix_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.JackpotRemixEnabled = ChkJackpotRemix.IsChecked ?? false;
            App.Settings?.Save();
            // No service bounce: the director reads the setting on every roll and poll.
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
                SelectMotion(s.FlashMotionStyle);
                ChkFlashGazePop.IsChecked = s.FlashGazePopEnabled;
                ChkFlashGazeLinger.IsChecked = s.FlashGazeLingerEnabled;
                SliderFlashLingerMs.Value = s.FlashGazeLingerExtensionMs;
                TxtFlashLingerMs.Text = $"{s.FlashGazeLingerExtensionMs} ms";
                ChkFlashAvoidCenter.IsChecked = s.FlashAvoidCenter;
                SliderCenterExclusion.Value = s.FlashCenterExclusionPercent;
                TxtCenterExclusion.Text = $"{s.FlashCenterExclusionPercent}%";
                ChkJackpotRemix.IsChecked = s.JackpotRemixEnabled;
                ChkFlashRoundedCorners.IsChecked = s.FlashRoundedCorners;
                ChkFlashDraggable.IsChecked = s.FlashDraggable;
                ChkFlashShatter.IsChecked = s.FlashShatterEnabled;
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
                e.PropertyName == nameof(Models.AppSettings.FlashMotionStyle) ||
                e.PropertyName == nameof(Models.AppSettings.FlashRoundedCorners) ||
                e.PropertyName == nameof(Models.AppSettings.FlashDraggable) ||
                e.PropertyName == nameof(Models.AppSettings.FlashShatterEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.FlashGazePopEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.FlashGazeLingerEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.FlashGazeLingerExtensionMs) ||
                e.PropertyName == nameof(Models.AppSettings.FlashAvoidCenter) ||
                e.PropertyName == nameof(Models.AppSettings.FlashCenterExclusionPercent) ||
                e.PropertyName == nameof(Models.AppSettings.JackpotRemixEnabled))
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
        //  Flashes v2 motion picker (Back Room prizes)
        // =====================================================================================

        /// <summary>
        /// Rebuilds the picker from ownership: Still always, each OWNED v2 style wearing the v2
        /// pill, and Mix once anything v2 is owned. Ownership is PrizeGrants' word, never a
        /// setting; with nothing owned the whole row stays hidden (a one-choice picker is noise).
        /// </summary>
        private void BuildMotionPicker()
        {
            var wasLoading = _isLoading;
            _isLoading = true;
            try
            {
                bool drift = PrizeGrants.IsGranted(PrizeGrants.FlashDriftBounce);
                bool pendulum = PrizeGrants.IsGranted(PrizeGrants.FlashPendulum);
                RowMotion.Visibility = (drift || pendulum) ? Visibility.Visible : Visibility.Collapsed;
                CmbMotion.Items.Clear();
                AddMotionChoice(Models.FlashMotionStyle.Still, "option_flash_motion_still", v2: false);
                if (drift) AddMotionChoice(Models.FlashMotionStyle.DriftBounce, "option_flash_motion_drift", v2: true);
                if (pendulum) AddMotionChoice(Models.FlashMotionStyle.Pendulum, "option_flash_motion_pendulum", v2: true);
                if (drift || pendulum) AddMotionChoice(Models.FlashMotionStyle.Mix, "option_flash_motion_mix", v2: false);
                SelectMotion(App.Settings?.Current?.FlashMotionStyle ?? Models.FlashMotionStyle.Still);
            }
            catch (Exception ex) { App.Logger?.Debug("FlashFeatureControl.BuildMotionPicker: {E}", ex.Message); }
            finally { _isLoading = wasLoading; }
        }

        /// <summary>
        /// The app-wide "a ComboBox needs an explicit black Foreground" rule is about the STOCK
        /// template, whose popup is a light system surface. This picker is styled
        /// <c>DarkComboBoxStyle</c>, whose popup is ElevatedSurface (#222240) - and no mod
        /// overrides that key, so black rows measured 1.7:1 on every skin (Wobberjockey read them
        /// on Circe, tier2 2026-09-19). Theme text, like the Bubble Pop picker beside it.
        /// The closed box shows a VisualBrush of the selected row, so this one brush paints both.
        /// </summary>
        private static System.Windows.Media.Brush RowTextBrush()
            => (System.Windows.Media.Brush?)Application.Current?.TryFindResource("TextLightBrush")
               ?? System.Windows.Media.Brushes.White;

        private void AddMotionChoice(Models.FlashMotionStyle style, string key, bool v2)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock
            {
                Text = Localization.Loc.Get(key),
                Foreground = RowTextBrush(),
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (v2) row.Children.Add(FeatureCard.NewV2Badge(new Thickness(8, 0, 0, 0)));
            // No Foreground on the CONTAINER: a local value beats a style trigger, so setting one
            // here would suppress DarkComboBoxStyle's own IsHighlighted / IsSelected foregrounds
            // and the hovered row would stop lifting. The row's TextBlock carries the colour, the
            // same way the Bubble Pop picker does it.
            CmbMotion.Items.Add(new ComboBoxItem { Content = row, Tag = style });
        }

        /// <summary>
        /// Selects the row for <paramref name="style"/>. A style this account does not own (a
        /// synced profile) has no row and shows as Still, which is exactly how it plays.
        /// </summary>
        private void SelectMotion(Models.FlashMotionStyle style)
        {
            if (CmbMotion.Items.Count == 0) return;
            ComboBoxItem? match = null;
            foreach (ComboBoxItem item in CmbMotion.Items)
            {
                if (item.Tag is Models.FlashMotionStyle s && s == style) { match = item; break; }
            }
            CmbMotion.SelectedItem = match ?? CmbMotion.Items[0];
        }

        private void CmbMotion_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            if (CmbMotion.SelectedItem is not ComboBoxItem item || item.Tag is not Models.FlashMotionStyle style) return;
            if (s.FlashMotionStyle == style) return;
            s.FlashMotionStyle = style;
            App.Settings?.Save();
            // No service bounce: every spawn resolves the picker, so the next flash uses it.
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
