using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Board;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Tonight Board's message glow (owner, 2026-10-07): the picture's dominant colour is the
/// field, everything clearly different is the message (ink), ink on the picture's outer ring is
/// the frame. Ink rests higher and glows into the grout; the field sits low and dark.
/// </summary>
public class BoardGlowTests
{
    private static BoardPost Post(string[]? fx = null, int w = 64, int h = 36) =>
        new(3, fx ?? Array.Empty<string>(), null, "lobby", BoardAudience.Everyone, 1, 6, w, h);

    private static int Argb(int a, int r, int g, int b) => unchecked((a << 24) | (r << 16) | (g << 8) | b);

    // ---- sample pictures (the mockup's watch-party sample, ported) ----------------------------

    private static readonly int[] Pal = { 0x120F26, 0xFF4FA8, 0x3CFF7D, 0xFFC94A, 0x5FE3FF, 0x9B7BFF, 0xFFFFFF, 0x2C2556 };

    private static readonly System.Collections.Generic.Dictionary<char, string[]> Font = new()
    {
        ['W'] = new[] { "10001", "10001", "10101", "10101", "01010" }, ['A'] = new[] { "010", "101", "111", "101", "101" },
        ['T'] = new[] { "111", "010", "010", "010", "010" }, ['C'] = new[] { "011", "100", "100", "100", "011" },
        ['H'] = new[] { "101", "101", "111", "101", "101" }, ['P'] = new[] { "110", "101", "110", "100", "100" },
        ['R'] = new[] { "110", "101", "110", "101", "101" }, ['Y'] = new[] { "101", "101", "010", "010", "010" },
        ['S'] = new[] { "011", "100", "010", "001", "110" }, ['I'] = new[] { "111", "010", "010", "010", "111" },
        ['N'] = new[] { "1001", "1101", "1011", "1001", "1001" }, ['E'] = new[] { "111", "100", "110", "100", "111" },
        ['L'] = new[] { "100", "100", "100", "100", "111" }, ['O'] = new[] { "010", "101", "101", "101", "010" },
        ['B'] = new[] { "110", "101", "110", "101", "110" }, ['2'] = new[] { "110", "001", "010", "100", "111" },
        ['1'] = new[] { "010", "110", "010", "010", "111" }, ['0'] = new[] { "010", "101", "101", "101", "010" },
        [':'] = new[] { "0", "1", "0", "1", "0" }, [' '] = new[] { "0", "0", "0", "0", "0" },
    };

    private static void Stamp(int[] buf, string s, int y, int col)
    {
        int width = s.Sum(c => Font[c][0].Length + 1) - 1;
        int x = (64 - width) / 2;
        foreach (char c in s)
        {
            var g = Font[c];
            for (int r = 0; r < g.Length; r++)
                for (int k = 0; k < g[r].Length; k++)
                    if (g[r][k] == '1') buf[(y + r) * 64 + x + k] = col;
            x += g[0].Length + 1;
        }
    }

    private static void Sprite(int[] buf, string[] rows, int x, int y, int col)
    {
        for (int r = 0; r < rows.Length; r++)
            for (int k = 0; k < rows[r].Length; k++)
                if (rows[r][k] == '1') buf[(y + r) * 64 + x + k] = col;
    }

    /// <summary>Palette indices of the mockup's "Watch party sample".</summary>
    internal static int[] WatchPartyIndices()
    {
        var buf = new int[64 * 36];
        for (int x = 0; x < 64; x++) { buf[x] = 1; buf[35 * 64 + x] = 1; }
        for (int y = 0; y < 36; y++) { buf[y * 64] = 1; buf[y * 64 + 63] = 1; }
        for (int x = 2; x < 62; x += 2) { buf[2 * 64 + x] = 7; buf[33 * 64 + x] = 7; }
        int[][] dots = { new[] { 5, 5 }, new[] { 12, 3 }, new[] { 57, 6 }, new[] { 50, 3 }, new[] { 4, 29 }, new[] { 59, 30 }, new[] { 30, 31 }, new[] { 44, 30 }, new[] { 20, 30 } };
        for (int i = 0; i < dots.Length; i++) buf[dots[i][1] * 64 + dots[i][0]] = i % 2 == 1 ? 6 : 3;
        Stamp(buf, "WATCH PARTY", 7, 1);
        Stamp(buf, "SAT 21:00", 15, 2);
        Stamp(buf, "IN THE LOBBY", 24, 4);
        var heart = new[] { "01010", "11111", "01110", "00100" };
        Sprite(buf, heart, 7, 15, 1); Sprite(buf, heart, 52, 15, 1);
        return buf;
    }

