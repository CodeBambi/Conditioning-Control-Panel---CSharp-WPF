using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
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
            SliderFrequency.ValueChanged += SliderFrequency_Changed;
            SliderImages.ValueChanged += SliderImages_Changed;
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
            if (e.PropertyName == nameof(AppSettings.FlashEnabled) ||
                e.PropertyName == nameof(AppSettings.FlashFrequency) ||
                e.PropertyName == nameof(AppSettings.SimultaneousImages) ||
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

            // Live-apply: start/stop the flash service if the engine is running. The GATE is real
            // now (CoreSession); what it gates is not.
            if (CoreSession.IsEngineRunning)
            {
                // ponytail: App.Flash.Start()/Stop() - FlashService
                // (ConditioningControlPanel/Services/Flash/FlashService.cs), still in the WPF head:
                // it spawns Win32 layered flash windows.
            }
        }

        private void SliderFrequency_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtFrequency.Text = v.ToString();
            CoreSettings.Current.FlashFrequency = v;
            // ponytail: WPF also calls App.Flash.RefreshSchedule() here. Needs FlashService
            // (ConditioningControlPanel/Services/), still in the WPF head.
            CoreSettings.Save();
        }

        private void SliderImages_Changed(object? sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            var v = (int)e.NewValue;
            TxtImages.Text = v.ToString();
            CoreSettings.Current.SimultaneousImages = v;
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
