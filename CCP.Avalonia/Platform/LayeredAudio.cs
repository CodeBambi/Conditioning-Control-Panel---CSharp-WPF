using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>A LibVLC player that repeats its file forever (WPF rewinds at EOF).</summary>
        internal sealed class VlcLayerPlayer : ILayerPlayer
        {
            private readonly MediaPlayer _player;
            private readonly Media _media;
            private int _volume;
            private bool _playing;

            public VlcLayerPlayer(LibVLC vlc, string path)
            {
                _media = new Media(vlc, path, FromType.FromPath);
                _media.AddOption(":input-repeat=65535");
                _player = new MediaPlayer(_media);
                // As in LibVlcAudio: volume set before Playing, or on libvlc's own thread, is lost.
                _player.Playing += (_, _) => ThreadPool.QueueUserWorkItem(_ =>
                {
                    lock (this) { _playing = true; _player.Volume = _volume; }
                });
                _player.Play();
            }

            public int Volume
            {
                set { lock (this) { _volume = value; if (_playing) _player.Volume = value; } }
            }

            public void Dispose()
            {
                // Off the caller's thread: Stop blocks until libvlc tears the output down.
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    lock (this) { _playing = false; _player.Stop(); _player.Dispose(); _media.Dispose(); }
                });
            }
        }
    }
}
