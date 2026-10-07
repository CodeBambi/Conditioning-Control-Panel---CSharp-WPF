using System;
using System.Collections.Generic;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Haptics.Core;

namespace ConditioningControlPanel.Services.Remote
{
    /// <summary>
    /// Plays remote haptics (<c>haptic_pattern</c> / <c>haptic_level</c>) through the haptic mixer
    /// as transient pulses, the way the Back Room and DtRH directors do: the subject's master
    /// intensity, cap and premium gate all apply inside the mixer, and a stop cancels OUR pulses
    /// only. Timing lives in <see cref="RemoteHapticPlayer"/>; this class owns the clock (a 100 ms
    /// UI timer while something plays) and the mixer sequences. UI thread only.
    /// </summary>
    internal sealed class RemoteHapticDriver
    {
        private const int TickMs = 100;

        private readonly RemoteHapticPlayer _player = new();
        private readonly List<HapticSequence> _live = new();
        private readonly Func<double> _scale;
        private DispatcherTimer? _timer;

        public RemoteHapticDriver(Func<double> scale) => _scale = scale;

        /// <summary>Raised when a remote haptic starts or ends.</summary>
        public event EventHandler? Changed;

        public RemoteHapticPlan? Plan => _player.Plan;
        public bool IsPlaying => _player.IsPlaying;
        public bool IsLooping => _player.Plan?.Loop == true;

        /// <summary>Commanded level right now, 0..100, before the Easy factor.</summary>
        public int CurrentLevel => _player.LevelAt(Environment.TickCount64);

        private static long Now => Environment.TickCount64;

        public void Play(RemoteHapticPlan plan)
        {
            var old = _live.ToArray();
            _live.Clear();
            Submit(_player.Start(plan, Now, _scale()));
            // New pulses first, old ones cancelled after: the two priorities take the max for the
            // instant they overlap, where cancel-then-play could drop the toy to zero for a tick.
            foreach (var s in old) s.Cancel();
            EnsureTimer();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>The Easy factor moved: replay what plays at the new strength, from its top.</summary>
        public void Rescale()
        {
            if (_player.Plan is { } plan) Play(plan);
        }

        /// <summary>Every controller command counts as activity for the loop idle cap.</summary>
        public void NoteCommand() => _player.NoteCommand(Now);

        public void Stop()
        {
            var was = _player.IsPlaying || _live.Count > 0;
            Halt();
            if (was) Changed?.Invoke(this, EventArgs.Empty);
        }

        private void Halt()
        {
            _player.Stop();
            foreach (var s in _live) s.Cancel();
            _live.Clear();
            _timer?.Stop();
        }

        private void EnsureTimer()
        {
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMs) };
                _timer.Tick += (_, _) => OnTick();
            }
            if (!_timer.IsEnabled) _timer.Start();
        }

        private void OnTick()
        {
            try
            {
                _live.RemoveAll(s => s.Completion.IsCompleted);
                var t = _player.Tick(Now, _scale());
                if (t.Queue.Count > 0) Submit(t.Queue);
                if (t.Ended)
                {
                    App.Logger?.Information("[RemoteControl] Remote haptic ended ({Reason})", t.EndReason);
                    Halt();
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[RemoteControl] Remote haptic tick failed");
                Stop();
            }
        }

        private void Submit(IReadOnlyList<RemoteHapticRun> runs)
        {
            if (runs.Count == 0) return;
            if (App.Settings?.Current?.Haptics is not { Enabled: true }) return;
            var mixer = App.Haptics is { IsConnected: true } h ? h.Mixer : null;
            if (mixer == null) return;

            var now = Now;
            var steps = new HapticPulseStep[runs.Count];
            for (int i = 0; i < runs.Count; i++)
            {
                var r = runs[i];
                steps[i] = new HapticPulseStep((int)Math.Max(0, r.StartMs - now),
                    new HapticPulse(r.Intensity, 0, r.DurationMs, 0, r.Priority));
            }
            _live.Add(mixer.Play(steps));
        }
    }
}
