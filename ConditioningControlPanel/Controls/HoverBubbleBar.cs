using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// Nav polish wave 4 (owner, 2026-10-06: "this whole rail can be lost, make them little
    /// bubbles that expand on hover, have them on the bottom right under the join our discord
    /// community"). A speed dial: the Buttons placed inside it in XAML keep their x:Name, Click
    /// and ToolTip, and the bar dresses each one as a 34 px round bubble showing a Segoe MDL2
    /// glyph (<see cref="GlyphProperty"/>). Hover or keyboard focus slides the label out to the
    /// LEFT of the glyph (the bar is right-aligned, so the glyph stays under the pointer) over
    /// 160 ms with a 1.06 pop; leaving collapses it. One bubble open at a time. Every 9 s one
    /// bubble (round robin) gives a 1.15 pulse so the row is findable, under AllowAmbientLoops
    /// only. Motion Off = instant states, no pulse.
    /// </summary>
    public sealed class HoverBubbleBar : StackPanel
    {
        public const double BubbleSize = 34;
        public const double Gap = 8;
        public const double HoverScale = 1.06;
        public const double PulseScale = 1.15;
        public const int ExpandMs = 160;
        public static readonly TimeSpan PulseEvery = TimeSpan.FromSeconds(9);

        /// <summary>Same font chain every glyph TextBlock in the app needs (FontFallbackTests).</summary>
        public const string GlyphFont = "Segoe MDL2 Assets, Segoe UI Symbol, Segoe UI";

        public static readonly DependencyProperty GlyphProperty = DependencyProperty.RegisterAttached(
            "Glyph", typeof(string), typeof(HoverBubbleBar), new PropertyMetadata(""));
        public static string GetGlyph(DependencyObject o) => (string)o.GetValue(GlyphProperty);
        public static void SetGlyph(DependencyObject o, string v) => o.SetValue(GlyphProperty, v);

        /// <summary>Loc keys for the label, joined by '+' ("section_scheduler+section_intensity_ramp").</summary>
        public static readonly DependencyProperty LabelKeysProperty = DependencyProperty.RegisterAttached(
            "LabelKeys", typeof(string), typeof(HoverBubbleBar), new PropertyMetadata(""));
        public static string GetLabelKeys(DependencyObject o) => (string)o.GetValue(LabelKeysProperty);
        public static void SetLabelKeys(DependencyObject o, string v) => o.SetValue(LabelKeysProperty, v);

        private sealed class Parts
        {
            public Border Plate = null!;
            public Border LabelHost = null!;
            public TextBlock Label = null!;
            public ScaleTransform Scale = null!;
            public bool Expanded;
        }

        private readonly Dictionary<Button, Parts> _parts = new();
        private readonly List<Button> _order = new();
        private DispatcherTimer? _pulseTimer;
        private int _pulseIndex;

        public static Color Hue => NavStripRules.Accent(NavSections.Home);

        public HoverBubbleBar()
        {
            Orientation = Orientation.Horizontal;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Center;
            Loaded += (_, _) => StartPulse();
            Unloaded += (_, _) => StopPulse();
        }

        /// <summary>The bubbles in XAML order (left to right).</summary>
        public IReadOnlyList<Button> Bubbles => _order;

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            foreach (var b in Children.OfType<Button>().ToList())
                Dress(b);
        }

        private static readonly ControlTemplate BareTemplate = BuildBareTemplate();

        private static ControlTemplate BuildBareTemplate()
        {
            var t = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
            t.Seal();
            return t;
        }

        private void Dress(Button b)
        {
            if (_parts.ContainsKey(b)) return;
            var hue = Hue;

            var label = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 2, 0),
                TextWrapping = TextWrapping.NoWrap,
            };
            var keys = GetLabelKeys(b);
            if (!string.IsNullOrWhiteSpace(keys))
            {
                var mb = LabelBinding(keys);
                label.SetBinding(TextBlock.TextProperty, mb);
                b.SetBinding(AutomationProperties.NameProperty, LabelBinding(keys));
            }

            var labelHost = new Border { Width = 0, ClipToBounds = true, Child = label, VerticalAlignment = VerticalAlignment.Stretch };
            var glyph = new TextBlock
            {
                Text = GetGlyph(b),
                FontFamily = new FontFamily(GlyphFont),
                FontSize = 15,
                Foreground = Brushes.White,
                Width = BubbleSize - 3,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { labelHost, glyph } };
            var scale = new ScaleTransform(1, 1);
            var plate = new Border
            {
                Height = BubbleSize,
                MinWidth = BubbleSize,
                CornerRadius = new CornerRadius(BubbleSize / 2),
                BorderThickness = new Thickness(1.5),
                Background = new SolidColorBrush(RestFill),
                BorderBrush = new SolidColorBrush(WithAlpha(hue, 0.55)),
                Child = row,
                RenderTransform = scale,
                RenderTransformOrigin = new Point(0.5, 0.5),
                SnapsToDevicePixels = true,
            };

            b.Template = BareTemplate;
            b.Content = plate;
            b.Cursor = Cursors.Hand;
            b.Background = Brushes.Transparent;
            b.Focusable = true;
            int index = _order.Count;
            b.Margin = new Thickness(index == 0 ? 0 : Gap, 0, 0, 0);
            b.VerticalAlignment = VerticalAlignment.Center;

            _parts[b] = new Parts { Plate = plate, LabelHost = labelHost, Label = label, Scale = scale };
            _order.Add(b);

            b.MouseEnter += (_, _) => Expand(b, MotionFx.AllowTransitions);
            b.MouseLeave += (_, _) => { if (!b.IsKeyboardFocusWithin) Collapse(b, MotionFx.AllowTransitions); };
            b.GotKeyboardFocus += (_, _) => Expand(b, MotionFx.AllowTransitions);
            b.LostKeyboardFocus += (_, _) => { if (!b.IsMouseOver) Collapse(b, MotionFx.AllowTransitions); };
        }

        private static MultiBinding LabelBinding(string keys)
        {
            var mb = new MultiBinding { Converter = LabelJoin.Instance, Mode = BindingMode.OneWay };
            foreach (var key in keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                mb.Bindings.Add(new Binding($"[{key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
            return mb;
        }

        public static readonly Color RestFill = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);

        public static Color WithAlpha(Color c, double a) =>
            Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

        /// <summary>Is this bubble showing its label?</summary>
        public bool IsExpanded(Button b) => _parts.TryGetValue(b, out var p) && p.Expanded;

        /// <summary>The label text a bubble shows when open (for tests and the desk run).</summary>
        public string LabelOf(Button b) => _parts.TryGetValue(b, out var p) ? p.Label.Text : "";

        /// <summary>Open one bubble (closing any other first).</summary>
        public void Expand(Button b, bool animate)
        {
            if (!_parts.TryGetValue(b, out var p)) return;
            foreach (var other in _order)
                if (!ReferenceEquals(other, b) && _parts[other].Expanded)
                    Collapse(other, animate);
            if (p.Expanded) return;
            p.Expanded = true;

            p.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double target = Math.Ceiling(p.Label.DesiredSize.Width);
            Animate(p.LabelHost, FrameworkElement.WidthProperty, target, animate);
            AnimateColor(p.Plate.Background, WithAlpha(Hue, 0.30), animate);
            AnimateScale(p.Scale, HoverScale, animate);
        }

        public void Collapse(Button b, bool animate)
        {
            if (!_parts.TryGetValue(b, out var p) || !p.Expanded) return;
            p.Expanded = false;
            Animate(p.LabelHost, FrameworkElement.WidthProperty, 0, animate);
            AnimateColor(p.Plate.Background, RestFill, animate);
            AnimateScale(p.Scale, 1.0, animate);
        }

        private static IEasingFunction Ease => new CubicEase { EasingMode = EasingMode.EaseOut };

        private static void Animate(UIElement el, DependencyProperty dp, double to, bool animate)
        {
            if (!animate)
            {
                el.BeginAnimation(dp, null);
                el.SetValue(dp, to);
                return;
            }
            // Start from the live width (the property itself may still be the NaN-free 0 or the old target).
            double from = el is FrameworkElement fe && !double.IsNaN(fe.ActualWidth) && dp == FrameworkElement.WidthProperty
                ? fe.ActualWidth : (double)el.GetValue(dp);
            var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ExpandMs)) { EasingFunction = Ease, FillBehavior = FillBehavior.HoldEnd };
            a.Completed += (_, _) => { el.BeginAnimation(dp, null); el.SetValue(dp, to); };
            el.SetValue(dp, to);
            el.BeginAnimation(dp, a);
        }

        private static void AnimateColor(Brush brush, Color to, bool animate)
        {
            if (brush is not SolidColorBrush s || s.IsFrozen) return;
            if (!animate)
            {
                s.BeginAnimation(SolidColorBrush.ColorProperty, null);
                s.Color = to;
                return;
            }
            var a = new ColorAnimation(to, TimeSpan.FromMilliseconds(ExpandMs)) { EasingFunction = Ease };
            s.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }

        private static void AnimateScale(ScaleTransform s, double to, bool animate)
        {
            if (!animate)
            {
                s.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                s.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                s.ScaleX = s.ScaleY = to;
                return;
            }
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ExpandMs)) { EasingFunction = Ease };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        // ---- idle cue ----

        private void StartPulse()
        {
            if (_pulseTimer != null || _order.Count == 0) return;
            _pulseTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PulseEvery };
            _pulseTimer.Tick += (_, _) => PulseNext();
            _pulseTimer.Start();
        }

        private void StopPulse()
        {
            _pulseTimer?.Stop();
            _pulseTimer = null;
        }

        /// <summary>Round robin: the next bubble breathes to 1.15 and back, unless one is open or loops are off.</summary>
        internal void PulseNext()
        {
            if (!MotionFx.AllowAmbientLoops || !IsVisible || _order.Count == 0) return;
            if (_order.Any(b => _parts[b].Expanded)) return;
            var b = _order[_pulseIndex % _order.Count];
            _pulseIndex = (_pulseIndex + 1) % _order.Count;
            var s = _parts[b].Scale;
            var a = new DoubleAnimation(1.0, PulseScale, TimeSpan.FromMilliseconds(220))
            {
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop,
            };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        /// <summary>
        /// Several labels the old pills carried start with an emoji ("⚙ System", "📅 Scheduler"):
        /// the bubble already shows a glyph, so the label drops everything before the first letter
        /// or digit. Two keys join as "A + B".
        /// </summary>
        public static string StripLeadingGlyph(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = 0;
            while (i < s.Length && !char.IsLetterOrDigit(s, i)) i++;
            return s.Substring(i).Trim();
        }

        private sealed class LabelJoin : IMultiValueConverter
        {
            public static readonly LabelJoin Instance = new();
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
                string.Join(" + ", values.Select(v => StripLeadingGlyph(v as string)).Where(v => v.Length > 0));
            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
                throw new NotSupportedException();
        }
    }
}
