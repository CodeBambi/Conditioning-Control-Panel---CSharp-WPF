using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// The unified overlay host: renders all registered <see cref="IWpfLayer"/>s as z-ordered Skia
/// layers inside ONE shared click-through window per monitor, replacing the historical
/// one-window-per-effect model (whose concurrent fullscreen layered windows are the root cause
/// of the session-lag / mouse-stutter cluster). Architecture mirrors the Avalonia port's
/// CompositorEngine so effect code converges across the two heads.
///
/// Cost model: fully parked when no layer is active - no Rendering subscription, hosts hidden.
/// Layers wake the engine via <see cref="Wake"/> (BaseLayer.SetActive does this).
///
/// Capture affinity: two surfaces per monitor. The MAIN surface must never be capture-excluded
/// (subliminal/flash/spiral visibility in recordings is a product decision); layers with
/// ExcludeFromCapture=true (brain drain) render on a separate lazily-created excluded surface.
/// Windows are created once and hidden/re-shown, never churned (layered-window churn deadlocks
/// the WPF render thread).
/// </summary>
public class CompositorEngine : IDisposable
{
    private static readonly TimeSpan IdleGrace = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxDelta = TimeSpan.FromMilliseconds(100);

    // Hiding hosts right at IdleGrace meant every effect trigger after a quiet half-second
    // re-Show()ed a fullscreen layered window (DWM re-composition = the "first load" hitch the
    // owner reported). The TICK still parks at IdleGrace (idle engine costs zero per frame);
    // the WINDOWS stay visible - empty and click-through - through this longer grace.
    private static readonly TimeSpan WindowHideGrace = TimeSpan.FromSeconds(30);

    private readonly object _layerLock = new();
    private readonly List<IWpfLayer> _layers = new();
    private readonly Dictionary<IWpfLayer, bool> _lastActiveState = new();

    private readonly Dictionary<string, ICompositorHost> _windows = new();
    private readonly Dictionary<string, ICompositorHost> _excludedWindows = new();

    // #550 off-thread present path (CompositorOffThreadPresent): latched the first time hosts are
    // created so a mid-run settings toggle can't mix host types on one engine.
    private bool _offThreadMode;
    private bool _modeLatched;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastTickElapsed;
    private TimeSpan _lastRenderingTime = TimeSpan.MinValue;
    private DateTime _lastAnyActiveUtc = DateTime.MinValue;
    private DateTime _lastExcludedActiveUtc = DateTime.MinValue;
    private bool _renderingHooked;
    private bool _disposed;
    private bool _wasMainActive, _wasExcludedActive;
    private DispatcherTimer? _hideTimer;

