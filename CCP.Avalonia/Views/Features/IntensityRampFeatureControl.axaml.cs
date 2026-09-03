using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Intensity Ramp panel, ported from the WPF head. Every editor now reads and writes
    /// <see cref="CoreSettings.Current"/> and persists through <see cref="CoreSettings.Save"/>,
    /// and the live curve preview is driven by the SAME
    /// <see cref="RampMath.ResolveFactor(AppSettings, double)"/> the runtime tick calls - so the
    /// preview cannot drift from the ramp, which is the whole point of it on WPF too.
    ///
    /// <para>The settings hook is inlined for the reason spelled out in
    /// <see cref="BubbleCountFeatureControl"/>. Nothing else here touches a service: the global
    /// ramp runs on MainWindow's own timer and the per-session ramp is SessionEngine's, and both
    /// read the settings this panel writes.</para>
    /// </summary>
    public partial class IntensityRampFeatureControl : UserControl
    {
        private bool _isLoading = true;
        private AppSettings? _hooked;

        private readonly CheckBox _enabled;
        private readonly CheckBox _endAt;
        private readonly ComboBox _mode;
        private readonly ComboBox _curve;
        private readonly Slider _duration;
        private readonly Slider _multiplier;
        private readonly Slider _rangeStart;
        private readonly Slider _rangeEnd;
        private readonly Canvas _canvas;
        private readonly Polyline _line;
        private readonly CheckBox[] _links;

        public IntensityRampFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);

            _duration = SliderLabel.Wire(this, "SliderDuration", "TxtDuration", v => $"{(int)v} min");
            _multiplier = SliderLabel.Wire(this, "SliderMultiplier", "TxtMultiplier", v => $"{v:F1}x");
            _rangeStart = SliderLabel.Wire(this, "SliderRangeStart", "TxtRangeStart", v => $"{(int)v}%");
            _rangeEnd = SliderLabel.Wire(this, "SliderRangeEnd", "TxtRangeEnd", v => $"{(int)v}%");
            _enabled = this.FindControl<CheckBox>("ChkEnabled")!;
            _endAt = this.FindControl<CheckBox>("ChkEndAt")!;
            _mode = this.FindControl<ComboBox>("CmbRampMode")!;
            _curve = this.FindControl<ComboBox>("CmbRampCurve")!;
            _canvas = this.FindControl<Canvas>("CurvePreviewCanvas")!;
            _line = this.FindControl<Polyline>("CurvePreviewLine")!;

            // Six toggles, one setting group, one handler - as on WPF, where Link_Changed writes
            // all six every time whichever one moved.
            _links = new[]
            {
                this.FindControl<CheckBox>("ChkLinkFlash")!,
                this.FindControl<CheckBox>("ChkLinkSpiral")!,
                this.FindControl<CheckBox>("ChkLinkPink")!,
                this.FindControl<CheckBox>("ChkLinkMaster")!,
                this.FindControl<CheckBox>("ChkLinkSub")!,
                this.FindControl<CheckBox>("ChkLinkBrainDrain")!,
            };

            _enabled.IsCheckedChanged += (_, _) => ChkEnabled_Changed();
            _endAt.IsCheckedChanged += (_, _) => ChkEndAt_Changed();
            _mode.SelectionChanged += (_, _) => CmbRampMode_Changed();
            _curve.SelectionChanged += (_, _) => CmbRampCurve_Changed();
            _duration.ValueChanged += (_, e) => SliderDuration_Changed(e.NewValue);
            _multiplier.ValueChanged += (_, e) => SliderMultiplier_Changed(e.NewValue);
            _rangeStart.ValueChanged += (_, e) => SliderRangeStart_Changed(e.NewValue);
            _rangeEnd.ValueChanged += (_, e) => SliderRangeEnd_Changed(e.NewValue);
            foreach (var link in _links) link.IsCheckedChanged += (_, _) => Link_Changed();
            _canvas.SizeChanged += (_, _) => RedrawPreview();

            Loaded += (_, _) => RebindToCurrentSettings();
            Unloaded += (_, _) => Unhook();

            RebindToCurrentSettings();
        }

        /// <summary>Re-points the settings hook at the live instance and repaints from it.</summary>
        public void RebindToCurrentSettings()
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
                _enabled.IsChecked = s.IntensityRampEnabled;
                _duration.Value = s.RampDurationMinutes;
                _multiplier.Value = s.SchedulerMultiplier;
                _mode.SelectedIndex = s.RampMode == RampMode.Range ? 1 : 0;
                _rangeStart.Value = s.RampStartPercent;
                _rangeEnd.Value = s.RampEndPercent;
                _endAt.IsChecked = s.EndSessionOnRampComplete;
                _curve.SelectedIndex = s.RampCurve switch
                {
                    RampCurve.EaseIn => 1,
                    RampCurve.EaseOut => 2,
                    RampCurve.SCurve => 3,
                    RampCurve.Exponential => 4,
                    _ => 0,
                };
                _links[0].IsChecked = s.RampLinkFlashOpacity;
                _links[1].IsChecked = s.RampLinkSpiralOpacity;
                _links[2].IsChecked = s.RampLinkPinkFilterOpacity;
                _links[3].IsChecked = s.RampLinkMasterAudio;
                _links[4].IsChecked = s.RampLinkSubliminalAudio;
                _links[5].IsChecked = s.RampLinkBrainDrain;
            }
            finally { _isLoading = false; }

            ApplyModeVisibility();
            RedrawPreview();
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.IntensityRampEnabled) ||
                e.PropertyName == nameof(AppSettings.RampDurationMinutes) ||
                e.PropertyName == nameof(AppSettings.SchedulerMultiplier) ||
                e.PropertyName == nameof(AppSettings.EndSessionOnRampComplete) ||
                e.PropertyName == nameof(AppSettings.RampCurve) ||
                e.PropertyName == nameof(AppSettings.RampMode) ||
                e.PropertyName == nameof(AppSettings.RampStartPercent) ||
                e.PropertyName == nameof(AppSettings.RampEndPercent) ||
                e.PropertyName == nameof(AppSettings.RampLinkFlashOpacity) ||
                e.PropertyName == nameof(AppSettings.RampLinkSpiralOpacity) ||
                e.PropertyName == nameof(AppSettings.RampLinkPinkFilterOpacity) ||
                e.PropertyName == nameof(AppSettings.RampLinkMasterAudio) ||
                e.PropertyName == nameof(AppSettings.RampLinkSubliminalAudio) ||
                e.PropertyName == nameof(AppSettings.RampLinkBrainDrain))
            {
                Dispatcher.UIThread.Post(LoadFromSettings);
            }
        }

        private void ChkEnabled_Changed()
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = _enabled.IsChecked ?? false;
            if (s.IntensityRampEnabled == on) return;
            s.IntensityRampEnabled = on;
            CoreSettings.Save();
        }

        private void SliderDuration_Changed(double value)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)value;
            if (s.RampDurationMinutes == v) return;
            s.RampDurationMinutes = v;
            CoreSettings.Save();
        }

        private void SliderMultiplier_Changed(double value)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            if (Math.Abs(s.SchedulerMultiplier - value) < 0.0001) return;
            s.SchedulerMultiplier = value;
            CoreSettings.Save();
            RedrawPreview();
        }

        private void CmbRampMode_Changed()
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var mode = _mode.SelectedIndex == 1 ? RampMode.Range : RampMode.Multiplier;
            if (s.RampMode == mode) return;
            s.RampMode = mode;
            CoreSettings.Save();
            ApplyModeVisibility();
            RedrawPreview();
        }

        private void SliderRangeStart_Changed(double value)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)value;
            if (s.RampStartPercent == v) return;
            s.RampStartPercent = v;
            CoreSettings.Save();
            RedrawPreview();
        }

        private void SliderRangeEnd_Changed(double value)
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var v = (int)value;
            if (s.RampEndPercent == v) return;
            s.RampEndPercent = v;
            CoreSettings.Save();
            RedrawPreview();
        }

        private void CmbRampCurve_Changed()
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var curve = _curve.SelectedIndex switch
            {
                1 => RampCurve.EaseIn,
                2 => RampCurve.EaseOut,
                3 => RampCurve.SCurve,
                4 => RampCurve.Exponential,
                _ => RampCurve.Linear,
            };
            if (s.RampCurve == curve) return;
            s.RampCurve = curve;
            CoreSettings.Save();
            RedrawPreview();
        }

        private void ChkEndAt_Changed()
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            var on = _endAt.IsChecked ?? false;
            if (s.EndSessionOnRampComplete == on) return;
            s.EndSessionOnRampComplete = on;
            CoreSettings.Save();
        }

        /// <summary>All six link toggles write together, as on WPF: one handler, one save.</summary>
        private void Link_Changed()
        {
            if (_isLoading) return;
            var s = CoreSettings.Current;
            s.RampLinkFlashOpacity = _links[0].IsChecked ?? false;
            s.RampLinkSpiralOpacity = _links[1].IsChecked ?? false;
            s.RampLinkPinkFilterOpacity = _links[2].IsChecked ?? false;
            s.RampLinkMasterAudio = _links[3].IsChecked ?? false;
            s.RampLinkSubliminalAudio = _links[4].IsChecked ?? false;
            s.RampLinkBrainDrain = _links[5].IsChecked ?? false;
            CoreSettings.Save();
        }

        /// <summary>
        /// Multiplier mode shows the single "up to Nx" dial; Range mode shows the start/end pair
        /// instead. Never both: they are two spellings of the same factor and showing the inert one
        /// is exactly the "dial that quietly does nothing" this panel already argues against.
        /// </summary>
        private void ApplyModeVisibility()
        {
            var isRange = CoreSettings.Current.RampMode == RampMode.Range;
            this.FindControl<Grid>("RowMultiplier")!.IsVisible = !isRange;
            this.FindControl<Grid>("RowRangeStart")!.IsVisible = isRange;
            this.FindControl<Grid>("RowRangeEnd")!.IsVisible = isRange;
        }

        /// <summary>
        /// Repaints the factor-over-time polyline from the same
        /// <see cref="RampMath.ResolveFactor(AppSettings, double)"/> the runtime tick calls, so the
        /// preview cannot drift from the ramp. The vertical axis is normalised to whatever span the
        /// curve actually covers (a flat 100 -> 100 draws a centred straight line rather than
        /// dividing by zero), because the shape is the point here, not absolute values.
        /// </summary>
        private void RedrawPreview()
        {
            try
            {
                var w = _canvas.Bounds.Width;
                var h = _canvas.Bounds.Height;
                if (w <= 1 || h <= 1) return; // not laid out yet - SizeChanged calls back

                var s = CoreSettings.Current;

                const int steps = 48;
                var values = new double[steps + 1];
                double min = double.MaxValue, max = double.MinValue;
                for (var i = 0; i <= steps; i++)
                {
                    var f = RampMath.ResolveFactor(s, (double)i / steps);
                    values[i] = f;
                    if (f < min) min = f;
                    if (f > max) max = f;
                }

                var span = max - min;
                if (span < 0.0001) { min -= 0.5; span = 1.0; }

                var points = new List<Point>(steps + 1);
                for (var i = 0; i <= steps; i++)
                    points.Add(new Point(w * i / steps, h - (values[i] - min) / span * h));
                _line.Points = points;
            }
            catch (Exception ex)
            {
                Log.Debug("Ramp preview redraw failed: {E}", ex.Message);
            }
        }
    }
}
