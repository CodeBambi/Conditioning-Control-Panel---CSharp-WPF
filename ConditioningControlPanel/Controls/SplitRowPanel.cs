using System;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// Two children on one line, the first on the left and the second on the right, as long as
    /// both fit. When they do not (a German "Gezählte Tage 0/25" beside "00:00 von 31:00:00
    /// hinzugefügt" in a 360 px card), the second drops to its own line under the first and
    /// stays right-aligned, instead of the two being drawn over each other in one Grid cell.
    /// Any child past the second is ignored.
    /// </summary>
    public class SplitRowPanel : Panel
    {
        public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
            nameof(Gap), typeof(double), typeof(SplitRowPanel),
            new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        /// <summary>Least space kept between the two when they share a line.</summary>
        public double Gap
        {
            get => (double)GetValue(GapProperty);
            set => SetValue(GapProperty, value);
        }

        /// <summary>True when the last layout pass put the second child on its own line.</summary>
        public bool IsStacked { get; private set; }

        protected override Size MeasureOverride(Size available)
        {
            var left = InternalChildren.Count > 0 ? InternalChildren[0] : null;
            var right = InternalChildren.Count > 1 ? InternalChildren[1] : null;
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

            // Stacked: each child gets the full width and may wrap inside it.
            var line = new Size(available.Width, double.PositiveInfinity);
            left!.Measure(line);
            right!.Measure(line);
            return new Size(Math.Min(Math.Max(left.DesiredSize.Width, right.DesiredSize.Width), available.Width),
                left.DesiredSize.Height + right.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            var left = InternalChildren.Count > 0 ? InternalChildren[0] : null;
            var right = InternalChildren.Count > 1 ? InternalChildren[1] : null;
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
            for (var i = 2; i < InternalChildren.Count; i++)
                InternalChildren[i].Arrange(new Rect(0, 0, 0, 0));
            return final;
        }
    }
}
