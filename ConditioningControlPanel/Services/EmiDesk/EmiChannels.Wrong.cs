using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Services.EmiDesk;

public static partial class EmiChannels
{
    /// <summary>
    /// THE WRONG CHANNEL. A port of the campus intrusion (<c>Resources/web/arcademy/emi/channels.js</c>,
    /// CH6) onto the desk glass: for a beat, the tube shows a feed that is not hers.
    ///
    /// <para>It is not a channel in the way the others are. Pong and spiral and video are things she
    /// puts on; this one arrives. On the campus it rides another channel's EXIT rather than sitting
    /// on the wheel, because the moment a signal ghost is most believable is the moment the real
    /// picture goes away. The desk keeps that shape: the ORCHESTRATOR lands this painter off its
    /// exit hook, so the once-per-session lock and the 1-in-40 roll are not this file's business.
    /// All this file owes is the look, and an arc short enough to be over before anyone decides
    /// what they saw.</para>
    ///
    /// <para>NEVER ACKNOWLEDGED. <see cref="EmiChannelPainter.Payload"/> stays null (the base's), so
    /// a tap fires nothing, and there is no line, no blip and no caught beat anywhere in here. The
    /// campus comment is the whole design note: it is never explained.</para>
    ///
    /// <para>Deliberately NOT wearing the scanline mask the other channels wear. That mask is what
    /// makes the deck read as one appliance, and this is not that appliance.</para>
    /// </summary>
    internal sealed class WrongPainter : EmiChannelPainter
    {
        /// <summary>Which of the campus's two intrusions this landing is. Chosen once, in the ctor.</summary>
        private enum Wrongness
        {
            /// <summary>The glass floods pink and her face is punched out of it in dark, twice.</summary>
            Negative,

            /// <summary>Snow, with one word in it that is almost too faint to have been there.</summary>
            Word
        }

        // The campus quotes this channel against its OWN glass, 152 x 137 virtual px (GLASS_W and
        // GLASS_H in channels.js), not against the 60 px reference pong's dials come from. Same
        // scaling law as pong, different divisor, so read every number below as campus px.
        private const double RefW = 152.0;

        // ---- the arc, in ms of Tick time -------------------------------------------------------
        // The orchestrator tears this down after roughly 1200 to 1500 ms, and a teardown that is a
        // frame late must never catch a live frame. So the whole thing is finished by 1150 and
        // everything after that is a held black screen: worst case the user gets a few extra ms of
        // dead tube, which is the correct ending anyway.
        private const double FlashMs = 90;      // the break-in: one white hit, no warning
        private const double CoreEndMs = 950;   // the wrongness itself
        private const double DecayEndMs = 1150; // the collapse, and then dead

        private const double ScrambleMs = 66;   // ~15 Hz. Snow, not a strobe, and a third of the churn
        private const double RollMs = 700;      // one pass of the vertical-hold bar

        // Campus densities times its 240 run budget: static_(0.55) under the word, static_r(0.12)
        // over it.
        private const int BaseGrains = 132;
        private const int TopGrains = 29;

