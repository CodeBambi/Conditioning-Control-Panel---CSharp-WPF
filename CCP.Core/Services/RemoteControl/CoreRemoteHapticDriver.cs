using System;
using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel.Services.Haptics.Core;
using Serilog;

namespace ConditioningControlPanel.Services.Remote
{
    /// <summary>
    /// WPF Services/Remote/RemoteHapticDriver.cs (main 719ed9ca5) on a <see cref="TimeProvider"/> instead of a
    /// DispatcherTimer: plays <c>haptic_pattern</c> / <c>haptic_level</c> through the haptic mixer as transient
    /// pulses (master intensity, cap and premium gate apply inside the mixer); a stop cancels ours only.
    /// Timing lives in <see cref="RemoteHapticPlayer"/>. Thread-safe: commands arrive on the UI thread, ticks
    /// on the timer's.
    /// </summary>
    internal sealed class CoreRemoteHapticDriver
    {
        private const int TickMs = 100;

        private readonly RemoteHapticPlayer _player = new();
        private readonly List<HapticSequence> _live = new();
        private readonly Func<double> _scale;
        private readonly TimeProvider _time;
        private readonly object _gate = new();
        private ITimer? _timer;

        /// <summary>Test seam: where pulses go. Default: the connected toy's mixer, if haptics are on.</summary>
        internal Func<IReadOnlyList<HapticPulseStep>, HapticSequence?> Sink = steps =>
            CoreSettings.Current.Haptics is { Enabled: true } && CoreHaptics.Service is { IsConnected: true } h
                ? h.Mixer.Play(steps) : null;

        public CoreRemoteHapticDriver(Func<double> scale, TimeProvider? time = null)
            => (_scale, _time) = (scale, time ?? TimeProvider.System);

        public bool IsPlaying { get { lock (_gate) return _player.IsPlaying; } }
        public bool IsLooping { get { lock (_gate) return _player.Plan?.Loop == true; } }
        public RemoteHapticPlan? Plan { get { lock (_gate) return _player.Plan; } }
        public int CurrentLevel { get { lock (_gate) return _player.LevelAt(Now); } }

        private long Now => _time.GetUtcNow().ToUnixTimeMilliseconds();

        public void Play(RemoteHapticPlan plan)
        {
            lock (_gate)
            {
                var old = _live.ToArray();
                _live.Clear();
                Submit(_player.Start(plan, Now, _scale()));
                // New pulses first, old ones cancelled after (WPF): no zero tick between the two.
                foreach (var s in old) s.Cancel();
                Arm();
            }
        }

        /// <summary>Every controller command counts as activity for the loop idle cap.</summary>
        public void NoteCommand() { lock (_gate) _player.NoteCommand(Now); }

        public void Stop() { lock (_gate) Halt(); }

        private void Halt()
        {
            _player.Stop();
            foreach (var s in _live) s.Cancel();
            _live.Clear();
            _timer?.Dispose();
            _timer = null;
        }

        private void Arm()
        {
            _timer?.Dispose();
            _timer = _time.CreateTimer(_ => OnTick(), null, TimeSpan.FromMilliseconds(TickMs), Timeout.InfiniteTimeSpan);
        }

        private void OnTick()
        {
            lock (_gate)
            {
                if (!_player.IsPlaying) return;
                try
                {
                    _live.RemoveAll(s => s.Completion.IsCompleted);
                    var t = _player.Tick(Now, _scale());
                    if (t.Queue.Count > 0) Submit(t.Queue);
                    if (t.Ended)
                    {
                        Log.Information("[RemoteControl] Remote haptic ended ({Reason})", t.EndReason);
                        Halt();
                        return;
                    }
                    Arm();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[RemoteControl] Remote haptic tick failed");
                    Halt();
                }
            }
        }

        private void Submit(IReadOnlyList<RemoteHapticRun> runs)
        {
            if (runs.Count == 0) return;
            var now = Now;
            var steps = new HapticPulseStep[runs.Count];
            for (int i = 0; i < runs.Count; i++)
            {
                var r = runs[i];
                steps[i] = new HapticPulseStep((int)Math.Max(0, r.StartMs - now),
                    new HapticPulse(r.Intensity, 0, r.DurationMs, 0, r.Priority));
            }
            if (Sink(steps) is { } seq) _live.Add(seq);
        }
    }
}
