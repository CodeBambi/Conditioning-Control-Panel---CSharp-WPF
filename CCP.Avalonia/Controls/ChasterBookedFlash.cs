// PORTED from ConditioningControlPanel/Controls/ChasterBookedFlash.cs: the flashing "+0:30" that
// lifts off the rail padlock and fades when Circe's tab books a price. On the adorner layer, as WPF:
// above the chrome, outside the rail's clip, never a window. What it draws is Core BookedFlashPlan.
using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Controls
{
    internal sealed class ChasterBookedFlash : Panel
    {
        private const double FlashDipOpacity = 0.4;   // the blink at birth, one frame down and back
        private const int FlashFrameMs = 33;
        private const double HoldFraction = 0.35;      // fully lit for this much of the life

        internal TextBlock Figure { get; }
        private readonly CancellationTokenSource _run = new();
        private bool _gone;

        private ChasterBookedFlash(BookedFlashPlan.Plan plan)
        {
            IsHitTestVisible = false;
            Figure = new TextBlock
            {
                Text = plan.Text, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 15,
                FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.FromUInt32(plan.Colour)),
                TextAlignment = TextAlignment.Center, RenderTransform = new TranslateTransform(), IsHitTestVisible = false,
            };
            Children.Add(Figure);
        }

        /// <summary>Put a figure on the padlock, or null when there is no adorner layer yet (the
        /// booking then goes unremarked, the right failure for decoration).</summary>
        internal static ChasterBookedFlash? Show(Control? anchor, BookedFlashPlan.Plan plan)
        {
            if (anchor is null || AdornerLayer.GetAdornerLayer(anchor) is not { } layer) return null;
            var flash = new ChasterBookedFlash(plan);
            AdornerLayer.SetAdornedElement(flash, anchor);
            AdornerLayer.SetIsClipEnabled(flash, false);   // the figure rests above the chip, outside its bounds
            layer.Children.Add(flash);
            flash.Run(plan);
            return flash;
        }

        /// <summary>Same figure, bigger number: the travel and the fade keep running.</summary>
        internal void Retitle(string text, uint colour)
        {
            if (_gone) return;
            Figure.Text = text;
            Figure.Foreground = new SolidColorBrush(Color.FromUInt32(colour));
        }

        /// <summary>Take the figure off now (a coalesce that netted to zero).</summary>
        internal void Dismiss() => Remove();

        private async void Run(BookedFlashPlan.Plan plan)
        {
            var life = TimeSpan.FromMilliseconds(Math.Max(1, plan.DurationMs));
            var fade = new Animation { Duration = life, FillMode = FillMode.Forward };
            fade.Children.Add(Key(0, OpacityProperty, 1d));
            if (plan.Flash)
            {
                fade.Children.Add(Key(FlashFrameMs / life.TotalMilliseconds, OpacityProperty, FlashDipOpacity));
                fade.Children.Add(Key(FlashFrameMs * 2 / life.TotalMilliseconds, OpacityProperty, 1d));
            }
            fade.Children.Add(Key(HoldFraction, OpacityProperty, 1d));
            fade.Children.Add(Key(1, OpacityProperty, 0d));
            var runs = fade.RunAsync(Figure, _run.Token);
            if (plan.TravelPx > 0)
            {
                // Out of the gate and settling, the way a number thrown off a surface moves.
                var rise = new Animation { Duration = life, FillMode = FillMode.Forward, Easing = new CubicEaseOut() };
                rise.Children.Add(Key(0, TranslateTransform.YProperty, 0d));
                rise.Children.Add(Key(1, TranslateTransform.YProperty, -plan.TravelPx));
                _ = rise.RunAsync(Figure, _run.Token);
            }
            try { await runs; } catch (OperationCanceledException) { }
            Remove();
        }

        private static KeyFrame Key(double cue, AvaloniaProperty property, double value) =>
            new() { Cue = new Cue(cue), Setters = { new Setter(property, value) } };

        private void Remove()
        {
            if (_gone) return;
            _gone = true;
            _run.Cancel();
            (Parent as Panel)?.Children.Remove(this);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            Figure.Measure(Size.Infinity);
            return default;
        }

        /// <summary>Centred on the badge and resting just above it, so the lift carries it up the rail.</summary>
        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = Figure.DesiredSize;
            Figure.Arrange(new Rect(new Point((finalSize.Width - size.Width) / 2, -size.Height - 2), size));
            return finalSize;
        }
    }
}
