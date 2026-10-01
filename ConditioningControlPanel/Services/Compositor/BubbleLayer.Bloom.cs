using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services.Super;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Super Inner Bloom drawing: the iridescent soap film (14 hue arcs at 0.88R, window highlight,
/// bottom caustic, thin rim), the kids orbiting inside it, the pop burst (squeeze, tear, droplets,
/// ring) and the clean sweep (gold ring, sparks, the word). Positions and timings come from
/// <see cref="InnerBloom"/>. Every film is drawn in its own unit space (translate + scale), so the
/// shaded fill, the per-hue glows and the hue faces are built once and reused: no per-frame shader.
/// UI thread only, like the rest of the layer.
/// </summary>
public sealed partial class BubbleLayer
{
    private const float Tau = MathF.PI * 2f;

    private enum BloomFxKind { Burst, Clean, KidPop }

    private sealed class BloomFx
    {
        public BloomFxKind Kind;
        public int Hue;
        public InnerBloom.Node? Node;
        public float X, Y, R;
        public long StartMs;
        public InnerBloom.Motion Motion;
        public string? Word;
        public float[] SparkAngle = System.Array.Empty<float>(), SparkSpeed = System.Array.Empty<float>();
    }

    private readonly List<BloomFx> _bloomFx = new();
    private readonly SKPath _filmPath = new();
    private readonly InnerBloom.Bulge[] _bulgeBuf = new InnerBloom.Bulge[4];
    private readonly int[] _kidOrder = new int[4];
    private readonly double[] _kidZ = new double[4];
    private SKPath? _leafClip;
    private readonly Dictionary<int, SKShader> _hueGlow = new(), _hueFace = new();
    private readonly Dictionary<BitmapSource, SKImage> _bloomPics = new();
    private SKShader? _filmShade;
    private SKMaskFilter? _cleanGlow;

    public void AddBloomBurst(InnerBloom.Node node, double xPx, double yPx, double rPx, long startMs, InnerBloom.Motion motion)
        => AddFx(new BloomFx { Node = node, X = (float)xPx, Y = (float)yPx, R = (float)rPx, StartMs = startMs, Motion = motion },
                 InnerBloom.Droplets, 110f, (int)startMs);

    public void AddBloomClean(double xPx, double yPx, double rPx, string word, InnerBloom.Motion motion)
        => AddFx(new BloomFx { Kind = BloomFxKind.Clean, X = (float)xPx, Y = (float)yPx, R = (float)rPx, Word = word,
                               StartMs = System.Environment.TickCount64, Motion = motion },
                 InnerBloom.SweepSparks + 20, 260f, (int)xPx * 31 + (int)yPx);

    /// <summary>A released kid popped: a ring and a spray of sparks in its own hue (the mockup's kid pop).</summary>
    public void AddBloomKidPop(double xPx, double yPx, double rPx, int hue, InnerBloom.Motion motion)
        => AddFx(new BloomFx { Kind = BloomFxKind.KidPop, Hue = hue, X = (float)xPx, Y = (float)yPx, R = (float)rPx,
                               StartMs = System.Environment.TickCount64, Motion = motion },
                 InnerBloom.KidPopSparks, 140f, (int)xPx * 17 + (int)yPx + hue);

    private void AddFx(BloomFx fx, int sparks, float speed, int seed)
    {
        var r = new System.Random(seed);
        fx.SparkAngle = new float[sparks];
        fx.SparkSpeed = new float[sparks];
        for (int i = 0; i < sparks; i++)
        {
            fx.SparkAngle[i] = (float)(r.NextDouble() * Tau);
            // Reduced motion halves how far the sparks fly (Off never gets here: the callers skip it).
            fx.SparkSpeed[i] = speed * (float)fx.Motion.Amp * (float)(.45 + .55 * r.NextDouble());
        }
        _bloomFx.Add(fx);
        _dirty = true;
        SetActive(true);
    }

    /// <summary>Panic, Stop or the switch going off: every burst and sweep goes at once.</summary>
    public void ClearBlooms()
    {
        _bloomFx.Clear();
        foreach (var img in _bloomPics.Values) { try { img.Dispose(); } catch { } }
        _bloomPics.Clear();
        _dirty = true;
        if (_items.Count == 0) SetActive(false);
    }

    private void DrawBloomItem(SKCanvas canvas, BubbleItem item, float cx, float cy)
    {
        var node = item.Bloom!;
        float r = (float)(item.SizeDip * InnerBloom.GlassOfSprite) * item.DpiScale * item.Scale;
        var m = InnerBloom.Motion.For(MotionFx.Level);
        // No dirt of its own: the bloom moves at the field's step cadence (SyncLayerItem marks it), #853.
        DrawNode(canvas, node, cx, cy, r, node.TimeAt(System.Environment.TickCount64), m, 0, item.Opacity, (float)node.Seed, true);
    }

    /// <summary>A bubble that carries kids: kid glows inside its film, the kids, then its own film on top.</summary>
    private void DrawNode(SKCanvas c, InnerBloom.Node node, float x, float y, float r, double t,
                          InnerBloom.Motion m, double squeeze, float alpha, float ph, bool withKids)
    {
        if (r < 1f || alpha <= .003f) return;
        int save = c.Save();
        c.Translate(x, y);
        if (squeeze > 0)
        {
            float sq = (float)(InnerBloom.SqueezeAmount * squeeze);
            c.Scale(1f + sq, 1f - sq);
        }
        c.Scale(r, r);
        int nb = InnerBloom.Bulges(node, t, 1, node.Seed, m, _bulgeBuf);
        var bul = new ReadOnlySpan<InnerBloom.Bulge>(_bulgeBuf, 0, nb);
        double ft = t * m.Speed;   // the film's shimmer obeys MotionFx too: still under Off, half under Reduced
        if (withKids && node.Kids.Count > 0)
        {
            // Depth order: the far side of the tilted orbit first, so a near kid passes in front.
            // Only the big bloom's kids travel through depth; a carrier's smalls stay flat.
            int n = Math.Min(node.Kids.Count, _kidOrder.Length);
            for (int j = 0; j < n; j++)
            {
                _kidOrder[j] = j;
                _kidZ[j] = node.IsRoot ? InnerBloom.Depth(InnerBloom.Place(node, j, t, 1, m).Angle, m) : 0;
            }
            for (int i = 1; i < n; i++)
                for (int k = i; k > 0 && _kidZ[_kidOrder[k]] < _kidZ[_kidOrder[k - 1]]; k--)
                    (_kidOrder[k], _kidOrder[k - 1]) = (_kidOrder[k - 1], _kidOrder[k]);
            // The recursion below reuses these buffers for a carrier's smalls: keep this level's copy.
            int o0 = _kidOrder[0], o1 = _kidOrder[1], o2 = _kidOrder[2];
            double z0 = _kidZ[0], z1 = _kidZ[1], z2 = _kidZ[2];

            // Each kid glows its own hue inside the big film.
            int gs = c.Save();
            BuildFilmPath(bul);
            c.ClipPath(_filmPath, antialias: true);
            _fill.BlendMode = SKBlendMode.Plus;
            for (int j = 0; j < n; j++)
            {
                var at = InnerBloom.Place(node, j, t, 1, m);
                _fill.Shader = HueGlow(node.Kids[j].Hue);
                _fill.Color = SKColors.White.WithAlpha(A(InnerBloom.DepthAlpha(_kidZ[j]), alpha));
                float gr = (float)at.Radius * 2.6f;
                c.DrawCircle((float)at.X, (float)at.Y, gr, _fill);
            }
            _fill.Shader = null;
            _fill.BlendMode = SKBlendMode.SrcOver;
            c.RestoreToCount(gs);
            for (int o = 0; o < n; o++)
            {
                int j = o == 0 ? o0 : o == 1 ? o1 : o2;
                double z = j == 0 ? z0 : j == 1 ? z1 : z2;
                var kid = node.Kids[j];
                var at = InnerBloom.Place(node, j, t, 1, m);
                float ka = alpha * (float)InnerBloom.DepthAlpha(z);
                if (kid.IsCarrier)
                    DrawNode(c, kid, (float)at.X, (float)at.Y, (float)at.Radius, t, m, 0, ka, j * 2 + ph, true);
                else
                {
                    int ks = c.Save();
                    c.Translate((float)at.X, (float)at.Y);
                    c.Scale((float)at.Radius);
                    DrawFilm(c, ReadOnlySpan<InnerBloom.Bulge>.Empty, ft, j * 2 + ph, kid.Hue, Picture(kid), ka);
                    c.RestoreToCount(ks);
                }
            }
        }
        DrawFilm(c, bul, ft, ph, -1, null, alpha);
        c.RestoreToCount(save);
    }

    /// <summary>One film in unit space (radius 1). hue &gt;= 0 = a leaf: its picture (or a hue face) sits inside.</summary>
    private void DrawFilm(SKCanvas c, ReadOnlySpan<InnerBloom.Bulge> bul, double t, double ph, int hue, SKImage? pic, float alpha)
    {
        float px = 1f / Math.Max(1e-3f, c.TotalMatrix.ScaleX);   // one device pixel in unit space
        if (hue >= 0)
        {
            int ps = c.Save();
            var box = new SKRect(-.8f, -.8f, .8f, .8f);
            if (_leafClip == null) { _leafClip = new SKPath(); _leafClip.AddCircle(0, 0, .8f); }
            c.ClipPath(_leafClip, antialias: true);   // a cached path: no native object per leaf per frame
            if (pic != null)
            {
                _img.Color = SKColors.White.WithAlpha(A(.92, alpha));
                c.DrawImage(pic, FillFit(pic, box), _img);
            }
            else
            {
                _fill.Shader = HueFace(hue);
                _fill.Color = SKColors.White.WithAlpha(A(1, alpha));
                c.DrawRect(box, _fill);
                _fill.Shader = null;
            }
            c.RestoreToCount(ps);
        }
        BuildFilmPath(bul);
        // Two-point conical, like the mockup's createRadialGradient(x-.3r, y-.3r, .05r, x, y, 1.05r):
        // the light sits upper left but the rim brightens evenly all round, which is what reads as a sphere.
        _fill.Shader = _filmShade ??= SKShader.CreateTwoPointConicalGradient(new SKPoint(-.3f, -.3f), .05f, new SKPoint(0, 0), 1.05f,
            new[] { new SKColor(255, 255, 255, 10), new SKColor(180, 200, 255, 20), new SKColor(255, 255, 255, 56) },
            new[] { 0f, .8f, 1f }, SKShaderTileMode.Clamp);
        _fill.Color = SKColors.White.WithAlpha(A(1, alpha));
        c.DrawPath(_filmPath, _fill);
        _fill.Shader = null;

        int cs = c.Save();
        c.ClipPath(_filmPath, antialias: true);
        _stroke.StrokeWidth = .2f;
        float ar = (float)InnerBloom.FilmArcRadius;
        var arcBox = new SKRect(-ar, -ar, ar, ar);
        float sweep = 360f / InnerBloom.FilmArcs * 1.2f;
        for (int i = 0; i < InnerBloom.FilmArcs; i++)
        {
            double a0 = (double)i / InnerBloom.FilmArcs * Tau + t * .5 + ph;
            float h = (float)((i * 26 + t * 40 + ph * 50) % 360 + 360) % 360;
            _stroke.Color = SKColor.FromHsl(h, 90, 70, A(.34, alpha));
            c.DrawArc(arcBox, (float)(a0 * 180 / Math.PI), sweep, false, _stroke);
        }
        c.RestoreToCount(cs);

        _stroke.StrokeWidth = 1.5f * px;
        _stroke.Color = SKColors.White.WithAlpha(A(.62, alpha));
        c.DrawPath(_filmPath, _stroke);
        int ws = c.Save();
        c.Translate(-.4f, -.42f);
        c.RotateRadians(-.7f);
        _fill.Color = SKColors.White.WithAlpha(A(.8, alpha));
        c.DrawOval(0, 0, .2f, .1f, _fill);
        c.RestoreToCount(ws);
        _stroke.StrokeWidth = Math.Max(px, .05f);
        _stroke.Color = SKColors.White.WithAlpha(A(.3, alpha));
        c.DrawArc(new SKRect(-.78f, -.78f, .78f, .78f), .3f * 180f, .42f * 180f, false, _stroke);
    }

    /// <summary>The 56-point film polygon, dented outward wherever a kid presses (unit space, reused path).</summary>
    private void BuildFilmPath(ReadOnlySpan<InnerBloom.Bulge> bul)
    {
        _filmPath.Rewind();
        for (int i = 0; i <= InnerBloom.FilmPoints; i++)
        {
            double a = (double)i / InnerBloom.FilmPoints * Tau;
            float k = (float)InnerBloom.RadiusFactor(a, bul);
            float x = MathF.Cos((float)a) * k, y = MathF.Sin((float)a) * k;
            if (i == 0) _filmPath.MoveTo(x, y); else _filmPath.LineTo(x, y);
        }
        _filmPath.Close();
    }

    private void DrawBloomFx(SKCanvas c)
    {
        long now = System.Environment.TickCount64;
        for (int i = _bloomFx.Count - 1; i >= 0; i--)
        {
            var fx = _bloomFx[i];
            float u = (now - fx.StartMs) / 1000f;
            bool alive = fx.Kind switch
            {
                BloomFxKind.Clean => DrawClean(c, fx, u),
                BloomFxKind.KidPop => DrawKidPop(c, fx, u),
                _ => DrawBurst(c, fx, u),
            };
            if (!alive) _bloomFx.RemoveAt(i);
        }
        if (_bloomFx.Count == 0 && _items.Count == 0) SetActive(false);
    }

    /// <summary>Squeeze 0.25 s with the kids still inside, then the tear runs round the rim, 22 droplets fly, a ring spreads.</summary>
    private bool DrawBurst(SKCanvas c, BloomFx fx, float u)
    {
        var node = fx.Node!;
        if (fx.Motion.Still)
        {
            // Motion Off: no tear, the film fades in 120 ms.
            float a = 1f - u / (float)InnerBloom.StillFadeS;
            if (a > 0) DrawNode(c, node, fx.X, fx.Y, fx.R, 0, fx.Motion, 0, a, (float)node.Seed, false);
            return a > 0;
        }
        float squeeze = (float)InnerBloom.SqueezeS;
        if (u < squeeze)
        {
            DrawNode(c, node, fx.X, fx.Y, fx.R, node.TimeAt(System.Environment.TickCount64), fx.Motion,
                     InnerBloom.Squeeze(u), 1f, (float)node.Seed, true);
            return true;
        }
        float v = u - squeeze;
        if (v < InnerBloom.TearS)
        {
            float g = (float)InnerBloom.TearGap(v), a0 = (float)node.Seed;
            float al = 1f - v / (float)InnerBloom.TearS;
            _stroke.StrokeWidth = 3f;
            _stroke.Color = new SKColor(255, 255, 255, (byte)(.85f * al * 255));
            Arc(c, fx.X, fx.Y, fx.R * (1 + v * .3f), a0 + g, Tau - 2 * g);
            _stroke.StrokeWidth = 1.5f;
            _stroke.Color = new SKColor(170, 200, 255, (byte)(.6f * al * 255));
            Arc(c, fx.X, fx.Y, fx.R * (1 + v * .5f), a0 + g * 1.2f, Tau - 2.4f * g);
        }
        if (v < InnerBloom.SweepRingS)
        {
            float q = v / (float)InnerBloom.SweepRingS;
            _stroke.StrokeWidth = 2f;
            _stroke.Color = new SKColor(255, 255, 255, (byte)(.7f * (1 - q) * 255));
            c.DrawCircle(fx.X, fx.Y, fx.R * (1 + q * 1.2f), _stroke);
        }
        const float life = .9f;
        if (v >= life) return false;
        _fill.Color = new SKColor(0xCF, 0xE6, 0xFF, (byte)((1 - v / life) * 255));
        Sparks(c, fx, v, fx.R, 0, fx.SparkAngle.Length, 2f);
        return true;
    }

    /// <summary>Clean sweep: gold ring, 46 gold sparks and 20 white, and the word floating up. Visual only.</summary>
    private bool DrawClean(SKCanvas c, BloomFx fx, float u)
    {
        if (u >= InnerBloom.CleanFloatS) return false;
        if (u < InnerBloom.SweepRingS)
        {
            float q = u / (float)InnerBloom.SweepRingS;
            _stroke.StrokeWidth = 3f;
            _stroke.Color = new SKColor(255, 207, 107, (byte)((1 - q) * 255));
            c.DrawCircle(fx.X, fx.Y, fx.R * .5f * (1 + q * (float)InnerBloom.SweepRingGrow), _stroke);
        }
        if (u < 1f)
        {
            _fill.Color = new SKColor(255, 207, 107, (byte)((1 - u) * 255));
            Sparks(c, fx, u, 0, 0, InnerBloom.SweepSparks, 2.4f);
            _fill.Color = new SKColor(255, 255, 255, (byte)(Math.Max(0, 1 - u / .8f) * 255));
            Sparks(c, fx, u, 0, InnerBloom.SweepSparks, fx.SparkAngle.Length, 1.8f);
        }
        if (!string.IsNullOrEmpty(fx.Word))
        {
            float q = u / (float)InnerBloom.CleanFloatS, a = 1 - q * q;
            _text.TextSize = Math.Max(16f, fx.R * .32f);
            float y = fx.Y - fx.R * .2f - q * fx.R * .6f + _text.TextSize * .35f;
            _text.Typeface = _bold;
            _text.MaskFilter = _cleanGlow ??= SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f);
            _text.Color = new SKColor(255, 207, 107, (byte)(a * 255));
            c.DrawText(fx.Word, fx.X, y, _text);
            _text.MaskFilter = null;
            _text.Color = new SKColor(255, 226, 154, (byte)(a * 255));
            c.DrawText(fx.Word, fx.X, y, _text);
        }
        return true;
    }

    /// <summary>A kid's pop: a white ring spreading 1.2x over half a second and its hue sparks for 0.7 s.</summary>
    private bool DrawKidPop(SKCanvas c, BloomFx fx, float u)
    {
        const float life = .7f;
        if (u >= life) return false;
        if (u < InnerBloom.SweepRingS)
        {
            float q = u / (float)InnerBloom.SweepRingS;
            _stroke.StrokeWidth = 2f;
            _stroke.Color = new SKColor(255, 255, 255, (byte)(.7f * (1 - q) * 255));
            c.DrawCircle(fx.X, fx.Y, fx.R * (1 + q * 1.2f * (float)fx.Motion.Amp), _stroke);
        }
        _fill.Color = SKColor.FromHsl(fx.Hue, 90, 72, (byte)((1 - u / life) * 255));
        Sparks(c, fx, u, fx.R * .5f, 0, fx.SparkAngle.Length, 2f);
        return true;
    }

    /// <summary>Sparks [from, to): outward with drag and a little fall. Colour comes from _fill.</summary>
    private void Sparks(SKCanvas c, BloomFx fx, float u, float startR, int from, int to, float size)
    {
        float travel = (1 - MathF.Exp(-3f * u)) / 3f;
        for (int i = from; i < to; i++)
        {
            float a = fx.SparkAngle[i], sp = fx.SparkSpeed[i];
            float d = startR + sp * travel;
            c.DrawCircle(fx.X + MathF.Cos(a) * d, fx.Y + MathF.Sin(a) * d + 40f * u * u, size, _fill);
        }
    }

    private void Arc(SKCanvas c, float x, float y, float r, float startRad, float sweepRad)
    {
        if (sweepRad <= 0) return;
        c.DrawArc(new SKRect(x - r, y - r, x + r, y + r), startRad * 180f / MathF.PI, sweepRad * 180f / MathF.PI, false, _stroke);
    }

    private SKShader HueGlow(int hue)
    {
        if (!_hueGlow.TryGetValue(hue, out var sh))
        {
            sh = SKShader.CreateRadialGradient(new SKPoint(0, 0), 1f,
                new[] { SKColor.FromHsl(hue, 90, 60, 77), SKColor.FromHsl(hue, 90, 60, 0) }, null, SKShaderTileMode.Clamp);
            _hueGlow[hue] = sh;
        }
        return sh;
    }

    private SKShader HueFace(int hue)
    {
        if (!_hueFace.TryGetValue(hue, out var sh))
        {
            sh = SKShader.CreateLinearGradient(new SKPoint(-1, -1), new SKPoint(1, 1),
                new[] { SKColor.FromHsl(hue, 80, 62), SKColor.FromHsl((hue + 50) % 360, 70, 34) }, null, SKShaderTileMode.Clamp);
            _hueFace[hue] = sh;
        }
        return sh;
    }

    /// <summary>A leaf's decoded still as a Skia image, converted once (bounded cache).</summary>
    private SKImage? Picture(InnerBloom.Node leaf)
    {
        if (leaf.Picture is not BitmapSource src) return null;
        if (_bloomPics.TryGetValue(src, out var img)) return img;
        if (_bloomPics.Count >= 24)   // bound it the way the tease frame cache is bounded
        {
            foreach (var v in _bloomPics.Values) { try { v.Dispose(); } catch { } }
            _bloomPics.Clear();
        }
        try { img = SkiaWpfInterop.ToSKImage(src); }
        catch { return null; }
        _bloomPics[src] = img;
        return img;
    }

    private static byte A(double a, float alpha) => (byte)Math.Clamp(a * alpha * 255, 0, 255);
}
