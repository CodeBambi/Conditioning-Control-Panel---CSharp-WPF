using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using ConditioningControlPanel.Models;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Port of WPF's LayeredAudioService (ConditioningControlPanel/Services/Audio/LayeredAudioService.cs)
    /// for suggestion #659 "Audio Layers". Same public surface and the same rules: Start rebuilds
    /// from settings (enabled tracks with an existing file, skipped otherwise), Restart keeps the
    /// running state, live per-track and master volume, cooperative duck. No fades - WPF has none.
    /// Deviation: one LibVLC player per track instead of one NAudio mixer; the OS mixes the streams.
    /// Gain per track = track% x layers master% x app master% x duck factor, as linear sample
    /// gain like WPF, mapped onto LibVLC's cubic volume.
    /// </summary>
    internal sealed class LayeredAudio : IDisposable
    {
        /// <summary>One looping track. The seam exists so the state machine is testable without libvlc.</summary>
        internal interface ILayerPlayer : IDisposable { int Volume { set; } }

        private readonly Func<string, ILayerPlayer> _open;
        private readonly object _lock = new();
        private readonly Dictionary<AudioLayerTrack, ILayerPlayer> _players = new();
        private float _duckFactor = 1f;
        private bool _disposed;

        internal static LayeredAudio? Instance { get; set; }

        public LayeredAudio(Func<string, ILayerPlayer> open) => _open = open;

        public bool IsPlaying { get { lock (_lock) return _players.Count > 0; } }

        public void Start(bool ignoreMasterToggle = false)
        {
            lock (_lock)
            {
                if (_disposed) return;
                StopInternal();
                var s = CoreSettings.Current;
                if (s == null || (!s.AudioLayersEnabled && !ignoreMasterToggle) || s.AudioLayers == null) return;
                foreach (var track in s.AudioLayers)
                {
                    if (track == null || !track.Enabled) continue;
                    if (string.IsNullOrWhiteSpace(track.Path) || !File.Exists(track.Path))
                    {
                        Log.Debug("LayeredAudio: skipping missing/unset track '{Path}'", track.Path);
                        continue;
                    }
                    try
                    {
                        var p = _open(track.Path);
                        p.Volume = VolumeFor(track.Volume, s);
                        _players[track] = p;
                    }
                    catch (Exception ex) { Log.Warning("LayeredAudio: could not load '{Path}': {Error}", track.Path, ex.Message); }
                }
                Log.Information("LayeredAudio: started with {Count} track(s)", _players.Count);
            }
        }

        public void Stop() { lock (_lock) StopInternal(); }

        public void Restart()
        {
            lock (_lock)
            {
                if (_disposed) return;
                var wasPlaying = _players.Count > 0;
                StopInternal();
                if (wasPlaying || CoreSettings.Current?.AudioLayersEnabled == true) Start();
            }
        }

        public void SetTrackVolumeLive(AudioLayerTrack track, int volumePercent)
        {
            lock (_lock)
                if (_players.TryGetValue(track, out var p)) p.Volume = VolumeFor(volumePercent, CoreSettings.Current);
        }

        public void SetMasterVolumeLive()
        {
            lock (_lock)
                foreach (var (track, p) in _players) p.Volume = VolumeFor(track.Volume, CoreSettings.Current);
        }

        public void ApplyDuck(float amount)
        {
            lock (_lock) { _duckFactor = Math.Clamp(1f - amount, 0f, 1f); SetMasterVolumeLive(); }
        }

        public void ReleaseDuck()
        {
            lock (_lock) { _duckFactor = 1f; SetMasterVolumeLive(); }
        }

        /// <summary>LibVLC volume 0-100 for a track, from WPF's linear gain chain.</summary>
        private int VolumeFor(int trackPercent, AppSettings s)
        {
            var gain = Math.Clamp(trackPercent / 100f, 0f, 1f)
                     * Math.Clamp(s.AudioLayersMasterVolume / 100f, 0f, 1f)
                     * Math.Clamp(s.MasterVolume / 100f, 0f, 1f)
                     * _duckFactor;
            return (int)Math.Round(Math.Cbrt(gain) * 100); // same cubic mapping as LibVlcAudio.PlayOneShot
        }

        private void StopInternal()
        {
            foreach (var p in _players.Values) { try { p.Dispose(); } catch { } }
            _players.Clear();
        }

        public void Dispose()
        {
            lock (_lock) { if (_disposed) return; _disposed = true; StopInternal(); }
        }

        /// <summary>App exit: stop every player synchronously (bounded), as WPF disposes its WaveOut.</summary>
        internal void Shutdown()
        {
            Task[] closing;
            lock (_lock)
            {
                closing = _players.Values.OfType<VlcLayerPlayer>().Select(p => p.Closing).ToArray();
                Dispose();
            }
            Task.WaitAll(closing, 2000);
        }

        /// <summary>A LibVLC player that loops its file forever. WPF's LoopingSampleProvider rewinds
        /// with no gap; reopening one player at EOF (Stop/Play) left a gap and opened the new stream
        /// before our volume stuck. So it loops as <see cref="LibVlcAudio"/>'s LoopVoice does: the
        /// next copy starts <see cref="OverlapMs"/> before the current one ends, muted until its
        /// volume sticks.</summary>
        internal sealed class VlcLayerPlayer : ILayerPlayer
        {
            private const int OverlapMs = 120;   // as LoopVoice (WPF CROSSFADE_OVERLAP_SECONDS)
            private sealed class Copy(MediaPlayer player, Media media)
            {
                public readonly MediaPlayer Player = player;
                public readonly Media Media = media;
                public bool Playing, Disposed;
            }

            private readonly LibVLC _vlc;
            private readonly string _path;
            private readonly bool _startMuted;
            private readonly object _gate = new();
            private readonly List<Copy> _live = new();
            private readonly TaskCompletionSource _closed = new();
            private Copy? _newest, _timerFor;
            private readonly TimeProvider _time;
            private ITimer? _next;
            private volatile int _volume;
            private bool _disposed;

            public Task Closing => _closed.Task;
            /// <summary>Copies started so far, and how many of them started with nothing else sounding (a gap).</summary>
            internal int Copies, GapStarts;
            /// <summary>The next pass is armed on the clock (it starts before this one ends, not at EOF).</summary>
            internal bool Armed { get { lock (_gate) return _timerFor != null && _timerFor == _newest; } }

            /// <param name="startMuted">Mute until the first volume sticks, so the stream never opens at
            /// full volume for the moment before Playing (the mantra drone).</param>
            public VlcLayerPlayer(LibVLC vlc, string path, bool startMuted = false, TimeProvider? time = null)
            {
                _time = time ?? TimeProvider.System;
                _vlc = vlc;
                _path = path;
                _startMuted = startMuted;
                StartCopy(after: null);
            }

            /// <summary>Starts the next pass, unless one already followed <paramref name="after"/>.</summary>
            private void StartCopy(Copy? after)
            {
                Copy c;
                lock (_gate)
                {
                    if (_disposed || _newest != after) return;
                    var media = new Media(_vlc, _path, FromType.FromPath);
                    media.AddOption(LibVlcAudio.NoVideo);
                    c = new Copy(new MediaPlayer(media), media);
                    // As in LibVlcAudio: volume set before Playing, or on libvlc's own thread, is lost.
                    c.Player.Playing += (_, _) => ThreadPool.QueueUserWorkItem(_ => OnPlaying(c));
                    c.Player.TimeChanged += (_, e) => { var t = e.Time; ThreadPool.QueueUserWorkItem(_ => Schedule(c, t)); };
                    c.Player.EndReached += (_, _) => ThreadPool.QueueUserWorkItem(_ => Retire(c, failed: false));
                    c.Player.EncounteredError += (_, _) => ThreadPool.QueueUserWorkItem(_ => Retire(c, failed: true));
                    if (_startMuted || Copies > 0) c.Player.Mute = true;
                    if (Copies > 0 && _live.Count == 0) GapStarts++;
                    Copies++;
                    _live.Add(c);
                    _newest = c;
                    if (c.Player.Play()) return;
                }
                Retire(c, failed: true);
            }

            private void OnPlaying(Copy c)
            {
                lock (_gate)
                {
                    if (_disposed || !_live.Contains(c)) return;
                    c.Playing = true;
                }
                Schedule(c, 0);
                lock (c) { if (c.Disposed) return; LibVlcAudio.ApplyPreferredDevice(c.Player); }
                ApplyVolume(c);
            }

            /// <summary>Start the next pass <see cref="OverlapMs"/> before this one ends, measured from its
            /// play position (re-aimed on every TimeChanged), so a slow or fast output clock cannot open a gap.</summary>
            private void Schedule(Copy c, long timeMs)
            {
                lock (_gate)
                {
                    if (_disposed || _newest != c || !c.Playing) return;
                    var length = c.Player.Length;
                    if (length <= 0) return;   // unknown length: Retire restarts at the end
                    var due = length - timeMs - OverlapMs;
                    if (due > 0)
                    {
                        _timerFor = c;
                        (_next ??= _time.CreateTimer(_ => { Copy? f; lock (_gate) f = _timerFor; if (f != null) StartCopy(f); }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan))
                            .Change(TimeSpan.FromMilliseconds(due), Timeout.InfiniteTimeSpan);
                        return;
                    }
                }
                StartCopy(c);
            }

            /// <summary>Runs on the thread pool, never on libvlc's thread. A copy that ends with nothing
            /// else sounding (unknown length) restarts at once; a failed one ends the loop.</summary>
            private void Retire(Copy c, bool failed)
            {
                bool restart;
                lock (_gate)
                {
                    if (!_live.Remove(c)) return;
                    DisposeCopy(c);
                    restart = !_disposed && !failed && _live.Count == 0;
                }
                if (restart) StartCopy(c);
            }

            private static void DisposeCopy(Copy c)
            {
                lock (c)
                {
                    c.Disposed = true;
                    try { c.Player.Stop(); c.Player.Dispose(); c.Media.Dispose(); }
                    catch (Exception ex) { Log.Debug(ex, "LayeredAudio: dispose"); }
                }
            }

            public int Volume
            {
                set
                {
                    Copy[] playing;
                    lock (_gate)
                    {
                        if (_disposed) return;
                        _volume = value;
                        playing = _live.Where(c => c.Playing).ToArray();
                    }
                    foreach (var c in playing) ApplyVolume(c);
                }
            }

            // ponytail: the pulse output drops a volume set too soon after the stream opens, so re-apply
            // until libvlc reports it (bounded, 20 x 25 ms). Never sleeps holding _gate: that would hold
            // up the next pass's start and reopen the gap.
            private void ApplyVolume(Copy c)
            {
                for (var i = 0; i < 20; i++)
                {
                    lock (c)
                    {
                        if (c.Disposed) return;
                        var v = _volume;
                        c.Player.Volume = v;
                        if (c.Player.Volume == v) { c.Player.Mute = false; return; }
                    }
                    Thread.Sleep(25);
                }
                Log.Debug("LayeredAudio: volume {V} did not stick", _volume);
                lock (c) if (!c.Disposed) c.Player.Mute = false;
            }

            public void Dispose()
            {
                // Off the caller's thread: Stop blocks until libvlc tears the output down.
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        lock (_gate)
                        {
                            _disposed = true;
                            _next?.Dispose();
                            foreach (var c in _live) DisposeCopy(c);
                            _live.Clear();
                        }
                    }
                    finally { _closed.TrySetResult(); }
                });
            }
        }
    }
}
