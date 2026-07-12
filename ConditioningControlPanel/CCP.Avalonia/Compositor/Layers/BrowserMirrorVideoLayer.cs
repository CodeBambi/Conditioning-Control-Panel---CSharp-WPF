using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Platform;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Mirrors whatever the embedded browser is playing fullscreen onto every OTHER monitor.
/// PRIVACY: the captured frames live in MEMORY ONLY — they are never written to disk, sent
/// over the network, or logged (the <see cref="IFrameSource"/> contract already enforces
/// this for its backends; this layer adds no persistence of its own). The bytes are copied
/// straight into a pinned native buffer that backs a long-lived zero-copy <see cref="SKImage"/>,
/// presented by the engine tick — no per-frame allocation on the render path.
///
/// SOURCE MONITOR IS SKIPPED (self-capture feedback avoidance): the engine re-asserts
/// <c>WS_EX_TOPMOST</c> on every compositor window every ~5s (<c>CompositorWindow.ReassertTopmost</c>),
/// so a compositor window can briefly sit above the browser fullscreen window on the source
/// monitor. If this layer painted its own previous frame back onto the source monitor, the
/// capture would then sample that frozen frame forever (the classic self-capture freeze).
/// Skipping the source monitor — the one whose bounds match <see cref="_source"/> — breaks the
/// loop: the source monitor keeps showing the live browser video underneath the transparent
/// compositor window, and the capture always samples the advancing video. This matches the
/// owner requirement verbatim ("copied to all OTHER screens") and is the same correctness rule
/// that makes the capture-excluded brain-drain surface work.
///
/// PORTRAIT HORIZONTAL STRETCH: on a portrait monitor the captured frame is drawn STRETCHED
/// to fill the monitor bounds (non-uniform Fill, not Uniform letterbox), so a landscape video
/// fills the portrait screen's width without zoom/crop — exactly "mirror it to fullscreen
/// horizontally, no need to zoom in". Landscape monitors are unaffected (the frame already
/// matches their aspect, so Fill == Uniform there).
///
/// COORDINATE CONTRACT: <c>bounds</c> is the monitor's PHYSICAL virtual-desktop rect (the
/// engine pre-transforms the canvas). Drawing the captured <see cref="SKImage"/> straight into
/// <c>ToSkRect(bounds)</c> fills that monitor. Z from <see cref="CompositorLayers"/> only.
/// Capture-VISIBLE (main surface): a mirrored browser video is session content the user wants
/// to see on every screen, so unlike brain-drain it must NOT be excluded from capture.
/// </summary>
public sealed class BrowserMirrorVideoLayer : BaseLayer, IDisposable
{
    // Two pinned native buffers + two long-lived SKImage wrappers, swapped front<->ready on the
    // UI tick (the same discipline VideoLayer uses, with two buffers instead of three because the
    // GDI capture cadence (~20-30fps) is far below the 60Hz render rate, so a buffer displaced from
    // FRONT has long stopped being sampled before the next copy targets it). Capture thread writes
    // READY; render thread samples FRONT; they are never the same index.
    private const int BufferCount = 2;

    private readonly IFrameSource _frameSource;
    private readonly ILogger? _logger;
    private readonly object _bufferLock = new();

    private IntPtr[]? _buffers;
    private SKImage?[]? _images;
    private int _frontIdx;
    private int _readyIdx = 1;
    private bool _readyFresh;
    private int _frameWidth;
    private int _frameHeight;
    private int _frameStride;
    private bool _hasFrontFrame;

    // Capture-loop state. _source is the monitor the browser fullscreen window sits on; the
    // layer skips painting on the compositor window whose screen equals it.
    private ScreenInfo? _source;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private volatile bool _active;
    private bool _dirty;
    private bool _disposed;
    private long _framesCaptured;

    public override int ZIndex => CompositorLayers.BrowserMirrorVideo;

    // "|| _dirty" keeps the layer engine-visible for the one tick needed to consume a post-Stop
    // dirty (the engine skips inactive layers): that final invalidate clears the last mirrored
    // frame even when a co-active static layer keeps activeCount > 0 (IMP-1 parity with VideoLayer).
    public override bool IsActive => _active || _dirty;

