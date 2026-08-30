using System;
using System.Collections.Generic;
using System.Windows.Media;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// ONE 16 x 16 ICON PER CARD, for the book's side rail.
///
/// <para><b>Why a rail exists at all.</b> The book was four cards and a row of dots. At twenty-two
/// the dots stopped being navigation and became a progress bar: eight identical squares that tell
/// you where you are and nothing about where you could go, so getting to a card you half-remember
/// meant stepping through the chapter one flip at a time. The rail names the destinations. The
/// owner's word for it, 2026-08-30: "it seems kinda hard to navigate, maybe add a sidebar with the
/// little icons of the features, so we can nav easily between them".</para>
///
/// <para><b>The rail is scoped to the current tab, never the whole book.</b> Twenty-two icons in a
/// column would not fit above the pager without shrinking them past reading size, and it would put
/// the chapter boundaries somewhere the eye has to hunt for them. The tab strip already picks the
/// chapter; the rail picks the page inside it, so it never holds more than eight.</para>
///
/// <para><b>These are drawn, not loaded.</b> Same rule as the demo loops and for the same reason:
/// no bitmap ships, nothing is generated, and the house palette is the only palette. Sixteen cells
/// is small enough that a glyph has room for exactly one idea, which is a useful discipline - if an
/// icon needs a second shape to be read, the card behind it is probably two cards.</para>
///
/// <para>The buffer is cleared to zero rather than to a colour, so a glyph is transparent where it
/// is not drawn and the rail chip's own background (and its lit pink state) shows through. Do not
/// clear these to <see cref="EmiPix.Ink"/>: the selected chip would then be a pink square with a
/// dark tile sitting on it.</para>
/// </summary>
internal static class EmiBookGlyphs
{
    /// <summary>Cells per side. Drawn at 2x in the rail, so 32 device pixels at 100% scaling.</summary>
    public const int Size = 16;

    private static Dictionary<string, ImageSource>? _cache;

    /// <summary>
    /// Every id this file draws, for the test that says the rail has no holes.
    ///
    /// <para>It reads the painter table rather than the built cache on purpose: rasterising twenty-two
    /// buffers costs a WPF imaging stack a headless test runner has no reason to spin up, and the
    /// question the test asks is whether a card was FORGOTTEN, which the table answers on its own.</para>
    /// </summary>
    public static IReadOnlyList<string> Ids
    {
        get
        {
            var ids = new List<string>(Painters.Length);
            foreach (var (id, _) in Painters) ids.Add(id);
            return ids;
        }
    }

