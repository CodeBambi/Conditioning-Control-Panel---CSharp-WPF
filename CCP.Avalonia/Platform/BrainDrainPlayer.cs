using System;
using System.IO;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Brain Drain's audio half: WPF Services/LockCard/BrainDrainService.cs over the
    /// <see cref="CoreBrainDrain"/> seam. A timer (5 s, 500 ms in high refresh) rolls
    /// intensity% per minute; a winning roll plays one random clip from assets/braindrain at
    /// master x Brain Drain volume. A clip always plays to its end (#983): a roll mid-clip is skipped.
    /// Plays through the same LibVLC voices as Mind Wipe.
    /// </summary>
    internal sealed class BrainDrainPlayer
    {
        private readonly Func<string, double, bool, Action, MindWipePlayer.IVoice?> _play;
        private readonly TimeProvider _time;
        private readonly Func<double> _roll;
        private readonly Random _random = new();
        private readonly object _lock = new();
        private string[] _clips = Array.Empty<string>();
        private ITimer? _tick;
        private MindWipePlayer.IVoice? _voice;
        private bool _running;
        private double _intensity = 50;
        private TimeSpan _interval = BrainDrainSchedule.TickInterval(false);

        /// <summary>WPF BrainDrainTriggered (avatar/UI notification).</summary>
        internal event Action? Triggered;

        internal BrainDrainPlayer(Func<string, double, bool, Action, MindWipePlayer.IVoice?> play,
            TimeProvider? time = null, Func<double>? roll = null)
        {
            _play = play;
            _time = time ?? TimeProvider.System;
            _roll = roll ?? _random.NextDouble;
            ReloadClips();
        }

        internal bool IsRunning { get { lock (_lock) return _running; } }
        internal int ClipCount { get { lock (_lock) return _clips.Length; } }
        internal double Intensity { get { lock (_lock) return _intensity; } set { lock (_lock) _intensity = BrainDrainSchedule.ClampIntensity(value); } }

        internal void Seed()
        {
            CoreBrainDrain.StartProvider = Start;
            CoreBrainDrain.StopProvider = Stop;
            CoreBrainDrain.IsRunningProvider = () => IsRunning;
            CoreBrainDrain.IntensityProvider = v => Intensity = v;
            CoreBrainDrain.ClipCountProvider = () => ClipCount;
            CoreBrainDrain.ReloadClipsProvider = ReloadClips;
        }

        internal void ReloadClips()
        {
            var clips = BrainDrainSchedule.DiscoverClips();
            lock (_lock) _clips = clips;
        }

        /// <summary>WPF Start: gated on BrainDrainEnabled, rescans the folder every start.</summary>
        internal void Start()
        {
            var s = CoreSettings.Current;
            if (!s.BrainDrainEnabled) { Log.Debug("BrainDrain: Not enabled in settings"); return; }
            lock (_lock) if (_running) return;
            ReloadClips();
            lock (_lock)
            {
                if (_running) return;
                _intensity = BrainDrainSchedule.ClampIntensity(s.BrainDrainIntensity);   // WPF UpdateSettings
                _interval = BrainDrainSchedule.TickInterval(s.BrainDrainHighRefresh);
                _running = true;
                _tick = _time.CreateTimer(_ => Tick(), null, _interval, _interval);
            }
            Log.Information("BrainDrain started at intensity {Intensity}%, mode: {Mode}", _intensity,
                s.BrainDrainHighRefresh ? "High Refresh (500ms)" : "Normal (5s)");
        }

        /// <summary>WPF Stop: audio teardown first and unconditional (panic relies on it).</summary>
        internal void Stop()
        {
            MindWipePlayer.IVoice? voice;
            ITimer? tick;
            lock (_lock)
            {
                _running = false;
                voice = _voice; _voice = null;
                tick = _tick; _tick = null;
            }
            voice?.Dispose();
            tick?.Dispose();
        }

        internal void Tick()
        {
            string clip;
            lock (_lock)
            {
                if (!_running || _clips.Length == 0) return;
                if (_roll() >= BrainDrainSchedule.Probability(_intensity, _interval)) return;
                if (_voice != null) return;   // #983: never displace a playing clip
                clip = _clips[_random.Next(_clips.Length)];
            }
            var s = CoreSettings.Current;
            var volume = BrainDrainSchedule.EffectiveVolume(s.MasterVolume, s.BrainDrainVolume);
            MindWipePlayer.IVoice? voice = null;
            voice = _play(clip, volume, false, () => { lock (_lock) if (ReferenceEquals(_voice, voice)) _voice = null; });
            if (voice == null) return;
            lock (_lock)
            {
                if (!_running) { voice.Dispose(); return; }   // stopped during the build
                _voice = voice;
            }
            Log.Debug("BrainDrain: Playing {File}", Path.GetFileName(clip));
            try { Triggered?.Invoke(); } catch { }
        }
    }
}