    /// <summary>
    /// CAPTURE-REGION: the mirror paints a stretched-to-fill frame on every monitor EXCEPT the
    /// source (the source monitor shows the live browser video underneath the transparent
    /// compositor window, so its clicks already pass natively to the browser fullscreen window).
    /// Capturing the non-source monitors matches the per-region rule (the mirror is session
    /// content the user interacts with, not ambient tinted glass) while preserving the
    /// self-capture-feedback avoidance that skipping the source monitor guarantees.
    /// </summary>
    public override void CollectCaptureRegions(
        ConditioningControlPanel.Core.Services.Compositor.CaptureMaskBuilder builder,
        System.Collections.Generic.IReadOnlyList<ScreenInfo> screens)
    {
        var source = _source;
        for (int i = 0; i < screens.Count; i++)
        {
            var s = screens[i];
            if (source != null && s.Bounds == source.Bounds) continue; // skip the browser-fullscreen source
            builder.Add(s.Bounds);
        }
    }

    public BrowserMirrorVideoLayer(IFrameSource frameSource, ILogger? logger = null)
    {
        _frameSource = frameSource;
        _logger = logger;
    }

    /// <summary>Frames captured since Start (monotonic probe; sample twice to prove a live loop).</summary>
    public long FramesCaptured => Interlocked.Read(ref _framesCaptured);

    /// <summary>The monitor this layer skips (the browser-fullscreen source).</summary>
    public ScreenInfo? Source => _source;

    /// <summary>
    /// Begins mirroring. Captures <paramref name="source"/> (the monitor the browser is fullscreen
    /// on) on a background thread and paints a stretched copy on every other compositor window.
    /// Idempotent: calling Start while already active just refreshes the source monitor.
    /// </summary>
    public void Start(ScreenInfo source)
    {
        if (_disposed) return;
        _source = source;
        if (_active)
        {
            _dirty = true;
            return;
        }
        _active = true;
        _dirty = true;
        _framesCaptured = 0;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _captureTask = Task.Run(() => CaptureLoopAsync(token));
        _logger?.LogInformation("BrowserMirrorVideoLayer: started, source={Source} ({W}x{H})", source.Name, (int)source.Bounds.Width, (int)source.Bounds.Height);
    }

