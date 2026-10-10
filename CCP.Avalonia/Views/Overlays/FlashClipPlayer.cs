using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// A moving online flash: WPF Services/Media/FlashClipPlayer.cs on this head. The clip loops
    /// silently (<c>:no-audio</c> per media, never <c>Mute</c>: LibVLC mute is process-wide), decoded
    /// at most 480 px on its long side and shown at ~15 fps into one <see cref="WriteableBitmap"/>.
    /// At most 8 run at once; past that a flash keeps its poster still. The GIF speed setting is the
    /// playback rate (0.25..4), as in WPF.
    /// </summary>
    internal sealed class FlashClipPlayer : IDisposable
    {
        private static int _alive;
        internal static int Alive => Volatile.Read(ref _alive);
        internal const int MaxAlive = 8;
        internal const int LongSideMax = 480;

        private readonly object _gate = new();
        private readonly Action<WriteableBitmap> _show;
        private readonly int _width, _height, _frameGapMs;
        private readonly MediaPlayer _player;
        private readonly Media _media;
        private IntPtr _buffer;
        private byte[]? _latest;
        private long _lastFrame;
        private bool _queued;
        private WriteableBitmap? _bitmap;
        private volatile bool _disposed;

        /// <summary>WPF FlashClipPlayer.Start: null when there is no path, no libvlc, or 8 already play.</summary>
        internal static FlashClipPlayer? Start(LibVLC? vlc, string? path, int width, int height, Action<WriteableBitmap> show, double speed = 1,
            int longSideMax = LongSideMax, int frameGapMs = 66)
        {
            if (string.IsNullOrEmpty(path) || vlc == null) return null;
            if (Interlocked.Increment(ref _alive) > MaxAlive) { Interlocked.Decrement(ref _alive); return null; }
            try { return new FlashClipPlayer(vlc, path, width, height, show, speed, longSideMax, frameGapMs); }
            catch (Exception ex)
            {
                Interlocked.Decrement(ref _alive);
                Log.Debug("Flash clip failed: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>Lane k16: the same silent looping player on the process's shared decoder, for a
        /// surface that must not name the audio stack (the Tonight Board's showcase clip). Silent by
        /// construction: every media this class opens carries <c>:no-audio</c>.</summary>
        internal static FlashClipPlayer? StartSilent(string? path, int width, int height, Action<WriteableBitmap> show,
            int longSideMax = LongSideMax, int frameGapMs = 66) =>
            Start(global::ConditioningControlPanel.Avalonia.Platform.LibVlcAudio.Shared, path, width, height, show, 1, longSideMax, frameGapMs);

        /// <summary>The decode size: the flash's size scaled so its long side is at most 480 px.</summary>
        internal static (int W, int H) DecodeSize(int width, int height, int longSideMax = LongSideMax)
        {
            var scale = Math.Min(1, (double)longSideMax / Math.Max(1, Math.Max(width, height)));
            return (Math.Max(2, (int)(width * scale)), Math.Max(2, (int)(height * scale)));
        }

        /// <summary>WPF SetRate clamp.</summary>
        internal static float Rate(double speed) => (float)Math.Clamp(speed, .25, 4);

        private FlashClipPlayer(LibVLC vlc, string path, int width, int height, Action<WriteableBitmap> show, double speed, int longSideMax, int frameGapMs)
        {
            _show = show;
            _frameGapMs = Math.Max(16, frameGapMs);
            (_width, _height) = DecodeSize(width, height, longSideMax);
            _buffer = Marshal.AllocHGlobal(_width * _height * 4);
            _player = new MediaPlayer(vlc) { EnableHardwareDecoding = false };
            _media = new Media(vlc, path, FromType.FromPath);
            try
            {
                _media.AddOption(":no-audio");
                _media.AddOption(":input-repeat=65535");
                _player.SetVideoFormat("RV32", (uint)_width, (uint)_height, (uint)_width * 4);
                _player.SetVideoCallbacks(Lock, null, Display);
                if (!_player.Play(_media)) throw new InvalidOperationException("Clip playback refused");
                _player.SetRate(Rate(speed));
            }
            catch
            {
                _player.Dispose(); _media.Dispose(); Marshal.FreeHGlobal(_buffer);
                throw;
            }
        }

        private IntPtr Lock(IntPtr opaque, IntPtr planes)
        {
            Marshal.WriteIntPtr(planes, _buffer);
            return IntPtr.Zero;
        }

        private void Display(IntPtr opaque, IntPtr picture)
        {
            if (_disposed || Environment.TickCount64 - _lastFrame < _frameGapMs) return;
            _lastFrame = Environment.TickCount64;
            lock (_gate)
            {
                if (_disposed) return;
                _latest ??= new byte[_width * _height * 4];
                Marshal.Copy(_buffer, _latest, 0, _latest.Length);
                if (_queued) return;
                _queued = true;
            }
            Dispatcher.UIThread.Post(Blit, DispatcherPriority.Render);
        }

        private void Blit()
        {
            byte[]? pixels;
            lock (_gate) { pixels = _latest; _latest = null; _queued = false; }
            if (_disposed || pixels == null) return;
            _bitmap ??= new WriteableBitmap(new PixelSize(_width, _height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var fb = _bitmap.Lock())
            {
                var row = _width * 4;
                for (var y = 0; y < _height; y++)
                    Marshal.Copy(pixels, y * row, fb.Address + y * fb.RowBytes, row);
            }
            _show(_bitmap);
        }

        /// <summary>Stop off the UI thread (Stop joins the decoder), then free the buffer. The
        /// bitmap stays with whoever shows it; the window's close disposes the window's source.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ = Task.Run(() =>
            {
                try
                {
                    _player.Stop();
                    _player.Dispose();
                    _media.Dispose();
                    Marshal.FreeHGlobal(_buffer);
                    _buffer = IntPtr.Zero;
                    Interlocked.Decrement(ref _alive);
                }
                catch (Exception ex) { Log.Warning("Flash clip cleanup failed: {Error}", ex.Message); }
            });
        }
    }
}
