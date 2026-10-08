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
        // WPF rebuilds on PrizeGrants.GrantsChanged; ownership never changes at runtime on this head
        // (PrizeOwnership), so the rebuild on load/rebind is all there is to do.

        /// <summary>Still always, Drift and Bounce with its v2 pill when owned, Mix once anything v2 is
        /// owned; with nothing owned the box stays hidden. Pendulum is not offered: this head cannot
        /// play it yet (it would play Still).</summary>
        private void BuildMotionPicker()
        {
            var drift = Platform.PrizeOwnership.IsGranted(Platform.PrizeOwnership.FlashDriftBounce);
            BoxFlashV2.IsVisible = RowMotion.IsVisible = drift;
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