    internal static BoardPicture WatchParty(string[]? fx = null)
    {
        var idx = WatchPartyIndices();
        var px = idx.Select(i => unchecked((int)0xFF000000) | Pal[i]).ToArray();
        return BoardPicture.FromPixels(px, 64, 36, Post(fx));
    }

    /// <summary>A light picture: cream field, coloured words, a violet frame and red hearts.</summary>
    internal static BoardPicture LightSample()
    {
        const int cream = 0xF3E9D2;
        var buf = Enumerable.Repeat(cream, 64 * 36).ToArray();
        for (int x = 0; x < 64; x++) { buf[x] = 0x7B4FE0; buf[35 * 64 + x] = 0x7B4FE0; }
        for (int y = 0; y < 36; y++) { buf[y * 64] = 0x7B4FE0; buf[y * 64 + 63] = 0x7B4FE0; }
        Stamp(buf, "OPEN TABLES", 9, 0x1E6FE0);
        Stamp(buf, "IN THE LOBBY", 22, 0xE0307F);
        var heart = new[] { "01010", "11111", "01110", "00100" };
        Sprite(buf, heart, 6, 15, 0xE02838); Sprite(buf, heart, 53, 15, 0xE02838);
        var px = buf.Select(c => unchecked((int)0xFF000000) | c).ToArray();
        return BoardPicture.FromPixels(px, 64, 36, Post());
    }

    private static void SavePng(BoardRaster r, string path)
    {
        var src = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgra32, null, r.Pixels, r.Width * 4);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static int Ch(int rgb, int shift) => (rgb >> shift) & 0xFF;

    private static double Lum(int argb) => BoardFxMath.Luma(Ch(argb, 16), Ch(argb, 8), Ch(argb, 0));

    private static int Tile(int x, int y) => y * 64 + x;

    // ---- message vs field ----------------------------------------------------------------------

    [Fact]
    public void The_field_is_the_dominant_colour_and_the_message_is_ink()
    {
        var pic = WatchParty();
        Assert.Equal(0x120F26, pic.BackgroundRgb);
        Assert.True(pic.HasInk);
        var ro = pic.Role[0];
        Assert.Equal(BoardTileRole.Background, ro[Tile(32, 12)]);      // empty field between the lines
        Assert.Equal(BoardTileRole.Ink, ro[Tile(7 + 1, 15)]);           // a heart tile
        Assert.Equal(BoardTileRole.Ink, ro[Tile(5, 5)]);                // a gold sparkle
        Assert.Equal(BoardTileRole.Ink, ro[Tile(2, 2)]);                // the dim rail dot (#2c2556) is 59 away: message
        Assert.Equal(BoardTileRole.Background, ro[Tile(3, 2)]);         // between two rail dots
        Assert.True(BoardPicture.RgbDistance(0x2C2556, 0x120F26) > BoardPicture.InkDistance);
    }

    [Fact]
    public void A_transparent_picture_has_the_board_base_as_its_field()
    {
        var px = new int[64 * 36];
        for (int i = 0; i < px.Length; i++) px[i] = Argb(0, 255, 255, 255);  // clear, whatever its rgb
        for (int x = 10; x < 30; x++) px[Tile(x, 18)] = Argb(255, 255, 79, 168);
        px[Tile(40, 20)] = Argb(100, 255, 255, 255);                         // mostly clear: field
        var pic = BoardPicture.FromPixels(px, 64, 36, Post());
        Assert.Equal(BoardPicture.BaseRgb, pic.BackgroundRgb);
        Assert.Equal(BoardTileRole.Ink, pic.Role[0][Tile(12, 18)]);
        Assert.Equal(BoardTileRole.Background, pic.Role[0][Tile(12, 19)]);
        Assert.Equal(BoardTileRole.Background, pic.Role[0][Tile(40, 20)]);
    }

