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

        private BoardPicture(BoardPost post, int frames, int x0, int y0, int w, int h,
            int[][] rgb, bool[][] lit, bool[][] bright, int[][][] cycle)
        {
            Post = post; FrameCount = frames; X0 = x0; Y0 = y0; PicW = w; PicH = h;
            Rgb = rgb; Lit = lit; Bright = bright; CycleRgb = cycle;
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
                    double luma = BoardFxMath.Luma(cr, cg, cb);
                    l[i] = a >= 128 && luma >= 0.12;
                    br[i] = l[i] && luma > 0.8;
                    if (l[i]) cy[i] = BoardFxMath.CycleColours(cr, cg, cb)!; // null entries = not saturated
                }
                rgb[f] = c; lit[f] = l; bright[f] = br; cycle[f] = cy;
            }
            return new BoardPicture(post, frames, x0, y0, fw, fh, rgb, lit, bright, cycle);
        }

        /// <summary>A plain picture of one colour, for tests and the preview.</summary>
        internal static BoardPicture Solid(BoardPost post, int argb)
        {
            var px = new int[post.Width * post.Frames * post.Height];
            Array.Fill(px, argb);
            return FromPixels(px, post.Width * post.Frames, post.Height, post);
        }
    }
}
