// PORTED from ConditioningControlPanel/Controls/LockTitle.cs.
// The lock's name as a title card: Fredoka bold in a candy gradient with a thin ice highlight along
// the top, and every o and O swapped for a padlock, the ring of the shackle standing in for the ring
// of the letter. Consecutive padlocks lean opposite ways. One element per run (text between the
// padlocks, one Image per padlock) on a horizontal panel, sized off a FormattedText measure so the
// padlock's body sits on the baseline. Give it a FitWidth and it scales the font down until the word
// fits on one line; it never wraps.
// Juice (WPF LockTitle.cs, every number from there):
//   PlayEntry - the glyphs drop in one by one, letters first then the padlocks, 40 ms apart: 70 px
//     down in 340 ms on the house thud (BackEase out 0.55), a squash on landing (260 ms), and each
//     padlock then swings from its shackle (14 degrees, 1.2 s, four decaying beats). Full only.
//   Start / Stop - the idle: every glyph wobbles 1.2 degrees (2.8 s) and rides 1.5 px (3.3 s) on its
//     own phase, the padlocks breathe 2 % (2.6 s). One VisibleBeat on the window's 30 fps beat; it
//     parks at rest when the title hides or the motion level leaves Full, and re-arms by itself.
//   Jolt tugs the padlocks when a price lands; a hover rattles them once. Each ends at rest.
// The entry is one FxTrack run (a function of its age), the idle one function of the beat's time:
// no Animation, nothing endless off the beat, no Effect.
// not ported: the drop shadow under the word and its 2-5 px drift (WPF DropShadowEffect on the whole
// title: an Effect over a subtree that moves every beat is the port's FX trap). Owed: a cached
// blurred silhouette under the row, as TierBadge does.
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
        internal const double BreathTo = 1.02, BreathSeconds = 2.6;
        internal const double WobbleDegrees = 1.2, WobbleSeconds = 2.8;
        internal const double SwayPx = 1.5, SwaySeconds = 3.3;
        internal const int DropMs = 340, DropStaggerMs = 40, SquashMs = 260;
        internal const double DropFromPx = -70;
        internal const int SwingMs = 1200;
        internal const double SwingDegrees = 14;
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
        /// <summary>One glyph and the transforms it moves on: a squash that sits on the baseline (runs) or
        /// a breath about the middle (padlocks), a wobble about the middle, a swing from the shackle.</summary>
        private sealed class Glyph
        {
            public Control Element = null!;
            public ScaleTransform Scale = null!;
            public RotateTransform Wobble = null!;
            public RotateTransform? Lean, Swing;
            public TranslateTransform Drop = null!, Sway = null!;
            public bool Padlock;
        }

        private readonly List<Glyph> _glyphs = new();
        private readonly Helpers.VisibleBeat _idle;
        private Helpers.FxTrack.Run? _entry;
        private bool _running;
        private double _idleT, _breathFrom;
        private double _effectiveFontSize;

        public LockTitle()
        {
            Children.Add(_row);
            Background = Brushes.Transparent; // hit-testable for the hover rattle, paints nothing
            PointerEntered += (_, _) => Rattle();
            _idle = Helpers.VisibleBeat.Attach(this, StepIdle, RestIdle, when: () => _running);
            Rebuild(entry: false);
        }

        public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
        public double FitWidth { get => GetValue(FitWidthProperty); set => SetValue(FitWidthProperty, value); }

        /// <summary>The size the word was actually drawn at: FontSize, or less when FitWidth made it shrink.</summary>
        public double EffectiveFontSize => _effectiveFontSize;

        /// <summary>How many letters became padlocks, for the tests.</summary>
        public int PadlockCount => _glyphs.Count(g => g.Padlock);

        /// <summary>The glyph elements in reading order: panels for the runs, Images for the padlocks.</summary>
        public IEnumerable<Control> Glyphs => _row.Children;

        /// <summary>The lean of each padlock in reading order, for the tests.</summary>
        public IEnumerable<double> Leans => _glyphs.Where(g => g.Padlock).Select(g => g.Lean!.Angle);

        /// <summary>The padlocks in reading order, for the page's sparks.</summary>
        public IEnumerable<Control> Padlocks => _glyphs.Where(g => g.Padlock).Select(g => g.Element);

        /// <summary>True while the idle loop is ticking (shown, wanted, motion Full).</summary>
        internal bool IsIdling => _idle.IsRunning;

        /// <summary>True while the drop-in is in flight.</summary>
        internal bool IsEntering => _entry is { IsDone: false };
        internal Helpers.FxTrack.Run? EntryRun => _entry;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            // WPF: a new name lands with the entry; a resize or a refit only redraws.
            if (change.Property == TextProperty) Rebuild(entry: true);
            else if (change.Property == FontSizeProperty || change.Property == FitWidthProperty) Rebuild(entry: false);
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

        private void Rebuild(bool entry)
        {
            try
            {
                // a rebuild mid-entry lands the old glyphs first; the idle keeps its say (_running)
                _entry?.Finish();
                _entry = null;
                _row.Children.Clear();
                _glyphs.Clear();
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
                var origin = new RelativePoint(0, 0, RelativeUnit.Absolute);   // every centre below is in the glyph's own pixels

                var tilt = 0;
                foreach (var (run, padlock) in Split(text))
                {
                    if (!padlock)
                    {
                        var measured = Measure(run, size);
                        double w = measured.WidthIncludingTrailingWhitespace, h = measured.Height;
                        var element = Run(run, size);
                        // squash on the baseline, wobble about the middle
                        var g = new Glyph
                        {
                            Element = element, Scale = new ScaleTransform(1, 1), Wobble = new RotateTransform(0, w / 2, h / 2),
                            Drop = new TranslateTransform(), Sway = new TranslateTransform(),
                        };
                        element.RenderTransformOrigin = origin;
                        element.RenderTransform = new TransformGroup
                        {
                            Children = { new TranslateTransform(-w / 2, -h), g.Scale, new TranslateTransform(w / 2, h), g.Wobble, g.Drop, g.Sway },
                        };
                        _row.Children.Add(element);
                        _glyphs.Add(g);
                        continue;
                    }
                    // breathe, lean and wobble about the middle, swing from the shackle top
                    var lockGlyph = new Glyph
                    {
                        Padlock = true,
                        Scale = new ScaleTransform(1, 1),
                        Lean = new RotateTransform((tilt++ & 1) == 0 ? -TiltDegrees : TiltDegrees, padlockWidth / 2, padlockHeight / 2),
                        Wobble = new RotateTransform(0, padlockWidth / 2, padlockHeight / 2),
                        Swing = new RotateTransform(0, padlockWidth / 2, padlockHeight * 0.06),
                        Drop = new TranslateTransform(), Sway = new TranslateTransform(),
                    };
                    var image = new Image
                    {
                        Source = Padlock(), Width = padlockWidth, Height = padlockHeight, Stretch = Stretch.Uniform,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        // the glyph's own light bleeds a little past its box: overlap the neighbours by that
                        Margin = new Thickness(-size * 0.02, 0, -size * 0.02, descent - padlockHeight * 0.06),
                        RenderTransformOrigin = origin,
                        RenderTransform = new TransformGroup
                        {
                            Children =
                            {
                                new TranslateTransform(-padlockWidth / 2, -padlockHeight / 2), lockGlyph.Scale,
                                new TranslateTransform(padlockWidth / 2, padlockHeight / 2),
                                lockGlyph.Lean, lockGlyph.Wobble, lockGlyph.Swing, lockGlyph.Drop, lockGlyph.Sway,
                            },
                        },
                    };
                    RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
                    lockGlyph.Element = image;
                    _row.Children.Add(image);
                    _glyphs.Add(lockGlyph);
                }
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] lock title"); }
            _idle.Refresh();
            if (entry && IsEffectivelyVisible) PlayEntry();
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

        // ------------------------------------------------------------------ the entry

        /// <summary>Where one glyph is, <paramref name="ms"/> into the entry. <paramref name="order"/> is its
        /// place in the drop order (letters first, then the padlocks). Pure: WPF PlayEntry's storyboard.</summary>
        internal static (double Opacity, double DropY, double ScaleX, double ScaleY, double Swing) EntryPose(double ms, int order, bool padlock)
        {
            double delay = DropStaggerMs * order;
            double opacity = Helpers.FxTrack.Clamp01((ms - delay) / 70);
            // the thud: cubic-bezier(.2,1.5,.4,1) is an overshoot, a back-ease out here
            double dropY = DropFromPx * (1 - Helpers.FxTrack.BackOut(Helpers.FxTrack.Clamp01((ms - delay) / DropMs), 0.55));
            // squash on landing
            double land = delay + (DropMs * 0.55), u = (ms - land) / SquashMs;
            double sy = Squash(u, 0.82, 1.05), sx = Squash(u, 1.12, 0.97);
            double swing = padlock ? SwingAngle((ms - land) / SwingMs, SwingDegrees, 4) : 0;
            return (opacity, dropY, sx, sy, swing);
        }

        private static double Squash(double u, double hit, double rebound) =>
            Helpers.FxTrack.Keys(Helpers.FxTrack.Clamp01(u), (0, 1, null), (0.3, hit, Helpers.FxTrack.QuadOut),
                (0.7, rebound, Helpers.FxTrack.QuadInOut), (1, 1, Helpers.FxTrack.QuadInOut));

        /// <summary>WPF Swing: full one way, back a little less each time (x0.55), ending at rest. u is 0..1.</summary>
        internal static double SwingAngle(double u, double degrees, int beats)
        {
            if (u <= 0 || u >= 1) return 0;
            double from = 0, fromAt = 0, sign = 1;
            for (var b = 1; b <= beats; b++)
            {
                double at = (b - 0.5) / beats, to = sign * degrees * Math.Pow(0.55, b - 1);
                if (u <= at) return from + ((to - from) * Helpers.FxTrack.SineInOut((u - fromAt) / (at - fromAt)));
                (from, fromAt, sign) = (to, at, -sign);
            }
            return from * (1 - Helpers.FxTrack.SineInOut((u - fromAt) / (1 - fromAt)));
        }

        /// <summary>How long the entry of a title with this many glyphs runs.</summary>
        internal static double EntryMs(int glyphs) => (DropStaggerMs * Math.Max(0, glyphs - 1)) + DropMs + SwingMs;

        /// <summary>The word lands: letters first, then the padlocks, each dropping in with the house thud,
        /// squashing as it hits the line, 40 ms apart. A padlock then swings from its shackle and settles.
        /// A second call while one is in flight is ignored, so the page can call this from every door.</summary>
        public void PlayEntry()
        {
            try
            {
                if (!Full || _glyphs.Count == 0 || IsEntering) return;
                var order = _glyphs.Where(g => !g.Padlock).Concat(_glyphs.Where(g => g.Padlock)).ToList();
                _entry = Helpers.FxTrack.Play(EntryMs(order.Count), ms =>
                {
                    for (int i = 0; i < order.Count; i++)
                    {
                        var g = order[i];
                        var pose = EntryPose(ms, i, g.Padlock);
                        g.Element.Opacity = pose.Opacity;
                        g.Drop.Y = pose.DropY;
                        g.Scale.ScaleX = pose.ScaleX;
                        g.Scale.ScaleY = pose.ScaleY;
                        if (g.Swing != null) g.Swing.Angle = pose.Swing;
                    }
                }, () =>
                {
                    // the OUT: every glyph at rest on the line; a breathing padlock takes its breath back from 1
                    foreach (var g in order)
                    {
                        g.Element.Opacity = 1;
                        g.Drop.Y = 0;
                        g.Scale.ScaleX = g.Scale.ScaleY = 1;
                        if (g.Swing != null) g.Swing.Angle = 0;
                    }
                    _breathFrom = _idleT;
                });
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] lock title entry"); }
        }

        // ------------------------------------------------------------------ the beats

        /// <summary>A price landed on the tab: the padlocks take a tug.</summary>
        public void Jolt()
        {
            if (!Full) return;
            try { foreach (var g in _glyphs.Where(g => g.Swing != null)) Swing(g.Swing!, 0, JoltDegrees, JoltMs, 3); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] lock title jolt"); }
        }

        /// <summary>The pointer crossed the word: the padlocks rattle once, 30 ms apart. Not during the entry.</summary>
        public void Rattle()
        {
            if (!Full || IsEntering) return;
            try
            {
                var i = 0;
                foreach (var g in _glyphs.Where(g => g.Swing != null)) Swing(g.Swing!, 30 * i++, RattleDegrees, RattleMs, 3);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] lock title rattle"); }
        }

        private void Swing(RotateTransform swing, double delayMs, double degrees, int ms, int beats)
        {
            Helpers.FxTrack.Play(delayMs + ms, at =>
            {
                if (!IsEntering) swing.Angle = SwingAngle((at - delayMs) / ms, degrees, beats);
            }, () => { if (!IsEntering) swing.Angle = 0; });
        }

        // ------------------------------------------------------------------ the idle

        /// <summary>Where glyph <paramref name="index"/> is, <paramref name="t"/> seconds into the idle. Pure:
        /// WPF Start's three loops, each on its own BeginTime (370 / 530 / 420 ms a glyph), sine in and out.</summary>
        internal static (double Wobble, double Sway, double Breath) IdlePose(double t, int index, bool padlock, double breathFrom = 0)
        {
            double w = t - (0.37 * index), s = t - (0.53 * index), b = t - breathFrom - (0.42 * index);
            return (w < 0 ? 0 : -WobbleDegrees + (2 * WobbleDegrees * Helpers.BeatLoop.Breath(w, WobbleSeconds)),
                    s < 0 ? 0 : SwayPx - (2 * SwayPx * Helpers.BeatLoop.Breath(s, SwaySeconds)),
                    !padlock || b < 0 ? 1 : 1 + ((BreathTo - 1) * Helpers.BeatLoop.Breath(b, BreathSeconds)));
        }

        /// <summary>The idle: every glyph wobbles a degree either way and rides up and down a pixel on its own
        /// phase, and the padlocks breathe two per cent. It runs only while the title shows at motion Full.</summary>
        public void Start()
        {
            _running = true;
            _breathFrom = 0;
            _idle.Refresh();
        }

        /// <summary>Parks the idle at rest (the page hid).</summary>
        public void Stop()
        {
            _running = false;
            _idle.Refresh();
        }

        private void StepIdle(double t)
        {
            _idleT = t;
            bool entering = IsEntering;
            for (int i = 0; i < _glyphs.Count; i++)
            {
                var g = _glyphs[i];
                var pose = IdlePose(t, i, g.Padlock, _breathFrom);
                g.Wobble.Angle = pose.Wobble;
                g.Sway.Y = pose.Sway;
                // during an entry the squash owns the scale; its landing hands the breath over
                if (g.Padlock && !entering) g.Scale.ScaleX = g.Scale.ScaleY = pose.Breath;
            }
        }

        private void RestIdle()
        {
            _idleT = _breathFrom = 0;
            bool entering = IsEntering;
            foreach (var g in _glyphs)
            {
                g.Wobble.Angle = 0;
                g.Sway.Y = 0;
                if (g.Padlock && !entering) g.Scale.ScaleX = g.Scale.ScaleY = 1;
            }
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
