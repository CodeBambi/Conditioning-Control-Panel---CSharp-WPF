using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Compositor twin of the subliminal flash cards. Mirrors the Avalonia port's SubliminalLayer
/// (queued items, per-item 50ms fade envelope, most-recent-wins render) plus the WPF
/// outlined-text construction the Avalonia head doesn't have yet: 8 border-color offset copies
/// under the main text, Arial Bold 120 DIP (BuildSubliminalContent parity).
///
/// Renders on the MAIN surface: subliminals stay visible in screen capture BY DESIGN (the
/// legacy windows deliberately set WDA_NONE); the awareness OCR skips the text via
/// <see cref="GetActiveTextRectsPx"/> instead. The focus-steal variant cannot come from the
/// click-through host and stays on the legacy per-screen windows.
///
/// SubliminalService owns ALL state and scheduling; each Flash hands this layer a fully
/// resolved card (colors, opacity, hold time, per-screen geometry).
/// </summary>
public sealed class SubliminalLayer : BaseLayer
{
    /// <summary>WPF AnimateSubliminal fade-in/out duration (50ms each side of the hold).</summary>
    private const double FadeMs = 50;
    /// <summary>Arial Bold 120 DIP (CreateTextBlock parity); multiplied by per-screen scale.</summary>
    private const float FontDip = 120f;
    /// <summary>Legacy OCR-rect padding (GetActiveTextScreenRects), in DIP.</summary>
    private const float OcrPadDip = 40f;

    /// <summary>WPF outline offsets in DIP (BuildSubliminalContent parity).</summary>
    private static readonly (float X, float Y)[] Offsets =
    {
        (-3, -3), (3, -3), (-3, 3), (3, 3),
        (0, -4), (0, 4), (-4, 0), (4, 0)
    };

    /// <summary>The WPF card's font (CreateTextBlock parity). Only ever the ACTUAL draw face for
    /// text Arial can render - see <see cref="Item.Runs"/> and bugs #615 / #717.</summary>
    private static readonly SKTypeface BoldArial =
        SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;

    /// <summary>One screen's card geometry in virtual-desktop device px.</summary>
    public readonly struct Placement
    {
        public Placement(SKRectI boundsPx, float scale)
        {
            BoundsPx = boundsPx;
            Scale = scale <= 0 ? 1f : scale;
        }

        /// <summary>The screen's rectangle in world (virtual-desktop) device px.</summary>
        public SKRectI BoundsPx { get; }
        /// <summary>Physical px per DIP on this screen - DIP-tuned text metrics multiply by it.</summary>
        public float Scale { get; }
    }

    private sealed class Item
    {
        public string Text = "";
        /// <summary>Bugs #615 / #717: the text split into runs, each with the typeface that can
        /// actually DRAW it. Arial for Latin (unchanged appearance), a system fallback family for
        /// anything Arial lacks - CJK, Cyrillic, emoji - and a SEPARATE run per script, because a
        /// phrase like "♤雌畜人妖♤" has no single face that covers all of it. Resolved once at
        /// queue time and reused for BOTH the measure and the draw; measuring in one font and
        /// drawing in another would mis-centre the card and hand the awareness OCR the wrong
        /// skip rect.</summary>
        public GlyphFallback.TextRun[] Runs = Array.Empty<GlyphFallback.TextRun>();
        public SKColor Bg, TextColor, Border;
        public bool BgTransparent;
        public double TargetOpacity;
        public TimeSpan Total, Remaining;
        public Placement[] Placements = Array.Empty<Placement>();
        // Measured text extents per placement (device px), for the OCR skip rects and centring.
        public float[] TextWidthPx = Array.Empty<float>();
        public float[] TextHeightPx = Array.Empty<float>();
        /// <summary>Per-placement, per-run advance widths (device px) so the render tick never
        /// measures. Jagged: [placement][run].</summary>
        public float[][] RunWidthsPx = Array.Empty<float[]>();
        /// <summary>Per-placement baseline offset from the card's vertical centre (device px),
        /// derived from the metrics of every face in the line, not just the first.</summary>
        public float[] BaselineOffsetPx = Array.Empty<float>();

