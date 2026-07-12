using System;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Renders the brain-drain effect: a blurred live copy of each monitor, WPF parity with
/// OverlayService's "Brain Drain Blur (Screen Capture - Optimized)" region. The screen is
/// captured downscaled at a service-configured FPS, blurred with an intensity-scaled radius,
/// and stretched back over the monitor so on-screen content becomes unreadable.
/// On non-Windows platforms (or when capture is broken) it degrades to the pulsing
/// dark-violet tint the port used before the blur landed.
/// </summary>
public sealed class BrainDrainLayer : BaseLayer, IDisposable
{
    // WPF parity: the capture is BitBlt-shrunk by this factor and blurred with a
    // proportionally smaller radius; the upscale back to full screen reads as extra blur.
    // WPF picks the factor from PerformanceProfile (4 on the default tier, 8 under load);
    // the perf-tier system is not ported, so the default tier's factor is used.
    private const int Downscale = 4;

    // After this many consecutive capture failures the layer stops burning GDI calls and
    // falls back to the tint rendering for the rest of the activation.
    private const int MaxConsecutiveFailures = 30;

    // Screens that have not been rendered for this long (window closed / monitor removed)
    // are pruned and their cached frame disposed.
    private static readonly TimeSpan ScreenRetention = TimeSpan.FromSeconds(2);

    private readonly object _frameLock = new();
    private readonly Dictionary<string, ScreenFrame> _frames = new();

    private int _intensity;
    private int _captureFps = 30;
    private double _pulsePhase;

    // IMP-4: cache render-path paints/filter instead of allocating them per frame. The blur
    // paint+filter is rebuilt only when sigma changes; the tint paint's Color is mutated in
    // place each frame (SKColor is a struct, no alloc). All three are touched only on the single
    // render thread. Blur access is additionally serialized under _frameLock; the tint fallback
    // runs lock-free, so its teardown safety rests on shutdown ordering (Stop() sets intensity 0
    // on the UI thread before Dispose), the `_intensity <= 0` render gate, and the engine's
    // per-layer render try/catch — not on _frameLock. WPF-parity blur/tint math is unchanged.
    private SKPaint? _blurPaint;
    private SKImageFilter? _blurFilter;
    private float _blurSigma = float.NaN;
    private SKPaint? _tintPaint;

    public override int ZIndex => CompositorLayers.BrainDrain;

    public override bool IsActive => _intensity > 0;

    /// <summary>
    /// CAPTURE-REGION: brain drain is a full-screen blur that paints the entire monitor, so
    /// clicks anywhere on the monitor must land on it (per-region rule 2026-07-09 — brain
    /// drain moved to capture-by-default; it is no longer static click-through). Lives on the
    /// capture-EXCLUDED surface (<see cref="ExcludeFromCapture"/>), but the capture mask is
    /// about INPUT, not screen capture: a brain-drain region still swallows pointer input.
    /// </summary>
    public override void CollectCaptureRegions(
        ConditioningControlPanel.Core.Services.Compositor.CaptureMaskBuilder builder,
        System.Collections.Generic.IReadOnlyList<ConditioningControlPanel.Core.Platform.ScreenInfo> screens)
    {
        for (int i = 0; i < screens.Count; i++)
            builder.Add(screens[i].Bounds);
    }

    /// <summary>
    /// GUARDRAIL (WPF parity, OverlayService.cs:1685): brain drain must never appear in
    /// screenshots, streams, recordings, or the app's own screen captures (keyword OCR and
    /// this layer's own blur source). The engine therefore routes it to the capture-excluded
    /// compositor surface (SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)), which is also
    /// what breaks the blur's self-capture feedback loop. Subliminals and every other layer
    /// stay on the main, capturable surface BY DESIGN.
    /// </summary>
    public override bool ExcludeFromCapture => true;

    public void SetIntensity(int intensity)
    {
        _intensity = intensity;
        if (intensity <= 0)
            ClearFrames();
    }

    /// <summary>Capture rate; the service derives it from BrainDrainHighRefresh (60) vs normal (30), WPF parity.</summary>
    public void SetCaptureFps(int fps)
    {
        _captureFps = Math.Clamp(fps, 1, 60);
    }

