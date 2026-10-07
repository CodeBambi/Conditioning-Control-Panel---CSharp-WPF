using System;

namespace ConditioningControlPanel.Services.Billboard.Board
{
    /// <summary>
    /// Where tiles sit at a given tile pitch (pixels per grid cell). One lamp top-left, as the
    /// house depth law says: grout between tiles, a resting shadow down and right, lifted tiles
    /// rise up-left with a longer shadow. Pure maths, pinned by tests.
    /// </summary>
    public readonly record struct BoardTileLayout(int Pitch)
    {
        /// <summary>Grout between two tiles, at least one pixel.</summary>
        public int Gap => Math.Max(1, (int)Math.Round(Pitch * 0.1));

        /// <summary>The face of a resting tile.</summary>
        public int TileSize => Math.Max(1, Pitch - Gap);

        /// <summary>Resting shadow offset (down; across is 60% of it).</summary>
        public double Rest => Pitch * 0.12;

        public int Width => BoardPicture.GridW * Pitch;
        public int Height => BoardPicture.GridH * Pitch;

        /// <summary>Top-left of a resting tile's face.</summary>
        public (int X, int Y) TileOrigin(int tx, int ty) => (tx * Pitch + Gap / 2, ty * Pitch + Gap / 2);

        /// <summary>A lifted tile: scale up 20% at the top, rise up-left, its shadow drops further.</summary>
        public LiftedTile Lifted(int tx, int ty, double z)
        {
            int ts = TileSize;
            double n = ts * (1 + 0.2 * z);
            var (ox, oy) = TileOrigin(tx, ty);
            double x = ox + ts / 2.0 - n / 2 - z * Pitch * 0.12;
            double y = oy + ts / 2.0 - n / 2 - z * Pitch * 0.6;
            double sh = Rest + z * Pitch * 0.7;
            return new LiftedTile((int)Math.Round(x), (int)Math.Round(y), Math.Max(1, (int)Math.Round(n)),
                (int)Math.Round(sh * 0.6), (int)Math.Round(sh), 0.45 + 0.2 * z, 0.12 * z);
        }

        /// <summary>
        /// The pitch for an art area <paramref name="pixelWidth"/> device pixels wide: the nearest
        /// whole number so the bitmap lands close to 1:1, clamped so a frame stays inside budget.
        /// </summary>
        public static int PitchFor(double pixelWidth)
        {
            if (double.IsNaN(pixelWidth) || pixelWidth <= 0) return 10;
            return Math.Clamp((int)Math.Round(pixelWidth / BoardPicture.GridW), MinPitch, MaxPitch);
        }

        public const int MinPitch = 4;
        public const int MaxPitch = 14;
    }

    /// <summary>A lifted tile's face rectangle, its shadow offset and the two overlay strengths.</summary>
    public readonly record struct LiftedTile(int X, int Y, int Size, int ShadowDx, int ShadowDy, double ShadowAlpha, double Highlight);

    /// <summary>
    /// Draws a <see cref="BoardScene"/> into a Bgra32 buffer (0xAARRGGBB ints). Everything that does
    /// not move is baked once per size: the grout with every resting shadow, the bevel as a
    /// multiply + add table, the CRT scanline and vignette mask. A frame is then one block copy,
    /// one pass over the resting tiles, the few lifted tiles on top, and the CRT pass if asked.
    /// No WPF, no allocation per frame; the view copies the buffer into its WriteableBitmap.
    /// </summary>
    public sealed class BoardRaster
    {
        public const int GroutRgb = 0x04030A;

        public BoardTileLayout Layout { get; }
        public int Width => Layout.Width;
        public int Height => Layout.Height;

        /// <summary>The finished frame.</summary>
        public int[] Pixels { get; }

        private readonly int[] _background;
        private readonly int _bevelN;
        private readonly int[] _bevelMul; // 0..256 per pixel of a tile face
        private readonly int[] _bevelAdd; // 0..255
        private readonly int[] _crtMul;   // 0..256 per pixel of the frame
        private readonly bool[] _lifted = new bool[BoardPicture.Tiles];

