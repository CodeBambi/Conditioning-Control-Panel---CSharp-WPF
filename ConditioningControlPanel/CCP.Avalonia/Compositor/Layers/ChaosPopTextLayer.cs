using System;
using System.Collections.Generic;
using Avalonia.Media;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Chaos floating combat text: a small, faint, color-coded word that pops at a bubble's
/// location the instant its effect fires — quick rise + fade, gone in ~half a second.
/// Second chaos overlay migrated onto the compositor (WS2/WP3, Phase F queue #2).
///
/// Behavior contract (WPF Chaos/ChaosPopText.cs):
/// - timing: 60ms fade-in 0 → 0.58, 230ms hold, 200ms fade-out (490ms total life);
/// - upward drift: label translate +6 → -22 DIP, linear, over the WHOLE 490ms life
///   (single-leg WPF DoubleAnimation — no AutoReverse, so no per-leg doubling here);
/// - look: Segoe UI Bold 22 DIP, UPPERCASE, fill = the bubble tint lifted 28% toward
///   white, stroke #0B0812 drawn UNDER the fill at pen width 2.0*2 with round joins
///   (the OutlinedText contract), whole word composited at the group opacity (WPF
///   animates WINDOW opacity, so stroke+fill fade as one — SaveLayer, not per-paint alpha);
/// - concurrency: WPF pools at most 14 floater windows; past the cap the word is DROPPED
///   ("losing a floater beats freezing the app") — same cap, same drop policy;
/// - gate: WPF ChaosPopText.Show is master-gated on ChaosAnnouncerEnabled — the gate
///   lives in the owning AvaloniaChaosService seam (services own policy, UCE rule 7).
///
/// Coordinates: anchors are PHYSICAL virtual-desktop px (the layer coordinate contract).
/// WPF took DIPs because its callers were DIP windows; the legacy Avalonia port kept the
/// DIP anchor but assigned it to PixelPoint unconverted (mixed-DPI misplacement bug) —
/// defining the seam in physical px removes that class of bug. The 22-DIP glyphs convert
/// to physical px per monitor via the screen-aware Render overload's Scaling (template
/// pattern; a word straddling a mixed-DPI seam draws each half at that monitor's scale).
///
/// Zero per-frame allocations: SKFont/paints are built once; each floater's SKTextBlob is
/// built once at spawn (content change) and disposed on expiry inside Update.
/// Capture affinity: capture-VISIBLE (main surface; no WPF chaos window touches
/// SetWindowDisplayAffinity — grep-verified 2026-07-04).
/// </summary>
public sealed class ChaosPopTextLayer : BaseLayer
{
    private const double InMs = 60;      // WPF IN_MS
    private const double HoldMs = 230;   // WPF HOLD_MS
    private const double OutMs = 200;    // WPF OUT_MS
    private const double TotalMs = InMs + HoldMs + OutMs; // 490ms life
    private const float FontSizeDip = 22f;   // WPF FONT_SIZE
    private const double PeakOpacity = 0.58; // WPF PEAK_OPAC
    private const double RiseStartDip = 6;   // WPF rise from
    private const double RiseEndDip = -22;   // WPF rise to (-RISE_DIP)
    private const float StrokePenWidth = 2.0f * 2f; // OutlinedText: pen = StrokeThickness*2
    private const int MaxFloaters = 14;  // WPF POOL_MAX (drop past the cap)

    private static readonly SKColor StrokeColor = new(0x0B, 0x08, 0x12);

    private readonly object _sync = new();
    private readonly List<Floater> _floaters = new();

    // Built once (UCE rule: no per-frame allocations). Never disposed — the layer lives
    // app-long (ChaosCursorGlowLayer precedent).
    private readonly SKFont _font;
    private readonly SKPaint _fillPaint;
    private readonly SKPaint _strokePaint;
    private readonly SKPaint _groupPaint; // SaveLayer alpha = the WPF window-opacity fade
    private readonly float _centerBaselineOffset; // vertical centering at the anchor
    private readonly float _lineTop;    // ascent relative to the centered baseline
    private readonly float _lineBottom; // descent relative to the centered baseline

