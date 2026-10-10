// PORTED from ConditioningControlPanel/Controls/LockTitle.cs.
// The lock's name as a title card: Fredoka bold in a candy gradient with a thin ice highlight along
// the top, and every o and O swapped for a padlock, the ring of the shackle standing in for the ring
// of the letter. Consecutive padlocks lean opposite ways. One element per run (text between the
// padlocks, one Image per padlock) on a horizontal panel, sized off a FormattedText measure so the
// padlock's body sits on the baseline. Give it a FitWidth and it scales the font down until the word
// fits on one line; it never wraps.
// Juice, at motion Full only: Jolt tugs the padlocks when a price lands and a hover rattles them
// once (a damped swing from the shackle that ends at rest: its own IN and OUT).
// not ported: PlayEntry (the drop-in), the idle wobble / sway / breath loops (Start / Stop) and the
// drifting drop shadow (no Effect and no endless loop on this page, the port's FX rule).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Controls
{
    public sealed class LockTitle : Panel
    {
        public const string PadlockArt = "features/chaster_padlock_o.png";
        private const double PadlockAspect = 152.0 / 213.0;
        /// <summary>The padlock's height as a share of the font size: the cap height, roughly.</summary>
        private const double PadlockHeightEm = 0.72;
        internal const double TiltDegrees = 10;
        private const int JoltMs = 600;
        private const double JoltDegrees = 10;
        private const int RattleMs = 250;
        private const double RattleDegrees = 4;

        public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<LockTitle, string>(nameof(Text), "");
        public static readonly StyledProperty<double> FontSizeProperty = AvaloniaProperty.Register<LockTitle, double>(nameof(FontSize), 88.0);
        /// <summary>The width the word must fit in; NaN means no limit.</summary>
        public static readonly StyledProperty<double> FitWidthProperty = AvaloniaProperty.Register<LockTitle, double>(nameof(FitWidth), double.NaN);

        private static readonly FontFamily Display = new("Fredoka, Segoe UI");
        private static readonly Typeface Face = new(Display, FontStyle.Normal, FontWeight.Bold);
        private static readonly IBrush Candy = MakeCandy();
        private static readonly IBrush Ice = new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFF));
        private static readonly IBrush IceMask = MakeIceMask();
        private static Bitmap? _padlock;
        private static bool _padlockTried;

        private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly List<(Control Element, RotateTransform Lean, RotateTransform Swing)> _padlocks = new();
        private double _effectiveFontSize;

        public LockTitle()
        {
            Children.Add(_row);
            Background = Brushes.Transparent; // hit-testable for the hover rattle, paints nothing
            PointerEntered += (_, _) => Rattle();
            Rebuild();
        }

        public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
        public double FitWidth { get => GetValue(FitWidthProperty); set => SetValue(FitWidthProperty, value); }

        /// <summary>The size the word was actually drawn at: FontSize, or less when FitWidth made it shrink.</summary>
        public double EffectiveFontSize => _effectiveFontSize;

        /// <summary>How many letters became padlocks, for the tests.</summary>
        public int PadlockCount => _padlocks.Count;

        /// <summary>The glyph elements in reading order: panels for the runs, Images for the padlocks.</summary>
        public IEnumerable<Control> Glyphs => _row.Children;

        /// <summary>The lean of each padlock in reading order, for the tests.</summary>
        public IEnumerable<double> Leans => _padlocks.Select(p => p.Lean.Angle);

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == TextProperty || change.Property == FontSizeProperty || change.Property == FitWidthProperty) Rebuild();
        }

        /// <summary>The runs of a title: letters between padlocks, and the padlocks. Pure, for the tests.</summary>
        public static IReadOnlyList<(string Run, bool Padlock)> Split(string? text)
        {
            var parts = new List<(string, bool)>();
            if (string.IsNullOrEmpty(text)) return parts;
            var run = "";
            foreach (var ch in text)
            {
                if (ch == 'o' || ch == 'O')
                {
                    if (run.Length > 0) { parts.Add((run, false)); run = ""; }
                    parts.Add((ch.ToString(), true));
                }
                else run += ch;
            }
            if (run.Length > 0) parts.Add((run, false));
            return parts;
        }

        /// <summary>The word's width at a font size, before any fitting.</summary>
        public static double NaturalWidth(string? text, double size)
        {
            double width = 0;
            foreach (var (run, padlock) in Split(text))
            {
                if (padlock) width += size * PadlockHeightEm * PadlockAspect - size * 0.04;
                else width += Measure(run, size).WidthIncludingTrailingWhitespace;
            }
            return width;
        }

        private static FormattedText Measure(string text, double size) =>
            new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, Brushes.White);

        private void Rebuild()
        {
            try
            {
                _row.Children.Clear();
                _padlocks.Clear();
                var text = Text ?? "";
                var size = Math.Max(8, FontSize);
                var fit = FitWidth;
                if (!double.IsNaN(fit) && fit > 0 && text.Length > 0)
                {
                    var natural = NaturalWidth(text, size);
                    if (natural > fit) size = Math.Max(8, size * fit / natural);
                }
                _effectiveFontSize = size;
                if (text.Length == 0) return;

                // One measure tells where the baseline sits, so every padlock lands on it.
                var probe = Measure("Hg", size);
                var descent = Math.Max(0, probe.Height - probe.Baseline);
                var padlockHeight = size * PadlockHeightEm;
                var padlockWidth = padlockHeight * PadlockAspect;

                var tilt = 0;
                foreach (var (run, padlock) in Split(text))
                {
                    if (!padlock) { _row.Children.Add(Run(run, size)); continue; }
                    // lean about the middle, swing from the shackle top (centres in the image's own pixels)
                    var lean = new RotateTransform((tilt++ & 1) == 0 ? -TiltDegrees : TiltDegrees, padlockWidth / 2, padlockHeight / 2);
                    var swing = new RotateTransform(0, padlockWidth / 2, padlockHeight * 0.06);
                    var image = new Image
                    {
                        Source = Padlock(), Width = padlockWidth, Height = padlockHeight, Stretch = Stretch.Uniform,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        // the glyph's own light bleeds a little past its box: overlap the neighbours by that
                        Margin = new Thickness(-size * 0.02, 0, -size * 0.02, descent - padlockHeight * 0.06),
                        RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute),
                        RenderTransform = new TransformGroup { Children = { lean, swing } },
                    };
                    RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
                    _row.Children.Add(image);
                    _padlocks.Add((image, lean, swing));
                }
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] lock title"); }
        }

        /// <summary>A run of letters: the candy fill, and over it the same letters in ice, masked down to their top sliver.</summary>
        private static Panel Run(string run, double size)
        {
            var body = new TextBlock
            {
                Text = run, FontFamily = Display, FontSize = size, FontWeight = FontWeight.Bold,
                Foreground = Candy, VerticalAlignment = VerticalAlignment.Bottom,
            };
            var ice = new TextBlock
            {
                Text = run, FontFamily = Display, FontSize = size, FontWeight = FontWeight.Bold,
                Foreground = Ice, Opacity = 0.55, OpacityMask = IceMask, VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
            };
            return new Panel { Children = { body, ice }, VerticalAlignment = VerticalAlignment.Bottom };
        }

        private static Bitmap? Padlock()
        {
            if (_padlockTried) return _padlock;
            _padlockTried = true;
            _padlock = Helpers.ModArt.TryLoad(PadlockArt, 304);
            return _padlock;
        }

        private static bool Full => AmbientFxCanvas.Env.Level == Models.MotionLevel.Full;

        /// <summary>A price landed on the tab: the padlocks take a tug.</summary>
        public void Jolt()
        {
            if (!Full) return;
            try { foreach (var p in _padlocks) Swing(p.Swing, JoltDegrees, JoltMs, 3); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] lock title jolt"); }
        }

        /// <summary>The pointer crossed the word: the padlocks rattle once, 30 ms apart.</summary>
        public void Rattle()
        {
            if (!Full) return;
            try
            {
                var i = 0;
                foreach (var p in _padlocks)
                {
                    var swing = p.Swing;
                    if (i == 0) Swing(swing, RattleDegrees, RattleMs, 3);
                    else DispatcherTimer.RunOnce(() => Swing(swing, RattleDegrees, RattleMs, 3), TimeSpan.FromMilliseconds(30 * i));
                    i++;
                }
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] lock title rattle"); }
        }

        /// <summary>WPF Swing: out one way, back the other at 55 per cent, and so on, ending at rest.</summary>
        private static void Swing(RotateTransform swing, double degrees, int ms, int beats)
        {
            var keys = new List<(double, AvaloniaProperty, double)> { (0, RotateTransform.AngleProperty, 0) };
            var sign = 1.0;
            for (var b = 1; b <= beats; b++)
            {
                keys.Add(((b - 0.5) / beats, RotateTransform.AngleProperty, sign * degrees * Math.Pow(0.55, b - 1)));
                sign = -sign;
            }
            keys.Add((1, RotateTransform.AngleProperty, 0));
            Helpers.TransformTween.Run(swing, TimeSpan.FromMilliseconds(ms), keys);
        }

        private static IBrush MakeCandy() => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xEF, 0xE0, 0xF3), 0.00),
                new GradientStop(Color.FromRgb(0xEE, 0xBC, 0xD9), 0.10),
                new GradientStop(Color.FromRgb(0xF7, 0x8F, 0xB4), 0.22),
                new GradientStop(Color.FromRgb(0xFF, 0x68, 0x91), 0.38),
                new GradientStop(Color.FromRgb(0xFC, 0x64, 0x93), 0.50),
                new GradientStop(Color.FromRgb(0xE3, 0x86, 0xCC), 0.62),
                new GradientStop(Color.FromRgb(0xD3, 0x9F, 0xF4), 0.78),
                new GradientStop(Color.FromRgb(0xCE, 0xA8, 0xFF), 0.92),
                new GradientStop(Color.FromRgb(0xC3, 0xA0, 0xF0), 1.00),
            },
        };

        /// <summary>Opaque over the top fourteen per cent of the letter, gone just under it.</summary>
        private static IBrush MakeIceMask() => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors.White, 0.0),
                new GradientStop(Colors.White, 0.14),
                new GradientStop(Colors.Transparent, 0.17),
            },
        };
    }
}
