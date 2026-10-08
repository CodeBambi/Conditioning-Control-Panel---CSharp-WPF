using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// WPF Controls/RowSweepAdorner.cs: a wash that sweeps in from a row's leading edge on hover
    /// (220ms) and back out on leave (170ms), quadratic ease-out. An INTERACTION clock: it gates on
    /// AllowTransitions, not AllowAmbientLoops, and with transitions off it snaps rather than
    /// vanishes (it is the row's only hover affordance). No clock at rest - one shot per hover.
    /// Colour from FxTheme's glow, re-read on every <see cref="Enter"/>. Never touches the row.
    /// </summary>
    public sealed class RowSweepAdorner : FxAdorner
    {
        private const int ExpandMs = 220;
        private const int CollapseMs = 170;
        private const int FrameRate = 30;

        /// <summary>Reach at full extent: a hint of arrival, short of the row's text at ~40%.</summary>
        private const double MaxReach = 0.38;

        /// <summary>Low, because the adorner draws OVER the row's title.</summary>
        private const byte PeakAlpha = 0x30;

        private readonly GradientStop _head = new(Colors.Transparent, 0.0);
        private readonly GradientStop _tail = new(Colors.Transparent, 1.0);
        private readonly LinearGradientBrush _brush;
        private readonly double _cornerRadius;

        private double _from, _to, _progress;
        private int _ms;
        private long _animStart;

        public RowSweepAdorner(Control adorned, double cornerRadius) : base(adorned, FrameRate)
        {
            _cornerRadius = Math.Max(0, cornerRadius);
            // Straight on purpose: "the row filling from its edge", not light on glass.
            _brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = new GradientStops { _head, _tail },
            };
            ApplyTint();
        }

        /// <summary>Animated 0..1 reach of the wash. Test seam.</summary>
        internal double Progress
        {
            get => _progress;
            private set
            {
                _progress = value;
                // The head stays pinned at the leading edge and the transparent tail walks right;
                // animating the stop colour instead would read as a flash, not a sweep.
                _tail.Offset = Math.Max(0.02, value * MaxReach);
            }
        }

        private void ApplyTint()
        {
            var tint = AmbientFxCanvas.Env.GlowColor;
            _head.Color = Color.FromArgb(PeakAlpha, tint.R, tint.G, tint.B);
            _tail.Color = Color.FromArgb(0, tint.R, tint.G, tint.B);
        }

        /// <summary>Sweep in. Safe to call again while already in.</summary>
        public void Enter()
        {
            ApplyTint();
            Animate(1.0, ExpandMs);
        }

        /// <summary>Sweep back out.</summary>
        public void Leave() => Animate(0.0, CollapseMs);

        /// <summary>Drops any running animation and parks the wash invisible.</summary>
        public void Reset() => Animate(0, 0, snap: true);

        private void Animate(double to, int ms, bool snap = false)
        {
            if (snap || !AmbientFxCanvas.Env.AllowTransitions || !IsVisible)
            {
                // Snap: the wash still lands at Off - only the tween goes.
                StopTicking();
                Progress = to;
                InvalidateVisual();
                return;
            }
            _from = Progress;
            _to = to;
            _ms = ms;
            _animStart = Time.GetTimestamp();
            StartTicking();
        }

        internal override void Tick()
        {
            double k = Math.Min(1.0, Time.GetElapsedTime(_animStart).TotalMilliseconds / _ms);
            double eased = 1 - ((1 - k) * (1 - k));   // QuadraticEase, EaseOut
            Progress = _from + ((_to - _from) * eased);
            if (k >= 1.0) StopTicking();
            InvalidateVisual();
        }

        /// <summary>Leaving the tab mid-hover raises no pointer-exit; drop the wash, as WPF does.</summary>
        protected override void OnShownChanged(bool shown) { if (!shown) Reset(); }

        public override void Render(DrawingContext context)
        {
            double p = Progress;
            if (p <= 0.001) return;
            var size = Adorned.Bounds.Size;
            if (size.Width <= 0 || size.Height <= 0) return;
            context.DrawRectangle(_brush, null, new Rect(size), _cornerRadius, _cornerRadius);
        }
    }
}
