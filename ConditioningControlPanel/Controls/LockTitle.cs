using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The lock's name as a title card: Fredoka bold in a candy gradient with a thin ice
    /// highlight along the top and a soft shadow under it, and every o and O swapped for a
    /// padlock, the ring of the shackle standing in for the ring of the letter. Consecutive
    /// padlocks lean opposite ways so a word like "Locktober" reads as a row of locks hung on
    /// a line rather than a stamp. Built as one glyph per element (text runs between the
    /// padlocks, one Image per padlock) on a horizontal panel, sized off a FormattedText measure
    /// so the padlock's body sits on the x-height and its shackle rises to the cap line. Give it
    /// a <see cref="FitWidth"/> and it scales the font down until the word fits on one line;
    /// it never wraps.
    ///
    /// <para>Juice, every bit of it gated on <see cref="MotionFx"/> with Reduced and Off getting
    /// the final state: <see cref="PlayEntry"/> drops the glyphs in one by one with the house
    /// thud and a squash on landing, the padlocks last and swinging from the shackle;
    /// <see cref="Start"/> runs the idle (each glyph on a slow wobble of its own, the padlocks
    /// breathing) and <see cref="Stop"/> parks it; <see cref="Jolt"/> tugs the padlocks when a
    /// price lands; a hover rattles them once. Nothing here ever throws into the page.</para>
    /// </summary>
    public sealed class LockTitle : Grid
    {
        public const string PadlockArt = "pack://application:,,,/Resources/features/chaster_padlock_o.png";
        private const double PadlockAspect = 152.0 / 213.0;
        /// <summary>The padlock's height as a share of the font size: the cap height, roughly.</summary>
        private const double PadlockHeightEm = 0.72;
        private const double TiltDegrees = 10;
        private const double BreathTo = 1.02;
        private const double BreathSeconds = 2.6;
        private const double WobbleDegrees = 1.2;
        private const double WobbleSeconds = 2.8;
        private const double SwayPx = 1.5;
        private const double SwaySeconds = 3.3;
        private const double ShadowDepthFrom = 2;
        private const double ShadowDepthTo = 5;
        private const double ShadowSeconds = 3.4;
        private const int DropMs = 340;
        private const int DropStaggerMs = 40;
        private const double DropFromPx = -70;
        private const int SwingMs = 1200;
        private const double SwingDegrees = 14;
        private const int JoltMs = 600;
        private const double JoltDegrees = 10;
        private const int RattleMs = 250;
        private const double RattleDegrees = 4;

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(LockTitle),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((LockTitle)d).Rebuild(entry: true)));

        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
            nameof(FontSize), typeof(double), typeof(LockTitle),
            new FrameworkPropertyMetadata(88.0, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((LockTitle)d).Rebuild(entry: false)));

        /// <summary>The width the word must fit in; NaN means no limit. The page hands it the
        /// hero's inner width so a long lock name shrinks instead of wrapping or clipping.</summary>
        public static readonly DependencyProperty FitWidthProperty = DependencyProperty.Register(
            nameof(FitWidth), typeof(double), typeof(LockTitle),
            new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((LockTitle)d).Rebuild(entry: false)));

        private static readonly FontFamily Display = new("/Fonts/#Fredoka, Segoe UI");
        private static readonly Typeface Face = new(Display, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Brush Candy = MakeCandy();
        private static readonly Brush Ice = Frozen(Color.FromRgb(0xEA, 0xF2, 0xFF));
        private static readonly Brush IceMask = MakeIceMask();
        private static BitmapImage? _padlock;

        /// <summary>One glyph and the transforms it moves on, every one with its own centre so
        /// a squash sits on the baseline, a lean on the middle and a swing on the shackle.</summary>
        private sealed record Glyph(FrameworkElement Element, ScaleTransform Scale, RotateTransform Wobble, RotateTransform? Swing,
            TranslateTransform Drop, TranslateTransform Sway, bool Padlock);

        private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly List<Glyph> _glyphs = new();
        private DropShadowEffect? _shadow;
        private bool _running;
        private DateTime _entryUntil = DateTime.MinValue;
        private double _effectiveFontSize;

        public LockTitle()
        {
            Children.Add(_row);
            Background = Brushes.Transparent; // hit-testable for the hover rattle, paints nothing
            MouseEnter += (_, _) => Rattle();
            Rebuild(entry: false);
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public double FontSize
        {
            get => (double)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public double FitWidth
        {
            get => (double)GetValue(FitWidthProperty);
            set => SetValue(FitWidthProperty, value);
        }

        /// <summary>The size the word was actually drawn at: <see cref="FontSize"/>, or less when
        /// <see cref="FitWidth"/> made it shrink.</summary>
        public double EffectiveFontSize => _effectiveFontSize;

        /// <summary>How many letters became padlocks, for the tests.</summary>
        public int PadlockCount => _glyphs.Count(g => g.Padlock);

        /// <summary>The glyph elements in reading order: TextBlocks for the runs, Images for the padlocks.</summary>
        public IEnumerable<FrameworkElement> Glyphs => _row.Children.OfType<FrameworkElement>();

        /// <summary>The padlocks in reading order, for the page's sparks.</summary>
        public IEnumerable<FrameworkElement> Padlocks => _glyphs.Where(g => g.Padlock).Select(g => g.Element);

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

        /// <summary>The word's width at a font size, before any fitting. Pure enough for the tests.</summary>
        public static double NaturalWidth(string? text, double size, double pixelsPerDip = 1.0)
        {
            double width = 0;
            foreach (var (run, padlock) in Split(text))
            {
                if (padlock) width += size * PadlockHeightEm * PadlockAspect - size * 0.04;
                else width += Measure(run, size, pixelsPerDip).WidthIncludingTrailingWhitespace;
            }
            return width;
        }

        private static FormattedText Measure(string text, double size, double pixelsPerDip) =>
            new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, Brushes.White, pixelsPerDip);

        private void Rebuild(bool entry)
        {
            // A title that was breathing keeps breathing after the lock renames it.
            var wasRunning = _running;
            try
            {
                Stop();
                _row.Children.Clear();
                _glyphs.Clear();
                var text = Text ?? "";
                var ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                var size = Math.Max(8, FontSize);
                var fit = FitWidth;
                if (!double.IsNaN(fit) && fit > 0 && text.Length > 0)
                {
                    var natural = NaturalWidth(text, size, ppd);
                    if (natural > fit) size = Math.Max(8, size * fit / natural);
                }
                _effectiveFontSize = size;
                if (text.Length == 0) { Effect = null; _shadow = null; return; }

                // One measure tells where the baseline sits, so every padlock lands on it.
                var probe = Measure("Hg", size, ppd);
                var descent = Math.Max(0, probe.Height - probe.Baseline);
                var padlockHeight = size * PadlockHeightEm;
                var padlockWidth = padlockHeight * PadlockAspect;

                var tilt = 0;
                foreach (var (run, padlock) in Split(text))
                {
                    if (!padlock)
                    {
                        var measured = Measure(run, size, ppd);
                        var w = measured.WidthIncludingTrailingWhitespace;
                        var h = measured.Height;
                        var element = Run(run, size);
                        // squash on the baseline, wobble about the middle
                        var scale = new ScaleTransform(1, 1, w / 2, h);
                        var wobble = new RotateTransform(0, w / 2, h / 2);
                        var drop = new TranslateTransform();
                        var sway = new TranslateTransform();
                        element.RenderTransform = new TransformGroup { Children = { scale, wobble, drop, sway } };
                        _row.Children.Add(element);
                        _glyphs.Add(new Glyph(element, scale, wobble, null, drop, sway, false));
                        continue;
                    }
                    var image = new Image
                    {
                        Source = Padlock(), Width = padlockWidth, Height = padlockHeight, Stretch = Stretch.Uniform,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        // the glyph's own light bleeds a little past its box: overlap the neighbours by that
                        Margin = new Thickness(-size * 0.02, 0, -size * 0.02, descent - padlockHeight * 0.06),
                    };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    // breathe and wobble about the middle, lean about the middle, swing from the shackle top
                    var breath = new ScaleTransform(1, 1, padlockWidth / 2, padlockHeight / 2);
                    var lean = new RotateTransform((tilt++ & 1) == 0 ? -TiltDegrees : TiltDegrees, padlockWidth / 2, padlockHeight / 2);
                    var wobbleLock = new RotateTransform(0, padlockWidth / 2, padlockHeight / 2);
                    var swing = new RotateTransform(0, padlockWidth / 2, padlockHeight * 0.06);
                    var fall = new TranslateTransform();
                    var drift = new TranslateTransform();
                    image.RenderTransform = new TransformGroup { Children = { breath, lean, wobbleLock, swing, fall, drift } };
                    _row.Children.Add(image);
                    _glyphs.Add(new Glyph(image, breath, wobbleLock, swing, fall, drift, true));
                }

                if (PerformanceProfile.AllowGlow(PerformanceProfile.CurrentTier))
                {
                    _shadow = new DropShadowEffect
                    {
                        Color = Colors.Black, BlurRadius = 10, ShadowDepth = 3, Opacity = 0.45, Direction = 270,
                        RenderingBias = RenderingBias.Performance,
                    };
                    Effect = _shadow;
                }
                else { Effect = null; _shadow = null; }
            }
            catch (Exception ex) { Diag.Swallowed(ex, "lock title"); }
            if (wasRunning) Start();
            if (entry && IsVisible) PlayEntry();
        }

        /// <summary>A run of letters: the candy fill, and over it the same letters in ice, masked
        /// down to their top sliver.</summary>
        private static Grid Run(string run, double size)
        {
            var body = new TextBlock
            {
                Text = run, FontFamily = Display, FontSize = size, FontWeight = FontWeights.Bold,
                Foreground = Candy, VerticalAlignment = VerticalAlignment.Bottom,
            };
            var ice = new TextBlock
            {
                Text = run, FontFamily = Display, FontSize = size, FontWeight = FontWeights.Bold,
                Foreground = Ice, Opacity = 0.55, OpacityMask = IceMask, VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
            };
            return new Grid { Children = { body, ice }, VerticalAlignment = VerticalAlignment.Bottom };
        }

        private static BitmapImage? Padlock()
        {
            if (_padlock != null) return _padlock;
            try
            {
                var image = new BitmapImage(new Uri(PadlockArt));
                image.Freeze();
                _padlock = image;
            }
            catch (Exception ex) { Diag.Swallowed(ex, "lock title padlock art"); }
            return _padlock;
        }

        private static bool Full => MotionFx.Level == MotionLevel.Full;

        // ------------------------------------------------------------------ the entry

        /// <summary>The word lands: letters first, then the padlocks, each dropping in with the
        /// house thud, squashing as it hits the line, 40 ms apart. A padlock then swings from its
        /// shackle and settles. A second call while one is in flight is ignored, so the page can
        /// call this from every door without the word jumping back up.</summary>
        public void PlayEntry()
        {
            try
            {
                if (!Full || _glyphs.Count == 0) return;
                if (DateTime.UtcNow < _entryUntil) return;
                var order = _glyphs.Where(g => !g.Padlock).Concat(_glyphs.Where(g => g.Padlock)).ToList();
                var lastStart = DropStaggerMs * (order.Count - 1);
                _entryUntil = DateTime.UtcNow.AddMilliseconds(lastStart + DropMs + SwingMs);
                var i = 0;
                foreach (var glyph in order)
                {
                    var self = glyph;
                    var phase = _glyphs.IndexOf(self);
                    var delay = TimeSpan.FromMilliseconds(DropStaggerMs * i++);
                    var drop = TimeSpan.FromMilliseconds(DropMs);

                    // invisible above the line until its turn
                    self.Element.Opacity = 0;
                    var show = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(70)) { BeginTime = delay };
                    show.Completed += (_, _) => Settle(() => { self.Element.BeginAnimation(OpacityProperty, null); self.Element.Opacity = 1; });
                    self.Element.BeginAnimation(OpacityProperty, show);

                    // the thud: cubic-bezier(.2,1.5,.4,1) is an overshoot, which is a back-ease out here
                    var fall = new DoubleAnimation(DropFromPx, 0, drop)
                    {
                        BeginTime = delay,
                        EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 },
                    };
                    fall.Completed += (_, _) => Settle(() => { self.Drop.BeginAnimation(TranslateTransform.YProperty, null); self.Drop.Y = 0; });
                    self.Drop.BeginAnimation(TranslateTransform.YProperty, fall);

                    // squash on landing, sitting on the baseline
                    var land = delay + TimeSpan.FromMilliseconds(DropMs * 0.55);
                    var squashY = Squash(land, 0.82, 1.05);
                    var squashX = Squash(land, 1.12, 0.97);
                    // a padlock that is breathing takes its breath back after the squash
                    squashY.Completed += (_, _) => Settle(() =>
                    {
                        self.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        self.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                        self.Scale.ScaleX = self.Scale.ScaleY = 1;
                        if (self.Padlock && _running) Breathe(self, phase);
                    });
                    self.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, squashY);
                    self.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, squashX);

                    if (self.Swing != null) Swing(self.Swing, land, SwingDegrees, SwingMs, 4);
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex, "lock title entry"); }
        }

        private static DoubleAnimationUsingKeyFrames Squash(TimeSpan begin, double hit, double rebound)
        {
            var frames = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = TimeSpan.FromMilliseconds(260) };
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(hit, KeyTime.FromPercent(0.3), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(rebound, KeyTime.FromPercent(0.7), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
            return frames;
        }

        private static void Settle(Action reset)
        {
            try { reset(); }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        /// <summary>A decaying swing about the shackle: full one way, then back a little less
        /// each time until it hangs still.</summary>
        private static void Swing(RotateTransform swing, TimeSpan begin, double degrees, int ms, int beats)
        {
            var frames = new DoubleAnimationUsingKeyFrames { BeginTime = begin, Duration = TimeSpan.FromMilliseconds(ms) };
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            var sign = 1.0;
            for (var b = 1; b <= beats; b++)
            {
                var amplitude = degrees * Math.Pow(0.55, b - 1);
                frames.KeyFrames.Add(new EasingDoubleKeyFrame(sign * amplitude, KeyTime.FromPercent((b - 0.5) / beats), new SineEase { EasingMode = EasingMode.EaseInOut }));
                sign = -sign;
            }
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new SineEase { EasingMode = EasingMode.EaseInOut }));
            frames.Completed += (_, _) => Settle(() => { swing.BeginAnimation(RotateTransform.AngleProperty, null); swing.Angle = 0; });
            swing.BeginAnimation(RotateTransform.AngleProperty, frames);
        }

        // ------------------------------------------------------------------ the beats

        /// <summary>A price landed on the tab: the padlocks take a tug.</summary>
        public void Jolt()
        {
            if (!Full) return;
            try
            {
                foreach (var glyph in _glyphs.Where(g => g.Swing != null))
                    Swing(glyph.Swing!, TimeSpan.Zero, JoltDegrees, JoltMs, 3);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "lock title jolt"); }
        }

        /// <summary>The pointer crossed the word: the padlocks rattle once.</summary>
        public void Rattle()
        {
            if (!Full || DateTime.UtcNow < _entryUntil) return;
            try
            {
                var i = 0;
                foreach (var glyph in _glyphs.Where(g => g.Swing != null))
                    Swing(glyph.Swing!, TimeSpan.FromMilliseconds(30 * i++), RattleDegrees, RattleMs, 3);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "lock title rattle"); }
        }

        // ------------------------------------------------------------------ the idle

        /// <summary>The idle: every glyph wobbles a degree either way and rides up and down a
        /// pixel on its own phase, the padlocks breathe two per cent, and the shadow under the
        /// whole word slides a little, as if the light moved.</summary>
        public void Start()
        {
            try
            {
                Stop();
                if (!MotionFx.AllowAmbientLoops || _glyphs.Count == 0) return;
                _running = true;
                var i = 0;
                foreach (var glyph in _glyphs)
                {
                    var wobble = new DoubleAnimation(-WobbleDegrees, WobbleDegrees, TimeSpan.FromSeconds(WobbleSeconds))
                    {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                        BeginTime = TimeSpan.FromMilliseconds(370 * i),
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    };
                    var sway = new DoubleAnimation(SwayPx, -SwayPx, TimeSpan.FromSeconds(SwaySeconds))
                    {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                        BeginTime = TimeSpan.FromMilliseconds(530 * i),
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    };
                    Timeline.SetDesiredFrameRate(wobble, 20);
                    Timeline.SetDesiredFrameRate(sway, 20);
                    glyph.Wobble.BeginAnimation(RotateTransform.AngleProperty, wobble);
                    glyph.Sway.BeginAnimation(TranslateTransform.YProperty, sway);
                    // during an entry the squash owns the scale; its landing hands the breath over
                    if (glyph.Padlock && DateTime.UtcNow >= _entryUntil) Breathe(glyph, i);
                    i++;
                }
                if (_shadow != null)
                {
                    var drift = new DoubleAnimation(ShadowDepthFrom, ShadowDepthTo, TimeSpan.FromSeconds(ShadowSeconds))
                    {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    };
                    Timeline.SetDesiredFrameRate(drift, 10);
                    _shadow.BeginAnimation(DropShadowEffect.ShadowDepthProperty, drift);
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex, "lock title start"); }
        }

        private static void Breathe(Glyph glyph, int phase)
        {
            var breath = new DoubleAnimation(1.0, BreathTo, TimeSpan.FromSeconds(BreathSeconds))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(420 * phase),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Timeline.SetDesiredFrameRate(breath, 20);
            glyph.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, breath);
            glyph.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, breath);
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            try
            {
                foreach (var glyph in _glyphs)
                {
                    glyph.Wobble.BeginAnimation(RotateTransform.AngleProperty, null);
                    glyph.Wobble.Angle = 0;
                    glyph.Sway.BeginAnimation(TranslateTransform.YProperty, null);
                    glyph.Sway.Y = 0;
                    if (glyph.Padlock)
                    {
                        glyph.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        glyph.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                        glyph.Scale.ScaleX = glyph.Scale.ScaleY = 1;
                    }
                }
                if (_shadow != null)
                {
                    _shadow.BeginAnimation(DropShadowEffect.ShadowDepthProperty, null);
                    _shadow.ShadowDepth = 3;
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex, "lock title stop"); }
        }

        private static Brush MakeCandy()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
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
            brush.Freeze();
            return brush;
        }

        /// <summary>Opaque over the top fourteen per cent of the letter, gone just under it.</summary>
        private static Brush MakeIceMask()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop(Colors.White, 0.0),
                    new GradientStop(Colors.White, 0.14),
                    new GradientStop(Colors.Transparent, 0.17),
                },
            };
            brush.Freeze();
            return brush;
        }

        private static Brush Frozen(Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }
    }
}
