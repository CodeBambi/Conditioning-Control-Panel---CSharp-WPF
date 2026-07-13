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

    private readonly Dictionary<string, CompositorHostWindow> _windows = new();
    private readonly Dictionary<string, CompositorHostWindow> _excludedWindows = new();

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
            {
                if (!w.IsVisible) w.Show();
                w.InvalidateSurface();
            }
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
            if (layer.ExcludeFromCapture) anyExcludedActive = true;
            else anyMainActive = true;

            try { layer.Update(delta); }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "CompositorEngine: layer Update failed ({Layer})", layer.GetType().Name);
            }
        }

        var nowUtc = DateTime.UtcNow;
        if (anyMainActive) _lastAnyActiveUtc = nowUtc;
        if (anyExcludedActive) _lastExcludedActiveUtc = nowUtc;

        ShowSurfaceIfActive(_windows, anyMainActive, excluded: false);
        ShowSurfaceIfActive(_excludedWindows, anyExcludedActive, excluded: true);

        if (anyMainActive) foreach (var w in _windows.Values) w.InvalidateSurface();
        if (anyExcludedActive) foreach (var w in _excludedWindows.Values) w.InvalidateSurface();

        // Active -> inactive: paint ONE cleared frame. The hosts stay visible through
        // WindowHideGrace, and without this the last effect frame would linger on screen.
        if (_wasMainActive && !anyMainActive) foreach (var w in _windows.Values) w.InvalidateSurface();
        if (_wasExcludedActive && !anyExcludedActive) foreach (var w in _excludedWindows.Values) w.InvalidateSurface();
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

    private void ShowSurfaceIfActive(Dictionary<string, CompositorHostWindow> surface, bool active, bool excluded)
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

    private void EnsureWindows(Dictionary<string, CompositorHostWindow> surface, bool excluded)
    {
        System.Windows.Forms.Screen[] screens;
        try { screens = System.Windows.Forms.Screen.AllScreens; }
        catch { return; }
        if (screens.Length == 0) return; // can be empty during display transitions

        foreach (var screen in screens)
        {
            if (surface.ContainsKey(screen.DeviceName)) continue;
            var window = new CompositorHostWindow(screen, excluded);
            window.PaintSurface += OnPaintSurface;
            surface[screen.DeviceName] = window;
        }
    }

    private void OnPaintSurface(CompositorHostWindow window, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var boundsPx = new SKRectI(0, 0, e.Info.Width, e.Info.Height);
        double dpiScale = window.ActualWidth > 0 ? e.Info.Width / window.ActualWidth : 1.0;
        var elapsed = _clock.Elapsed;

        IWpfLayer[] layers;
        lock (_layerLock) layers = _layers.ToArray();

        foreach (var layer in layers) // already z-sorted; lower draws first
        {
            if (!layer.IsActive || layer.ExcludeFromCapture != window.IsExcludedSurface) continue;
            try { layer.Render(canvas, boundsPx, dpiScale, elapsed); }
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

    private static void RebuildSurface(Dictionary<string, CompositorHostWindow> surface)
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