    [Fact]
    public void A_light_field_is_found_too_and_its_own_shading_stays_field()
    {
        var pic = LightSample();
        Assert.Equal(0xF3E9D2, pic.BackgroundRgb);
        Assert.Equal(BoardTileRole.Background, pic.Role[0][Tile(32, 30)]);
        Assert.Equal(BoardTileRole.Ink, pic.Role[0][Tile(7, 15)]);            // a red heart
        Assert.Equal(BoardTileRole.Frame, pic.Role[0][Tile(0, 10)]);          // the violet frame

        // A soft shade of the field (20 away) is field; a real stroke is message.
        var px = Enumerable.Repeat(Argb(255, 0xF3, 0xE9, 0xD2), 64 * 36).ToArray();
        px[Tile(20, 10)] = Argb(255, 0xE6, 0xDC, 0xC6);
        px[Tile(21, 10)] = Argb(255, 0x1E, 0x6F, 0xE0);
        var p2 = BoardPicture.FromPixels(px, 64, 36, Post());
        Assert.Equal(BoardTileRole.Background, p2.Role[0][Tile(20, 10)]);
        Assert.Equal(BoardTileRole.Ink, p2.Role[0][Tile(21, 10)]);
    }

    [Fact]
    public void Frame_is_message_on_the_pictures_own_outer_ring()
    {
        // A 32 x 20 picture sits centred at (16, 8): its ring, not the grid's, is the frame.
        var px = Enumerable.Repeat(Argb(255, 18, 15, 38), 32 * 20).ToArray();
        for (int x = 0; x < 32; x++) { px[x] = Argb(255, 255, 79, 168); px[19 * 32 + x] = Argb(255, 255, 79, 168); }
        px[10 * 32 + 0] = Argb(255, 60, 255, 125);   // left edge, middle
        px[10 * 32 + 15] = Argb(255, 60, 255, 125);  // inside
        var pic = BoardPicture.FromPixels(px, 32, 20, Post(w: 32, h: 20));
        Assert.Equal((16, 8), (pic.X0, pic.Y0));
        var ro = pic.Role[0];
        Assert.Equal(BoardTileRole.Frame, ro[Tile(16, 8)]);
        Assert.Equal(BoardTileRole.Frame, ro[Tile(47, 27)]);
        Assert.Equal(BoardTileRole.Frame, ro[Tile(16, 18)]);
        Assert.Equal(BoardTileRole.Ink, ro[Tile(31, 18)]);
        Assert.Equal(BoardTileRole.Background, ro[Tile(17, 18)]);
        Assert.Equal(BoardTileRole.Background, ro[Tile(0, 0)]);      // the board round the picture
        Assert.Equal(BoardTileRole.Background, pic.EmptyRole);
    }

    [Fact]
    public void A_flat_picture_has_no_message_and_keeps_the_plain_look()
    {
        var pic = BoardPicture.Solid(Post(), Argb(255, 255, 79, 168));
        Assert.False(pic.HasInk);
        Assert.All(pic.Role[0], r => Assert.Equal(BoardTileRole.Plain, r));
        var r = new BoardRaster(10);
        r.SetPicture(pic);
        Assert.Equal(0, r.GlowPixels);
    }

