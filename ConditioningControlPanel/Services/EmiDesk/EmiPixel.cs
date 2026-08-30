using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// A tiny software framebuffer, and the ten props every book demo is built from.
///
/// <para><b>Why a bitmap and not shapes.</b> Her glass channels (<see cref="EmiChannelPainter"/>)
/// draw with WPF <c>Shape</c>s because they draw a handful of big things. A book demo is a 96 x 72
/// pixel screen, and a shape per pixel is 6,912 visuals on a 30 fps clock. One
/// <see cref="WriteableBitmap"/> written as a <c>uint</c> array and blitted once per frame is two
/// orders of magnitude cheaper and, more to the point, is the only way to get pixels that are
/// actually square: the <c>Image</c> is scaled 3x with
/// <see cref="BitmapScalingMode.NearestNeighbor"/>, so one buffer cell is exactly nine screen
/// pixels with no filtering and no half-pixel seams.</para>
///
/// <para><b>Format is Bgra32</b>, so a colour packs as <c>(a &lt;&lt; 24) | (r &lt;&lt; 16) |
/// (g &lt;&lt; 8) | b</c>: written little-endian that lays the bytes down B, G, R, A, which is what
/// the format wants. Get this backwards and everything comes out with the reds and blues swapped,
/// which on this palette reads as "the pink went cyan".</para>
///
/// <para>Every draw call clips. Nothing in here throws on an off-screen rectangle, because a demo
/// loop that has to bounds-check by hand is a demo loop with an out-of-range bug in it.</para>
/// </summary>
public sealed class EmiPixelCanvas
{
    /// <summary>Buffer width in cells.</summary>
    public int W { get; }

    /// <summary>Buffer height in cells.</summary>
    public int H { get; }

    private readonly WriteableBitmap _bmp;
    private readonly uint[] _buf;
    private readonly System.Windows.Int32Rect _all;

    /// <summary>Builds a buffer. 96 x 72 is the book's 4:3 mini screen.</summary>
    public EmiPixelCanvas(int w, int h)
    {
        W = Math.Max(1, w);
        H = Math.Max(1, h);
        _buf = new uint[W * H];
        _bmp = new WriteableBitmap(W, H, 96, 96, PixelFormats.Bgra32, null);
        _all = new System.Windows.Int32Rect(0, 0, W, H);
    }

    /// <summary>The image source to hang on an <c>Image</c>. Stable for the canvas's life.</summary>
    public ImageSource Source => _bmp;

    /// <summary>Pack a colour for this buffer.</summary>
    public static uint Rgb(byte r, byte g, byte b) => 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;

    // ---------------------------------------------------------------- primitives

    /// <summary>Fill the whole buffer.</summary>
    public void Clear(uint c)
    {
        for (int i = 0; i < _buf.Length; i++) _buf[i] = c;
    }

    /// <summary>One cell. Silently ignores anything outside the buffer.</summary>
    public void Px(int x, int y, uint c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        _buf[y * W + x] = c;
    }

    /// <summary>A filled rectangle, clipped.</summary>
    public void Rect(double dx, double dy, double dw, double dh, uint c)
    {
        int x = (int)Math.Round(dx), y = (int)Math.Round(dy);
        int w = (int)Math.Round(dw), h = (int)Math.Round(dh);
        if (w <= 0 || h <= 0) return;

        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(W, x + w), y1 = Math.Min(H, y + h);
        for (int yy = y0; yy < y1; yy++)
        {
            int row = yy * W;
            for (int xx = x0; xx < x1; xx++) _buf[row + xx] = c;
        }
    }

    /// <summary>
    /// A rectangle blended over what is already there. Used for the pink wash and for the fade a
    /// flash leaves on its way out; <paramref name="a"/> is 0 to 1.
    /// </summary>
    public void RectA(double dx, double dy, double dw, double dh, uint c, double a)
    {
        if (a >= 0.999) { Rect(dx, dy, dw, dh, c); return; }
        if (a <= 0.001) return;

        int x = (int)Math.Round(dx), y = (int)Math.Round(dy);
        int w = (int)Math.Round(dw), h = (int)Math.Round(dh);
        if (w <= 0 || h <= 0) return;

        int sr = (int)((c >> 16) & 0xFF), sg = (int)((c >> 8) & 0xFF), sb = (int)(c & 0xFF);
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(W, x + w), y1 = Math.Min(H, y + h);
        for (int yy = y0; yy < y1; yy++)
        {
            int row = yy * W;
            for (int xx = x0; xx < x1; xx++)
            {
                uint d = _buf[row + xx];
                int dr = (int)((d >> 16) & 0xFF), dg = (int)((d >> 8) & 0xFF), db = (int)(d & 0xFF);
                byte r = (byte)(dr + (sr - dr) * a);
                byte g = (byte)(dg + (sg - dg) * a);
                byte b = (byte)(db + (sb - db) * a);
                _buf[row + xx] = Rgb(r, g, b);
            }
        }
    }

