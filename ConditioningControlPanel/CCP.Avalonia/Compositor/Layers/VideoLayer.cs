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
/// Uses fixed-size RV32 buffers (matching the proven multi-monitor video service)
/// so LibVLC always has a valid output surface.
///
/// FRAME PATH (plan Phase D): triple-buffered and allocation-free per frame. LibVLC
/// decodes straight into pinned native buffers that long-lived <see cref="SKImage"/>
/// wrappers present zero-copy; no <c>SKBitmap</c>, no <c>FromPixelCopy</c>, no
/// <c>DrawBitmap</c>, and no per-frame pixel copy (the old copy tick moved ~8&#160;MB
/// per frame, measured ~480&#160;MB/s of allocations). The presentation tick lives in
/// <see cref="Update"/>, driven by the engine's single 16&#160;ms loop (Phase D.2 —
/// the layer's private DispatcherTimer is gone).
/// </summary>
public class VideoLayer : BaseLayer, IDisposable
{
    private const uint DefaultVideoWidth = 1920;
    private const uint DefaultVideoHeight = 1080;

    // ------------------------------------------------------------------------------
    // TRIPLE-BUFFER PROTOCOL (who owns which buffer when; how the races are excluded)
    //
    // Three pinned native buffers rotate through three roles, tracked as indices into
    // _buffers/_images. Every role mutation happens under _bufferLock; the critical
    // sections are index swaps and pointer reads (nanoseconds), so LibVLC threads, the
    // UI tick, and the render thread never stall each other.
    //
    //   DECODE (_decodeIdx): owned by LibVLC. LockCallback ONLY ever hands out
    //           _buffers[_decodeIdx]; this is the sole entry point through which LibVLC
    //           gets a pointer, so LibVLC can never write FRONT by construction.
    //   READY  (_readyIdx):  the newest COMPLETED frame awaiting presentation (when
    //           _readyFresh), or a spare/cooldown slot (when !_readyFresh).
    //   FRONT  (_frontIdx):  the presented frame. Read-only; Render() draws its
    //           long-lived SKImage wrapper. Nobody writes it.
    //
    // Transitions (each one an index swap under _bufferLock):
    //   LockCallback    -> _locksOutstanding++; return DECODE pointer.
    //   DisplayCallback -> _locksOutstanding--; when it reaches 0 (frame complete, no
    //                      other picture in flight): swap DECODE<->READY, _readyFresh=true.
    //                      LibVLC vmem may lock the next picture before displaying the
    //                      previous one; the outstanding-lock count guarantees a buffer
    //                      is never rotated into READY while any decode write can still
    //                      target it. If READY held an unpresented frame it becomes the
    //                      next decode target (latest-wins; that frame is dropped, never
    //                      torn).
    //   Update (UI tick)-> if _readyFresh: swap FRONT<->READY, _readyFresh=false,
    //                      FramesCopied++ (frames PRESENTED). No pixel copy — LibVLC
    //                      already wrote the pixels.
    //
    // Race exclusion:
    //   - Decoder vs canvas: {FRONT, READY, DECODE} is always a permutation of {0,1,2}
    //     and LibVLC only receives DECODE, so the buffer Skia samples (FRONT) is never
    //     the buffer LibVLC writes. A buffer leaving FRONT parks in READY (cooldown) and
    //     only becomes DECODE after the *next* DisplayCallback rotation — by which time
    //     the render pass that last sampled it has long flushed. This extra hop is why
    //     three buffers instead of two: with two, the buffer displaced from FRONT would
    //     be LibVLC's very next lock target while a just-recorded draw could still be
    //     flushing, and a lock arriving before the UI tick would starve presentation.
    //   - Lock-before-swap: a LockCallback that lands before the tick swapped simply
    //     keeps writing DECODE; FRONT and READY are untouched, so no tear — at worst a
    //     dropped frame.
    //   - Teardown: Stop() invalidates state under the lock, then defers player/media
    //     dispose, SKImage dispose, and FreeHGlobal by 400ms — the canvas may be
    //     mid-draw on the last engine tick and LibVLC may briefly touch a buffer while
    //     the player winds down (same deferred-free discipline the single-buffer code
    //     used). Render grabs the FRONT wrapper reference under the lock, so it can
    //     never observe a disposed image: images are only disposed 400ms after
    //     _hasFrontFrame went false.
    //   - Stale sessions: Stop() zeroes _locksOutstanding; a late DisplayCallback from
    //     a dying player sees the count at 0 and never rotates the next session's
    //     buffers.
    //
    // SKIA THREAD RULE: the SKImage wrappers are created once per PlayVideo (zero-copy
    // SKImage.FromPixels over each pinned buffer) and only ever drawn on the render
    // path; the buffers are only written by LibVLC threads between lock and display.
    // ------------------------------------------------------------------------------
    private const int BufferCount = 3;

