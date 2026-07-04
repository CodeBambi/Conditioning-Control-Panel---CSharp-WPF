using System;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Chaos "braindrain" payload: ONE random flash-pool image held over the whole stage at a
/// low opacity for a few seconds, fading in then back out (WS2/WP3 Phase F #5 — distinct
/// from the session <see cref="FlashLayer"/> at Z=30, which pops many small clickable
/// flashes; this is the chaos-run full-screen wash and the two must never merge).
///
/// Behavior contract (WPF Chaos/ChaosFlashOverlay.cs):
/// - one wash at a time; a new Show() SWAPS the image and restarts the cycle from opacity 0
///   (WPF DisplayCore: life stop → ClearImage → fresh 0→peak fade);
/// - fade-in 0 → peak over 500ms; peak = clamp(opacity, 0.02, 1.0);
/// - hold: the WPF life timer starts WITH the fade-in at max(600, durationMs), so fade-out
///   begins at t = holdMs (the ≥600ms floor guarantees the 500ms fade-in completes first);
/// - fade-out peak → 0 over 700ms, then the image is cleared and the surface goes idle;
/// - stage = primary screen unless dual-monitor is enabled (WPF ChaosWindowZ.StageBounds —
///   the service passes the effect-screen union; the legacy Avalonia window forced primary
///   always, a parity drift this layer fixes);
/// - stretch = WPF UniformToFill: cover-fit anchored TOP-LEFT (WPF clips the right/bottom
///   overflow; Avalonia's UniformToFill centers — WPF is the contract), clipped to the stage;
/// - GIFs loop at their real per-frame delays for the whole wash.
///
/// Decode-once discipline (SpiralLayer pattern): the owning service decodes OFF-thread via
/// <see cref="SkiaImageDecoder"/> into a <see cref="SkiaFrameSet"/> and hands the set here —
/// no per-frame decodes (the legacy window streamed GIF frames through AvaloniaAnimatedGif).
/// Budgets: stills cap 2560 (WPF DecodePixelWidth min(2560, …)); animated cap 1280 / 40
/// frames (the WPF animated-WEBP wash budget — WPF GIFs streamed native-res one frame at a
/// time, which decode-once cannot mirror, so the webp budget WPF itself picked for "a faint
/// 10% wash doesn't need native res" is the honest analog) + the spiral 96MB safety cap.
///
/// Frame-set lifetime (UCE rule 8, FlashLayer discipline): Render draws under _sync and
/// every Release happens under _sync, so no draw can race disposal.
///
/// Capture-VISIBLE (main surface): no WPF chaos window calls SetWindowDisplayAffinity.
/// Z-order comes from CompositorLayers only (UCE rule 9) — the WPF Show-time
/// RaiseAboveVideo/ForceTopmost churn has no layer equivalent.
/// </summary>
public sealed class ChaosFlashWashLayer : BaseLayer
{
    private const double FadeInMs = 500.0;   // WPF DisplayCore fade-in
    private const double FadeOutMs = 700.0;  // WPF _life.Tick fade-out

    private readonly object _sync = new();
    // Reused paint: only touched inside Render under _sync. Never disposed (layer lives app-long).
    private readonly SKPaint _paint = new() { IsAntialias = true };

    private SkiaFrameSet? _frames;   // owned reference; released under _sync
    private ConditioningControlPanel.Core.Platform.PixelRect _stage = ConditioningControlPanel.Core.Platform.PixelRect.Empty;
    private double _clockMs;
    private double _peak;
    private double _holdMs;
    private int _frameIndex;
    private double _frameTimerSec;
    private bool _dirty;

    public override int ZIndex => CompositorLayers.ChaosFlashWash;

    public override bool IsActive
    {
        get { lock (_sync) { return _frames != null; } }
    }

    /// <summary>
    /// Show (or swap to) a wash. Takes ownership of one reference on <paramref name="frames"/>.
    /// <paramref name="stagePx"/> is the stage rect in PHYSICAL virtual-desktop px;
    /// <paramref name="durationMs"/>/<paramref name="opacity"/> are the raw WPF Show args
    /// (clamps applied here, WPF DisplayCore parity).
    /// </summary>
    public void ShowWash(SkiaFrameSet frames, ConditioningControlPanel.Core.Platform.PixelRect stagePx,
        int durationMs, double opacity)
    {
        lock (_sync)
        {
            _frames?.Release();      // under _sync: no render pass can be mid-draw
            _frames = frames;
            _stage = stagePx;
            _clockMs = 0;
            _peak = Math.Clamp(opacity, 0.02, 1.0);
            _holdMs = Math.Max(600, durationMs);
            _frameIndex = 0;
            _frameTimerSec = 0;
            _dirty = true;
        }
    }

    /// <summary>Instant teardown (run end — WPF CloseActive).</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _frames?.Release();
            _frames = null;
            _dirty = true;
        }
    }

    public override void Update(TimeSpan deltaTime)
    {
        lock (_sync)
        {
            if (_frames == null) return;
            _clockMs += deltaTime.TotalMilliseconds;

            if (_clockMs >= _holdMs + FadeOutMs)
            {
                // Wash over: clear + idle (WPF fade.Completed → ClearImage + Hide).
                _frames.Release();
                _frames = null;
                _dirty = true;
                return;
            }

            // Fading in/out = every frame repaints.
            if (_clockMs < FadeInMs || _clockMs >= _holdMs) _dirty = true;

            // Advance GIF frames at the file's real per-frame delays (FlashLayer pattern).
            if (_frames.IsAnimated)
            {
                var delays = _frames.FrameDelaysSeconds;
                _frameTimerSec += deltaTime.TotalSeconds;
                var guard = 0;
                while (guard++ < 1000)
                {
                    var delay = delays[_frameIndex % delays.Length];
                    if (delay <= 0.0005 || _frameTimerSec < delay) break;
                    _frameTimerSec -= delay;
                    _frameIndex = (_frameIndex + 1) % _frames.Frames.Length;
                    _dirty = true;
                }
            }
        }
    }

    public override bool ConsumeDirty()
    {
        lock (_sync)
        {
            var was = _dirty;
            _dirty = false;
            return was;
        }
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
    {
        lock (_sync)
        {
            var frames = _frames;
            if (frames == null || _stage.IsEmpty) return;
            var image = frames.Frames[_frameIndex % frames.Frames.Length];
            if (image == null || image.Width <= 0 || image.Height <= 0) return;

            // WPF opacity timeline: 500ms in → hold → 700ms out (see class doc).
            double opacity;
            if (_clockMs < FadeInMs) opacity = _peak * (_clockMs / FadeInMs);
            else if (_clockMs < _holdMs) opacity = _peak;
            else opacity = _peak * (1.0 - (_clockMs - _holdMs) / FadeOutMs);
            if (opacity <= 0) return;

            // WPF UniformToFill: cover-fit, TOP-LEFT anchored, right/bottom overflow clipped.
            var cover = Math.Max(_stage.Width / image.Width, _stage.Height / image.Height);
            var dest = new SKRect(
                (float)_stage.X, (float)_stage.Y,
                (float)(_stage.X + image.Width * cover), (float)(_stage.Y + image.Height * cover));

            var save = canvas.Save();
            canvas.ClipRect(ToSkRect(_stage));
            _paint.Color = new SKColor(255, 255, 255, (byte)Math.Clamp(opacity * 255, 0, 255));
            canvas.DrawImage(image, dest, _paint);
            canvas.RestoreToCount(save);
        }
    }
}
