using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using LibVLCSharp.Shared;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The real <see cref="IDtrhVideoBackend"/> on the SP-018-admitted LibVLCSharp 3.10.0
/// shape (live-feed re-confirmed 2026-07-22). Findings baked in as binding disciplines
/// (video-handoff-spike.md §3):
///  V1 — D3D11VA hw decode segfaults this box even on local parse → software decode
///       (<c>--avcodec-hw=none</c>); hw viability stays with the unified-video row.
///  V2 — dummy vout crashes at/after EndReached → vmem memory-callback vout (5/5 stable)
///       AND frame-level decode proof (display-callback count, strictly stronger than
///       TimeChanged).
///  V3 — libvlc native release segfaults in the probe shape (every disposal order) →
///       ONE app-lifetime LibVLC + MediaPlayer; per-fire only a new Media; LibVLC/player
///       release SKIPPED at teardown (OS reclaims; clean-teardown ownership stays with
///       the unified-video row — recorded, never claimed solved). Media release ON
///       REPLACE is dev-loop-CLEARED (SP-025 evidence/devloop/v3-transcript.txt: 5/5
///       clean cycles, EndReached → background-Stop → Media.Dispose, exit 0).
/// Stop() runs on a background thread (pre-completion consult: UI-thread Stop deadlocks
/// against vmem callbacks — libvlc known class). The frame pin + vmem delegates stay
/// rooted for the process lifetime (shutdown crash class: callbacks into freed managed
/// memory).
/// </summary>
public sealed class LibVlcDtrhVideo : IDtrhVideoBackend
{
    private readonly Action<string> _log;
    private readonly Action<Action> _uiPost;
    private LibVLC? _vlc;
    private MediaPlayer? _player;
    private Media? _media;

    // vmem state — rooted for the PROCESS lifetime (never freed: late native callbacks).
    private byte[]? _frameBuffer;
    private GCHandle _framePin;
    private uint _frameWidth;
    private uint _frameHeight;
    private readonly object _frameGate = new();
    private WriteableBitmap? _currentFrame;
    private long _frameCount;
    private double _positionSec;
    private int _playing;
    private int _proposalsLogged;
    private bool _formatFrozen;

    // Delegates as FIELDS (rooted): a collected delegate is a native crash on next frame.
    private MediaPlayer.LibVLCVideoFormatCb? _formatCb;
    private MediaPlayer.LibVLCVideoLockCb? _lockCb;
    private MediaPlayer.LibVLCVideoUnlockCb? _unlockCb;
    private MediaPlayer.LibVLCVideoDisplayCb? _displayCb;

    public LibVlcDtrhVideo(Action<string> log, Action<Action> uiPost)
    {
        _log = log;
        _uiPost = uiPost;
    }

    public long FrameCount => Interlocked.Read(ref _frameCount);

    public double PositionSec => _positionSec;

    public WriteableBitmap? CurrentFrame => _currentFrame;

    public event EventHandler? FramePresented;

    public event EventHandler? PlaybackEnded;

    public event EventHandler? EncounteredError;

    /// <inheritdoc/>
    public bool TryPlay(string path)
    {
        try
        {
            EnsurePlayer();
            if (_player is null || _vlc is null) return false;

            var media = new Media(_vlc, path, FromType.FromPath);
            var old = Interlocked.Exchange(ref _media, media);
            if (old is not null)
            {
                // V3 dev-loop experiment (SP-025 evidence/devloop/v3-transcript.txt):
                // Media.Dispose after EndReached/Stop is CLEAN 5/5 in the product shape
                // (persistent player, media replaced per fire) — the probe-shape segfault
                // does NOT transfer. LibVLC/player release at process exit stays SKIPPED
                // (trailing libvlc stderr noise at teardown; OS reclaims).
                try { old.Dispose(); } catch { /* best-effort */ }
            }

            Interlocked.Exchange(ref _frameCount, 0L);
            _positionSec = 0;
            _proposalsLogged = 0;
            _formatFrozen = false;
            _player.Media = media;
            Interlocked.Exchange(ref _playing, 1);
            var ok = _player.Play();
            if (!ok)
            {
                Interlocked.Exchange(ref _playing, 0);
                _log($"dtrh-video: libvlc refused play ({Path.GetFileName(path)})");
            }

            return ok;
        }
        catch (Exception ex)
        {
            _log($"dtrh-video: TryPlay failed ({ex.GetType().Name})");
            return false;
        }
    }

    /// <inheritdoc/>
    public void SetPaused(bool paused)
    {
        try
        {
            if (Volatile.Read(ref _playing) == 1) _player?.SetPause(paused);
        }
        catch (Exception ex) { _log($"dtrh-video: SetPause({paused}) failed ({ex.GetType().Name})"); }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (Interlocked.Exchange(ref _playing, 0) != 1) return;
        var player = _player;
        if (player is null) return;
        // Background thread (consult): UI-thread Stop can deadlock against vmem callbacks.
        _ = Task.Run(() =>
        {
            try { player.Stop(); } catch (Exception ex) { _log($"dtrh-video: Stop failed ({ex.GetType().Name})"); }
        });
    }