        /// <summary>0..1 fade envelope: 50ms ramp up, hold, 50ms ramp down (WPF storyboard parity).</summary>
        public double Envelope()
        {
            var elapsedMs = (Total - Remaining).TotalMilliseconds;
            var remainingMs = Remaining.TotalMilliseconds;
            if (elapsedMs < FadeMs) return Math.Clamp(elapsedMs / FadeMs, 0.0, 1.0);
            if (remainingMs < FadeMs) return Math.Clamp(remainingMs / FadeMs, 0.0, 1.0);
            return 1.0;
        }
    }

    private readonly List<Item> _items = new();
    private readonly object _sync = new();
    // Reused paints, only touched under _sync (no per-frame allocations).
    private readonly SKPaint _bgPaint = new();
    private readonly SKPaint _textPaint = new()
    {
        IsAntialias = true,
        TextAlign = SKTextAlign.Center,
        Typeface = BoldArial
    };

    public SubliminalLayer(CompositorEngine engine) : base(engine) { }

    public override int ZIndex => CompositorLayers.Subliminal;

    public override bool WorldSpacePx => true;

    /// <summary>
    /// Queue a subliminal flash across the given screens. <paramref name="holdMs"/> is the hold
    /// time; the 50ms fades are added on top (WPF parity: fade-in + hold + fade-out storyboard).
    /// <paramref name="targetOpacity"/> scales the whole card (background AND text), 0..1 -
    /// exactly like the legacy whole-window Opacity animation. Safe from any thread.
    /// </summary>
    public void Flash(IReadOnlyList<Placement> placements, string text,
        SKColor bg, SKColor textColor, SKColor border,
        bool bgTransparent, double targetOpacity, int holdMs)
    {
        if (string.IsNullOrWhiteSpace(text) || placements.Count == 0) return;

        var runs = GlyphFallback.Split(text, BoldArial, SKFontStyle.Bold);
        if (runs.Length == 0) return;

        var item = new Item
        {
            Text = text,
            // #615/#717: split into draw faces BEFORE measuring (memoised per phrase inside).
            Runs = runs,
            Bg = bg,
            TextColor = textColor,
            Border = border,
            BgTransparent = bgTransparent,
            TargetOpacity = Math.Clamp(targetOpacity, 0.0, 1.0),
            Total = TimeSpan.FromMilliseconds(holdMs + 2 * FadeMs),
            Placements = new Placement[placements.Count],
            TextWidthPx = new float[placements.Count],
            TextHeightPx = new float[placements.Count],
            RunWidthsPx = new float[placements.Count][],
            BaselineOffsetPx = new float[placements.Count]
        };
        item.Remaining = item.Total;

        lock (_sync)
        {
            for (int i = 0; i < placements.Count; i++)
            {
                item.Placements[i] = placements[i];
                _textPaint.TextSize = FontDip * placements[i].Scale;
                // #615/#717: measure with the faces we will draw with, and take the vertical
                // extents across ALL of them (a CJK fallback face is not metric-compatible with
                // Arial, so centring on Arial's metrics alone would sit the card off-centre).
                var widths = new float[runs.Length];
                item.RunWidthsPx[i] = widths;
                item.TextWidthPx[i] = GlyphFallback.Measure(runs, _textPaint, widths,
                    out var ascent, out var descent);
                item.TextHeightPx[i] = descent - ascent;
                item.BaselineOffsetPx[i] = -(ascent + descent) / 2f;
            }
            _items.Add(item);
        }
        SetActive(true);
    }

    /// <summary>Drop every live card (service Stop / mid-run flag flip). Safe from any thread.</summary>
    public void Clear()
    {
        lock (_sync) { _items.Clear(); }
        SetActive(false);
    }

