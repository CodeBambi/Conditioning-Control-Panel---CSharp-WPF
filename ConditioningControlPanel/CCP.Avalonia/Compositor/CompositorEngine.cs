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
using ConditioningControlPanel.Core.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using PixelRect = ConditioningControlPanel.Core.Platform.PixelRect;

namespace ConditioningControlPanel.Avalonia.Compositor;

/// <summary>
/// The unified compositor engine. Renders all registered <see cref="ILayer"/> instances
/// directly into Avalonia's render thread via <see cref="CompositorDrawOp"/> at 60Hz.
/// No WriteableBitmap, no Image control, no manual invalidation — Avalonia handles presentation.
///
/// LIFECYCLE (WS0 lot 3): the engine never tears its windows down on idle. When no layer is
/// active it drops to a cheap ~4Hz watchdog tick that skips Update/InvalidateVisual entirely
/// and demotes the windows from Topmost — matching WPF's "overlay windows are created once and
/// hidden/shown, never churned mid-run" contract. The watchdog notices the first IsActive
/// layer and restores the 60Hz loop, so any effect activation (flash, subliminal, bubbles,
/// bouncing text, pink/spiral/brain-drain toggles) renders without its service having to
/// call <see cref="Start"/> — though calling Start() is still the recommended wake for
/// instant (same-tick) response and for the engine-never-started case.
///
/// COORDINATE CONTRACT (WS0 lot 3): layer geometry — item positions/sizes and the bounds
/// passed to <see cref="IAvaloniaLayer.Render(SKCanvas, PixelRect, ScreenInfo?, TimeSpan)"/> —
/// is in PHYSICAL virtual-desktop pixels (the space Win32, the WH_MOUSE_LL hook, and
/// ScreenInfo.Bounds all share). Each per-monitor window's render pass pre-transforms the
/// canvas by scale(1/screen.Scaling) · translate(-screen.Bounds.Origin), so physical
/// coordinates land on the window's DIP surface, positioned items appear only on the monitor
/// they occupy, and hook hit-tests compare physical-to-physical with no per-DPI conversion.
/// </summary>
public sealed class CompositorEngine : IDisposable
{
    private const double ActiveIntervalMs = 16;  // ~60 Hz render loop
    private const double IdleIntervalMs = 250;   // ~4 Hz watchdog while no layer is active

    private readonly ILogger<CompositorEngine>? _logger;
    private readonly IScreenProvider? _screenProvider;
    private readonly ISettingsService? _settings;
    private readonly CaptureMaskState? _captureMaskState;
    private readonly IMouseHook? _mouseHook;
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
    // PER-REGION CLICK-THROUGH (team review 2026-07-09): reusable builder for the per-frame
    // capture mask = union of every NON-AMBIENT active layer's painted region. Reset + repopulated
    // once per active tick on the UI thread; the resulting immutable CaptureMask is published to
    // CaptureMaskState and read lock-free by the low-level mouse hook.
    private readonly CaptureMaskBuilder _captureMaskBuilder = new();
    // Whether the engine currently holds a reference on the shared mouse hook for the mask. The
    // hook is reference-counted across consumers (bubble pop, flash, chaos ripple); the engine
    // takes its OWN share whenever the mask is non-empty so clicks over a capturing layer (e.g.
    // video-only or subliminal-only) are swallowed even when no other consumer installed it.
    private bool _hookInstalledForMask;
    // Immutable snapshot of the registered IAvaloniaLayers, rebuilt only on Register/Unregister.
    // Read lock-free from both the UI tick and the render thread (array reference swap is
    // atomic), replacing the old per-tick/per-render OfType().Where().ToArray() LINQ churn.
    private IAvaloniaLayer[] _layerSnapshot = Array.Empty<IAvaloniaLayer>();
    private DateTime? _excludedEmptySince;
    // Epochs cancel staggered window-creation timers that outlive their surface
    // (engine stopped, or the excluded surface torn down, before a 250ms timer fired).
    private int _mainEpoch;
    private int _excludedEpoch;
    // Epoch for which excluded-surface creation has already been scheduled; prevents the
    // tick from re-scheduling (and duplicating) a staggered batch when the first window
    // creation failed but later staggered windows are still pending.
    private int _excludedScheduledEpoch = -1;
    // Pending staggered-creation timers. Tracked so Stop()/Dispose() can cancel them —
    // an untracked timer surviving Stop() used to resurrect a single secondary-monitor
    // window and latch a corrupted window set (lot-3 stagger race). The epoch check in
    // the timer callback is the second, independent guard.
    private readonly List<DispatcherTimer> _staggerTimers = new();

