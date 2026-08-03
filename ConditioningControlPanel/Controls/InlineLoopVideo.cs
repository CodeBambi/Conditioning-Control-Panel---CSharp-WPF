using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using ConditioningControlPanel.Services;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// A small muted, looping video surface that renders LibVLC frames into a WPF
    /// <see cref="Image"/> via memory callbacks (no <c>VideoView</c>/HwndHost), so it
    /// composites cleanly inside a transparent, rounded <see cref="System.Windows.Controls.Primitives.Popup"/>
    /// — unlike a windowed VideoView, which airspace-conflicts with a layered popup.
    ///
    /// Pattern (buffer + lock/display callbacks + CompositionTarget.Rendering blit +
    /// delayed native teardown) is lifted from <see cref="DualMonitorVideoService"/>,
    /// trimmed to a single in-process surface.
    ///
    /// Lifecycle: <see cref="Resume"/> on show, <see cref="Pause"/> on hide (keeps the
    /// player alive but stops decoding), <see cref="Dispose"/> on teardown. Fail-soft:
    /// if LibVLC is unavailable or the clip is missing, the surface simply stays blank.
    /// </summary>
    public sealed class InlineLoopVideo : IDisposable
    {
        private readonly string _path;
        private readonly uint _w;
        private readonly uint _h;
        private readonly Image _image;
        private readonly WriteableBitmap _bitmap;
        private readonly object _bufferLock = new();

        private IntPtr _frameBuffer = IntPtr.Zero;
        private volatile bool _frameReady;
        private volatile bool _bufferValid;

        /// <summary>Managed staging copy of the newest decoded frame — see <see cref="OnRendering"/>
        /// for why the native buffer is never read while the bitmap lock is held.</summary>
        private byte[] _staging = Array.Empty<byte>();

        /// <summary>Per-frame budget for <c>WriteableBitmap.TryLock</c>; a wedged render thread must
        /// cost a frame, not the dispatcher.</summary>
        private static readonly Duration BlitLockBudget = new(TimeSpan.FromMilliseconds(150));

        private VlcMediaPlayer? _player;
        private Media? _media;
        private bool _started;
        private bool _renderingHooked;
        private bool _disposed;

        public InlineLoopVideo(string clipPath, uint width = 480, uint height = 270)
        {
            _path = clipPath;
            _w = width;
            _h = height;
            _bitmap = new WriteableBitmap((int)_w, (int)_h, 96, 96, PixelFormats.Bgr32, null);
            _image = new Image
            {
                Source = _bitmap,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
        }

        /// <summary>The WPF element to place in the layout.</summary>
        public UIElement Surface => _image;

        /// <summary>Start (first call) or resume decoding into the surface.</summary>
        public void Resume()
        {
            if (_disposed) return;
            if (!_started)
            {
                _started = TryStart();
                return;
            }
            try { _player?.SetPause(false); } catch { /* disposed/native race */ }
            HookRendering();
        }

        /// <summary>Stop decoding but keep the player alive for a later <see cref="Resume"/>.</summary>
        public void Pause()
        {
            if (_disposed || !_started) return;
            UnhookRendering();
            try { _player?.SetPause(true); } catch { /* disposed/native race */ }
        }

        private bool TryStart()
        {
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path)) return false;
            var libVLC = VideoService.SharedLibVLC;
            if (libVLC == null) return false;

            try
            {
                var bufferSize = _w * _h * 4;
                lock (_bufferLock)
                {
                    _frameBuffer = Marshal.AllocHGlobal((int)bufferSize);
                    _bufferValid = true;
                }

                _player = new VlcMediaPlayer(libVLC) { Mute = true, EnableHardwareDecoding = true };
                _player.SetVideoFormat("RV32", _w, _h, _w * 4);
                _player.SetVideoCallbacks(LockCallback, null, DisplayCallback);
                // EndReached re-play is a fallback in case :input-repeat isn't honoured.
                _player.EndReached += OnEndReached;

                _media = new Media(libVLC, _path, FromType.FromPath);
                _media.AddOption(":input-repeat=65535"); // loop natively
                _media.AddOption(":no-audio");           // muted preview

                HookRendering();
                _player.Play(_media);
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "InlineLoopVideo: failed to start {Path}", _path);
                TeardownPlayer();
                return false;
            }
        }

        private void OnEndReached(object? sender, EventArgs e)
        {
            // LibVLC raises this on a native thread; re-play must be marshalled.
            var disp = Application.Current?.Dispatcher;
            if (disp == null) return;
            disp.BeginInvoke(() =>
            {
                if (_disposed || _media == null) return;
                try { _player?.Stop(); _player?.Play(_media); } catch { /* ignore */ }
            });
        }

        // ----- LibVLC memory callbacks ------------------------------------------

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

        // ----- blit on the WPF render tick --------------------------------------

        private void HookRendering()
        {
            if (_renderingHooked) return;
            CompositionTarget.Rendering += OnRendering;
            _renderingHooked = true;
        }

        private void UnhookRendering()
        {
            if (!_renderingHooked) return;
            try { CompositionTarget.Rendering -= OnRendering; } catch { /* ignore */ }
            _renderingHooked = false;
        }

        /// <summary>
        /// Per-frame blit, UI thread. Two strictly separated phases — the same split
        /// <c>VideoService.BlurVmemSurface.OnRendering</c> had to make for the #632/#636/#687 freeze
        /// cluster, which this control's ancestor code predates:
        ///
        ///   1. Under <c>_bufferLock</c>: native buffer -> managed staging. Nothing here blocks on
        ///      another thread.
        ///   2. Lock RELEASED: <c>TryLock</c> / copy / <c>AddDirtyRect</c> / <c>Unlock</c>.
        ///
        /// Doing step 2 while still holding <c>_bufferLock</c> is a three-thread deadlock:
        /// <c>WriteableBitmap.Lock()</c> waits on the WPF render thread, and the LibVLC decoder
        /// thread parks on <c>_bufferLock</c> inside <c>LockCallback</c> — with the decoder parked in
        /// a native callback, <c>player.Stop()</c> can never return. And <c>Lock()</c> has no
        /// timeout, so a render thread that never drops its read lock takes the dispatcher with it.
        /// One extra memcpy per frame buys "a stalled render thread can only ever cost frames".
        /// </summary>
        private void OnRendering(object? sender, EventArgs e)
        {
            if (!_bufferValid || !_frameReady) return;
            _frameReady = false;

            int bytes = (int)(_w * _h * 4);

            // ---- Phase 1: native -> managed, under the lock, no blocking calls. ----
            bool got = false;
            try
            {
                got = Monitor.TryEnter(_bufferLock, 8);
                if (!got) return;
                if (!_bufferValid || _frameBuffer == IntPtr.Zero) return;

                if (_staging.Length < bytes) _staging = new byte[bytes];
                Marshal.Copy(_frameBuffer, _staging, 0, bytes);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("InlineLoopVideo: frame snapshot error: {Error}", ex.Message);
                return;
            }
            finally
            {
                if (got) Monitor.Exit(_bufferLock);
            }

            // ---- Phase 2: managed -> bitmap, lock released, bounded wait on the render thread. ----
            try
            {
                if (!_bitmap.TryLock(BlitLockBudget)) return; // render thread is busy: drop the frame
                try
                {
                    Marshal.Copy(_staging, 0, _bitmap.BackBuffer, bytes);
                    _bitmap.AddDirtyRect(new Int32Rect(0, 0, (int)_w, (int)_h));
                }
                finally
                {
                    _bitmap.Unlock();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("InlineLoopVideo: frame copy error: {Error}", ex.Message);
            }
        }

        // ----- teardown ----------------------------------------------------------

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            TeardownPlayer();
        }

        private void TeardownPlayer()
        {
            // Invalidate the buffer first so LockCallback hands LibVLC nothing.
            _bufferValid = false;
            _frameReady = false;
            UnhookRendering();

            var player = _player;
            _player = null;
            var media = _media;
            _media = null;
            if (player != null) player.EndReached -= OnEndReached;

            try { player?.Stop(); } catch { /* ignore */ }

            IntPtr buf;
            lock (_bufferLock)
            {
                buf = _frameBuffer;
                _frameBuffer = IntPtr.Zero;
            }

            // Dispose native objects and free the buffer AFTER a delay, so any frame
            // still in flight on a LibVLC thread can't touch freed memory.
            Task.Run(async () =>
            {
                await Task.Delay(400);
                try { player?.Dispose(); } catch { /* ignore */ }
                try { media?.Dispose(); } catch { /* ignore */ }
                if (buf != IntPtr.Zero)
                {
                    try { Marshal.FreeHGlobal(buf); } catch { /* ignore */ }
                }
            });
        }
    }
}
