using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Billboard.Board
{
    /// <summary>
    /// The board's effect maths, one function per effect, ported from the approved mockup
    /// (drawPosted, 2026-10-07). Pure and allocation-free so the renderer can call them for
    /// every tile every frame and the tests can pin each curve.
    /// </summary>
    public static class BoardFxMath
    {
        // ---- arrival: tiles pop up and land one by one over 900 ms -------------------------
        public const double BuildSeconds = 0.9;
        public const double BuildFlashBand = 0.07;
        public const double BuildLiftBand = 0.12;

        /// <summary>Build progress for <paramref name="seconds"/> since arrival. 1 = every tile placed.</summary>
        public static double BuildProgress(double seconds) => seconds / BuildSeconds;

        /// <summary>Seconds until the last tile has landed (its lift band has passed).</summary>
        public static double BuildDoneSeconds => (1 + BuildLiftBand) * BuildSeconds;

        public static bool BuildHidden(double hash, double build) => build < 1 && hash > build;

        public static bool BuildFlash(double hash, double build) => build < 1 && hash <= build && hash > build - BuildFlashBand;

        public static double BuildLift(double hash, double build)
        {
            if (build >= 1 + BuildLiftBand) return 0;
            if (hash > build || hash <= build - BuildLiftBand) return 0;
            return (hash - (build - BuildLiftBand)) / BuildLiftBand;
        }

        // ---- ola: a crest of lifted tiles rolling left to right, ~9 tiles wide, every ~3 s ---
        public const double OlaSpeed = 0.42;
        public const double OlaPeriod = 1.3;
        public const double OlaCrest = 0.13;
        public const double OlaSlantX = 0.85;
        public const double OlaSlantY = 0.18;

        // Owner, 2026-10-07: the ola is a STADIUM wave, not a ruler. Every tile keeps its own timer:
        // it stands up a beat early or late (+/- 120 ms around the crest reaching its column), takes
        // a little more or less time to rise and sit (+/- 20%) and jumps a little less or more
        // (0.75..1 of a full lift). All three come from the tile index, so they never change
        // between frames or launches; the crest still rolls left to right, its front ragged.
        public const double OlaJitterSeconds = 0.12;
        public const double OlaRiseJitter = 0.2;
        public const double OlaMinHeight = 0.75;

        private static readonly float[] OlaOffsetTable = BuildTable(stream: 1, -OlaJitterSeconds, OlaJitterSeconds);
        private static readonly float[] OlaRiseTable = BuildTable(stream: 2, 1 - OlaRiseJitter, 1 + OlaRiseJitter);
        private static readonly float[] OlaHeightTable = BuildTable(stream: 3, OlaMinHeight, 1.0);

        /// <summary>A tile's own ola timing: seconds early (-) or late (+), rise duration scale, jump height.</summary>
        public static (double OffsetSeconds, double RiseScale, double Height) OlaTile(int tile)
        {
            int i = (int)((uint)tile % (uint)OlaOffsetTable.Length);
            return (OlaOffsetTable[i], OlaRiseTable[i], OlaHeightTable[i]);
        }

        public static double OlaLift(double t, int x, int y, int w = BoardPicture.GridW, int h = BoardPicture.GridH)
        {
            int tile = y * BoardPicture.GridW + x;
            int i = (int)((uint)tile % (uint)OlaOffsetTable.Length);
            double crest = OlaCrest * OlaRiseTable[i];
            double ph = Mod((t - OlaOffsetTable[i]) * OlaSpeed - (double)x / w * OlaSlantX - (double)y / h * OlaSlantY, OlaPeriod);
            return ph < crest ? OlaHeightTable[i] * Math.Sin(ph / crest * Math.PI) : 0;
        }

        /// <summary>The same idea for the touch ripple: each tile answers the ring +/- 40 ms, at 0.8..1 of the lift.</summary>
        public const double RippleJitterSeconds = 0.04;
        private static readonly float[] RippleOffsetTable = BuildTable(stream: 4, -RippleJitterSeconds, RippleJitterSeconds);
        private static readonly float[] RippleHeightTable = BuildTable(stream: 5, 0.8, 1.0);

        public static double RippleLiftAt(int tile, double age, double distance)
        {
            int i = (int)((uint)tile % (uint)RippleOffsetTable.Length);
            return RippleHeightTable[i] * RippleLift(age - RippleOffsetTable[i], distance);
        }

        /// <summary>A stable value in [lo, hi) per tile, from an integer hash of (tile, stream). Never Random.</summary>
        public static double TileNoise(int tile, int stream)
        {
            unchecked
            {
                uint z = (uint)tile * 0x9E3779B9u + (uint)stream * 0x85EBCA6Bu + 0x27D4EB2Fu;
                z ^= z >> 16; z *= 0x7FEB352Du;
                z ^= z >> 15; z *= 0x846CA68Bu;
                z ^= z >> 16;
                return z / 4294967296.0;
            }
        }

        private static float[] BuildTable(int stream, double lo, double hi)
        {
            var a = new float[BoardPicture.Tiles];
            for (int i = 0; i < a.Length; i++) a[i] = (float)(lo + (hi - lo) * TileNoise(i, stream));
            return a;
        }

        /// <summary>Seconds between two crests.</summary>
        public static double OlaEverySeconds => OlaPeriod / OlaSpeed;

        /// <summary>Width of the crest in tiles across the grid.</summary>
        public static double OlaCrestTiles(int w = BoardPicture.GridW) => OlaCrest / OlaSlantX * w;

        // ---- ripple: a ring of lifted tiles out from a touched tile -------------------------
        public const double RippleSpeed = 30;    // tiles per second
        public const double RippleWidth = 4;     // tiles
        public const double RippleLife = 1.6;    // seconds of amplitude
        public const double RippleDropAfter = 1.8;

        public static double RippleLift(double age, double distance)
        {
            if (age < 0) return 0;
            double f = distance - age * RippleSpeed;
            if (f >= 0 || f <= -RippleWidth) return 0;
            return Math.Sin(-f / RippleWidth * Math.PI) * Math.Max(0, 1 - age / RippleLife);
        }

        // ---- twinkle: bright tiles shimmer on their own clocks ------------------------------
        public static double Twinkle(double t, double hash) => 0.3 + 0.7 * Math.Abs(Math.Sin(t * 2.4 + hash * 40));

        // ---- cycle: saturated tiles step through the hue wheel in a band moving across ------
        public static int CycleStep(double t, int x) => (int)Mod(Math.Floor(t * 2.2 + x / 8.0), 3);

        /// <summary>The three colours a saturated tile cycles through (its own, +120, +240 degrees), or null.</summary>
        public static int[]? CycleColours(int r, int g, int b)
        {
            RgbToHsv(r, g, b, out var hue, out var s, out var v);
            if (s < 0.45 || v < 0.35) return null;
            return new[]
            {
                (r << 16) | (g << 8) | b,
                HsvToRgb(hue + 120, s, v),
                HsvToRgb(hue + 240, s, v),
            };
        }

        // ---- chase: lit tiles on the picture's edge light up in a running chase -------------
        /// <summary>Index round the rectangle, clockwise from the top-left; -1 inside it.</summary>
        public static int Perimeter(int x, int y, int x0, int y0, int w, int h)
        {
            int lx = x - x0, ly = y - y0;
            if (lx < 0 || ly < 0 || lx >= w || ly >= h) return -1;
            if (ly == 0) return lx;
            if (lx == w - 1) return w - 1 + ly;
            if (ly == h - 1) return w - 1 + h - 1 + (w - 1 - lx);
            if (lx == 0) return 2 * (w - 1) + h - 1 + (h - 1 - ly);
            return -1;
        }

        public static bool ChaseOn(int perimeter, double t) => Mod(perimeter - Math.Floor(t * 16), 8) < 3;

        public const int ChaseRgb = (255 << 16) | (236 << 8) | 246;
        public const double ChaseOffDim = 0.55;

        // ---- shine: a diagonal glint sweeps the lit tiles ------------------------------------
        public static double Shine(double t, int x, int y, int w = BoardPicture.GridW, int h = BoardPicture.GridH)
        {
            double p = (Mod(t * 0.45, 1.8) - 0.3) * (w + h);
            double d = Math.Abs(x + y - p);
            return d < 2.5 ? (1 - d / 2.5) * 0.75 : 0;
        }

        // ---- wave: columns bob up and down like a flag ---------------------------------------
        public static int WaveOffset(double t, int x) => (int)Math.Round(Math.Sin(t * 2.6 + x * 0.28));

        // ---- glow: the message lights the field round it -------------------------------------
        public const double GlowBreathPeriod = 4.0;
        public const double GlowBreathLow = 0.8;
        public const double GlowStill = 0.9;

        /// <summary>
        /// The one scale the baked glow is drawn at this frame: a slow breath (0.8..1.0 every 4 s)
        /// while ambient loops may run, a steady 0.9 otherwise, times the arrival so the field never
        /// glows where tiles have not landed yet. Pure.
        /// </summary>
        public static double GlowStrength(double t, double buildSeconds, bool still, bool breathe)
        {
            double arrival = still ? 1 : Math.Clamp(BuildProgress(buildSeconds), 0, 1);
            double breath = breathe && !still
                ? GlowBreathLow + (1 - GlowBreathLow) * (0.5 + 0.5 * Math.Sin(t * 2 * Math.PI / GlowBreathPeriod))
                : GlowStill;
            return arrival * breath;
        }

        // ---- crt: scanlines, vignette, a faint flicker ---------------------------------------
        public static double CrtFlicker(double t) => 0.012 + 0.012 * Math.Sin(t * 40);

        // ---- per-tile hash, the same sequence the mockup uses -------------------------------
        private static readonly float[] HashTable = BuildHash();

        public static double Hash(int tile) => HashTable[(uint)tile % (uint)HashTable.Length];

        private static float[] BuildHash()
        {
            var h = new float[BoardPicture.Tiles];
            long s = 7;
            for (int i = 0; i < h.Length; i++) { s = s * 16807 % 2147483647; h[i] = (float)(s / 2147483647.0); }
            return h;
        }

        // ---- colour helpers ------------------------------------------------------------------
        public static double Luma(int r, int g, int b) => (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;

        public static int Scale(int rgb, double m)
        {
            if (m >= 0.999) return rgb;
            if (m <= 0) return 0;
            int r = (int)(((rgb >> 16) & 0xFF) * m), g = (int)(((rgb >> 8) & 0xFF) * m), b = (int)((rgb & 0xFF) * m);
            return (r << 16) | (g << 8) | b;
        }

        public static int TowardWhite(int rgb, double s)
        {
            if (s <= 0) return rgb;
            int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
            r += (int)((255 - r) * s); g += (int)((255 - g) * s); b += (int)((255 - b) * s);
            return (r << 16) | (g << 8) | b;
        }

        public static void RgbToHsv(int r, int g, int b, out double h, out double s, out double v)
        {
            double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
            double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf)), d = max - min;
            v = max;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0) { h = 0; return; }
            if (max == rf) h = 60 * Mod((gf - bf) / d, 6);
            else if (max == gf) h = 60 * ((bf - rf) / d + 2);
            else h = 60 * ((rf - gf) / d + 4);
        }

        public static int HsvToRgb(double h, double s, double v)
        {
            h = Mod(h, 360);
            double c = v * s, x = c * (1 - Math.Abs(Mod(h / 60, 2) - 1)), m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            int R = (int)Math.Round((r + m) * 255), G = (int)Math.Round((g + m) * 255), B = (int)Math.Round((b + m) * 255);
            return (Math.Clamp(R, 0, 255) << 16) | (Math.Clamp(G, 0, 255) << 8) | Math.Clamp(B, 0, 255);
        }

        public static double Mod(double a, double n) => ((a % n) + n) % n;
    }

    /// <summary>A touch on the board: a ripple centre in tile units and the clock time it started.</summary>
    public readonly record struct BoardRipple(double X, double Y, double StartSeconds);

    /// <summary>Which effects a post asked for, as flags the renderer can test cheaply.</summary>
    public readonly record struct BoardFxSet(bool Ola, bool Twinkle, bool Cycle, bool Shine, bool Chase, bool Wave, bool Crt)
    {
        public static BoardFxSet From(IReadOnlyList<string>? fx)
        {
            bool Has(string n) => fx != null && System.Linq.Enumerable.Contains(fx, n, StringComparer.OrdinalIgnoreCase);
            return new BoardFxSet(Has(BoardFx.Ola), Has(BoardFx.Twinkle), Has(BoardFx.Cycle), Has(BoardFx.Shine),
                Has(BoardFx.Chase), Has(BoardFx.Wave), Has(BoardFx.Crt));
        }

        /// <summary>True when something on the board moves with time (an idle board stops redrawing).</summary>
        public bool Animates => Ola || Twinkle || Cycle || Shine || Chase || Wave || Crt;
    }

    /// <summary>
    /// One frame of the board as tiles: the colour each tile shows (before its bevel) and how far
    /// it is lifted (0 resting, 1 at the top of a crest). Pure: same inputs, same scene.
    /// </summary>
    public static class BoardScene
    {
        /// <param name="t">Effect time in seconds (it stands still while the card is paused).</param>
        /// <param name="buildSeconds">Effect seconds since the arrival began.</param>
        /// <param name="ripples">Touches, timed on <paramref name="rippleNow"/>'s clock.</param>
        /// <param name="rippleNow">The ripple clock now; it keeps running under a paused card so a touch still spreads.</param>
        /// <param name="still">A board that never played (Motion Off): the flat picture, no motion at all.</param>
        public static void Compute(BoardPicture pic, int frame, BoardFxSet fx, double t, double buildSeconds,
            IReadOnlyList<BoardRipple>? ripples, double rippleNow, bool still, int[] colour, float[] lift)
            => Compute(pic, frame, fx, t, buildSeconds, ripples, rippleNow, still, colour, lift, null);

        /// <param name="role">Optional: what each tile is to the message this frame (it follows the wave; a tile not landed yet is field).</param>
        public static void Compute(BoardPicture pic, int frame, BoardFxSet fx, double t, double buildSeconds,
            IReadOnlyList<BoardRipple>? ripples, double rippleNow, bool still, int[] colour, float[] lift, BoardTileRole[]? role)
        {
            const int W = BoardPicture.GridW, H = BoardPicture.GridH;
            frame = Math.Clamp(frame, 0, pic.FrameCount - 1);
            var rgb = pic.Rgb[frame];
            var lit = pic.Lit[frame];
            var bright = pic.Bright[frame];
            var cyc = pic.CycleRgb[frame];
            var roles = pic.Role[frame];
            var empty = pic.EmptyRole;
            double build = still ? 2 : BoardFxMath.BuildProgress(buildSeconds);
            double te = still ? 0 : t;
            bool building = build < 1 + BoardFxMath.BuildLiftBand;

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    int sy = fx.Wave && !still ? y - BoardFxMath.WaveOffset(te, x) : y;
                    int c;
                    bool isLit;
                    int src = -1;
                    var ro = empty;
                    if (sy >= 0 && sy < H) { src = sy * W + x; c = rgb[src]; isLit = lit[src]; ro = roles[src]; }
                    else { c = BoardPicture.BaseRgb; isLit = false; }

                    double m = 1;
                    if (isLit)
                    {
                        if (fx.Cycle && !still && (int[]?)cyc[src] is { } three) c = three[BoardFxMath.CycleStep(te, x)];
                        if (fx.Twinkle && bright[src]) m = BoardFxMath.Twinkle(te, BoardFxMath.Hash(i));
                        if (fx.Chase)
                        {
                            int p = BoardFxMath.Perimeter(x, y, pic.X0, pic.Y0, pic.PicW, pic.PicH);
                            if (p >= 0)
                            {
                                if (BoardFxMath.ChaseOn(p, te)) { c = BoardFxMath.ChaseRgb; m = 1; }
                                else m *= BoardFxMath.ChaseOffDim;
                            }
                        }
                        if (fx.Shine) c = BoardFxMath.TowardWhite(c, BoardFxMath.Shine(te, x, y));
                    }

                    double z = 0;
                    if (building)
                    {
                        double hv = BoardFxMath.Hash(i);
                        if (BoardFxMath.BuildHidden(hv, build)) { c = BoardPicture.BaseRgb; m = 1; ro = empty; }
                        else if (isLit && BoardFxMath.BuildFlash(hv, build)) { c = 0xFFFFFF; m = 1; }
                        z = BoardFxMath.BuildLift(hv, build);
                    }

                    if (fx.Ola && !still) z = Math.Max(z, BoardFxMath.OlaLift(te, x, y));
                    if (ripples != null && !still)
                    {
                        for (int k = 0; k < ripples.Count; k++)
                        {
                            var rp = ripples[k];
                            double dx = x - rp.X, dy = y - rp.Y;
                            z = Math.Max(z, BoardFxMath.RippleLiftAt(i, rippleNow - rp.StartSeconds, Math.Sqrt(dx * dx + dy * dy)));
                        }
                    }

                    colour[i] = BoardFxMath.Scale(c, m);
                    lift[i] = (float)z;
                    if (role != null) role[i] = ro;
                }
            }
        }
    }
}