    // Display topology / single-display-setting reconciliation.
    private bool _screensDirty;
    private DateTime _lastReconcileCheck = DateTime.MinValue;

    // Topmost watchdog (WPF OverlayService parity: 500ms conditional re-pin when
    // WS_EX_TOPMOST was stripped externally + 5s unconditional force-kick).
    private DateTime _lastTopmostProbe = DateTime.MinValue;
    private DateTime _lastTopmostForce = DateTime.MinValue;

    private DateTime _lastFrame = DateTime.MinValue;
    private bool _wasActive;
    private bool _disposed;

    public CompositorEngine(
        ILogger<CompositorEngine>? logger = null,
        IScreenProvider? screenProvider = null,
        ISettingsService? settings = null,
        ConditioningControlPanel.Core.Services.Compositor.CaptureMaskState? captureMaskState = null,
        IMouseHook? mouseHook = null)
    {
        _logger = logger;
        _screenProvider = screenProvider;
        _settings = settings;
        _captureMaskState = captureMaskState;
        _mouseHook = mouseHook;

        // IMP-2: run the engine tick at Render priority. The parameterless DispatcherTimer ctor
        // defaults to Background priority (verified against Avalonia 12.0.5 Avalonia.Base.xml:
        // "process the timer event at background priority"), which is starvable under UI-thread
        // load — a scheduling stutter that also feeds the row #2 MinFps=0 hypothesis. Render
        // priority ("same priority as render") schedules the tick with the framework's own render
        // pass so it is not deferred behind Background/Input work.
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(ActiveIntervalMs)
        };
        _timer.Tick += OnFrameTick;

