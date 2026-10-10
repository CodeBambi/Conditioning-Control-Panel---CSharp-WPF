using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Flash Images panel, ported from the WPF head, settings logic restored against
    /// <see cref="CoreSettings"/>. Every toggle and slider round-trips the AppSettings property
    /// the WPF original wrote and saves, and the slider read-outs are repainted the same way.
    ///
    /// <para>The WPF <c>SettingsHook</c>/<c>ISettingsRebindable</c> pair is inlined: a cloud
    /// restore SWAPS the settings instance, so the PropertyChanged subscription is tracked by
    /// instance and re-pointed on <c>SettingsService.CurrentReplaced</c>.</para>
    ///
    /// <para>What is still head-side is named at each handler: the FlashService live-apply
    /// (start/stop and RefreshSchedule) and the mod-art repaint.</para>
    /// </summary>
    public partial class FlashFeatureControl : UserControl
    {
        private bool _isLoading = true;

        public FlashFeatureControl()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and everything below reads them.
            InitializeComponent();
            // WPF ApplyFeatureArt: hero + side plates from features/flash.png, mod override first, repainted on a mod switch.
            Helpers.ModArt.BindFeaturePlates(this, "features/flash.png", HeroArt, SideArt);

            ChkEnable.IsCheckedChanged += ChkEnable_Changed;
            BtnTestFlash.Click += (_, _) => Overlays.FlashOverlay.TriggerOnce(this);
            // No click-through overlays on this platform (headless, native Wayland): nothing to test.
            BtnTestFlash.IsEnabled = Platform.X11Overlay.IsAvailable;
            SliderFrequency.ValueChanged += SliderFrequency_Changed;
            SliderImages.ValueChanged += SliderImages_Changed;
            ChkRandomImages.IsCheckedChanged += ChkRandomImages_Changed;
            SliderImagesMin.ValueChanged += SliderImagesMin_Changed;
            SliderMaxOnScreen.ValueChanged += SliderMaxOnScreen_Changed;
            ChkClickable.IsCheckedChanged += ChkClickable_Changed;
            ChkStayUntilPopped.IsCheckedChanged += ChkStayUntilPopped_Changed;
            CmbExit.SelectionChanged += CmbExit_Changed;
            ChkFlashRoundedCorners.IsCheckedChanged += ChkFlashRoundedCorners_Changed;
            ChkFlashDraggable.IsCheckedChanged += ChkFlashDraggable_Changed;
            ChkFlashShatter.IsCheckedChanged += ChkFlashShatter_Changed;
            ChkCorruption.IsCheckedChanged += ChkCorruption_Changed;
            ChkHydraLinked.IsCheckedChanged += ChkHydraLinked_Changed;
            ChkGlow.IsCheckedChanged += ChkGlow_Changed;
            ChkSolidMode.IsCheckedChanged += ChkSolidMode_Changed;
            ChkFlashAvoidCenter.IsCheckedChanged += ChkFlashAvoidCenter_Changed;
            SliderCenterExclusion.ValueChanged += SliderCenterExclusion_Changed;
            ChkFlashGazePop.IsCheckedChanged += ChkFlashGazePop_Changed;
            ChkFlashGazeLinger.IsCheckedChanged += ChkFlashGazeLinger_Changed;
            SliderFlashLingerMs.ValueChanged += SliderFlashLingerMs_Changed;
            CmbMotion.SelectionChanged += CmbMotion_Changed;
            SliderDriftSpeed.ValueChanged += SliderDriftSpeed_Changed;

            LoadFromSettings();

            // The hero and side plates repaint themselves on a mod switch (ModArt.BindFeaturePlates).
        }

        // ---- settings instance tracking (WPF: SettingsHook + ISettingsRebindable) --------------

        private AppSettings? _hooked;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            Platform.PrizeOwnership.Changed += OnGrantsChanged;
            // WPF OnLoaded: the Get it row names its prize and tells the box when to re-measure.
            RowGetFlashesV2.RowChanged -= OnGetRowChanged;
            RowGetFlashesV2.RowChanged += OnGetRowChanged;
            RowGetFlashesV2.Configure(Platform.V2PurchaseRule.FlashesPrizeId, "v2_get_flash_blurb", "section_flash_v2");
            RebindToCurrentSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            Platform.PrizeOwnership.Changed -= OnGrantsChanged;
            Unhook();
            base.OnDetachedFromVisualTree(e);
        }

        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(RebindToCurrentSettings);

        /// <summary>WPF OnGrantsChanged: a prize granted (or an account cleared) while the panel is up
        /// rebuilds the motion picker and the v2 rows. Raised off the UI thread, so it is posted.</summary>
        // WPF OnGetRowChanged: the row repaints on its own schedule (a counter read landing, a
        // sign-in), so the box is re-measured from the row and not from ownership alone.
        private void OnGetRowChanged(object? sender, EventArgs e) =>
            BoxFlashV2.IsVisible = RowRoundedCorners.IsVisible || !RowGetFlashesV2.IsRowHidden;

        private void OnGrantsChanged() => Dispatcher.UIThread.Post(() =>
        {
            var was = _isLoading;
            _isLoading = true;
            try { BuildMotionPicker(); }
            finally { _isLoading = was; }
        });

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
                ChkEnable.IsChecked = s.FlashEnabled;
                SliderFrequency.Value = s.FlashFrequency;
                TxtFrequency.Text = s.FlashFrequency.ToString();
                SliderImages.Value = s.SimultaneousImages;
                TxtImages.Text = s.SimultaneousImages.ToString();
                ChkRandomImages.IsChecked = s.SimultaneousImagesRandom;
                SliderImagesMin.Value = s.SimultaneousImagesMin;
                TxtImagesMin.Text = s.SimultaneousImagesMin.ToString();
                RowImagesMin.IsVisible = s.SimultaneousImagesRandom;
                SliderMaxOnScreen.Value = s.HydraLimit;
                TxtMaxOnScreen.Text = s.HydraLimit.ToString();
                ChkClickable.IsChecked = s.FlashClickable;
                ChkStayUntilPopped.IsChecked = s.FlashStayUntilPopped;
                SelectExit(s.FlashExitStyle);
                ChkFlashRoundedCorners.IsChecked = s.FlashRoundedCorners;
                ChkFlashDraggable.IsChecked = s.FlashDraggable;
                ChkFlashShatter.IsChecked = s.FlashShatterEnabled;
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
                SliderDriftSpeed.Value = s.FlashDriftSpeed;
                TxtDriftSpeed.Text = FormatDriftSpeed(s.FlashDriftSpeed);
                BuildMotionPicker();
            }
            finally { _isLoading = false; }
        }

        // ---- Flashes v2 motion picker (WPF FlashFeatureControl.xaml.cs:414-560) ----------------
        // Rebuilt on load, on a settings rebind and on PrizeOwnership.Changed (WPF GrantsChanged).

        /// <summary>Still always, Drift and Bounce with its v2 pill when owned, Mix once anything v2 is
        /// owned; with nothing owned the box stays hidden. Pendulum is not offered: this head cannot
        /// play it yet (it would play Still).</summary>
        private void BuildMotionPicker()
        {
            var drift = Platform.PrizeOwnership.IsGranted(Platform.PrizeOwnership.FlashDriftBounce);
            // WPF RefreshV2Box: rounded corners, dragging and shatter dress the picture the motion
            // prizes animate, so they ride those grants (either one). No Jackpot Remix row here.
            var motion = drift || Platform.PrizeOwnership.IsGranted(Platform.PrizeOwnership.FlashPendulum);
            RowMotion.IsVisible = drift;
            RowRoundedCorners.IsVisible = RowDraggable.IsVisible = RowShatter.IsVisible = motion;
            // The box no longer collapses on ownership alone: with nothing owned it holds the offer
            // to buy the prize (WPF RefreshV2Box).
            BoxFlashV2.IsVisible = motion || !RowGetFlashesV2.IsRowHidden;
            RefreshExitRow();
            CmbMotion.Items.Clear();
            AddMotionChoice(FlashMotionStyle.Still, "option_flash_motion_still", v2: false);
            if (drift)
            {
                AddMotionChoice(FlashMotionStyle.DriftBounce, "option_flash_motion_drift", v2: true);
                AddMotionChoice(FlashMotionStyle.Mix, "option_flash_motion_mix", v2: false);
            }
            // A style with no row here (unowned, Pendulum) shows as Still, which is how it plays.
            var style = CoreSettings.Current.FlashMotionStyle;
            CmbMotion.SelectedItem = CmbMotion.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is FlashMotionStyle t && t == style)
                ?? CmbMotion.Items[0];
            UpdateDriftSpeedRow();
        }

        private void AddMotionChoice(FlashMotionStyle style, string key, bool v2)
        {
            var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = ConditioningControlPanel.Localization.Loc.Get(key), VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center });
            if (v2)
                row.Children.Add(new Border
                {
                    Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(6, 2, 7, 3), CornerRadius = new CornerRadius(7),
                    Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x1A, 0x1A, 0x2E)),
                    BorderBrush = V2Brush, BorderThickness = new Thickness(1), IsHitTestVisible = false,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                    Child = new TextBlock { Text = ConditioningControlPanel.Localization.Loc.Get("badge_v2"), Foreground = V2Brush, FontSize = 9, FontWeight = FontWeight.Bold },
                });
            CmbMotion.Items.Add(new ComboBoxItem { Content = row, Tag = style });
        }

        private static readonly IBrush V2Brush = new SolidColorBrush(Color.Parse("#FFE08A"));

        // ---- When clicked: stay until popped, the leave animation (WPF :377, :495-536) ----------

        private static readonly (FlashExitStyle Style, string Key)[] ExitChoices =
        {
            (FlashExitStyle.Mix, "option_flash_exit_mix"),
            (FlashExitStyle.Pop, "option_flash_exit_pop"),
            (FlashExitStyle.TvOff, "option_flash_exit_tvoff"),
            (FlashExitStyle.Spiral, "option_flash_exit_spiral"),
            (FlashExitStyle.Melt, "option_flash_exit_melt"),
            (FlashExitStyle.Glitch, "option_flash_exit_glitch"),
            (FlashExitStyle.None, "option_flash_exit_none"),
        };

        /// <summary>Selects the leave-animation row, building the rows on first use.</summary>
        private void SelectExit(FlashExitStyle style)
        {
            if (CmbExit.Items.Count == 0)
                foreach (var (s, key) in ExitChoices)
                    CmbExit.Items.Add(new ComboBoxItem { Content = ConditioningControlPanel.Localization.Loc.Get(key), Tag = s });
            CmbExit.SelectedItem = CmbExit.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is FlashExitStyle t && t == style)
                ?? CmbExit.Items[0];
        }

        private void CmbExit_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (CmbExit.SelectedItem is not ComboBoxItem { Tag: FlashExitStyle style }) return;
            if (CoreSettings.Current.FlashExitStyle == style) return;
            CoreSettings.Current.FlashExitStyle = style;
            CoreSettings.Save();
        }

        private void ChkStayUntilPopped_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.FlashStayUntilPopped = ChkStayUntilPopped.IsChecked ?? false;
            CoreSettings.Save();
        }

        /// <summary>WPF ShatterDecidesClick (#1386): while an owned Shatter is on it decides what a
        /// click does, so the "when clicked" picker would do nothing. PURE.</summary>
        internal static bool ShatterDecidesClick(bool shatterOn, bool ownsMotion) => shatterOn && ownsMotion;

        // Greys the "when clicked" picker and shows the one-line reason while Shatter owns the click.
        private void RefreshExitRow()
        {
            var shatter = ShatterDecidesClick(CoreSettings.Current.FlashShatterEnabled,
                Platform.PrizeOwnership.IsGranted(Platform.PrizeOwnership.FlashDriftBounce)
                || Platform.PrizeOwnership.IsGranted(Platform.PrizeOwnership.FlashPendulum));
            CmbExit.IsEnabled = !shatter;
            TxtExitShatterNote.IsVisible = shatter;
        }

        // ---- Flashes v2 switches (WPF :89-126). Each spawn, press and dismiss reads the setting,
        // so none of them bounces the service.

        private void ChkFlashRoundedCorners_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.FlashRoundedCorners = ChkFlashRoundedCorners.IsChecked ?? false;
            CoreSettings.Save();
        }

        private void ChkFlashDraggable_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.FlashDraggable = ChkFlashDraggable.IsChecked ?? false;
            CoreSettings.Save();
        }

        private void ChkFlashShatter_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.FlashShatterEnabled = ChkFlashShatter.IsChecked ?? false;
            CoreSettings.Save();
            RefreshExitRow();
        }

        private void CmbMotion_Changed(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (CmbMotion.SelectedItem is not ComboBoxItem { Tag: FlashMotionStyle style }) return;
            UpdateDriftSpeedRow();
            if (CoreSettings.Current.FlashMotionStyle == style) return;
            CoreSettings.Current.FlashMotionStyle = style;
            CoreSettings.Save();
            // No service bounce: every spawn resolves the picker, so the next flash uses it.
        }

        /// <summary>#1265: the speed row shows only while Drift and Bounce is owned and the picker can
        /// roll it (Drift and Bounce itself, or Mix).</summary>
        private void UpdateDriftSpeedRow()
        {
            var picked = (CmbMotion.SelectedItem as ComboBoxItem)?.Tag as FlashMotionStyle?;
            RowDriftSpeed.IsVisible = Platform.PrizeOwnership.IsGranted(Platform.PrizeOwnership.FlashDriftBounce)
                && picked is FlashMotionStyle.DriftBounce or FlashMotionStyle.Mix;
        }

        private static string FormatDriftSpeed(double v)
            => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";

        private void SliderDriftSpeed_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            s.FlashDriftSpeed = e.NewValue;
            TxtDriftSpeed.Text = FormatDriftSpeed(s.FlashDriftSpeed);
            CoreSettings.Save();
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Reload on any flash-related property; the set is small.
            if (e.PropertyName == nameof(AppSettings.FlashEnabled) ||
                e.PropertyName == nameof(AppSettings.FlashFrequency) ||
                e.PropertyName == nameof(AppSettings.SimultaneousImages) ||
                e.PropertyName == nameof(AppSettings.SimultaneousImagesRandom) ||
                e.PropertyName == nameof(AppSettings.SimultaneousImagesMin) ||
                e.PropertyName == nameof(AppSettings.HydraLimit) ||
                e.PropertyName == nameof(AppSettings.FlashClickable) ||
                e.PropertyName == nameof(AppSettings.FlashStayUntilPopped) ||
                e.PropertyName == nameof(AppSettings.FlashExitStyle) ||
                e.PropertyName == nameof(AppSettings.FlashRoundedCorners) ||
                e.PropertyName == nameof(AppSettings.FlashDraggable) ||
                e.PropertyName == nameof(AppSettings.FlashShatterEnabled) ||
                e.PropertyName == nameof(AppSettings.CorruptionMode) ||
                e.PropertyName == nameof(AppSettings.HydraLinkedTiming) ||
                e.PropertyName == nameof(AppSettings.FlashGlowEnabled) ||
                e.PropertyName == nameof(AppSettings.FlashSolidMode) ||
                e.PropertyName == nameof(AppSettings.FlashGazePopEnabled) ||
                e.PropertyName == nameof(AppSettings.FlashGazeLingerEnabled) ||
                e.PropertyName == nameof(AppSettings.FlashGazeLingerExtensionMs) ||
                e.PropertyName == nameof(AppSettings.FlashAvoidCenter) ||
                e.PropertyName == nameof(AppSettings.FlashMotionStyle) ||
                e.PropertyName == nameof(AppSettings.FlashDriftSpeed) ||
                e.PropertyName == nameof(AppSettings.FlashCenterExclusionPercent))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }

        private void ChkFlashGazePop_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.FlashGazePopEnabled = ChkFlashGazePop.IsChecked ?? false;
            CoreSettings.Save();
        }

        private void ChkFlashGazeLinger_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.FlashGazeLingerEnabled = ChkFlashGazeLinger.IsChecked ?? false;
            CoreSettings.Save();
        }

        private void SliderFlashLingerMs_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtFlashLingerMs.Text = $"{v} ms";
            CoreSettings.Current.FlashGazeLingerExtensionMs = v;
            CoreSettings.Save();
        }

        /// <summary>
        /// #770/#859 — keeps flashes out of a centered square on every monitor so they never
        /// cover a game's crosshair. Global user preference: sessions and presets never touch
        /// it, and this control is its only UI surface.
        /// </summary>
        private void ChkFlashAvoidCenter_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = ChkFlashAvoidCenter.IsChecked ?? false;
            s.FlashAvoidCenter = on;
            Log.Information("Flash avoid-center toggled: {Enabled} ({Pct}%)",
                on, s.FlashCenterExclusionPercent);
            CoreSettings.Save();
        }

        /// <summary>
        /// #770 — size of the centered no-flash square, as a % of the shorter monitor edge.
        /// AppSettings clamps to 5-60; the slider carries the same range.
        /// </summary>
        private void SliderCenterExclusion_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtCenterExclusion.Text = $"{v}%";
            CoreSettings.Current.FlashCenterExclusionPercent = v;
            CoreSettings.Save();
        }

        private void ChkEnable_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.FlashEnabled = ChkEnable.IsChecked ?? false;
            CoreSettings.Save();

            // Live-apply only while the engine runs (WPF FlashFeatureControl.xaml.cs:268).
            CoreEngine.ApplyLive("flash", CoreSettings.Current.FlashEnabled);
        }

        private void SliderFrequency_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtFrequency.Text = v.ToString();
            CoreSettings.Current.FlashFrequency = v;
            CoreSettings.Save();
            CoreFlash.RefreshSchedule();
        }

        private void SliderImages_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtImages.Text = v.ToString();
            CoreSettings.Current.SimultaneousImages = v;
            CoreSettings.Save();
        }

        // #658: WPF FlashFeatureControl.xaml.cs ChkRandomImages_Changed / SliderImagesMin_Changed.
        private void ChkRandomImages_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var on = ChkRandomImages.IsChecked ?? false;
            CoreSettings.Current.SimultaneousImagesRandom = on;
            RowImagesMin.IsVisible = on;
            CoreSettings.Save();
        }

        private void SliderImagesMin_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtImagesMin.Text = v.ToString();
            CoreSettings.Current.SimultaneousImagesMin = v;
            CoreSettings.Save();
        }

        private void SliderMaxOnScreen_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtMaxOnScreen.Text = v.ToString();
            CoreSettings.Current.HydraLimit = v;
            CoreSettings.Save();
        }

        private void ChkClickable_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.FlashClickable = ChkClickable.IsChecked ?? false;
            CoreSettings.Save();
        }

        private void ChkCorruption_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.CorruptionMode = ChkCorruption.IsChecked ?? false;
            CoreSettings.Save();
        }

        private void ChkHydraLinked_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.HydraLinkedTiming = ChkHydraLinked.IsChecked ?? false;
            CoreSettings.Save();
        }

        private void ChkGlow_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.FlashGlowEnabled = ChkGlow.IsChecked ?? false;
            CoreSettings.Save();
        }

        private void ChkSolidMode_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            CoreSettings.Current.FlashSolidMode = ChkSolidMode.IsChecked ?? false;
            CoreSettings.Save();
            // No service bounce needed: each spawn reads the setting, so the next flash uses the
            // new mode. Live flashes finish out on whichever renderer spawned them.
        }
    }
}
