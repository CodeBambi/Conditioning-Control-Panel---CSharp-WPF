using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// Intensity Ramp panel, ported from the WPF head. The mode/row visibility, the slider
    /// read-outs and the live curve preview are real and driven by the controls themselves;
    /// on WPF they are driven by App.Settings, which is what the preview reads there.
    /// </summary>
    public partial class IntensityRampFeatureControl : UserControl
    {
        private readonly ComboBox _mode;
        private readonly ComboBox _curve;
        private readonly Slider _multiplier;
        private readonly Slider _rangeStart;
        private readonly Slider _rangeEnd;
        private readonly Canvas _canvas;
        private readonly Polyline _line;

        public IntensityRampFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);

            SliderLabel.Wire(this, "SliderDuration", "TxtDuration", v => $"{(int)v} min");
            _multiplier = SliderLabel.Wire(this, "SliderMultiplier", "TxtMultiplier", v => $"{v:F1}x");
            _rangeStart = SliderLabel.Wire(this, "SliderRangeStart", "TxtRangeStart", v => $"{(int)v}%");
            _rangeEnd = SliderLabel.Wire(this, "SliderRangeEnd", "TxtRangeEnd", v => $"{(int)v}%");
            _mode = this.FindControl<ComboBox>("CmbRampMode")!;
            _curve = this.FindControl<ComboBox>("CmbRampCurve")!;
            _canvas = this.FindControl<Canvas>("CurvePreviewCanvas")!;
            _line = this.FindControl<Polyline>("CurvePreviewLine")!;

            // Placeholder defaults = a fresh AppSettings: Multiplier mode, Linear curve.
            _mode.SelectedIndex = 0;
            _curve.SelectedIndex = 0;

            _mode.SelectionChanged += (_, _) => { ApplyModeVisibility(); RedrawPreview(); };
            _curve.SelectionChanged += (_, _) => RedrawPreview();
            _multiplier.ValueChanged += (_, _) => RedrawPreview();
            _rangeStart.ValueChanged += (_, _) => RedrawPreview();
            _rangeEnd.ValueChanged += (_, _) => RedrawPreview();
            _canvas.SizeChanged += (_, _) => RedrawPreview();

            ApplyModeVisibility();
            // ponytail: needs App.Settings, wired when it moves to Core. ChkEnabled, ChkEndAt and the
            // six ChkLink* toggles are settings writes only.
        }

        /// <summary>
        /// Multiplier mode shows the single "up to Nx" dial; Range mode shows the start/end pair
        /// instead. Never both.
        /// </summary>
        private void ApplyModeVisibility()
        {
            var isRange = _mode.SelectedIndex == 1;
            this.FindControl<Grid>("RowMultiplier")!.IsVisible = !isRange;
            this.FindControl<Grid>("RowRangeStart")!.IsVisible = isRange;
            this.FindControl<Grid>("RowRangeEnd")!.IsVisible = isRange;
        }

        private RampCurve SelectedCurve => _curve.SelectedIndex switch
        {
            1 => RampCurve.EaseIn,
            2 => RampCurve.EaseOut,
            3 => RampCurve.SCurve,
            4 => RampCurve.Exponential,
            _ => RampCurve.Linear,
        };

        /// <summary>
        /// Repaints the factor-over-time polyline. The vertical axis is normalised to whatever
        /// span the curve covers (a flat 100 -> 100 draws a centred straight line rather than
        /// dividing by zero), because the shape is the point here, not absolute values.
        /// </summary>
        private void RedrawPreview()
        {
            var w = _canvas.Bounds.Width;
            var h = _canvas.Bounds.Height;
            if (w <= 1 || h <= 1) return; // not laid out yet - SizeChanged calls back

            var isRange = _mode.SelectedIndex == 1;
            var curve = SelectedCurve;
            var mult = _multiplier.Value;
            var start = Math.Clamp(_rangeStart.Value, 0, 300);
            var end = Math.Clamp(_rangeEnd.Value, 0, 300);

            const int steps = 48;
            var values = new double[steps + 1];
            double min = double.MaxValue, max = double.MinValue;
            for (var i = 0; i <= steps; i++)
            {
                // ponytail: mirrors Helpers/RampMath.ResolveFactor (WPF head, RampMode lives there);
                // delete when RampMath moves to Core.
                var eased = RampCurves.ApplyCurve((double)i / steps, curve);
                var f = isRange ? (start + (end - start) * eased) / 100.0 : 1.0 + (mult - 1.0) * eased;
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
    }
}
