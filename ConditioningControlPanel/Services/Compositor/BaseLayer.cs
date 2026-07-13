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

    public abstract void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed);
}