    public CompositorEngine()
    {
        try
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "CompositorEngine: could not subscribe display-change events");
        }
    }

    /// <summary>Register a layer. Layers live for the app's lifetime; there is no unregister path yet.</summary>
    public void RegisterLayer(IWpfLayer layer)
    {
        lock (_layerLock)
        {
            if (_layers.Contains(layer)) return;
            _layers.Add(layer);
            _layers.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));
            _lastActiveState[layer] = false;
        }
        Wake();
    }

    /// <summary>
    /// Ensure the engine is ticking. Safe from any thread and cheap when already awake;
    /// called by layers whenever their activity or visible state changes.
    /// </summary>
    public void Wake()
    {
        if (_disposed) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;

        if (!dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(DispatcherPriority.Render, Wake);
            return;
        }

        _hideTimer?.Stop();
        if (!_renderingHooked)
        {
            _renderingHooked = true;
            _lastTickElapsed = _clock.Elapsed;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    /// <summary>True when this engine presents off the UI thread (the #550 fix path is active).</summary>
    public bool OffThreadActive => _offThreadMode;

    /// <summary>
    /// Pay the one-time host costs (window creation, hwnd + ex-styles, Skia surface alloc, paint
    /// JIT) at startup instead of on the first effect trigger. Creates and shows the main-surface
    /// hosts (empty, transparent, click-through - invisible to the user), paints one cleared
    /// frame, then lets the normal idle path park the tick and hide them after the grace.
    /// Call on the UI thread once the compositor is enabled; no-op otherwise.
    /// </summary>
    public void Prewarm()
    {
        if (_disposed) return;
        try
        {
            EnsureWindows(_windows, excluded: false);
            foreach (var w in _windows.Values)
                if (!w.IsVisible) w.Show();
            PresentSurface(_windows, excluded: false); // one (empty) frame realizes the surface + JIT
            Wake(); // ticks once, finds nothing active, parks + schedules the window hide
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "CompositorEngine: prewarm failed");
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_disposed) return;

        // CompositionTarget.Rendering can fire more than once per composed frame; dedupe on
        // the composition clock so Update() advances once per real frame.
        if (e is RenderingEventArgs args)
        {
            if (args.RenderingTime == _lastRenderingTime) return;
            _lastRenderingTime = args.RenderingTime;
        }

        var now = _clock.Elapsed;
        var delta = now - _lastTickElapsed;
        _lastTickElapsed = now;
        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero;
        if (delta > MaxDelta) delta = MaxDelta; // hitch: drop time instead of lurching

        bool anyMainActive = false, anyExcludedActive = false;
        // Dirty-gated invalidation (#550): a surface only re-rasters when one of its active layers
        // reports changed output this frame. The tick still runs at 60fps (cheap Update()s), but a
        // ~10fps spiral / static pink tint no longer forces a fullscreen software raster + layered
        // composite on the UI thread every frame. Layers default Dirty=true, so continuously-
        // animating ones (bubbles/chaos FX/flash/subliminal) keep repainting every frame.
        bool anyMainDirty = false, anyExcludedDirty = false;

        IWpfLayer[] layers;
        lock (_layerLock) layers = _layers.ToArray();

        foreach (var layer in layers)
        {
            bool active = layer.IsActive;
            if (_lastActiveState.TryGetValue(layer, out var was) && was != active)
            {
                _lastActiveState[layer] = active;
                try
                {
                    if (active) layer.OnActivated();
                    else layer.OnDeactivated();
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "CompositorEngine: layer lifecycle hook failed ({Layer})", layer.GetType().Name);
                }
            }

            if (!active) continue;
            bool excluded = layer.ExcludeFromCapture;
            if (excluded) anyExcludedActive = true;
            else anyMainActive = true;

            try { layer.Update(delta); }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "CompositorEngine: layer Update failed ({Layer})", layer.GetType().Name);
            }

            // Read Dirty AFTER Update so this frame's advance/opacity change counts.
            if (layer.Dirty) { if (excluded) anyExcludedDirty = true; else anyMainDirty = true; }
        }

        var nowUtc = DateTime.UtcNow;
        if (anyMainActive) _lastAnyActiveUtc = nowUtc;
        if (anyExcludedActive) _lastExcludedActiveUtc = nowUtc;

        ShowSurfaceIfActive(_windows, anyMainActive, excluded: false);
        ShowSurfaceIfActive(_excludedWindows, anyExcludedActive, excluded: true);

        if (anyMainActive && anyMainDirty) PresentSurface(_windows, excluded: false);
        if (anyExcludedActive && anyExcludedDirty) PresentSurface(_excludedWindows, excluded: true);

        // Reset dirty flags now this frame's invalidations are queued. Only active layers matter;
        // an inactive layer re-marks itself dirty when it next activates.
        foreach (var layer in layers)
        {
            if (!layer.IsActive) continue;
            try { layer.ClearDirty(); }
            catch (Exception ex) { App.Logger?.Error(ex, "CompositorEngine: ClearDirty failed ({Layer})", layer.GetType().Name); }
        }

        // Active -> inactive: paint ONE cleared frame. The hosts stay visible through
        // WindowHideGrace, and without this the last effect frame would linger on screen.
        if (_wasMainActive && !anyMainActive) PresentSurface(_windows, excluded: false);
        if (_wasExcludedActive && !anyExcludedActive) PresentSurface(_excludedWindows, excluded: true);
        _wasMainActive = anyMainActive;
        _wasExcludedActive = anyExcludedActive;

        // Fully park when everything has been idle past the grace window: unhook the tick so
        // an idle compositor costs literally zero per frame. The (visible, empty) hosts are
        // hidden later by the one-shot grace timer, not here.
        if (!anyMainActive && !anyExcludedActive
            && nowUtc - _lastAnyActiveUtc > IdleGrace
            && nowUtc - _lastExcludedActiveUtc > IdleGrace)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
            ScheduleWindowHide();
        }
    }

    private void ShowSurfaceIfActive(Dictionary<string, ICompositorHost> surface, bool active, bool excluded)
    {
        if (!active) return;
        EnsureWindows(surface, excluded);
        foreach (var w in surface.Values)
        {
            if (!w.IsVisible)
            {
                try { w.Show(); }
                catch (Exception ex) { App.Logger?.Error(ex, "CompositorEngine: host Show failed"); }
            }
        }
    }

    /// <summary>Arm the one-shot hide of all (idle, empty) host windows, WindowHideGrace from
    /// now. Cancelled by any Wake(); re-armed each time the engine parks. UI thread.</summary>
    private void ScheduleWindowHide()
    {
        _hideTimer ??= CreateHideTimer();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private DispatcherTimer CreateHideTimer()
    {
        var t = new DispatcherTimer { Interval = WindowHideGrace };
        t.Tick += (_, _) =>
        {
            t.Stop();
            if (_disposed || _renderingHooked) return; // re-woke during the grace: stay visible
            foreach (var w in _windows.Values.Concat(_excludedWindows.Values))
            {
                if (w.IsVisible)
                {
                    try { w.Hide(); }
                    catch (Exception ex) { App.Logger?.Error(ex, "CompositorEngine: host Hide failed"); }
                }
            }
        };
        return t;
    }

    private void EnsureWindows(Dictionary<string, ICompositorHost> surface, bool excluded)
    {
        System.Windows.Forms.Screen[] screens;
        try { screens = System.Windows.Forms.Screen.AllScreens; }
        catch { return; }
        if (screens.Length == 0) return; // can be empty during display transitions

        if (!_modeLatched)
        {
            _offThreadMode = App.CompositorOffThreadPresent;
            _modeLatched = true;
            App.Logger?.Information("CompositorEngine: present mode = {Mode}",
                _offThreadMode ? "OFF-THREAD (UpdateLayeredWindow)" : "UI-thread SKElement");
        }

        foreach (var screen in screens)
        {
            if (surface.ContainsKey(screen.DeviceName)) continue;
            if (_offThreadMode)
            {
                surface[screen.DeviceName] = new LayeredCompositorHost(screen, excluded);
            }
            else
            {
                var window = new CompositorHostWindow(screen, excluded);
                window.PaintSurface += OnPaintSurface;
                surface[screen.DeviceName] = window;
            }
        }
    }

    /// <summary>Present one surface's dirty frame in the active mode: WPF hosts invalidate (UI-thread
    /// raster via <see cref="OnPaintSurface"/>); off-thread hosts get a freshly recorded picture handed
    /// to their present thread. Also used for the single cleared frame on the active-&gt;inactive edge
    /// (records an empty picture) and for prewarm.</summary>
    private void PresentSurface(Dictionary<string, ICompositorHost> surface, bool excluded)
    {
        IWpfLayer[]? layers = null;
        foreach (var host in surface.Values)
        {
            if (host is LayeredCompositorHost lh)
            {
                layers ??= SnapshotLayers();
                lh.PresentPicture(RecordHost(host, layers, excluded));
            }
            else if (host is CompositorHostWindow ww)
            {
                ww.InvalidateSurface();
            }
        }
    }

    private IWpfLayer[] SnapshotLayers()
    {
        lock (_layerLock) return _layers.ToArray();
    }

    /// <summary>Record a monitor's active layers into an immutable picture, in device px, monitor-
    /// local. Cheap (draw-command capture only); runs on the UI thread where layer state is safe to
    /// read. The present thread rasterizes it later.</summary>
    private SKPicture RecordHost(ICompositorHost host, IWpfLayer[] layers, bool excluded)
    {
        var sb = host.ScreenBoundsPx;
        int w = Math.Max(1, sb.Width), h = Math.Max(1, sb.Height);
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0, 0, w, h));
        DrawLayers(canvas, layers, new SKRectI(0, 0, w, h), sb, host.DpiScale, excluded, _clock.Elapsed);
        return recorder.EndRecording();
    }

    private void OnPaintSurface(CompositorHostWindow window, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var boundsPx = new SKRectI(0, 0, e.Info.Width, e.Info.Height);
        double dpiScale = window.ActualWidth > 0 ? e.Info.Width / window.ActualWidth : 1.0;
        IWpfLayer[] layers = SnapshotLayers();
        DrawLayers(canvas, layers, boundsPx, window.ScreenBoundsPx, dpiScale,
                   window.IsExcludedSurface, _clock.Elapsed);
    }

    /// <summary>Draw the active layers for one surface onto <paramref name="canvas"/>. Shared by the
    /// WPF live-raster path (<see cref="OnPaintSurface"/>) and the off-thread record path
    /// (<see cref="RecordHost"/>) so layer transforms/culling are defined once.</summary>
    private static void DrawLayers(SKCanvas canvas, IWpfLayer[] layers, SKRectI boundsPx,
        System.Drawing.Rectangle screenBoundsPx, double dpiScale, bool excludedSurface, TimeSpan elapsed)
    {
        foreach (var layer in layers) // already z-sorted; lower draws first
        {
            if (!layer.IsActive || layer.ExcludeFromCapture != excludedSurface) continue;
            // Per-monitor opt-out (e.g. fullscreen fill layers honoring DualMonitorEnabled). The
            // host exists on every screen; a gated-out host just records an empty (transparent) frame.
            if (!layer.ShouldRenderOnScreen(screenBoundsPx)) continue;
            try
            {
                if (layer.WorldSpacePx)
                {
                    // World-space layers draw in virtual-desktop device px. The surface is
                    // normally exactly device-px sized; if WPF rasterized the SKElement at a
                    // stale scale during a mixed-DPI transition, the scale factor re-squares it.
                    var sb = screenBoundsPx;
                    if (sb.Width <= 0 || sb.Height <= 0) continue;
                    canvas.Save();
                    if (boundsPx.Width != sb.Width || boundsPx.Height != sb.Height)
                        canvas.Scale(boundsPx.Width / (float)sb.Width, boundsPx.Height / (float)sb.Height);
                    canvas.Translate(-sb.X, -sb.Y);
                    layer.Render(canvas, new SKRectI(sb.X, sb.Y, sb.X + sb.Width, sb.Y + sb.Height),
                                 dpiScale, elapsed);
                    canvas.Restore();
                }
                else
                {
                    layer.Render(canvas, boundsPx, dpiScale, elapsed);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "CompositorEngine: layer Render failed ({Layer})", layer.GetType().Name);
            }
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_disposed) return;
            try
            {
                RebuildSurface(_windows);
                RebuildSurface(_excludedWindows);
                Wake();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "CompositorEngine: display-change rebuild failed");
            }
        });
    }

    private static void RebuildSurface(Dictionary<string, ICompositorHost> surface)
    {
        System.Windows.Forms.Screen[] screens;
        try { screens = System.Windows.Forms.Screen.AllScreens; }
        catch { return; }
        if (screens.Length == 0) return;

        var byName = screens.ToDictionary(s => s.DeviceName);

        // Topology changes are the ONE sanctioned teardown point for host windows.
        foreach (var gone in surface.Keys.Where(k => !byName.ContainsKey(k)).ToList())
        {
            try { surface[gone].Close(); } catch { }
            surface.Remove(gone);
        }

        foreach (var (name, window) in surface)
        {
            window.UpdateScreenBounds(byName[name]);
        }
        // New monitors get hosts lazily on the next active tick (EnsureWindows).
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _hideTimer?.Stop(); } catch { }
        try { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; } catch { }
        if (_renderingHooked)
        {
            try { CompositionTarget.Rendering -= OnRendering; } catch { }
            _renderingHooked = false;
        }
        foreach (var w in _windows.Values.Concat(_excludedWindows.Values))
        {
            try { w.Close(); } catch { }
        }
        _windows.Clear();
        _excludedWindows.Clear();
    }
}