    public ChaosPopTextLayer()
    {
        _font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), FontSizeDip)
        {
            Subpixel = true,
        };
        var metrics = _font.Metrics;
        _centerBaselineOffset = -(metrics.Ascent + metrics.Descent) / 2f;
        _lineTop = metrics.Ascent + _centerBaselineOffset;
        _lineBottom = metrics.Descent + _centerBaselineOffset;
        _fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _strokePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = StrokePenWidth,
            StrokeJoin = SKStrokeJoin.Round,
            Color = StrokeColor,
        };
        _groupPaint = new SKPaint();
    }

    public override int ZIndex => CompositorLayers.ChaosPopText;

    public override bool IsActive
    {
        get { lock (_sync) { return _floaters.Count > 0; } }
    }

    // ConsumeDirty stays the base always-dirty: every live floater rises/fades each frame,
    // and with no floaters IsActive is false so the engine never ticks or renders this layer.

    /// <summary>Pop one word at the anchor (PHYSICAL virtual-desktop px, the layer's native
    /// space). Text is uppercased and the tint lifted 28% toward white (WPF Palette). Past
    /// the 14-floater cap the word is dropped, exactly like the WPF pool.</summary>
    public void Spawn(double pxX, double pxY, string text, Color tint)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var upper = text.ToUpperInvariant();
        var blob = SKTextBlob.Create(upper, _font);
        if (blob == null) return;
        var width = _font.MeasureText(upper);
        static byte Lift(byte c) => (byte)Math.Clamp(c + (255 - c) * 0.28, 0, 255);
        var fill = new SKColor(Lift(tint.R), Lift(tint.G), Lift(tint.B));
        lock (_sync)
        {
            if (_floaters.Count >= MaxFloaters) { blob.Dispose(); return; }
            _floaters.Add(new Floater(pxX, pxY, blob, width, fill));
        }
    }

    /// <summary>Drop every live floater (run teardown / harness cleanup — WPF ShutdownPool).</summary>
    public void Clear()
    {
        lock (_sync)
        {
            foreach (var f in _floaters) f.Blob.Dispose();
            _floaters.Clear();
        }
    }

    public override void Update(TimeSpan deltaTime)
    {
        lock (_sync)
        {
            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                _floaters[i].ClockMs += deltaTime.TotalMilliseconds;
                if (_floaters[i].ClockMs >= TotalMs)
                {
                    _floaters[i].Blob.Dispose();
                    _floaters.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>Screen-aware render: 22-DIP glyphs convert to physical px with the
    /// composited monitor's scaling (template pattern).</summary>
    public void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, ScreenInfo? screen, TimeSpan deltaTime)
    {
        var scaling = screen != null && screen.Scaling > 0 ? screen.Scaling : 1.0;
        lock (_sync)
        {
            foreach (var f in _floaters)
            {
                var clock = f.ClockMs;
                // WPF: window opacity 0→0.58 over IN, hold, →0 over OUT (all linear).
                var opacity = clock < InMs
                    ? PeakOpacity * (clock / InMs)
                    : clock < InMs + HoldMs
                        ? PeakOpacity
                        : PeakOpacity * Math.Max(0, 1 - (clock - InMs - HoldMs) / OutMs);
                if (opacity <= 0) continue;
                // WPF: translate +6 → -22 DIP linearly across the whole 490ms life.
                var riseDip = RiseStartDip + (RiseEndDip - RiseStartDip) * Math.Min(1, clock / TotalMs);

                var save = canvas.Save();
                canvas.Translate((float)f.X, (float)f.Y);
                canvas.Scale((float)scaling);

                var halfW = f.Width / 2f;
                var baselineY = (float)riseDip + _centerBaselineOffset;
                // Group-opacity layer over a tight local rect (stroke pad included): the WPF
                // fade animates WINDOW opacity, compositing stroke+fill as one.
                var pad = StrokePenWidth + 2f;
                var local = new SKRect(
                    -halfW - pad, (float)riseDip + _lineTop - pad,
                    halfW + pad, (float)riseDip + _lineBottom + pad);
                _groupPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(opacity * 255, 0, 255));
                canvas.SaveLayer(local, _groupPaint);
                _fillPaint.Color = f.Fill;
                canvas.DrawText(f.Blob, -halfW, baselineY, _strokePaint); // stroke UNDER fill
                canvas.DrawText(f.Blob, -halfW, baselineY, _fillPaint);
                canvas.RestoreToCount(save);
            }
        }
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
        => Render(canvas, bounds, null, deltaTime);

    private sealed class Floater
    {
        public double X { get; }
        public double Y { get; }
        public SKTextBlob Blob { get; }
        public float Width { get; }
        public SKColor Fill { get; }
        public double ClockMs { get; set; }

        public Floater(double x, double y, SKTextBlob blob, float width, SKColor fill)
        {
            X = x;
            Y = y;
            Blob = blob;
            Width = width;
            Fill = fill;
        }
    }
}