    private readonly LibVLC _libVlc;
    private readonly ILogger? _logger;
    private readonly object _bufferLock = new();

    private VlcMediaPlayer? _player;
    private Media? _media;
    private IntPtr[]? _buffers;
    private SKImage?[]? _images;
    private int _frontIdx;
    private int _readyIdx = 1;
    private int _decodeIdx = 2;
    private bool _readyFresh;
    private bool _hasFrontFrame;
    private int _locksOutstanding;
    private volatile bool _bufferValid;
    private uint _videoWidth = DefaultVideoWidth;
    private uint _videoHeight = DefaultVideoHeight;
    private uint _videoPitch = DefaultVideoWidth * 4;
    private bool _disposed;
    // Holds the most recent deferred teardown scheduled by Stop() so a synchronous app shutdown
    // (AvaloniaVideoService.Dispose -> WaitForTeardown) can drain it before the process exits.
    // Without the drain the ~400 ms deferred native player.Dispose()/FreeHGlobal races runtime
    // teardown and segfaults on exit. UI-thread-only (Stop is UI-thread-only), so no volatile.
    private Task? _teardownTask;

    // Dirty gate state (IMP-1): UI-tick-only — set by Update() when it presents a frame
    // (FRONT<->READY swap) and by Stop() so the torn-down frame's region repaints (clears)
    // once even when a co-active static layer keeps the engine out of its idle path.
    // Consumed once per tick by ConsumeDirty(). Plain bool per the PinkTintLayer idiom:
    // writers (Update/Stop, UI thread) and the consumer share the engine's UI tick.
    private bool _dirty;
    private bool _firstFrameLogged;
    private bool _loop;
    private bool _withAudio;
    private long _framesPresented;
    // TimeChanged relay throttle (B2 residual). LibVLC fires TimeChanged ~4x/s per player;
    // the service's watch-position watermark and the Deeper time-rule feed only need ~1 Hz
    // granularity, so relays are coalesced to one per ~1000 ms. The exact position at
    // teardown is recovered by the service via CurrentTimeMs (FinalizeWatchCredit live read),
    // so the throttle never costs more than ~1 s of resolution mid-playback. Reset in
    // PlayVideo/Stop. Read on LibVLC's thread; Environment.TickCount64 is thread-safe.
    private long _lastTimeRelayTickMs;

    /// <summary>True while the wrapped LibVLC player reports active playback (Phase B:
    /// lets the service's <c>IsPlaying</c>/<c>HasOpenVideoWindows</c> reflect the UCE path).</summary>
    public bool IsPlaying => _player?.IsPlaying ?? false;

    /// <summary>Verification probe (--verify-video): current LibVLC player volume, null if no player.</summary>
    public int? PlayerVolume { get { try { return _player?.Volume; } catch { return null; } } }

    /// <summary>Current media length in milliseconds, or -1 while unknown / no player (Phase B2:
    /// duration source for the service's safety timer and attention scheduling).</summary>
    public long DurationMs { get { try { return _player?.Length ?? -1; } catch { return -1; } } }

    /// <summary>Current playback time in milliseconds, or -1 when no player is active (B2
    /// residual: live position source for the service's watch-credit watermark at teardown —
    /// recovers the exact position the ~1 Hz <see cref="PlaybackTimeChanged"/> relay may not
    /// have delivered yet). Mirrors <c>MediaPlayer.Time</c>; narrow by design.</summary>
    public long CurrentTimeMs { get { try { return _player?.Time ?? -1; } catch { return -1; } } }

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

    /// <summary>Verification probe (--verify-video): the front buffer holds a presented frame.</summary>
    public bool HasRenderedFrame { get { lock (_bufferLock) return _hasFrontFrame; } }

    /// <summary>Verification probe (--verify-video): frames PRESENTED (front/ready swaps) since
    /// construction. Monotonic; sample twice to prove live playback (delta &gt; 0). The name is
    /// kept for the harness contract even though Phase D removed the per-frame copy.</summary>
    public long FramesCopied => Interlocked.Read(ref _framesPresented);

    public override int ZIndex => CompositorLayers.Video;
    // "|| _dirty" keeps the layer engine-visible for the one tick needed to consume a
    // post-Stop dirty (the engine skips inactive layers): that final invalidate clears the
    // last frame's region even when a co-active static layer (e.g. a constant tint) keeps
    // activeCount > 0 so the engine's idle-transition invalidate never runs (IMP-1).
    public override bool IsActive => _bufferValid || _dirty;

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

