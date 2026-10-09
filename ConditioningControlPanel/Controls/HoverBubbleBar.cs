using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    ///
    /// Nav polish wave 5 (owner, 2026-10-06: the account strip and the bubble row become ONE
    /// line): the bar dresses any <see cref="ButtonBase"/> that carries a Glyph, so a CheckBox
    /// (the Rich Presence switch) is a bubble too and lights up while checked; a child with no
    /// Glyph (the Discord pill, a divider) is left exactly as written. <see cref="FillProperty"/>
    /// gives a bubble a solid rest fill (Logout stays pink: the dangerous one at a glance).
    /// On that line the label FLOATS: the plate grows to the left under a negative margin of the
    /// same width, so the button's layout stays 34 px, nothing beside it moves (the name used
    /// to trim to a star when a label opened) and the open plate, painted opaque, covers its
    /// left neighbour for the moment it is open.
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

        /// <summary>A solid rest fill (alpha 0 = the glass plate). Hover lightens it a little.</summary>
        public static readonly DependencyProperty FillProperty = DependencyProperty.RegisterAttached(
            "Fill", typeof(Color), typeof(HoverBubbleBar), new PropertyMetadata(Colors.Transparent));
        public static Color GetFill(DependencyObject o) => (Color)o.GetValue(FillProperty);
        public static void SetFill(DependencyObject o, Color v) => o.SetValue(FillProperty, v);

        private sealed class Parts
        {
            public Border Plate = null!;
            public Border LabelHost = null!;
            public TextBlock Label = null!;
            public TextBlock Glyph = null!;
            public ScaleTransform Scale = null!;
            public Color RestColor;
            public Color HoverColor;
            public bool Expanded;
        }

        private readonly Dictionary<ButtonBase, Parts> _parts = new();
        private readonly List<ButtonBase> _order = new();
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

        /// <summary>The bubbles in XAML order (left to right): every child with a Glyph.</summary>
        public IReadOnlyList<ButtonBase> Bubbles => _order;

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            foreach (var b in Children.OfType<ButtonBase>().Where(b => !string.IsNullOrEmpty(GetGlyph(b))).ToList())
                Dress(b);
        }

        // ButtonBase so the same bare template fits a Button and a CheckBox (WPF refuses a
        // template whose TargetType is not the element's type or one of its bases).
        private static readonly ControlTemplate BareTemplate = BuildBareTemplate();

        private static ControlTemplate BuildBareTemplate()
        {
            var t = new ControlTemplate(typeof(ButtonBase)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
            t.Seal();
            return t;
        }

        private void Dress(ButtonBase b)
        {
            if (_parts.ContainsKey(b)) return;
            var hue = Hue;
            var fill = GetFill(b);
            bool filled = fill.A > 0;
            var rest = filled ? fill : RestFill;
            // Opaque while open: the floating label sits over the bubble to its left.
            var hover = filled ? Lighten(fill, 0.14) : Over(WithAlpha(hue, 0.30), OpenBase);

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
                Background = new SolidColorBrush(rest),
                BorderBrush = new SolidColorBrush(filled ? Lighten(fill, 0.25) : WithAlpha(hue, 0.55)),
                Child = row,
                RenderTransform = scale,
                RenderTransformOrigin = new Point(1, 0.5),
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

            _parts[b] = new Parts { Plate = plate, LabelHost = labelHost, Label = label, Glyph = glyph, Scale = scale, RestColor = rest, HoverColor = hover };
            _order.Add(b);

            if (b is ToggleButton toggle)
            {
                PaintToggle(toggle);
                toggle.Checked += (_, _) => PaintToggle(toggle);
                toggle.Unchecked += (_, _) => PaintToggle(toggle);
            }

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

        /// <summary>What an open glass plate is composited over: the strip's dark surface.</summary>
        public static readonly Color OpenBase = Color.FromRgb(0x1E, 0x1B, 0x33);

        /// <summary>Source-over: <paramref name="top"/> (with its alpha) over an opaque <paramref name="under"/>.</summary>
        public static Color Over(Color top, Color under)
        {
            double a = top.A / 255.0;
            return Color.FromRgb(
                (byte)Math.Round(top.R * a + under.R * (1 - a)),
                (byte)Math.Round(top.G * a + under.G * (1 - a)),
                (byte)Math.Round(top.B * a + under.B * (1 - a)));
        }

        public static Color WithAlpha(Color c, double a) =>
            Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

        public static Color Lighten(Color c, double t) => Color.FromArgb(c.A,
            (byte)Math.Round(c.R + (255 - c.R) * t), (byte)Math.Round(c.G + (255 - c.G) * t), (byte)Math.Round(c.B + (255 - c.B) * t));

        /// <summary>A switch bubble reads its state on the ring: lit (full hue border, a glow,
        /// a bright glyph) while checked, dim (half-alpha ring, glyph at 55%) while not. The
        /// plate's background stays the hover channel so the two never fight.</summary>
        private void PaintToggle(ToggleButton t)
        {
            if (!_parts.TryGetValue(t, out var p)) return;
            bool on = t.IsChecked == true;
            var hue = Hue;
            p.Plate.BorderBrush = new SolidColorBrush(on ? hue : WithAlpha(hue, 0.45));
            p.Glyph.Opacity = on ? 1.0 : 0.55;
            p.Plate.Effect = on
                ? new System.Windows.Media.Effects.DropShadowEffect { Color = hue, BlurRadius = 12, ShadowDepth = 0, Opacity = 0.75 }
                : null;
        }

        /// <summary>Is a switch bubble painted lit?</summary>
        public bool IsLit(ButtonBase b) => _parts.TryGetValue(b, out var p) && p.Plate.Effect != null;

        /// <summary>Is this bubble showing its label?</summary>
        public bool IsExpanded(ButtonBase b) => _parts.TryGetValue(b, out var p) && p.Expanded;

        /// <summary>The label text a bubble shows when open (for tests and the desk run).</summary>
        public string LabelOf(ButtonBase b) => _parts.TryGetValue(b, out var p) ? p.Label.Text : "";

        /// <summary>The rest fill a bubble was dressed with (glass, or its own Fill).</summary>
        public Color RestFillOf(ButtonBase b) => _parts.TryGetValue(b, out var p) ? p.RestColor : Colors.Transparent;

        /// <summary>Open one bubble (closing any other first).</summary>
        public void Expand(ButtonBase b, bool animate)
        {
            if (!_parts.TryGetValue(b, out var p)) return;
            foreach (var other in _order)
                if (!ReferenceEquals(other, b) && _parts[other].Expanded)
                    Collapse(other, animate);
            if (p.Expanded) return;
            p.Expanded = true;

            p.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double target = Math.Ceiling(p.Label.DesiredSize.Width);
            Panel.SetZIndex(b, 1);
            Animate(p.LabelHost, FrameworkElement.WidthProperty, target, animate);
            AnimateMargin(p.Plate, new Thickness(-target, 0, 0, 0), animate);
            AnimateColor(p.Plate.Background, p.HoverColor, animate);
            AnimateScale(p.Scale, HoverScale, animate);
        }

        public void Collapse(ButtonBase b, bool animate)
        {
            if (!_parts.TryGetValue(b, out var p) || !p.Expanded) return;
            p.Expanded = false;
            Panel.SetZIndex(b, 0);
            Animate(p.LabelHost, FrameworkElement.WidthProperty, 0, animate);
            AnimateMargin(p.Plate, new Thickness(0), animate);
            AnimateColor(p.Plate.Background, p.RestColor, animate);
            AnimateScale(p.Scale, 1.0, animate);
        }

        /// <summary>The plate's negative left margin: the label grows left, the layout does not.</summary>
        private static void AnimateMargin(FrameworkElement el, Thickness to, bool animate)
        {
            if (!animate)
            {
                el.BeginAnimation(FrameworkElement.MarginProperty, null);
                el.Margin = to;
                return;
            }
            var a = new ThicknessAnimation(to, TimeSpan.FromMilliseconds(ExpandMs)) { EasingFunction = Ease, FillBehavior = FillBehavior.HoldEnd };
            a.Completed += (_, _) => { el.BeginAnimation(FrameworkElement.MarginProperty, null); el.Margin = to; };
            el.BeginAnimation(FrameworkElement.MarginProperty, a);
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
            ButtonBase? b = null;
            for (int tries = 0; tries < _order.Count && b == null; tries++)
            {
                var candidate = _order[_pulseIndex % _order.Count];
                _pulseIndex = (_pulseIndex + 1) % _order.Count;
                if (candidate.IsVisible) b = candidate;
            }
            if (b == null) return;
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
