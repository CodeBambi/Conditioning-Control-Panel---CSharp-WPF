using System;

namespace ConditioningControlPanel.Services.Billboard.Board
{
    /// <summary>
    /// Where tiles sit at a given tile pitch (pixels per grid cell). One lamp top-left, as the
    /// house depth law says: grout between tiles, a resting shadow down and right, lifted tiles
    /// rise up-left with a longer shadow. Message tiles rest a little higher than the field, with
    /// a darker, longer shadow (owner, 2026-10-07). Pure maths, pinned by tests.
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

        // ---- the message rests higher --------------------------------------------------------

        /// <summary>The height a message tile rests at, as a lift (0.15 = about 0.09 tile up).</summary>
        public const double InkRestZ = 0.15;

        /// <summary>The message tile's darker shadow (field tiles cast grout * .4 baked under them).</summary>
        public const double InkShadowAlpha = 0.7;

        /// <summary>How far a resting message tile sits up and left of a field tile: about 0.09 tile up, half that across.</summary>
        public (int Dx, int Dy) InkRaise => ((int)Math.Round(Pitch * 0.045), Math.Max(1, (int)Math.Round(Pitch * 0.09)));

        /// <summary>A resting message tile's shadow, offset from its raised face: longer than the field's.</summary>
        public (int Dx, int Dy) InkShadow => (Math.Max(1, (int)Math.Round(Pitch * 0.155)), Math.Max(2, (int)Math.Round(Pitch * 0.25)));

        /// <summary>The field's resting shadow offset (baked into the grout).</summary>
        public (int Dx, int Dy) FieldShadow => ((int)Math.Round(Rest * 0.6), (int)Math.Round(Rest));

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
    /// not move is baked once per size: the grout with every resting shadow, the bevels as multiply
    /// + add tables, the CRT scanline and vignette mask; the message glow is baked once per picture
    /// and size (<see cref="SetPicture"/>). A frame is one block copy, the field tiles (low, dark,
    /// a flat bevel), the glow added over them as one sparse pass at one scale, the message tiles
    /// (raised, brighter, a long shadow) over that, the few lifted tiles on top, then the CRT pass.
    /// No WPF, no allocation per frame; the view copies the buffer into its WriteableBitmap.
    /// </summary>
    public sealed class BoardRaster
    {
        public const int GroutRgb = 0x04030A;

        /// <summary>Field tiles: 70% bright (a 30% dark overlay) under a half-strength bevel.</summary>
        public const double FieldDim = 0.70;
        public const double FieldBevel = 0.5;

        /// <summary>Message tiles: a touch brighter than their colour.</summary>
        public const double InkBright = 1.10;

        /// <summary>A lifted field tile keeps a fifth of the highlight, so a crest never washes the field grey over the message.</summary>
        public const double FieldLiftHighlight = 0.2;

        /// <summary>
        /// The glow, as the approved mockup draws it: every message tile painted 1.4 tiles wide in
        /// its own colour (message .7, frame 1.0), blurred 1.2 tiles at .8 with a tight 0.5-tile
        /// blur at .7 over it, then added (lighter) over the field. <see cref="GlowGain"/> is the
        /// one tuning knob on top.
        /// </summary>
        public const double GlowInkAlpha = 0.7;
        public const double GlowFrameAlpha = 1.0;
        public const double GlowWideTiles = 1.2;
        public const double GlowWideAlpha = 0.8;
        public const double GlowTightTiles = 0.5;
        public const double GlowTightAlpha = 0.7;
        public const double GlowGain = 1.15;

        /// <summary>Pixels per tile the glow is blurred at, before it is scaled up to the pitch.</summary>
        private const int GlowRes = 5;

        public BoardTileLayout Layout { get; }
        public int Width => Layout.Width;
        public int Height => Layout.Height;

        /// <summary>The finished frame.</summary>
        public int[] Pixels { get; }

        private readonly int[] _background;
        private readonly int _bevelN;
        private readonly int[] _bevelMul, _bevelAdd; // plain tiles: 0..256 and 0..255 per pixel of a face
        private readonly int[] _inkMul, _inkAdd;     // message tiles
        private readonly int[] _fieldMul, _fieldAdd; // field tiles
        private readonly int[] _crtMul;              // 0..256 per pixel of the frame
        private readonly bool[] _lifted = new bool[BoardPicture.Tiles];

        // The baked glow: the whole layer (tests read it) and the non-zero pixels as one sparse list.
        private int[]? _glowLayer;
        private int[] _glowIdx = Array.Empty<int>();
        private int[] _glowRgb = Array.Empty<int>();
        private BoardPicture? _picture;

