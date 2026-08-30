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
    /// CHANNEL SHOP AT HOME. The campus's CH5 (<c>Resources/web/arcademy/emi/channels.js</c>) on
    /// the desk glass: one piece of in-universe merchandise turning on a pedestal, a price that
    /// only ever falls, and a CALL NOW bar blinking at nobody.
    ///
    /// <para>THE JOKE IS THE LAYOUT, NOT THE ITEM. Every beat here is a shopping channel's tell -
    /// the gold header, the plum product name, the price in a size nothing else on the glass gets,
    /// the coral call bar. She is not selling anything (nothing on this channel is buyable, and a
    /// tap on it just puts her face back, same as the pong board), she is doing the format.</para>
    ///
    /// <para>SIZING. Pong's numbers were quoted against a 60 px campus board, so its scalar is
    /// <c>w / 60</c>; this channel's were written against the 152 x 137 campus GLASS, so the same
    /// law with a different reference gives <c>w / 152</c>. One scalar covers both axes because the
    /// desk glass keeps the campus aspect (137/152 and the desk's 0.903 are the same ratio).</para>
    ///
    /// <para>The desk glass is 60 to 175 DIPs across, which is a quarter of the campus width at the
    /// bottom end, so the vertical stack is NOT the campus's literal y ladder. Bands and type are
    /// floored at readable pixel sizes first and the pedestal takes what is left over. At 152 that
    /// arithmetic lands within a few px of the campus frame, and at 60 it still reads as the same
    /// appliance instead of overlapping into mush.</para>
    /// </summary>
    private sealed class ShopPainter : EmiChannelPainter
    {
        private const double RefGlass = 152.0;  // the campus GLASS_W the CH5 numbers are quoted at
        private const double SpinStepMs = 220;  // CAMPUS: floor(t / 220) % 4
        private const double PriceTickMs = 90;  // CAMPUS: floor(t / 90), one markdown per tick
        private const double BlinkMs = 500;     // CAMPUS: floor(t / 500) % 2, the CALL NOW bar
        private const double FloorBlinkMs = 250;
        private const int FloorPrice = 9;       // CAMPUS: Math.max(9, ...). It never reaches zero.

        /// <summary>
        /// The price has to hit the floor while the channel is still up. <see cref="ChannelLife"/>
        /// is ten seconds and the glitch flip eats the front of it, so aim the landing at seven and
        /// a bit and leave the rest of the life for the floored bar to scream.
        /// </summary>
        private const double FallMs = 7300;

        /// <summary>CAMPUS: the four step squeeze that reads as a turn. Frame 2 is edge on.</summary>
        private static readonly double[] Squeeze = { 1.0, 0.6, 0.15, 0.6 };

        private static readonly SolidColorBrush BgBrush =
            Freeze(new SolidColorBrush(Color.FromRgb(0x12, 0x04, 0x0F)));   // CAMPUS #12040f
        private static readonly SolidColorBrush BandBrush =
            Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x0A, 0x20)));   // CAMPUS #2a0a20
        private static readonly SolidColorBrush GoldBrush =
            Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xC1, 0x4E)));   // CAMPUS #F2C14E
        private static readonly SolidColorBrush PlumBrush =
            Freeze(new SolidColorBrush(Color.FromRgb(0xC9, 0xA0, 0xDC)));   // CAMPUS #c9a0dc
        private static readonly SolidColorBrush CoralBrush =
            Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0x83, 0x6F)));   // CAMPUS #E8836F, lit
        private static readonly SolidColorBrush DeadBarBrush =
            Freeze(new SolidColorBrush(Color.FromRgb(0x3A, 0x14, 0x20)));   // CAMPUS #3a1420, unlit
        private static readonly SolidColorBrush DeadInkBrush =
            Freeze(new SolidColorBrush(Color.FromRgb(0x7A, 0x40, 0x50)));   // CAMPUS #7a4050
        private static readonly SolidColorBrush DarkInkBrush =
            Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)));   // CAMPUS DARK, on coral

        /// <summary>What is on the pedestal tonight. CAMPUS SHOP_ITEMS, prices included.</summary>
        private enum Kind { Card, Watch, Disc }

        private readonly Kind _kind;
        private readonly string _label;
        private readonly int _start;
        private readonly int _drop;

        private readonly double _w, _h, _k;
        private double _ks;                     // the pedestal cluster's own scale, see Attach
        private double _cx, _cy, _bodyW;

        private readonly Rectangle _body = new();
        private readonly Rectangle _base = new();
        private readonly List<Rectangle> _detail = new(4);
        private readonly Rectangle _foot = new();
        private readonly TextBlock _price = new();
        private readonly TextBlock _call = new();

        private double _lastW = -1;
        private int _shownPrice = -1;
        private bool _lit = true;

        public ShopPainter(double w, double h)
        {
            _w = w; _h = h;
            _k = Math.Max(0.35, w / RefGlass);

            // Rng rather than a private Random: every painter is built on the dispatcher, and the
            // shop has no per-instance stream to keep the way pong's fumble does.
            switch (Rng.Next(3))
            {
                case 0: _kind = Kind.Card; _label = "CARD"; _start = 480; break;
                case 1: _kind = Kind.Watch; _label = "WATCH"; _start = 1290; break;
                default: _kind = Kind.Disc; _label = "DISC"; _start = 760; break;
            }

            // CAMPUS drops a flat 7 per tick, which lands CARD (480) on the floor at about six
            // seconds and strands WATCH (1290) at four figures forever. On a page that sits there
            // all evening that is fine; on a ten second channel a price that never arrives is a
            // beat that never pays, so the slope is per item and 7 stays the minimum. CARD keeps
            // the literal campus number; only the two that could not land get steeper.
            int ticksToFloor = Math.Max(1, (int)(FallMs / PriceTickMs));
            _drop = Math.Max(7, (int)Math.Ceiling((_start - FloorPrice) / (double)ticksToFloor));
        }

        public override string Id => "shop";

        public override void Attach(Panel host)
        {
            // NOT the shared ScreenBrush. Every other channel sits on the dead navy the glass is
            // when it is off; the shopping channel is a different appliance broadcasting into her,
            // and the campus gives it its own maroon black to say so.
            host.Children.Add(new Rectangle
            {
                Width = _w,
                Height = _h,
                Fill = BgBrush,
                IsHitTestVisible = false
            });

            // ---- the stack, floored from the type outward ------------------------------------
            // Press Start 2P is an 8 x 8 cell and smears between device pixels at fractional
            // sizes, so every size here is a whole number. The per line divisors are the longest
            // string that line can hold (the cell advance is one em), which is what keeps
            // "SHOP AT HOME" inside a 60 DIP glass instead of running off both ends.
            double titleSize = PixelSize(7 * _k, _w / 13.5, 4);
            double callSize = PixelSize(7 * _k, _w / 8.5, 4);
            double labelSize = PixelSize(7 * _k, _w / 6.0, 4);
            double priceSize = PixelSize(12 * _k, _w / 7.5, 6);

            double headH = Math.Max(titleSize * 1.6, 16 * _k);
            double footH = Math.Max(callSize * 1.6, 14 * _k);
            double priceTop = _h - footH - priceSize * 1.45 - _k;
            double labelTop = priceTop - labelSize * 1.55;

            host.Children.Add(Box(0, 0, _w, headH, BandBrush, 1.0));
            host.Children.Add(CopyLine("SHOP AT HOME", titleSize, GoldBrush, Math.Max(0, (headH - titleSize * 1.35) / 2)));

            // ---- the pedestal ------------------------------------------------------------------
            // The campus cluster (item top at cy - 20 through the base bar's bottom at cy + 30) is
            // 50 tall. Fit that block into whatever the bands and the type left behind and centre
            // it, so the item shrinks rather than the copy when the user drags her small.
            double stageTop = headH + _k;
            double stageH = Math.Max(6 * _k, labelTop - stageTop - _k);
            _ks = Math.Min(_k, stageH / 52.0);
            _cx = _w / 2;
            _cy = stageTop + (stageH - 50 * _ks) / 2 + 20 * _ks;

            _base.Width = Math.Max(2, 52 * _ks);
            _base.Height = Math.Max(1.5, 6 * _ks);
            _base.Fill = BandBrush;
            _base.IsHitTestVisible = false;
            Canvas.SetLeft(_base, _cx - _base.Width / 2);
            Canvas.SetTop(_base, _cy + 24 * _ks);
            host.Children.Add(_base);

            // The body and its cut-outs are built once and only ever resized: the spin is four
            // widths on the same rectangles, never four rectangles.
            double bodyH;
            switch (_kind)
            {
                case Kind.Card: _bodyW = 34 * _ks; bodyH = 40 * _ks; _body.Fill = CreamInk; break;
                case Kind.Watch: _bodyW = 30 * _ks; bodyH = 32 * _ks; _body.Fill = GoldBrush; break;
                default: _bodyW = 36 * _ks; bodyH = 36 * _ks; _body.Fill = PinkBrush; break;
            }
            _body.Height = Math.Max(2, bodyH);
            _body.Width = Math.Max(1, _bodyW);
            _body.IsHitTestVisible = false;
            Canvas.SetTop(_body, _cy - _body.Height / 2);
            Canvas.SetLeft(_body, _cx - _body.Width / 2);
            host.Children.Add(_body);

            int details = _kind == Kind.Card ? 4 : 1;
            for (int i = 0; i < details; i++)
            {
                // The card's pips read as a punched loyalty card; the watch's and the disc's single
                // cut-out is the face and the hole. Both are the item's own dark, so the shape reads
                // as one object rather than as two stacked blocks.
                var cut = Box(0, 0, 1, 1, _kind == Kind.Disc ? BgBrush : BandBrush, 1.0);
                _detail.Add(cut);
                host.Children.Add(cut);
            }

            host.Children.Add(CopyLine(_label, labelSize, PlumBrush, labelTop));

            _price.Text = _start.ToString(CultureInfo.InvariantCulture) + " SP";
            _shownPrice = _start;
            StyleLine(_price, priceSize, GoldBrush, priceTop);
            host.Children.Add(_price);

            // ---- CALL NOW ----------------------------------------------------------------------
            _foot.Width = _w;
            _foot.Height = footH;
            _foot.Fill = CoralBrush;
            _foot.IsHitTestVisible = false;
            Canvas.SetLeft(_foot, 0);
            Canvas.SetTop(_foot, _h - footH);
            host.Children.Add(_foot);

            _call.Text = "CALL NOW";
            StyleLine(_call, callSize, DarkInkBrush, _h - footH + Math.Max(0, (footH - callSize * 1.35) / 2));
            host.Children.Add(_call);

            // The scanline mask every campus channel wears, as ONE tiled brush instead of seventy
            // rectangles: the glass is short enough that a per line loop is affordable, but this
            // painter already mutates a rectangle a frame and the mask never moves.
            host.Children.Add(new Rectangle
            {
                Width = _w,
                Height = _h,
                Fill = ScanMask(Math.Max(2.0, 2 * _k)),
                IsHitTestVisible = false
            });

            Paint(0);
        }

        public override void Tick(double tMs) => Paint(tMs);

        private void Paint(double tMs)
        {
            // THE SPIN. The campus turns the item by squeezing it horizontally through four frames
            // rather than by rotating it, because an 8 px pixel object that rotates smoothly stops
            // looking like a pixel object. Frames 1 and 3 share a width, so the guard in SetWidth
            // makes half the frames free.
            SetWidth(Squeeze[(int)(tMs / SpinStepMs) & 3]);

            int price = Math.Max(FloorPrice, _start - (int)(tMs / PriceTickMs) * _drop);
            if (price != _shownPrice)
            {
                // Only on a change: at 30 fps the price moves about every third frame, and a string
                // per frame for a number that did not move is the allocation storm the painter laws
                // are about.
                _shownPrice = price;
                _price.Text = price.ToString(CultureInfo.InvariantCulture) + " SP";
            }

            // THE FLOOR IS THE PAYOFF. Once the price cannot fall any further the bar doubles its
            // blink and takes the price with it, so the ten seconds end on a beat rather than on a
            // number that simply stopped. The campus never needed this: its shop sits on the glass
            // as long as the takeover lasts and the price is still falling when it goes.
            bool floored = price <= FloorPrice;
            bool lit = ((int)(tMs / (floored ? FloorBlinkMs : BlinkMs)) & 1) == 0;
            if (lit == _lit && tMs > 0) return;
            _lit = lit;
            _foot.Fill = lit ? CoralBrush : DeadBarBrush;
            _call.Foreground = lit ? DarkInkBrush : DeadInkBrush;
            _price.Opacity = floored && !lit ? 0.45 : 1.0;
        }

        /// <summary>Resize the item and its cut-outs to this frame of the turn.</summary>
        private void SetWidth(double squeeze)
        {
            double w = Math.Max(2 * _ks, _bodyW * squeeze);
            if (Math.Abs(w - _lastW) < 0.05) return;
            _lastW = w;

            _body.Width = w;
            Canvas.SetLeft(_body, _cx - w / 2);
            double left = _cx - w / 2;

            switch (_kind)
            {
                case Kind.Card:
                    for (int i = 0; i < _detail.Count; i++)
                    {
                        // CAMPUS draws all four pips whenever the card is wider than 8, which spills
                        // the last of them off a squeezed card. Per pip fitting instead: they vanish
                        // into the edge as it turns, which is what a punched card actually does.
                        double px = (4 + i * 7) * _ks;
                        Show(_detail[i], px + 3 * _ks <= w, left + px, _cy - 12 * _ks, 3 * _ks, 3 * _ks);
                    }
                    break;
                case Kind.Watch:
                    Show(_detail[0], w > 10 * _ks, left + 4 * _ks, _cy - 12 * _ks, w - 8 * _ks, 24 * _ks);
                    break;
                default:
                    Show(_detail[0], w > 12 * _ks, _cx - w / 4, _cy - 9 * _ks, w / 2, 18 * _ks);
                    break;
            }
        }

        private static void Show(Rectangle r, bool on, double x, double y, double w, double h)
        {
            r.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;
            r.Width = Math.Max(0.5, w);
            r.Height = Math.Max(0.5, h);
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
        }

        /// <summary>A whole pixel size that both scales and fits the longest string on its line.</summary>
        private static double PixelSize(double wanted, double fits, double floor) =>
            Math.Max(floor, Math.Round(Math.Min(wanted, fits)));

        /// <summary>
        /// One centred line of TV copy. Centring is a full width TextBlock with
        /// <see cref="TextAlignment.Center"/> rather than a measured offset, so the price can change
        /// its digit count mid channel without a Measure pass per frame.
        /// </summary>
        private TextBlock CopyLine(string text, double size, Brush ink, double top)
        {
            var tb = new TextBlock { Text = text };
            StyleLine(tb, size, ink, top);
            return tb;
        }

        private void StyleLine(TextBlock tb, double size, Brush ink, double top)
        {
            tb.FontFamily = EmiFace.PixelFont;
            tb.FontSize = size;
            tb.Foreground = ink;
            tb.Width = _w;
            tb.TextAlignment = TextAlignment.Center;
            tb.IsHitTestVisible = false;
            Canvas.SetLeft(tb, 0);
            Canvas.SetTop(tb, top);
        }

        /// <summary>
        /// CAMPUS scanlines(g, 0.18): a one px black line every two px. 0x2E is that alpha, and the
        /// tile is stretched with the glass so the pitch stays a scanline rather than becoming a
        /// grille on a big window.
        /// </summary>
        private static DrawingBrush ScanMask(double pitch)
        {
            var line = new GeometryDrawing(
                Freeze(new SolidColorBrush(Color.FromArgb(0x2E, 0, 0, 0))),
                null,
                new RectangleGeometry(new Rect(0, 0, 2, 1)));
            return Freeze(new DrawingBrush(line)
            {
                TileMode = TileMode.Tile,
                Viewbox = new Rect(0, 0, 2, 2),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, pitch, pitch),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill
            });
        }
    }
}