        // Snow ink, verbatim from the campus helpers. The base pass is a dull grey pair so it reads
        // as a dead carrier; the pass ON TOP of the word is half-alpha white and black, which is
        // what buries the word without hiding it.
        private static readonly SolidColorBrush GrainLight =
            Freeze(new SolidColorBrush(Color.FromRgb(0xC9, 0xC9, 0xD6)));
        private static readonly SolidColorBrush GrainDark =
            Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x58)));
        private static readonly SolidColorBrush TopLight =
            Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)));
        private static readonly SolidColorBrush TopDark =
            Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00)));

        /// <summary>Campus <c>#e8e8f2</c>: the word is not cream and not pink, which is the point.</summary>
        private static readonly SolidColorBrush WordInk =
            Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xF2)));

        /// <summary>
        /// The vertical-hold bar: a soft white band, transparent at both edges, that walks down the
        /// glass. It is the one thing in here that says TUBE rather than says GLITCH, and it is what
        /// keeps the snow from reading as a decoration.
        /// </summary>
        private static readonly LinearGradientBrush RollBrush = Freeze(new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.0),
                new GradientStop(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF), 0.5),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0)
            },
            90.0));

        /// <summary>
        /// The campus word list (<c>WRONG_WORDS</c>), lowercase as written. Single innocent words,
        /// never explained: the campus-side QA note is that the eeriness is the timing and not the
        /// vocabulary, so do not make these cleverer.
        /// </summary>
        private static readonly string[] Words = { "soon", "hi", "again" };

        // The face the negative is cut out of. The desk has no handle on what the locked face
        // renderer is showing this instant, so this takes the campus's own fallback (its plan()
        // ends with the literal 0_0) as a constant: a flat wide-eyed stare, the one that reads worst.
        private const string NegativeFace = "0_0";

        private readonly double _w, _h, _k;
        private readonly Random _rng = new();
        private readonly Wrongness _variant;

        private readonly Rectangle _flash = new();   // the break-in hit
        private readonly Rectangle _flood = new();   // negative: the pink ground
        private readonly Rectangle _roll = new();    // the vertical-hold band
        private readonly Rectangle _dead = new();    // black, over everything, the safe final frame
        private readonly Rectangle _tear = new();    // the line the picture collapses into and dies
        private readonly TextBlock _ghost = new();   // negative: the smaller one standing behind her
        private readonly TextBlock _face = new();    // negative: her own face, punched dark
        private readonly TextBlock _word = new();    // word: the thing that should not be legible

        private readonly List<Rectangle> _snow = new();
        private readonly List<Rectangle> _snowTop = new();

        private double _faceX, _faceY;   // the face's resting corner; the twitch offsets from here
        private double _rollH;
        private double _lastScramble = double.NegativeInfinity;
        private bool _ended;

        // Two dropouts: the carrier goes entirely for a handful of frames. Seeded once so they land
        // on different beats in different sessions, and both sit inside the core, never on the
        // decay, where they would only look like the collapse stuttering.
        private readonly double _dropAt0, _dropAt1, _dropFor0, _dropFor1;

        public WrongPainter(double w, double h)
        {
            _w = w; _h = h;
            _k = Math.Max(0.5, w / RefW);

            _variant = _rng.NextDouble() > 0.5 ? Wrongness.Negative : Wrongness.Word;

            _dropAt0 = 240 + _rng.NextDouble() * 180;
            _dropAt1 = 620 + _rng.NextDouble() * 220;
            _dropFor0 = 50 + _rng.NextDouble() * 30;
            _dropFor1 = 50 + _rng.NextDouble() * 30;
        }

        public override string Id => "wrong";

        public override void Attach(Panel host)
        {
            // The campus clears its snow to #0b0b12; the shared dead-screen navy is that colour to
            // within a couple of levels, so the deck's own backdrop does the job.
            AddBackdrop(host, _w, _h);

            if (_variant == Wrongness.Negative) AttachNegative(host);
            else AttachWord(host);

            _rollH = Math.Max(2, _h * 0.16);
            _roll.Width = _w;
            _roll.Height = _rollH;
            _roll.Fill = RollBrush;
            _roll.IsHitTestVisible = false;
            host.Children.Add(_roll);

            // Order from here down is load bearing. The dead frame has to sit UNDER the collapsing
            // line (a picture dying into a bright hairline on black) and under the break-in flash,
            // and over everything else, so that raising one opacity is a guaranteed safe frame no
            // matter what the variant left on screen.
            _dead.Width = _w;
            _dead.Height = _h;
            _dead.Fill = ScreenBrush;
            _dead.Opacity = 0;
            _dead.IsHitTestVisible = false;
            host.Children.Add(_dead);

            _tear.Width = _w;
            _tear.Height = _h;
            _tear.Fill = CreamInk;
            _tear.Opacity = 0;
            _tear.IsHitTestVisible = false;
            host.Children.Add(_tear);

            _flash.Width = _w;
            _flash.Height = _h;
            _flash.Fill = CreamInk;
            _flash.Opacity = 1;
            _flash.IsHitTestVisible = false;
            host.Children.Add(_flash);
        }

        /// <summary>
        /// THE NEGATIVE. The glass floods pink and her face is the hole in it, with a second,
        /// smaller one standing a few px behind and above it. Campus geometry: the ghost at 22 px
        /// offset (+3, -6), her own at 30 px dead centre, both in the face font, because the shape
        /// that unsettles is HER face rendered wrong and not some new drawing.
        /// </summary>
        private void AttachNegative(Panel host)
        {
            _flood.Width = _w;
            _flood.Height = _h;
            _flood.Fill = PinkBrush;
            _flood.IsHitTestVisible = false;
            host.Children.Add(_flood);

            // Both faces take the shared dead-screen navy rather than a fresh dark brush: the hole
            // punched in the pink should be the same nothing the tube shows when it is off.
            _ghost.Text = NegativeFace;
            _ghost.FontFamily = EmiFace.FaceFont;
            _ghost.FontSize = Math.Max(5, 22 * _k);
            _ghost.Foreground = ScreenBrush;
            _ghost.Opacity = 0.55;
            _ghost.IsHitTestVisible = false;
            host.Children.Add(_ghost);
            Centre(_ghost, 3 * _k, -6 * _k);

            _face.Text = NegativeFace;
            _face.FontFamily = EmiFace.FaceFont;
            _face.FontSize = Math.Max(6, 30 * _k);
            _face.Foreground = ScreenBrush;
            _face.IsHitTestVisible = false;
            host.Children.Add(_face);
            var rest = Centre(_face, 0, 0);
            _faceX = rest.X;
            _faceY = rest.Y;
        }

        /// <summary>
        /// THE WORD. Two thicknesses of snow with one word between them. The word sits UNDER the top
        /// pass on purpose: legible enough to be read, buried enough that reading it feels like a
        /// decision you made rather than something you were shown.
        /// </summary>
        private void AttachWord(Panel host)
        {
            // Grain COUNT is fixed and grain SIZE scales with the glass. The other way round (an
            // area-scaled count) makes a 60 DIP glass read as dust on a dark screen instead of as a
            // dead channel, because snow reads by grain density, not by grain count.
            Snow(host, _snow, BaseGrains, GrainLight, GrainDark);

            _word.Text = Words[_rng.Next(Words.Length)];
            _word.FontFamily = EmiFace.PixelFont;
            _word.FontSize = Math.Max(5, 14 * _k);
            _word.Foreground = WordInk;
            _word.Opacity = 0;
            _word.IsHitTestVisible = false;
            host.Children.Add(_word);
            Centre(_word, 0, -6 * _k);

            Snow(host, _snowTop, TopGrains, TopLight, TopDark);
        }

        private void Snow(Panel host, List<Rectangle> into, int count, Brush light, Brush dark)
        {
            double tall = Math.Max(0.5, _k);
            for (int i = 0; i < count; i++)
            {
                // Width is drawn once per grain and never again. The campus re-rolls it every frame,
                // but a pool of mixed-width runs that MOVE every frame is the same picture and does
                // not put a layout pass on the glass thirty times a second.
                double wide = Math.Max(0.5, (1 + _rng.Next(4)) * _k);
                var r = Box(0, 0, wide, tall, i % 2 == 0 ? light : dark, 1.0);
                into.Add(r);
                host.Children.Add(r);
            }
        }

        public override void Tick(double tMs)
        {
            // Past the arc: hold the dead frame. Guarded, so a late teardown costs one comparison a
            // frame rather than a pile of property writes on a screen nobody is looking at.
            if (tMs >= DecayEndMs)
            {
                if (_ended) return;
                _ended = true;
                _dead.Opacity = 1;
                _tear.Opacity = 0;
                return;
            }

            _flash.Opacity = tMs < FlashMs ? 1.0 - tMs / FlashMs : 0.0;

            if (tMs < CoreEndMs)
            {
                Core(tMs);
                return;
            }

            Decay((tMs - CoreEndMs) / (DecayEndMs - CoreEndMs));
        }

        private void Core(double tMs)
        {
            // A dropout is the black frame doing the same job it does at the end, briefly. One
            // element, so there is no second way for the screen to go dark and no way for the two
            // of them to disagree about who owns the top of the stack.
            _dead.Opacity = InDropout(tMs) ? 1.0 : 0.0;

            Canvas.SetTop(_roll, (tMs % RollMs) / RollMs * (_h + _rollH) - _rollH);

            if (tMs - _lastScramble >= ScrambleMs)
            {
                _lastScramble = tMs;
                Scramble();
            }

            if (_variant == Wrongness.Word)
            {
                // The word fades up, sits, and is gone well before the collapse, so it is never on
                // screen at the moment the picture dies. Whatever was there had already left.
                _word.Opacity = 0.35 * Ramp(tMs, 300, 380, 620, 700);
            }
        }

        /// <summary>
        /// The end: the picture collapses to a bright hairline and the line shrinks out, the way a
        /// tube dies. The black frame rises underneath the whole way, so by the time the line is
        /// gone the glass is already the safe final frame.
        /// </summary>
        private void Decay(double p)
        {
            p = Math.Clamp(p, 0, 1);
            _dead.Opacity = p;

            if (p < 0.6)
            {
                double q = p / 0.6;
                double hair = Math.Max(1, _k);
                double tall = _h + (hair - _h) * q;
                _tear.Width = _w;
                _tear.Height = tall;
                Canvas.SetLeft(_tear, 0);
                Canvas.SetTop(_tear, (_h - tall) / 2);
                _tear.Opacity = 0.9;
                return;
            }

            double r = (p - 0.6) / 0.4;
            double wide = Math.Max(0.5, _w * (1 - r));
            _tear.Width = wide;
            Canvas.SetLeft(_tear, (_w - wide) / 2);
            _tear.Opacity = 0.9 * (1 - r);
        }

        private bool InDropout(double t) =>
            (t >= _dropAt0 && t < _dropAt0 + _dropFor0) ||
            (t >= _dropAt1 && t < _dropAt1 + _dropFor1);

        /// <summary>
        /// Re-roll the intrusion. The snow moves; the negative twitches. Both variants get their one
        /// change on the same beat, so the channel has a single heartbeat rather than two.
        /// </summary>
        private void Scramble()
        {
            if (_variant == Wrongness.Negative)
            {
                // A pixel of drift, no more. A face that swims reads as an effect; a face that is
                // one pixel off where it just was reads as a picture nobody is holding steady.
                Canvas.SetLeft(_face, _faceX + (_rng.NextDouble() - 0.5) * 2 * _k);
                Canvas.SetTop(_face, _faceY + (_rng.NextDouble() - 0.5) * 2 * _k);
                _ghost.Opacity = 0.45 + _rng.NextDouble() * 0.18;
                return;
            }

            for (int i = 0; i < _snow.Count; i++) Move(_snow[i], GrainLight, GrainDark);
            for (int i = 0; i < _snowTop.Count; i++) Move(_snowTop[i], TopLight, TopDark);
        }

        private void Move(Rectangle r, Brush light, Brush dark)
        {
            Canvas.SetLeft(r, Math.Floor(_rng.NextDouble() * _w));
            Canvas.SetTop(r, Math.Floor(_rng.NextDouble() * _h));
            r.Fill = _rng.NextDouble() > 0.5 ? light : dark;
        }

        /// <summary>
        /// Centre a text block on the glass with an offset, and hand back where it landed. The
        /// campus draws with textAlign centre and textBaseline middle; WPF has neither on a Canvas,
        /// so this measures the ink once at attach time and never again.
        /// </summary>
        private Point Centre(TextBlock tb, double dx, double dy)
        {
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var d = tb.DesiredSize;
            double x = _w / 2 - d.Width / 2 + dx;
            double y = _h / 2 - d.Height / 2 + dy;
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);
            return new Point(x, y);
        }

        /// <summary>A 0 to 1 trapezoid: up over [a,b], held to c, down by d.</summary>
        private static double Ramp(double t, double a, double b, double c, double d)
        {
            if (t <= a || t >= d) return 0;
            if (t < b) return (t - a) / (b - a);
            if (t <= c) return 1;
            return (d - t) / (d - c);
        }
    }
}
