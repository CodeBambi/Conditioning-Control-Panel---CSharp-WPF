using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The now-playing watcher, on Windows' System Media Transport Controls — the single richest cheap
    /// signal on this platform (doc 02 §2.2). It is what turns "you're on Spotify" into "that is the
    /// fourth time round on the same song".
    ///
    /// <para><b>Defensive by design.</b> SMTC is denied outright on some machines, absent on others,
    /// and its session objects can be torn down between two calls. Every path here is wrapped, the
    /// manager is requested asynchronously exactly once, and any failure simply leaves
    /// <see cref="IsAvailable"/> false — awareness losing the media signal is one missing joke, never
    /// an error the user sees.</para>
    ///
    /// <para><b>Privacy.</b> Titles and artists read here are machine-local. They reach
    /// <see cref="ContextFrame.NowPlaying"/>, from which the LOCAL projection may include them and the
    /// CLOUD projection includes only the repeat count (<see cref="AwarenessProjection"/>). Nothing
    /// here is ever written to disk: <see cref="ActivityLedger"/> has no parameter that could carry a
    /// track title, and this class never touches a file.</para>
    /// </summary>
    public sealed class SmtcMediaWatcher : IMediaWatcher
    {
        /// <summary>How often the current session is re-read. Media does not need 1.5s resolution.</summary>
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

        private readonly object _lock = new();
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private Timer? _timer;
        private MediaSample? _current;
        private volatile bool _available;
        private int _reading;
        private bool _started;
        private bool _disposed;

        /// <inheritdoc />
        public MediaSample? Current
        {
            get { lock (_lock) return _current; }
        }

        /// <inheritdoc />
        public bool IsAvailable => _available;

        /// <inheritdoc />
        public void Start()
        {
            if (_disposed || _started) return;
            _started = true;

            _ = Task.Run(async () =>
            {
                try
                {
                    var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    if (_disposed) return;

                    _manager = manager;
                    _available = manager != null;

                    if (!_available)
                    {
                        App.Logger?.Debug("AwarenessObserver: SMTC returned no session manager - media signal off");
                        return;
                    }

                    _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, PollInterval);
                    App.Logger?.Debug("AwarenessObserver: SMTC media watcher armed");
                }
                catch (Exception ex)
                {
                    // Denied, unsupported, or the WinRT activation failed. Feature-off, not a crash.
                    _available = false;
                    App.Logger?.Debug("AwarenessObserver: SMTC unavailable - {Error}", ex.Message);
                }
            });
        }

        /// <inheritdoc />
        public void Stop()
        {
            try { _timer?.Change(Timeout.Infinite, Timeout.Infinite); } catch (ObjectDisposedException) { }
            lock (_lock) _current = null;
        }

        /// <inheritdoc />
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
            if (_disposed) return;

            // One read in flight at a time: TryGetMediaPropertiesAsync can outlive a 3s tick when the
            // owning app is busy, and a backlog of overlapping reads would fight over _current.
            if (Interlocked.Exchange(ref _reading, 1) == 1) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var sample = await ReadAsync().ConfigureAwait(false);
                    if (_disposed) return;
                    lock (_lock) _current = sample;
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("AwarenessObserver: SMTC read failed - {Error}", ex.Message);
                }
                finally
                {
                    Interlocked.Exchange(ref _reading, 0);
                }
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

            string state = "Unknown";
            try { state = session.GetPlaybackInfo()?.PlaybackStatus.ToString() ?? "Unknown"; }
            catch { }

            var position = TimeSpan.Zero;
            try { position = session.GetTimelineProperties()?.Position ?? TimeSpan.Zero; }
            catch { }

            return new MediaSample(title, artist, state, position);
        }
    }
}
