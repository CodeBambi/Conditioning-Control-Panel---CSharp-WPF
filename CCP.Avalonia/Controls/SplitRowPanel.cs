using System;
using Avalonia;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Controls/SplitRowPanel.cs (WPF 54d020e60). Two children on
    /// one line, first left and second right, while both fit; when they do not (a longer language),
    /// the second drops under the first, right-aligned, instead of running into it.
    /// </summary>
    public class SplitRowPanel : Panel
    {
        public static readonly StyledProperty<double> GapProperty =
            AvaloniaProperty.Register<SplitRowPanel, double>(nameof(Gap), 12.0);

        static SplitRowPanel() => AffectsMeasure<SplitRowPanel>(GapProperty);

        public double Gap
        {
            get => GetValue(GapProperty);
            set => SetValue(GapProperty, value);
        }

        /// <summary>True when the last measure put the second child under the first.</summary>
        public bool IsStacked { get; private set; }

        protected override Size MeasureOverride(Size available)
        {
            var left = Children.Count > 0 ? Children[0] : null;
            var right = Children.Count > 1 ? Children[1] : null;
            var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
            left?.Measure(inf);
            right?.Measure(inf);
            var l = left?.DesiredSize ?? default;
            var r = right?.DesiredSize ?? default;
            var leftShown = l.Width > 0;
            var rightShown = r.Width > 0;
            var oneLine = l.Width + (leftShown && rightShown ? Gap : 0) + r.Width;
            IsStacked = leftShown && rightShown && oneLine > available.Width;
            if (!IsStacked)
                return new Size(Math.Min(oneLine, available.Width), Math.Max(l.Height, r.Height));

            var line = new Size(available.Width, double.PositiveInfinity);
            left!.Measure(line);
            right!.Measure(line);
            return new Size(Math.Min(Math.Max(left.DesiredSize.Width, right.DesiredSize.Width), available.Width),
                left.DesiredSize.Height + right.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            var left = Children.Count > 0 ? Children[0] : null;
            var right = Children.Count > 1 ? Children[1] : null;
            var l = left?.DesiredSize ?? default;
            var r = right?.DesiredSize ?? default;
            if (IsStacked)
            {
                left?.Arrange(new Rect(0, 0, Math.Min(l.Width, final.Width), l.Height));
                var rw = Math.Min(r.Width, final.Width);
                right?.Arrange(new Rect(final.Width - rw, l.Height, rw, r.Height));
            }
            else
            {
                var h = final.Height;
                left?.Arrange(new Rect(0, (h - l.Height) / 2, l.Width, l.Height));
                right?.Arrange(new Rect(Math.Max(0, final.Width - r.Width), (h - r.Height) / 2, r.Width, r.Height));
            }
            for (var i = 2; i < Children.Count; i++)
                Children[i].Arrange(new Rect(0, 0, 0, 0));
            return final;
        }
    }
}