    public override void Update(TimeSpan deltaTime)
    {
        if (_intensity <= 0) return;

        _pulsePhase += deltaTime.TotalSeconds * 2.0;
        if (_pulsePhase > Math.PI * 2) _pulsePhase -= Math.PI * 2;

        if (!OperatingSystem.IsWindows()) return;

        // Capture on the UI thread at the configured FPS (WPF parity: a DispatcherTimer
        // drives CaptureScreenOptimized at 30/60 FPS). Render() self-registers each monitor
        // it is asked to draw, so only screens actually composited get captured, and stale
        // screens are pruned here.
        var now = DateTime.UtcNow;
        var interval = TimeSpan.FromMilliseconds(1000.0 / _captureFps);

        List<(string Key, ScreenInfo Screen)>? due = null;
        lock (_frameLock)
        {
            List<string>? stale = null;
            foreach (var (key, frame) in _frames)
            {
                if (now - frame.LastSeen > ScreenRetention)
                {
                    (stale ??= new List<string>()).Add(key);
                    continue;
                }
                if (frame.Failures >= MaxConsecutiveFailures) continue; // capture broken; tint fallback
                if (now - frame.LastCapture < interval) continue;
                (due ??= new List<(string, ScreenInfo)>()).Add((key, frame.Screen));
            }

            if (stale != null)
            {
                foreach (var key in stale)
                {
                    _frames[key].Image?.Dispose();
                    _frames.Remove(key);
                }
            }
        }

        if (due == null) return;

        foreach (var (key, screen) in due)
        {
            // The capture cannot see this layer: its window carries WDA_EXCLUDEFROMCAPTURE,
            // which GDI screen capture honors — no self-capture feedback loop (WPF parity).
            var image = BrainDrainScreenCapture.CaptureDownscaled(screen.Bounds, Downscale);
            lock (_frameLock)
            {
                if (!_frames.TryGetValue(key, out var frame))
                {
                    image?.Dispose();
                    continue;
                }

                frame.LastCapture = now;
                if (image == null)
                {
                    frame.Failures++;
                }
                else
                {
                    // Superseded frame disposed under the same lock Render draws under, so
                    // the render thread can never be mid-draw on the old image.
                    frame.Image?.Dispose();
                    frame.Image = image;
                    frame.Failures = 0;
                }
            }
        }
    }

    /// <summary>Screen-aware render: draws the blurred capture for the monitor being composited.</summary>
    public void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, ScreenInfo? screen, TimeSpan deltaTime)
    {
        if (_intensity <= 0) return;

        if (OperatingSystem.IsWindows() && screen != null)
        {
            lock (_frameLock)
            {
                var key = ScreenKey(screen);
                if (!_frames.TryGetValue(key, out var frame))
                {
                    frame = new ScreenFrame(screen);
                    _frames[key] = frame;
                }
                frame.Screen = screen;
                frame.LastSeen = DateTime.UtcNow;

                if (frame.Image != null)
                {
                    // WPF parity blur math (OverlayService.cs:1643): radius = intensity * 0.4
                    // divided by the downscale factor, because the blurred bitmap is
                    // 1/Downscale size and the stretch back to full screen supplies the rest.
                    var sigma = (float)(_intensity * 0.4 / Downscale);
                    if (_blurPaint is null || sigma != _blurSigma)
                    {
                        _blurPaint?.Dispose();
                        _blurFilter?.Dispose();
                        _blurFilter = SKImageFilter.CreateBlur(sigma, sigma);
                        _blurPaint = new SKPaint { ImageFilter = _blurFilter };
                        _blurSigma = sigma;
                    }
                    canvas.DrawImage(frame.Image, ToSkRect(bounds), _blurPaint);
                    return;
                }

                if (frame.Failures < MaxConsecutiveFailures)
                {
                    // First capture has not landed yet: draw nothing this frame (WPF parity —
                    // the blur window is transparent until the first capture tick).
                    return;
                }
                // Capture is broken on this system; fall through to the tint fallback.
            }
        }

        RenderTintFallback(canvas, bounds);
    }

    public override void Render(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds, TimeSpan deltaTime)
        => Render(canvas, bounds, null, deltaTime);

    /// <summary>
    /// Pre-blur fallback: dark-violet pulsing tint. Used on non-Windows platforms, when the
    /// engine cannot identify the monitor, or when screen capture is persistently failing.
    /// </summary>
    private void RenderTintFallback(SKCanvas canvas, ConditioningControlPanel.Core.Platform.PixelRect bounds)
    {
        var baseAlpha = Math.Clamp(_intensity / 100.0, 0, 1);
        var pulse = 0.85 + 0.15 * Math.Sin(_pulsePhase); // subtle 85%-100% pulse
        var alpha = (byte)Math.Clamp(baseAlpha * pulse * 255, 0, 255);
        _tintPaint ??= new SKPaint();
        _tintPaint.Color = new SKColor(20, 0, 40, alpha);
        canvas.DrawRect(ToSkRect(bounds), _tintPaint);
    }

    // Name alone can collide (two same-model monitors share a DisplayName), so the key
    // includes the physical bounds — unique per monitor within a display configuration.
    private static string ScreenKey(ScreenInfo screen)
        => $"{screen.Name}|{screen.Bounds.X},{screen.Bounds.Y},{screen.Bounds.Width}x{screen.Bounds.Height}";

    private void ClearFrames()
    {
        lock (_frameLock)
        {
            foreach (var frame in _frames.Values)
                frame.Image?.Dispose();
            _frames.Clear();
        }
    }

    public void Dispose()
    {
        ClearFrames();
        lock (_frameLock)
        {
            _blurPaint?.Dispose();
            _blurFilter?.Dispose();
            _tintPaint?.Dispose();
            _blurPaint = null;
            _blurFilter = null;
            _tintPaint = null;
        }
    }

    private sealed class ScreenFrame
    {
        public ScreenFrame(ScreenInfo screen)
        {
            Screen = screen;
        }

        public ScreenInfo Screen;
        public SKImage? Image;
        public DateTime LastSeen;
        public DateTime LastCapture;
        public int Failures;
    }
}
