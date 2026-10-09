// PORTED from ConditioningControlPanel/Controls/CirceSays.cs: Circe says a line, a small paper
// speech bubble with her padlock face. Pops in, holds 4.2 s, fades and collapses. Text only.
// The rail's bubble (WPF CirceSaysAdorner) sits on the adorner layer beside the padlock chip,
// like ChasterBookedFlash: above the chrome, outside the rail's clip, never a window.
using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Controls
{
    public sealed class CirceSays : Border
    {
        public const int HoldMs = 4200;

        private static readonly Color Paper = Color.FromRgb(0xF3, 0xEA, 0xD9);
        internal static readonly Color InkColour = Color.FromRgb(0x24, 0x1A, 0x2E);

        private readonly TextBlock _text;
        private readonly ScaleTransform _pop = new(1, 1);
        private CancellationTokenSource? _anim;

        /// <summary>The hold. Exposed so tests step it instead of waiting.</summary>
        internal DispatcherTimer Hold { get; } = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(HoldMs) };

        /// <summary>The bubble finished and collapsed.</summary>
        public event Action? Finished;

        public CirceSays()
        {
            Background = new SolidColorBrush(Paper);
            CornerRadius = new CornerRadius(13, 13, 13, 3);
            Padding = new Thickness(6, 6, 11, 6);
            MaxWidth = 230;
            IsHitTestVisible = false;
            IsVisible = false;
            RenderTransformOrigin = new RelativePoint(0.1, 1, RelativeUnit.Relative);
            RenderTransform = _pop;
            BoxShadow = BoxShadows.Parse("0 4 16 0 #80000000");

            _text = new TextBlock
            {
                FontFamily = new FontFamily("Fredoka, Segoe UI"), FontSize = 12.5, FontWeight = FontWeight.SemiBold, FontStyle = FontStyle.Italic,
                Foreground = new SolidColorBrush(InkColour), TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0),
            };
            var face = Face(22);
            DockPanel.SetDock(face, Dock.Left);
            Child = new DockPanel { Children = { face, _text } };
            Hold.Tick += (_, _) => { Hold.Stop(); Hush(); };
        }

        /// <summary>What she is saying, for the tests.</summary>
        internal string Text => _text.Text ?? "";

        /// <summary>Her padlock face: a lilac-to-pink disc with an ink padlock.</summary>
        public static Control Face(double size)
        {
            var ink = new SolidColorBrush(InkColour);
            var art = new Canvas { Width = 14, Height = 15 };
            art.Children.Add(new Path { Data = Geometry.Parse("M4,7 V5 a3,3 0 0 1 6,0 V7"), Stroke = ink, StrokeThickness = 1.8 });
            var body = new Rectangle { Width = 10, Height = 7.5, RadiusX = 2, RadiusY = 2, Fill = ink };
            Canvas.SetLeft(body, 2);
            Canvas.SetTop(body, 6.5);
            art.Children.Add(body);
            return new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), VerticalAlignment = VerticalAlignment.Center,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromRgb(0xD9, 0xC4, 0xFF), 0), new GradientStop(Color.FromRgb(0xFF, 0x8F, 0xB1), 1) },
                },
                Child = new Viewbox { Width = size * 0.58, Height = size * 0.58, Child = art },
            };
        }

        /// <summary>Say a line. A new line replaces the one showing and restarts the hold.</summary>
        public void Say(string text)
        {
            _text.Text = text;
            _anim?.Cancel();
            _anim = null;
            IsVisible = true;
            Opacity = 1;
            if (AmbientFxCanvas.Env.Level == MotionLevel.Full)
            {
                var run = _anim = new CancellationTokenSource();
                _ = CircesMoodMeter.Tween(this, ScaleTransform.ScaleXProperty, 0.4, 1d, 260, new BackEaseOut(), run);
                _ = CircesMoodMeter.Tween(this, ScaleTransform.ScaleYProperty, 0.4, 1d, 260, new BackEaseOut(), run);
            }
            Hold.Stop();
            Hold.Start();
        }

        /// <summary>Take the line away: a short fade, or at once with motion off.</summary>
        public async void Hush()
        {
            Hold.Stop();
            if (!IsVisible) return;
            if (!AmbientFxCanvas.Env.AllowTransitions) { Done(); return; }
            _anim?.Cancel();
            var run = _anim = new CancellationTokenSource();
            try { await CircesMoodMeter.Tween(this, OpacityProperty, 1d, 0d, 220, new LinearEasing(), run); }
            catch (OperationCanceledException) { }
            if (!run.IsCancellationRequested) Done();
        }

        private void Done()
        {
            IsVisible = false;
            Opacity = 1;
            Finished?.Invoke();
        }
    }

    /// <summary>The rail's speech bubble beside the padlock chip (WPF CirceSaysAdorner). One at a
    /// time; it takes itself off when the bubble finishes. Nothing without an adorner layer.</summary>
    internal sealed class CirceSaysAdorner : Panel
    {
        private static CirceSaysAdorner? _live;
        internal CirceSays Bubble { get; } = new();
        private Control? _anchor;

        private CirceSaysAdorner()
        {
            IsHitTestVisible = false;
            Children.Add(Bubble);
            Bubble.Finished += Remove;
        }

        internal static CirceSaysAdorner? Show(Control anchor, string text)
        {
            if (_live is { } live && live._anchor == anchor) { live.Bubble.Say(text); return live; }
            _live?.Remove();
            if (AdornerLayer.GetAdornerLayer(anchor) is not { } layer) return null;
            var adorner = new CirceSaysAdorner { _anchor = anchor };
            AdornerLayer.SetAdornedElement(adorner, anchor);
            AdornerLayer.SetIsClipEnabled(adorner, false);
            layer.Children.Add(adorner);
            _live = adorner;
            adorner.Bubble.Say(text);
            return adorner;
        }

        private void Remove()
        {
            (Parent as Panel)?.Children.Remove(this);
            if (_live == this) _live = null;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            Bubble.Measure(Size.Infinity);
            return default;
        }

        /// <summary>Out to the right of the chip, its tail corner level with the padlock.</summary>
        protected override Size ArrangeOverride(Size finalSize)
        {
            var size = Bubble.DesiredSize;
            Bubble.Arrange(new Rect(new Point(finalSize.Width + 4, finalSize.Height / 2 - size.Height), size));
            return finalSize;
        }
    }
}