    /// <summary>
    /// The icon for a card id, or null if that card has none. Built once and held: a rail chip is
    /// rebuilt on every card change, and re-rasterising twenty-two buffers per flip would be work
    /// for nothing since the glyphs never animate.
    /// </summary>
    public static ImageSource? For(string id)
    {
        try
        {
            _cache ??= Build();
            return _cache.TryGetValue(id, out var src) ? src : null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] book glyph lookup failed for {Card}", id);
            return null;
        }
    }

    private static Dictionary<string, ImageSource> Build()
    {
        var d = new Dictionary<string, ImageSource>(StringComparer.Ordinal);
        foreach (var (id, draw) in Painters)
        {
            try
            {
                var p = new EmiPixelCanvas(Size, Size);
                p.Clear(0);            // transparent, so the lit chip shows through
                draw(p);
                p.Commit();
                d[id] = p.Source;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] book glyph draw failed for {Card}", id);
            }
        }
        return d;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A filled disc. There is no circle in the props kit because nothing at 96 x 72
    /// needed one; at 16 cells a round shape is the only way an eye or a dial reads.</summary>
    private static void Disc(EmiPixelCanvas p, int cx, int cy, double r, uint c)
    {
        int ri = (int)Math.Ceiling(r);
        for (int y = cy - ri; y <= cy + ri; y++)
        for (int x = cx - ri; x <= cx + ri; x++)
        {
            double dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy <= r * r) p.Px(x, y, c);
        }
    }

    /// <summary>A triangle pointing right, for anything that means "play".</summary>
    private static void PlayHead(EmiPixelCanvas p, int x, int y, int h, uint c)
    {
        for (int i = 0; i * 2 < h; i++) p.Rect(x + i, y + i, 1, h - i * 2, c);
    }

    /// <summary>A triangle pointing down, for anything that means "into".</summary>
    private static void DownHead(EmiPixelCanvas p, int cx, int y, int w, uint c)
    {
        for (int i = 0; w - i * 2 > 0; i++) p.Rect(cx - (w - i * 2) / 2, y + i, w - i * 2, 1, c);
    }

    // ------------------------------------------------------------------ the glyphs

    private static readonly (string Id, Action<EmiPixelCanvas> Draw)[] Painters =
    {
        // ---- START ---------------------------------------------------------
        ("the-ccp", p =>
        {
            p.Rect(1, 2, 14, 10, EmiPix.Mid);
            p.Rect(2, 3, 12, 8, EmiPix.Ink);
            p.Rect(2, 3, 12, 2, EmiPix.Pink);      // the app is the thing with the pink bar on it
            p.Rect(6, 12, 4, 2, EmiPix.Mid);
            p.Rect(4, 14, 8, 1, EmiPix.Mid);
        }),

        ("the-panic-key", p =>
        {
            p.Rect(2, 4, 12, 8, EmiPixelCanvas.Rgb(0x8F, 0x8A, 0x7D));   // the key's side
            p.Rect(2, 3, 12, 7, EmiPix.Cream);                            // its face
            p.Rect(6, 5, 4, 3, EmiPix.Pink);
        }),

        ("your-media", p =>
        {
            p.Rect(2, 2, 6, 2, EmiPixelCanvas.Rgb(0xC9, 0x9B, 0x34));     // the tab
            p.Rect(2, 4, 12, 9, EmiPix.Gold);
            p.Rect(4, 6, 8, 5, EmiPix.Lav);                               // a picture inside it
            p.Rect(9, 7, 2, 2, EmiPix.Cream);
            p.Rect(4, 9, 8, 2, EmiPixelCanvas.Rgb(0x6A, 0x5C, 0xA8));
        }),

        ("sessions", p =>
        {
            p.Rect(2, 3, 10, 2, EmiPix.Pink);      // three booked lanes, one clock
            p.Rect(2, 7, 6, 2, EmiPix.Lav);
            p.Rect(2, 11, 12, 2, EmiPix.Pink);
            p.Rect(10, 1, 1, 14, EmiPix.Cream);    // the playhead crossing them
        }),

        ("progression", p =>
        {
            p.Rect(1, 14, 14, 1, EmiPix.Mid);
            p.Rect(2, 10, 3, 4, EmiPix.Lav);
            p.Rect(6, 7, 3, 7, EmiPix.Pink);
            p.Rect(10, 3, 3, 11, EmiPix.Gold);
        }),

        ("the-desk", p =>
        {
            p.Rect(3, 2, 10, 4, EmiPix.Pink);      // her, and she is the only face in the book
            p.Rect(3, 5, 1, 5, EmiPix.Pink);
            p.Rect(12, 5, 1, 5, EmiPix.Pink);
            p.Rect(4, 5, 8, 6, EmiPix.Cream);
            p.Rect(5, 7, 2, 2, EmiPix.Ink);
            p.Rect(9, 7, 2, 2, EmiPix.Ink);
            p.Rect(5, 11, 6, 4, EmiPix.Pink);
        }),

        // ---- TOOLS ---------------------------------------------------------
        ("flashes", p =>
        {
            p.Rect(3, 4, 10, 8, EmiPix.Cream);
            p.Rect(4, 5, 8, 6, EmiPix.Lav);
            // four sparks, because a flash is a picture that ARRIVED rather than a picture
            p.Rect(1, 2, 2, 1, EmiPix.Pink); p.Rect(1, 2, 1, 2, EmiPix.Pink);
            p.Rect(13, 2, 2, 1, EmiPix.Pink); p.Rect(14, 2, 1, 2, EmiPix.Pink);
            p.Rect(1, 13, 2, 1, EmiPix.Pink); p.Rect(1, 12, 1, 2, EmiPix.Pink);
            p.Rect(13, 13, 2, 1, EmiPix.Pink); p.Rect(14, 12, 1, 2, EmiPix.Pink);
        }),

        ("subliminals", p =>
        {
            p.Rect(2, 4, 12, 8, EmiPix.Cream);     // a card of words you do not get to read
            p.Rect(3, 6, 5, 1, EmiPix.Ink);
            p.Rect(9, 6, 3, 1, EmiPix.Ink);
            p.Rect(3, 8, 7, 1, EmiPix.Ink);
            p.Rect(3, 10, 4, 1, EmiPix.Ink);
        }),

        ("videos", p =>
        {
            p.Rect(1, 3, 14, 10, EmiPix.Mid);
            p.Rect(2, 4, 12, 8, EmiPix.Ink);
            PlayHead(p, 6, 5, 7, EmiPix.Cream);
        }),

        ("whispers", p =>
        {
            p.Rect(3, 6, 2, 4, EmiPix.Cream);      // a cone, then two arcs leaving it
            for (int i = 0; i < 3; i++) p.Rect(5 + i, 5 - i, 1, 6 + i * 2, EmiPix.Cream);
            p.Rect(10, 6, 1, 4, EmiPix.Pink);
            p.Rect(12, 4, 1, 8, EmiPix.Pink);
        }),

        ("spiral", p =>
        {
            EmiPix.Spiral(p, 8, 8, 7.0, 0.0, EmiPix.Pink);
            EmiPix.Spiral(p, 8, 8, 7.0, Math.PI, EmiPix.Lav);
        }),

        ("overlays", p =>
        {
            p.Rect(1, 2, 10, 8, EmiPix.Mid);       // three sheets, and they stack
            p.Rect(3, 4, 10, 8, EmiPix.Lav);
            p.Rect(5, 6, 10, 8, EmiPix.Pink);
        }),

        ("bubbles", p =>
        {
            Disc(p, 5, 10, 3.4, EmiPix.Lav);
            p.Rect(4, 8, 1, 1, EmiPix.Cream);
            Disc(p, 11, 6, 2.6, EmiPix.Pink);
            p.Rect(10, 4, 1, 1, EmiPix.Cream);
            Disc(p, 12, 12, 1.8, EmiPix.Lav);
        }),

        ("corner-gifs", p =>
        {
            p.Rect(1, 3, 14, 10, EmiPix.Mid);
            p.Rect(2, 4, 12, 8, EmiPix.Ink);
            p.Rect(3, 8, 4, 4, EmiPix.Pink);       // the whole point is WHICH corner
        }),

        // ---- DEEPER --------------------------------------------------------
        ("deeper", p =>
        {
            p.Rect(7, 1, 2, 8, EmiPix.Pink);       // something going down into a track
            DownHead(p, 8, 9, 7, EmiPix.Pink);
            p.Rect(1, 13, 14, 2, EmiPix.Mid);
            p.Rect(3, 13, 2, 2, EmiPix.Gold);
            p.Rect(11, 13, 2, 2, EmiPix.Gold);
        }),

        ("companion", p =>
        {
            p.Rect(2, 3, 12, 8, EmiPix.Cream);     // a bubble, since she is the one who talks
            p.Rect(4, 11, 3, 2, EmiPix.Cream);
            p.Rect(4, 13, 1, 1, EmiPix.Cream);
            p.Rect(5, 6, 2, 2, EmiPix.Pink);
            p.Rect(9, 6, 2, 2, EmiPix.Pink);
        }),

        ("awareness", p =>
        {
            // an eye, drawn as a lens rather than a circle so it cannot be mistaken for the vault
            for (int x = 2; x <= 13; x++)
            {
                int h = (int)Math.Round(4.2 * Math.Sin(Math.PI * (x - 2) / 11.0));
                if (h > 0) p.Rect(x, 8 - h, 1, h * 2, EmiPix.Cream);
            }
            Disc(p, 8, 8, 3.2, EmiPix.Pink);
            Disc(p, 8, 8, 1.4, EmiPix.Ink);
        }),

        ("lockdown", p =>
        {
            p.Rect(5, 2, 1, 5, EmiPix.Cream);      // a shut padlock
            p.Rect(10, 2, 1, 5, EmiPix.Cream);
            p.Rect(6, 1, 4, 1, EmiPix.Cream);
            p.Rect(3, 6, 10, 8, EmiPix.Gold);
            p.Rect(7, 9, 2, 3, EmiPix.Ink);
        }),

        ("takeover", p =>
        {
            EmiPix.Cursor(p, 5, 4);                // a cursor that is moving without you
            p.Rect(1, 2, 3, 1, EmiPix.Pink);
            p.Rect(12, 4, 3, 1, EmiPix.Pink);
            p.Rect(2, 12, 3, 1, EmiPix.Pink);
            p.Rect(11, 13, 3, 1, EmiPix.Pink);
        }),

        ("arcademy", p =>
        {
            p.Rect(6, 2, 4, 1, EmiPix.Pink);       // a mortarboard, seen head on
            p.Rect(4, 3, 8, 1, EmiPix.Pink);
            p.Rect(2, 4, 12, 1, EmiPix.Pink);
            p.Rect(4, 5, 8, 1, EmiPix.Pink);
            p.Rect(6, 6, 4, 1, EmiPix.Pink);
            p.Rect(5, 7, 6, 3, EmiPix.Lav);
            p.Rect(13, 4, 1, 6, EmiPix.Gold);
            p.Rect(12, 10, 2, 1, EmiPix.Gold);
        }),

        ("the-games", p =>
        {
            p.Rect(3, 1, 10, 14, EmiPix.Mid);      // a cabinet, because these are whole games
            p.Rect(4, 3, 8, 5, EmiPix.Pink);
            p.Rect(5, 10, 6, 1, EmiPix.Ink);
            p.Rect(5, 12, 2, 2, EmiPix.Cream);
            p.Rect(9, 12, 2, 2, EmiPix.Cream);
        }),

        ("vault", p =>
        {
            p.Rect(2, 3, 12, 10, EmiPixelCanvas.Rgb(0xC9, 0x9B, 0x34));   // a door, not a padlock
            p.Rect(3, 4, 10, 8, EmiPix.Gold);
            Disc(p, 8, 8, 3.2, EmiPix.Cream);
            Disc(p, 8, 8, 1.2, EmiPix.Ink);
            p.Rect(12, 7, 2, 2, EmiPix.Cream);
        }),
    };
}
