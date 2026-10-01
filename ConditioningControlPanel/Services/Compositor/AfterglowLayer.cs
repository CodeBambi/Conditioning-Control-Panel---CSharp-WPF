using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Super Afterglow, the drawing half: words popped near the mouse, their trail of echoes and their
/// sparks. All maths lives in <see cref="AfterglowField"/>; <see cref="AfterglowDriver"/> feeds it the
/// cursor and decides when to spawn. This layer only paints what the field holds.
///
/// Main surface like <see cref="SubliminalLayer"/>: subliminals record (WDA_NONE) by design, so the
/// pops do too, and <see cref="GetActiveTextRectsPx"/> hands the awareness OCR their rects.
/// Click-through host: it never takes input.
/// </summary>
public sealed class AfterglowLayer : BaseLayer
{
    private const float OcrPadDip = 40f;   // SubliminalLayer parity
    private const float GlowSigmaDip = 6f;
    private static readonly SKColor WordFill = new(255, 255, 255);
    // Pink, mint, lilac (mockup palette).
    private static readonly SKColor[] Glow = { new(255, 95, 176), new(95, 255, 208), new(185, 156, 255) };
    private static readonly SKColor[] SparkCol = { new(255, 155, 224), new(150, 255, 225), new(210, 190, 255) };

    private static readonly SKTypeface BoldArial =
        SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;

    private sealed class Draw
    {
        public GlyphFallback.TextRun[] Runs = Array.Empty<GlyphFallback.TextRun>();
        public float[] Widths = Array.Empty<float>();
        public float Total, Height, BaselineOffset, FontPx;
        public SKMaskFilter? Blur;
    }

    private readonly AfterglowField _field = new();
    private readonly object _sync = new();
    private readonly Random _rng = new();
    private readonly Func<double> _rnd;
    private readonly SKPaint _text = new() { IsAntialias = true, TextAlign = SKTextAlign.Center, Typeface = BoldArial };
    private readonly SKPaint _dot = new() { IsAntialias = true, BlendMode = SKBlendMode.Plus };
    private readonly Dictionary<int, SKMaskFilter> _blurs = new();
    private MotionLevel _level = MotionLevel.Full;
    private bool _photosafe;

    public AfterglowLayer(CompositorEngine engine) : base(engine)
    {
        _rnd = _rng.NextDouble;
    }

    /// <summary>Just above the card (40), below bubbles (45). WPF-only slot.</summary>
    public override int ZIndex => CompositorLayers.Subliminal + 1;

    public override bool WorldSpacePx => true;

    /// <summary>Anything alive animates every frame; an empty layer is inactive.</summary>
    public override bool Dirty => IsActive;

    /// <summary>One cursor sample (about 30 Hz). Safe from any thread.</summary>
    public void SampleCursor(double dt, double x, double y)
    {
        lock (_sync) _field.SampleCursor(dt, x, y);
    }

    public void ResetCursor()
    {
        lock (_sync) _field.ResetCursor();
    }

    /// <summary>Pop <paramref name="text"/> near the cursor (<paramref name="cx"/>, <paramref name="cy"/>),
    /// inside <paramref name="screenPx"/>, at <paramref name="scale"/> physical px per DIP.</summary>
    public void Spawn(string text, double cx, double cy, double scale, SKRectI screenPx)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var runs = GlyphFallback.Split(text, BoldArial, SKFontStyle.Bold);
        if (runs.Length == 0) return;
        var level = MotionFx.Level;
        bool photosafe = App.Settings?.Current?.LockdownPhotosafe == true;
        scale = scale <= 0 ? 1 : scale;

