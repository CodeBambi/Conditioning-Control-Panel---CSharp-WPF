using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// The Avalonia head's mind-wipe service: WPF <c>Services/LockCard/MindWipeService.cs</c> over
    /// <see cref="CoreMindWipe"/>. The deciding half (tick interval, per-tick probability, clip
    /// discovery) is <see cref="MindWipeSchedule"/> in Core; this is the playing half.
    ///
    /// <para>The loop is one player on LibVLC's own repeat instead of WPF's two-player 120 ms
    /// crossfade: the clip restarts in the decoder, so there is no rebuild between cycles to hide.</para>
    /// </summary>
    internal sealed class MindWipePlayer
    {
        /// <summary>One playing clip; volume is 0..1 and can change while it plays.</summary>
        internal interface IVoice : IDisposable { double Volume { set; } }

        private readonly Func<string, double, bool, IVoice?> _play;
        private readonly TimeProvider _time;
        private readonly Func<double> _roll;
        private readonly Random _random = new();
        private readonly object _lock = new();
        private string[] _clips = Array.Empty<string>();
        private ITimer? _tick, _cleanSlate;
        private IVoice? _oneShot, _loop;
        private bool _running;
        private double _frequency = 6, _volume = 0.5;

        /// <summary>Raised with the loop's length in seconds once it has played 60 s (WPF Clean Slate).</summary>
        internal Action<double>? CleanSlate;

        /// <param name="play">Starts a clip (path, 0..1 volume, loop) or returns null when it cannot.</param>
        internal MindWipePlayer(Func<string, double, bool, IVoice?> play, TimeProvider? time = null, Func<double>? roll = null)
        {
            _play = play;
            _time = time ?? TimeProvider.System;
            _roll = roll ?? _random.NextDouble;
            ReloadClips();
        }

        internal bool IsRunning { get { lock (_lock) return _running; } }
        internal bool IsLooping { get { lock (_lock) return _loop != null; } }
        internal int ClipCount { get { lock (_lock) return _clips.Length; } }

        /// <summary>Seeds every <see cref="CoreMindWipe"/> provider with this instance.</summary>
        internal void Seed()
        {
            CoreMindWipe.StartProvider = Start;
            CoreMindWipe.StopProvider = Stop;
            CoreMindWipe.IsRunningProvider = () => IsRunning;
            CoreMindWipe.TriggerOnceProvider = TriggerOnce;
            CoreMindWipe.StartLoopProvider = StartLoop;
            CoreMindWipe.StopLoopProvider = StopLoop;
            CoreMindWipe.IsLoopingProvider = () => IsLooping;
            CoreMindWipe.UpdateSettingsProvider = UpdateSettings;
            CoreMindWipe.ReloadClipsProvider = ReloadClips;
            CoreMindWipe.ClipCountProvider = () => ClipCount;
        }

        internal void ReloadClips()
        {
            var clips = MindWipeSchedule.DiscoverClips(CoreSettings.Current.MindWipeAudioPath);
            lock (_lock) _clips = clips;
        }

        internal void Start(double frequencyPerHour, double volume)
        {
            lock (_lock)
            {
                if (!_running)
                {
                    _running = true;
                    var every = MindWipeSchedule.TickInterval;
                    _tick = _time.CreateTimer(_ => Tick(), null, every, every);
                }
            }
            UpdateSettings(frequencyPerHour, volume);
            Log.Information("MindWipe: Started (frequency: {Freq}/hour, volume: {Vol}%, files: {Count})",
                frequencyPerHour, volume * 100, ClipCount);
        }

        /// <summary>Everything off, running or not: panic reaches a test clip played with the service stopped.</summary>
        internal void Stop()
        {
            IVoice? oneShot;
            ITimer? tick;
            lock (_lock)
            {
                _running = false;
                oneShot = _oneShot; _oneShot = null;
                tick = _tick; _tick = null;
            }
            tick?.Dispose();
            oneShot?.Dispose();
            StopLoop();
        }

        internal void UpdateSettings(double frequencyPerHour, double volume)
        {
            lock (_lock)
            {
                _frequency = MindWipeSchedule.ClampFrequency(frequencyPerHour);
                _volume = MindWipeSchedule.ClampVolume(volume);
                if (_oneShot != null) _oneShot.Volume = _volume;
                if (_loop != null) _loop.Volume = _volume;
            }
        }

        /// <summary>WPF Timer_Tick: no random clips over a loop.</summary>
        private void Tick()
        {
            lock (_lock)
            {
                if (!_running || _loop != null || _clips.Length == 0) return;
                if (_roll() >= MindWipeSchedule.Probability(_frequency)) return;
            }
            PlayOnce();
        }

        /// <summary>WPF TriggerOnce: plays at the saved volume, with the service stopped too.</summary>
        internal void TriggerOnce()
        {
            lock (_lock) _volume = MindWipeSchedule.ClampVolume(CoreSettings.Current.MindWipeVolume / 100.0);
            PlayOnce();
        }

        private void PlayOnce()
        {
            string clip;
            double volume;
            lock (_lock)
            {
                if (_clips.Length == 0) return;
                clip = _clips[_random.Next(_clips.Length)];
                volume = _volume;
            }
            var voice = _play(clip, volume, false);
            IVoice? displaced;
            lock (_lock) { displaced = _oneShot; _oneShot = voice; }
            displaced?.Dispose();
            Log.Debug("MindWipe: Playing {File} at volume {Vol}%", Path.GetFileName(clip), volume * 100);
        }

        internal void StartLoop(double volume)
        {
            StopLoop();
            string clip;
            lock (_lock)
            {
                if (_clips.Length == 0) { Log.Warning("MindWipe: No audio files available for loop"); return; }
                clip = _clips[_random.Next(_clips.Length)];
                _volume = MindWipeSchedule.ClampVolume(volume);
            }
            var voice = _play(clip, _volume, true);
            if (voice == null) return;
            var started = _time.GetTimestamp();
            var check = _time.CreateTimer(_ =>
            {
                lock (_lock) if (!ReferenceEquals(_loop, voice)) return;
                CleanSlate?.Invoke(_time.GetElapsedTime(started).TotalSeconds);
            }, null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);
            lock (_lock) { _loop = voice; _cleanSlate = check; }
            Log.Information("MindWipe: Loop started with {File} at {Vol}% volume", Path.GetFileName(clip), volume * 100);
        }

        internal void StopLoop()
        {
            IVoice? loop;
            ITimer? check;
            lock (_lock) { loop = _loop; _loop = null; check = _cleanSlate; _cleanSlate = null; }
            check?.Dispose();
            loop?.Dispose();
        }
    }
}
