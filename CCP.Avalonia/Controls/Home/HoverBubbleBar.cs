// PORTED from WPF 7.1.5 ConditioningControlPanel/Controls/HoverBubbleBar.cs (parity lane E3).
//
// The Home account strip's line of glyph bubbles (nav polish wave 4/5). Every child button that
// carries a Glyph is dressed as a 34 px round bubble; hovering (or focusing) one opens its label
// to the LEFT out of the bubble, one label at a time, and an idle pulse walks the line every 9 s
// so the strip reads as alive. Children without a Glyph (the Discord pill, the dividers) are left
// alone. Numbers and colour maths are Core HomeDashboardRules; WPF's storyboards are Avalonia
// transitions here, switched off under Motion Off (AmbientFxCanvas.Env.AllowTransitions).
//
// Linux has no Segoe MDL2: a glyph the font lacks would draw nothing on a bubble that is ONLY a
// glyph, so each bubble may name a FallbackGlyph (an emoji) used when the MDL2 glyph does not
// render (the same check the section strip uses).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.NavRail;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Services;
using R = ConditioningControlPanel.Services.HomeDashboardRules;

namespace ConditioningControlPanel.Avalonia.Controls.Home
{
    public sealed class HoverBubbleBar : StackPanel
    {
        public const string GlyphFont = "Segoe MDL2 Assets, Segoe UI Symbol, Segoe UI";

        public static readonly AttachedProperty<string> GlyphProperty =
            AvaloniaProperty.RegisterAttached<HoverBubbleBar, Control, string>("Glyph", "");
        public static string GetGlyph(Control o) => o.GetValue(GlyphProperty);
        public static void SetGlyph(Control o, string v) => o.SetValue(GlyphProperty, v);

        /// <summary>Shown instead of <see cref="GlyphProperty"/> where the MDL2 font cannot draw it.</summary>
        public static readonly AttachedProperty<string> FallbackGlyphProperty =
            AvaloniaProperty.RegisterAttached<HoverBubbleBar, Control, string>("FallbackGlyph", "");
        public static string GetFallbackGlyph(Control o) => o.GetValue(FallbackGlyphProperty);
        public static void SetFallbackGlyph(Control o, string v) => o.SetValue(FallbackGlyphProperty, v);

        /// <summary>Loc keys for the label, joined with "+" ("section_scheduler+section_intensity_ramp").</summary>
        public static readonly AttachedProperty<string> LabelKeysProperty =
            AvaloniaProperty.RegisterAttached<HoverBubbleBar, Control, string>("LabelKeys", "");
        public static string GetLabelKeys(Control o) => o.GetValue(LabelKeysProperty);
        public static void SetLabelKeys(Control o, string v) => o.SetValue(LabelKeysProperty, v);

        /// <summary>A filled bubble (Logout's pink). Transparent = the plain glass bubble.</summary>
        public static readonly AttachedProperty<Color> FillProperty =
            AvaloniaProperty.RegisterAttached<HoverBubbleBar, Control, Color>("Fill", Colors.Transparent);
        public static Color GetFill(Control o) => o.GetValue(FillProperty);
        public static void SetFill(Control o, Color v) => o.SetValue(FillProperty, v);

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

        private readonly Dictionary<Button, Parts> _parts = new();
        private readonly List<Button> _order = new();
        private DispatcherTimer? _pulseTimer;
        private int _pulseIndex;

        /// <summary>Home's hue (the section table), as the bubbles' rim and hover tint.</summary>
        public static Color Hue => ToColor(NavStripRules.Accent(NavSections.Home));

        public HoverBubbleBar()
        {
            Orientation = Orientation.Horizontal;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Center;
            AttachedToVisualTree += (_, _) => StartPulse();
            DetachedFromVisualTree += (_, _) => StopPulse();
        }

        public IReadOnlyList<Button> Bubbles => _order;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            foreach (var b in Children.OfType<Button>().Where(b => !string.IsNullOrEmpty(GetGlyph(b))).ToList())
                Dress(b);
        }

