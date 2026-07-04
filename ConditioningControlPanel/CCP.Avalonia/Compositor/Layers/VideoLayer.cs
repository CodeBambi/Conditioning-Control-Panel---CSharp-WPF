using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Services.Video;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Models;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace ConditioningControlPanel.Avalonia.Compositor.Layers;

/// <summary>
/// Video layer for the compositor. Receives decoded frames from LibVLC via memory
/// callbacks and renders them into the shared Skia canvas at z=10.
/// Uses a fixed-size RV32 buffer (matching the proven multi-monitor video service)
/// so LibVLC always has a valid output surface.
/// </summary>
public class VideoLayer : BaseLayer, IDisposable
{
    private const uint DefaultVideoWidth = 1920;
    private const uint DefaultVideoHeight = 1080;

    private readonly LibVLC _libVlc;
    private readonly ILogger? _logger;
    private readonly object _bufferLock = new();
    private readonly DispatcherTimer _renderTimer;

    private VlcMediaPlayer? _player;
    private Media? _media;
    private IntPtr _frameBuffer = IntPtr.Zero;
    private uint _bufferSize;
    private volatile bool _frameReady;
    private volatile bool _bufferValid;
    private uint _videoWidth = DefaultVideoWidth;
    private uint _videoHeight = DefaultVideoHeight;
    private uint _videoPitch = DefaultVideoWidth * 4;
    private bool _disposed;

    private SKBitmap? _currentBitmap;
    private readonly object _frameLock = new();
    private bool _firstFrameLogged;
    private bool _loop;
    private bool _withAudio;
    private long _framesCopied;

    /// <summary>True while the wrapped LibVLC player reports active playback (Phase B:
    /// lets the service's <c>IsPlaying</c>/<c>HasOpenVideoWindows</c> reflect the UCE path).</summary>
    public bool IsPlaying => _player?.IsPlaying ?? false;

    /// <summary>Verification probe (--verify-video): current LibVLC player volume, null if no player.</summary>
    public int? PlayerVolume { get { try { return _player?.Volume; } catch { return null; } } }

    /// <summary>Current media length in milliseconds, or -1 while unknown / no player (Phase B2:
    /// duration source for the service's safety timer and attention scheduling).</summary>
    public long DurationMs { get { try { return _player?.Length ?? -1; } catch { return -1; } } }

