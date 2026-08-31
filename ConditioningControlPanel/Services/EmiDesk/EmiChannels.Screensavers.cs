using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// THE TWO DEEP-IDLE SAVERS, ported off the campus deck.
///
/// <para>The campus glass (<c>Resources/web/arcademy/emi/channels.js</c>) keeps a pair of channels
/// that are not offers at all: CH7 SCREENSAVER, the code rain, and CH8 OFF AIR, the test card. They
/// are the two things her screen does when it has been left alone, and the desk had neither. Both
/// land here as WPF painters so the desk and the campus show the same appliance.</para>
///
/// <para>The campus quotes its numbers for a 152 x 137 virtual-px glass (<c>GLASS_W/GLASS_H</c>),
/// where the desk's glass is anywhere from about 60 to 175 DIPs across as the user resizes her, so
/// every length below is scaled by <c>w / 152</c> and then floored. The floors matter more here
/// than they did for pong: pong scaled a ball, and these two scale TYPE, which stops being type
/// somewhere around five pixels.</para>
/// </summary>
public static partial class EmiChannels
{
    /// <summary>
    /// Bits both savers wear. Kept as a nested holder rather than as loose members on
    /// <see cref="EmiChannels"/> because the partial is split across several files and a name like
    /// "Scanlines" is exactly the one two ports would both reach for.
    /// </summary>
    private static class SaverGlass
    {
        /// <summary>
        /// True black, shared. Both savers want it and neither wants the house navy: the rain
        /// clears to it and the test card dims with it.
        ///
        /// <para>DECLARED FIRST ON PURPOSE. Static initialisers run in textual order and
        /// <see cref="BuildScanTile"/> paints with this one, so a tidier alphabetical ordering here
        /// would hand the tile a null brush and the scanlines would silently never draw.</para>
        /// </summary>
        internal static readonly SolidColorBrush Black = Freeze(new SolidColorBrush(Colors.Black));

        /// <summary>
        /// The scanline mask every campus channel wears (its <c>scanlines()</c>: one dark row every
        /// two px) as ONE tiling brush instead of one Rectangle per row. A 137 DIP glass would be
        /// sixty-eight rectangles otherwise, all of them static, all of them re-measured on every
        /// layout pass the window does.
        ///
        /// <para>The tile is in ABSOLUTE units, so the mask stays two DIPs no matter how far the
        /// user has dragged her out. A scanline that scales with the window is a stripe.</para>
        /// </summary>
        private static readonly DrawingBrush ScanTile = BuildScanTile();

        private static DrawingBrush BuildScanTile()
        {
            // The drawing fills the top half of a 1 x 2 source box, so the tile is line, gap, line.
            var cell = new GeometryDrawing
            {
                Brush = Black,
                Geometry = new RectangleGeometry(new Rect(0, 0, 1, 1))
            };
            var b = new DrawingBrush(cell)
            {
                TileMode = TileMode.Tile,
                Viewbox = new Rect(0, 0, 1, 2),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 1, 2),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill
            };
            return Freeze(b);
        }

