using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>The editor's local-audio transport: WPF's WaveOutEvent + AudioFileReader pair
    /// (DeeperEditorWindow.xaml.cs:1214) on the process's shared LibVLC. One seam so tests can
    /// drive the editor's play/seek/end path without a sound card.</summary>
    internal interface IDeeperLocalAudio : IDisposable
    {
        double DurationSeconds { get; }
        double PositionSeconds { get; set; }
        void Play();
        void Pause();
        /// <summary>Raised off the UI thread once the clip has played to its end and rewound.</summary>
        event Action? Ended;
    }

    internal static class DeeperLocalAudio
    {
        /// <summary>Null when libvlc did not load or the file will not parse. Tests swap it.</summary>
        internal static Func<string, Task<IDeeperLocalAudio?>> Open { get; set; } = OpenVlcAsync;

        private static async Task<IDeeperLocalAudio?> OpenVlcAsync(string path)
        {
            var vlc = LibVlcAudio.Shared;
            if (vlc == null) return null;
            var media = new Media(vlc, path, FromType.FromPath);
            media.AddOption(LibVlcAudio.NoVideo);
            // AudioFileReader.TotalTime: the parse gives the duration before anything plays.
            var status = await media.Parse(MediaParseOptions.ParseLocal, 5000);
            if (status != MediaParsedStatus.Done || media.Duration <= 0) { media.Dispose(); return null; }
            var mp = new MediaPlayer(media);
            var audio = new Transport(new VlcEngine(mp), media.Duration, () =>
            {
                try { mp.Stop(); mp.Dispose(); media.Dispose(); }
                catch (Exception ex) { Serilog.Log.Debug(ex, "DeeperEditor: local audio dispose"); }
            });
            // Never act inside libvlc's own event (deadlock; device and seek do not stick there).
            mp.Playing += (_, _) => ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (audio.Gate) if (!audio.IsDisposed) LibVlcAudio.ApplyPreferredDevice(mp);
                audio.OnPlaying();
            });
            mp.EndReached += (_, _) => ThreadPool.QueueUserWorkItem(_ => audio.OnEndReached());
            return audio;
        }

        /// <summary>The MediaPlayer members the transport uses; a seam so its state rules are
        /// tested without libvlc or real time.</summary>
        internal interface IEngine
        {
            VLCState State { get; }
            long Time { get; set; }
            long Length { get; }
            void Play();
            void SetPause(bool pause);
            void Stop();
        }

        private sealed class VlcEngine(MediaPlayer p) : IEngine
        {
            public VLCState State => p.State;
            public long Time { get => p.Time; set => p.Time = value; }
            public long Length => p.Length;
            public void Play() => p.Play();
            public void SetPause(bool pause) => p.SetPause(pause);
            public void Stop() => p.Stop();
        }

        /// <summary>Play/pause/seek/end over an <see cref="IEngine"/>. <see cref="OnPlaying"/> and
        /// <see cref="OnEndReached"/> are called off libvlc's thread by the adapter.</summary>
        internal sealed class Transport(IEngine player, long parsedMs, Action release) : IDeeperLocalAudio
        {
            internal readonly object Gate = new();
            private long _pendingMs;   // a seek made before playback could take it; consumed once
            private int _disposed;
            internal bool IsDisposed => _disposed != 0;

            public event Action? Ended;

            private bool Live => player.State is VLCState.Playing or VLCState.Paused;

            // The parse misreads some LAME/Xing mp3 headers (measured: a 6.0 s ffmpeg mp3 parsed as
            // 12.1 s); the playing player's Length is exact, so it wins once known.
            public double DurationSeconds
            {
                get
                {
                    var len = Live ? player.Length : -1;
                    return (len > 0 ? len : parsedMs) / 1000.0;
                }
            }

            public double PositionSeconds
            {
                get
                {
                    lock (Gate)
                    {
                        var t = Live ? player.Time : -1;
                        return (t >= 0 ? t : _pendingMs) / 1000.0;
                    }
                }
                set
                {
                    lock (Gate)
                    {
                        var ms = (long)(Math.Max(0, value) * 1000);
                        if (Live) { player.Time = ms; _pendingMs = 0; }
                        else _pendingMs = ms;
                    }
                }
            }

            public void Play()
            {
                lock (Gate)
                {
                    if (player.State == VLCState.Paused) player.SetPause(false);
                    else player.Play();
                }
            }

            /// <summary>Paused when playing; while the clip is still opening, stopped, so a pause
            /// (or a panic) in that gap cannot be followed by audio starting under a play glyph.</summary>
            public void Pause()
            {
                lock (Gate)
                {
                    if (player.State == VLCState.Playing) player.SetPause(true);
                    else if (player.State != VLCState.Paused) player.Stop();
                }
            }

            /// <summary>Raised on every start AND every resume: a pending seek is applied once.</summary>
            internal void OnPlaying()
            {
                lock (Gate)
                {
                    if (IsDisposed || _pendingMs <= 0) return;
                    player.Time = _pendingMs;
                    _pendingMs = 0;
                }
            }

            /// <summary>WPF OnVlcEndReached: Stop + rewind so the next Play replays.</summary>
            internal void OnEndReached()
            {
                lock (Gate) { if (IsDisposed) return; player.Stop(); _pendingMs = 0; }
                Ended?.Invoke();
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                Ended = null;
                // Never dispose a player from inside its own libvlc event: that deadlocks.
                ThreadPool.QueueUserWorkItem(_ => { lock (Gate) release(); });
            }
        }
    }
}