    /// <summary>A one-cell line, stepped rather than Bresenham. Good enough at this size.</summary>
    public void Line(double x0, double y0, double x1, double y1, uint c)
    {
        double dx = x1 - x0, dy = y1 - y0;
        int n = (int)Math.Max(1, Math.Round(Math.Max(Math.Abs(dx), Math.Abs(dy))));
        for (int i = 0; i <= n; i++)
            Px((int)Math.Round(x0 + dx * i / n), (int)Math.Round(y0 + dy * i / n), c);
    }

    /// <summary>Push the buffer to the bitmap. Once per frame, on the dispatcher.</summary>
    public void Commit()
    {
        try { _bmp.WritePixels(_all, _buf, W * 4, 0); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] book frame blit failed"); }
    }
}

/// <summary>
/// THE PROPS KIT: one stage and ten props, and every demo in the book is made of these.
///
/// <para>Nothing bespoke, nothing generated, no bitmaps at all. House palette only, hard edges,
/// integer coordinates. If a card needs an eleventh prop, the card is wrong, not the kit.</para>
///
/// <para>The deliberately unreadable <see cref="Phrase"/> block glyphs are load-bearing rather than
/// lazy. Real words in a subliminal demo would need the trigger fence applied to them and would
/// need translating into nine languages; blocks say "a phrase went past" in every language and
/// carry no text at all.</para>
/// </summary>
public static class EmiPix
{
    // ---- the palette, and it is the app's own -------------------------------

    /// <summary>The desk ground, <c>#1A1A2E</c>.</summary>
    public static readonly uint Navy = EmiPixelCanvas.Rgb(0x1A, 0x1A, 0x2E);

    /// <summary>The deepest ink, <c>#0E0E1C</c>. Taskbars, window wells, the dead screen.</summary>
    public static readonly uint Ink = EmiPixelCanvas.Rgb(0x0E, 0x0E, 0x1C);

    /// <summary>Her pink, <c>#FF69B4</c>.</summary>
    public static readonly uint Pink = EmiPixelCanvas.Rgb(0xFF, 0x69, 0xB4);

    /// <summary>Cream, <c>#F5F0E1</c>. Cursors, keycaps, phrase cards.</summary>
    public static readonly uint Cream = EmiPixelCanvas.Rgb(0xF5, 0xF0, 0xE1);

    /// <summary>Lavender, <c>#B9A7F5</c>. The second voice.</summary>
    public static readonly uint Lav = EmiPixelCanvas.Rgb(0xB9, 0xA7, 0xF5);

    /// <summary>Gold, <c>#F0C050</c>. Locks, ramps, the catch.</summary>
    public static readonly uint Gold = EmiPixelCanvas.Rgb(0xF0, 0xC0, 0x50);

    /// <summary>The mid tone that reads as chrome.</summary>
    public static readonly uint Mid = EmiPixelCanvas.Rgb(0x34, 0x34, 0x4F);

    /// <summary>Dark fill inside a pix-image.</summary>
    public static readonly uint Dark = EmiPixelCanvas.Rgb(0x10, 0x10, 0x22);

    private static readonly uint Rust = EmiPixelCanvas.Rgb(0x4A, 0x2D, 0x43);

    // ---- the stage -----------------------------------------------------------

    /// <summary>
    /// The mini desktop ten of the demos share: navy ground, a hairline top edge, and an optional
    /// taskbar strip with three window buttons on it. Drawing this first is what lets every prop
    /// below assume an opaque background and skip alpha entirely.
    /// </summary>
    public static void Desk(EmiPixelCanvas p, bool taskbar = true)
    {
        p.Clear(Navy);
        p.Rect(0, 0, p.W, 1, EmiPixelCanvas.Rgb(0x24, 0x24, 0x40));
        if (!taskbar) return;
        p.Rect(0, p.H - 6, p.W, 6, Ink);
        p.Rect(2, p.H - 5, 9, 4, Mid);
        p.Rect(13, p.H - 5, 5, 4, Mid);
        p.Rect(20, p.H - 5, 5, 4, Mid);
    }

    // ---- the ten props -------------------------------------------------------

