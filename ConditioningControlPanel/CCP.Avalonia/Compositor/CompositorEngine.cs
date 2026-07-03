using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Compositor;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using PixelRect = ConditioningControlPanel.Core.Platform.PixelRect;

namespace ConditioningControlPanel.Avalonia.Compositor;

/// <summary>
/// The unified compositor engine. Renders all registered <see cref="ILayer"/> instances
/// directly into Avalonia's render thread via <see cref="CompositorDrawOp"/> at 60Hz.
/// No WriteableBitmap, no Image control, no manual invalidation — Avalonia handles presentation.
/// </summary>
public sealed class CompositorEngine : IDisposable
{
    private readonly ILogger<CompositorEngine>? _logger;
    private readonly IScreenProvider? _screenProvider;
    private readonly List<CompositorWindow> _windows = new();
    // Second, capture-excluded surface (one window per monitor, WDA_EXCLUDEFROMCAPTURE).
    // Created lazily on the first tick where a layer with ExcludeFromCapture=true is active
    // (today: BrainDrainLayer only), torn down again after 500ms of excluded-idle.
    // Inter-surface z caveat: two sibling topmost windows cannot interleave layers, so the
    // excluded surface is shown LAST and therefore sits above every main-surface layer
    // (brain drain z55 renders above spiral z60 / pink tint z70). WPF's inter-window z was
    // show-order based too, so this is the documented, accepted order.
    private readonly List<CompositorWindow> _excludedWindows = new();
    private readonly SortedList<int, ILayer> _layers = new();
    private readonly DispatcherTimer _timer;
    private readonly object _layerLock = new();
    private DateTime? _emptySince;
    private DateTime? _excludedEmptySince;
    // Epochs cancel staggered window-creation timers that outlive their surface
    // (engine stopped, or the excluded surface torn down, before a 250ms timer fired).
    private int _mainEpoch;
    private int _excludedEpoch;
    // Epoch for which excluded-surface creation has already been scheduled; prevents the
    // tick from re-scheduling (and duplicating) a staggered batch when the first window
    // creation failed but later staggered windows are still pending.
    private int _excludedScheduledEpoch = -1;

    private DateTime _lastFrame = DateTime.MinValue;
    private bool _disposed;