    /// <summary>
    /// Raised (on the UI thread) with the current playback time in milliseconds. B2 residual:
    /// the service feeds this into its watch-position watermark (<c>_lastWatchPositionMs</c>)
    /// and the <c>PrimaryPlaybackTimeMsChanged</c> Core seam (Deeper time rules) — the same
    /// contract the legacy <c>VideoOverlayWindow</c> fulfills via LibVLC <c>PositionChanged</c>
    /// (WPF <c>TimeChanged</c>, VideoService.cs:1380-1385 / :1004). The layer stays dumb: it
    /// only forwards the value. THROTTLED to at most one relay per ~1000 ms (TimeChanged fires
    /// ~4x/s; the watermark does not need more) — see <see cref="_lastTimeRelayTickMs"/>. The
    /// service reads <see cref="CurrentTimeMs"/> directly at teardown so throttle resolution
    /// never under-credits watch time.
    /// </summary>
    public event EventHandler<long>? PlaybackTimeChanged;

    public VideoLayer(LibVLC libVlc, ILogger? logger = null)
    {
        _libVlc = libVlc;
        _logger = logger;
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
                // Allocate the triple buffers and create their long-lived zero-copy
                // SKImage wrappers ONCE per playback (see protocol block above). This is
                // the only place images or buffers are created — the per-frame paths
                // (LockCallback/DisplayCallback/Update/Render) allocate nothing.
                var info = new SKImageInfo((int)_videoWidth, (int)_videoHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
                var buffers = new IntPtr[BufferCount];
                var images = new SKImage?[BufferCount];
                for (var i = 0; i < BufferCount; i++)
                {
                    buffers[i] = Marshal.AllocHGlobal((int)size);
                    using var pixmap = new SKPixmap(info, buffers[i], (int)_videoPitch);
                    images[i] = SKImage.FromPixels(pixmap);
                    if (images[i] == null)
                        throw new InvalidOperationException("SKImage.FromPixels returned null for the video buffer wrap");
                }
                _buffers = buffers;
                _images = images;
                _frontIdx = 0;
                _readyIdx = 1;
                _decodeIdx = 2;
                _readyFresh = false;
                _hasFrontFrame = false;
                _locksOutstanding = 0;
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
            _player.TimeChanged += OnTimeChanged;
            _lastTimeRelayTickMs = 0; // release the throttle for the new session

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
        _dirty = true; // the presented frame goes away — its region must repaint (clear) once (IMP-1)
        _firstFrameLogged = false;
        _loop = false;
        _lastTimeRelayTickMs = 0; // a queued TimeChanged from the dying player must not relay

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
            player.TimeChanged -= OnTimeChanged;
        }
        try { player?.Stop(); } catch { }

        IntPtr[]? buffers;
        SKImage?[]? images;
        lock (_bufferLock)
        {
            buffers = _buffers;
            images = _images;
            _buffers = null;
            _images = null;
            _bufferValid = false;
            _readyFresh = false;
            _hasFrontFrame = false;
            _locksOutstanding = 0;
            _frontIdx = 0;
            _readyIdx = 1;
            _decodeIdx = 2;
        }

        // Deferred teardown (see protocol block): the player may still be winding down its
        // vout and the render thread may be mid-draw of the front wrapper on the last engine
        // tick. Dispose the player first (no more callbacks), then the SKImage wrappers, and
        // only then free the pinned memory they wrap.
        // Track the task (only when there is something to free, so a redundant Stop() cannot
        // overwrite a still-pending real teardown with a no-op task) so WaitForTeardown() can
        // drain it on app shutdown.
        if (player != null || media != null || buffers != null || images != null)
        {
            _teardownTask = Task.Run(async () =>
            {
                await Task.Delay(400);
                try { player?.Dispose(); } catch { }
                try { media?.Dispose(); } catch { }
                if (images != null)
                {
                    foreach (var image in images)
                    {
                        try { image?.Dispose(); } catch { }
                    }
                }
                if (buffers != null)
                {
                    foreach (var buffer in buffers)
                    {
                        if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                    }
                }
            });
        }
    }

