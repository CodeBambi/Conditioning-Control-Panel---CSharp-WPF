using System;
using System.Collections.Generic;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// E-Stim lightning arc bolts on the compositor. Ports the WPF <c>ChaosSkiaFxOverlay</c>
/// bolt renderer (the DEFAULT arc visual when <c>ChaosSkiaFxEnabled ?? true</c>) onto a UCE
/// layer, replacing the standalone <c>ChaosEStimOverlay</c> window so the Electrified-Rabbits
/// free discharge renders through the single Skia compositor instead of a per-effect window.
///
/// Behavior contract (WPF <c>Chaos/ChaosSkiaFxOverlay.cs</c> bolt path, cited from the
/// wpf-archaeologist extract):
/// - each bolt is a jagged cyan polyline (Electric <c>#42DCE6</c>) between two endpoints,
///   built by perpendicular midpoint displacement: <c>mids = max(2, len/55)</c>, offset
///   <c>±16</c>, with <c>0..2</c> forks (<c>flen 20..75</c>) (WPF BuildBolt/Rejitter);
/// - <c>Life = 0.20s</c> fixed; re-jitters every <c>0.04s</c> while <c>Life &gt; Max*0.4</c>
///   so the current visibly dances (WPF :544);
/// - alpha envelope <c>a = t &gt; 0.4 ? 1 : t/0.4</c> (hold hot, then fade, WPF :369);
/// - draw: blurred glow stroke (5px blur, width 6.5, alpha <c>120*a</c>) + white-blue core
///   <c>#BFECFF</c> (width 1.8, alpha <c>235*a</c>); forks dimmer (core <c>160*a</c>);
///   endpoint bloom flashes — strike-end <c>fr = 16*a + 6</c> alpha <c>200*a</c>, source-end
///   <c>fr*0.7</c> alpha <c>150*a</c> (WPF DrawBolts :367-390).
///
/// Coordinate contract: bolt endpoints arrive as PHYSICAL virtual-desktop px — Core
/// <c>BubbleEngine.CenterPx</c> already multiplies the logical position by the bubble's
/// <c>Scaling</c>, so the endpoints are already in the layer's native space (no seam
/// conversion; see <see cref="IAvaloniaLayer"/>). The DIP-authored sizes (stroke widths,
/// endpoint flash radii) scale by the composited monitor's <c>Scaling</c> at render (WPF drew
/// in DIP under <c>canvas.Scale(dpi)</c>): at 100% scale this is exact, on HiDPI a seam-only
/// difference, matching <c>ChaosCursorGlowLayer</c>. The bolt jitter geometry uses the DIP
/// constants as px (monitor-agnostic, computed once per tick in <see cref="Update"/>).
///
/// The owning <c>AvaloniaChaosService</c> feeds <see cref="Strike"/> (UCE rule 7: the service
/// owns state, the layer only renders). Capture-VISIBLE (chaos FX appears in captures/streams;
/// no <c>WDA_EXCLUDEFROMCAPTURE</c>, matching every WPF chaos window).
/// </summary>
public sealed class ChaosEStimArcLayer : BaseLayer
{
    private const float BoltLifeSec = 0.20f;        // WPF :317
    private const float JitterIntervalSec = 0.04f;  // WPF :544
    private const float SegLenPx = 55f;             // WPF BuildBolt: mids = max(2, len/55)
    private const float JitterOffsetPx = 16f;       // WPF BuildBolt: offset ±16
    private static readonly SKColor ElectricColor = new(0x42, 0xDC, 0xE6); // ChaosBoonColors.Electric
    private static readonly SKColor BoltCoreColor = new(0xBF, 0xEC, 0xFF); // WPF BoltCoreColor
    private static readonly SKColorFilter ElectricCF =
        SKColorFilter.CreateBlendMode(ElectricColor, SKBlendMode.Modulate);

    private sealed class Bolt
    {
        public SKPoint A, B;
        public SKPoint[] Main = Array.Empty<SKPoint>();
        public readonly List<SKPoint[]> Branches = new();
        public float Life, Max, JitterAcc;
    }