        private static readonly IControlTemplate BareTemplate = new FuncControlTemplate<ContentControl>((c, _) =>
            new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                // Fluent's Button:pointerover/:pressed setters reach this presenter by name and
                // painted a grey square behind the open plate; local values outrank them.
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                [!ContentPresenter.ContentProperty] = c[!ContentControl.ContentProperty],
            });

        private static bool Animate => AmbientFxCanvas.Env.AllowTransitions;

        private void Dress(Button b)
        {
            if (_parts.ContainsKey(b)) return;
            uint hue = NavStripRules.Accent(NavSections.Home);
            var fillC = GetFill(b);
            uint fill = ToArgb(fillC);
            bool filled = fillC.A > 0;
            var rest = filled ? fillC : ToColor(R.BubbleRestFill);
            var hover = ToColor(R.BubbleHover(hue, fill));

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
                b.Bind(global::Avalonia.Automation.AutomationProperties.NameProperty, LabelBinding(keys));
            }

            var labelHost = new Border { Width = 0, ClipToBounds = true, Child = label, VerticalAlignment = VerticalAlignment.Stretch };
            string glyphText = GetGlyph(b);
            var fallback = GetFallbackGlyph(b);
            bool mdl2 = SectionTabStrip.GlyphRenders(glyphText) || string.IsNullOrEmpty(fallback);
            var glyph = new TextBlock
            {
                Text = mdl2 ? glyphText : fallback,
                FontFamily = mdl2 ? new FontFamily(GlyphFont) : FontFamily.Default,
                FontSize = mdl2 ? 15 : 14,
                Foreground = Brushes.White,
                Width = R.BubbleSize - 3,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { labelHost, glyph } };
            var scale = new ScaleTransform(1, 1);
            var plate = new Border
            {
                Height = R.BubbleSize,
                MinWidth = R.BubbleSize,
                CornerRadius = new CornerRadius(R.BubbleSize / 2),
                BorderThickness = new Thickness(1.5),
                Background = new SolidColorBrush(rest),
                BorderBrush = new SolidColorBrush(ToColor(R.BubbleBorder(hue, fill))),
                Child = row,
                RenderTransform = scale,
                RenderTransformOrigin = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            };

            b.Template = BareTemplate;
            b.Content = plate;
            b.Cursor = new Cursor(StandardCursorType.Hand);
            b.Background = Brushes.Transparent;
            b.Padding = new Thickness(0);
            b.BorderThickness = new Thickness(0);
            b.Focusable = true;
            // The label FLOATS: the plate grows left past the button's 34 px slot under a
            // negative margin. The Fluent Button theme clips a Button to its bounds, which cut
            // the open plate back to a 34 px square and hid every label (parity fix, 2026-10-09);
            // WPF never clipped here. A local value outranks the theme setter.
            b.ClipToBounds = false;
            int index = _order.Count;
            b.Margin = new Thickness(index == 0 ? 0 : R.BubbleGap, 0, 0, 0);
            b.VerticalAlignment = VerticalAlignment.Center;

            _parts[b] = new Parts { Plate = plate, LabelHost = labelHost, Label = label, Glyph = glyph, Scale = scale, RestColor = rest, HoverColor = hover };
            _order.Add(b);

            if (b is ToggleButton toggle)
            {
                PaintToggle(toggle);
                toggle.IsCheckedChanged += (_, _) => PaintToggle(toggle);
            }

            b.PointerEntered += (_, _) => Expand(b, Animate);
            b.PointerExited += (_, _) => { if (!b.IsKeyboardFocusWithin) Collapse(b, Animate); };
            b.GotFocus += (_, _) => Expand(b, Animate);
            b.LostFocus += (_, _) => { if (!b.IsPointerOver) Collapse(b, Animate); };
        }

        private static MultiBinding LabelBinding(string keys)
        {
            var mb = new MultiBinding { Converter = LabelJoin, Mode = BindingMode.OneWay };
            foreach (var key in keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                mb.Bindings.Add(new Binding($"[{key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
            return mb;
        }

        private static readonly IMultiValueConverter LabelJoin = new FuncMultiValueConverter<object?, string>(values =>
            string.Join(" + ", values.Select(v => StripLeadingGlyph(v as string)).Where(v => v.Length > 0)));

        /// <summary>Loc strings like "📅 Scheduler" lead with an emoji; the bubble already has a glyph.</summary>
        public static string StripLeadingGlyph(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = 0;
            while (i < s.Length && !char.IsLetterOrDigit(s, i)) i++;
            return s.Substring(i).Trim();
        }

        private void PaintToggle(ToggleButton t)
        {
            if (!_parts.TryGetValue(t, out var p)) return;
            bool on = t.IsChecked == true;
            var hue = Hue;
            p.Plate.BorderBrush = new SolidColorBrush(on ? hue : ToColor(R.WithAlpha(ToArgb(hue), 0.45)));
            p.Glyph.Opacity = on ? 1.0 : 0.55;
            // WPF lit the plate with a DropShadowEffect; the depth law forbids an Effect over a
            // chip, and Avalonia's BoxShadow is the same glow without one.
            p.Plate.BoxShadow = on
                ? new BoxShadows(new BoxShadow { Color = Color.FromArgb(0xBF, hue.R, hue.G, hue.B), Blur = 12 })
                : default;
        }

        public bool IsLit(Button b) => _parts.TryGetValue(b, out var p) && p.Plate.BoxShadow.Count > 0;
        public bool IsExpanded(Button b) => _parts.TryGetValue(b, out var p) && p.Expanded;
        public string LabelOf(Button b) => _parts.TryGetValue(b, out var p) ? p.Label.Text ?? "" : "";
        public Color RestFillOf(Button b) => _parts.TryGetValue(b, out var p) ? p.RestColor : Colors.Transparent;

        /// <summary>Opens one bubble's label to the left; any other open one closes.</summary>
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
            Tween(p, animate);
            p.LabelHost.Width = target;
            p.Plate.Margin = new Thickness(-target, 0, 0, 0);
            ((SolidColorBrush)p.Plate.Background!).Color = p.HoverColor;
            p.Scale.ScaleX = p.Scale.ScaleY = R.BubbleHoverScale;
        }

        public void Collapse(Button b, bool animate)
        {
            if (!_parts.TryGetValue(b, out var p) || !p.Expanded) return;
            p.Expanded = false;
            b.ZIndex = 0;
            Tween(p, animate);
            p.LabelHost.Width = 0;
            p.Plate.Margin = new Thickness(0);
            ((SolidColorBrush)p.Plate.Background!).Color = p.RestColor;
            p.Scale.ScaleX = p.Scale.ScaleY = 1.0;
        }

        /// <summary>Arms (or clears) the bubble's transitions before its values move.</summary>
        private static void Tween(Parts p, bool animate, int ms = R.BubbleExpandMs, Easing? easing = null)
        {
            if (!animate)
            {
                p.LabelHost.Transitions = null;
                p.Plate.Transitions = null;
                p.Scale.Transitions = null;
                if (p.Plate.Background is SolidColorBrush s0) s0.Transitions = null;
                return;
            }
            var d = TimeSpan.FromMilliseconds(ms);
            easing ??= new CubicEaseOut();
            p.LabelHost.Transitions = new Transitions { new DoubleTransition { Property = WidthProperty, Duration = d, Easing = easing } };
            p.Plate.Transitions = new Transitions { new ThicknessTransition { Property = MarginProperty, Duration = d, Easing = easing } };
            p.Scale.Transitions = new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = d, Easing = easing },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = d, Easing = easing },
            };
            if (p.Plate.Background is SolidColorBrush s)
                s.Transitions = new Transitions { new ColorTransition { Property = SolidColorBrush.ColorProperty, Duration = d, Easing = easing } };
        }

        // ---- idle cue: one bubble swells and settles every 9 s ----------------------------------

        private void StartPulse()
        {
            if (_pulseTimer != null || _order.Count == 0) return;
            _pulseTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = R.BubblePulseEvery };
            _pulseTimer.Tick += (_, _) => PulseNext();
            _pulseTimer.Start();
        }

        private void StopPulse()
        {
            _pulseTimer?.Stop();
            _pulseTimer = null;
        }

        internal void PulseNext()
        {
            if (!AmbientFxCanvas.Env.AllowAmbientLoops || !IsEffectivelyVisible || _order.Count == 0) return;
            if (_order.Any(b => _parts[b].Expanded)) return;
            var (pick, next) = R.NextPulse(_pulseIndex, _order.Select(b => b.IsEffectivelyVisible).ToArray());
            _pulseIndex = next;
            if (pick < 0) return;
            var p = _parts[_order[pick]];
            Tween(p, true, 220, new SineEaseInOut());
            p.Scale.ScaleX = p.Scale.ScaleY = R.BubblePulseScale;
            DispatcherTimer.RunOnce(() =>
            {
                if (p.Expanded) return;
                p.Scale.ScaleX = p.Scale.ScaleY = 1.0;
            }, TimeSpan.FromMilliseconds(220));
        }

        private static Color ToColor(uint argb) =>
            Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        private static uint ToArgb(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
    }
}