        public BoardRaster(int pitch)
        {
            Layout = new BoardTileLayout(Math.Clamp(pitch, 2, 64));
            Pixels = new int[Width * Height];
            _bevelN = Layout.TileSize;
            (_bevelMul, _bevelAdd) = BuildBevel(_bevelN);
            _background = BuildBackground(Layout);
            _crtMul = BuildCrtMask(Width, Height, Layout.Pitch);
        }

        /// <summary>Draws one frame. <paramref name="colour"/> and <paramref name="lift"/> come from <see cref="BoardScene.Compute"/>.</summary>
        public void Draw(int[] colour, float[] lift, bool crt, double crtFlicker)
        {
            const int W = BoardPicture.GridW, H = BoardPicture.GridH;
            Buffer.BlockCopy(_background, 0, Pixels, 0, _background.Length * 4);

            int ts = _bevelN;
            int liftedCount = 0;
            for (int ty = 0; ty < H; ty++)
            {
                for (int tx = 0; tx < W; tx++)
                {
                    int i = ty * W + tx;
                    if (lift[i] > 0.02f) { _lifted[i] = true; liftedCount++; continue; }
                    _lifted[i] = false;
                    var (ox, oy) = Layout.TileOrigin(tx, ty);
                    DrawFace(colour[i], ox, oy, ts, 0);
                }
            }

            if (liftedCount > 0)
            {
                for (int i = 0; i < BoardPicture.Tiles; i++)
                {
                    if (!_lifted[i]) continue;
                    var lt = Layout.Lifted(i % W, i / W, lift[i]);
                    Darken(lt.X + lt.ShadowDx, lt.Y + lt.ShadowDy, lt.Size, lt.Size, lt.ShadowAlpha);
                    DrawFace(colour[i], lt.X, lt.Y, lt.Size, lt.Highlight);
                }
            }

            if (crt) ApplyCrt(crtFlicker);
        }

        /// <summary>One tile face with its bevel, sampled to <paramref name="size"/>, clipped to the frame.</summary>
        private void DrawFace(int rgb, int x0, int y0, int size, double highlight)
        {
            int cr = (rgb >> 16) & 0xFF, cg = (rgb >> 8) & 0xFF, cb = rgb & 0xFF;
            int hl = (int)(highlight * 256);
            int n = _bevelN, w = Width, h = Height;
            int xs = Math.Max(0, x0), xe = Math.Min(w, x0 + size);
            int ys = Math.Max(0, y0), ye = Math.Min(h, y0 + size);
            if (xs >= xe || ys >= ye) return;
            var px = Pixels;
            bool same = size == n;
            for (int y = ys; y < ye; y++)
            {
                int by = same ? y - y0 : (y - y0) * n / size;
                int brow = by * n;
                int row = y * w;
                for (int x = xs; x < xe; x++)
                {
                    int bx = same ? x - x0 : (x - x0) * n / size;
                    int m = _bevelMul[brow + bx], a = _bevelAdd[brow + bx];
                    int r = ((cr * m) >> 8) + a, g = ((cg * m) >> 8) + a, b = ((cb * m) >> 8) + a;
                    if (hl > 0)
                    {
                        r += ((255 - r) * hl) >> 8; g += ((255 - g) * hl) >> 8; b += ((255 - b) * hl) >> 8;
                    }
                    if (r > 255) r = 255; if (g > 255) g = 255; if (b > 255) b = 255;
                    px[row + x] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                }
            }
        }

        private void Darken(int x0, int y0, int sw, int sh, double alpha)
        {
            int keep = 256 - (int)(Math.Clamp(alpha, 0, 1) * 256);
            int xs = Math.Max(0, x0), xe = Math.Min(Width, x0 + sw);
            int ys = Math.Max(0, y0), ye = Math.Min(Height, y0 + sh);
            var px = Pixels;
            for (int y = ys; y < ye; y++)
            {
                int row = y * Width;
                for (int x = xs; x < xe; x++)
                {
                    int p = px[row + x];
                    int r = (((p >> 16) & 0xFF) * keep) >> 8, g = (((p >> 8) & 0xFF) * keep) >> 8, b = ((p & 0xFF) * keep) >> 8;
                    px[row + x] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                }
            }
        }