    private readonly object _sync = new();
    private readonly List<Bolt> _bolts = new();
    private readonly Random _rng = new();

    // Paints built ONCE (UCE rule: no per-frame allocation); never disposed (the layer lives
    // app-long, same lifecycle as ChaosCursorGlowLayer / BubbleLayer paints).
    private readonly SKPaint _boltGlow = new()
    {
        Style = SKPaintStyle.Stroke, IsAntialias = true, BlendMode = SKBlendMode.Plus,
        StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
        MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 5f),
    };
    private readonly SKPaint _boltCore = new()
    {
        Style = SKPaintStyle.Stroke, IsAntialias = true, BlendMode = SKBlendMode.Plus,
        StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
    };
    private readonly SKPaint _flashPaint = new() { IsAntialias = true, BlendMode = SKBlendMode.Plus };

    // Soft radial sprite for the endpoint bloom flashes (white gradient, tinted cyan via
    // ElectricCF). Built once, shared, never disposed (WPF ChaosSkiaFxOverlay.Dot()).
    private static SKImage? _dot;

    public override int ZIndex => CompositorLayers.ChaosEStimArc;

    public override bool IsActive
    {
        get { lock (_sync) { return _bolts.Count > 0; } }
    }

    /// <summary>
    /// Flash cyan lightning bolt(s) between PHYSICAL virtual-desktop px endpoint pairs
    /// (WPF <c>ChaosSkiaFxOverlay.Strike</c>). Endpoints are already physical px from Core
    /// <c>CenterPx</c>; passed as raw <c>(fromX, fromY, toX, toY)</c> doubles so the layer
    /// carries no Avalonia/Core Point-type coupling.
    /// </summary>
    public void Strike(IReadOnlyList<(double FromX, double FromY, double ToX, double ToY)> boltsPx)
    {
        if (boltsPx == null || boltsPx.Count == 0) return;
        lock (_sync)
        {
            foreach (var (fx, fy, tx, ty) in boltsPx)
            {
                var bolt = new Bolt
                {
                    A = new SKPoint((float)fx, (float)fy),
                    B = new SKPoint((float)tx, (float)ty),
                    Life = BoltLifeSec, Max = BoltLifeSec,
                };
                Rejitter(bolt);
                _bolts.Add(bolt);
            }
        }
    }

    public override void Update(TimeSpan deltaTime)
    {
        float dt = (float)deltaTime.TotalSeconds;
        if (dt <= 0) return;
        if (dt > 0.1f) dt = 0.1f;

        lock (_sync)
        {
            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                var bolt = _bolts[i];
                bolt.Life -= dt;
                if (bolt.Life <= 0f) { _bolts.RemoveAt(i); continue; }
                bolt.JitterAcc += dt;
                // Re-jitter the current only while it is still hot (WPF :544), so the tail fade
                // holds a stable shape.
                if (bolt.Life > bolt.Max * 0.4f && bolt.JitterAcc >= JitterIntervalSec)
                {
                    bolt.JitterAcc = 0f;
                    Rejitter(bolt);
                }
            }
        }
    }

    public void Render(SKCanvas canvas, PixelRect bounds, ScreenInfo? screen, TimeSpan deltaTime)
    {
        float scale = (float)(screen != null && screen.Scaling > 0 ? screen.Scaling : 1.0);
        var img = Dot();
        lock (_sync)
        {
            for (int b = 0; b < _bolts.Count; b++)
            {
                var bolt = _bolts[b];
                float t = bolt.Life / bolt.Max;
                float a = t > 0.4f ? 1f : t / 0.4f;

                _boltGlow.Color = ElectricColor.WithAlpha((byte)(120 * a));
                _boltGlow.StrokeWidth = 6.5f * scale;
                _boltCore.Color = BoltCoreColor.WithAlpha((byte)(235 * a));
                _boltCore.StrokeWidth = 1.8f * scale;

                DrawPolyline(canvas, bolt.Main, _boltGlow);
                foreach (var br in bolt.Branches) DrawPolyline(canvas, br, _boltGlow);
                DrawPolyline(canvas, bolt.Main, _boltCore);
                _boltCore.Color = BoltCoreColor.WithAlpha((byte)(160 * a));
                foreach (var br in bolt.Branches) DrawPolyline(canvas, br, _boltCore);

                float fr = (16f * a + 6f) * scale;
                DrawFlash(canvas, img, bolt.B.X, bolt.B.Y, fr, (byte)(200 * a));
                DrawFlash(canvas, img, bolt.A.X, bolt.A.Y, fr * 0.7f, (byte)(150 * a));
            }
        }
    }

    public override void Render(SKCanvas canvas, PixelRect bounds, TimeSpan deltaTime)
        => Render(canvas, bounds, null, deltaTime);

    // ---- geometry (ported verbatim from WPF ChaosSkiaFxOverlay BuildBolt/Rejitter) ----

    private void Rejitter(Bolt bolt)
    {
        bolt.Main = BuildBolt(bolt.A, bolt.B);
        bolt.Branches.Clear();
        int forks = _rng.Next(3); // 0..2
        for (int k = 0; k < forks && bolt.Main.Length > 2; k++)
        {
            int idx = 1 + _rng.Next(bolt.Main.Length - 2);
            var origin = bolt.Main[idx];
            float ang = (float)(_rng.NextDouble() * Math.PI * 2);
            float flen = 20f + (float)_rng.NextDouble() * 55f;
            var end = new SKPoint(origin.X + MathF.Cos(ang) * flen, origin.Y + MathF.Sin(ang) * flen);
            bolt.Branches.Add(BuildBolt(origin, end));
        }
    }

    private SKPoint[] BuildBolt(SKPoint a, SKPoint b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        int mids = Math.Max(2, (int)(len / SegLenPx));
        float px = len > 0.001f ? -dy / len : 0f, py = len > 0.001f ? dx / len : 0f;
        var pts = new SKPoint[mids + 2];
        pts[0] = a;
        for (int m = 1; m <= mids; m++)
        {
            float f = m / (float)(mids + 1);
            float off = (float)(_rng.NextDouble() * 2 - 1) * JitterOffsetPx;
            pts[m] = new SKPoint(a.X + dx * f + px * off, a.Y + dy * f + py * off);
        }
        pts[mids + 1] = b;
        return pts;
    }

    private static void DrawPolyline(SKCanvas canvas, SKPoint[] pts, SKPaint paint)
    {
        if (pts.Length < 2) return;
        using var path = new SKPath();
        path.MoveTo(pts[0]);
        for (int i = 1; i < pts.Length; i++) path.LineTo(pts[i]);
        canvas.DrawPath(path, paint);
    }

    private void DrawFlash(SKCanvas canvas, SKImage img, float cx, float cy, float radius, byte alpha)
    {
        if (radius <= 0.2f || alpha == 0) return;
        _flashPaint.ColorFilter = ElectricCF;
        _flashPaint.Color = new SKColor(255, 255, 255, alpha);
        var dest = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        canvas.DrawImage(img, dest, _flashPaint);
    }

    private static SKImage Dot()
    {
        if (_dot != null) return _dot;
        const int s = 128;
        var info = new SKImageInfo(s, s, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surf = SKSurface.Create(info);
        var c = surf.Canvas;
        c.Clear(SKColors.Transparent);
        float r = s / 2f;
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(r, r), r,
            new[] { new SKColor(255, 255, 255, 255), new SKColor(255, 255, 255, 160), new SKColor(255, 255, 255, 0) },
            new[] { 0f, 0.32f, 1f }, SKShaderTileMode.Clamp);
        using var p = new SKPaint { Shader = shader, IsAntialias = true };
        c.DrawCircle(r, r, r, p);
        _dot = surf.Snapshot();
        return _dot;
    }
}
