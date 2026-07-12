using System;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Services.Video;

/// <summary>
/// Owns the <see cref="BrowserMirrorVideoLayer"/>: starts/stops mirroring the browser's
/// fullscreen video onto every OTHER monitor. Activated by the browser-fullscreen path
/// (SettingsTabView) and by engine-initiated online-video navigation (auto-fullscreen).
///
/// Design choice — SCREEN-CAPTURE, not URL resolve: the owner wants EVERY browser fullscreen
/// video mirrored (including HTML video pages like HypnoTube and DRM/streaming content), and
/// resolving the real media URL from an arbitrary page is unreliable/DRM-bound. Capturing the
/// monitor the browser is fullscreen on via <see cref="IFrameSource"/> reliably mirrors whatever
/// the browser is playing. The per-monitor WINDOWING is reused from the mandatory-video path:
/// the layer registers with the <see cref="CompositorEngine"/> and renders on every per-monitor
/// compositor window (one topmost window per monitor), exactly like
/// <see cref="MandatoryVideoLayer"/>. So "use mandatory-video stuff" is honored at the render
/// surface, with the reliable capture source swapped in.
///
/// PRIVACY: captured frames are MEMORY-ONLY (never disk/net/logs) per the IFrameSource contract;
/// this service adds no persistence. Degrades to a no-op on heads without an IFrameSource
/// (Linux/macOS until their capture backends exist) — Start() simply does nothing.
/// </summary>
public sealed class BrowserMirrorVideoService : IDisposable
{
    private readonly CompositorEngine? _compositor;
    private readonly IFrameSource? _frameSource;
    private readonly IScreenProvider? _screens;
    private readonly ILogger<BrowserMirrorVideoService>? _logger;

    private BrowserMirrorVideoLayer? _layer;
    private bool _registered;
    private bool _disposed;

    /// <summary>True while a mirror capture loop is running (probe for verification/logging).</summary>
    public bool IsMirroring => _layer?.IsActive == true;

    public BrowserMirrorVideoService(
        CompositorEngine? compositor = null,
        IFrameSource? frameSource = null,
        IScreenProvider? screens = null,
        ILogger<BrowserMirrorVideoService>? logger = null)
    {
        _compositor = compositor;
        _frameSource = frameSource;
        _screens = screens;
        _logger = logger;
    }

    /// <summary>
    /// Begins mirroring the browser's fullscreen video onto every other monitor. The browser is
    /// assumed to already be fullscreen on the primary monitor (the SettingsTabView fullscreen
    /// window, or an auto-fullscreen triggered by engine online-video navigation). Idempotent.
    /// </summary>
    /// <param name="sourceScreen">Explicit source monitor; when null the primary screen is used.</param>
    public void Start(ConditioningControlPanel.Core.Platform.ScreenInfo? sourceScreen = null)
    {
        if (_disposed) return;
        if (_compositor == null || _frameSource == null || _screens == null)
        {
            _logger?.LogDebug("BrowserMirrorVideoService: mirror unavailable on this head (no compositor/frame-source/screens)");
            return;
        }

        var source = sourceScreen ?? _screens.GetPrimaryScreen() ?? FirstScreen();
        if (source == null || source.Bounds.IsEmpty)
        {
            _logger?.LogWarning("BrowserMirrorVideoService: no source screen to mirror from");
            return;
        }

        if (_layer == null)
        {
            _layer = new BrowserMirrorVideoLayer(_frameSource, _logger);
            if (!_registered)
            {
                _compositor.RegisterLayer(_layer);
                _registered = true;
            }
        }

        _layer.Start(source);
        // Wake the engine to the active cadence immediately (same-tick wake instead of the ~250ms
        // idle-watchdog latency) so the first captured frame paints without delay.
        _compositor.Start();

        var targets = CountNonSourceScreens(source);
        _logger?.LogInformation("BrowserMirrorVideoService: mirror started, source={Source}, target monitor(s)={Count}", source.Name, targets);
    }

    /// <summary>Stops the mirror capture loop and clears the mirrored frame (other monitors return to the desktop).</summary>
    public void Stop()
    {
        if (_layer == null) return;
        if (!_layer.IsActive) return;
        _layer.Stop();
        _logger?.LogInformation("BrowserMirrorVideoService: mirror stopped");
    }

    private ConditioningControlPanel.Core.Platform.ScreenInfo? FirstScreen()
    {
        var all = _screens?.GetAllScreens();
        if (all == null || all.Count == 0) return null;
        return all[0];
    }

    private int CountNonSourceScreens(ConditioningControlPanel.Core.Platform.ScreenInfo source)
    {
        var all = _screens?.GetAllScreens();
        if (all == null) return 0;
        var count = 0;
        foreach (var s in all)
        {
            if (!Equals(s, source)) count++;
        }
        return count;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var layer = _layer;
        if (layer != null)
        {
            try { _compositor?.UnregisterLayer(layer); } catch { /* ignore */ }
            try { layer.Dispose(); } catch { /* ignore */ }
        }
        _registered = false;
        _layer = null;
    }
}
