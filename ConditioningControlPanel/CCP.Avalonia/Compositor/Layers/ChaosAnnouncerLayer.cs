using System;
using Avalonia.Media;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Chaos announcer: the fast bordered "subtitle" line in the upper third of the screen that
/// announces a Chaos pickup/beat (mantra drafted, temptation taken, streak milestone, the
/// Madam's narrator lines). Fourth chaos overlay migrated onto the compositor (WS2/WP3,
/// Phase F queue #4).
///
/// This layer renders ONE line — the current one. The announcement QUEUE (priority ordering,
/// the showing flag, gates, palette policy, art resolution) stays in the owning
/// AvaloniaChaosService, byte-equivalent to the WPF static queue (services own state, layers
/// render it — UCE rule 7). The service drives the line lifecycle through
/// <see cref="ShowLine"/> / <see cref="CutShort"/> / <see cref="HideNow"/> and advances its
/// queue from <see cref="LineCompleted"/>, which this layer fires on the engine tick (UI
/// thread) after the fade-out completes — the WPF fade.Completed → ShowNext() chain.
///
/// Behavior contract (WPF Chaos/ChaosAnnouncerOverlay.cs, per line):
/// - fade-in 110ms linear (window opacity 0 → 1) + scale pop 0.85 → 1.0 over 180ms
///   (IN + 70) with WPF BackEase amplitude 0.6 EaseOut — eased(t) = 1 − ((1−t)³ −
///   (1−t)·0.6·sin(π(1−t))), a gentler overshoot than the standard c1=1.70158 back-out the
///   legacy Avalonia window substituted (parity restored); the pop keeps running through
///   hold/fade-out (WPF never stops the scale animation);
/// - hold per line (default 650ms, teach lines 3000ms), then fade-out 240ms from the
///   CURRENT opacity; CutShort (narrator STORY interrupt) jumps straight to the fade-out,
///   also from the current opacity (WPF restarts the life timer at 1ms);
/// - content: art banner (assets/Chaos/announce/{artKey}.png, 120 DIP high) with the
///   dynamic subText in small outlined type beneath (26 DIP, stroke 2.2, +2 DIP margin),
///   or — no art — the line itself as outlined text, Segoe UI Bold 60 DIP UPPERCASE,
///   stroke #0B0812 pen 3.2*2 round-join under the fill;
/// - placement: centered horizontally over the PRIMARY work area (not the virtual-desktop
///   center — WPF explicitly anchors to the primary so a second monitor can't pull the
///   line off-screen), content top at wa.Top + 92 DIP (right under the effect-banner
///   strip), scale pop about the content center.
///
/// Fades composite stroke+fill+art as one group via SaveLayer (WPF animates WINDOW
/// opacity), skipped at alpha 255 during the hold. Zero per-frame allocations: font/paints
/// built once, per-line SKTextBlobs built at ShowLine and disposed on the next
/// ShowLine/expiry; art SKImages cached in <see cref="ChaosLayerArtCache"/>.
/// Capture affinity: capture-VISIBLE (main surface; grep-verified chaos finding).
/// </summary>
public sealed class ChaosAnnouncerLayer : BaseLayer
{
    private const double InMs = 110;          // WPF IN_MS
    private const double OutMs = 240;         // WPF OUT_MS
    private const double PopMs = InMs + 70;   // WPF scale pop duration (IN_MS + 70)
    private const double ScaleFrom = 0.85;    // WPF pop from
    private const double BackAmplitude = 0.6; // WPF BackEase Amplitude
    private const float FontSizeDip = 60f;    // WPF FONT_SIZE
    private const float SubFontSizeDip = 26f; // WPF SUB_FONT_SIZE
    private const double ArtHeightDip = 120;  // WPF ART_HEIGHT_DIP
    private const double SubGapDip = 2;       // WPF sub margin top
    private const double TopOffsetDip = 92;   // WPF TOP_OFFSET_DIP
    private const float StrokePenWidth = 3.2f * 2f;    // OutlinedText: pen = thickness*2
    private const float SubStrokePenWidth = 2.2f * 2f;
    private const float TextPadDip = 3.2f + 6f;        // OutlinedText pad (thickness + 6)
    private const float SubPadDip = 2.2f + 6f;

    private static readonly SKColor StrokeColor = new(0x0B, 0x08, 0x12);
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private enum Phase { Idle, In, Hold, Out }

    private readonly object _sync = new();
    private Phase _phase = Phase.Idle;
    private double _phaseClockMs;
    private double _scaleClockMs;
    private double _holdMs;
    private double _outStartOpacity;
    private bool _completionPending;

    // Current line content (rebuilt per ShowLine — content change, not per frame).
    private SKTextBlob? _blob;
    private float _blobWidth;
    private SKImage? _art;            // cached in ChaosLayerArtCache; never disposed here
    private SKTextBlob? _subBlob;
    private float _subBlobWidth;
    private SKColor _fill;
    private ConditioningControlPanel.Core.Platform.PixelRect _workArea = ConditioningControlPanel.Core.Platform.PixelRect.Empty;
    private double _scaling = 1.0;

