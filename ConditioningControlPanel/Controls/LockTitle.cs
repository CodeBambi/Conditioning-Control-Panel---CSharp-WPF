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
    /// so the padlock's body sits on the x-height and its shackle rises to the cap line.
    ///
    /// <para>Motion: <see cref="Start"/> under ambient loops makes the padlocks breathe two
    /// per cent and the shadow drift; <see cref="Stop"/> parks them. Nothing here ever throws
    /// into the page.</para>
    /// </summary>
    public sealed class LockTitle : Grid
    {
        public const string PadlockArt = "pack://application:,,,/Resources/features/chaster_padlock_o.png";
        private const double PadlockAspect = 152.0 / 213.0;
        /// <summary>The padlock's height as a share of the font size: the cap height, roughly.</summary>
        private const double PadlockHeightEm = 0.78;
        private const double TiltDegrees = 10;
        private const double BreathTo = 1.02;
        private const double BreathSeconds = 2.6;
        private const double ShadowDepthFrom = 2;
        private const double ShadowDepthTo = 5;
        private const double ShadowSeconds = 3.4;

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(LockTitle),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((LockTitle)d).Rebuild()));

        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
            nameof(FontSize), typeof(double), typeof(LockTitle),
            new FrameworkPropertyMetadata(64.0, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((LockTitle)d).Rebuild()));

        private static readonly FontFamily Display = new("/Fonts/#Fredoka, Segoe UI");
        private static readonly Typeface Face = new(Display, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Brush Candy = MakeCandy();
        private static readonly Brush Ice = Frozen(Color.FromRgb(0xEA, 0xF2, 0xFF));
        private static readonly Brush IceMask = MakeIceMask();
        private static BitmapImage? _padlock;

        private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly List<Image> _padlocks = new();
        private DropShadowEffect? _shadow;
        private bool _running;

        public LockTitle()
        {
            Children.Add(_row);
            IsHitTestVisible = false;
            Rebuild();
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

        /// <summary>How many letters became padlocks, for the tests.</summary>
        public int PadlockCount => _padlocks.Count;

        /// <summary>The glyph elements in reading order: TextBlocks for the runs, Images for the padlocks.</summary>
        public IEnumerable<FrameworkElement> Glyphs => _row.Children.OfType<FrameworkElement>();

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

        private void Rebuild()
        {
            // A title that was breathing keeps breathing after the lock renames it.
            var wasRunning = _running;
            try
            {
                Stop();
                _row.Children.Clear();
                _padlocks.Clear();
                var size = Math.Max(8, FontSize);
                var text = Text ?? "";
                if (text.Length == 0) { Effect = null; _shadow = null; return; }

                // One measure tells where the baseline sits, so every padlock lands on it.
                var probe = new FormattedText("Hg", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                var descent = Math.Max(0, probe.Height - probe.Baseline);
                var padlockHeight = size * PadlockHeightEm;
                var padlockWidth = padlockHeight * PadlockAspect;

                var tilt = 0;
                foreach (var (run, padlock) in Split(text))
                {
                    if (!padlock)
                    {
                        _row.Children.Add(Run(run, size));
                        continue;
                    }
                    var image = new Image
                    {
                        Source = Padlock(), Width = padlockWidth, Height = padlockHeight, Stretch = Stretch.Uniform,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        // the glyph's own light bleeds a little past its box: overlap the neighbours by that
                        Margin = new Thickness(-size * 0.02, 0, -size * 0.02, descent - padlockHeight * 0.06),
                        RenderTransformOrigin = new Point(0.5, 0.5),
                        RenderTransform = new TransformGroup
                        {
                            Children = { new ScaleTransform(1, 1), new RotateTransform((tilt++ & 1) == 0 ? -TiltDegrees : TiltDegrees) },
                        },
                    };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                    _row.Children.Add(image);
                    _padlocks.Add(image);
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

        /// <summary>The idle: each padlock breathes two per cent on its own phase, and the shadow
        /// under the whole word slides a little, as if the light moved.</summary>
        public void Start()
        {
            try
            {
                Stop();
                if (!MotionFx.AllowAmbientLoops || _padlocks.Count == 0) return;
                _running = true;
                var i = 0;
                foreach (var padlock in _padlocks)
                {
                    if (padlock.RenderTransform is not TransformGroup g || g.Children.OfType<ScaleTransform>().FirstOrDefault() is not { } scale) continue;
                    var breath = new DoubleAnimation(1.0, BreathTo, TimeSpan.FromSeconds(BreathSeconds))
                    {
                        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                        BeginTime = TimeSpan.FromMilliseconds(420 * i++),
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                    };
                    Timeline.SetDesiredFrameRate(breath, 20);
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, breath);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, breath);
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

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            try
            {
                foreach (var padlock in _padlocks)
                {
                    if (padlock.RenderTransform is not TransformGroup g || g.Children.OfType<ScaleTransform>().FirstOrDefault() is not { } scale) continue;
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleX = scale.ScaleY = 1;
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