    [Fact]
    public void The_scene_hands_out_roles_that_follow_the_wave_and_the_arrival()
    {
        var pic = WatchParty(new[] { "wave" });
        var c = new int[BoardPicture.Tiles]; var z = new float[BoardPicture.Tiles]; var ro = new BoardTileRole[BoardPicture.Tiles];
        BoardScene.Compute(pic, 0, default, 0, 10, null, 0, true, c, z, ro);
        Assert.Equal(pic.Role[0], ro);
        // Mid-arrival a tile not landed yet shows the base and is field, never raised message.
        BoardScene.Compute(pic, 0, default, 0, 0.0, null, 0, false, c, z, ro);
        Assert.All(ro, r => Assert.Equal(BoardTileRole.Background, r));
        // Under the wave a tile takes the role of the tile its colour came from.
        var fx = BoardFxSet.From(pic.Post.Fx);
        BoardScene.Compute(pic, 0, fx, 0.4, 10, null, 0.4, false, c, z, ro);
        for (int x = 0; x < 64; x++)
        {
            int off = BoardFxMath.WaveOffset(0.4, x);
            int y = 16, sy = y - off;
            Assert.Equal(pic.Role[0][Tile(x, sy)], ro[Tile(x, y)]);
        }
    }

    // ---- the glow ------------------------------------------------------------------------------

