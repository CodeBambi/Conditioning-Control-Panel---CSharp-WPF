using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// Circe says a line: a small paper speech bubble with her padlock face, the same cream stock
    /// as the tab's tag and the bill. Pops in, holds, fades, and collapses itself. Text only.
    /// Literal colours: paper looks like paper under every mod skin.
    /// </summary>
    public sealed class CirceSays : Border
    {
        public const int HoldMs = 4200;

        private static readonly Color Paper = Color.FromRgb(0xF3, 0xEA, 0xD9);
        internal static readonly Color InkColour = Color.FromRgb(0x24, 0x1A, 0x2E);

        private readonly TextBlock _text;
        private readonly ScaleTransform _pop = new(1, 1);
        private readonly DispatcherTimer _hold;

        /// <summary>The bubble finished and collapsed.</summary>
        public event Action? Finished;

        public CirceSays()
        {
            Background = Frozen(Paper);
            CornerRadius = new CornerRadius(13, 13, 13, 3);
            Padding = new Thickness(6, 6, 11, 6);
            MaxWidth = 230;
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;
            RenderTransformOrigin = new Point(0.1, 1);
            RenderTransform = _pop;
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 4, Opacity = 0.5 };

            _text = new TextBlock
            {
                FontFamily = new FontFamily("/Fonts/#Fredoka, Segoe UI"),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                FontStyle = FontStyles.Italic,
                Foreground = Frozen(InkColour),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 0, 0),
            };
            var row = new DockPanel();
            var face = Face(22);
            DockPanel.SetDock(face, Dock.Left);
            row.Children.Add(face);
            row.Children.Add(_text);
            Child = row;

            _hold = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(HoldMs) };
            _hold.Tick += (_, _) => { _hold.Stop(); Hush(); };
        }

        /// <summary>What she is saying, for the tests.</summary>
        internal string Text => _text.Text;

        /// <summary>Her padlock face: a lilac-to-pink disc with an ink padlock. Also used on the bill.</summary>
        public static FrameworkElement Face(double size)
        {
            var disc = new LinearGradientBrush(Color.FromRgb(0xD9, 0xC4, 0xFF), Color.FromRgb(0xFF, 0x8F, 0xB1), 45);
            disc.Freeze();
            var ink = Frozen(InkColour);
            var art = new Canvas { Width = 14, Height = 15 };
            art.Children.Add(new Path
            {
                Data = Geometry.Parse("M4,7 V5 a3,3 0 0 1 6,0 V7"),
                Stroke = ink,
                StrokeThickness = 1.8,
            });
            art.Children.Add(new Rectangle { Width = 10, Height = 7.5, RadiusX = 2, RadiusY = 2, Fill = ink, Margin = new Thickness(2, 6.5, 0, 0) });
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = disc,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Viewbox { Width = size * 0.58, Height = size * 0.58, Child = art },
            };
        }

        /// <summary>Say a line. A new line replaces the one showing and restarts the hold.</summary>
        public void Say(string text)
        {
            _text.Text = text;
            Visibility = Visibility.Visible;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            if (MotionFx.Level == MotionLevel.Full)
            {
                var grow = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut },
                };
                _pop.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                _pop.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }
            _hold.Stop();
            _hold.Start();
        }

        /// <summary>Take the line away: a short fade, or at once with motion off.</summary>
        public void Hush()
        {
            _hold.Stop();
            if (Visibility != Visibility.Visible) return;
            if (!MotionFx.AllowTransitions) { Done(); return; }
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            fade.Completed += (_, _) => { if (Opacity <= 0.01) Done(); };
            BeginAnimation(OpacityProperty, fade);
        }

        private void Done()
        {
            BeginAnimation(OpacityProperty, null);
            Visibility = Visibility.Collapsed;
            Finished?.Invoke();
        }

        private static Brush Frozen(Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// The rail's speech bubble: a <see cref="CirceSays"/> in the adorner layer beside the padlock
    /// chip, for the same reason the flashing figure is an adorner (above the chrome, not clipped
    /// by the rail, no window that could hold the process open). One at a time; it takes itself off
    /// when the bubble finishes.
    /// </summary>
    internal sealed class CirceSaysAdorner : Adorner
    {
        private static CirceSaysAdorner? _live;

        private readonly VisualCollection _children;
        private readonly CirceSays _bubble = new();
        private AdornerLayer? _layer;

        private CirceSaysAdorner(UIElement adorned) : base(adorned)
        {
            IsHitTestVisible = false;
            _children = new VisualCollection(this) { _bubble };
            _bubble.Finished += Remove;
        }

        /// <summary>Say a line beside <paramref name="anchor"/>. Quietly nothing when it has no
        /// adorner layer (not rendered yet, window on its way out): decoration fails silent.</summary>
        internal static void Show(UIElement anchor, string text)
        {
            if (_live is { } live && live.AdornedElement == anchor) { live._bubble.Say(text); return; }
            _live?.Remove();
            var layer = AdornerLayer.GetAdornerLayer(anchor);
            if (layer is null) return;
            var adorner = new CirceSaysAdorner(anchor) { _layer = layer };
            layer.Add(adorner);
            _live = adorner;
            adorner._bubble.Say(text);
        }

        private void Remove()
        {
            try { _layer?.Remove(this); }
            catch (Exception ex) { Diag.Swallowed(ex, "circe says remove"); }
            _layer = null;
            if (_live == this) _live = null;
        }

        // The Adorner base constructor asks before the derived fields exist.
        protected override int VisualChildrenCount => _children?.Count ?? 0;

        protected override Visual GetVisualChild(int index) => _children[index];

        protected override Size MeasureOverride(Size constraint)
        {
            _bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            // Out to the right of the chip, its tail corner level with the padlock.
            var size = _bubble.DesiredSize;
            _bubble.Arrange(new Rect(new Point(finalSize.Width + 4, finalSize.Height / 2 - size.Height), size));
            return finalSize;
        }
    }
}
