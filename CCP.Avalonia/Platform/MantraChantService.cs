// PORTED from ConditioningControlPanel/Services/MantraChantService.cs (7.1.5). NAudio's chained WaveOut
// becomes one LibVLC voice per clip (LibVlcAudio.PlayVoice: re-volumed live, stopped by Dispose).
using System;
using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// "Mantra Chant": loops the active mod's VOICED mantra clips as ambient audio, each one as a pair
    /// (her ask, a beat to say it into, her affirmation), then the user's gap, on repeat. Recorded clips
    /// only: a mantra with no clip is skipped and a mod with no voiced mantras leaves the loop dark.
    /// #685 rules: it plays the pair, never the ask alone; "Mute avatar" silences it; it never
    /// auto-starts (the saved switch is cleared on launch); panic disarms the saved switch.
    /// All state is touched on the UI thread.
    /// </summary>
    internal sealed class MantraChantService : IDisposable
    {
        internal static MantraChantService Instance { get; } = new();

        /// <summary>One playing clip: its level can move, and Dispose stops it.</summary>
        internal interface IClip : IDisposable { double Volume { set; } }

        private sealed class VoiceClip : IClip
        {
            private readonly MindWipePlayer.IVoice _voice;
            public VoiceClip(MindWipePlayer.IVoice voice) => _voice = voice;
            public double Volume { set => _voice.Volume = value; }
            public void Dispose() => _voice.Dispose();
        }

        // Seams (tests swap them). The defaults are the real services.
        internal Func<string, double, Action, IClip?> Play = (path, volume, ended) =>
            LibVlcAudio.Instance?.PlayVoice(path, volume, false, ended) is { } v ? new VoiceClip(v) : null;
        internal Func<bool> HasVoiced = () => global::ConditioningControlPanel.Avalonia.App.MantraVoice.HasVoicedMantras();
        internal Func<MantraEntry?> Next = () => global::ConditioningControlPanel.Avalonia.App.MantraVoice.NextMantra();
        internal Func<string?, string?> Resolve = file => global::ConditioningControlPanel.Avalonia.App.MantraVoice.ResolveAudio(file);
        internal Func<Action, TimeSpan, IDisposable> After = (act, wait) => DispatcherTimer.RunOnce(act, wait);

        private readonly Queue<string> _pending = new();   // clips still owed for the mantra in flight
        private IClip? _clip;
        private IDisposable? _gap, _muteWatch;
        private bool _running, _mutedIdle, _disposed;
        private int _generation;

        private static readonly TimeSpan MuteWatchInterval = TimeSpan.FromSeconds(0.5);
        /// <summary>Between her ask and her affirmation: a beat to say the mantra into.</summary>
        internal static readonly TimeSpan PairBeat = TimeSpan.FromSeconds(2.5);

        public bool IsRunning => _running;
        public bool CanChant() { try { return HasVoiced(); } catch { return false; } }

        public void Start()
        {
            if (_disposed || _running) return;
            if (!CanChant())
            {
                Log.Information("MantraChantService: no voiced mantras for the active mod - chant stays off.");
                return;
            }
            _running = true;
            _mutedIdle = false;
            Log.Information("MantraChantService: chant started.");
            ArmMuteWatch();
            PlayNext();
        }

        public void Stop()
        {
            bool wasActive = _running || _clip != null || _gap != null || _muteWatch != null;
            _running = false;
            _mutedIdle = false;
            _generation++;
            if (!wasActive) return;
            DropClip();          // audio first
            _pending.Clear();    // never resume into the back half of a mantra from a previous run
            Drop(ref _gap);
            Drop(ref _muteWatch);
            Log.Debug("MantraChantService: chant stopped.");
        }

        /// <summary>Panic: stop AND clear the saved switch, so panic ends the chant rather than pausing it.</summary>
        public void StopAndDisarm()
        {
            Stop();
            try
            {
                var s = CoreSettings.Current;
                if (s?.MantraChantEnabled == true)
                {
                    s.MantraChantEnabled = false;
                    CoreSettings.Save();
                    Log.Information("MantraChantService: chant disarmed - MantraChantEnabled cleared.");
                }
            }
            catch (Exception ex) { Log.Debug("MantraChantService.StopAndDisarm error: {Error}", ex.Message); }
        }

        /// <summary>WPF App.xaml.cs:2678: the chant starts OFF on every launch, so the switch matches reality.</summary>
        internal static void ClearStaleSwitchOnLaunch()
        {
            try
            {
                var s = CoreSettings.Current;
                if (s == null || !s.MantraChantEnabled) return;
                s.MantraChantEnabled = false;
                CoreSettings.Save();
                Log.Information("Mantra Chant left OFF on startup (it never auto-resumes - #685)");
            }
            catch (Exception ex) { Log.Debug("MantraChant launch clear: {E}", ex.Message); }
        }

        /// <summary>Live-apply the slider to a clip that is already playing.</summary>
        public void ApplyVolume()
        {
            try { if (_clip != null) _clip.Volume = EffectiveVolume(); }
            catch (Exception ex) { Log.Debug("MantraChantService.ApplyVolume error: {Error}", ex.Message); }
        }

        private static double EffectiveVolume()
        {
            var s = CoreSettings.Current;
            return ResolveVolume(IsCompanionMuted(), s?.MantraChantVolume ?? 50, s?.MasterVolume ?? 100);
        }

        /// <summary>Chant volume folded with master; muting her zeroes it outright.</summary>
        public static float ResolveVolume(bool companionMuted, double chantVolume, double masterVolume)
        {
            if (companionMuted) return 0f;
            return (float)Math.Clamp((chantVolume / 100.0) * (masterVolume / 100.0), 0.0, 1.0);
        }

        /// <summary>Her ask then her affirmation; a half with no clip is left out.</summary>
        public static IReadOnlyList<string> ResolveClipSequence(string? promptPath, string? responsePath)
        {
            var seq = new List<string>(2);
            if (!string.IsNullOrEmpty(promptPath)) seq.Add(promptPath);
            if (!string.IsNullOrEmpty(responsePath)) seq.Add(responsePath);
            return seq;
        }

        internal static Func<bool> CompanionMuted = () => CoreSettings.Current?.AvatarMuted == true;
        private static bool IsCompanionMuted() { try { return CompanionMuted(); } catch { return false; } }

        private void ArmMuteWatch()
        {
            if (_disposed || !_running) return;
            Drop(ref _muteWatch);
            _muteWatch = After(() => { _muteWatch = null; OnMuteWatchTick(); ArmMuteWatch(); }, MuteWatchInterval);
        }

        internal void OnMuteWatchTick()
        {
            if (_disposed || !_running) return;
            if (IsCompanionMuted())
            {
                if (_clip == null && _gap == null) return;   // already parked
                Log.Debug("MantraChantService: companion muted - dropping the chant clip.");
                Drop(ref _gap);
                DropClip();
                _pending.Clear();   // both halves go: she does not come back mid-mantra
                _mutedIdle = true;
                return;
            }
            bool parked = _mutedIdle;
            _mutedIdle = false;
            if (parked)
            {
                Log.Debug("MantraChantService: companion unmuted - resuming the chant.");
                PlayNext();
            }
        }

        private void PlayNext()
        {
            if (_disposed || !_running) return;
            if (IsCompanionMuted())
            {
                _pending.Clear();
                _mutedIdle = true;
                return;
            }

            string? path = _pending.Count > 0 ? _pending.Dequeue() : null;
            for (int i = 0; i < 12 && string.IsNullOrEmpty(path); i++)
            {
                MantraEntry? entry;
                try { entry = Next(); } catch { entry = null; }
                if (entry == null) break;
                foreach (var clip in ResolveClipSequence(Resolve(entry.PromptAudio), Resolve(entry.ResponseAudio))) _pending.Enqueue(clip);
                if (_pending.Count > 0) path = _pending.Dequeue();
            }
            if (string.IsNullOrEmpty(path))
            {
                Log.Information("MantraChantService: no voiced clip resolved - stopping.");
                Stop();
                return;
            }

            try
            {
                DropClip();
                int gen = _generation;
                // The voice ends off the UI thread; come back to it, and only for the run that started it.
                _clip = Play(path, EffectiveVolume(), () => Dispatcher.UIThread.Post(() => OnClipEnded(gen)));
                if (_clip == null) ScheduleNext();   // no audio backend or a bad clip: do not wedge the loop
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MantraChantService.PlayNext failed for {Path} - moving on after the next wait.", path);
                DropClip();
                ScheduleNext();
            }
        }

        internal void OnClipEnded(int generation)
        {
            if (generation != _generation || !_running) return;
            _clip = null;
            ScheduleNext();
        }

        internal void OnClipEndedForTest() => OnClipEnded(_generation);

        /// <summary>Mid-mantra the wait is the answer beat; between mantras it is MantraChantGapSeconds.</summary>
        private void ScheduleNext()
        {
            if (_disposed || !_running) return;
            Drop(ref _gap);
            var wait = _pending.Count > 0
                ? PairBeat
                : TimeSpan.FromSeconds(Math.Clamp(CoreSettings.Current?.MantraChantGapSeconds ?? 5, 0, 60));
            LastWait = wait;
            IDisposable? mine = null;
            mine = After(() => { if (ReferenceEquals(_gap, mine)) _gap = null; PlayNext(); }, wait);
            _gap = mine;
        }

        internal TimeSpan LastWait { get; private set; }

        private void DropClip()
        {
            var c = _clip;
            _clip = null;
            _generation++;   // the dropped clip's own end must not queue another
            try { c?.Dispose(); } catch (Exception ex) { Log.Debug("MantraChantService clip dispose: {E}", ex.Message); }
        }

        private static void Drop(ref IDisposable? timer)
        {
            var t = timer;
            timer = null;
            try { t?.Dispose(); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
        }
    }
}