        /// <summary>One element, drawn once, never touched by a tick.</summary>
        internal static Rectangle ScanVeil(double w, double h, double alpha) => new()
        {
            Width = w,
            Height = h,
            Fill = ScanTile,
            Opacity = alpha,
            IsHitTestVisible = false
        };
    }

    /// <summary>
    /// CHANNEL CODE RAIN. The campus CH7 saver, falling down the desk's glass: green columns of
    /// glyphs, a bright head, a fading tail, and about one column in twenty in her pink - which is
    /// the tell under the costume, and the only reason this is HER screensaver rather than a stock
    /// one.
    ///
    /// <para>Renamed from the campus id <c>saver</c> on purpose: the desk already ships a
    /// <c>rain</c> channel (the user's own images falling), and two ids one letter apart in the
    /// same wheel is a bug waiting for a typo.</para>
    ///
    /// <para>ONE TEXTBLOCK PER COLUMN, NOT PER GLYPH. The campus repaints every cell of every
    /// column every frame, which a canvas does not mind and WPF very much does: a hundred and fifty
    /// TextBlocks re-measuring at 30 fps is the phone FX diet's own bug again. A column is a
    /// vertical strip whose brightness only ever falls off upward, so the whole tail fade is one
    /// frozen vertical gradient on one block, and a frame is then just moving each block down.</para>
    /// </summary>
    internal sealed class CodeRainPainter : EmiChannelPainter
    {
        private const double RefGlass = 152.0;  // CAMPUS GLASS_W, the width its numbers are quoted at
        private const double StepRef = 4.0;     // CAMPUS RAIN_STEP, a column every four virtual px
        private const double CellRef = 6.0;     // CAMPUS RAIN_CELL, 4 x 6 glyph cells
        private const int MaxTail = 9;          // CAMPUS 5 + floor(rand * 5), so nine at the top

        /// <summary>
        /// CAMPUS RAIN_GLYPHS, character for character. Written as escapes so this file stays pure
        /// ASCII and cannot be broken by a tool that guesses an encoding; the tail is the katakana
        /// A KA SA TA NA HA MA YA RA WA. Note the missing I and O in the latin run - that is the
        /// campus list, and they are out because they read as 1 and 0 at six pixels.
        /// </summary>
        private const string Glyphs =
            "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789" +
            "\u30A2\u30AB\u30B5\u30BF\u30CA\u30CF\u30DE\u30E4\u30E9\u30EF";

        /// <summary>
        /// The terminal ladder. A latin monospace leads so the letters and digits get a real
        /// terminal face, and the Japanese system faces sit behind it purely to catch the katakana:
        /// neither Press Start 2P nor Consolas has a single one of them, and WPF falls back per
        /// glyph across a comma list (the same trick <see cref="EmiFace.FaceFont"/> uses for the
        /// exotic kaomoji). The katakana come back full width, a touch wider than their column,
        /// which is what they do on the campus too.
        /// </summary>
        private static readonly FontFamily RainFont = Services.UI.FontGuard.Family(
            "Cascadia Mono, Consolas, Noto Sans Mono, MS Gothic, Yu Gothic UI, Meiryo, " +
            "Courier New, Global Monospace");

        private static readonly Color RainGreen = Color.FromRgb(0x00, 0xFF, 0x41);  // CAMPUS RAIN_GREEN

        private readonly double _w, _h, _cell, _step, _rows;
        private readonly Random _rng = new();

        private readonly List<TextBlock> _cols = new();
        private readonly List<double> _y = new();       // head position, in CELLS, like the campus
        private readonly List<double> _speed = new();   // cells per second
        private readonly List<int> _tail = new();

        private readonly char[] _buf = new char[(MaxTail + 1) * 2];
        private double _last;
        private int _churn;

        public CodeRainPainter(double w, double h)
        {
            _w = w; _h = h;
            double k = Math.Max(0.1, w / RefGlass);

            // FLOORED, NOT JUST SCALED. Six times a sixty-DIP glass's k is 2.4, and a 2.4 px glyph
            // is a smear: the pink column and the katakana both stop being visible below about
            // five, and they are the two things that make this rain hers rather than stock.
            _cell = Math.Max(5.0, CellRef * k);
            _step = Math.Max(3.0, StepRef * k);
            _rows = Math.Ceiling(_h / _cell) + 2;
        }

        public override string Id => "coderain";

        public override void Attach(Panel host)
        {
            // BLACK, not the house navy every other channel clears to. Phosphor green only reads as
            // a CRT over true black; over #0E0E1C it reads as a highlighter.
            host.Children.Add(new Rectangle
            {
                Width = _w,
                Height = _h,
                Fill = SaverGlass.Black,
                IsHitTestVisible = false
            });

            int cols = Math.Max(1, (int)Math.Ceiling(_w / _step));
            for (int i = 0; i < cols; i++)
            {
                int tail = 5 + _rng.Next(5);                    // CAMPUS 5 + floor(rand * 5)
                bool pink = _rng.NextDouble() < 0.05;           // CAMPUS: about one column in twenty is hers

                var tb = new TextBlock
                {
                    Text = Roll(tail),
                    FontFamily = RainFont,
                    FontSize = _cell,
                    // The line box is forced down to the cell height so the column packs the way
                    // the campus grid does. Left alone, WPF adds a comfortable reading leading and
                    // the rain falls as a dotted line.
                    LineHeight = _cell,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    TextAlignment = TextAlignment.Center,
                    Width = _cell * 2,
                    Foreground = TailBrush(pink ? Pink : RainGreen, tail),
                    IsHitTestVisible = false
                };
                // Half a cell of slack either side, so a full-width katakana bleeds symmetrically
                // instead of shoving its whole column right.
                Canvas.SetLeft(tb, i * _step - _cell * 0.5);

                _cols.Add(tb);
                _tail.Add(tail);
                _y.Add(-Math.Floor(_rng.NextDouble() * 40));    // CAMPUS: they start well above the glass
                _speed.Add(12 + _rng.NextDouble() * 26);        // CAMPUS 12 + rand * 26 cells/s
                host.Children.Add(tb);
            }

            host.Children.Add(SaverGlass.ScanVeil(_w, _h, 0.14));   // CAMPUS scanlines(g, 0.14)
        }

        public override void Tick(double tMs)
        {
            // Clamped exactly like the campus frame(): the glass timer is a Background-priority
            // DispatcherTimer and WILL be late under load, and an unclamped dt drops whole columns
            // off the bottom in a single step.
            double dt = Math.Min(120.0, tMs - _last) / 1000.0;
            _last = tMs;

            for (int i = 0; i < _cols.Count; i++)
            {
                double y = _y[i] + _speed[i] * dt;
                if (y - _tail[i] > _rows)
                {
                    y = -2;                                  // CAMPUS: back to just above the glass
                    _cols[i].Text = Roll(_tail[i]);          // and a fresh run of letters with it
                }
                _y[i] = y;

                // SNAPPED TO WHOLE CELLS. The campus draws on integer rows, so the fall is stepped
                // rather than smooth, and copying that is also the cheap choice here: text parked
                // on a fractional offset re-rasterises with different hinting every frame and the
                // whole column shimmers.
                Canvas.SetTop(_cols[i], (Math.Floor(y) - _tail[i]) * _cell);
            }

            // THE CHURN, at a fraction of the price. The campus derives each glyph from its
            // ABSOLUTE row, so a falling column's letters change under it; a per-column block
            // cannot do that for free. One column re-rolls per frame instead, round robin, so each
            // one turns over about once a second and the rain still crawls.
            if (_cols.Count > 0)
            {
                _churn = (_churn + 1) % _cols.Count;
                _cols[_churn].Text = Roll(_tail[_churn]);
            }
        }

        /// <summary>A column's run of glyphs, one per line, built into the shared buffer.</summary>
        private string Roll(int tail)
        {
            int len = 0;
            for (int j = 0; j <= tail; j++)
            {
                if (j > 0) _buf[len++] = '\n';
                _buf[len++] = Glyphs[_rng.Next(Glyphs.Length)];
            }
            return new string(_buf, 0, len);
        }

        /// <summary>
        /// The tail fade as a frozen gradient: the campus's per-cell alpha (1 at the head, then
        /// <c>max(0, 1 - k / tail) * 0.7</c> going up) written as two stops per line, so the bands
        /// are hard-edged like drawn cells rather than a soft wash.
        /// </summary>
        private static LinearGradientBrush TailBrush(Color c, int tail)
        {
            int n = tail + 1;
            var stops = new GradientStopCollection(n * 2);
            for (int j = 0; j < n; j++)
            {
                int k = n - 1 - j;                            // 0 is the bottom line, and the head
                double a = k == 0 ? 1.0 : Math.Max(0.0, 1.0 - (double)k / tail) * 0.7;
                var band = Color.FromArgb((byte)Math.Round(a * 255.0), c.R, c.G, c.B);
                stops.Add(new GradientStop(band, (double)j / n));
                stops.Add(new GradientStop(band, (double)(j + 1) / n));
            }
            return Freeze(new LinearGradientBrush(stops, new Point(0, 0), new Point(0, 1)));
        }
    }

    /// <summary>
    /// CHANNEL OFF AIR. The campus CH8 test card: six colour bars, a dark plate with "brb" on it,
    /// and NO SIGNAL along the bottom. She has stopped broadcasting.
    ///
    /// <para>THIS IS THE ONE THAT HAS TO SURVIVE A STARVED CLOCK. Everything else on the glass is a
    /// thing in motion, so when the tick is throttled they degrade into a still of a half-drawn
    /// animation. A test card is a still by nature, so this is the deep-idle look that still reads
    /// when nothing moves - and that only holds if <see cref="Attach"/> alone leaves a FINISHED
    /// card behind. It does: bars, dim, plate, "brb" and NO SIGNAL all land at once, and
    /// <see cref="Tick"/> may do nothing at all forever without the picture looking unfinished.</para>
    ///
    /// <para>Which is the one deliberate break from the campus. There the dim and the NO SIGNAL
    /// line both arrive at four seconds, on a channel that runs until you touch it. A desk channel
    /// airs for ten (<see cref="ChannelLife"/>), so a bottom line that only shows up at second four
    /// is a bottom line that never shows up on a throttled clock. Both are baked in at zero and the
    /// tick is left holding a hum.</para>
    /// </summary>
    internal sealed class OffAirPainter : EmiChannelPainter
    {
        private const double RefGlass = 152.0;  // CAMPUS GLASS_W again

        /// <summary>
        /// CAMPUS BAR_COLOURS, in order. The third is the house cream, so it reuses the shared
        /// brush rather than minting a second one of the same colour.
        /// </summary>
        private static readonly SolidColorBrush[] Bars =
        {
            Freeze(new SolidColorBrush(Color.FromRgb(0xB3, 0x6A, 0x75))),
            Freeze(new SolidColorBrush(Color.FromRgb(0x5A, 0x6B, 0x84))),
            CreamBrush,
            Freeze(new SolidColorBrush(Color.FromRgb(0x4E, 0x8C, 0x86))),
            Freeze(new SolidColorBrush(Color.FromRgb(0x7A, 0x5C, 0x87))),
            Freeze(new SolidColorBrush(Color.FromRgb(0x2F, 0x2F, 0x3D)))
        };

        /// <summary>CAMPUS DARK, the plate the "brb" sits on. Not the screen navy.</summary>
        private static readonly SolidColorBrush PlateBrush =
            Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)));

        /// <summary>
        /// The baked dim. The campus lands on 0.55 after its fade; this sits a little under it,
        /// because there the bars have had four bright seconds first and here they never do, and a
        /// card that opens at 0.55 opens looking switched off rather than signed off.
        /// </summary>
        private const double DimBase = 0.45;

        private readonly double _w, _h, _k, _drift, _noSignalLeft;
        private readonly bool _motion;

        private readonly Rectangle _dim = new();
        private readonly TextBlock _noSignal = new();

        public OffAirPainter(double w, double h)
        {
            _w = w; _h = h;
            _k = Math.Max(0.1, w / RefGlass);
            _drift = 4 * _k;                    // CAMPUS: NO SIGNAL steps four virtual px a second
            _noSignalLeft = 10 * _k;

            // Read once, not per frame. An airing is ten seconds long and nobody flips the motion
            // setting inside one; the alternative is a guarded static property read at 30 fps.
            _motion = MotionOk();
        }

        public override string Id => "offair";

        public override void Attach(Panel host)
        {
            // The bars, edge to edge. The +1 on the width is the campus's own seam guard: six exact
            // sixths of an odd width leave hairlines of backdrop showing between them.
            double bw = _w / Bars.Length;
            for (int i = 0; i < Bars.Length; i++)
            {
                host.Children.Add(Box(i * bw, 0, bw + 1, _h, Bars[i], 1.0));
            }

            _dim.Width = _w;
            _dim.Height = _h;
            _dim.Fill = SaverGlass.Black;
            _dim.Opacity = DimBase;
            _dim.IsHitTestVisible = false;
            host.Children.Add(_dim);

            // the centre plate
            double r = 30 * _k;
            var plate = new Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Fill = PlateBrush,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(plate, _w / 2 - r);
            Canvas.SetTop(plate, _h / 2 - 6 * _k - r);
            host.Children.Add(plate);

            // "brb", lowercase, exactly as the campus says it. Centred by handing the block the
            // full width and letting WPF centre inside it: the alternative is measuring pixel type,
            // and Press Start 2P seen through a fallback chain is not a width this can predict.
            double brbSize = Math.Max(6, 12 * _k);
            var brb = new TextBlock
            {
                Text = "brb",
                FontFamily = EmiFace.PixelFont,
                FontSize = brbSize,
                Foreground = CreamBrush,
                Width = _w,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(brb, 0);
            Canvas.SetTop(brb, _h / 2 - brbSize);
            host.Children.Add(brb);

            _noSignal.Text = "NO SIGNAL";
            _noSignal.FontFamily = EmiFace.PixelFont;
            _noSignal.FontSize = Math.Max(5, 7 * _k);
            _noSignal.Foreground = CreamBrush;
            _noSignal.Opacity = 0.85;
            _noSignal.IsHitTestVisible = false;
            Canvas.SetLeft(_noSignal, _noSignalLeft);
            Canvas.SetTop(_noSignal, _h - Math.Max(9, 16 * _k));
            host.Children.Add(_noSignal);

            host.Children.Add(SaverGlass.ScanVeil(_w, _h, 0.16));    // CAMPUS scanlines(g, 0.16)
        }

        public override void Tick(double tMs)
        {
            // Nothing below is load bearing. Under reduced motion the card simply sits, which is
            // the entire reason this channel exists.
            if (!_motion) return;

            // NO SIGNAL walks one step a second and wraps at six. That is the campus's whole
            // animation, and it is why this look is the reduced-motion-safe one.
            int step = (int)(tMs / 1000.0) % 6;
            Canvas.SetLeft(_noSignal, _noSignalLeft + step * _drift);

            // The hum: a tube that has been left on, not a fade. Two hundredths of opacity on a
            // slow sine, plus one dropped frame about every three seconds - enough that a watching
            // eye knows the set is still powered, small enough that a still of it is the same
            // picture.
            double flicker = tMs % 3300.0 < 60.0 ? -0.06 : 0.0;
            _dim.Opacity = DimBase + 0.02 * Math.Sin(tMs / 900.0) + flicker;
        }
    }
}
