using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Services.Billboard.Board
{
    /// <summary>
    /// A decoded board post, laid onto the 64 x 36 grid and read once for the effects: which
    /// tiles are lit, which are bright enough to twinkle, which are saturated enough to cycle and
    /// what they cycle through. Everything comes from the picture's own colours, so any PNG works.
    /// Immutable after <see cref="FromPixels"/>; safe to hand between threads.
    /// </summary>
    public sealed class BoardPicture
    {
        public const int GridW = BoardWire.MaxFrameWidth;
        public const int GridH = BoardWire.MaxHeight;
        public const int Tiles = GridW * GridH;

        /// <summary>The board's own colour where the picture is transparent (mockup PAL[0], #120f26).</summary>
        public const int BaseRgb = 0x120F26;

        public BoardPost Post { get; }
        public int FrameCount { get; }

        /// <summary>Where the picture sits on the grid (centred). Chase runs round this rectangle.</summary>
        public int X0 { get; }
        public int Y0 { get; }
        public int PicW { get; }
        public int PicH { get; }

        /// <summary>Per frame, per tile: the colour as 0xRRGGBB.</summary>
        public int[][] Rgb { get; }

        /// <summary>Per frame, per tile: a picture pixel that is drawn (not transparent, not near-black).</summary>
        public bool[][] Lit { get; }

        /// <summary>Per frame, per tile: luma over 0.8 (twinkle).</summary>
        public bool[][] Bright { get; }

        /// <summary>Per frame, per tile: the three colours a cycle walks through; a null entry = not saturated.</summary>
        public int[][][] CycleRgb { get; }

        // ---- the message (owner, 2026-10-07: "the tiles with the message should glow") ----------

        /// <summary>
        /// RGB distance (Euclidean, 0..441) past which a tile counts as message rather than field.
        /// 48 keeps a field's own soft shading in the field and puts any real stroke in the message.
        /// </summary>
        public const double InkDistance = 48;

        /// <summary>The field: the picture's most frequent colour over every frame (transparent counts as the board's base).</summary>
        public int BackgroundRgb { get; }

        /// <summary>True when some tile is clearly not the field. False = a flat picture: every tile is <see cref="BoardTileRole.Plain"/>.</summary>
        public bool HasInk { get; }

        /// <summary>Per frame, per tile: field, message (ink) or message on the picture's outer ring (frame).</summary>
        public BoardTileRole[][] Role { get; }

        private BoardPicture(BoardPost post, int frames, int x0, int y0, int w, int h,
            int[][] rgb, bool[][] lit, bool[][] bright, int[][][] cycle,
            int backgroundRgb, bool hasInk, BoardTileRole[][] role)
        {
            Post = post; FrameCount = frames; X0 = x0; Y0 = y0; PicW = w; PicH = h;
            Rgb = rgb; Lit = lit; Bright = bright; CycleRgb = cycle;
            BackgroundRgb = backgroundRgb; HasInk = hasInk; Role = role;
        }

        /// <summary>A tile with no picture under it: field when the picture has a message, plain otherwise.</summary>
        public BoardTileRole EmptyRole => HasInk ? BoardTileRole.Background : BoardTileRole.Plain;

        /// <summary>Euclidean distance between two 0xRRGGBB colours.</summary>
        public static double RgbDistance(int a, int b)
        {
            int dr = ((a >> 16) & 0xFF) - ((b >> 16) & 0xFF);
            int dg = ((a >> 8) & 0xFF) - ((b >> 8) & 0xFF);
            int db = (a & 0xFF) - (b & 0xFF);
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        /// <summary>
        /// The most frequent colour among <paramref name="count"/> composited pixels; a pixel marked
        /// transparent counts as <see cref="BaseRgb"/>. Ties go to the darker colour so the result
        /// never depends on enumeration order.
        /// </summary>
        public static int DominantRgb(int[] rgb, bool[] transparent, int count)
        {
            var counts = new System.Collections.Generic.Dictionary<int, int>();
            for (int i = 0; i < count; i++)
            {
                int c = transparent[i] ? BaseRgb : rgb[i];
                counts.TryGetValue(c, out var n);
                counts[c] = n + 1;
            }
            int best = BaseRgb, bestN = -1;
            double bestLuma = double.MaxValue;
            foreach (var (c, n) in counts)
            {
                double l = BoardFxMath.Luma((c >> 16) & 0xFF, (c >> 8) & 0xFF, c & 0xFF);
                if (n > bestN || (n == bestN && l < bestLuma)) { best = c; bestN = n; bestLuma = l; }
            }
            return best;
        }

        /// <summary>
        /// Decodes PNG bytes (WPF imaging, any thread) and lays them out. Null when the bytes are not
        /// a readable image. A strip wider or taller than the post says is cropped, never scaled.
        /// </summary>
        public static BoardPicture? Decode(byte[] png, BoardPost post)
        {
            if (png == null || png.Length < 8) return null;
            // PNG signature: never hand anything else to a codec.
            if (png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47) return null;
            try
            {
                using var ms = new MemoryStream(png, writable: false);
                var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                    BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0) return null;
                BitmapSource src = decoder.Frames[0];
                if (src.PixelWidth <= 0 || src.PixelHeight <= 0) return null;
                if (src.PixelWidth > BoardWire.MaxFrameWidth * BoardWire.MaxFrames || src.PixelHeight > 512) return null;
                if (src.Format != PixelFormats.Bgra32)
                    src = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                int w = src.PixelWidth, h = src.PixelHeight;
                var px = new int[w * h];
                src.CopyPixels(px, w * 4, 0);
                return FromPixels(px, w, h, post);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Lays out raw Bgra32 pixels (0xAARRGGBB ints, row-major). Pure; the test seam.</summary>
        public static BoardPicture FromPixels(int[] px, int width, int height, BoardPost post)
        {
            int frames = Math.Clamp(post.Frames, 1, BoardWire.MaxFrames);
            // One frame is the post's width, never wider than the strip allows.
            int fw = Math.Min(Math.Min(post.Width, GridW), Math.Max(1, width / frames));
            int fh = Math.Min(Math.Min(post.Height, GridH), height);
            // A strip shorter than it claims to be still shows what it has.
            if (width < fw * frames) frames = Math.Max(1, width / Math.Max(1, fw));
            int x0 = (GridW - fw) / 2, y0 = (GridH - fh) / 2;

            var rgb = new int[frames][];
            var lit = new bool[frames][];
            var bright = new bool[frames][];
            var cycle = new int[frames][][];
            // The picture's own pixels, composited, for the field/message split below.
            int area = fw * fh;
            var picRgb = new int[frames * area];
            var picClear = new bool[frames * area];
            for (int f = 0; f < frames; f++)
            {
                var c = new int[Tiles];
                var l = new bool[Tiles];
                var br = new bool[Tiles];
                var cy = new int[Tiles][];
                Array.Fill(c, BaseRgb);
                for (int y = 0; y < fh; y++)
                for (int x = 0; x < fw; x++)
                {
                    int p = px[y * width + f * fw + x];
                    int a = (p >> 24) & 0xFF, r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF, b = p & 0xFF;
                    int i = (y0 + y) * GridW + x0 + x;
                    // Composite over the board's base so a half-transparent edge reads as a soft tile.
                    int br0 = (BaseRgb >> 16) & 0xFF, bg0 = (BaseRgb >> 8) & 0xFF, bb0 = BaseRgb & 0xFF;
                    int cr = (r * a + br0 * (255 - a)) / 255;
                    int cg = (g * a + bg0 * (255 - a)) / 255;
                    int cb = (b * a + bb0 * (255 - a)) / 255;
                    c[i] = (cr << 16) | (cg << 8) | cb;
                    picRgb[f * area + y * fw + x] = c[i];
                    picClear[f * area + y * fw + x] = a < 128;
                    double luma = BoardFxMath.Luma(cr, cg, cb);
                    l[i] = a >= 128 && luma >= 0.12;
                    br[i] = l[i] && luma > 0.8;
                    if (l[i]) cy[i] = BoardFxMath.CycleColours(cr, cg, cb)!; // null entries = not saturated
                }
                rgb[f] = c; lit[f] = l; bright[f] = br; cycle[f] = cy;
            }

            int field = DominantRgb(picRgb, picClear, picRgb.Length);
            var role = new BoardTileRole[frames][];
            bool hasInk = false;
            for (int f = 0; f < frames; f++)
            {
                var ro = new BoardTileRole[Tiles];
                for (int y = 0; y < fh; y++)
                for (int x = 0; x < fw; x++)
                {
                    int k = f * area + y * fw + x;
                    // Transparent is field; anything clearly away from the field colour is message.
                    if (picClear[k] || RgbDistance(picRgb[k], field) <= InkDistance) continue;
                    bool edge = x == 0 || y == 0 || x == fw - 1 || y == fh - 1;
                    ro[(y0 + y) * GridW + x0 + x] = edge ? BoardTileRole.Frame : BoardTileRole.Ink;
                    hasInk = true;
                }
                role[f] = ro;
            }
            // Every tile that is not message is field, including the board round a small picture.
            // A picture with no message at all keeps the plain look (Plain = 0, the arrays' default).
            if (hasInk)
                foreach (var ro in role)
                    for (int i = 0; i < ro.Length; i++)
                        if (ro[i] == BoardTileRole.Plain) ro[i] = BoardTileRole.Background;

            return new BoardPicture(post, frames, x0, y0, fw, fh, rgb, lit, bright, cycle, field, hasInk, role);
        }

        /// <summary>A plain picture of one colour, for tests and the preview.</summary>
        internal static BoardPicture Solid(BoardPost post, int argb)
        {
            var px = new int[post.Width * post.Frames * post.Height];
            Array.Fill(px, argb);
            return FromPixels(px, post.Width * post.Frames, post.Height, post);
        }
    }

    /// <summary>What a tile is to the message: drawn plain, the field (low, dark), message (raised, glowing) or frame (glows strongest).</summary>
    public enum BoardTileRole : byte
    {
        Plain = 0,
        Background = 1,
        Ink = 2,
        Frame = 3,
    }
}
