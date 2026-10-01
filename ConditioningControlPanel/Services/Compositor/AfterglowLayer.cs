using System.Runtime.InteropServices;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Super Afterglow, the drawing half. Ghost words left by subliminal cards, their embers, the
/// wake sparks and shock rings. All maths lives in <see cref="AfterglowField"/>; this layer only
/// feeds it the cursor and paints what it holds.
///
/// Main surface like <see cref="SubliminalLayer"/>: subliminals record (WDA_NONE) by design, so the
/// ghosts do too, and <see cref="GetActiveTextRectsPx"/> hands the awareness OCR their rects the way
/// the card does. Click-through host, so the cursor is polled (GetCursorPos), never hooked.
/// </summary>
public sealed class AfterglowLayer : BaseLayer
{
    private const float FontDip = 120f;      // SubliminalLayer parity: the ghost is the card's word
    private const float OcrPadDip = 40f;     // SubliminalLayer parity
    private static readonly SKColor GhostFill = new(255, 235, 250);
    private static readonly SKColor GhostGlow = new(255, 95, 176);
    private static readonly SKColor Spark = new(255, 155, 224);
    private static readonly SKColor RingCol = new(255, 180, 230);

    private static readonly SKTypeface BoldArial =
        SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;

    private sealed class Draw
    {
        public GlyphFallback.TextRun[] Runs = Array.Empty<GlyphFallback.TextRun>();
        public float[] Widths = Array.Empty<float>();
        public float Total, Height, BaselineOffset, Scale;
    }

    private readonly AfterglowField _field = new();
    private readonly object _sync = new();
    private readonly Random _rng = new();
    private readonly Func<double> _rnd;
    private readonly List<AfterglowGhost> _woken = new();
    private readonly List<string> _wokenText = new();
    private readonly SKPaint _text = new() { IsAntialias = true, TextAlign = SKTextAlign.Center, Typeface = BoldArial };
    private readonly SKPaint _dot = new() { IsAntialias = true, BlendMode = SKBlendMode.Plus };
    private readonly SKPaint _ring = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    private readonly Dictionary<int, SKMaskFilter> _blurs = new();
    private MotionLevel _level = MotionLevel.Full;
    private bool _photosafe;

    /// <summary>A ghost was woken by the cursor (UI thread, outside the lock). Carries its word.</summary>
    public event Action<string>? Woke;

    public AfterglowLayer(CompositorEngine engine) : base(engine)
    {
        _rnd = _rng.NextDouble;
    }

    /// <summary>Just above the card (40), below bubbles (45). WPF-only slot.</summary>
    public override int ZIndex => CompositorLayers.Subliminal + 1;

    public override bool WorldSpacePx => true;

    /// <summary>A card went up on these screens; leave a ghost on each once it starts to fade out.</summary>
    public void Spawn(IReadOnlyList<SubliminalLayer.Placement> placements, string text, double opacity, double delayS)
    {
        if (string.IsNullOrWhiteSpace(text) || placements.Count == 0) return;
        var runs = GlyphFallback.Split(text, BoldArial, SKFontStyle.Bold);
        if (runs.Length == 0) return;
        var level = MotionFx.Level;

        lock (_sync)
        {
            for (int i = 0; i < placements.Count; i++)
            {
                var p = placements[i];
                var d = new Draw { Runs = runs, Widths = new float[runs.Length], Scale = p.Scale };
                _text.TextSize = FontDip * p.Scale;
                d.Total = GlyphFallback.Measure(runs, _text, d.Widths, out var ascent, out var descent);
                d.Height = descent - ascent;
                d.BaselineOffset = -(ascent + descent) / 2f;
                var key = (p.BoundsPx.Left * 397) ^ p.BoundsPx.Top;
                var b = p.BoundsPx;
                var g = _field.Spawn(text, key, b.MidX, b.MidY, FontDip * p.Scale, opacity, delayS,
                    b.Left, b.Top, b.Right, b.Bottom, d.Total / 2f + 24f * p.Scale, level, _rnd);
                g.Payload = d;
            }
            // Under the lock: an off-thread Update that just found the field empty must not
            // switch the layer off after this ghost went in.
            SetActive(true);
        }
    }