        public BoardRaster(int pitch)
        {
            Layout = new BoardTileLayout(Math.Clamp(pitch, 2, 64));
            Pixels = new int[Width * Height];
            _bevelN = Layout.TileSize;
            (_bevelMul, _bevelAdd) = BuildBevel(_bevelN);
            (_inkMul, _inkAdd) = BuildBevel(_bevelN, 1.0, InkBright);
            (_fieldMul, _fieldAdd) = BuildBevel(_bevelN, FieldBevel, FieldDim);
            _background = BuildBackground(Layout);
            _crtMul = BuildCrtMask(Width, Height, Layout.Pitch);
        }

        /// <summary>The picture whose glow is baked, or null.</summary>
        public BoardPicture? Picture => _picture;

        /// <summary>How many pixels the glow touches.</summary>
        public int GlowPixels => _glowIdx.Length;

        /// <summary>The baked glow at a pixel as 0xRRGGBB (0 = none), at full strength.</summary>
        public int GlowAt(int x, int y) => _glowLayer == null ? 0 : _glowLayer[y * Width + x];

        /// <summary>
        /// Bakes the message glow for <paramref name="picture"/> at this size. Call when the picture
        /// changes; the raster is already per size. A multi-frame strip glows where any frame has
        /// message. Null (or a picture with no message) clears it.
        /// </summary>
        public void SetPicture(BoardPicture? picture)
        {
            if (ReferenceEquals(picture, _picture)) return;
            _picture = picture;
            _glowLayer = picture != null && picture.HasInk ? BakeGlow(picture, Layout) : null;
            if (_glowLayer == null) { _glowIdx = Array.Empty<int>(); _glowRgb = Array.Empty<int>(); return; }
            // The frame pass skips pixels a resting message face covers on every frame (it draws
            // over them anyway). Not under the wave: there the message moves off its own tiles.
            var covered = new bool[_glowLayer.Length];
            if (!BoardFxSet.From(picture!.Post.Fx).Wave)
            {
                const int W = BoardPicture.GridW;
                var (rx, ry) = Layout.InkRaise;
                int ts = Layout.TileSize;
                for (int t = 0; t < BoardPicture.Tiles; t++)
                {
                    bool always = true;
                    for (int f = 0; f < picture.FrameCount && always; f++) always = picture.Role[f][t] >= BoardTileRole.Ink;
                    if (!always) continue;
                    var (ox, oy) = Layout.TileOrigin(t % W, t / W);
                    for (int y = Math.Max(0, oy - ry); y < Math.Min(Height, oy - ry + ts); y++)
                    for (int x = Math.Max(0, ox - rx); x < Math.Min(Width, ox - rx + ts); x++)
                        covered[y * Width + x] = true;
                }
            }
            int n = 0;
            for (int i = 0; i < _glowLayer.Length; i++) if (_glowLayer[i] != 0 && !covered[i]) n++;
            _glowIdx = new int[n];
            _glowRgb = new int[n];
            for (int i = 0, k = 0; i < _glowLayer.Length; i++)
                if (_glowLayer[i] != 0 && !covered[i]) { _glowIdx[k] = i; _glowRgb[k] = _glowLayer[i]; k++; }
        }

        /// <summary>Draws one frame without the message look (every tile plain). Kept for callers with no roles.</summary>
        public void Draw(int[] colour, float[] lift, bool crt, double crtFlicker) => Draw(colour, lift, crt, crtFlicker, null, 0);

