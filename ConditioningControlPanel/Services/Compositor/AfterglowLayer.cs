using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Super Afterglow, the drawing half: words popped near the mouse, their trail of echoes and their
/// sparks. All maths lives in <see cref="AfterglowField"/> and <see cref="AfterglowOptions"/>;
/// <see cref="AfterglowDriver"/> feeds it the cursor and decides when to spawn. This layer only paints
/// what the field holds, and holds the few burst words still waiting for their stagger.
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

    private static readonly SKTypeface BoldArial =
        SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;

    private sealed class Draw
    {
        public GlyphFallback.TextRun[] Runs = Array.Empty<GlyphFallback.TextRun>();
        public float[] Widths = Array.Empty<float>();
        public float Total, Height, BaselineOffset, FontPx;
        public SKMaskFilter? Blur;
    }

    /// <summary>A burst word waiting for its stagger.</summary>
    private struct Pending
    {
        public string Text;
        public Draw Draw;
        public double X, Y, Scale, At;
    }

    private readonly AfterglowField _field = new();
    private readonly object _sync = new();
    private readonly Random _rng = new();
    private readonly Func<double> _rnd;
    private readonly SKPaint _text = new() { IsAntialias = true, TextAlign = SKTextAlign.Center, Typeface = BoldArial };
    private readonly SKPaint _dot = new() { IsAntialias = true, BlendMode = SKBlendMode.Plus };
    private readonly Dictionary<int, SKMaskFilter> _blurs = new();
    private readonly List<Pending> _pending = new(AfterglowOptions.CountMax);
    private MotionLevel _level = MotionLevel.Full;
    private bool _photosafe;
    // The player's two glow colours (option box), read live; index = AfterglowPop.Hue.
    private readonly SKColor[] _glow = new SKColor[2];
    private readonly SKColor[] _spark = new SKColor[2];
    private string? _hexA, _hexB;

    public AfterglowLayer(CompositorEngine engine) : base(engine)
    {
        _rnd = _rng.NextDouble;
        RefreshColours();
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

    /// <summary>Re-read the two colours when the settings strings change: two reference compares a frame.</summary>
    private void RefreshColours()
    {
        var s = App.Settings?.Current;
        var a = s?.AfterglowColorA ?? AfterglowOptions.ColorADefault;
        var b = s?.AfterglowColorB ?? AfterglowOptions.ColorBDefault;
        if (ReferenceEquals(a, _hexA) && ReferenceEquals(b, _hexB)) return;
        _hexA = a; _hexB = b;
        SetColour(0, AfterglowOptions.ParseColor(a, AfterglowOptions.ColorADefault));
        SetColour(1, AfterglowOptions.ParseColor(b, AfterglowOptions.ColorBDefault));
    }

    private void SetColour(int i, (byte R, byte G, byte B) c)
    {
        _glow[i] = new SKColor(c.R, c.G, c.B);
        var sp = AfterglowOptions.SparkOf(c);
        _spark[i] = new SKColor(sp.R, sp.G, sp.B);
    }

    /// <summary>
    /// Pop a burst near the cursor (<paramref name="cx"/>, <paramref name="cy"/>), inside
    /// <paramref name="screenPx"/>, at <paramref name="scale"/> physical px per DIP. How many words
    /// (up to <paramref name="words"/>.Count) and their size come from the option box, read now, so a
    /// slider takes effect on the next pop. The burst stacks one line apart (no overlap) and staggers
    /// 50..90 ms per word; the first word pops at once. One word = the pop as it shipped.
    /// </summary>
    public void Spawn(IReadOnlyList<string> words, double cx, double cy, double scale, SKRectI screenPx)
    {
        if (words == null || words.Count == 0) return;
        var settings = App.Settings?.Current;
        int count = Math.Min(words.Count, AfterglowOptions.ClampCount(settings?.AfterglowCount ?? AfterglowOptions.CountDefault));
        double size = AfterglowOptions.SizeFactor(settings?.AfterglowSize ?? AfterglowOptions.SizeDefault);
        var level = MotionFx.Level;
        bool photosafe = settings?.LockdownPhotosafe == true;
        scale = (scale <= 0 ? 1 : scale) * size;

        lock (_sync)
        {
            var draws = new Draw?[count];
            float fontPx = (float)(AfterglowField.FontDip * scale), maxHalfW = 0, height = 0;
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrWhiteSpace(words[i])) continue;
                var runs = GlyphFallback.Split(words[i], BoldArial, SKFontStyle.Bold);
                if (runs.Length == 0) continue;
                var d = new Draw { Runs = runs, Widths = new float[runs.Length], FontPx = fontPx };
                _text.TextSize = d.FontPx;
                d.Total = GlyphFallback.Measure(runs, _text, d.Widths, out var ascent, out var descent);
                d.Height = descent - ascent;
                d.BaselineOffset = -(ascent + descent) / 2f;
                d.Blur = Blur((float)(GlowSigmaDip * scale));
                draws[i] = d;
                maxHalfW = Math.Max(maxHalfW, d.Total / 2);
                height = Math.Max(height, d.Height);
            }
            if (height <= 0) return;

            float pad = (float)(8 * scale);
            float halfStack = (float)(AfterglowOptions.BurstHalfLines(count) * fontPx);
            AfterglowField.SpawnPoint(cx, cy, scale, screenPx.Left, screenPx.Top, screenPx.Right, screenPx.Bottom,
                maxHalfW + pad, Math.Max(height / 2, halfStack) + pad, level, _rnd(), _rnd(), out var bx, out var by);

            _field.Capacity = AfterglowOptions.MaxAlive(count);
            var plan = AfterglowOptions.BurstPlan(count, _rnd);
            for (int k = 0; k < plan.Length; k++)
            {
                var d = draws[k];
                if (d == null) continue;
                var slot = plan[k];
                double lo = screenPx.Left + d.Total / 2 + pad, hi = screenPx.Right - d.Total / 2 - pad;
                double x = bx + slot.Nudge * fontPx;
                x = lo > hi ? (lo + hi) / 2 : Math.Clamp(x, lo, hi);
                double y = by + slot.Line * fontPx;
                if (slot.DelayS <= 0)
                    _field.Spawn(words[k], x, y, scale, level, photosafe, _rnd).Payload = d;
                else
                    _pending.Add(new Pending { Text = words[k], Draw = d, X = x, Y = y, Scale = scale, At = _field.Now + slot.DelayS });
            }
            // Under the lock: an off-thread Update that just found the field empty must not switch
            // the layer off after this pop went in.
            SetActive(true);
        }
    }

    /// <summary>Panic, Stop, switch off. Safe from any thread. Words still waiting for their stagger go too.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _field.Clear();
            _pending.Clear();
            SetActive(false);
        }
    }

    /// <summary>Padded world-px rects of every pop still alive (or about to pop), for the awareness OCR to skip.</summary>
    public System.Drawing.Rectangle[] GetActiveTextRectsPx()
    {
        lock (_sync)
        {
            if (_field.Pops.Count == 0 && _pending.Count == 0) return Array.Empty<System.Drawing.Rectangle>();
            var list = new List<System.Drawing.Rectangle>(_field.Pops.Count + _pending.Count);
            foreach (var p in _field.Pops)
                if (p.Payload is Draw d) list.Add(Rect(p.X, p.Y, p.Scale, d));
            foreach (var w in _pending)
                list.Add(Rect(w.X, w.Y, w.Scale, w.Draw));
            return list.ToArray();
        }
    }

    private static System.Drawing.Rectangle Rect(double x, double y, double scale, Draw d)
    {
        // The word drifts during its split second: pad by the card pad plus a drift allowance.
        var pad = (float)((OcrPadDip + AfterglowField.OffsetMinDip) * scale);
        var halfW = d.Total / 2f + pad;
        var halfH = d.Height / 2f + pad;
        return new System.Drawing.Rectangle(
            (int)Math.Floor(x - halfW), (int)Math.Floor(y - halfH),
            (int)Math.Ceiling(halfW * 2), (int)Math.Ceiling(halfH * 2));
    }

    public override void Update(TimeSpan delta)
    {
        _level = MotionFx.Level;
        _photosafe = App.Settings?.Current?.LockdownPhotosafe == true;
        RefreshColours();
        lock (_sync)
        {
            _field.Step(Math.Min(delta.TotalSeconds, 0.1), _level, _photosafe, _rnd);
            for (int i = 0; i < _pending.Count; i++)
            {
                var w = _pending[i];
                if (w.At > _field.Now) continue;
                _field.Spawn(w.Text, w.X, w.Y, w.Scale, _level, _photosafe, _rnd).Payload = w.Draw;
                _pending.RemoveAt(i--);
            }
            if (_field.IsEmpty && _pending.Count == 0) SetActive(false);
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
                _dot.Color = _spark[q.Hue & 1].WithAlpha((byte)(a * 255));
                canvas.DrawCircle((float)q.X, (float)q.Y, (float)((0.8 + 1.8 * a) * q.Size), _dot);
            }

            foreach (var p in _field.Pops)
            {
                if (p.Payload is not Draw d) continue;
                if (!boundsPx.Contains((int)p.X, (int)p.Y)) continue;
                var a = AfterglowField.Alpha(now - p.Born, _level, _photosafe);
                if (a <= 0.004) continue;
                _text.TextSize = d.FontPx;
                var glow = _glow[p.Hue & 1];

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