        if (_screenProvider != null)
            _screenProvider.ScreensChanged += OnScreensChanged;
    }

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        // Raised on the UI thread (AvaloniaScreenProvider filters spurious events by layout
        // signature and has already notified DisplayChangeCoordinator). The actual rebuild
        // happens on the tick after the spawn-quiesce window clears.
        _screensDirty = true;
        _logger?.LogInformation("CompositorEngine: display topology changed; window reconcile scheduled");
    }

    /// <summary>
    /// Starts the compositor: creates one <see cref="CompositorWindow"/> per effect monitor
    /// (honoring <c>DualMonitorEnabled</c>) and begins the update loop. Idempotent and cheap
    /// when already running — services call this from layer-activation paths to guarantee
    /// the engine is awake (same-tick wake instead of the ~250ms idle-watchdog latency).
    /// </summary>
    public void Start()
    {
        if (_disposed) return;

        if (_windows.Count == 0 && _staggerTimers.Count == 0)
        {
            StaggeredCreateWindows(GetDesiredScreens(), excludeFromCapture: false);
        }
        else if (!_timer.IsEnabled)
        {
            _lastFrame = DateTime.UtcNow;
            _timer.Start();
        }

        // Wake to the active cadence immediately; the tick demotes back to the idle
        // watchdog on its own when nothing is active.
        SetTimerInterval(ActiveIntervalMs);
    }

    /// <summary>
    /// Effect screens for both surfaces: all monitors, or primary-only when the user has
    /// turned DualMonitorEnabled off (WPF parity: every effect surface confines to the
    /// primary monitor when the setting is off).
    /// </summary>
    private IReadOnlyList<ScreenInfo> GetDesiredScreens()
    {
        var dual = _settings?.Current?.DualMonitorEnabled != false;
        var screens = _screenProvider?.GetEffectScreens(dual) ?? Array.Empty<ScreenInfo>();
        if (screens.Count == 0)
        {
            screens = new[] { new ScreenInfo("fallback", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1080), 1.0) };
        }
        return screens;
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
                _staggerTimers.Remove(t);
                // The surface this window was scheduled for may have been torn down while
                // the timer was pending; creating it now would leak an orphaned window.
                var currentEpoch = excludeFromCapture ? _excludedEpoch : _mainEpoch;
                if (currentEpoch != epoch) return;
                CreateOneWindow(screen, excludeFromCapture);
                MaybeStartTimer();
            };
            _staggerTimers.Add(t);
            t.Start();
        }
    }

    private void CreateOneWindow(ScreenInfo screen, bool excludeFromCapture)
    {
        if (_disposed) return;
        var target = excludeFromCapture ? _excludedWindows : _windows;
        // Defensive: a reconcile or double Start racing a stagger tick must not duplicate
        // a monitor's window.
        foreach (var existing in target)
        {
            if (Equals(existing.Screen, screen)) return;
        }
        try
        {
            var window = new CompositorWindow(screen, this, excludeFromCapture);
            window.Show();
            // ApplyNativeTransparency is deferred to the window's Opened handler (next
            // dispatcher tick). Calling SetWindowLong/SetWindowSubclass synchronously right
            // after Show() races with Avalonia's native window initialization + render thread
            // startup, causing an intermittent native access violation (0xC0000005).
            target.Add(window);
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

    private void SetTimerInterval(double milliseconds)
    {
        var interval = TimeSpan.FromMilliseconds(milliseconds);
        if (_timer.Interval != interval)
            _timer.Interval = interval; // note: assignment restarts the countdown
    }

    /// <summary>
    /// Stops the update loop, cancels pending staggered creations, and closes all compositor
    /// windows (both surfaces). NOT called on idle — idle is handled in-place by the watchdog
    /// tick (windows stay alive, WPF churn contract). This is for explicit teardown
    /// (<see cref="Dispose"/> or a deliberate shutdown).
    /// </summary>
    public void Stop()
    {
        _wasActive = false;
        _timer.Stop();
        CancelStaggerTimers();
        _mainEpoch++; // cancel any pending staggered main-window creation (second guard)
        foreach (var window in _windows.ToList())
        {
            try { window.Close(); } catch { /* ignore */ }
        }
        _windows.Clear();
        CloseExcludedWindows();
        // Clear the capture mask and release the hook share the engine holds for it, so an
        // explicit stop restores full click-through (no swallowing) for the ambient regions /
        // bare desktop. The idle tick normally does this, but Stop() halts the tick.
        PublishCaptureMask(CaptureMask.Empty);
        _logger?.LogInformation("CompositorEngine stopped");
    }

    private void CancelStaggerTimers()
    {
        foreach (var t in _staggerTimers)
        {
            try { t.Stop(); } catch { /* ignore */ }
        }
        _staggerTimers.Clear();
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
            RebuildLayerSnapshot();
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
                RebuildLayerSnapshot();
                _logger?.LogDebug("Layer unregistered: {Layer}", layer.GetType().Name);
            }
        }
    }

    private void RebuildLayerSnapshot()
    {
        // Called under _layerLock. SortedList keeps z-order; snapshot preserves it.
        _layerSnapshot = _layers.Values.OfType<IAvaloniaLayer>().ToArray();
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
    public IReadOnlyList<IAvaloniaLayer> Layers => _layerSnapshot;

    /// <summary>
    /// Immediately rebuild and publish the capture mask from the current layer states, without
    /// waiting for the next engine tick. Call this from a layer-activation path (e.g. video
    /// PlayVideo) that needs the mask armed the instant a capturing layer becomes active, so
    /// there is no ~16ms tick window where clicks over the newly-active capturing layer could
    /// leak to the desktop. Safe to call any time; a no-op when no capturing layer is active
    /// (the mask just stays empty). The next tick rebuilds it again as normal.
    /// </summary>
    public void RebuildCaptureMaskNow()
    {
        if (_disposed) return;
        BuildAndPublishCaptureMask(_layerSnapshot);
    }

    /// <summary>Number of main-surface compositor windows (one per monitor).</summary>
    public int WindowCount => _windows.Count;

    /// <summary>Number of capture-excluded surface windows (0 unless an excluded layer is active).</summary>
    public int ExcludedWindowCount => _excludedWindows.Count;

    /// <summary>True when the update loop is running.</summary>
    public bool IsRunning => _timer.IsEnabled;

    /// <summary>
    /// Force every compositor window back to the front of the topmost band. WPF parity with
    /// OverlayService.NotifyTopWindowClosed: call after a topmost window (fullscreen video,
    /// lock card, OS dialog) closes so the overlays surface again without waiting for the
    /// 5s force cadence.
    /// </summary>
    public void ReassertTopmostNow()
    {
        foreach (var window in _windows) window.ReassertTopmost(force: true);
        foreach (var window in _excludedWindows) window.ReassertTopmost(force: true);
    }

    /// <summary>
    /// Render the active layers belonging to one surface into the given Skia canvas.
    /// Called from the render thread via <see cref="CompositorDrawOp"/>.
    /// Capture affinity split: the main surface renders only layers with
    /// <c>ExcludeFromCapture == false</c>; the excluded surface renders only layers with
    /// <c>ExcludeFromCapture == true</c>. <paramref name="screen"/> identifies the monitor
    /// whose window is being drawn; when present the canvas is pre-transformed so layers
    /// draw in PHYSICAL virtual-desktop pixels (see the class doc coordinate contract) and
    /// <paramref name="bounds"/> is replaced by the screen's physical bounds. When null
    /// (fallback/tests) layers draw in window-local units, untransformed.
    /// </summary>
    public void RenderToCanvas(SKCanvas canvas, PixelRect bounds, ScreenInfo? screen = null, bool excludedSurface = false)
    {
        // Clear to transparent so inactive pixels pass through to the desktop.
        // Avalonia's render thread may clear to a solid color before our ICustomDrawOperation
        // runs, so we explicitly clear to transparent here.
        canvas.Clear(SKColors.Transparent);

        var layers = _layerSnapshot;
        var save = canvas.Save();
        var layerBounds = bounds;
        if (screen != null)
        {
            // physical virtual-desktop px -> this window's DIP surface:
            // local = (phys - screenOrigin) / scaling.
            var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
            canvas.Scale((float)(1.0 / scale));
            canvas.Translate((float)-screen.Bounds.X, (float)-screen.Bounds.Y);
            layerBounds = screen.Bounds;
        }

        foreach (var layer in layers)
        {
            if (!layer.IsActive || layer.ExcludeFromCapture != excludedSurface) continue;
            try
            {
                canvas.Save();
                layer.Render(canvas, layerBounds, screen, TimeSpan.Zero);
                canvas.Restore();
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "Layer {Layer} render failed", layer.GetType().Name); }
        }

        canvas.RestoreToCount(save);
    }

    private void OnFrameTick(object? sender, EventArgs e)
    {
        if (_disposed) return;

        var now = DateTime.UtcNow;
        var delta = now - _lastFrame;
        _lastFrame = now;

        // Cap delta to avoid large time jumps after pauses (idle watchdog cadence,
        // debugger break, window drag).
        var cappedDelta = delta.TotalMilliseconds > 100
            ? TimeSpan.FromMilliseconds(100)
            : delta;

        var layers = _layerSnapshot;
        var activeCount = 0;
        var anyExcludedActive = false;
        foreach (var layer in layers)
        {
            if (!layer.IsActive) continue;
            activeCount++;
            if (layer.ExcludeFromCapture) anyExcludedActive = true;
        }

        // Display hotplug / resolution / DualMonitorEnabled reconcile (both surfaces).
        MaybeReconcileWindows(now);

        // Self-heal: content appeared while the engine has no windows (explicit Stop() or a
        // reconcile close raced an activation). Recreate through the staggered path.
        if (_windows.Count == 0)
        {
            if (activeCount > 0 && _staggerTimers.Count == 0
                && !ConditioningControlPanel.Core.Services.DisplayChangeCoordinator.SpawnsSuppressed)
            {
                StaggeredCreateWindows(GetDesiredScreens(), excludeFromCapture: false);
            }
            if (_windows.Count == 0) return;
        }

        // Keep compositor topmost whenever active layers are present.
        // WS_EX_LAYERED | WS_EX_TRANSPARENT handles click-through natively,
        // so we no longer need to lower the compositor for dialogs.
        var shouldBeTopmost = activeCount > 0;
        foreach (var window in _windows)
        {
            if (window.Topmost != shouldBeTopmost)
            {
                try { window.Topmost = shouldBeTopmost; }
                catch { }
            }
        }

        // Native topmost recovery (WPF OverlayService parity, :424-464): the Avalonia
        // Topmost bool is a steady-state no-op once true, so external topmost churn (OS
        // notifications, other always-on-top apps, fullscreen transitions stripping
        // WS_EX_TOPMOST) buried the compositor permanently. Probe every 500ms and re-pin
        // any window whose WS_EX_TOPMOST bit was stripped; unconditionally force-kick to
        // the front of the topmost band every 5s. Skipped during display transitions.
        if (shouldBeTopmost
            && !ConditioningControlPanel.Core.Services.DisplayChangeCoordinator.SpawnsSuppressed)
        {
            if ((now - _lastTopmostForce).TotalMilliseconds > 5000)
            {
                _lastTopmostForce = now;
                _lastTopmostProbe = now;
                foreach (var window in _windows) window.ReassertTopmost(force: true);
                foreach (var window in _excludedWindows) window.ReassertTopmost(force: true);
            }
            else if ((now - _lastTopmostProbe).TotalMilliseconds > 500)
            {
                _lastTopmostProbe = now;
                foreach (var window in _windows) window.ReassertTopmost(force: false);
                foreach (var window in _excludedWindows) window.ReassertTopmost(force: false);
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
                StaggeredCreateWindows(GetDesiredScreens(), excludeFromCapture: true);
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

        if (activeCount == 0)
        {
            // Idle: keep the windows (click-through, demoted from Topmost above, invisible —
            // the final invalidate below presents one cleared transparent frame) and drop to
            // the cheap watchdog cadence. The next tick that sees an active layer restores
            // 60Hz — this is the wake path that makes the old destructive auto-stop safe to
            // remove (flash/subliminal/bubble/bouncing-text activations render again).
            PublishCaptureMask(CaptureMask.Empty);
            if (_wasActive)
            {
                _wasActive = false;
                InvalidateWindows(_windows);
                InvalidateWindows(_excludedWindows);
            }
            SetTimerInterval(IdleIntervalMs);
            return;
        }

        if (!_wasActive)
        {
            _wasActive = true;
            _logger?.LogDebug("CompositorEngine: layer activity resumed ({Count} active)", activeCount);
        }
        SetTimerInterval(ActiveIntervalMs);

        // Update all layer animations / state, collecting the dirty flags.
        // A layer whose content did not change since the last presented frame returns false
        // from ConsumeDirty() (default: true), letting a fully static frame skip the
        // invalidate + full-screen re-render below (lot-3 static-frame overdraw fix).
        var anyDirty = false;
        foreach (var layer in layers)
        {
            if (!layer.IsActive) continue;
            try
            {
                layer.Update(cappedDelta);
                if (layer.ConsumeDirty()) anyDirty = true;
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "Layer {Layer} update failed", layer.GetType().Name); }
        }

        // PER-REGION CLICK-THROUGH: build the capture mask from every active NON-AMBIENT layer's
        // painted region (ambient layers — filter/spiral/blink — are skipped, their regions stay
        // click-through). Done after the Update loop so layer state (newly spawned/removed items,
        // moved bubbles) is current for this frame. Published to CaptureMaskState, which the
        // low-level mouse hook reads lock-free to decide swallow-vs-pass per click.
        BuildAndPublishCaptureMask(layers);

        // Tell Avalonia to re-render each window's CompositorControl.
        // Invalidating the control directly (not the window) is required for
        // ICustomDrawOperation to re-run on the render thread.
        if (anyDirty)
            InvalidateWindows(_windows);

        // The excluded surface is invalidated for as long as it exists — even when its
        // layers just went inactive — so its last visible frame is cleared to transparent
        // before the idle teardown closes it.
        InvalidateWindows(_excludedWindows);
    }

    private void InvalidateWindows(List<CompositorWindow> windows)
    {
        foreach (var window in windows)
        {
            try
            {
                window.GetControl().InvalidateVisual();
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "Compositor control invalidation failed"); }
        }
    }

    /// <summary>
    /// The current effect-screen list for capture-mask purposes: the screens of the live
    /// compositor windows (avoiding a fresh allocation each tick), or the desired list from
    /// the screen provider when the window set is empty/staggering. The mask uses these so a
    /// full-bounds layer captures every monitor it paints on.
    /// </summary>
    private IReadOnlyList<ConditioningControlPanel.Core.Platform.ScreenInfo> GetCurrentEffectScreens()
    {
        if (_windows.Count > 0)
        {
            // Reuse a scratch list (no per-tick allocation) filled from the window screens.
            _scratchScreens.Clear();
            foreach (var w in _windows) _scratchScreens.Add(w.Screen);
            return _scratchScreens;
        }
        return GetDesiredScreens();
    }

    // Scratch list reused across ticks for capture-mask screen enumeration (no per-tick alloc).
    private readonly List<ConditioningControlPanel.Core.Platform.ScreenInfo> _scratchScreens = new();

    /// <summary>
    /// Build the per-frame capture mask from every active NON-AMBIENT layer and publish it.
    /// Ambient layers (<see cref="ILayer.IsAmbientClickThrough"/> == true — filter/spiral) are
    /// explicitly skipped; their regions stay click-through. Layers implement
    /// <see cref="ILayer.CollectCaptureRegions"/> to add their painted rect(s). Never throws —
    /// a layer that fails to report its region simply contributes nothing that frame.
    /// </summary>
    private void BuildAndPublishCaptureMask(IAvaloniaLayer[] layers)
    {
        if (_captureMaskState == null) return; // no mask wiring (headless/tests without the hook)
        _captureMaskBuilder.Reset();
        var screens = GetCurrentEffectScreens();
        foreach (var layer in layers)
        {
            if (!layer.IsActive) continue;
            if (layer.IsAmbientClickThrough) continue; // filter/spiral/blink stay click-through
            try { layer.CollectCaptureRegions(_captureMaskBuilder, screens); }
            catch (Exception ex) { _logger?.LogDebug(ex, "Layer {Layer} capture-region collection failed", layer.GetType().Name); }
        }
        PublishCaptureMask(_captureMaskBuilder.Build());
    }

    /// <summary>
    /// Publish the given mask and manage the mouse-hook install lifecycle: take an OWN reference
    /// on the shared hook whenever the mask becomes non-empty (so clicks over a capturing layer
    /// are swallowed even when no other consumer — bubbles/flash — installed it), and release it
    /// when the mask goes empty so the ambient regions and bare desktop pass input again.
    /// </summary>
    private void PublishCaptureMask(CaptureMask mask)
    {
        if (_captureMaskState == null) return;
        var previous = _captureMaskState.Publish(mask);
        var hadRegions = previous.Count > 0;
        var hasRegions = mask.Count > 0;
        if (hasRegions && !_hookInstalledForMask)
        {
            _hookInstalledForMask = true;
            try { _mouseHook?.Install(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "CompositorEngine: failed to install mouse hook for capture mask"); }
        }
        else if (!hasRegions && _hookInstalledForMask)
        {
            _hookInstalledForMask = false;
            try { _mouseHook?.Uninstall(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "CompositorEngine: failed to release mouse hook for capture mask"); }
        }
        if (hadRegions != hasRegions)
            _logger?.LogDebug("CompositorEngine: capture mask {State} ({Regions} region(s))",
                hasRegions ? "armed" : "cleared", mask.Count);
    }

    /// <summary>
    /// Rebuilds the window sets when the desired screen list (topology, resolution, DPI, or
    /// the DualMonitorEnabled setting) no longer matches the live windows. Runs on a ~1s
    /// cadence (immediately when ScreensChanged fired), waits out the DisplayChangeCoordinator
    /// spawn-quiesce window, and never runs while a staggered batch is still pending.
    /// </summary>
    private void MaybeReconcileWindows(DateTime now)
    {
        if (_windows.Count == 0 && _excludedWindows.Count == 0) return; // next creation enumerates fresh
        if (_staggerTimers.Count > 0) return;
        if (!_screensDirty && (now - _lastReconcileCheck).TotalMilliseconds < 1000) return;
        _lastReconcileCheck = now;
        if (ConditioningControlPanel.Core.Services.DisplayChangeCoordinator.SpawnsSuppressed) return; // retry next tick

        var desired = GetDesiredScreens();
        if (WindowsMatch(desired))
        {
            _screensDirty = false;
            return;
        }
        _screensDirty = false;

        _logger?.LogInformation("CompositorEngine: reconciling windows to {Count} screen(s)", desired.Count);

        // Rebuild the main surface through the staggered path (v12 native-race rule).
        _mainEpoch++;
        foreach (var window in _windows.ToList())
        {
            try { window.Close(); } catch { /* ignore */ }
        }
        _windows.Clear();
        StaggeredCreateWindows(desired, excludeFromCapture: false);

        // The excluded surface is torn down here and lazily recreated by the next tick while
        // an excluded layer is active (CloseExcludedWindows bumps its epoch, which re-arms
        // the scheduled-epoch check).
        if (_excludedWindows.Count > 0)
            CloseExcludedWindows();
    }

    private bool WindowsMatch(IReadOnlyList<ScreenInfo> desired)
    {
        if (_windows.Count != desired.Count) return false;
        foreach (var screen in desired)
        {
            var found = false;
            foreach (var window in _windows)
            {
                if (Equals(window.Screen, screen)) { found = true; break; }
            }
            if (!found) return false;
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_screenProvider != null)
            _screenProvider.ScreensChanged -= OnScreensChanged;
        Stop();
        _timer.Tick -= OnFrameTick;

        lock (_layerLock)
        {
            foreach (var layer in _layers.Values) layer.OnDeactivated();
            _layers.Clear();
            RebuildLayerSnapshot();
        }
    }
}
