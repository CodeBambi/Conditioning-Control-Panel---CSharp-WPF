using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ConditioningControlPanel.Services.EmiDesk;

public static partial class EmiChannels
{
    /// <summary>
    /// CHANNEL BROWSING. Caught with tabs open. A port of the campus saver
    /// (<c>Resources/web/arcademy/emi/channels.js</c>, CH2 "LATE NIGHT BROWSING") onto WPF shapes:
    /// six wireframe pages of in-universe apps, one accent per app, flipped every 2500 ms.
    ///
    /// <para>NOTHING HERE IS A REAL BRAND. The parody is the LAYOUT - a title bar, chunky blocks,
    /// a three to six character name - so each page reads as a different place at a glance without
    /// a single word of anybody else's copy. Every page is somewhere the base already knows: her
    /// mail, the board, the bank, the counter's now-serving card, the hole, the records room.</para>
    ///
    /// <para>SIZING. The campus numbers for this channel are quoted on the LOCKED FACE GRID, the
    /// 152 x 137 virtual glass <c>face.js</c> paints, not on pong's quoted 60 - so the scaling law
    /// is the same idea with a different reference, <c>k = w / 152</c>. The desk glass runs about
    /// 60 to 175 DIPs across at the same 0.91 aspect, so the grid maps over almost exactly. The one
    /// place literal offsets had to go is COPY: a title bar of 13 * k is 5 px on a small glass and
    /// Press Start 2P cannot live in 5 px, so every font size has a legibility floor and the rows
    /// are laid out in whatever body height is left over. Port the look, not the canvas calls.</para>
    ///
    /// <para>THE CHANNEL OUTLIVES THE CYCLE. Ten seconds of life is about four pages, and the
    /// campus's plan() only ever deals two to four, so the order here is a full shuffle of all six
    /// read round-robin: nobody sees the wrap, and two airings running are two different sittings.</para>
    /// </summary>
    internal sealed class BrowsingPainter : EmiChannelPainter
    {
        private const double RefGlass = 152.0;  // the locked face grid these numbers are written at
        private const double PageMs = 2500.0;   // CAMPUS PAGE_MS
        private const double BlinkMs = 500.0;   // CAMPUS blink, floor(t / 500) % 2
        private const double DropAtMs = 1400.0; // the bank's bad news, measured in page dwell
        private const double DeepRollMs = 1000.0;

        private const int Mail = 0, Board = 1, Bank = 2, Cards = 3, Deep = 4, Rec = 5;
        private const int PageCount = 6;