    private void EnsurePlayer()
    {
        if (_player is not null) return;
        try
        {
            Core.Initialize();
            _vlc = new LibVLC("--no-video-title-show", "--avcodec-hw=none");
            _player = new MediaPlayer(_vlc);
            _formatCb = FormatCallback;
            _lockCb = LockFrame;
            _unlockCb = null; // unlock not required by vmem
            _displayCb = DisplayFrame;
            _player.SetVideoFormatCallbacks(_formatCb, null);
            _player.SetVideoCallbacks(_lockCb, _unlockCb!, _displayCb);
            _player.TimeChanged += (_, e) => _positionSec = e.Time / 1000.0;
            _player.EndReached += (_, _) => { Interlocked.Exchange(ref _playing, 0); PlaybackEnded?.Invoke(this, EventArgs.Empty); };
            _player.EncounteredError += (_, _) => { Interlocked.Exchange(ref _playing, 0); EncounteredError?.Invoke(this, EventArgs.Empty); };
            _log("dtrh-video: libvlc up (vmem vout, software decode — V1/V2 disciplines)");
        }
        catch (Exception ex)
        {
            // Honest unavailable (e.g. libvlc absent on a Linux box without libvlc5):
            // the video channel reports refused, never crashes the host.
            _log($"dtrh-video: libvlc init failed ({ex.GetType().Name}: {ex.Message}) — video channel unavailable this session");
            _vlc = null;
            _player = null;
        }
    }

    // Decoder-proposed dims; we request RV32 (= BGRA on little-endian), CAPPED at 1280
    // wide (aspect preserved): the any→RV32 converter at NATIVE 4K dims produces black
    // frames on this box (run-A evidence: 3840x2178 requested → 91.5%-dark captures while
    // the source frames measure luma 109-134; 1280x738 native rendered fine, and the
    // SP-018 spike's forced-96x96 conversion is the proven envelope). The window's
    // Stretch.Uniform rescales — pixels stay real decoded content.
    private const uint MaxFrameWidth = 1280;

    private uint FormatCallback(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        var rv32 = System.Text.Encoding.ASCII.GetBytes("RV32");
        Marshal.Copy(rv32, 0, chroma, 4);
        if (_proposalsLogged < 3)
        {
            _proposalsLogged++;
            _log($"dtrh-video: vmem format proposal {width}x{height} (pitches {pitches}, lines {lines})");
        }

        if (!_formatFrozen)
        {
            // First proposal of the stream FREEZES the output format (run-A forensics:
            // libvlc re-asks with jittering proposals — 1920x1088 → 1920x1090 — and a
            // mid-stream buffer realloc frees the pin the vout still writes into →
            // luma-0 black frames). One allocation per stream, pin rooted forever.
            _formatFrozen = true;
            if (width > MaxFrameWidth)
            {
                height = Math.Max(1, height * MaxFrameWidth / width);
                width = MaxFrameWidth;
            }

            lock (_frameGate)
            {
                _frameWidth = width;
                _frameHeight = height;
                _frameBuffer = new byte[width * 4 * height];
                // Never freed (process-lifetime rooted): a late native callback into a
                // freed pin is a crash/black-frame class (pre-completion consult).
                _framePin = GCHandle.Alloc(_frameBuffer, GCHandleType.Pinned);
            }
        }

        // Every callback (incl. re-asks) returns the SAME frozen format.
        lock (_frameGate)
        {
            width = _frameWidth;
            height = _frameHeight;
            pitches = _frameWidth * 4;
            lines = _frameHeight;
        }

        return 1;
    }

    private IntPtr LockFrame(IntPtr opaque, IntPtr planes)
    {
        lock (_frameGate)
        {
            var addr = _framePin.AddrOfPinnedObject();
            Marshal.WriteIntPtr(planes, addr);
            return addr;
        }
    }

    private void DisplayFrame(IntPtr opaque, IntPtr picture)
    {
        byte[] copy;
        int w, h;
        lock (_frameGate)
        {
            if (_frameBuffer is null) return;
            copy = (byte[])_frameBuffer.Clone();
            w = (int)_frameWidth;
            h = (int)_frameHeight;
        }

        // Decoded-content proof from the backend itself (first 3 frames): mean luma of
        // the vmem buffer — a black-buffer defect is diagnosable without a capture.
        if (Volatile.Read(ref _frameCount) < 3)
        {
            long sum = 0;
            var samples = 0;
            for (var i = 0; i + 2 < copy.Length; i += 401 * 4)
            {
                sum += (copy[i] + copy[i + 1] + copy[i + 2]) / 3;
                samples++;
            }

            _log($"dtrh-video: vmem frame luma≈{(samples > 0 ? sum / samples : -1)} ({w}x{h})");
        }

        // Marshal the bitmap swap to the UI thread (pre-completion consult); the display
        // callback fires on a libvlc thread.
        _uiPost(() =>
        {
            try
            {
                if (_currentFrame is null || _currentFrame.PixelSize.Width != w || _currentFrame.PixelSize.Height != h)
                {
                    _currentFrame = new WriteableBitmap(
                        new PixelSize(w, h), new Vector(96, 96),
                        Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
                }

                using (var fb = _currentFrame.Lock())
                {
                    Marshal.Copy(copy, 0, fb.Address, copy.Length);
                }

                Interlocked.Increment(ref _frameCount);
                FramePresented?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _log($"dtrh-video: frame swap failed ({ex.GetType().Name})");
            }
        });
    }
}
