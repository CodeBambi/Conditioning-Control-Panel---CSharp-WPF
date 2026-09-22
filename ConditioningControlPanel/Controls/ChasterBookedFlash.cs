using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The flashing "+0:30": a small mono figure that lifts off the rail padlock and fades when
    /// Circe's tab books a price.
    ///
    /// <para><b>Why an adorner and not a window.</b> A floating figure wants to live OVER the
    /// chrome, outside the nav rail's own clip, and it must not be a window: an unowned visible
    /// window blocks <c>OnLastWindowClose</c> and hangs the process after the UI is gone (house
    /// rule). A Popup would work but brings screen coordinates, per-monitor DPI and its own HWND
    /// for a 40px number. The adorner layer already sits above everything in the window, is not
    /// clipped by the rail, and follows the adorned element - so the figure stays on the padlock
    /// when the rail collapses, when the window resizes, and when the rail scrolls, with no
    /// position maths at all.</para>
    ///
    /// <para><b>One shot.</b> Each instance draws one figure and removes itself from the layer
    /// when its fade completes. The live one can be re-labelled in place while a coalescing window
    /// is open (<see cref="Retitle"/>) rather than stacking a second figure on the first; the
    /// travel is never restarted, so a burst of prices reads as one number settling, not as a
    /// stutter. Coalescing itself is decided in <see cref="BookedFlashPlan"/> and driven from
    /// <c>MainWindow.Chaster.cs</c>.</para>
    ///
    /// <para>Transform and opacity only, nothing hit-testable, and no clock left running: under
    /// MotionLevel.Off the figure simply appears and fades where it was born.</para>
    /// </summary>
    internal sealed class ChasterBookedFlash : Adorner
    {
        /// <summary>The blink at birth, one frame down and one back, so the eye catches the arrival
        /// of a number that is otherwise already moving away.</summary>
        private const double FlashDipOpacity = 0.4;
        private const int FlashFrameMs = 33;

        /// <summary>Fraction of the life the figure stays fully lit before it starts to go.</summary>
        private const double HoldFraction = 0.35;

        private readonly VisualCollection _children;
        private readonly TextBlock _figure;
        private readonly TranslateTransform _lift = new();
        private AdornerLayer? _layer;
        private bool _gone;

        private ChasterBookedFlash(UIElement adorned, BookedFlashPlan.Plan plan) : base(adorned)
        {
            IsHitTestVisible = false;
            _children = new VisualCollection(this);

            _figure = new TextBlock
            {
                Text = plan.Text,
                // Mono so a figure that changes under a coalesce does not jump sideways, and
                // FontGuard so a broken Cascadia install cannot throw out of the layout pass.
                FontFamily = Services.UI.FontGuard.Mono,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(plan.Colour),
                TextAlignment = TextAlignment.Center,
                RenderTransform = _lift,
                IsHitTestVisible = false,
            };
            _children.Add(_figure);
        }

        /// <summary>
        /// Put a figure on the padlock. Returns the live adorner so the caller can re-label it
        /// while the coalescing window is open, or null when there is no adorner layer yet (the
        /// chip has not been rendered, or the window is on its way out) - in which case the
        /// booking simply goes unremarked, which is the right failure for decoration.
        /// </summary>
        internal static ChasterBookedFlash? Show(UIElement anchor, BookedFlashPlan.Plan plan)
        {
            if (anchor is null) return null;
            var layer = AdornerLayer.GetAdornerLayer(anchor);
            if (layer is null) return null;

            var flash = new ChasterBookedFlash(anchor, plan) { _layer = layer };
            layer.Add(flash);
            flash.Run(plan);
            return flash;
        }

        /// <summary>Rewrite the live figure (a booking landed inside the coalescing window). The
        /// travel and the fade keep running: this is the same figure saying a bigger number.</summary>
        internal void Retitle(string text, Color colour)
        {
            if (_gone) return;
            _figure.Text = text;
            _figure.Foreground = new SolidColorBrush(colour);
            InvalidateArrange();
        }

        /// <summary>Take the figure off now (a coalesce that netted to zero has nothing to say).</summary>
        internal void Dismiss()
        {
            if (_gone) return;
            _figure.BeginAnimation(UIElement.OpacityProperty, null);
            _lift.BeginAnimation(TranslateTransform.YProperty, null);
            Remove();
        }

        private void Run(BookedFlashPlan.Plan plan)
        {
            var life = TimeSpan.FromMilliseconds(Math.Max(1, plan.DurationMs));

            var fade = new DoubleAnimationUsingKeyFrames { Duration = life, FillBehavior = FillBehavior.HoldEnd };
            if (plan.Flash)
            {
                // Two frames: down, back. Discrete, so it reads as a blink and not as a dip.
                fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(FlashDipOpacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(FlashFrameMs))));
                fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(FlashFrameMs * 2))));
            }
            else
            {
                fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            }
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(plan.DurationMs * HoldFraction))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(life)));
            fade.Completed += (_, _) => Remove();
            _figure.BeginAnimation(UIElement.OpacityProperty, fade);

            if (plan.TravelPx <= 0) return;

            var rise = new DoubleAnimation(0, -plan.TravelPx, life)
            {
                // Out of the gate and settling, the way a number thrown off a surface moves.
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd,
            };
            _lift.BeginAnimation(TranslateTransform.YProperty, rise);
        }

        private void Remove()
        {
            if (_gone) return;
            _gone = true;
            try { _layer?.Remove(this); }
            catch (Exception ex) { App.Logger?.Debug("[Chaster] flash remove: {E}", ex.Message); }
            _layer = null;
        }

        protected override int VisualChildrenCount => _children.Count;

        protected override Visual GetVisualChild(int index) => _children[index];

        protected override Size MeasureOverride(Size constraint)
        {
            _figure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = _figure.DesiredSize;
            // Centred on the badge and resting just above it, so the lift carries it up the rail
            // rather than across the page content.
            var x = (finalSize.Width - size.Width) / 2;
            var y = -size.Height - 2;
            _figure.Arrange(new Rect(new Point(x, y), size));
            return finalSize;
        }
    }
}