    private readonly SKFont _font;
    private readonly SKFont _subFont;
    private readonly SKPaint _fillPaint;
    private readonly SKPaint _strokePaint;
    private readonly SKPaint _subStrokePaint;
    private readonly SKPaint _imagePaint;
    private readonly SKPaint _groupPaint;

    /// <summary>Fired on the engine tick (UI thread) when the fade-out completes — the
    /// service's queue-advance hook (WPF fade.Completed → ShowNext). NOT fired by
    /// <see cref="HideNow"/> (WPF CloseActive never chains).</summary>
    public Action? LineCompleted { get; set; }

    public ChaosAnnouncerLayer()
    {
        _font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), FontSizeDip)
        {
            Subpixel = true,
        };
        _subFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), SubFontSizeDip)
        {
            Subpixel = true,
        };
        _fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _strokePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = StrokePenWidth,
            StrokeJoin = SKStrokeJoin.Round,
            Color = StrokeColor,
        };
        _subStrokePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = SubStrokePenWidth,
            StrokeJoin = SKStrokeJoin.Round,
            Color = StrokeColor,
        };
        _imagePaint = new SKPaint { IsAntialias = true };
        _groupPaint = new SKPaint();
    }

    public override int ZIndex => CompositorLayers.ChaosAnnouncer;

    public override bool IsActive
    {
        get { lock (_sync) { return _phase != Phase.Idle; } }
    }

    // ConsumeDirty stays the base always-dirty: the line fades/pops every frame while
    // showing, and while Idle IsActive is false so the engine never ticks or renders.

    /// <summary>Show one announcement line, replacing whatever is on screen (WPF DisplayCore).
    /// <paramref name="fill"/> is the FINAL palette color (service policy). <paramref name="artPath"/>
    /// is the resolved announce-art path or null; a missing/failed decode falls back to the
    /// text look exactly like WPF ChaosArt.Resolve returning null.</summary>
    public void ShowLine(string text, Color fill, string? artPath, string? subText, int holdMs,
        ConditioningControlPanel.Core.Platform.PixelRect workAreaPx, double scaling)
    {
        var art = ChaosLayerArtCache.Get(artPath);
        SKTextBlob? blob = null;
        float blobWidth = 0;
        SKTextBlob? subBlob = null;
        float subBlobWidth = 0;
        if (art == null)
        {
            var upper = (text ?? "").ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(upper)) return;
            blob = SKTextBlob.Create(upper, _font);
            if (blob == null) return;
            blobWidth = _font.MeasureText(upper);
        }
        else if (!string.IsNullOrWhiteSpace(subText))
        {
            var subUpper = subText!.ToUpperInvariant();
            subBlob = SKTextBlob.Create(subUpper, _subFont);
            subBlobWidth = subBlob != null ? _subFont.MeasureText(subUpper) : 0;
        }

        lock (_sync)
        {
            _blob?.Dispose();
            _subBlob?.Dispose();
            _blob = blob;
            _blobWidth = blobWidth;
            _art = art;
            _subBlob = subBlob;
            _subBlobWidth = subBlobWidth;
            _fill = new SKColor(fill.R, fill.G, fill.B);
            _workArea = workAreaPx;
            _scaling = scaling > 0 ? scaling : 1.0;
            _holdMs = Math.Max(0, holdMs);
            _phase = Phase.In;
            _phaseClockMs = 0;
            _scaleClockMs = 0;
            _outStartOpacity = 1.0;
            _completionPending = false;
        }
    }

    /// <summary>End the currently-shown line ASAP so a higher-priority (STORY narrator) line
    /// lands next: jump to the fade-out from the CURRENT opacity (WPF CutShort restarts the
    /// life timer at 1ms and the fade animates from the window's current opacity).</summary>
    public void CutShort()
    {
        lock (_sync)
        {
            if (_phase == Phase.In) BeginOutLocked(Math.Clamp(_phaseClockMs / InMs, 0, 1));
            else if (_phase == Phase.Hold) BeginOutLocked(1.0);
        }
    }

    /// <summary>Instant teardown (WPF CloseActive): drop the visible line without firing
    /// <see cref="LineCompleted"/> — the service clears its queue itself.</summary>
    public void HideNow()
    {
        lock (_sync)
        {
            _blob?.Dispose();
            _subBlob?.Dispose();
            _blob = null;
            _subBlob = null;
            _art = null;
            _phase = Phase.Idle;
            _completionPending = false;
        }
    }

    private void BeginOutLocked(double startOpacity)
    {
        _phase = Phase.Out;
        _phaseClockMs = 0;
        _outStartOpacity = startOpacity;
    }

    public override void Update(TimeSpan deltaTime)
    {
        var fireCompleted = false;
        lock (_sync)
        {
            if (_phase == Phase.Idle) return;
            var dt = deltaTime.TotalMilliseconds;
            _scaleClockMs += dt; // the pop runs across phases (WPF never stops it)
            _phaseClockMs += dt;
            switch (_phase)
            {
                case Phase.In:
                    if (_phaseClockMs >= InMs)
                    {
                        _phase = Phase.Hold;
                        _phaseClockMs -= InMs;
                    }
                    break;
                case Phase.Hold:
                    if (_phaseClockMs >= _holdMs) BeginOutLocked(1.0);
                    break;
                case Phase.Out:
                    if (_phaseClockMs >= OutMs)
                    {
                        _blob?.Dispose();
                        _subBlob?.Dispose();
                        _blob = null;
                        _subBlob = null;
                        _art = null;
                        _phase = Phase.Idle;
                        fireCompleted = !_completionPending;
                        _completionPending = true;
                    }
                    break;
            }
        }
        // Outside the lock: the service's handler takes its own queue lock and may call
        // ShowLine (which takes _sync) — service→layer lock order only, no inversion.
        if (fireCompleted) LineCompleted?.Invoke();
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        lock (_sync)
        {
            if (_phase == Phase.Idle || _workArea.IsEmpty) return;
            var opacity = _phase switch
            {
                Phase.In => Math.Clamp(_phaseClockMs / InMs, 0, 1),
                Phase.Hold => 1.0,
                _ => _outStartOpacity * Math.Max(0, 1 - _phaseClockMs / OutMs),
            };
            if (opacity <= 0) return;

            // WPF BackEase(0.6) EaseOut over 180ms, from 0.85 to 1.0 (with a slight overshoot).
            var t = Math.Clamp(_scaleClockMs / PopMs, 0, 1);
            var inv = 1 - t;
            var eased = 1 - (Math.Pow(inv, 3) - inv * BackAmplitude * Math.Sin(Math.PI * inv));
            var scale = ScaleFrom + (1.0 - ScaleFrom) * eased;

            var s = (float)_scaling;
            var fm = _font.Metrics;
            var sfm = _subFont.Metrics;

            // Content box in DIPs (the OutlinedText/Image sizes WPF lays out at the anchor).
            double wDip, hDip;
            if (_art != null)
            {
                var artWDip = ArtHeightDip * (_art.Height > 0 ? (double)_art.Width / _art.Height : 1.0);
                var subHDip = _subBlob != null ? SubGapDip + SubPadDip * 2 + (sfm.Descent - sfm.Ascent) : 0;
                var subWDip = _subBlob != null ? _subBlobWidth + SubPadDip * 2 : 0;
                wDip = Math.Max(artWDip, subWDip);
                hDip = ArtHeightDip + subHDip;
            }
            else
            {
                wDip = _blobWidth + TextPadDip * 2;
                hDip = TextPadDip * 2 + (fm.Descent - fm.Ascent);
            }

            // Centered over the PRIMARY work area, content top at wa.Top + 92 DIP; the pop
            // scales about the content center (WPF RenderTransformOrigin 0.5,0.5).
            var cx = (float)(_workArea.X + _workArea.Width / 2.0);
            var cy = (float)(_workArea.Y + TopOffsetDip * s + hDip * s / 2.0);

            var save = canvas.Save();
            canvas.Translate(cx, cy);
            canvas.Scale((float)(scale * s));

            var alpha = (byte)Math.Clamp(opacity * 255, 0, 255);
            var halfW = (float)(wDip / 2.0);
            var halfH = (float)(hDip / 2.0);
            if (alpha < 255)
            {
                // WPF fades WINDOW opacity: stroke+fill+art composite as one group.
                var pad = StrokePenWidth + 2f;
                _groupPaint.Color = SKColors.White.WithAlpha(alpha);
                canvas.SaveLayer(new SKRect(-halfW - pad, -halfH - pad, halfW + pad, halfH + pad), _groupPaint);
            }

            if (_art != null)
            {
                var artWDip = ArtHeightDip * (_art.Height > 0 ? (double)_art.Width / _art.Height : 1.0);
                var dest = new SKRect(
                    (float)(-artWDip / 2.0), -halfH,
                    (float)(artWDip / 2.0), (float)(-hDip / 2.0 + ArtHeightDip));
                _imagePaint.Color = SKColors.White;
                canvas.DrawImage(_art, dest, Sampling, _imagePaint);
                if (_subBlob != null)
                {
                    var subBaseline = (float)(-hDip / 2.0 + ArtHeightDip + SubGapDip) + SubPadDip - sfm.Ascent;
                    _fillPaint.Color = _fill;
                    canvas.DrawText(_subBlob, -_subBlobWidth / 2f, subBaseline, _subStrokePaint);
                    canvas.DrawText(_subBlob, -_subBlobWidth / 2f, subBaseline, _fillPaint);
                }
            }
            else if (_blob != null)
            {
                var baseline = -halfH + TextPadDip - fm.Ascent;
                _fillPaint.Color = _fill;
                canvas.DrawText(_blob, -_blobWidth / 2f, baseline, _strokePaint); // stroke UNDER fill
                canvas.DrawText(_blob, -_blobWidth / 2f, baseline, _fillPaint);
            }

            canvas.RestoreToCount(save);
        }
    }
}
