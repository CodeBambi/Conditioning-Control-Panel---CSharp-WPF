using System.Threading;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Compositor twin of the spiral GIF windows: frames decoded ONCE (via AnimatedWebp's
/// SKCodec path, behind its global 2-permit decode gate) into persistent SKImages, stepped on
/// the engine tick, drawn UniformToFill. OverlayService owns all opacity math (including the
/// x0.1 spiral reduction and ramps) and pushes final values here. Video spirals stay on the
/// legacy MediaElement windows - this layer only handles the GIF/animated path.
/// </summary>
public class SpiralLayer : BaseLayer
{
    private SKImage[] _frames = Array.Empty<SKImage>();
    private TimeSpan _frameDelay = TimeSpan.FromMilliseconds(100);
    private TimeSpan _accum;
    private int _frameIndex;
    private double _opacity;
    private int _generation;               // orphans stale async decodes on Show/Hide races
    private volatile bool _loading;
    private readonly SKPaint _paint = new() { FilterQuality = SKFilterQuality.Medium };

    public SpiralLayer(CompositorEngine engine) : base(engine) { }

    public override int ZIndex => CompositorLayers.Spiral;

    /// <summary>True while visible OR still decoding - the 500ms settings sync polls this,
    /// so it must cover the async decode window or Show() gets re-fired mid-decode.</summary>
    public bool IsShowing => IsActive || _loading;

    /// <summary>
    /// Show the spiral from ALREADY-DECODED (frozen) WPF frames - the caller uses the legacy
    /// loader + cache (OverlayService.LoadSpiralGifFrames), which by construction supports every
    /// asset source the legacy windows supported (pack:// resources, file:// mod overrides, user
    /// paths). Conversion to persistent SKImages runs off-thread. Opacity is the FINAL value
    /// (caller applies the x0.1 spiral reduction). UI thread.
    /// </summary>
    public void ShowFrames(IReadOnlyList<System.Windows.Media.Imaging.BitmapSource> sourceFrames,
        TimeSpan frameDelay, double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.0, 1.0);
        int gen = Interlocked.Increment(ref _generation);
        _loading = true;

        // Snapshot the list; the caller's collection is cleared on StopSpiral. Frames are frozen
        // BitmapSources, safe to read from the worker.
        var snapshot = sourceFrames.ToArray();
        var delay = frameDelay;

        Task.Run(() =>
        {
            SKImage[]? frames = null;
            try
            {
                frames = snapshot.Select(SkiaWpfInterop.ToSKImage).ToArray();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("SpiralLayer: frame conversion failed: {E}", ex.Message);
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                if (frames != null) foreach (var f in frames) f.Dispose();
                _loading = false;
                return;
            }

            dispatcher.BeginInvoke(() =>
            {
                if (gen != _generation)
                {
                    // superseded by a newer Show/Hide while converting
                    if (frames != null) foreach (var f in frames) f.Dispose();
                    return;
                }
                _loading = false;
                if (frames == null || frames.Length == 0)
                {
                    App.Logger?.Warning("SpiralLayer: no frames converted; spiral not shown");
                    return;
                }
                DisposeFrames();
                _frames = frames;
                _frameDelay = delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(50);
                _frameIndex = 0;
                _accum = TimeSpan.Zero;
                SetActive(true);
            });
        });
    }

    /// <summary>Push a new FINAL opacity (ramp/pulse/settings already folded in). UI thread.</summary>
    public void SetOpacity(double opacity) => _opacity = Math.Clamp(opacity, 0.0, 1.0);

    public void Hide()
    {
        Interlocked.Increment(ref _generation); // orphan any in-flight decode
        _loading = false;
        SetActive(false);
    }

    public override void OnDeactivated() => DisposeFrames();

    public override void Update(TimeSpan delta)
    {
        if (_frames.Length < 2) return;
        _accum += delta;
        while (_accum >= _frameDelay)
        {
            _accum -= _frameDelay;
            _frameIndex = (_frameIndex + 1) % _frames.Length;
        }
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        if (_frames.Length == 0 || _opacity <= 0) return;
        var img = _frames[Math.Min(_frameIndex, _frames.Length - 1)];

        // UniformToFill: cover the monitor, preserve aspect, center-crop overflow.
        float scale = Math.Max((float)boundsPx.Width / img.Width, (float)boundsPx.Height / img.Height);
        float w = img.Width * scale, h = img.Height * scale;
        float x = boundsPx.Left + (boundsPx.Width - w) / 2f;
        float y = boundsPx.Top + (boundsPx.Height - h) / 2f;

        _paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(_opacity * 255, 0, 255));
        canvas.DrawImage(img, SKRect.Create(x, y, w, h), _paint);
    }

    private void DisposeFrames()
    {
        var old = _frames;
        _frames = Array.Empty<SKImage>();
        _frameIndex = 0;
        foreach (var f in old) f.Dispose();
    }
}