    /// <summary>
    /// Seek the wrapped player to an absolute time in milliseconds (Phase B2: chaos
    /// random-segment jump). No-op when no player is active or the media is not seekable.
    /// Narrow by design — the service owns all segment/orchestration state; the layer never
    /// exposes its MediaPlayer.
    /// </summary>
    public void SeekTo(long ms)
    {
        try
        {
            var player = _player;
            if (player == null || !player.IsSeekable) return;
            player.Time = Math.Max(0, ms);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("VideoLayer: SeekTo failed: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Applies volume, mute, and preferred output device to the active player (Phase B audio
    /// parity). Service-driven at playback start; the layer stores no settings (services own
    /// state). For live volume-slider updates use <see cref="ApplyVolume"/> — it skips the
    /// output-device re-bind per the WPF contract (WS0 lot 4 V1-22).
    /// </summary>
    public void ApplyAudioSettings(AppSettings? settings)
    {
        var player = _player;
        if (player == null) return;
        player.ApplyAudioSettings(settings, _withAudio, _logger);
    }

    /// <summary>Applies volume/mute only — no output-device re-bind (live slider updates).</summary>
    public void ApplyVolume(AppSettings? settings)
    {
        var player = _player;
        if (player == null) return;
        player.ApplyVolume(settings, _withAudio);
    }

    /// <summary>Verification probe (--verify-video): a decoded frame is ready to draw.</summary>
    public bool HasRenderedFrame { get { lock (_frameLock) return _currentBitmap != null; } }

    /// <summary>Verification probe (--verify-video): frames copied from LibVLC since construction.
    /// Monotonic; sample twice to prove live playback (delta &gt; 0).</summary>
    public long FramesCopied => Interlocked.Read(ref _framesCopied);

    public override int ZIndex => CompositorLayers.Video;
    public override bool IsActive => _bufferValid && _frameBuffer != IntPtr.Zero;

    /// <summary>
    /// Opaque color used to fill the monitor around a letterboxed video so the desktop never
    /// shows through the bars. Defaults to black (conventional letterbox).
    /// <see cref="MandatoryVideoLayer"/> overrides this so a mandatory video fully occludes the
    /// screen even when the monitor's aspect ratio differs from the clip (e.g. a landscape clip
    /// on a portrait monitor) — a mandatory video must never expose the desktop.
    /// </summary>
    protected SKColor BackgroundColor { get; set; } = SKColors.Black;

    public event EventHandler? VideoStarted;
    public event EventHandler? VideoEnded;

    /// <summary>
    /// Raised (on the UI thread) when LibVLC reports the real media length, in milliseconds.
    /// Phase B2: lets the service upgrade its fallback safety timeout to the accurate duration
    /// and apply the chaos random-segment seek — the same LengthChanged contract the legacy
    /// VideoOverlayWindow uses. The layer stays dumb: it only forwards the value.
    /// </summary>
    public event EventHandler<long>? LengthKnown;

    /// <summary>LibVLC failed to open/decode the media — no Playing/EndReached will ever fire.
    /// The service routes this into the end pipeline (B2 review F1; WPF EncounteredError→OnEnded).
    /// Marshaled to the UI thread.</summary>
    public event EventHandler? EncounteredError;

    public VideoLayer(LibVLC libVlc, ILogger? logger = null)
    {
        _libVlc = libVlc;
        _logger = logger;
        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += OnRenderTick;
    }

    public void PlayVideo(string path, bool withAudio = true, bool loop = false)
    {
        if (_disposed) return;
        Stop();

        try
        {
            if (!File.Exists(path))
            {
                _logger?.LogWarning("VideoLayer: file not found {Path}", path);
                return;
            }

            _videoWidth = DefaultVideoWidth;
            _videoHeight = DefaultVideoHeight;
            _videoPitch = _videoWidth * 4;
            _loop = loop;
            _withAudio = withAudio;
            var size = _videoPitch * _videoHeight;

            lock (_bufferLock)
            {
                if (_frameBuffer != IntPtr.Zero) Marshal.FreeHGlobal(_frameBuffer);
                _frameBuffer = Marshal.AllocHGlobal((int)size);
                _bufferSize = size;
                _bufferValid = true;
            }

            _media = new Media(_libVlc, path, FromType.FromPath);
            if (loop) _media.AddOption(":input-repeat=65535");

            _player = new VlcMediaPlayer(_libVlc) { Mute = !withAudio };
            // Match the proven multi-monitor service: callbacks first, then format.
            _player.SetVideoCallbacks(LockCallback, null, DisplayCallback);
            _player.SetVideoFormat("RV32", _videoWidth, _videoHeight, _videoPitch);
            _player.EndReached += OnEndReached;
            _player.EncounteredError += OnEncounteredError;
            _player.Playing += OnPlaying;
            _player.LengthChanged += OnLengthChanged;

            _renderTimer.Start();
            _player.Play(_media);
            _logger?.LogInformation("VideoLayer: started {Path}", path);
            // Phase B2: VideoStarted now fires from the LibVLC Playing event (OnPlaying)
            // rather than here. Play() only queues the open — raising at the call site
            // reported "started" before playback existed (or even when the media failed to
            // open), and the service's orchestration (safety timer, attention scheduling)
            // must anchor to actual playback start, matching the legacy
            // VideoOverlayWindow.OnPlaying contract.
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "VideoLayer: failed to play {Path}", path);
            Cleanup();
        }
    }

    public void Stop()
    {
        _bufferValid = false;
        _frameReady = false;
        _firstFrameLogged = false;
        _loop = false;
        _renderTimer.Stop();

        var player = _player;
        _player = null;
        var media = _media;
        _media = null;
        if (player != null)
        {
            player.EndReached -= OnEndReached;
            player.EncounteredError -= OnEncounteredError;
            player.Playing -= OnPlaying;
            player.LengthChanged -= OnLengthChanged;
        }
        try { player?.Stop(); } catch { }

        IntPtr buf;
        lock (_bufferLock)
        {
            buf = _frameBuffer;
            _frameBuffer = IntPtr.Zero;
            _bufferSize = 0;
            _bufferValid = false;
        }

        Task.Run(async () =>
        {
            await Task.Delay(400);
            try { player?.Dispose(); } catch { }
            try { media?.Dispose(); } catch { }
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
        });

        lock (_frameLock)
        {
            _currentBitmap?.Dispose();
            _currentBitmap = null;
        }
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            VideoEnded?.Invoke(this, EventArgs.Empty);
            // Auto-close: a one-shot clip must release the screen when it finishes. Otherwise
            // the layer lingers on its final frame — IsActive stays true while the last decoded
            // bitmap is still set — and the desktop stays blocked. A looping clip is exempt: it
            // is driven by input-repeat and replayed by LibVLC rather than reaching a real end.
            // Stop() clears the frame and deactivates the layer, so the compositor drops it next
            // frame and (if nothing else is active) tears its windows down, freeing the desktop.
            if (!_loop)
                Stop();
        });
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        // Fires on LibVLC's thread — marshal so subscribers (service orchestration, timers)
        // always run on the UI thread (VideoOverlayWindow.OnPlaying parity). LibVLC re-fires
        // Playing after an unpause; the service guards double-run, not the layer.
        Dispatcher.UIThread.Post(() => VideoStarted?.Invoke(this, EventArgs.Empty));
    }

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        var lengthMs = e.Length; // already milliseconds
        Dispatcher.UIThread.Post(() => LengthKnown?.Invoke(this, lengthMs));
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        // Surfaces the most common silent failure: LibVLC could not open/decode the
        // media (bad codec, missing plugins, unreadable path). Without this the layer
        // just never produces frames and the video appears to "not play".
        _logger?.LogWarning("VideoLayer: LibVLC EncounteredError — media failed to open/decode");
        // B2 review F1: the service routes this into the same end pipeline WPF does
        // (EncounteredError → OnEnded, VideoService.cs:1498-1511) so a failed open can
        // never wedge the video slot. Marshaled like the other player events.
        Dispatcher.UIThread.Post(() => EncounteredError?.Invoke(this, EventArgs.Empty));
    }

    private IntPtr LockCallback(IntPtr opaque, IntPtr planes)
    {
        lock (_bufferLock)
        {
            if (!_bufferValid || _frameBuffer == IntPtr.Zero)
            {
                Marshal.WriteIntPtr(planes, IntPtr.Zero);
                return IntPtr.Zero;
            }
            Marshal.WriteIntPtr(planes, _frameBuffer);
            return IntPtr.Zero;
        }
    }

    private void DisplayCallback(IntPtr opaque, IntPtr picture) => _frameReady = true;

    private unsafe void OnRenderTick(object? sender, EventArgs e)
    {
        if (!_bufferValid || !_frameReady) return;
        _frameReady = false;

        bool got = false;
        SKBitmap? newBitmap = null;
        try
        {
            got = Monitor.TryEnter(_bufferLock, 16);
            if (!got) return;
            if (!_bufferValid || _frameBuffer == IntPtr.Zero) return;

            var info = new SKImageInfo((int)_videoWidth, (int)_videoHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
            newBitmap = new SKBitmap(info);
            var dstPtr = newBitmap.GetPixels();
            if (dstPtr == IntPtr.Zero)
            {
                newBitmap.Dispose();
                newBitmap = null;
                return;
            }

            var srcPtr = _frameBuffer.ToPointer();
            var srcRowBytes = (int)_videoPitch;
            var dstRowBytes = newBitmap.RowBytes;
            var height = (int)_videoHeight;
            var copyRowBytes = Math.Min(srcRowBytes, dstRowBytes);
            var totalBytes = copyRowBytes * height;

            if (srcRowBytes == dstRowBytes)
            {
                Buffer.MemoryCopy(srcPtr, dstPtr.ToPointer(), totalBytes, totalBytes);
            }
            else
            {
                for (var y = 0; y < height; y++)
                {
                    var srcRow = (byte*)srcPtr + (y * srcRowBytes);
                    var dstRow = (byte*)dstPtr.ToPointer() + (y * dstRowBytes);
                    Buffer.MemoryCopy(srcRow, dstRow, copyRowBytes, copyRowBytes);
                }
            }

            lock (_frameLock)
            {
                _currentBitmap?.Dispose();
                _currentBitmap = newBitmap;
            }
            Interlocked.Increment(ref _framesCopied);

            if (!_firstFrameLogged)
            {
                _firstFrameLogged = true;
                _logger?.LogInformation("VideoLayer: first frame copied {Width}x{Height} (Z={Z})", _videoWidth, _videoHeight, ZIndex);
            }
            _logger?.LogTrace("VideoLayer: frame copied {Width}x{Height}", _videoWidth, _videoHeight);
        }
        catch (Exception ex)
        {
            newBitmap?.Dispose();
            _logger?.LogDebug("VideoLayer: frame copy error: {Error}", ex.Message);
        }
        finally
        {
            if (got) Monitor.Exit(_bufferLock);
        }
    }

    public override void Update(TimeSpan deltaTime) { }

    public override void Render(SKCanvas canvas, PixelRect bounds, TimeSpan deltaTime)
    {
        lock (_frameLock)
        {
            if (_currentBitmap == null) return;

            // Fill the entire monitor with an opaque background before drawing the letterboxed
            // video, so the desktop never shows through the bars. Essential when the monitor's
            // aspect ratio differs from the clip (e.g. a landscape clip on a portrait screen).
            using (var bg = new SKPaint { Color = BackgroundColor })
                canvas.DrawRect(ToSkRect(bounds), bg);

            // Preserve aspect ratio (Uniform stretch) inside the monitor bounds.
            var frameW = (float)_videoWidth;
            var frameH = (float)_videoHeight;
            var boundsW = (float)bounds.Width;
            var boundsH = (float)bounds.Height;

            var scale = MathF.Min(boundsW / frameW, boundsH / frameH);
            var destW = frameW * scale;
            var destH = frameH * scale;
            var destX = (float)bounds.X + (boundsW - destW) / 2f;
            var destY = (float)bounds.Y + (boundsH - destH) / 2f;

            var dest = new SKRect(destX, destY, destX + destW, destY + destH);
            canvas.DrawBitmap(_currentBitmap, dest);
        }
    }

    private void Cleanup()
    {
        Stop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cleanup();
    }
}