        // ---- the palette, straight off CH2 -----------------------------------------------------
        private static readonly SolidColorBrush DarkBrush =
            Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)));   // CAMPUS DARK
        private static readonly SolidColorBrush RowBrush =
            Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x44)));   // an unread row
        private static readonly SolidColorBrush BarBrush =
            Freeze(new SolidColorBrush(Color.FromRgb(0x43, 0x43, 0x6A)));   // the fake subject line
        private static readonly SolidColorBrush BlockBrush =
            Freeze(new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x3F)));   // a dead tile
        private static readonly SolidColorBrush LabelBrush =
            Freeze(new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x7A)));   // small grey caption
        private static readonly SolidColorBrush DropBrush =
            Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0x83, 0x6F)));   // the minus, in REC orange

        /// <summary>One accent per app. The same six, in the same order, as CAMPUS BROWSE_PAGES.</summary>
        private static readonly SolidColorBrush[] Accents =
        {
            Freeze(new SolidColorBrush(Color.FromRgb(0x7F, 0xD4, 0xC1))),   // MAIL
            Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xC1, 0x4E))),   // BOARD
            Freeze(new SolidColorBrush(Color.FromRgb(0x8C, 0xC6, 0x3F))),   // BANK
            Freeze(new SolidColorBrush(Color.FromRgb(0xC9, 0xA0, 0xDC))),   // CARDS
            Freeze(new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xDC))),   // DEEP
            Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0x83, 0x6F)))    // REC
        };

        /// <summary>The app names. Three to six characters, and shouted, exactly as on campus.</summary>
        private static readonly string[] Titles = { "MAIL", "BOARD", "BANK", "CARDS", "DEEP", "REC" };

        /// <summary>
        /// The CRT rake, as ONE tiled brush under ONE rectangle. The campus fills a 1 px bar every
        /// 2 px, which on the tallest desk glass is eighty shapes per page and six pages of them; a
        /// frozen 2 px tile is the same picture for one node.
        /// </summary>
        private static readonly DrawingBrush ScanlineBrush = BuildScanlines();

        // ---- geometry ---------------------------------------------------------------------------
        private readonly double _w, _h, _k;
        private readonly double _font, _small, _big, _barH, _body, _pad;
        private readonly Random _rng = new();

        // ---- the built pages --------------------------------------------------------------------
        private readonly Canvas[] _pages = new Canvas[PageCount];
        private readonly int[] _order = new int[PageCount];

        // the bits Tick is allowed to touch, and nothing else
        private readonly List<Rectangle> _mailBars = new();
        private readonly List<Rectangle> _boardBars = new();
        private readonly Rectangle _litRow = new();
        private readonly Rectangle _litDot = new();
        private readonly Rectangle _litBar = new();
        private readonly TextBlock _bankNum = new();
        private readonly TextBlock _bankDrop = new();
        private readonly Polyline _deepTrace = new();
        private readonly PointCollection _deepPts = new();

        private double _mailBarMax, _boardBarMax, _deepStep, _deepTop;
        private int _live = -1, _litState = -1, _bankN = -1;
        private double _pageUpMs, _deepRolledMs;

        public BrowsingPainter(double w, double h)
        {
            _w = w; _h = h;
            _k = Math.Max(0.5, w / RefGlass);

            // The floors are the whole reason this is not a straight multiply: at the small end of
            // the glass a literal 8 px cell lands under 4 px and the page turns into grey mush.
            _font = Math.Max(5.0, 8.0 * _k);
            _small = Math.Max(5.0, 7.0 * _k);
            _big = Math.Max(9.0, 16.0 * _k);
            _barH = Math.Max(_font + 3.0, 13.0 * _k);
            _pad = Math.Max(2.0, 6.0 * _k);
            _body = Math.Max(8.0, _h - _barH);

            for (int i = 0; i < PageCount; i++) _order[i] = i;
            for (int i = PageCount - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
        }

        public override string Id => "browsing";

        public override void Attach(Panel host)
        {
            // CAMPUS clears to #0d0d18; the house dead-screen navy is #0E0E1C, the same colour to
            // within a rounding error, so the shared backdrop stands in for it.
            AddBackdrop(host, _w, _h);

            for (int i = 0; i < PageCount; i++)
            {
                var page = new Canvas
                {
                    Width = _w,
                    Height = _h,
                    ClipToBounds = true,
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed
                };

                // the title bar, one accent per app, name in the dark ink so it reads as chrome
                page.Children.Add(Box(0, 0, _w, _barH, Accents[i], 1.0));
                page.Children.Add(LeftText(new TextBlock(), Titles[i], Math.Max(2, 4 * _k),
                    Math.Max(0, (_barH - _font * 1.3) / 2), _font, DarkBrush));

                switch (i)
                {
                    case Mail: BuildMail(page); break;
                    case Board: BuildBoard(page); break;
                    case Bank: BuildBank(page, Accents[i]); break;
                    case Cards: BuildCards(page, Accents[i]); break;
                    case Deep: BuildDeep(page, Accents[i]); break;
                    default: BuildRec(page, Accents[i]); break;
                }

                _pages[i] = page;
                host.Children.Add(page);
            }

            // the rake goes on last so it lies over every page at once
            host.Children.Add(new Rectangle
            {
                Width = _w,
                Height = _h,
                Fill = ScanlineBrush,
                IsHitTestVisible = false
            });
        }

        public override void Tick(double tMs)
        {
            int slot = tMs <= 0 ? 0 : (int)(tMs / PageMs);
            int page = _order[slot % PageCount];

            if (page != _live)
            {
                if (_live >= 0) _pages[_live].Visibility = Visibility.Collapsed;
                _live = page;
                _pageUpMs = tMs;
                _bankN = -1;
                _litState = -1;
                _pages[page].Visibility = Visibility.Visible;
                Reroll(page, tMs);
            }

            double dwell = tMs - _pageUpMs;

            switch (page)
            {
                case Board:
                    Blink(tMs);
                    break;

                case Bank:
                    // The balance climbs, which is the joke: she is being paid to sit there. The
                    // string is only rebuilt when the number actually moves (about 25 times a
                    // second at the campus rate), never once per frame for its own sake.
                    int n = 1200 + (int)(tMs / 40.0);
                    if (n != _bankN)
                    {
                        _bankN = n;
                        _bankNum.Text = n.ToString(CultureInfo.InvariantCulture);
                    }

                    // CAMPUS quotes this beat off airing time (t > 6000). On the desk the page up at
                    // six seconds is whichever one the shuffle dealt third, so the minus is quoted
                    // off PAGE DWELL instead: it lands every time the bank is on, which is the only
                    // way anybody ever sees it.
                    var want = dwell > DropAtMs ? Visibility.Visible : Visibility.Collapsed;
                    if (_bankDrop.Visibility != want) _bankDrop.Visibility = want;
                    break;

                case Deep:
                    // A sounding, not a static chart. The campus re-rolls this trace every frame,
                    // which on a canvas reads as a live instrument and in WPF would just be thirty
                    // frames a second of noise, so it takes a fresh reading once a second instead.
                    if (tMs - _deepRolledMs >= DeepRollMs) RollDeep(tMs);
                    break;
            }
        }

        // ---------------------------------------------------------------- the pages

        /// <summary>
        /// MAIL. Five rows, and the second one is FROM HER - the only pink on the page and the only
        /// lower case word in the whole channel, because a subject line is not chrome.
        /// </summary>
        private void BuildMail(Canvas page)
        {
            double top = _barH + _pad;
            double pitch = Math.Max(3.0, (_h - top - _pad * 0.5) / 5.0);
            double rowH = Math.Max(_small + 1.0, pitch * 0.78);
            double x = _pad, w = Math.Max(4.0, _w - _pad * 2);
            _mailBarMax = Math.Max(3.0, w - _pad * 1.2);

            for (int r = 0; r < 5; r++)
            {
                double y = top + r * pitch;
                if (r == 1)
                {
                    page.Children.Add(Box(x, y, w, rowH, PinkBrush, 1.0));
                    page.Children.Add(LeftText(new TextBlock(), "re: emi", x + _pad * 0.6,
                        y + Math.Max(0, (rowH - _small * 1.3) / 2), _small, DarkBrush));
                }
                else
                {
                    page.Children.Add(Box(x, y, w, rowH, RowBrush, 1.0));
                    var bar = Box(x + _pad * 0.6, y + rowH * 0.42, _mailBarMax * 0.5,
                        Math.Max(1.5, 4 * _k), BarBrush, 1.0);
                    _mailBars.Add(bar);
                    page.Children.Add(bar);
                }
            }
        }

        /// <summary>
        /// BOARD. Four threads, and the third one keeps lighting up. Only that row animates, so only
        /// that row's three shapes are held; the rest are drawn once and never looked at again.
        /// </summary>
        private void BuildBoard(Canvas page)
        {
            double top = _barH + _pad;
            double pitch = Math.Max(4.0, (_h - top - _pad * 0.5) / 4.0);
            double rowH = Math.Max(3.0, pitch * 0.72);
            double x = _pad, w = Math.Max(4.0, _w - _pad * 2);
            double dot = Math.Max(2.0, 4 * _k);
            double barH = Math.Max(1.5, 4 * _k);
            _boardBarMax = Math.Max(3.0, w - _pad * 1.6 - dot * 2);

            for (int r = 0; r < 4; r++)
            {
                double y = top + r * pitch;
                bool tracked = r == 2;

                var row = tracked ? _litRow : new Rectangle();
                Place(row, w, rowH, BlockBrush, x, y);
                page.Children.Add(row);

                var pip = tracked ? _litDot : new Rectangle();
                Place(pip, dot, dot, Accents[Board], x + _pad * 0.6, y + rowH * 0.16);
                page.Children.Add(pip);

                var bar = tracked ? _litBar : new Rectangle();
                Place(bar, _boardBarMax * 0.6, barH, BarBrush, x + _pad * 0.6 + dot * 2, y + rowH * 0.42);
                if (!tracked) _boardBars.Add(bar);
                page.Children.Add(bar);
            }
        }

        /// <summary>
        /// BANK. A number going up, a caption, and a small red thing that turns up late. The block
        /// at the bottom is a transactions list nobody gets to read.
        /// </summary>
        private void BuildBank(Canvas page, Brush accent)
        {
            page.Children.Add(Centered(new TextBlock(), "BALANCE", _barH + _body * 0.09, _small, LabelBrush));
            page.Children.Add(Centered(_bankNum, "1200", _barH + _body * 0.22, _big, accent));

            _bankDrop.Visibility = Visibility.Collapsed;
            page.Children.Add(Centered(_bankDrop, "-14", _barH + _body * 0.50, Math.Max(6, 10 * _k), DropBrush));

            page.Children.Add(Box(_pad * 1.5, _barH + _body * 0.72,
                Math.Max(4.0, _w - _pad * 3), Math.Max(3.0, _body * 0.16), BlockBrush, 1.0));
        }

        /// <summary>
        /// CARDS. The counter's now-serving card, on cream, with the number in pink. Still 004, and
        /// still not yours.
        /// </summary>
        private void BuildCards(Canvas page, Brush accent)
        {
            double cardX = _w * 0.13, cardW = Math.Max(6.0, _w * 0.74);
            double cardY = _barH + _body * 0.11, cardH = Math.Max(6.0, _body * 0.49);
            page.Children.Add(Box(cardX, cardY, cardW, cardH, CreamBrush, 1.0));

            page.Children.Add(Centered(new TextBlock(), "NOW", cardY + cardH * 0.08, _font, DarkBrush));
            page.Children.Add(Centered(new TextBlock(), "SERVING", cardY + cardH * 0.36, _font, DarkBrush));
            page.Children.Add(Centered(new TextBlock(), "004", cardY + cardH * 0.64,
                Math.Max(7, 12 * _k), PinkBrush));

            page.Children.Add(Box(cardX, _barH + _body * 0.72, cardW, Math.Max(2.0, 8 * _k), accent, 1.0));
        }

        /// <summary>
        /// DEEP. One line that only ever goes down, and off the bottom of the glass. That is the
        /// whole page, and it is the funniest one.
        /// </summary>
        private void BuildDeep(Canvas page, Brush accent)
        {
            _deepStep = Math.Max(3.0, 9 * _k);
            _deepTop = _barH + _body * 0.07;

            int count = Math.Max(4, (int)((_w - _pad * 2) / _deepStep) + 1);
            for (int i = 0; i < count; i++) _deepPts.Add(new Point(_pad + i * _deepStep, _deepTop));

            _deepTrace.Points = _deepPts;
            _deepTrace.Stroke = accent;
            _deepTrace.StrokeThickness = Math.Max(1.0, 2 * _k);
            _deepTrace.StrokeLineJoin = PenLineJoin.Round;
            _deepTrace.IsHitTestVisible = false;
            page.Children.Add(_deepTrace);

            page.Children.Add(LeftText(new TextBlock(), "DEPTH", _pad,
                _barH + Math.Max(1.0, 2 * _k), _small, LabelBrush));
        }

        /// <summary>
        /// REC. A twelve tile shelf with exactly one tile lit, the same one every time. Drawn once
        /// and never touched again: the records room does not move for anybody.
        /// </summary>
        private void BuildRec(Canvas page, Brush accent)
        {
            double x0 = _w * 0.053, pitchX = _w * 0.224, tileW = Math.Max(3.0, _w * 0.184);
            double y0 = _barH + _body * 0.07, pitchY = _body * 0.29, tileH = Math.Max(3.0, _body * 0.24);

            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    bool lit = r == 1 && c == 2;
                    page.Children.Add(Box(x0 + c * pitchX, y0 + r * pitchY, tileW, tileH,
                        lit ? accent : BlockBrush, 1.0));
                }
            }
        }

        // ---------------------------------------------------------------- the beats

        /// <summary>
        /// Fresh nonsense for the page that just came up. The campus re-randomises its bar widths
        /// every frame; on a page of fake TEXT that reads as static rather than as writing, so the
        /// dice are rolled once per page turn - a different sitting each time it comes back around.
        /// </summary>
        private void Reroll(int page, double tMs)
        {
            switch (page)
            {
                case Mail:
                    foreach (var bar in _mailBars) bar.Width = _mailBarMax * (0.30 + _rng.NextDouble() * 0.36);
                    break;
                case Board:
                    foreach (var bar in _boardBars) bar.Width = _boardBarMax * (0.42 + _rng.NextDouble() * 0.29);
                    _litBar.Width = _boardBarMax * (0.42 + _rng.NextDouble() * 0.29);
                    Blink(tMs);
                    break;
                case Deep:
                    RollDeep(tMs);
                    break;
            }
        }

        /// <summary>The third thread, lighting. The brushes only swap on the half second they change.</summary>
        private void Blink(double tMs)
        {
            int lit = (int)(tMs / BlinkMs) % 2 == 0 ? 1 : 0;
            if (lit == _litState) return;
            _litState = lit;

            _litRow.Fill = lit == 1 ? Accents[Board] : BlockBrush;
            _litDot.Fill = lit == 1 ? DarkBrush : Accents[Board];
            _litBar.Fill = lit == 1 ? DarkBrush : BarBrush;
        }

        /// <summary>
        /// One sounding of the hole. Points are assigned in place, never re-collected: the trace
        /// only ever descends, and it is meant to run off the bottom edge and get clipped.
        /// </summary>
        private void RollDeep(double tMs)
        {
            _deepRolledMs = tMs;
            double y = _deepTop;
            for (int i = 0; i < _deepPts.Count; i++)
            {
                _deepPts[i] = new Point(_pad + i * _deepStep, Math.Min(y, _h + 20 * _k));
                y += (4 + _rng.NextDouble() * 10) * _k;
            }
        }

        // ---------------------------------------------------------------- small builders

        private static void Place(Rectangle r, double w, double h, Brush fill, double x, double y)
        {
            r.Width = Math.Max(0.5, w);
            r.Height = Math.Max(0.5, h);
            r.Fill = fill;
            r.IsHitTestVisible = false;
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
        }

        /// <summary>Copy centred the way the campus's align 'center' does, without measuring text.</summary>
        private TextBlock Centered(TextBlock tb, string s, double top, double size, Brush ink)
        {
            tb.Text = s;
            tb.FontFamily = EmiFace.PixelFont;
            tb.FontSize = size;
            tb.Foreground = ink;
            tb.Width = _w;
            tb.TextAlignment = TextAlignment.Center;
            tb.IsHitTestVisible = false;
            Canvas.SetLeft(tb, 0);
            Canvas.SetTop(tb, top);
            return tb;
        }

        private TextBlock LeftText(TextBlock tb, string s, double x, double top, double size, Brush ink)
        {
            tb.Text = s;
            tb.FontFamily = EmiFace.PixelFont;
            tb.FontSize = size;
            tb.Foreground = ink;
            tb.IsHitTestVisible = false;
            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, top);
            return tb;
        }

        private static DrawingBrush BuildScanlines()
        {
            // 0x33 alpha is the campus's rgba(0,0,0,0.2), and the 2 x 2 tile with a 2 x 1 bar in the
            // top of it is its "a 1 px line every 2 px" loop.
            var ink = Freeze(new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)));
            var drawing = new GeometryDrawing(ink, null, new RectangleGeometry(new Rect(0, 0, 2, 1)));
            return Freeze(new DrawingBrush(drawing)
            {
                TileMode = TileMode.Tile,
                Viewbox = new Rect(0, 0, 2, 2),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 2, 2),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill
            });
        }
    }
}