        /// <summary>
        /// Draws one frame. <paramref name="colour"/>, <paramref name="lift"/> and <paramref name="role"/> come
        /// from <see cref="BoardScene.Compute(BoardPicture,int,BoardFxSet,double,double,System.Collections.Generic.IReadOnlyList{BoardRipple},double,bool,int[],float[],BoardTileRole[])"/>;
        /// <paramref name="glow"/> is <see cref="BoardFxMath.GlowStrength"/> (0 = no glow).
        /// </summary>
        public void Draw(int[] colour, float[] lift, bool crt, double crtFlicker, BoardTileRole[]? role, double glow)
        {
            const int W = BoardPicture.GridW, H = BoardPicture.GridH;
            Buffer.BlockCopy(_background, 0, Pixels, 0, _background.Length * 4);

            int ts = _bevelN;
            int liftedCount = 0, inkCount = 0;
            // 1. Plain and field tiles at rest.
            for (int ty = 0; ty < H; ty++)
            {
                for (int tx = 0; tx < W; tx++)
                {
                    int i = ty * W + tx;
                    if (lift[i] > 0.02f) { _lifted[i] = true; liftedCount++; continue; }
                    _lifted[i] = false;
                    var ro = role == null ? BoardTileRole.Plain : role[i];
                    if (ro >= BoardTileRole.Ink) { inkCount++; continue; }
                    var (ox, oy) = Layout.TileOrigin(tx, ty);
                    if (ro == BoardTileRole.Background) DrawRestingFace(colour[i], ox, oy, _fieldFaces, _fieldMul, _fieldAdd);
                    else DrawRestingFace(colour[i], ox, oy, _plainFaces, _bevelMul, _bevelAdd);
                }
            }

            // 2. The glow over the field and the grout, under the message.
            if (glow > 0 && _glowIdx.Length > 0 && role != null) AddGlow(glow);

            // 3. Lifted field and plain tiles. A field crest rolls UNDER the message, so it never
            // hides a word; it stays dark (a fifth of the highlight, its own flat bevel).
            if (liftedCount > 0) DrawLifted(colour, lift, role, inkLayer: false);

            // 4. Message tiles at rest: raised up-left, a long dark shadow over the field first.
            if (inkCount > 0)
            {
                var (rx, ry) = Layout.InkRaise;
                var (sx, sy) = Layout.InkShadow;
                for (int i = 0; i < BoardPicture.Tiles; i++)
                {
                    if (_lifted[i] || role![i] < BoardTileRole.Ink) continue;
                    var (ox, oy) = Layout.TileOrigin(i % W, i / W);
                    int fx = ox - rx, fy = oy - ry;
                    // Only the part the face will not cover: a strip to the right and one below.
                    Darken(fx + ts, fy + sy, sx, ts, BoardTileLayout.InkShadowAlpha);
                    Darken(fx + sx, fy + ts, ts - sx, sy, BoardTileLayout.InkShadowAlpha);
                }
                for (int i = 0; i < BoardPicture.Tiles; i++)
                {
                    if (_lifted[i] || role![i] < BoardTileRole.Ink) continue;
                    var (ox, oy) = Layout.TileOrigin(i % W, i / W);
                    DrawRestingFace(colour[i], ox - rx, oy - ry, _inkFaces, _inkMul, _inkAdd);
                }
            }

            // 5. Lifted message tiles on top: they lift from their resting height, full highlight.
            if (liftedCount > 0 && role != null) DrawLifted(colour, lift, role, inkLayer: true);

            if (crt) ApplyCrt(crtFlicker);
        }