    /// <summary>
    /// Padded virtual-desktop px rects of the currently DRAWN text (the most recent card, the
    /// only one <see cref="Render"/> shows), for the awareness OCR to skip - same contract as
    /// the legacy GetActiveTextScreenRects window walk. Empty when nothing is visibly flashing.
    /// </summary>
    public System.Drawing.Rectangle[] GetActiveTextRectsPx()
    {
        lock (_sync)
        {
            if (_items.Count == 0) return Array.Empty<System.Drawing.Rectangle>();
            var item = _items[^1];
            if (item.TargetOpacity * item.Envelope() <= 0.01)
                return Array.Empty<System.Drawing.Rectangle>();

            var rects = new System.Drawing.Rectangle[item.Placements.Length];
            for (int i = 0; i < item.Placements.Length; i++)
            {
                var p = item.Placements[i];
                // Text bounds + the 4-DIP max outline offset, then the legacy 40-DIP OCR pad.
                var halfW = item.TextWidthPx[i] / 2f + (4f + OcrPadDip) * p.Scale;
                var halfH = item.TextHeightPx[i] / 2f + (4f + OcrPadDip) * p.Scale;
                var cx = p.BoundsPx.MidX;
                var cy = p.BoundsPx.MidY;
                rects[i] = new System.Drawing.Rectangle(
                    (int)Math.Floor(cx - halfW), (int)Math.Floor(cy - halfH),
                    (int)Math.Ceiling(halfW * 2), (int)Math.Ceiling(halfH * 2));
            }
            return rects;
        }
    }

    public override void Update(TimeSpan delta)
    {
        lock (_sync)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                _items[i].Remaining -= delta;
                if (_items[i].Remaining <= TimeSpan.Zero)
                    _items.RemoveAt(i);
            }
            if (_items.Count == 0)
                SetActive(false);
        }
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        lock (_sync)
        {
            if (_items.Count == 0) return;

            // Most recent card wins (legacy parity: a new show replaces the previous outright).
            var item = _items[^1];
            var alphaScale = item.TargetOpacity * item.Envelope();
            if (alphaScale <= 0) return;
            var alpha = (byte)Math.Clamp(alphaScale * 255, 0, 255);

            for (int i = 0; i < item.Placements.Length; i++)
            {
                var p = item.Placements[i];
                if (!p.BoundsPx.IntersectsWith(boundsPx)) continue;   // cull to this monitor

                if (!item.BgTransparent)
                {
                    // Legacy card bg is fully opaque, scaled only by the window opacity.
                    _bgPaint.Color = item.Bg.WithAlpha(alpha);
                    canvas.DrawRect(p.BoundsPx, _bgPaint);
                }

                // #615/#717: the same faces and advances the card was measured with - metrics AND
                // glyphs must agree, or the line mis-centres and the OCR skip rect is wrong.
                _textPaint.TextSize = FontDip * p.Scale;
                var cx = p.BoundsPx.MidX;
                var baseline = p.BoundsPx.MidY + item.BaselineOffsetPx[i];
                var widths = item.RunWidthsPx[i];
                var total = item.TextWidthPx[i];

                // #615 caveat: a COLOUR emoji glyph (Segoe UI Emoji) paints its own colours and
                // ignores the paint colour, so these 8 border copies come out as full-colour
                // duplicates at +-3-4 DIP rather than an outline. At 120 DIP that reads as a
                // slight thickening behind the glyph, so it is left as-is for parity; monochrome
                // text (everything else, including CJK) outlines exactly as before.
                _textPaint.Color = item.Border.WithAlpha(alpha);
                foreach (var (ox, oy) in Offsets)
                    GlyphFallback.DrawCentered(canvas, item.Runs, cx + ox * p.Scale,
                        baseline + oy * p.Scale, _textPaint, widths, total);

                _textPaint.Color = item.TextColor.WithAlpha(alpha);
                GlyphFallback.DrawCentered(canvas, item.Runs, cx, baseline, _textPaint, widths, total);
            }
        }
    }
}