    [Fact]
    public void The_glow_bake_never_passes_255_and_is_zero_far_from_ink()
    {
        // Worst case for the sum: a field of white message tiles round one field tile.
        var dense = Enumerable.Repeat(Argb(255, 255, 255, 255), 64 * 36).ToArray();
        for (int i = 0; i < 64 * 20; i++) dense[i] = Argb(255, 18, 15, 38); // 20 rows of field beat 16 of white
        var pic = BoardPicture.FromPixels(dense, 64, 36, Post());
        Assert.Equal(0x120F26, pic.BackgroundRgb);
        var layout = new BoardTileLayout(BoardTileLayout.MaxPitch);
        var glow = BoardRaster.BakeGlow(pic, layout);
        Assert.All(glow, g => Assert.Equal(0, g & unchecked((int)0xFF000000)));
        Assert.Contains(glow, g => Ch(g, 16) > 200);

        // One message tile at (5, 5): bright beside it, nothing a few tiles away.
        var px = Enumerable.Repeat(Argb(255, 18, 15, 38), 64 * 36).ToArray();
        px[Tile(5, 5)] = Argb(255, 255, 79, 168);
        var one = BoardPicture.FromPixels(px, 64, 36, Post());
        var r = new BoardRaster(10);
        r.SetPicture(one);
        int Near(int x, int y) => r.GlowAt(x, y);
        Assert.True(Ch(Near(5 * 10 + 5, 6 * 10 + 5), 16) > 20);    // the field tile just below
        Assert.True(Ch(Near(5 * 10 + 9, 5 * 10 + 5), 16) > Ch(Near(7 * 10 + 5, 5 * 10 + 5), 16)); // falls off
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++)
            {
                double dx = Math.Max(0, Math.Max(50 - x, x - 60)) / 10.0, dy = Math.Max(0, Math.Max(50 - y, y - 60)) / 10.0;
                if (Math.Max(dx, dy) > 6) Assert.Equal(0, r.GlowAt(x, y));
            }
        Assert.InRange(r.GlowPixels, 1, 16 * 16 * 100);
    }

    [Fact]
    public void A_frame_tile_glows_stronger_than_a_message_tile()
    {
        var px = Enumerable.Repeat(Argb(255, 18, 15, 38), 64 * 36).ToArray();
        px[Tile(0, 18)] = Argb(255, 60, 255, 125);   // frame (left edge)
        px[Tile(32, 18)] = Argb(255, 60, 255, 125);  // message (inside)
        var pic = BoardPicture.FromPixels(px, 64, 36, Post());
        Assert.Equal(BoardTileRole.Frame, pic.Role[0][Tile(0, 18)]);
        var r = new BoardRaster(10);
        r.SetPicture(pic);
        int frame = Ch(r.GlowAt(15, 185), 8), ink = Ch(r.GlowAt(335, 185), 8);
        Assert.True(frame > ink * 1.15, $"frame {frame} vs ink {ink}");
    }

    [Fact]
    public void The_glow_rises_with_the_arrival_and_breathes_slowly()
    {
        Assert.Equal(0, BoardFxMath.GlowStrength(1, 0, false, true));
        Assert.InRange(BoardFxMath.GlowStrength(0, BoardFxMath.BuildSeconds / 2, false, false), 0.44, 0.46);
        Assert.Equal(BoardFxMath.GlowStill, BoardFxMath.GlowStrength(3.3, 0, true, true));   // still board: steady
        Assert.Equal(BoardFxMath.GlowStill, BoardFxMath.GlowStrength(3.3, 10, false, false)); // Reduced / Off: no breath
        double lo = 1, hi = 0;
        for (double t = 0; t < 8; t += 0.05)
        {
            double s = BoardFxMath.GlowStrength(t, 10, false, true);
            lo = Math.Min(lo, s); hi = Math.Max(hi, s);
            Assert.Equal(s, BoardFxMath.GlowStrength(t + BoardFxMath.GlowBreathPeriod, 10, false, true), 9);
        }
        Assert.InRange(lo, 0.79, 0.81);
        Assert.InRange(hi, 0.99, 1.0);
    }

    [Fact]
    public void The_glow_lights_the_field_but_not_while_tiles_are_still_landing()
    {
        var pic = WatchParty();
        var c = new int[BoardPicture.Tiles]; var z = new float[BoardPicture.Tiles]; var ro = new BoardTileRole[BoardPicture.Tiles];
        BoardScene.Compute(pic, 0, default, 0, 10, null, 0, true, c, z, ro);
        var lit = new BoardRaster(10); lit.SetPicture(pic);
        var dark = new BoardRaster(10); dark.SetPicture(pic);
        lit.Draw(c, z, false, 0, ro, 1.0);
        dark.Draw(c, z, false, 0, ro, 0.0);
        // A field tile under "WATCH PARTY": the glow brightens it, in the words' own pink.
        int at = 6 * 10 * lit.Width + 17 * 10 + 5;   // tile (17, 6), just above the W
        Assert.True(Ch(lit.Pixels[at], 16) > Ch(dark.Pixels[at], 16) + 8);
        Assert.True(Ch(lit.Pixels[at], 16) - Ch(dark.Pixels[at], 16) > Ch(lit.Pixels[at], 8) - Ch(dark.Pixels[at], 8));
    }

    // ---- height and brightness -----------------------------------------------------------------

    [Fact]
    public void The_message_rests_higher_with_a_longer_shadow()
    {
        foreach (int p in new[] { 6, 10, 14 })
        {
            var l = new BoardTileLayout(p);
            Assert.True(l.InkRaise.Dy >= 1);
            Assert.True(l.InkShadow.Dy > l.FieldShadow.Dy, $"pitch {p}");
            Assert.True(l.InkShadow.Dx >= l.FieldShadow.Dx, $"pitch {p}");
        }
        // Drawn: a message tile's face starts above its cell, a field tile's does not.
        var pic = WatchParty();
        var c = new int[BoardPicture.Tiles]; var z = new float[BoardPicture.Tiles]; var ro = new BoardTileRole[BoardPicture.Tiles];
        BoardScene.Compute(pic, 0, default, 0, 10, null, 0, true, c, z, ro);
        var r = new BoardRaster(14);
        r.SetPicture(pic);
        r.Draw(c, z, false, 0, ro, 0);
        var (ox, oy) = r.Layout.TileOrigin(8, 15);       // a heart tile, (7,15)+1 is ink
        Assert.Equal(BoardTileRole.Ink, ro[Tile(8, 15)]);
        var (rx, ry) = r.Layout.InkRaise;
        int above = r.Pixels[(oy - ry) * r.Width + ox + 6];
        Assert.True(Ch(above, 16) > 150, "the raised face covers the row above its cell");
        var (fx, fy) = r.Layout.TileOrigin(32, 12);      // a field tile
        int fieldAbove = r.Pixels[(fy - 1) * r.Width + fx + 6];
        Assert.True(Lum(fieldAbove) < 0.05, "a field tile sits flat in its cell");
        // Its shadow falls on the field below-right, darker than a field tile's own face there.
        var (_, sy) = r.Layout.InkShadow;
        var (bx, by) = r.Layout.TileOrigin(8, 17);        // heart row 17; (8, 18) below it is field
        Assert.Equal(BoardTileRole.Background, ro[Tile(8, 18)]);
        int below = r.Pixels[(by - ry + r.Layout.TileSize + sy - 1) * r.Width + bx + 6];
        Assert.True(Lum(below) < 0.03);
    }

    [Fact]
    public void Field_tiles_recede_and_message_tiles_are_a_touch_brighter()
    {
        int n = 13;
        var (pm, pa) = BoardRaster.BuildBevel(n);
        var (fm, fa) = BoardRaster.BuildBevel(n, BoardRaster.FieldBevel, BoardRaster.FieldDim);
        var (im, ia) = BoardRaster.BuildBevel(n, 1.0, BoardRaster.InkBright);
        double Mean(int[] m, int[] a, int v) => Enumerable.Range(0, n * n).Average(i => Math.Min(255, (v * m[i] >> 8) + a[i]));
        double Spread(int[] m, int[] a, int v)
        {
            var vals = Enumerable.Range(0, n * n).Select(i => Math.Min(255, (v * m[i] >> 8) + a[i])).ToArray();
            return vals.Max() - vals.Min();
        }
        double ratio = Mean(fm, fa, 160) / Mean(pm, pa, 160);
        Assert.InRange(ratio, 0.65, 0.75);
        Assert.True(Spread(fm, fa, 160) < Spread(pm, pa, 160) * 0.6, "the field's bevel is flatter");
        Assert.True(Mean(im, ia, 160) > Mean(pm, pa, 160) * 1.05);
    }

    [Fact]
    public void A_lifted_field_tile_stays_dark_and_rolls_under_the_message()
    {
        var pic = WatchParty();
        var c = new int[BoardPicture.Tiles]; var z = new float[BoardPicture.Tiles]; var ro = new BoardTileRole[BoardPicture.Tiles];
        BoardScene.Compute(pic, 0, default, 0, 10, null, 0, true, c, z, ro);
        // Lift the field tile right under the heart's tip (9, 18 is ink; 9, 19 is field).
        Assert.Equal(BoardTileRole.Ink, ro[Tile(9, 17)]);
        Assert.Equal(BoardTileRole.Background, ro[Tile(9, 19)]);
        z[Tile(9, 19)] = 1f;
        var r = new BoardRaster(14);
        r.SetPicture(pic);
        r.Draw(c, z, false, 0, ro, 1.0);
        var lt = r.Layout.Lifted(9, 19, 1);
        int crest = r.Pixels[(lt.Y + lt.Size / 2) * r.Width + lt.X + lt.Size / 2];
        Assert.True(Lum(crest) < 0.06, $"lifted field tile washed grey: {Lum(crest):F3}");
        // The ink tile above keeps its colour where the crest would have covered it.
        var (ox, oy) = r.Layout.TileOrigin(9, 18);
        Assert.Equal(BoardTileRole.Ink, ro[Tile(9, 18)]);
        int ink = r.Pixels[(oy + r.Layout.TileSize - 3) * r.Width + ox + 4];
        Assert.True(Ch(ink, 16) > 150, "the crest covered the message");

        // A lifted message tile keeps the full lift highlight: brighter than at rest.
        var z2 = new float[BoardPicture.Tiles];
        var rest = new BoardRaster(14); rest.SetPicture(pic); rest.Draw(c, z2, false, 0, ro, 0);
        z2[Tile(9, 17)] = 1f;
        var up = new BoardRaster(14); up.SetPicture(pic); up.Draw(c, z2, false, 0, ro, 0);
        var l2 = up.Layout.Lifted(9, 17, 1);
        var (rox, roy) = rest.Layout.TileOrigin(9, 17);
        var (rx, ry) = rest.Layout.InkRaise;
        double liftedLum = Lum(up.Pixels[(l2.Y + l2.Size / 2) * up.Width + l2.X + l2.Size / 2]);
        double restLum = Lum(rest.Pixels[(roy - ry + 6) * rest.Width + rox - rx + 6]);
        Assert.True(liftedLum > restLum);
    }

    /// <summary>
    /// Desk check, off by default: set CCP_BOARD_SHOTS to a folder and this renders the three
    /// stills and writes the frame cost at pitch 14 + CRT to cost.txt beside them.
    /// </summary>
    [Fact]
    public void Shots_and_frame_cost_for_the_desk()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_BOARD_SHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;

        var c = new int[BoardPicture.Tiles]; var z = new float[BoardPicture.Tiles]; var ro = new BoardTileRole[BoardPicture.Tiles];
        void Shot(BoardPicture pic, string name, BoardFxSet fx, double t, int pitch, bool crt)
        {
            var r = new BoardRaster(pitch);
            r.SetPicture(pic);
            BoardScene.Compute(pic, 0, fx, t, 10, null, t, false, c, z, ro);
            r.Draw(c, z, crt, 0.012, ro, BoardFxMath.GlowStrength(t, 10, false, true));
            SavePng(r, Path.Combine(dir, name));
        }
        var party = WatchParty(new[] { "ola" });
        Shot(party, "glow-watch-party.png", default, 0, 14, false);
        Shot(LightSample(), "glow-light.png", default, 0, 14, false);
        Shot(party, "glow-ola.png", BoardFxSet.From(new[] { "ola" }), 1.25, 14, false);

        // Frame cost: every effect on, a ripple running, pitch 14 + CRT (the worst case).
        var all = WatchParty(BoardFx.All.ToArray());
        var fxAll = BoardFxSet.From(all.Post.Fx);
        var rr = new BoardRaster(BoardTileLayout.MaxPitch);
        var bake = Stopwatch.StartNew();
        rr.SetPicture(all);
        double bakeMs = bake.Elapsed.TotalMilliseconds;
        var ripples = new[] { new BoardRipple(20, 10, 0.1) };
        for (int k = 0; k < 30; k++) { BoardScene.Compute(all, 0, fxAll, 0.3 + k / 30.0, 10, ripples, 0.3 + k / 30.0, false, c, z, ro); rr.Draw(c, z, true, 0.02, ro, 0.9); }
        var sw = Stopwatch.StartNew();
        const int frames = 300;
        for (int k = 0; k < frames; k++)
        {
            double t = 0.5 + k / 30.0;
            BoardScene.Compute(all, 0, fxAll, t, 10, ripples, t, false, c, z, ro);
            rr.Draw(c, z, true, 0.02, ro, BoardFxMath.GlowStrength(t, 10, false, true));
        }
        double ms = sw.Elapsed.TotalMilliseconds / frames;
        string Time(string label, Action body)
        {
            for (int k = 0; k < 20; k++) body();
            var s = Stopwatch.StartNew();
            for (int k = 0; k < frames; k++) body();
            return $" {label}={(s.Elapsed.TotalMilliseconds / frames).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}";
        }
        string parts = Time("scene", () => BoardScene.Compute(all, 0, fxAll, 0.7, 10, ripples, 0.7, false, c, z, ro))
            + Time("plain", () => rr.Draw(c, z, false, 0.02, null, 0))
            + Time("roles", () => rr.Draw(c, z, false, 0.02, ro, 0))
            + Time("roles+glow", () => rr.Draw(c, z, false, 0.02, ro, 0.9))
            + Time("roles+glow+crt", () => rr.Draw(c, z, true, 0.02, ro, 0.9));
        File.AppendAllText(Path.Combine(dir, "cost.txt"), "   parts:" + parts + Environment.NewLine);
        File.AppendAllText(Path.Combine(dir, "cost.txt"),
            $"{DateTime.Now:HH:mm:ss} pitch14+crt {ms.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} ms/frame, glow bake {bakeMs.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} ms, {rr.GlowPixels} glow px{Environment.NewLine}");
    }
}
