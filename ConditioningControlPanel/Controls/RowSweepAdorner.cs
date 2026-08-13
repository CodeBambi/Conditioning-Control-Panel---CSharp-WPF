using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// A wash that sweeps in from a row's leading edge on hover and back out on leave.
    ///
    /// <para><b>What it is.</b> The Session Door's port of the dashboard split-card's seam sweep
    /// (<see cref="Features.SplitFeatureCard"/>). That control grows one diagonal half over the
    /// whole tile by animating a clip geometry, which only makes sense for a card cut in two; a
    /// full-width row has no seam, so the same IDEA - "a fill arrives from the edge you came from"
    /// - lands here as a gradient whose reach is animated instead.</para>
    ///
    /// <para><b>Why an adorner, same as <see cref="CardSheenAdorner"/>.</b> The rows it decorates
    /// are a mix of XAML Borders (the four built-in sessions) and code-built Borders whose Child is
    /// already a Grid (custom sessions, built by MainWindow.SessionIO.AddCustomSessionCard).
    /// Injecting a wash layer into each would mean editing both builders, fighting each row's
    /// Padding - a child stops at the padding box, not the row edge - and getting the z-order right
    /// under content that was never authored with a layer beneath it. An adorner costs the row
    /// nothing: no element in its visual tree, no layout pass, and it draws UNDER nothing and OVER
    /// everything, which is why the wash is kept to a low alpha.</para>
    ///
    /// <para><b>House rules this type obeys.</b>
    /// <list type="bullet">
    /// <item>INTERACTION clock (<see cref="ExpandMs"/>/<see cref="CollapseMs"/>, both inside the
    /// plan's 80-400ms band), not an ambient one. It therefore gates on
    /// <see cref="MotionFx.AllowTransitions"/> and NOT on <c>AllowAmbientLoops</c>: a
    /// reduced-motion user keeps hover feedback, they only lose the idle loops. With transitions
    /// off entirely the wash still appears - it just snaps instead of tweening, because silently
    /// removing the only hover affordance a row has is a usability regression, not a motion
    /// setting.</item>
    /// <item>No clock runs at rest. The animation is a one-shot per hover; between hovers there is
    /// nothing ticking, unlike the sheen's forever-repeating cycle.</item>
    /// <item>Colour from <see cref="FxTheme"/>, re-read on every <see cref="Enter"/>, so a mod
    /// switch re-tints without the adorner being rebuilt. The stops and brush are deliberately
    /// left unfrozen for that; freezing either would make the re-tint throw.</item>
    /// <item>It never touches a single property on the adorned row. Decoration may not be why a
    /// row's own hover trigger, lift transform or selection border stops working.</item>
    /// </list></para>
    /// </summary>
    internal sealed class RowSweepAdorner : Adorner
    {
        private const int ExpandMs = 220;
        private const int CollapseMs = 170;
        private const int FrameRate = 30;

        /// <summary>How far across the row the wash reaches at full extent. Deliberately short of
        /// half: this is a hint of arrival, not a fill, and the row's text starts around 40%.</summary>
        private const double MaxReach = 0.38;

        /// <summary>Peak alpha at the leading edge. Low, because an adorner draws OVER the row's
        /// content - anything heavier would visibly tint the title text.</summary>
        private const byte PeakAlpha = 0x30;

        private readonly GradientStop _head;
        private readonly GradientStop _tail;
        private readonly LinearGradientBrush _brush;
        private readonly double _cornerRadius;

        /// <summary>
        /// Animated 0..1. An <c>AffectsRender</c> DP so WPF's own animation system is the frame
        /// pump - there is no DispatcherTimer here to leak, and no per-tick allocation.
        /// </summary>
        private static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register(nameof(Progress), typeof(double), typeof(RowSweepAdorner),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        private double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }

        internal RowSweepAdorner(UIElement adorned, double cornerRadius) : base(adorned)
        {
            IsHitTestVisible = false;
            _cornerRadius = Math.Max(0, cornerRadius);

            _head = new GradientStop(Colors.Transparent, 0.0);
            _tail = new GradientStop(Colors.Transparent, 1.0);
            // Horizontal, left to right. Unlike the sheen's leaning band this one is straight on
            // purpose: it is reading as "the row filling from its edge", not as light on glass.
            _brush = new LinearGradientBrush(new GradientStopCollection { _head, _tail },
                                             new Point(0, 0), new Point(1, 0));
            ApplyTint();
        }

        private void ApplyTint()
        {
            var tint = FxTheme.GlowColor;
            _head.Color = Color.FromArgb(PeakAlpha, tint.R, tint.G, tint.B);
            _tail.Color = Color.FromArgb(0, tint.R, tint.G, tint.B);
        }

        /// <summary>Sweep in. Safe to call again while already in.</summary>
        internal void Enter()
        {
            ApplyTint();
            Animate(1.0, ExpandMs);
        }

        /// <summary>Sweep back out.</summary>
        internal void Leave() => Animate(0.0, CollapseMs);

        /// <summary>Drops any running animation and parks the wash invisible. Used when the row is
        /// being torn down, so the adorner cannot outlive its clock.</summary>
        internal void Reset()
        {
            try
            {
                BeginAnimation(ProgressProperty, null);
                Progress = 0.0;
            }
            catch (Exception ex) { App.Logger?.Debug("RowSweepAdorner.Reset: {E}", ex.Message); }
        }

        private void Animate(double to, int ms)
        {
            try
            {
                if (!MotionFx.AllowTransitions)
                {
                    // Snap. See the class remarks: the wash is the row's hover affordance, so it
                    // still lands at Off - only the tween goes.
                    BeginAnimation(ProgressProperty, null);
                    Progress = to;
                    return;
                }

                var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd,
                };
                Timeline.SetDesiredFrameRate(anim, FrameRate);
                BeginAnimation(ProgressProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("RowSweepAdorner.Animate: {E}", ex.Message); }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            try
            {
                double p = Progress;
                if (p <= 0.001) return;

                var size = AdornedElement.RenderSize;
                if (size.Width <= 0 || size.Height <= 0) return;

                // The gradient's own span is what grows: the head stays pinned at the row's leading
                // edge and the transparent tail walks right. Animating the STOP would fade the whole
                // wash in instead, which reads as a flash rather than as a sweep.
                //
                // Guarded because this writes to a DependencyObject from inside a render pass:
                // changing the stop invalidates the brush, which would schedule another render, and
                // an unguarded write would leave that settling to WPF's own no-op-set check. The
                // epsilon makes the loop provably terminate here instead - at rest we never reach
                // this line at all (the early return above), and at full extent the value stops
                // moving.
                double reach = Math.Max(0.02, p * MaxReach);
                if (Math.Abs(_tail.Offset - reach) > 0.0005) _tail.Offset = reach;

                drawingContext.DrawRoundedRectangle(_brush, null, new Rect(size),
                                                    _cornerRadius, _cornerRadius);
            }
            catch (Exception ex) { App.Logger?.Debug("RowSweepAdorner.OnRender: {E}", ex.Message); }
        }
    }
}