    /// <summary>Panic, Stop, switch off. Safe from any thread.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _field.Clear();
            SetActive(false);
        }
    }

    // #853: a ghost still waiting behind its card draws nothing, so it must not force the shared
    // surface to re-raster. Anything born, or any spark or ring, animates every frame.
    private volatile bool _moving;
    public override bool Dirty => IsActive && _moving;

    /// <summary>Padded world-px rects of every ghost still visible, for the awareness OCR to skip.</summary>
    public System.Drawing.Rectangle[] GetActiveTextRectsPx()
    {
        lock (_sync)
        {
            if (_field.Ghosts.Count == 0) return Array.Empty<System.Drawing.Rectangle>();
            var list = new List<System.Drawing.Rectangle>(_field.Ghosts.Count);
            foreach (var g in _field.Ghosts)
            {
                if (g.Payload is not Draw d) continue;
                var a = AfterglowField.Alpha(_field.Now - g.Born, _field.Now - g.Wake, _level, _photosafe) * g.Opacity;
                if (a <= 0.01) continue;
                var halfW = d.Total / 2f + (4f + OcrPadDip) * d.Scale;
                var halfH = d.Height / 2f + (4f + OcrPadDip) * d.Scale;
                list.Add(new System.Drawing.Rectangle(
                    (int)Math.Floor(g.X - halfW), (int)Math.Floor(g.Y - halfH),
                    (int)Math.Ceiling(halfW * 2), (int)Math.Ceiling(halfH * 2)));
            }
            return list.ToArray();
        }
    }

    public override void Update(TimeSpan delta)
    {
        _level = MotionFx.Level;
        _photosafe = App.Settings?.Current?.LockdownPhotosafe == true;
        bool hasCursor = GetCursorPos(out var cur);

        _wokenText.Clear();
        lock (_sync)
        {
            _woken.Clear();
            _field.Step(Math.Min(delta.TotalSeconds, 0.1), hasCursor, cur.X, cur.Y, _level, _rnd, _woken);
            foreach (var g in _woken) _wokenText.Add(g.Text);
            if (_field.IsEmpty) SetActive(false);
            bool moving = _field.ParticleCount > 0 || _field.Rings.Count > 0;
            foreach (var g in _field.Ghosts) if (g.Born <= _field.Now) { moving = true; break; }
            _moving = moving;
        }

        foreach (var t in _wokenText)
        {
            try { Woke?.Invoke(t); } catch { }
        }
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        lock (_sync)
        {
            double now = _field.Now;

            foreach (var g in _field.Ghosts)
            {
                if (g.Payload is not Draw d) continue;
                if (!boundsPx.Contains((int)g.X, (int)g.Y)) continue;
                var a = AfterglowField.Alpha(now - g.Born, now - g.Wake, _level, _photosafe) * g.Opacity;
                if (a <= 0.004) continue;
                var alpha = (byte)Math.Clamp(a * 255, 0, 255);
                _text.TextSize = FontDip * d.Scale;
                float baseline = (float)g.Y + d.BaselineOffset;

                // Mockup shadowBlur is 14 x alpha at a 34 px font (a sigma of about 7 x unit x a):
                // the halo swells with the wake flare and shrinks as the ghost fades.
                _text.MaskFilter = Blur((float)(7 * AfterglowField.Unit(g.FontPx) * Math.Max(a, 0.2)));
                _text.Color = GhostGlow.WithAlpha(alpha);
                GlyphFallback.DrawCentered(canvas, d.Runs, (float)g.X, baseline, _text, d.Widths, d.Total);
                _text.MaskFilter = null;
                _text.Color = GhostFill.WithAlpha(alpha);
                GlyphFallback.DrawCentered(canvas, d.Runs, (float)g.X, baseline, _text, d.Widths, d.Total);
            }

            foreach (var r in _field.Rings)
            {
                if (!boundsPx.Contains((int)r.X, (int)r.Y)) continue;
                double u = Math.Clamp((now - r.T0) / AfterglowField.RingS, 0, 1);
                _ring.Color = RingCol.WithAlpha((byte)Math.Clamp((1 - u) * 255, 0, 255));
                _ring.StrokeWidth = (float)(2 * AfterglowField.Unit(r.FontPx));
                canvas.DrawCircle((float)r.X, (float)r.Y, (float)AfterglowField.RingRadius(r, now, _level), _ring);
            }

            for (int i = 0; i < _field.ParticleCount; i++)
            {
                ref readonly var p = ref _field.Particles[i];
                var a = AfterglowField.ParticleAlpha(p);
                if (a <= 0) continue;
                _dot.Color = Spark.WithAlpha((byte)(a * 255));
                canvas.DrawCircle((float)p.X, (float)p.Y, (float)((1 + 2.2 * a) * p.Unit), _dot);
            }
        }
    }

    private SKMaskFilter Blur(float sigma)
    {
        // Half-px steps: the halo now tracks alpha, so keep the cache small (bounded by the max sigma).
        int key = (int)Math.Round(sigma * 2);
        if (!_blurs.TryGetValue(key, out var f))
        {
            f = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.5f, key / 2f));
            _blurs[key] = f;
        }
        return f;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);
}