    /// <summary>
    /// PROP 1, pix-image. A framed abstract inside a one-cell pink border. It reads as "a picture"
    /// without being one, which is the whole point: the app shows the user's own content and the
    /// book must not appear to ship any.
    ///
    /// <para><b>The seed picks a COMPOSITION, not a dither offset.</b> The first cut shifted one
    /// diagonal dither by the seed, and five of those on a stage read as five tiles of the same
    /// wallpaper rather than as five different pictures - which quietly undoes the sentence the
    /// flashes loop is trying to say. Five motifs, because the busiest stage in the book (THE CCP)
    /// puts five pictures up at once and a repeat among them would say the same thing again.</para>
    /// </summary>
    public static void PixImage(EmiPixelCanvas p, int x, int y, int w, int h, int seed, double alpha = 1.0)
    {
        if (w < 3 || h < 3) return;
        if (alpha < 0.999)
        {
            // A fading picture keeps its dominant tone rather than collapsing to a flat plate: on
            // the flashes stage the same image fades in and out repeatedly, and an image that
            // changed colour on the way out would read as a different image arriving.
            uint tone = (((seed % 5) + 5) % 5) switch
            {
                0 => Lav,
                1 => Mid,
                2 => Rust,
                3 => Lav,
                _ => Rust,
            };
            p.RectA(x, y, w, h, Pink, alpha);
            p.RectA(x + 1, y + 1, w - 2, h - 2, tone, alpha);
            return;
        }
        p.Rect(x, y, w, h, Pink);

        int ix = x + 1, iy = y + 1, iw = w - 2, ih = h - 2;
        p.Rect(ix, iy, iw, ih, Dark);

        switch (((seed % 5) + 5) % 5)
        {
            case 0:   // a horizon, with a sun low in it
                p.Rect(ix, iy, iw, ih * 0.55, Lav);
                p.Rect(ix, iy + ih * 0.55, iw, ih * 0.45, Rust);
                p.Rect(ix + iw * 0.62, iy + ih * 0.22, 4, 3, Gold);
                p.Rect(ix + iw * 0.66, iy + ih * 0.16, 2, 5, Gold);
                break;

            case 1:   // a figure: shoulders and a head, centred
                p.Rect(ix, iy, iw, ih, Mid);
                p.Rect(ix + iw / 2 - 3, iy + 2, 6, 5, Rust);
                p.Rect(ix + iw / 2 - 6, iy + 8, 12, ih - 8, Rust);
                break;

            case 2:   // three bands
                p.Rect(ix, iy, iw, ih / 3.0, Rust);
                p.Rect(ix, iy + ih / 3.0, iw, ih / 3.0, Lav);
                p.Rect(ix, iy + ih * 2 / 3.0, iw, ih / 3.0, Mid);
                break;

            case 3:   // quartered blocks
                p.Rect(ix, iy, iw / 2.0, ih / 2.0, Lav);
                p.Rect(ix + iw / 2.0, iy, iw / 2.0, ih / 2.0, Mid);
                p.Rect(ix, iy + ih / 2.0, iw / 2.0, ih / 2.0, Rust);
                p.Rect(ix + iw / 2.0, iy + ih / 2.0, iw / 2.0, ih / 2.0, Lav);
                break;

            default:  // upright bars
                for (int b = 0; b < 4; b++)
                    p.Rect(ix + b * iw / 4.0, iy + (b % 2 == 0 ? 0 : 3), iw / 4.0 - 1,
                           ih - (b % 2 == 0 ? 0 : 3), b % 2 == 0 ? Lav : Rust);
                break;
        }
    }

    /// <summary>
    /// PROP 2, phrase card. A cream card carrying two rows of solid block words. Unreadable on
    /// purpose: no trigger text on screen, and nothing to localize.
    /// </summary>
    public static void Phrase(EmiPixelCanvas p, int x, int y, int w, int h)
    {
        p.Rect(x, y, w, h, Cream);
        int[][] rows = { new[] { 5, 7, 4, 6 }, new[] { 6, 4, 8 } };
        int cy = y + 4;
        foreach (var row in rows)
        {
            int cx = x + 4;
            foreach (int word in row)
            {
                if (cx + word > x + w - 3) break;
                p.Rect(cx, cy, word, 3, Ink);
                cx += word + 3;
            }
            cy += 6;
        }
    }

    /// <summary>PROP 3, keycap. Depresses two cells when <paramref name="down"/>.</summary>
    public static void Keycap(EmiPixelCanvas p, int x, int y, bool down)
    {
        int o = down ? 2 : 0;
        p.Rect(x, y + 4, 18, 9, Mid);
        p.Rect(x, y + o, 18, 6, down ? EmiPixelCanvas.Rgb(0xC9, 0xC4, 0xB4) : Cream);
        p.Rect(x + 4, y + 2 + o, 10, 2, Ink);
    }