        private void DrawLifted(int[] colour, float[] lift, BoardTileRole[]? role, bool inkLayer)
        {
            const int W = BoardPicture.GridW;
            {
                for (int i = 0; i < BoardPicture.Tiles; i++)
                {
                    if (!_lifted[i]) continue;
                    var ro = role == null ? BoardTileRole.Plain : role[i];
                    if ((ro >= BoardTileRole.Ink) != inkLayer) continue;
                    double z = lift[i];
                    if (ro >= BoardTileRole.Ink) z = BoardTileLayout.InkRestZ + (1 - BoardTileLayout.InkRestZ) * z;
                    var lt = Layout.Lifted(i % W, i / W, z);
                    Darken(lt.X + lt.ShadowDx, lt.Y + lt.ShadowDy, lt.Size, lt.Size, lt.ShadowAlpha);
                    switch (ro)
                    {
                        case BoardTileRole.Background:
                            DrawFace(colour[i], lt.X, lt.Y, lt.Size, lt.Highlight * FieldLiftHighlight, _fieldMul, _fieldAdd);
                            break;
                        case BoardTileRole.Ink:
                        case BoardTileRole.Frame:
                            DrawFace(colour[i], lt.X, lt.Y, lt.Size, lt.Highlight, _inkMul, _inkAdd);
                            break;
                        default:
                            DrawFace(colour[i], lt.X, lt.Y, lt.Size, lt.Highlight, _bevelMul, _bevelAdd);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// One sparse pass: the baked glow, scaled once, added (lighter) to what is under it. Two
        /// channels per multiply and a carry-saturating add, so it costs a few integer ops a pixel.
        /// </summary>
        private void AddGlow(double strength)
        {
            uint s = (uint)(Math.Clamp(strength, 0, 1) * 256);
            if (s == 0) return;
            var px = Pixels;
            var idx = _glowIdx;
            var gl = _glowRgb;
            for (int k = 0; k < idx.Length; k++)
            {
                int i = idx[k];
                uint g = (uint)gl[k], p = (uint)px[i];
                uint rb = (p & 0xFF00FFu) + ((((g & 0xFF00FFu) * s) >> 8) & 0xFF00FFu);
                uint carry = rb & 0x1000100u;
                rb = (rb | (carry - (carry >> 8))) & 0xFF00FFu;
                uint gg = (p & 0xFF00u) + ((((g & 0xFF00u) * s) >> 8) & 0xFF00u);
                uint c2 = gg & 0x10000u;
                gg = (gg | (c2 - (c2 >> 8))) & 0xFF00u;
                px[i] = unchecked((int)(0xFF000000u | rb | gg));
            }
        }

        // Resting faces repeat: the field is mostly one colour. A finished face per (table, colour)
        // is kept and copied row by row; any colour an effect makes on the fly fills the cache,
        // which simply starts over once it holds more than FaceCacheCap faces.
        private const int FaceCacheCap = 96;
        private readonly System.Collections.Generic.Dictionary<int, int[]> _plainFaces = new();
        private readonly System.Collections.Generic.Dictionary<int, int[]> _fieldFaces = new();
        private readonly System.Collections.Generic.Dictionary<int, int[]> _inkFaces = new();

        private void DrawRestingFace(int rgb, int x0, int y0, System.Collections.Generic.Dictionary<int, int[]> cache, int[] mulT, int[] addT)
        {
            int n = _bevelN;
            if (x0 < 0 || y0 < 0 || x0 + n > Width || y0 + n > Height)
            {
                DrawFace(rgb, x0, y0, n, 0, mulT, addT);
                return;
            }
            if (!cache.TryGetValue(rgb, out var face))
            {
                if (cache.Count >= FaceCacheCap) cache.Clear();
                face = new int[n * n];
                int cr = (rgb >> 16) & 0xFF, cg = (rgb >> 8) & 0xFF, cb = rgb & 0xFF;
                for (int k = 0; k < face.Length; k++)
                {
                    int m = mulT[k], a = addT[k];
                    int r = ((cr * m) >> 8) + a, g = ((cg * m) >> 8) + a, b = ((cb * m) >> 8) + a;
                    if (r > 255) r = 255; if (g > 255) g = 255; if (b > 255) b = 255;
                    face[k] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                }
                cache[rgb] = face;
            }
            var px = Pixels.AsSpan();
            var src = face.AsSpan();
            int w = Width;
            for (int y = 0; y < n; y++)
                src.Slice(y * n, n).CopyTo(px.Slice((y0 + y) * w + x0, n));
        }

        /// <summary>One tile face with its bevel, sampled to <paramref name="size"/>, clipped to the frame.</summary>
        private void DrawFace(int rgb, int x0, int y0, int size, double highlight, int[] mulT, int[] addT)
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
                    int m = mulT[brow + bx], a = addT[brow + bx];
                    int r = ((cr * m) >> 8) + a, g = ((cg * m) >> 8) + a, b = ((cb * m) >> 8) + a;
                    if (hl > 0)
                    {
                        if (r > 255) r = 255; if (g > 255) g = 255; if (b > 255) b = 255;
                        r += ((255 - r) * hl) >> 8; g += ((255 - g) * hl) >> 8; b += ((255 - b) * hl) >> 8;
                    }
                    if (r > 255) r = 255; if (g > 255) g = 255; if (b > 255) b = 255;
                    px[row + x] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
                }
            }
        }

        private void Darken(int x0, int y0, int sw, int sh, double alpha)
        {
            if (sw <= 0 || sh <= 0) return;
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

        /// <summary>
        /// Scanlines and vignette, then the flicker toward white, folded into one multiply and one
        /// add: out = in * mask * (1 - f) + 255 * f. Two channels per multiply; nothing overflows
        /// because the mask never passes 256.
        /// </summary>
        private void ApplyCrt(double flicker)
        {
            uint f = (uint)(Math.Clamp(flicker, 0, 1) * 256);
            uint keep = 256 - f, lift = (255 * f) >> 8;
            uint addRb = lift * 0x10001u, addG = lift << 8;
            var px = Pixels;
            var mask = _crtMul;
            for (int i = 0; i < px.Length; i++)
            {
                uint p = (uint)px[i], m = ((uint)mask[i] * keep) >> 8;
                uint rb = (((p & 0xFF00FFu) * m) >> 8) & 0xFF00FFu;
                uint g = (((p & 0xFF00u) * m) >> 8) & 0xFF00u;
                px[i] = unchecked((int)(0xFF000000u | (rb + addRb) | (g + addG)));
            }
        }

        // ---- baked layers --------------------------------------------------------------------

        /// <summary>
        /// The bevel of a tile face, as out = colour * mul / 256 + add: a top-left to bottom-right
        /// diagonal (white .40 fading out by 42%, black .45 rising from 58%), then a lit top and
        /// left edge (white .30) and a shaded bottom and right edge (black .40).
        /// </summary>
        public static (int[] Mul, int[] Add) BuildBevel(int n) => BuildBevel(n, 1.0, 1.0);

        /// <summary>
        /// The bevel with every overlay at <paramref name="strength"/> of its alpha (field tiles use a
        /// half-strength, flatter bevel), the whole result then scaled by <paramref name="gain"/>
        /// (0.7 = the field's 30% dark overlay, 1.1 = the message's lift in brightness).
        /// </summary>
        public static (int[] Mul, int[] Add) BuildBevel(int n, double strength, double gain)
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
                Over(ref m, ref a, aw * strength, 255);
                Over(ref m, ref a, ab * strength, 0);
                bool top = y < e, left = x < e, bottom = y >= n - e, right = x >= n - e;
                if (top || left) Over(ref m, ref a, 0.30 * strength, 255);
                if (bottom || right) Over(ref m, ref a, 0.40 * strength, 0);
                mul[y * n + x] = (int)Math.Round(m * gain * 256);
                add[y * n + x] = (int)Math.Round(Math.Min(255, a * gain));
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
            var (dx, dy) = l.FieldShadow;
            int ts = l.TileSize;
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

        /// <summary>
        /// The message glow at the layout's size, as 0xRRGGBB per pixel (0 = none), each channel at
        /// most 255. Painted and blurred at <see cref="GlowRes"/> pixels per tile (it is soft, so
        /// nothing is lost), then scaled up bilinearly. Message = the union over every frame.
        /// </summary>
        public static int[] BakeGlow(BoardPicture pic, BoardTileLayout layout)
        {
            const int W = BoardPicture.GridW, H = BoardPicture.GridH, q = GlowRes;
            int lw = W * q, lh = H * q, n = lw * lh;
            // Premultiplied emission: each message tile a 1.4-tile square in its colour, source-over.
            var er = new float[n]; var eg = new float[n]; var eb = new float[n]; var ea = new float[n];
            for (int t = 0; t < BoardPicture.Tiles; t++)
            {
                int colour = 0; double alpha = 0;
                for (int f = 0; f < pic.FrameCount; f++)
                {
                    var ro = pic.Role[f][t];
                    if (ro < BoardTileRole.Ink) continue;
                    double a = ro == BoardTileRole.Frame ? GlowFrameAlpha : GlowInkAlpha;
                    if (a > alpha) { alpha = a; colour = pic.Rgb[f][t]; }
                }
                if (alpha <= 0) continue;
                float cr = (colour >> 16) & 0xFF, cg = (colour >> 8) & 0xFF, cb = colour & 0xFF, al = (float)alpha;
                int tx = t % W, ty = t / W;
                int x0 = Math.Max(0, (int)Math.Round((tx - 0.2) * q)), x1 = Math.Min(lw, (int)Math.Round((tx + 1.2) * q));
                int y0 = Math.Max(0, (int)Math.Round((ty - 0.2) * q)), y1 = Math.Min(lh, (int)Math.Round((ty + 1.2) * q));
                for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int i = y * lw + x;
                    float k = 1 - al;
                    er[i] = cr * al + er[i] * k; eg[i] = cg * al + eg[i] * k; eb[i] = cb * al + eb[i] * k; ea[i] = al + ea[i] * k;
                }
            }

            // Wide blur (rgb) and tight blur (rgba); the canvas blur radius is a standard deviation.
            double sWide = Math.Max(2.0 / layout.Pitch * q, GlowWideTiles * q);
            double sTight = Math.Max(1.0 / layout.Pitch * q, GlowTightTiles * q);
            var w1r = Gauss(er, lw, lh, sWide); var w1g = Gauss(eg, lw, lh, sWide); var w1b = Gauss(eb, lw, lh, sWide);
            var w2r = Gauss(er, lw, lh, sTight); var w2g = Gauss(eg, lw, lh, sTight); var w2b = Gauss(eb, lw, lh, sTight);
            var w2a = Gauss(ea, lw, lh, sTight);
            // The tight copy drawn over the wide one (source-over), both at their own alpha.
            var gr = new float[n]; var gg = new float[n]; var gb = new float[n];
            for (int i = 0; i < n; i++)
            {
                float under = (float)(GlowWideAlpha * (1 - GlowTightAlpha * Math.Min(1f, w2a[i])));
                gr[i] = (float)(GlowGain * (GlowTightAlpha * w2r[i] + under * w1r[i]));
                gg[i] = (float)(GlowGain * (GlowTightAlpha * w2g[i] + under * w1g[i]));
                gb[i] = (float)(GlowGain * (GlowTightAlpha * w2b[i] + under * w1b[i]));
            }

            // Up to the pitch, bilinear; a channel under half a step is nothing.
            int pw = layout.Width, ph = layout.Height, p = layout.Pitch;
            var outp = new int[pw * ph];
            for (int y = 0; y < ph; y++)
            {
                double vy = (y + 0.5) / p * q - 0.5;
                int y0 = (int)Math.Floor(vy); double fy = vy - y0;
                int ya = Math.Clamp(y0, 0, lh - 1), yb = Math.Clamp(y0 + 1, 0, lh - 1);
                for (int x = 0; x < pw; x++)
                {
                    double vx = (x + 0.5) / p * q - 0.5;
                    int x0 = (int)Math.Floor(vx); double fx = vx - x0;
                    int xa = Math.Clamp(x0, 0, lw - 1), xb = Math.Clamp(x0 + 1, 0, lw - 1);
                    int i00 = ya * lw + xa, i10 = ya * lw + xb, i01 = yb * lw + xa, i11 = yb * lw + xb;
                    int r = Sample(gr, i00, i10, i01, i11, fx, fy);
                    int g = Sample(gg, i00, i10, i01, i11, fx, fy);
                    int b = Sample(gb, i00, i10, i01, i11, fx, fy);
                    outp[y * pw + x] = (r << 16) | (g << 8) | b;
                }
            }
            return outp;

            static int Sample(float[] a, int i00, int i10, int i01, int i11, double fx, double fy)
            {
                double v = (a[i00] * (1 - fx) + a[i10] * fx) * (1 - fy) + (a[i01] * (1 - fx) + a[i11] * fx) * fy;
                int c = (int)(v + 0.5);
                return c < 0 ? 0 : c > 255 ? 255 : c;
            }
        }

        /// <summary>A Gaussian of standard deviation <paramref name="sigma"/> as three box blurs; outside the image is empty.</summary>
        private static float[] Gauss(float[] src, int w, int h, double sigma)
        {
            var a = (float[])src.Clone();
            var b = new float[a.Length];
            foreach (int r in BoxesForGauss(sigma))
            {
                BoxH(a, b, w, h, r);
                BoxV(b, a, w, h, r);
            }
            return a;
        }

        private static int[] BoxesForGauss(double sigma)
        {
            const int n = 3;
            double wIdeal = Math.Sqrt(12 * sigma * sigma / n + 1);
            int wl = (int)Math.Floor(wIdeal);
            if (wl % 2 == 0) wl--;
            int wu = wl + 2;
            double mIdeal = (12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4.0 * wl - 4);
            int m = (int)Math.Round(mIdeal);
            var radii = new int[n];
            for (int i = 0; i < n; i++) radii[i] = ((i < m ? wl : wu) - 1) / 2;
            return radii;
        }

        private static void BoxH(float[] s, float[] d, int w, int h, int r)
        {
            float inv = 1f / (2 * r + 1);
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                float acc = 0;
                for (int x = 0; x <= r && x < w; x++) acc += s[row + x];
                for (int x = 0; x < w; x++)
                {
                    d[row + x] = acc * inv;
                    int add = x + r + 1, sub = x - r;
                    if (add < w) acc += s[row + add];
                    if (sub >= 0) acc -= s[row + sub];
                }
            }
        }

        private static void BoxV(float[] s, float[] d, int w, int h, int r)
        {
            float inv = 1f / (2 * r + 1);
            for (int x = 0; x < w; x++)
            {
                float acc = 0;
                for (int y = 0; y <= r && y < h; y++) acc += s[y * w + x];
                for (int y = 0; y < h; y++)
                {
                    d[y * w + x] = acc * inv;
                    int add = y + r + 1, sub = y - r;
                    if (add < h) acc += s[add * w + x];
                    if (sub >= 0) acc -= s[sub * w + x];
                }
            }
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
