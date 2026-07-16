using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Convenience base for compositor layers: activity flag with engine wake-up, no-op lifecycle
/// hooks. Services own the state a layer renders; the layer holds no business logic.
/// </summary>
public abstract class BaseLayer : IWpfLayer
{
    private readonly CompositorEngine _engine;
    private volatile bool _isActive;

    protected BaseLayer(CompositorEngine engine)
    {
        _engine = engine;
    }

    public abstract int ZIndex { get; }

    public bool IsActive => _isActive;

    public virtual bool ExcludeFromCapture => false;

    public virtual bool WorldSpacePx => false;

    public virtual bool ShouldRenderOnScreen(System.Drawing.Rectangle screenBoundsPx) => true;

    /// <summary>Shared gate for fullscreen FILL layers (pink/spiral): when the user has multi-monitor
    /// overlays off, paint only the primary monitor. Compared by origin so a mixed-DPI width/height
    /// rounding difference can't misidentify the screen. Errs toward showing the effect on any
    /// enumeration hiccup rather than blanking it.</summary>
    protected static bool PrimaryOnlyUnlessDualMonitor(System.Drawing.Rectangle screenBoundsPx)
    {
        try
        {
            if (App.Settings?.Current?.DualMonitorEnabled == true) return true;
            // Cached lookup: Screen.PrimaryScreen re-enumerates all monitors per call on .NET
            // Core, and this runs per fill layer per monitor per presented frame.
            foreach (var s in App.GetAllScreensCached())
            {
                if (s.Primary)
                    return screenBoundsPx.X == s.Bounds.X && screenBoundsPx.Y == s.Bounds.Y;
            }
            return true; // empty cache (display transition): err toward showing
        }
        catch { return true; }
    }

    /// <summary>
    /// Flip layer activity. Safe from any thread; wakes the parked engine so the first frame
    /// renders promptly. The engine invokes OnActivated/OnDeactivated on the UI thread.
    /// </summary>
    protected void SetActive(bool value)
    {
        if (_isActive == value) return;
        _isActive = value;
        _engine.Wake();
    }

    public virtual void OnActivated() { }

    public virtual void OnDeactivated() { }

    public virtual void Update(TimeSpan delta) { }

    /// <summary>Default true: repaint every active frame. Slow/static layers (spiral, pink tint)
    /// override this with real change-tracking to throttle the fullscreen surface re-raster (#550).</summary>
    public virtual bool Dirty => true;

    public virtual void ClearDirty() { }

    public abstract void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed);
}
