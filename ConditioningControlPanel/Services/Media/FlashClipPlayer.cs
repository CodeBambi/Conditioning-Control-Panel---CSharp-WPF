using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace ConditioningControlPanel.Services;

// Silent clip frames use the same transparent WPF/Skia surfaces as GIFs.
internal sealed class FlashClipPlayer : IDisposable
{
    private static int _alive;
    private readonly object _gate = new();
    private readonly Action<BitmapSource> _show;
    private readonly int _width, _height;
    private readonly MediaPlayer _player;
    private readonly Media _media;
    private IntPtr _buffer;
    private byte[]? _latest;
    private long _lastFrame;
    private bool _queued;
    private volatile bool _disposed;

    internal static FlashClipPlayer? Start(string? path, int width, int height, Action<BitmapSource> show, double speed = 1)
    {
        if (path == null || VideoService.SharedLibVLC is not { } vlc) return null;
        if (Interlocked.Increment(ref _alive) > 8) { Interlocked.Decrement(ref _alive); return null; }
        try { return new FlashClipPlayer(vlc, path, width, height, show, speed); }
        catch (Exception ex)
        {
            Interlocked.Decrement(ref _alive);
            App.Logger?.Debug("Flash clip failed: {Error}", ex.Message);
            return null;
        }
    }

    private FlashClipPlayer(LibVLC vlc, string path, int width, int height, Action<BitmapSource> show, double speed)
    {
        _show = show;
        double scale = Math.Min(1, 480.0 / Math.Max(1, Math.Max(width, height)));
        _width = Math.Max(2, (int)(width * scale));
        _height = Math.Max(2, (int)(height * scale));
        _buffer = Marshal.AllocHGlobal(_width * _height * 4);
        _player = new MediaPlayer(vlc) { Mute = true, EnableHardwareDecoding = false };
        _media = new Media(vlc, path, FromType.FromPath);
        try
        {
            _media.AddOption(":no-audio");
            _media.AddOption(":input-repeat=65535");
            _player.SetVideoFormat("RV32", (uint)_width, (uint)_height, (uint)_width * 4);
            _player.SetVideoCallbacks(Lock, null, Display);
            if (!_player.Play(_media)) throw new InvalidOperationException("Clip playback refused");
            _player.SetRate((float)Math.Clamp(speed, .25, 4));
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
        if (_disposed || Environment.TickCount64 - _lastFrame < 66) return;
        _lastFrame = Environment.TickCount64;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        lock (_gate)
        {
            if (_disposed) return;
            _latest ??= new byte[_width * _height * 4];
            Marshal.Copy(_buffer, _latest, 0, _latest.Length);
            if (_queued) return;
            _queued = true;
        }
        dispatcher.BeginInvoke(() =>
        {
            byte[]? pixels;
            lock (_gate) { pixels = _latest; _latest = null; _queued = false; }
            if (_disposed || pixels == null) return;
            var frame = BitmapSource.Create(_width, _height, 96, 96, PixelFormats.Bgr32, null, pixels, _width * 4);
            frame.Freeze();
            _show(frame);
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Native callbacks retain their buffer until Stop and Dispose have completed.
        _ = Task.Run(() =>
        {
            try
            {
                _player.Stop();
                _player.Dispose();
                _media.Dispose();
                Marshal.FreeHGlobal(_buffer);
                Interlocked.Decrement(ref _alive);
            }
            catch (Exception ex)
            {
                // Keep the native buffer and budget slot if shutdown cannot be confirmed.
                App.Logger?.Warning("Flash clip cleanup failed: {Error}", ex.Message);
            }
        });
    }
}