    /// <summary>PROP 4, dial. A quarter gauge with a needle; <paramref name="frac"/> is 0 to 1.</summary>
    public static void Dial(EmiPixelCanvas p, int cx, int cy, int r, double frac)
    {
        for (double a = Math.PI; a <= Math.PI * 1.52; a += 0.05)
            p.Px((int)Math.Round(cx + Math.Cos(a) * r), (int)Math.Round(cy + Math.Sin(a) * r), Cream);
        double ang = Math.PI + Math.Max(0, Math.Min(1, frac)) * Math.PI * 0.5;
        p.Line(cx, cy, cx + Math.Cos(ang) * (r - 2), cy + Math.Sin(ang) * (r - 2), Pink);
        p.Rect(cx - 1, cy - 1, 3, 3, Pink);
    }

    /// <summary>PROP 5, bar. A timeline, an XP track, a countdown.</summary>
    public static void Bar(EmiPixelCanvas p, int x, int y, int w, int h, double frac, uint col)
    {
        p.Rect(x, y, w, h, Ink);
        p.Rect(x, y, w, 1, Mid);
        p.Rect(x + 1, y + 1, (w - 2) * Math.Max(0, Math.Min(1, frac)), h - 2, col);
    }

    /// <summary>PROP 6, clock. Hands sweep fast; this is a demo, not a watch.</summary>
    public static void Clock(EmiPixelCanvas p, int cx, int cy, int r, double t)
    {
        for (double a = 0; a < Math.PI * 2; a += 0.1)
            p.Px((int)Math.Round(cx + Math.Cos(a) * r), (int)Math.Round(cy + Math.Sin(a) * r), Cream);
        double m = t / 260.0, h = t / 2600.0;
        p.Line(cx, cy, cx + Math.Sin(m) * (r - 3), cy - Math.Cos(m) * (r - 3), Pink);
        p.Line(cx, cy, cx + Math.Sin(h) * (r - 7), cy - Math.Cos(h) * (r - 7), Lav);
    }

    /// <summary>PROP 7, padlock. The shackle lifts four cells when open.</summary>
    public static void Padlock(EmiPixelCanvas p, int x, int y, bool open)
    {
        int sy = open ? y - 4 : y;
        p.Rect(x + 2, sy + 2, 2, 6, Cream);
        p.Rect(x + 9, sy + 2, 2, 6, open ? EmiPixelCanvas.Rgb(0x8F, 0x8A, 0x7D) : Cream);
        p.Rect(x + 3, sy, 7, 2, Cream);
        p.Rect(x, y + 7, 13, 10, Gold);
        p.Rect(x + 1, y + 8, 11, 8, EmiPixelCanvas.Rgb(0xC9, 0x9B, 0x34));
        p.Rect(x + 5, y + 10, 3, 3, Ink);
        p.Rect(x + 6, y + 12, 1, 3, Ink);
    }

    /// <summary>PROP 8, spiral. The same archimedean sweep her glass channel draws, plotted.</summary>
    public static void Spiral(EmiPixelCanvas p, int cx, int cy, double r, double ang, uint col)
    {
        const double turns = Math.PI * 7;
        for (double a = 0; a < turns; a += 0.09)
        {
            double rr = r * (a / turns);
            p.Px((int)Math.Round(cx + Math.Cos(a + ang) * rr),
                 (int)Math.Round(cy + Math.Sin(a + ang) * rr), col);
        }
    }

    /// <summary>PROP 9, wave. A row of bouncing columns; <paramref name="amp"/> ducks it.</summary>
    public static void Wave(EmiPixelCanvas p, int x, int y, int w, int h, double t, double amp, uint col, int n = 8)
    {
        int bw = Math.Max(2, w / Math.Max(1, n));
        for (int i = 0; i < n; i++)
        {
            double hh = Math.Max(1, (0.35 + 0.65 * Math.Abs(Math.Sin(t / 150.0 + i * 0.8))) * h * amp);
            p.Rect(x + i * bw, y + h - hh, bw - 1, hh, col);
        }
    }

    /// <summary>PROP 10, cursor ghost. A cream arrow with a one-cell dark left edge.</summary>
    public static void Cursor(EmiPixelCanvas p, double dx, double dy)
    {
        int x = (int)Math.Round(dx), y = (int)Math.Round(dy);
        int[] rows = { 1, 2, 3, 4, 5, 6, 7, 4, 2, 2 };
        for (int j = 0; j < rows.Length; j++) p.Rect(x, y + j, rows[j], 1, Cream);
        p.Rect(x + 3, y + 7, 1, 3, Cream);
        p.Rect(x, y, 1, 7, Ink);
    }

    /// <summary>A folder, for the one card that has to say "this comes from a folder you chose".</summary>
    public static void Folder(EmiPixelCanvas p, int x, int y)
    {
        p.Rect(x, y, 10, 3, EmiPixelCanvas.Rgb(0xC9, 0xA0, 0x3C));
        p.Rect(x, y + 2, 22, 15, Gold);
        p.Rect(x + 1, y + 4, 20, 12, EmiPixelCanvas.Rgb(0xD2, 0xA8, 0x3F));
    }
}