        private void ApplyCrt(double flicker)
        {
            int f = (int)(Math.Clamp(flicker, 0, 1) * 256);
            var px = Pixels;
            for (int i = 0; i < px.Length; i++)
            {
                int p = px[i], m = _crtMul[i];
                int r = (((p >> 16) & 0xFF) * m) >> 8, g = (((p >> 8) & 0xFF) * m) >> 8, b = ((p & 0xFF) * m) >> 8;
                r += ((255 - r) * f) >> 8; g += ((255 - g) * f) >> 8; b += ((255 - b) * f) >> 8;
                px[i] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
            }
        }

        // ---- baked layers --------------------------------------------------------------------

        /// <summary>
        /// The bevel of a tile face, as out = colour * mul / 256 + add: a top-left to bottom-right
        /// diagonal (white .40 fading out by 42%, black .45 rising from 58%), then a lit top and
        /// left edge (white .30) and a shaded bottom and right edge (black .40).
        /// </summary>
        public static (int[] Mul, int[] Add) BuildBevel(int n)
        {
            var mul = new int[n * n];
            var add = new int[n * n];
            int e = Math.Max(1, (int)Math.Round(n * 0.11));
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double m = 1, a = 0;
                double t = n <= 1 ? 0.5 : (x + y + 1) / (2.0 * n);
                double aw = t < 0.42 ? 0.40 * (1 - t / 0.42) : 0;
                double ab = t > 0.58 ? 0.45 * ((t - 0.58) / 0.42) : 0;
                Over(ref m, ref a, aw, 255);
                Over(ref m, ref a, ab, 0);
                bool top = y < e, left = x < e, bottom = y >= n - e, right = x >= n - e;
                if (top || left) Over(ref m, ref a, 0.30, 255);
                if (bottom || right) Over(ref m, ref a, 0.40, 0);
                mul[y * n + x] = (int)Math.Round(m * 256);
                add[y * n + x] = (int)Math.Round(a);
            }
            return (mul, add);

            static void Over(ref double m, ref double a, double alpha, double value)
            {
                if (alpha <= 0) return;
                m *= 1 - alpha;
                a = a * (1 - alpha) + value * alpha;
            }
        }

        /// <summary>Grout everywhere, every tile's resting shadow on it (black .6 down and right).</summary>
        private static int[] BuildBackground(BoardTileLayout l)
        {
            int w = l.Width, h = l.Height;
            var bg = new int[w * h];
            int grout = unchecked((int)0xFF000000) | GroutRgb;
            Array.Fill(bg, grout);
            int sr = (int)(((GroutRgb >> 16) & 0xFF) * 0.4), sg = (int)(((GroutRgb >> 8) & 0xFF) * 0.4), sb = (int)((GroutRgb & 0xFF) * 0.4);
            int shadow = unchecked((int)0xFF000000) | (sr << 16) | (sg << 8) | sb;
            int dx = (int)Math.Round(l.Rest * 0.6), dy = (int)Math.Round(l.Rest), ts = l.TileSize;
            for (int ty = 0; ty < BoardPicture.GridH; ty++)
            for (int tx = 0; tx < BoardPicture.GridW; tx++)
            {
                var (ox, oy) = l.TileOrigin(tx, ty);
                for (int y = Math.Max(0, oy + dy); y < Math.Min(h, oy + dy + ts); y++)
                for (int x = Math.Max(0, ox + dx); x < Math.Min(w, ox + dx + ts); x++)
                    bg[y * w + x] = shadow;
            }
            return bg;
        }

        /// <summary>One scanline per tile row (the lower quarter at .7) and a soft vignette to .55 at the corners.</summary>
        public static int[] BuildCrtMask(int w, int h, int pitch)
        {
            var mask = new int[w * h];
            double cx = w / 2.0, cy = h / 2.0, r0 = h * 0.3, r1 = w * 0.62;
            for (int y = 0; y < h; y++)
            {
                double inRow = (y % pitch) / (double)pitch;
                double scan = inRow >= 0.76 ? 0.7 : 1.0;
                for (int x = 0; x < w; x++)
                {
                    double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    double k = Math.Clamp((d - r0) / (r1 - r0), 0, 1);
                    double vig = 1 - 0.45 * k;
                    mask[y * w + x] = (int)Math.Round(scan * vig * 256);
                }
            }
            return mask;
        }
    }
}
