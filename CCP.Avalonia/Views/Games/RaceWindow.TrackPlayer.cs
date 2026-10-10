// PORTED from WPF 7.1.5 Services/Race/TrackPlayer.cs. WPF plays the picked file with NAudio
// (AudioFileReader + WaveOutEvent), which is Windows only; this one plays it through the port's shared
// LibVLC (Platform/LibVlcAudio.Shared), so it runs on Windows and Linux alike. Same face: the app's
// master volume, Play / Pause / Resume / Stop, PositionSec for the 250 ms clock, Ended when the file
// runs out (never for a Stop the run asked for). Also the decoder half WPF gets from NAudio:
// TranscodeToWav writes the 16 kHz mono WAV Core TrackDecoder reads for the chart.
using System;
using System.IO;
using System.Threading;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Race;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>What the race host needs from a player: the clock's transport plus Load and Ended.</summary>
    internal interface IRaceTrackPlayer : ITrackTransport, IDisposable
    {
        /// <summary>Raised once, on the UI thread, when the file plays out to its end.</summary>
        event Action? Ended;

        /// <summary>Open a file for playback, dropping whatever was loaded. Throws on a file (or a
        /// build) that cannot play, so the caller keeps this inside its own try.</summary>
        void Load(string path);
    }

    internal sealed class RaceTrackPlayer : IRaceTrackPlayer
    {
        public event Action? Ended;

        private readonly object _gate = new();
        private Media? _media;
        private MediaPlayer? _player;
        /// <summary>True while a Stop, a restart or a teardown is the reason playback is ending.</summary>
        private volatile bool _stopping;
        private volatile bool _endedFired;
        private volatile bool _disposed;
        private float _gain = 1f;

        /// <summary>The message a pick gets on a machine with no libvlc: plain, and it is the truth.</summary>
        internal const string NoAudioMessage = "audio playback is not available on this machine";

        public double PositionSec
        {
            get { try { return Math.Max(0, (_player?.Time ?? 0) / 1000.0); } catch { return 0; } }
        }

        public double DurationSec
        {
            get
            {
                try
                {
                    long ms = _player?.Length ?? 0;
                    if (ms <= 0) ms = _media?.Duration ?? 0;
                    return Math.Max(0, ms / 1000.0);
                }
                catch { return 0; }
            }
        }

        /// <summary>True only while sound is actually coming out (paused reads false).</summary>
        public bool IsPlaying
        {
            get { try { return _player?.IsPlaying == true; } catch { return false; } }
        }

        public void Load(string path)
        {
            if (_disposed) return;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Track file not found", path ?? "");
            var vlc = LibVlcAudio.Shared ?? throw new InvalidOperationException(NoAudioMessage);
            Unload();
            var media = new Media(vlc, path, FromType.FromPath);
            media.AddOption(LibVlcAudio.NoVideo);
            // The length before the first play: the page shows it on the plate. Local parse only.
            try { _ = media.Parse(MediaParseOptions.ParseLocal); }
            catch (Exception ex) { Log.Debug("RaceTrackPlayer: parse: {E}", ex.Message); }
            var player = new MediaPlayer(media);
            player.EndReached += OnEndReached;
            player.EncounteredError += OnError;
            player.Playing += OnPlaying;
            lock (_gate) { _media = media; _player = player; }
            _stopping = false;
            _endedFired = false;
        }

        /// <summary>Start the track from the beginning, restarting it if it is already running.</summary>
        public void Play()
        {
            var p = _player;
            if (_disposed || p == null) return;
            try
            {
                _stopping = true;                 // the restart's own stop must not read as an ending
                if (p.State is not (VLCState.Stopped or VLCState.NothingSpecial)) p.Stop();
                _stopping = false;
                _endedFired = false;
                if (!p.Play()) Log.Warning("RaceTrackPlayer.Play: the player refused to start");
            }
            catch (Exception ex) { Log.Warning("RaceTrackPlayer.Play: {E}", ex.Message); }
        }

        public void Pause()
        {
            var p = _player;
            if (_disposed || p == null) return;
            try { if (p.IsPlaying) p.SetPause(true); }
            catch (Exception ex) { Log.Warning("RaceTrackPlayer.Pause: {E}", ex.Message); }
        }

        public void Resume()
        {
            var p = _player;
            if (_disposed || p == null) return;
            try { if (p.State == VLCState.Paused) { p.SetPause(false); ApplyVolume(); } }
            catch (Exception ex) { Log.Warning("RaceTrackPlayer.Resume: {E}", ex.Message); }
        }

        /// <summary>End playback for good. Never raises Ended: the run asked for this.</summary>
        public void Stop()
        {
            var p = _player;
            if (p == null) return;
            try
            {
                _stopping = true;
                if (p.State is not (VLCState.Stopped or VLCState.NothingSpecial)) p.Stop();
            }
            catch (Exception ex) { Log.Warning("RaceTrackPlayer.Stop: {E}", ex.Message); }
        }

        public void RefreshVolume() => ApplyVolume();

        /// <summary>The app's master percentage scales the track's own gain (WPF ApplyVolume). LibVLC's
        /// volume is cubic where WPF's is linear sample gain, hence the cube root (as LibVlcAudio).</summary>
        internal static int VlcVolume(int masterPercent, float gain)
        {
            float linear = Math.Clamp(Math.Clamp(masterPercent / 100f, 0f, 1f) * Math.Clamp(gain, 0f, 1f), 0f, 1f);
            return (int)Math.Round(Math.Cbrt(linear) * 100);
        }

        private void ApplyVolume()
        {
            var p = _player;
            if (p == null) return;
            int vol;
            try { vol = VlcVolume(CoreSettings.Current?.MasterVolume ?? 100, _gain); }
            catch { vol = 100; }
            // Off libvlc's own thread, and only once the output exists (a volume set earlier is lost).
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { lock (_gate) if (!_disposed && ReferenceEquals(p, _player) && p.IsPlaying) p.Volume = vol; }
                catch (Exception ex) { Log.Debug("RaceTrackPlayer.ApplyVolume: {E}", ex.Message); }
            });
        }

        private void OnPlaying(object? sender, EventArgs e)
        {
            var p = _player;
            if (p == null) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { lock (_gate) if (!_disposed && ReferenceEquals(p, _player)) LibVlcAudio.ApplyPreferredDevice(p); }
                catch (Exception ex) { Log.Debug("RaceTrackPlayer device: {E}", ex.Message); }
            });
            ApplyVolume();
        }

        private void OnError(object? sender, EventArgs e) => Log.Warning("RaceTrackPlayer: playback stopped with an error");

        /// <summary>LibVLC's event thread. A stop we asked for is not an ending; anything else is the
        /// file running out and raises Ended once, on the UI thread.</summary>
        private void OnEndReached(object? sender, EventArgs e)
        {
            if (_stopping || _endedFired || _disposed) return;
            _endedFired = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;
                try { Ended?.Invoke(); }
                catch (Exception ex) { Log.Warning("RaceTrackPlayer.Ended handler: {E}", ex.Message); }
            });
        }

        /// <summary>Close the player and the media, leaving this reusable through Load.</summary>
        private void Unload()
        {
            _stopping = true;
            MediaPlayer? player; Media? media;
            lock (_gate) { player = _player; media = _media; _player = null; _media = null; }
            if (player == null && media == null) return;
            if (player != null)
            {
                player.EndReached -= OnEndReached;
                player.EncounteredError -= OnError;
                player.Playing -= OnPlaying;
            }
            // Never stop or dispose a player on a libvlc event thread, and never make the UI wait on it.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { player?.Stop(); } catch { }
                try { player?.Dispose(); } catch (Exception ex) { Log.Debug("RaceTrackPlayer.Unload player: {E}", ex.Message); }
                try { media?.Dispose(); } catch (Exception ex) { Log.Debug("RaceTrackPlayer.Unload media: {E}", ex.Message); }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unload();
            Ended = null;
        }

        // ============================ the decoder ============================

        /// <summary>The sout chain that writes 16 kHz mono signed 16-bit WAV to <paramref name="dest"/>.
        /// Forward slashes and an escaped quote, so a Windows path survives libvlc's option parser.</summary>
        internal static string SoutFor(string dest) =>
            ":sout=#transcode{vcodec=none,acodec=s16l,channels=1,samplerate=16000}:std{access=file,mux=wav,dst='"
            + dest.Replace('\\', '/').Replace("'", "\\'") + "'}";

        /// <summary>Core TrackDecoder.ToWavProvider: decode any file libvlc reads to the WAV the chart
        /// is analysed from. Runs as fast as the decoder goes (a sout chain has no clock), reports the
        /// position as progress, and stops at once on cancel. Throws when the file will not decode.</summary>
        internal static void TranscodeToWav(string source, string dest, IProgress<double>? progress, CancellationToken ct)
        {
            var vlc = LibVlcAudio.Shared ?? throw new InvalidOperationException(NoAudioMessage);
            using var done = new ManualResetEventSlim(false);
            bool failed = false;
            using var media = new Media(vlc, source, FromType.FromPath);
            media.AddOption(SoutFor(dest));
            media.AddOption(":no-sout-video");
            media.AddOption(":sout-keep");
            using var player = new MediaPlayer(media);
            player.EndReached += (_, _) => done.Set();
            player.EncounteredError += (_, _) => { failed = true; done.Set(); };
            player.PositionChanged += (_, e) => { try { progress?.Report(Math.Clamp(e.Position, 0f, 1f)); } catch { } };
            if (!player.Play()) throw new InvalidDataException("the track would not open");
            try
            {
                // An hour of audio decodes in well under a minute; ten minutes is a wedged decoder.
                var limit = DateTime.UtcNow.AddMinutes(10);
                while (!done.Wait(100))
                {
                    ct.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow > limit) throw new TimeoutException("the track took too long to decode");
                }
            }
            finally
            {
                // The muxer patches the WAV header and closes the file on stop.
                try { player.Stop(); } catch (Exception ex) { Log.Debug("RaceTrackPlayer.Transcode stop: {E}", ex.Message); }
            }
            if (failed) throw new InvalidDataException("the track would not decode");
        }
    }
}
