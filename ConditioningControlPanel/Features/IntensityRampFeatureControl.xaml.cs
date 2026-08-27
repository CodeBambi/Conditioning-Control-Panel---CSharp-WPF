using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ConditioningControlPanel.Features
{
    public partial class IntensityRampFeatureControl : UserControl, ISettingsRebindable
    {
        private bool _isLoading = true;

        public IntensityRampFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        // Tracks WHICH AppSettings instance the hook is attached to, so a cloud restore - which
        // SWAPS the instance - can be followed instead of leaving this permanently-mounted rack
        // panel listening to, and displaying, the discarded object. See ISettingsRebindable.
        private SettingsHook? _settingsHook;

        private void OnLoaded(object sender, RoutedEventArgs e) => RebindToCurrentSettings();

        private void OnUnloaded(object sender, RoutedEventArgs e) => _settingsHook?.Unhook();

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
                ChkEnabled.IsChecked = s.IntensityRampEnabled;
                SliderDuration.Value = s.RampDurationMinutes;
                TxtDuration.Text = $"{s.RampDurationMinutes} min";
                SliderMultiplier.Value = s.SchedulerMultiplier;
                TxtMultiplier.Text = $"{s.SchedulerMultiplier:F1}x";
                CmbRampMode.SelectedIndex = s.RampMode == Models.RampMode.Range ? 1 : 0;
                SliderRangeStart.Value = s.RampStartPercent;
                TxtRangeStart.Text = $"{s.RampStartPercent}%";
                SliderRangeEnd.Value = s.RampEndPercent;
                TxtRangeEnd.Text = $"{s.RampEndPercent}%";
                ChkEndAt.IsChecked = s.EndSessionOnRampComplete;
                CmbRampCurve.SelectedIndex = s.RampCurve switch
                {
                    Models.RampCurve.EaseIn => 1,
                    Models.RampCurve.EaseOut => 2,
                    Models.RampCurve.SCurve => 3,
                    Models.RampCurve.Exponential => 4,
                    _ => 0,
                };
                ChkLinkFlash.IsChecked = s.RampLinkFlashOpacity;
                ChkLinkSpiral.IsChecked = s.RampLinkSpiralOpacity;
                ChkLinkPink.IsChecked = s.RampLinkPinkFilterOpacity;
                ChkLinkMaster.IsChecked = s.RampLinkMasterAudio;
                ChkLinkSub.IsChecked = s.RampLinkSubliminalAudio;
                ChkLinkBrainDrain.IsChecked = s.RampLinkBrainDrain;
            }
            finally { _isLoading = false; }

            ApplyModeVisibility();
            RedrawPreview();
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.IntensityRampEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.RampDurationMinutes) ||
                e.PropertyName == nameof(Models.AppSettings.SchedulerMultiplier) ||
                e.PropertyName == nameof(Models.AppSettings.EndSessionOnRampComplete) ||
                e.PropertyName == nameof(Models.AppSettings.RampCurve) ||
                e.PropertyName == nameof(Models.AppSettings.RampMode) ||
                e.PropertyName == nameof(Models.AppSettings.RampStartPercent) ||
                e.PropertyName == nameof(Models.AppSettings.RampEndPercent) ||
                e.PropertyName == nameof(Models.AppSettings.RampLinkFlashOpacity) ||
                e.PropertyName == nameof(Models.AppSettings.RampLinkSpiralOpacity) ||
                e.PropertyName == nameof(Models.AppSettings.RampLinkPinkFilterOpacity) ||
                e.PropertyName == nameof(Models.AppSettings.RampLinkMasterAudio) ||
                e.PropertyName == nameof(Models.AppSettings.RampLinkSubliminalAudio) ||
                e.PropertyName == nameof(Models.AppSettings.RampLinkBrainDrain))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.IntensityRampEnabled = ChkEnabled.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void SliderDuration_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtDuration.Text = $"{v} min";
            s.RampDurationMinutes = v;
            App.Settings?.Save();
        }

        private void SliderMultiplier_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = e.NewValue;
            TxtMultiplier.Text = $"{v:F1}x";
            s.SchedulerMultiplier = v;
            App.Settings?.Save();
            RedrawPreview();
        }

        private void CmbRampMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.RampMode = CmbRampMode.SelectedIndex == 1 ? Models.RampMode.Range : Models.RampMode.Multiplier;
            App.Settings?.Save();
            ApplyModeVisibility();
            RedrawPreview();
        }

        private void SliderRangeStart_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtRangeStart.Text = $"{v}%";
            s.RampStartPercent = v;
            App.Settings?.Save();
            RedrawPreview();
        }

        private void SliderRangeEnd_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var v = (int)e.NewValue;
            TxtRangeEnd.Text = $"{v}%";
            s.RampEndPercent = v;
            App.Settings?.Save();
            RedrawPreview();
        }

        private void CurvePreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RedrawPreview();

        /// <summary>
        /// Multiplier mode shows the single "up to Nx" dial; Range mode shows the start/end pair
        /// instead. Never both: they are two spellings of the same factor and showing the inert one
        /// is exactly the "dial that quietly does nothing" this popup already argues against.
        /// </summary>
        private void ApplyModeVisibility()
        {
            var isRange = (App.Settings?.Current?.RampMode ?? Models.RampMode.Multiplier) == Models.RampMode.Range;
            RowMultiplier.Visibility = isRange ? Visibility.Collapsed : Visibility.Visible;
            RowRangeStart.Visibility = isRange ? Visibility.Visible : Visibility.Collapsed;
            RowRangeEnd.Visibility = isRange ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Repaints the little factor-over-time polyline from the SAME
        /// <see cref="Helpers.RampMath.ResolveFactor"/> the runtime tick calls, so the preview
        /// cannot drift from the ramp. The vertical axis is normalised to whatever span the curve
        /// actually covers (a flat 100 -> 100 draws a centred straight line rather than dividing
        /// by zero), because the shape is the point here, not absolute values.
        /// </summary>
        private void RedrawPreview()
        {
            try
            {
                if (CurvePreviewCanvas == null || CurvePreviewLine == null) return;
                var w = CurvePreviewCanvas.ActualWidth;
                var h = CurvePreviewCanvas.ActualHeight;
                // Not laid out yet - SizeChanged will call us back with real numbers.
                if (w <= 1 || h <= 1) return;

                var s = App.Settings?.Current;
                var mode = s?.RampMode ?? Models.RampMode.Multiplier;
                var curve = s?.RampCurve ?? Models.RampCurve.Linear;
                var mult = s?.SchedulerMultiplier ?? 1.5;
                var start = s?.RampStartPercent ?? 100;
                var end = s?.RampEndPercent ?? 100;

                const int steps = 48;
                var values = new double[steps + 1];
                var min = double.MaxValue;
                var max = double.MinValue;
                for (var i = 0; i <= steps; i++)
                {
                    var f = Helpers.RampMath.ResolveFactor(mode, (double)i / steps, curve, mult, start, end);
                    values[i] = f;
                    if (f < min) min = f;
                    if (f > max) max = f;
                }

                var span = max - min;
                if (span < 0.0001)
                {
                    min -= 0.5;
                    span = 1.0;
                }

                var points = new PointCollection(steps + 1);
                for (var i = 0; i <= steps; i++)
                {
                    var x = w * i / steps;
                    var y = h - (values[i] - min) / span * h;
                    points.Add(new Point(x, y));
                }
                CurvePreviewLine.Points = points;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Ramp preview redraw failed: {E}", ex.Message);
            }
        }

        private void ChkEndAt_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.EndSessionOnRampComplete = ChkEndAt.IsChecked ?? false;
            App.Settings?.Save();
        }

        private void CmbRampCurve_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.RampCurve = CmbRampCurve.SelectedIndex switch
            {
                1 => Models.RampCurve.EaseIn,
                2 => Models.RampCurve.EaseOut,
                3 => Models.RampCurve.SCurve,
                4 => Models.RampCurve.Exponential,
                _ => Models.RampCurve.Linear,
            };
            App.Settings?.Save();
            RedrawPreview();
        }

        private void Link_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.RampLinkFlashOpacity = ChkLinkFlash.IsChecked ?? false;
            s.RampLinkSpiralOpacity = ChkLinkSpiral.IsChecked ?? false;
            s.RampLinkPinkFilterOpacity = ChkLinkPink.IsChecked ?? false;
            s.RampLinkMasterAudio = ChkLinkMaster.IsChecked ?? false;
            s.RampLinkSubliminalAudio = ChkLinkSub.IsChecked ?? false;
            s.RampLinkBrainDrain = ChkLinkBrainDrain.IsChecked ?? false;
            App.Settings?.Save();
        }
    }
}
