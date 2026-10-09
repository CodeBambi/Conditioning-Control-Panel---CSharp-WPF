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
            var audio = new Vlc(new MediaPlayer(media), media);
            audio.Hook();
            return audio;
        }

        private sealed class Vlc(MediaPlayer player, Media media) : IDeeperLocalAudio
        {
            private long _pendingMs;
            private int _disposed;

            public event Action? Ended;
            // The parse misreads some LAME/Xing mp3 headers (measured: a 6.0 s ffmpeg mp3 parsed as
            // 12.1 s); the playing player's Length is exact, so it wins once known.
            public double DurationSeconds
            {
                get
                {
                    var len = player.State is VLCState.Playing or VLCState.Paused ? player.Length : -1;
                    return (len > 0 ? len : media.Duration) / 1000.0;
                }
            }

            public double PositionSeconds
            {
                get
                {
                    var t = player.State is VLCState.Playing or VLCState.Paused ? player.Time : -1;
                    return (t >= 0 ? t : _pendingMs) / 1000.0;
                }
                set
                {
                    _pendingMs = (long)(Math.Max(0, value) * 1000);
                    if (player.State is VLCState.Playing or VLCState.Paused) player.Time = _pendingMs;
                }
            }

            public void Play()
            {
                if (player.State == VLCState.Paused) { player.SetPause(false); return; }
                player.Play();
            }

            public void Pause()
            {
                if (player.State == VLCState.Playing) player.SetPause(true);
            }

            internal void Hook()
            {
                player.Playing += (_, _) => ThreadPool.QueueUserWorkItem(_ =>
                {
                    // Not inside libvlc's own event: device and seek only stick from another thread.
                    lock (player)
                    {
                        if (_disposed != 0) return;
                        LibVlcAudio.ApplyPreferredDevice(player);
                        if (_pendingMs > 0) player.Time = _pendingMs;
                    }
                });
                // WPF OnVlcEndReached: Stop + rewind so the next Play replays instead of no-oping.
                player.EndReached += (_, _) => ThreadPool.QueueUserWorkItem(_ =>
                {
                    lock (player) { if (_disposed != 0) return; player.Stop(); _pendingMs = 0; }
                    Ended?.Invoke();
                });
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                Ended = null;
                // Never dispose a player from inside its own libvlc event: that deadlocks.
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { lock (player) { player.Stop(); player.Dispose(); } media.Dispose(); }
                    catch (Exception ex) { Serilog.Log.Debug(ex, "DeeperEditor: local audio dispose"); }
                });
            }
        }
    }
}
