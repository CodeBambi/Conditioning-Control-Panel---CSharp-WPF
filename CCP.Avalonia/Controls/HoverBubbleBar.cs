using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// WPF Controls/HoverBubbleBar.cs (41288a884 + acd51fcf9): a speed dial. Every Button child
    /// carrying a <see cref="GlyphProperty"/> becomes a 34 px round bubble; hover or keyboard focus
    /// slides its label out to the LEFT over 160 ms with a 1.06 pop (one open at a time). The label
    /// floats: the plate grows left under a negative margin of the same width, so the button's
    /// layout stays 34 px and nothing beside it moves. A ToggleButton child (the Rich Presence
    /// CheckBox) is lit while checked. Every 9 s one bubble (round robin) pulses to 1.15 under
    /// AllowAmbientLoops; Motion Off = instant states. Children without a Glyph are left alone.
    /// Head difference: WPF draws Segoe MDL2 glyphs, which Linux has no font for; the bubbles carry
    /// the colour emoji the old pills already used (CLAUDE.md: Avalonia renders them natively).
    /// </summary>
    public sealed class HoverBubbleBar : StackPanel
    {
        public const double BubbleSize = 34;
        public const double Gap = 8;
        public const double HoverScale = 1.06;
        public const double PulseScale = 1.15;
        public const int ExpandMs = 160;
        public static readonly TimeSpan PulseEvery = TimeSpan.FromSeconds(9);

        public static readonly AttachedProperty<string> GlyphProperty =
            AvaloniaProperty.RegisterAttached<HoverBubbleBar, Control, string>("Glyph", "");
        public static string GetGlyph(Control o) => o.GetValue(GlyphProperty);
        public static void SetGlyph(Control o, string v) => o.SetValue(GlyphProperty, v);

        /// <summary>Loc keys for the label, joined by '+' ("section_scheduler+section_intensity_ramp").</summary>
        public static readonly AttachedProperty<string> LabelKeysProperty =
            AvaloniaProperty.RegisterAttached<HoverBubbleBar, Control, string>("LabelKeys", "");
        public static string GetLabelKeys(Control o) => o.GetValue(LabelKeysProperty);
        public static void SetLabelKeys(Control o, string v) => o.SetValue(LabelKeysProperty, v);

        /// <summary>A solid rest fill (alpha 0 = the glass plate). Hover lightens it a little.</summary>
        public static readonly AttachedProperty<Color> FillProperty =
            AvaloniaProperty.RegisterAttached<HoverBubbleBar, Control, Color>("Fill", Colors.Transparent);
        public static Color GetFill(Control o) => o.GetValue(FillProperty);
        public static void SetFill(Control o, Color v) => o.SetValue(FillProperty, v);

        public static readonly Color RestFill = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);
        /// <summary>What an open glass plate is composited over: the strip's dark surface.</summary>
        public static readonly Color OpenBase = Color.FromRgb(0x1E, 0x1B, 0x33);

        public static Color Hue
        {
            get
            {
                var rgb = NavStripTable.AccentRgb(NavSections.Home);
                return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            }
        }

        private sealed class Parts
        {
            public Border Plate = null!;
            public Border LabelHost = null!;
            public TextBlock Label = null!;
            public TextBlock Glyph = null!;
            public ScaleTransform Scale = null!;
            public Color RestColor, HoverColor;
            public bool Expanded;
        }

        private readonly Dictionary<Button, Parts> _parts = new();
        private readonly List<Button> _order = new();
        private readonly DispatcherTimer _pulseTimer = new(DispatcherPriority.Background) { Interval = PulseEvery };
        private int _pulseIndex;

        public HoverBubbleBar()
        {
            Orientation = Orientation.Horizontal;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Center;
            _pulseTimer.Tick += (_, _) => PulseNext();
            // P65: a timer stopped only on hide keeps a closed window alive.
            DetachedFromVisualTree += (_, _) => _pulseTimer.Stop();
        }

        /// <summary>The bubbles in XAML order (left to right): every child with a Glyph.</summary>
        public IReadOnlyList<Button> Bubbles => _order;

        /// <summary>The idle pulse runs only while the host says Home is attached, shown and not
        /// minimised (WPF: Loaded/Unloaded; this head hides tabs with IsVisible, P01).</summary>
        public void SetPulseActive(bool active)
        {
            if (active && _order.Count > 0) _pulseTimer.Start();
            else _pulseTimer.Stop();
        }

        internal bool PulseRunning => _pulseTimer.IsEnabled;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            foreach (var b in Children.OfType<Button>().Where(b => !string.IsNullOrEmpty(GetGlyph(b))).ToList())
                Dress(b);
        }

        private static readonly FuncControlTemplate<Button> BareTemplate = new((b, _) => new ContentPresenter
        {
            Name = "PART_ContentPresenter",
            Background = Brushes.Transparent,
            [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty],
        });

        private void Dress(Button b)
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
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 2, 0),
                TextWrapping = TextWrapping.NoWrap,
            };
            var keys = GetLabelKeys(b);
            if (!string.IsNullOrWhiteSpace(keys))
            {
                label.Bind(TextBlock.TextProperty, LabelBinding(keys));
                b.Bind(AutomationProperties.NameProperty, LabelBinding(keys));
            }

            var labelHost = new Border { Width = 0, ClipToBounds = true, Child = label, VerticalAlignment = VerticalAlignment.Stretch };
            var glyph = new TextBlock
            {
                Text = GetGlyph(b),
                FontSize = 15,
                Foreground = Brushes.White,
                Width = BubbleSize - 3,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var scale = new ScaleTransform(1, 1);
            var plate = new Border
            {
                Height = BubbleSize,
                MinWidth = BubbleSize,
                CornerRadius = new CornerRadius(BubbleSize / 2),
                BorderThickness = new Thickness(1.5),
                Background = new SolidColorBrush(rest),
                BorderBrush = new SolidColorBrush(filled ? Lighten(fill, 0.25) : WithAlpha(hue, 0.55)),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { labelHost, glyph } },
                RenderTransform = scale,
                RenderTransformOrigin = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            };

            b.Template = BareTemplate;
            b.Content = plate;
            b.Cursor = new Cursor(StandardCursorType.Hand);
            b.Background = Brushes.Transparent;
            b.ClipToBounds = false;   // the app's Button style clips; the label floats outside the 34 px slot
            b.Padding = default;
            b.Focusable = true;
            b.Margin = new Thickness(_order.Count == 0 ? 0 : Gap, 0, 0, 0);
            b.VerticalAlignment = VerticalAlignment.Center;

            _parts[b] = new Parts { Plate = plate, LabelHost = labelHost, Label = label, Glyph = glyph, Scale = scale, RestColor = rest, HoverColor = hover };
            _order.Add(b);

            if (b is ToggleButton toggle)
            {
                PaintToggle(toggle);
                toggle.IsCheckedChanged += (_, _) => PaintToggle(toggle);
            }

            b.PointerEntered += (_, _) => Expand(b, AmbientFxCanvas.Env.AllowTransitions);
            b.PointerExited += (_, _) => { if (!b.IsKeyboardFocusWithin) Collapse(b, AmbientFxCanvas.Env.AllowTransitions); };
            b.GotFocus += (_, _) => Expand(b, AmbientFxCanvas.Env.AllowTransitions);
            b.LostFocus += (_, _) => { if (!b.IsPointerOver) Collapse(b, AmbientFxCanvas.Env.AllowTransitions); };
        }

        private static MultiBinding LabelBinding(string keys)
        {
            var mb = new MultiBinding { Converter = LabelJoin.Instance, Mode = BindingMode.OneWay };
            foreach (var key in keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                mb.Bindings.Add(new Binding($"[{key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
            return mb;
        }

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

        /// <summary>A switch bubble reads its state on the ring: lit (full hue border, a glow, a
        /// bright glyph) while checked, dim (half-alpha ring, glyph at 55%) while not.</summary>
        private void PaintToggle(ToggleButton t)
        {
            if (!_parts.TryGetValue(t, out var p)) return;
            bool on = t.IsChecked == true;
            var hue = Hue;
            p.Plate.BorderBrush = new SolidColorBrush(on ? hue : WithAlpha(hue, 0.45));
            p.Glyph.Opacity = on ? 1.0 : 0.55;
            p.Plate.Effect = on ? new DropShadowEffect { Color = hue, BlurRadius = 12, OffsetX = 0, OffsetY = 0, Opacity = 0.75 } : null;
        }

        /// <summary>Is a switch bubble painted lit?</summary>
        public bool IsLit(Button b) => _parts.TryGetValue(b, out var p) && p.Plate.Effect != null;

        /// <summary>Is this bubble showing its label?</summary>
        public bool IsExpanded(Button b) => _parts.TryGetValue(b, out var p) && p.Expanded;

        /// <summary>The label text a bubble shows when open.</summary>
        public string LabelOf(Button b) => _parts.TryGetValue(b, out var p) ? p.Label.Text ?? "" : "";

        /// <summary>The rest fill a bubble was dressed with (glass, or its own Fill).</summary>
        public Color RestFillOf(Button b) => _parts.TryGetValue(b, out var p) ? p.RestColor : Colors.Transparent;

        /// <summary>The open label's width (0 when collapsed): the floating plate's left overhang.</summary>
        internal double LabelWidthOf(Button b) => _parts.TryGetValue(b, out var p) ? p.LabelHost.Width : 0;

        /// <summary>Open one bubble (closing any other first).</summary>
        public void Expand(Button b, bool animate)
        {
            if (!_parts.TryGetValue(b, out var p)) return;
            foreach (var other in _order)
                if (!ReferenceEquals(other, b) && _parts[other].Expanded)
                    Collapse(other, animate);
            if (p.Expanded) return;
            p.Expanded = true;
            p.Label.Measure(Size.Infinity);
            double target = Math.Ceiling(p.Label.DesiredSize.Width);
            b.ZIndex = 1;
            Apply(p, target, p.HoverColor, HoverScale, animate);
        }

        public void Collapse(Button b, bool animate)
        {
            if (!_parts.TryGetValue(b, out var p) || !p.Expanded) return;
            p.Expanded = false;
            b.ZIndex = 0;
            Apply(p, 0, p.RestColor, 1.0, animate);
        }

        private static void Apply(Parts p, double labelWidth, Color fill, double scale, bool animate)
        {
            var d = TimeSpan.FromMilliseconds(ExpandMs);
            var ease = new CubicEaseOut();
            p.LabelHost.Transitions = animate ? new Transitions { new DoubleTransition { Property = WidthProperty, Duration = d, Easing = ease } } : null;
            p.Plate.Transitions = animate
                ? new Transitions
                {
                    new ThicknessTransition { Property = MarginProperty, Duration = d, Easing = ease },
                    new BrushTransition { Property = Border.BackgroundProperty, Duration = d, Easing = ease },
                }
                : null;
            p.Scale.Transitions = animate
                ? new Transitions
                {
                    new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = d, Easing = ease },
                    new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = d, Easing = ease },
                }
                : null;
            p.LabelHost.Width = labelWidth;
            p.Plate.Margin = new Thickness(-labelWidth, 0, 0, 0);
            p.Plate.Background = new SolidColorBrush(fill);
            p.Scale.ScaleX = p.Scale.ScaleY = scale;
        }

        // ---- idle cue ----

        /// <summary>Round robin: the next visible bubble breathes to 1.15 and back, unless one is
        /// open or ambient loops are off. Returns the bubble that pulsed (tests).</summary>
        internal Button? PulseNext()
        {
            if (!AmbientFxCanvas.Env.AllowAmbientLoops || !IsEffectivelyVisible || _order.Count == 0) return null;
            if (_order.Any(b => _parts[b].Expanded)) return null;
            Button? b = null;
            for (int tries = 0; tries < _order.Count && b == null; tries++)
            {
                var candidate = _order[_pulseIndex % _order.Count];
                _pulseIndex = (_pulseIndex + 1) % _order.Count;
                if (candidate.IsVisible) b = candidate;
            }
            if (b == null) return null;
            var pulse = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(440),
                Easing = new SineEaseInOut(),
                Children =
                {
                    Frame(0, 1.0), Frame(0.5, PulseScale), Frame(1, 1.0),
                },
            };
            _ = pulse.RunAsync(_parts[b].Plate);   // TransformAnimator drives the plate's ScaleTransform
            return b;
        }

        private static KeyFrame Frame(double cue, double s) => new()
        {
            Cue = new Cue(cue),
            Setters = { new Setter(ScaleTransform.ScaleXProperty, s), new Setter(ScaleTransform.ScaleYProperty, s) },
        };

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
            public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
                string.Join(" + ", values.Select(v => StripLeadingGlyph(v as string)).Where(v => v.Length > 0));
        }
    }
}