    /// <summary>Stops the capture loop and clears the mirrored frame (the layer goes inactive).</summary>
    public void Stop()
    {
        if (!_active && _captureTask == null) return;
        _active = false;
        _dirty = true; // the presented frame goes away — its region must repaint (clear) once (IMP-1)
        try { _cts?.Cancel(); } catch { /* ignore */ }
        // Do not await the capture task here (this is called on the UI thread); let it drain in the
        // background. The buffers/images are freed later by EnsureTeardownBuffers/Dispose.
        _hasFrontFrame = false;
        _readyFresh = false;
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _active)
        {
            var source = _source;
            if (source == null || source.Bounds.IsEmpty)
            {
                try { await Task.Delay(100, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                continue;
            }

            RawFrame? frame = null;
            try
            {
                frame = await _frameSource.CaptureAsync(source, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // PRIVACY: never log frame contents — only the failure message.
                _logger?.LogDebug(ex, "BrowserMirrorVideoLayer: capture failed");
            }

            if (frame != null && frame.Width > 0 && frame.Height > 0 && frame.BgraData.Length >= frame.Width * frame.Height * 4)
            {
                HandoffFrame(frame);
            }

            try { await Task.Delay(33, ct).ConfigureAwait(false); } // ~30fps capture ceiling
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Copies a captured frame into the READY buffer (allocating / resizing the buffer pair on a
    /// dimension change), then marks it fresh for the UI-tick swap. Runs on the capture thread.
    /// </summary>
    private void HandoffFrame(RawFrame frame)
    {
        lock (_bufferLock)
        {
            if (!_active) return;
            var width = frame.Width;
            var height = frame.Height;
            var stride = width * 4; // Format32bppArgb tight stride (width*4 is a multiple of 4)
            // (Re)allocate the pinned buffers + SKImage wrappers when this is the first frame or the
            // source dimensions changed (display-mode / monitor swap mid-session). The previous
            // buffers/images are freed here — the capture thread is the only writer, and the render
            // thread samples FRONT which is about to be swapped to the freshly written READY, so a
            // resize mid-flight at worst drops one frame.
            if (_buffers == null || _frameWidth != width || _frameHeight != height)
            {
                AllocateBuffers(width, height, stride);
                _frameWidth = width;
                _frameHeight = height;
                _frameStride = stride;
            }
            // Copy into the READY buffer (the one the render thread never samples). BGRA bytes land
            // straight into the pinned memory the READY SKImage wraps, zero extra allocation.
            var ready = _buffers![_readyIdx];
            var bytes = frame.BgraData;
            var byteCount = Math.Min(bytes.Length, stride * height);
            Marshal.Copy(bytes, 0, ready, byteCount);
            _readyFresh = true;
        }
        Interlocked.Increment(ref _framesCaptured);
    }

    private void AllocateBuffers(int width, int height, int stride)
    {
        // Called under _bufferLock. Frees any prior buffers/images first.
        FreeBuffers();
        var size = stride * height;
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        _buffers = new IntPtr[BufferCount];
        _images = new SKImage?[BufferCount];
        for (var i = 0; i < BufferCount; i++)
        {
            _buffers[i] = Marshal.AllocHGlobal(size);
            using var pixmap = new SKPixmap(info, _buffers[i], stride);
            _images[i] = SKImage.FromPixels(pixmap);
            if (_images[i] == null)
                throw new InvalidOperationException("SKImage.FromPixels returned null for the browser-mirror buffer wrap");
        }
        _frontIdx = 0;
        _readyIdx = 1;
        _readyFresh = false;
        _hasFrontFrame = false;
    }

    private void FreeBuffers()
    {
        // Frees the pinned buffers + their SKImage wrappers. Safe to call when nothing samples them
        // (Dispose path) or under _bufferLock during a resize (the capture thread is the only writer
        // and AllocateBuffers is called from HandoffFrame on that same thread).
        if (_images != null)
        {
            foreach (var image in _images)
            {
                try { image?.Dispose(); } catch { /* ignore */ }
            }
            _images = null;
        }
        if (_buffers != null)
        {
            foreach (var buffer in _buffers)
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            }
            _buffers = null;
        }
    }

    public override void Update(TimeSpan deltaTime)
    {
        // Presentation tick: if the capture thread produced a fresh READY frame, swap FRONT<->READY
        // so the render path samples the newest frame. No pixel copy — both buffers are already
        // populated; this is an index swap.
        var presented = false;
        lock (_bufferLock)
        {
            if (_active && _readyFresh && _images != null)
            {
                (_frontIdx, _readyIdx) = (_readyIdx, _frontIdx);
                _readyFresh = false;
                _hasFrontFrame = true;
                presented = true;
            }
        }
        if (presented) _dirty = true;
    }

    public override bool ConsumeDirty()
    {
        var dirty = _dirty;
        _dirty = false;
        return dirty;
    }

    /// <summary>
    /// Screen-aware render: paints the captured frame STRETCHED to fill this monitor's bounds,
    /// skipping the source monitor (self-capture feedback avoidance — see class doc).
    /// </summary>
    public void Render(SKCanvas canvas, PixelRect bounds, ScreenInfo? screen, TimeSpan deltaTime)
    {
        SKImage? front;
        int width, height;
        lock (_bufferLock)
        {
            if (!_hasFrontFrame || _images == null) return;
            front = _images[_frontIdx];
            width = _frameWidth;
            height = _frameHeight;
        }
        if (front == null || width <= 0 || height <= 0) return;

        // Skip the source monitor: painting the captured frame back onto the very screen it was
        // captured from would freeze the mirror once the compositor window sits above the browser
        // (self-capture feedback). The source keeps showing the live browser video underneath the
        // transparent compositor window; every OTHER monitor gets the stretched copy. ScreenInfo is
        // a record, so value equality on Bounds/Name identifies the same monitor across the engine's
        // per-monitor windows.
        if (screen != null && _source != null && Equals(screen, _source)) return;

        // Opaque black fill first so no desktop bleeds through (Fill stretch leaves no letterbox, but
        // this is a harmless safety net for a 0-size edge).
        using (var bg = new SKPaint { Color = SKColors.Black })
            canvas.DrawRect(ToSkRect(bounds), bg);

        // STRETCH to fill (Fill, not Uniform): the owner wants a landscape browser video to fill a
        // portrait monitor's width without zoom/crop, so non-uniform scaling is intentional. On a
        // landscape monitor whose aspect matches the capture this is visually identical to Uniform.
        canvas.DrawImage(front, ToSkRect(bounds));
    }

    public override void Render(SKCanvas canvas, PixelRect bounds, TimeSpan deltaTime)
        => Render(canvas, bounds, null, deltaTime);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _active = false;
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _captureTask?.Wait(500); } catch { /* capture task draining — non-fatal */ }
        lock (_bufferLock)
        {
            FreeBuffers();
        }
        _cts?.Dispose();
        _cts = null;
        _captureTask = null;
    }
}