    /// <summary>
    /// Blocks up to <paramref name="timeoutMs"/> ms for the most recent deferred teardown scheduled
    /// by <see cref="Stop"/> to finish (dispose the player/media and free the pinned buffers). Called
    /// by AvaloniaVideoService.Dispose on app shutdown so the process does not exit while a native
    /// LibVLC dispose / HGlobal free is still pending - that race segfaults on teardown. No-op when
    /// no teardown is pending. Safe from the UI thread: the deferred body runs entirely on the thread
    /// pool and touches no UI-thread state, so Wait() cannot deadlock.
    /// </summary>
    public void WaitForTeardown(int timeoutMs = 1500)
    {
        try { _teardownTask?.Wait(timeoutMs); }
        catch { /* a faulted/timed-out teardown is non-fatal at shutdown */ }
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            VideoEnded?.Invoke(this, EventArgs.Empty);
            // Auto-close: a one-shot clip must release the screen when it finishes. Otherwise
            // the layer lingers on its final frame — IsActive stays true while the last decoded
            // frame is still presented — and the desktop stays blocked. A looping clip is exempt:
            // it is driven by input-repeat and replayed by LibVLC rather than reaching a real end.
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

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        // Fires on LibVLC's thread ~4x/s. THROTTLE to ~1 relay/s (the watch-position watermark
        // and the Deeper time-rule feed do not need sub-second granularity); the service reads
        // CurrentTimeMs directly at teardown, so this only bounds mid-playback event spam.
        var now = Environment.TickCount64;
        var last = _lastTimeRelayTickMs;
        if (now - last < 1000) return;
        // Non-interlocked CAS-style update is fine: worst case two LibVLC threads both pass the
        // gate in the same ms and post twice — the service's watermark is a max(), so idempotent.
        _lastTimeRelayTickMs = now;
        var timeMs = e.Time; // already milliseconds
        // Stale-session rejection: a TimeChanged queued by a player being torn down must not
        // poison the next session's watermark. The sender is the firing LibVLC player; if it is
        // no longer the current player (Stop nulled _player, or a new PlayVideo replaced it),
        // the post is from a dead session and is dropped. Marshaled like the other player events.
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(sender, _player)) return;
            PlaybackTimeChanged?.Invoke(this, timeMs);
        });
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
            var buffers = _buffers;
            if (!_bufferValid || buffers == null)
            {
                Marshal.WriteIntPtr(planes, IntPtr.Zero);
                return IntPtr.Zero;
            }
            // Only ever hand LibVLC the DECODE buffer (protocol block above). The
            // outstanding-lock count keeps DisplayCallback from rotating a buffer that
            // another in-flight picture could still be writing.
            _locksOutstanding++;
            Marshal.WriteIntPtr(planes, buffers[_decodeIdx]);
            return IntPtr.Zero;
        }
    }

    private void DisplayCallback(IntPtr opaque, IntPtr picture)
    {
        lock (_bufferLock)
        {
            // Unpaired display (failed lock, or a stale callback from a player being torn
            // down — Stop() zeroed the count): never rotate on it.
            if (_locksOutstanding == 0) return;
            _locksOutstanding--;
            if (_locksOutstanding != 0 || !_bufferValid) return;

            // Frame complete and no other picture in flight: publish DECODE as READY.
            // The old READY becomes the next decode target (latest-wins).
            (_decodeIdx, _readyIdx) = (_readyIdx, _decodeIdx);
            _readyFresh = true;
        }
    }

    public override void Update(TimeSpan deltaTime)
    {
        // Presentation tick (Phase D.2): runs on the UI thread from the engine's single
        // ~16ms loop while IsActive (true from PlayVideo's buffer allocation onward, so
        // the first frames are presented as soon as they decode — the service Start()s
        // the engine before PlayVideo). Swap FRONT<->READY; no pixel copy.
        var presented = false;
        lock (_bufferLock)
        {
            if (_bufferValid && _readyFresh)
            {
                (_frontIdx, _readyIdx) = (_readyIdx, _frontIdx);
                _readyFresh = false;
                _hasFrontFrame = true;
                presented = true;
            }
        }
        if (!presented) return;

        _dirty = true; // a new frame is on FRONT — this tick must invalidate (IMP-1)
        Interlocked.Increment(ref _framesPresented);
        if (!_firstFrameLogged)
        {
            _firstFrameLogged = true;
            _logger?.LogInformation("VideoLayer: first frame presented {Width}x{Height} (Z={Z})", _videoWidth, _videoHeight, ZIndex);
        }
    }

    /// <summary>
    /// Dirty gate (IMP-1): the engine ticks at ~60Hz while clips decode at ~25-30fps, so
    /// reporting dirty only when <see cref="Update"/> actually presented a frame
    /// (FRONT&lt;-&gt;READY swap) — or when <see cref="Stop"/> tore the frame down and its
    /// region must clear — roughly halves full-screen GPU render passes during plain video
    /// playback. Consume-once flag per the PinkTintLayer idiom; no lock (set and consumed on
    /// the engine's UI tick). A false here never starves overlapping effects: the engine
    /// re-renders ALL active layers whenever any window invalidates.
    /// </summary>
    public override bool ConsumeDirty()
    {
        var dirty = _dirty;
        _dirty = false;
        return dirty;
    }

    public override void Render(SKCanvas canvas, PixelRect bounds, TimeSpan deltaTime)
    {
        // Grab the FRONT wrapper reference under the lock, draw outside it: the draw
        // records against a buffer nobody writes (protocol block above), and disposal is
        // 400ms-deferred, so the reference stays valid even if Stop() lands mid-draw.
        SKImage? front;
        lock (_bufferLock)
        {
            var images = _images;
            if (!_hasFrontFrame || images == null) return;
            front = images[_frontIdx];
        }
        if (front == null) return;

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
        canvas.DrawImage(front, dest);
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
