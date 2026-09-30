using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// LibVLC's decode target (vmem) copied into one <see cref="WriteableBitmap"/> - the frame copy
    /// WPF InlineLoopVideo does, and the only video path on this head (LibVLCSharp.Avalonia is
    /// Avalonia-11 only). The buffer is written by the decoder thread and copied on the UI thread;
    /// the lock guards its lifetime, not tearing. Any number of <c>Image</c>s may show
    /// <see cref="Bitmap"/>: one decoder feeds every screen.
    /// </summary>
    internal sealed class VlcFrameSink
    {
        private readonly object _lock = new();
        private readonly Func<Media?> _media;
        private readonly Action<WriteableBitmap> _bitmapChanged;
        private readonly Action _frame;
        private IntPtr _buffer;
        private int _width, _height, _blitQueued, _frames;
        private bool _freed;
        private byte[] _row = Array.Empty<byte>();

        /// <param name="bitmapChanged">UI thread: a new bitmap (first frame or a size change) to show.</param>
        /// <param name="frame">UI thread: the bitmap holds a new frame (invalidate what shows it).</param>
        public VlcFrameSink(MediaPlayer player, Func<Media?> media, Action<WriteableBitmap> bitmapChanged, Action frame)
        {
            _media = media;
            _bitmapChanged = bitmapChanged;
            _frame = frame;
            player.SetVideoFormatCallbacks(Format, (ref IntPtr _) => { });
            player.SetVideoCallbacks(LockPicture, null, Display);
        }

        public WriteableBitmap? Bitmap { get; private set; }
        public int FrameCount => Volatile.Read(ref _frames);
        public bool Freed { get { lock (_lock) return _buffer == IntPtr.Zero; } }

        private uint Format(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
        {
            // RV32 = B,G,R,X in memory: Bgra8888 with the alpha ignored (AlphaFormat.Opaque).
            Marshal.Copy(new[] { (byte)'R', (byte)'V', (byte)'3', (byte)'2' }, 0, chroma, 4);
            // VLC offers padded decoder dimensions (320x240 arrives as 320x258) and scales into
            // whatever we answer, so answer the track's display size to keep the aspect ratio.
            foreach (var t in _media()?.Tracks ?? Array.Empty<MediaTrack>())
            {
                if (t.TrackType != TrackType.Video || t.Data.Video.Width == 0 || t.Data.Video.Height == 0) continue;
                var v = t.Data.Video;
                width = v.SarNum > 0 && v.SarDen > 0 ? v.Width * v.SarNum / v.SarDen : v.Width;
                height = v.Height;
                break;
            }
            pitches = width * 4;
            lines = height;
            lock (_lock)
            {
                if (_freed) return 0;
                if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
                _buffer = Marshal.AllocHGlobal((int)(pitches * lines));
                _width = (int)width;
                _height = (int)height;
            }
            return 1;
        }

        private IntPtr LockPicture(IntPtr opaque, IntPtr planes)
        {
            lock (_lock) Marshal.WriteIntPtr(planes, _buffer);
            return IntPtr.Zero;
        }

        private void Display(IntPtr opaque, IntPtr picture)
        {
            // One pending blit at a time: a slow UI thread drops frames rather than queueing them.
            if (Interlocked.Exchange(ref _blitQueued, 1) == 0)
                Dispatcher.UIThread.Post(Blit, DispatcherPriority.Render);
        }

        private void Blit()
        {
            Volatile.Write(ref _blitQueued, 0);
            lock (_lock)
            {
                if (_buffer == IntPtr.Zero) return;
                if (Bitmap == null || Bitmap.PixelSize.Width != _width || Bitmap.PixelSize.Height != _height)
                {
                    Bitmap?.Dispose();
                    Bitmap = new WriteableBitmap(new PixelSize(_width, _height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                    _bitmapChanged(Bitmap);
                }
                using var fb = Bitmap.Lock();
                var rowBytes = _width * 4;
                if (_row.Length != rowBytes) _row = new byte[rowBytes];
                for (var y = 0; y < _height; y++)
                {
                    Marshal.Copy(_buffer + y * rowBytes, _row, 0, rowBytes);
                    Marshal.Copy(_row, 0, fb.Address + y * fb.RowBytes, rowBytes);
                }
            }
            Interlocked.Increment(ref _frames);
            _frame();
        }

        /// <summary>Free the buffer and bitmap. Call after <c>MediaPlayer.Stop</c>, which joins the
        /// decoder thread, so no callback touches the buffer afterwards.</summary>
        public void Free()
        {
            lock (_lock)
            {
                _freed = true;
                if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
                _buffer = IntPtr.Zero;
            }
            Bitmap?.Dispose();
            Bitmap = null;
        }
    }
}