    public CompositorEngine(ILogger<CompositorEngine>? logger = null, IScreenProvider? screenProvider = null)
    {
        _logger = logger;
        _screenProvider = screenProvider;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 Hz
        };
        _timer.Tick += OnFrameTick;
    }

    /// <summary>
    /// Starts the compositor: creates one <see cref="CompositorWindow"/> per monitor
    /// and begins the 60Hz update loop. Safe to call multiple times.
    /// </summary>
    public void Start()
    {
        if (_disposed) return;
        if (_windows.Count > 0)
        {
            // Already started; just ensure the timer is running.
            if (!_timer.IsEnabled)
            {
                _lastFrame = DateTime.UtcNow;
                _emptySince = null;
                _timer.Start();
            }
            return;
        }

        var screens = _screenProvider?.GetAllScreens() ?? Array.Empty<ScreenInfo>();
        if (screens.Count == 0)
        {
            screens = new[] { new ScreenInfo("fallback", new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), new ConditioningControlPanel.Core.Platform.PixelRect(0, 0, 1920, 1080), 1.0) };
        }

        // Create the compositor windows one per dispatcher tick. Creating several transparent
        // topmost windows (each starting its own native Win32 surface + render thread) in the
        // same frame races inside Avalonia v12's Win32 platform backend and intermittently
        // faults with a native access violation (0xC0000005) before managed code runs. Giving
        // each window a full message-pump cycle to finish its native init eliminates the race.
        StaggeredCreateWindows(screens, excludeFromCapture: false);
    }

    private void StaggeredCreateWindows(IReadOnlyList<ScreenInfo> screens, bool excludeFromCapture)
    {
        if (_disposed || screens.Count == 0) return;

        // Create the first window synchronously so the caller has at least one window up, then
        // create the remaining windows on staggered DispatcherTimers (~250ms apart). Creating
        // several transparent topmost windows in the same frame races inside Avalonia v12's
        // Win32 platform backend and intermittently faults with a native access violation
        // (0xC0000005) before managed code runs. Spacing them out gives each native window
        // surface time to fully initialize its render thread. The excluded surface obeys the
        // same stagger rule.
        var epoch = excludeFromCapture ? _excludedEpoch : _mainEpoch;
        CreateOneWindow(screens[0], excludeFromCapture);
        MaybeStartTimer();

        for (int i = 1; i < screens.Count; i++)
        {
            var screen = screens[i];
            var due = TimeSpan.FromMilliseconds(250 * i);
            var t = new DispatcherTimer { Interval = due };
            t.Tick += (_, _) =>
            {
                t.Stop();
                // The surface this window was scheduled for may have been torn down while
                // the timer was pending; creating it now would leak an orphaned window.
                var currentEpoch = excludeFromCapture ? _excludedEpoch : _mainEpoch;
                if (currentEpoch != epoch) return;
                CreateOneWindow(screen, excludeFromCapture);
                MaybeStartTimer();
            };
            t.Start();
        }
    }

    private void CreateOneWindow(ScreenInfo screen, bool excludeFromCapture)
    {
        if (_disposed) return;
        try
        {
            var window = new CompositorWindow(screen, this, excludeFromCapture);
            window.Show();
            // ApplyNativeTransparency is deferred to the window's Opened handler (next
            // dispatcher tick). Calling SetWindowLong/SetWindowSubclass synchronously right
            // after Show() races with Avalonia's native window initialization + render thread
            // startup, causing an intermittent native access violation (0xC0000005).
            (excludeFromCapture ? _excludedWindows : _windows).Add(window);
            _logger?.LogInformation("CompositorWindow created on {Screen} (excludeFromCapture={Excluded})", screen.Name, excludeFromCapture);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create CompositorWindow on {Screen}", screen.Name);
        }
    }

    private void MaybeStartTimer()
    {
        if (_windows.Count > 0 && !_timer.IsEnabled && !_disposed)
        {
            _lastFrame = DateTime.UtcNow;
            _timer.Start();
            _logger?.LogInformation("CompositorEngine started: {Count} window(s)", _windows.Count);
        }
    }

    /// <summary>Stops the update loop and closes all compositor windows (both surfaces).</summary>
    public void Stop()
    {
        _emptySince = null;
        _timer.Stop();
        _mainEpoch++; // cancel any pending staggered main-window creation
        foreach (var window in _windows.ToList())
        {
            try { window.Close(); } catch { /* ignore */ }
        }
        _windows.Clear();
        CloseExcludedWindows();
        _logger?.LogInformation("CompositorEngine stopped");
    }

    /// <summary>Closes the capture-excluded surface windows (excluded layers went idle or engine stopped).</summary>
    private void CloseExcludedWindows()
    {
        _excludedEmptySince = null;
        _excludedEpoch++; // cancel any pending staggered excluded-window creation
        if (_excludedWindows.Count == 0) return;
        foreach (var window in _excludedWindows.ToList())
        {
            try { window.Close(); } catch { /* ignore */ }
        }
        _excludedWindows.Clear();
        _logger?.LogInformation("CompositorEngine: excluded surface closed");
    }

    /// <summary>Register a layer with the compositor. Layer is ordered by <see cref="ILayer.ZIndex"/>.</summary>
    public void RegisterLayer(ILayer layer)
    {
        lock (_layerLock)
        {
            if (_layers.ContainsKey(layer.ZIndex))
            {
                _logger?.LogWarning("Layer with ZIndex {ZIndex} already registered; replacing", layer.ZIndex);
                _layers[layer.ZIndex].OnDeactivated();
                _layers.Remove(layer.ZIndex);
            }
            _layers.Add(layer.ZIndex, layer);
            layer.OnActivated();
            _logger?.LogDebug("Layer registered: {Layer} at Z={ZIndex}", layer.GetType().Name, layer.ZIndex);
        }
    }

    /// <summary>Unregister a layer from the compositor.</summary>
    public void UnregisterLayer(ILayer layer)
    {
        lock (_layerLock)
        {
            if (_layers.TryGetValue(layer.ZIndex, out var existing) && ReferenceEquals(existing, layer))
            {
                layer.OnDeactivated();
                _layers.Remove(layer.ZIndex);
                _logger?.LogDebug("Layer unregistered: {Layer}", layer.GetType().Name);
            }
        }
    }

    /// <summary>Get the layer at the specified z-index, or null.</summary>
    public ILayer? GetLayer(int zIndex)
    {
        lock (_layerLock)
        {
            _layers.TryGetValue(zIndex, out var layer);
            return layer;
        }
    }

    /// <summary>All currently registered layers, ordered by z-index.</summary>
    public IReadOnlyList<IAvaloniaLayer> Layers
    {
        get
        {
            lock (_layerLock)
            {
                return _layers.Values.OfType<IAvaloniaLayer>().ToList();
            }
        }
    }

    /// <summary>Number of main-surface compositor windows (one per monitor).</summary>
    public int WindowCount => _windows.Count;

    /// <summary>Number of capture-excluded surface windows (0 unless an excluded layer is active).</summary>
    public int ExcludedWindowCount => _excludedWindows.Count;

    /// <summary>True when the update loop is running.</summary>
    public bool IsRunning => _timer.IsEnabled;

    private int _dialogModeRefCount;

    /// <summary>
    /// Temporarily lower compositor windows so dialogs and popups can be clicked.
    /// DEPRECATED: compositor now uses WS_EX_LAYERED | WS_EX_TRANSPARENT for native
    /// click-through, so it stays on top of dialogs while still passing clicks through.
    /// This method is kept for API compatibility but is a no-op.
    /// </summary>
    public void PushDialogMode()
    {
        Interlocked.Increment(ref _dialogModeRefCount);
        // No longer lowering Topmost — compositor stays on top with click-through styles
    }

    /// <summary>Restore compositor windows after a dialog closes. No-op for compatibility.</summary>
    public void PopDialogMode()
    {
        if (Interlocked.Decrement(ref _dialogModeRefCount) <= 0)
        {
            _dialogModeRefCount = 0;
        }
    }

    /// <summary>
    /// Render the active layers belonging to one surface into the given Skia canvas.
    /// Called from the render thread via <see cref="CompositorDrawOp"/>.
    /// Capture affinity split: the main surface renders only layers with
    /// <c>ExcludeFromCapture == false</c>; the excluded surface renders only layers with
    /// <c>ExcludeFromCapture == true</c>. <paramref name="screen"/> identifies the monitor
    /// whose window is being drawn (used by screen-aware layers such as the brain-drain blur).
    /// </summary>
    public void RenderToCanvas(SKCanvas canvas, PixelRect bounds, ScreenInfo? screen = null, bool excludedSurface = false)
    {
        // Clear to transparent so inactive pixels pass through to the desktop.
        // Avalonia's render thread may clear to a solid color before our ICustomDrawOperation
        // runs, so we explicitly clear to transparent here.
        canvas.Clear(SKColors.Transparent);

        IAvaloniaLayer[] activeLayers;
        lock (_layerLock)
        {
            activeLayers = _layers.Values.OfType<IAvaloniaLayer>()
                .Where(l => l.IsActive && l.ExcludeFromCapture == excludedSurface)
                .ToArray();
        }

        foreach (var layer in activeLayers)
        {
            try
            {
                canvas.Save();
                layer.Render(canvas, bounds, screen, TimeSpan.Zero);
                canvas.Restore();
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "Layer {Layer} render failed", layer.GetType().Name); }
        }
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        if (_disposed || _windows.Count == 0) return;

        var now = DateTime.UtcNow;
        var delta = now - _lastFrame;
        _lastFrame = now;

        // Cap delta to avoid large time jumps after pauses (e.g. debugger break, window drag)
        var cappedDelta = delta.TotalMilliseconds > 100
            ? TimeSpan.FromMilliseconds(100)
            : delta;

        IAvaloniaLayer[] activeLayers;
        lock (_layerLock)
        {
            activeLayers = _layers.Values.OfType<IAvaloniaLayer>().Where(l => l.IsActive).ToArray();
        }

        var anyExcludedActive = false;
        foreach (var layer in activeLayers)
        {
            if (layer.ExcludeFromCapture) { anyExcludedActive = true; break; }
        }

        // Keep compositor topmost whenever active layers are present.
        // WS_EX_LAYERED | WS_EX_TRANSPARENT handles click-through natively,
        // so we no longer need to lower the compositor for dialogs.
        var shouldBeTopmost = activeLayers.Length > 0;
        foreach (var window in _windows)
        {
            if (window.Topmost != shouldBeTopmost)
            {
                try { window.Topmost = shouldBeTopmost; }
                catch { }
            }
        }

        // Capture-excluded surface lifecycle: create lazily on first excluded-layer
        // activation (staggered, same v12 native-race rule as the main windows), tear
        // down after 500ms of excluded-idle so the surface does not outlive the effect.
        // Created after the main windows -> shown last -> sits above the main surface.
        if (anyExcludedActive)
        {
            _excludedEmptySince = null;
            if (_excludedWindows.Count == 0 && _excludedScheduledEpoch != _excludedEpoch)
            {
                _excludedScheduledEpoch = _excludedEpoch;
                var screens = _screenProvider?.GetAllScreens() ?? Array.Empty<ScreenInfo>();
                if (screens.Count == 0)
                {
                    screens = new[] { new ScreenInfo("fallback", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1080), 1.0) };
                }
                StaggeredCreateWindows(screens, excludeFromCapture: true);
            }
        }
        else if (_excludedWindows.Count > 0)
        {
            _excludedEmptySince ??= now;
            if ((now - _excludedEmptySince.Value).TotalMilliseconds > 500)
            {
                CloseExcludedWindows();
            }
        }

        if (activeLayers.Length == 0)
        {
            // No active layers — start the auto-shutdown timer.
            _emptySince ??= now;
            if ((now - _emptySince.Value).TotalMilliseconds > 500)
            {
                Stop();
            }
            return;
        }

        _emptySince = null;

        // Update all layer animations / state
        foreach (var layer in activeLayers)
        {
            try { layer.Update(cappedDelta); }
            catch (Exception ex) { _logger?.LogDebug(ex, "Layer {Layer} update failed", layer.GetType().Name); }
        }

        // Tell Avalonia to re-render each window's CompositorControl.
        // Invalidating the control directly (not the window) is required for
        // ICustomDrawOperation to re-run on the render thread.
        foreach (var window in _windows)
        {
            try
            {
                window.GetControl().InvalidateVisual();
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "Compositor control invalidation failed"); }
        }

        // The excluded surface is invalidated for as long as it exists — even when its
        // layers just went inactive — so its last visible frame is cleared to transparent
        // before the idle teardown closes it.
        foreach (var window in _excludedWindows)
        {
            try
            {
                window.GetControl().InvalidateVisual();
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "Excluded compositor control invalidation failed"); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _timer.Tick -= OnFrameTick;

        lock (_layerLock)
        {
            foreach (var layer in _layers.Values) layer.OnDeactivated();
            _layers.Clear();
        }
    }
}