        lock (_sync)
        {
            var d = new Draw { Runs = runs, Widths = new float[runs.Length], FontPx = (float)(AfterglowField.FontDip * scale) };
            _text.TextSize = d.FontPx;
            d.Total = GlyphFallback.Measure(runs, _text, d.Widths, out var ascent, out var descent);
            d.Height = descent - ascent;
            d.BaselineOffset = -(ascent + descent) / 2f;
            d.Blur = Blur((float)(GlowSigmaDip * scale));

            AfterglowField.SpawnPoint(cx, cy, scale, screenPx.Left, screenPx.Top, screenPx.Right, screenPx.Bottom,
                d.Total / 2 + 8 * scale, d.Height / 2 + 8 * scale, level, _rnd(), _rnd(), out var x, out var y);
            var p = _field.Spawn(text, x, y, scale, level, photosafe, _rnd);
            p.Payload = d;
            // Under the lock: an off-thread Update that just found the field empty must not switch
            // the layer off after this pop went in.
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

    /// <summary>Padded world-px rects of every pop still alive, for the awareness OCR to skip.</summary>
    public System.Drawing.Rectangle[] GetActiveTextRectsPx()
    {
        lock (_sync)
        {
            if (_field.Pops.Count == 0) return Array.Empty<System.Drawing.Rectangle>();
            var list = new List<System.Drawing.Rectangle>(_field.Pops.Count);
            foreach (var p in _field.Pops)
            {
                if (p.Payload is not Draw d) continue;
                // The word drifts during its split second: pad by the card pad plus a drift allowance.
                var pad = (float)((OcrPadDip + AfterglowField.OffsetMinDip) * p.Scale);
                var halfW = d.Total / 2f + pad;
                var halfH = d.Height / 2f + pad;
                list.Add(new System.Drawing.Rectangle(
                    (int)Math.Floor(p.X - halfW), (int)Math.Floor(p.Y - halfH),
                    (int)Math.Ceiling(halfW * 2), (int)Math.Ceiling(halfH * 2)));
            }
            return list.ToArray();
        }
    }

    public override void Update(TimeSpan delta)
    {
        _level = MotionFx.Level;
        _photosafe = App.Settings?.Current?.LockdownPhotosafe == true;
        lock (_sync)
        {
            _field.Step(Math.Min(delta.TotalSeconds, 0.1), _level, _photosafe, _rnd);
            if (_field.IsEmpty) SetActive(false);
        }
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        lock (_sync)
        {
            double now = _field.Now;

            // Sparks under the words, additive.
            for (int i = 0; i < _field.ParticleCount; i++)
            {
                ref readonly var q = ref _field.Particles[i];
                if (!boundsPx.Contains((int)q.X, (int)q.Y)) continue;
                var a = AfterglowField.ParticleAlpha(q);
                if (a <= 0) continue;
                _dot.Color = SparkCol[q.Hue % 3].WithAlpha((byte)(a * 255));
                canvas.DrawCircle((float)q.X, (float)q.Y, (float)((0.8 + 1.8 * a) * q.Size), _dot);
            }

            foreach (var p in _field.Pops)
            {
                if (p.Payload is not Draw d) continue;
                if (!boundsPx.Contains((int)p.X, (int)p.Y)) continue;
                var a = AfterglowField.Alpha(now - p.Born, _level, _photosafe);
                if (a <= 0.004) continue;
                _text.TextSize = d.FontPx;
                var glow = Glow[p.Hue % 3];

                // Trail: echoes oldest first, glow colour only. Echo 0 sits on the word itself.
                for (int k = p.TrailCount - 1; k >= 1; k--)
                {
                    p.TrailAt(k, out var tx, out var ty);
                    var ea = AfterglowField.TrailAlpha(k - 1) * a;
                    if (ea <= 0.004) continue;
                    _text.Color = glow.WithAlpha((byte)Math.Clamp(ea * 255, 0, 255));
                    GlyphFallback.DrawCentered(canvas, d.Runs, (float)tx, (float)ty + d.BaselineOffset, _text, d.Widths, d.Total);
                }

                var alpha = (byte)Math.Clamp(a * 255, 0, 255);
                float baseline = (float)p.Y + d.BaselineOffset;
                _text.MaskFilter = d.Blur;
                _text.Color = glow.WithAlpha(alpha);
                GlyphFallback.DrawCentered(canvas, d.Runs, (float)p.X, baseline, _text, d.Widths, d.Total);
                _text.MaskFilter = null;
                _text.Color = WordFill.WithAlpha(alpha);
                GlyphFallback.DrawCentered(canvas, d.Runs, (float)p.X, baseline, _text, d.Widths, d.Total);
            }
        }
    }

    private SKMaskFilter Blur(float sigma)
    {
        int key = (int)Math.Round(sigma * 2);
        if (!_blurs.TryGetValue(key, out var f))
        {
            f = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.5f, key / 2f));
            _blurs[key] = f;
        }
        return f;
    }
}
