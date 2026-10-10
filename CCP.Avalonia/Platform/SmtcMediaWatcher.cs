#if CCP_WINRT
// PORTED from ConditioningControlPanel/Services/Awareness/SmtcMediaWatcher.cs (7.1.5), unchanged in
// behaviour: one session manager requested once, a 3 s poll while started, one read in flight, every
// path wrapped. Privacy as WPF: titles and artists stay in memory on this machine; this class never
// touches a file and never logs what is playing.

using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Awareness;
using Serilog;
using Windows.Media.Control;

namespace ConditioningControlPanel.Avalonia.Platform;

[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class SmtcMediaWatcher : IMediaWatcher
{
    private readonly object _lock = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private Timer? _timer;
    private MediaSample? _current;
    private volatile bool _available;
    private int _reading;
    private bool _started;
    private bool _disposed;

    public MediaSample? Current { get { lock (_lock) return _current; } }
    public bool IsAvailable => _available;

    public void Start()
    {
        if (_disposed || _started) return;
        _started = true;

        // Restart: the manager is already resolved, re-arm the poll (WPF: Stop/Start is a mainline cycle).
        if (_manager != null)
        {
            _available = true;
            if (_timer != null)
            {
                try { _timer.Change(TimeSpan.Zero, MediaAwareness.PollInterval); return; }
                catch (ObjectDisposedException) { _timer = null; }
            }
            _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, MediaAwareness.PollInterval);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_disposed) return;
                _manager = manager;
                if (!_started) return;   // stopped while WinRT was thinking: keep the manager, do not poll
                _available = manager != null;
                if (!_available) { Log.Debug("Media awareness: SMTC returned no session manager, media signal off"); return; }
                _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, MediaAwareness.PollInterval);
                Log.Debug("Media awareness: SMTC watcher armed");
            }
            catch (Exception ex)
            {
                _available = false;
                Log.Debug("Media awareness: SMTC unavailable - {Error}", ex.Message);
            }
        });
    }

    public void Stop()
    {
        _started = false;
        try { _timer?.Change(Timeout.Infinite, Timeout.Infinite); } catch (ObjectDisposedException) { }
        lock (_lock) _current = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _timer?.Dispose(); } catch { }
        _timer = null;
        _manager = null;
        _available = false;
        lock (_lock) _current = null;
    }

    private void Poll()
    {
        if (_disposed || !_started) return;
        if (Interlocked.Exchange(ref _reading, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var sample = await ReadAsync().ConfigureAwait(false);
                if (_disposed || !_started) return;
                lock (_lock) _current = sample;
            }
            catch (Exception ex) { Log.Debug("Media awareness: SMTC read failed - {Error}", ex.Message); }
            finally { Interlocked.Exchange(ref _reading, 0); }
        });
    }

    private async Task<MediaSample?> ReadAsync()
    {
        var manager = _manager;
        if (manager == null) return null;

        GlobalSystemMediaTransportControlsSession? session;
        try { session = manager.GetCurrentSession(); }
        catch { return null; }
        if (session == null) return null;

        string title;
        string? artist;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (props == null) return null;
            title = props.Title ?? "";
            artist = string.IsNullOrWhiteSpace(props.Artist) ? null : props.Artist;
        }
        catch { return null; }
        if (string.IsNullOrWhiteSpace(title)) return null;

        var state = "Unknown";
        try { state = session.GetPlaybackInfo()?.PlaybackStatus.ToString() ?? "Unknown"; } catch { }
        var position = TimeSpan.Zero;
        try { position = session.GetTimelineProperties()?.Position ?? TimeSpan.Zero; } catch { }
        return new MediaSample(title, artist, state, position);
    }
}
#endif
