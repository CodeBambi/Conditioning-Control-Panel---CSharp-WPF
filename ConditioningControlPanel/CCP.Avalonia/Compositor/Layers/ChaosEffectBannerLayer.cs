using System;
using System.Collections.Generic;
using Avalonia.Media;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Top-of-screen strip that names every temporary Chaos bonus while it lasts (VIBEPOPPING,
/// FREEZE, TIME SLOW, PORN DVD…). Third chaos overlay migrated onto the compositor
/// (WS2/WP3, Phase F queue #3).
///
/// Behavior contract (WPF Chaos/ChaosEffectBannerOverlay.cs):
/// - one shared strip hosts all concurrent banners side by side at the top of the PRIMARY
///   work area (window at wa.Top + 6 DIP, entries horizontally centered, top-aligned,
///   insertion order), keyed by effect id so an effect can't stack duplicate labels
///   (duplicate Show(id) while the entry is live = no-op, "let it ride");
/// - entry visual: neon word-art PNG (assets/Chaos/announce/{artKey ?? id}.png) 56 DIP
///   high if it exists, else outlined text — Segoe UI Bold 34 DIP UPPERCASE, accent fill,
///   stroke #0B0812 pen 2.6*2 round-join under the fill; 18 DIP side margins either way;
/// - fade-in 200ms to 1.0 (linear); heartbeat throb scale 1.0↔1.03, SineEase in-out,
///   850ms PER LEG with AutoReverse = 1700ms full cycle, forever, centered on the entry
///   (the legacy Avalonia window got this cycle RIGHT — 1700 — no 2x bug here);
/// - End(id): 380ms fade-out from CURRENT opacity, then the entry is gone. The id is freed
///   the moment End is called (WPF removes it from the dict before the fade), so a
///   re-Show(id) during the fade adds a fresh entry alongside the fading one — preserved;
/// - no settings gate (WPF Show has none); accent color policy (ChaosBoonColors) is
///   applied by the owning service, not here.
///
/// Legacy-window parity bugs fixed by this layer (both absent in WPF):
/// - the Avalonia window shared ONE OpacityFade field across all entries, so a second
///   Show/End cancelled the previous entry's in-flight fade — here each entry owns its
///   fade envelope;
/// - AddEntry indexed <c>_pulses[id]?.Dispose()</c> before insertion (KeyNotFoundException
///   swallowed per add) — no per-entry timer objects exist anymore at all.
///
/// The whole strip anchors to the primary work area, whose physical rect + scaling the
/// service passes with each Show (layers get final geometry inputs; no screen lookups
/// here). Opacity fades composite stroke+fill as one via SaveLayer (WPF animates ELEMENT
/// opacity), skipped at alpha 255 — the steady state — so the common frame is direct draws.
/// Zero per-frame allocations: font/paints built once, per-entry SKTextBlob built at Show
/// and disposed on removal, art images cached app-long in <see cref="ChaosLayerArtCache"/>.
/// Capture affinity: capture-VISIBLE (main surface; grep-verified chaos finding).
/// </summary>
public sealed class ChaosEffectBannerLayer : BaseLayer
{
    private const float FontSizeDip = 34f;         // WPF FONT_SIZE
    private const double ArtHeightDip = 56;        // WPF ART_HEIGHT_DIP
    private const double FadeInMs = 200;           // WPF FADE_IN_MS
    private const double FadeOutMs = 380;          // WPF FADE_OUT_MS
    private const double ThrobFullCycleMs = 1700;  // WPF: 850ms per leg, AutoReverse
    private const double ThrobMax = 1.03;          // WPF pulse 1.0 -> 1.03
    private const float StrokePenWidth = 2.6f * 2f; // OutlinedText: pen = StrokeThickness*2
    private const double SideMarginDip = 18;       // WPF Margin(18,0,18,0)
    private const double TopOffsetDip = 6;         // WPF window at wa.Top + 6
    // WPF OutlinedText internal pad (StrokeThickness + 6) — the label's glyph box inset.
    private const float TextPadDip = 2.6f + 6f;

    private static readonly SKColor StrokeColor = new(0x0B, 0x08, 0x12);
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly object _sync = new();
    private readonly List<Entry> _entries = new();          // render/StackPanel order
    private readonly HashSet<string> _liveIds = new(StringComparer.Ordinal); // duplicate-id gate

    // Primary work area (PHYSICAL px) + its scaling, refreshed by the service on Show.
    private ConditioningControlPanel.Core.Platform.PixelRect _workArea = ConditioningControlPanel.Core.Platform.PixelRect.Empty;
    private double _scaling = 1.0;

    private readonly SKFont _font;
    private readonly SKPaint _fillPaint;
    private readonly SKPaint _strokePaint;
    private readonly SKPaint _imagePaint;
    private readonly SKPaint _groupPaint;
    private readonly float _baselineFromTop; // glyph baseline below the entry top (DIP)

    public ChaosEffectBannerLayer()
    {
        _font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), FontSizeDip)
        {
            Subpixel = true,
        };
        // WPF: label top-aligned with the OutlinedText pad above the line box; the glyph
        // baseline sits ~pad + ascent below the entry top (line-box vs font-metrics
        // baseline differs sub-DIP — accepted approximation).
        _baselineFromTop = TextPadDip - _font.Metrics.Ascent;
        _fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _strokePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = StrokePenWidth,
            StrokeJoin = SKStrokeJoin.Round,
            Color = StrokeColor,
        };
        _imagePaint = new SKPaint { IsAntialias = true };
        _groupPaint = new SKPaint();
    }

    public override int ZIndex => CompositorLayers.ChaosEffectBanner;

    public override bool IsActive
    {
        get { lock (_sync) { return _entries.Count > 0; } }
    }

    // ConsumeDirty stays the base always-dirty: every live entry throbs each frame, and
    // with no entries IsActive is false so the engine never ticks or renders this layer.

    /// <summary>Show (or keep) the banner for an effect. <paramref name="accent"/> is the
    /// FINAL fill color (the service applies ChaosBoonColors policy). <paramref name="artPath"/>
    /// is the resolved announce-art path or null for the text look. <paramref name="workAreaPx"/>
    /// + <paramref name="scaling"/> anchor the strip to the primary work area.</summary>
    public void Show(string id, string text, Color accent, string? artPath,
        ConditioningControlPanel.Core.Platform.PixelRect workAreaPx, double scaling)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var art = ChaosLayerArtCache.Get(artPath);
        SKTextBlob? blob = null;
        float textWidth = 0;
        if (art == null)
        {
            var upper = (text ?? "").ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(upper)) return;
            blob = SKTextBlob.Create(upper, _font);
            if (blob == null) return;
            textWidth = _font.MeasureText(upper);
        }

        lock (_sync)
        {
            _workArea = workAreaPx;
            _scaling = scaling > 0 ? scaling : 1.0;
            if (!_liveIds.Add(id)) { blob?.Dispose(); return; } // already on screen — let it ride

            double widthDip = art != null
                ? SideMarginDip * 2 + ArtHeightDip * (art.Height > 0 ? (double)art.Width / art.Height : 1.0)
                : SideMarginDip * 2 + textWidth + TextPadDip * 2;
            _entries.Add(new Entry(id, blob, art, textWidth,
                new SKColor(accent.R, accent.G, accent.B), widthDip));
        }
    }

    /// <summary>Fade out + remove the banner for an effect (no-op if it isn't showing).
    /// Frees the id immediately, exactly like WPF FadeEntry.</summary>
    public void End(string id)
    {
        lock (_sync)
        {
            if (!_liveIds.Remove(id)) return;
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (!e.FadingOut && e.Id == id)
                {
                    e.FadingOut = true;
                    e.FadeOutStartOpacity = Math.Min(1.0, e.ClockMs / FadeInMs);
                    e.FadeOutClockMs = 0;
                    break;
                }
            }
        }
    }

    /// <summary>Instant teardown (run end / harness cleanup — WPF CloseActive).</summary>
    public void Clear()
    {
        lock (_sync)
        {
            foreach (var e in _entries) e.Blob?.Dispose();
            _entries.Clear();
            _liveIds.Clear();
        }
    }

    public override void Update(TimeSpan deltaTime)
    {
        lock (_sync)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                e.ClockMs += deltaTime.TotalMilliseconds;
                if (e.FadingOut)
                {
                    e.FadeOutClockMs += deltaTime.TotalMilliseconds;
                    if (e.FadeOutClockMs >= FadeOutMs)
                    {
                        e.Blob?.Dispose();
                        _entries.RemoveAt(i);
                    }
                }
            }
        }
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        lock (_sync)
        {
            if (_entries.Count == 0 || _workArea.IsEmpty) return;
            var s = (float)_scaling;

            // WPF StackPanel: entries side by side, centered within the primary work area.
            double totalWidthDip = 0;
            foreach (var e in _entries) totalWidthDip += e.WidthDip;
            var topPx = (float)(_workArea.Y + TopOffsetDip * s);
            var xPx = (float)(_workArea.X + _workArea.Width / 2.0 - totalWidthDip * s / 2.0);

            foreach (var e in _entries)
            {
                // Fade-in 0→1 over 200ms (linear); fade-out 380ms from the captured opacity.
                var opacity = e.FadingOut
                    ? e.FadeOutStartOpacity * Math.Max(0, 1 - e.FadeOutClockMs / FadeOutMs)
                    : Math.Min(1.0, e.ClockMs / FadeInMs);
                // Heartbeat: triangle over the 1700ms full cycle, sine-eased (per-leg 850ms
                // AutoReverse), phase 0 at the entry's birth — keeps beating during fade-out.
                var phase = (e.ClockMs % ThrobFullCycleMs) / (ThrobFullCycleMs / 2.0); // 0..2
                var tri = phase <= 1 ? phase : 2 - phase;
                var eased = (1 - Math.Cos(tri * Math.PI)) / 2.0;
                var throb = 1.0 + (ThrobMax - 1.0) * eased;

                var entryWidthPx = (float)(e.WidthDip * s);
                if (opacity > 0)
                {
                    var alpha = (byte)Math.Clamp(opacity * 255, 0, 255);
                    var save = canvas.Save();
                    // Throb about the entry's visual center (RenderTransformOrigin 0.5,0.5).
                    var entryHeightDip = e.Art != null ? ArtHeightDip : TextPadDip * 2 - _font.Metrics.Ascent + _font.Metrics.Descent;
                    var cx = xPx + entryWidthPx / 2f;
                    var cy = topPx + (float)(entryHeightDip * s / 2.0);
                    canvas.Translate(cx, cy);
                    canvas.Scale((float)(throb * s));
                    // Local DIP space, origin at the entry center.
                    var halfWDip = (float)(e.WidthDip / 2.0);
                    var topDip = (float)(-entryHeightDip / 2.0);

                    if (e.Art != null)
                    {
                        var artWDip = e.WidthDip - SideMarginDip * 2;
                        var dest = new SKRect(
                            (float)(-artWDip / 2.0), topDip,
                            (float)(artWDip / 2.0), topDip + (float)ArtHeightDip);
                        _imagePaint.Color = SKColors.White.WithAlpha(alpha);
                        canvas.DrawImage(e.Art, dest, Sampling, _imagePaint);
                    }
                    else
                    {
                        var baselineY = topDip + _baselineFromTop;
                        var textX = -e.TextWidth / 2f;
                        if (alpha < 255)
                        {
                            // WPF fades ELEMENT opacity: stroke+fill composite as one group.
                            var pad = StrokePenWidth + 2f;
                            var local = new SKRect(-halfWDip, topDip - pad, halfWDip, baselineY + _font.Metrics.Descent + pad);
                            _groupPaint.Color = SKColors.White.WithAlpha(alpha);
                            canvas.SaveLayer(local, _groupPaint);
                        }
                        _fillPaint.Color = e.Fill;
                        canvas.DrawText(e.Blob, textX, baselineY, _strokePaint); // stroke UNDER fill
                        canvas.DrawText(e.Blob, textX, baselineY, _fillPaint);
                    }
                    canvas.RestoreToCount(save);
                }
                xPx += entryWidthPx;
            }
        }
    }

    private sealed class Entry
    {
        public string Id { get; }
        public SKTextBlob? Blob { get; }
        public SKImage? Art { get; }
        public float TextWidth { get; }
        public SKColor Fill { get; }
        public double WidthDip { get; }
        public double ClockMs { get; set; }
        public bool FadingOut { get; set; }
        public double FadeOutClockMs { get; set; }
        public double FadeOutStartOpacity { get; set; }

        public Entry(string id, SKTextBlob? blob, SKImage? art, float textWidth, SKColor fill, double widthDip)
        {
            Id = id;
            Blob = blob;
            Art = art;
            TextWidth = textWidth;
            Fill = fill;
            WidthDip = widthDip;
        }
    }
}
