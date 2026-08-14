using System;
using System.Collections.Generic;
using System.Windows.Threading;
using NAudio.Wave;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// "Mantra Chant" — loops the active mod's VOICED mantra clips back-to-back as ambient audio.
    ///
    /// Where <see cref="MantraVoiceService"/> serves a single spoken mantra for the Takeover mic
    /// mechanic, this runs each mantra as an unattended call-and-response — her ask, a beat to say it
    /// into, then her affirmation — on repeat with a configurable gap, so the user can chant along
    /// hands-free. It reuses <see cref="MantraVoiceService"/> for clip selection + path resolution and
    /// copies the chained WaveOutEvent + reader loop from <see cref="Deeper.EnhancementAudioPlayer"/>
    /// (the next clip is queued on PlaybackStopped).
    ///
    /// Scoped to voiced entries only: a mantra with no resolvable clip is skipped, and a mod that
    /// ships no voiced mantras at all leaves the loop dark (see <see cref="CanChant"/>), so the
    /// feature never loops silence.
    ///
    /// #685 rules, learned the hard way:
    ///  • it plays the pair, never the mic mechanic's "say it with me" ask on its own — looping the
    ///    ask means she demands a response, never hears one, and never acknowledges it;
    ///  • "Mute avatar" silences it like any other line of hers (see <see cref="IsCompanionMuted"/>);
    ///  • it never auto-starts — App.OnStartup deliberately leaves it off (see App.xaml.cs), and
    ///    panic disarms the persisted flag via <see cref="StopAndDisarm"/>.
    /// </summary>
    public sealed class MantraChantService : IDisposable
    {
        private readonly object _lock = new();
        private WaveOutEvent? _output;
        private AudioFileReader? _reader;
        private DispatcherTimer? _gapTimer;
        private DispatcherTimer? _muteWatch;
        // Clips still owed for the mantra in flight (her affirmation, once the ask has played).
        // UI-thread only, like _gapTimer — every path that touches it runs on the dispatcher.
        private readonly Queue<string> _pending = new();
        private bool _mutedIdle;   // parked because she's muted — resume the moment she isn't
        private bool _running;
        private bool _disposed;

        // How often the mute watch re-checks. Cheap (a bool read), and half a second is fast enough
        // that "mute" feels immediate without polling the audio device.
        private static readonly TimeSpan MuteWatchInterval = TimeSpan.FromSeconds(0.5);

        // Between her ask and her affirmation: a beat to actually say the mantra into. Deliberately
        // NOT the configured gap — that one is the space between mantras. Drop this to zero for
        // back-to-back delivery.
        private static readonly TimeSpan PairBeat = TimeSpan.FromSeconds(2.5);

        /// <summary>True while the chant loop is active.</summary>
        public bool IsRunning { get { lock (_lock) return _running; } }

        /// <summary>
        /// True when the active mod has at least one voiced mantra we can actually chant. Keyed off
        /// promptAudio, which is the first half of every pair (see <see cref="PickClips"/>) — a cheap
        /// check that doesn't disturb the Takeover mechanic's no-repeat rotation.
        /// </summary>
        public bool CanChant() => App.MantraVoice?.HasVoicedMantras() == true;

        /// <summary>Start the loop. No-ops if already running or the active mod has nothing voiced.</summary>
        public void Start()
        {
            if (_disposed) return;
            lock (_lock)
            {
                if (_running) return;
                if (App.MantraVoice?.HasVoicedMantras() != true)
                {
                    App.Logger?.Information("MantraChantService: no voiced mantras for the active mod — chant stays off.");
                    return;
                }
                _running = true;
                _mutedIdle = false;
            }
            App.Logger?.Information("MantraChantService: chant started.");
            StartMuteWatch();
            PlayNext();
        }

        /// <summary>Stop the loop and release the output device. Safe to call when already stopped.</summary>
        public void Stop()
        {
            bool wasActive;
            lock (_lock)
            {
                wasActive = _running || _output != null || _gapTimer != null || _muteWatch != null;
                _running = false;
                _mutedIdle = false;
            }
            if (!wasActive) return;

            // Audio teardown FIRST and inline. Panic reaches us off-thread, and both timer stops
            // below have to hop the dispatcher — a wedged UI thread must never be the reason she
            // keeps talking. DisposeOutput unsubscribes before it stops, so nothing re-queues.
            DisposeOutput();
            _pending.Clear(); // never resume into the back half of a mantra from a previous run
            StopGapTimer();
            StopMuteWatch();
            App.Logger?.Debug("MantraChantService: chant stopped.");
        }

        /// <summary>
        /// Panic-grade stop: kills the loop AND clears the persisted opt-in (#685). Panic used to only
        /// pause the chant, so <c>MantraChantEnabled</c> survived and it came back on the next launch —
        /// panic has to mean "this is over", not "see you after the reboot".
        /// </summary>
        public void StopAndDisarm()
        {
            Stop();
            try
            {
                var s = App.Settings?.Current;
                if (s?.MantraChantEnabled == true)
                {
                    s.MantraChantEnabled = false;
                    App.Settings?.Save();
                    App.Logger?.Information("MantraChantService: chant disarmed — MantraChantEnabled cleared.");
                }
            }
            catch (Exception ex) { App.Logger?.Debug("MantraChantService.StopAndDisarm error: {Error}", ex.Message); }
        }

        /// <summary>
        /// Panic FALLBACK entry point, callable from any thread. Same runtime teardown as
        /// <see cref="Stop"/> — audio down inline first, timers routed through the dispatcher — but
        /// it deliberately writes NO persisted setting. <see cref="StopAndDisarm"/> is the user's
        /// panic and clearing <c>MantraChantEnabled</c> is the point of it; an automatic path
        /// (watchdog, KillAllAudio) firing that same method silently wiped the user's opt-in on a
        /// false alarm, so the automatic fallbacks call this instead.
        /// </summary>
        public void StopEmergency()
        {
            Stop();
            App.Logger?.Debug("MantraChantService: emergency stop — persisted MantraChantEnabled left untouched.");
        }

        /// <summary>Push the current MantraChantVolume onto a clip that's already playing (live slider drag).</summary>
        public void ApplyVolume()
        {
            try { if (_reader != null) _reader.Volume = EffectiveVolume(); }
            catch (Exception ex) { App.Logger?.Debug("MantraChantService.ApplyVolume error: {Error}", ex.Message); }
        }

        // Chant volume folded with the app master volume so a global mute silences the chant too.
        // Muting her zeroes it outright: nothing opens at audible volume while she's muted, and a
        // slider drag mid-mute can't sneak the level back up. (Tearing the clip down is the mute
        // watch's job — this just makes every path that sets a volume agree with it.)
        private static float EffectiveVolume()
        {
            var s = App.Settings?.Current;
            return ResolveVolume(IsCompanionMuted(), s?.MantraChantVolume ?? 50, s?.MasterVolume ?? 100);
        }

        /// <summary>
        /// The volume decision itself, pure so the mute gate is testable without WPF or a sound card.
        /// Muted wins over both sliders — the chant is her voice, not an effect (#685).
        /// </summary>
        public static float ResolveVolume(bool companionMuted, double chantVolume, double masterVolume)
        {
            if (companionMuted) return 0f;
            return (float)Math.Clamp((chantVolume / 100.0) * (masterVolume / 100.0), 0.0, 1.0);
        }

        /// <summary>
        /// The clip choice itself, pure: the ask (which carries the mantra phrase) first, then her
        /// affirmation. Either half may be missing — a set that voices only one still chants that one,
        /// so we never sit silent while a clip exists. Empty only when the entry is text-only, which
        /// is what lets the loop self-heal to off. See <see cref="PickClips"/> for the why.
        /// </summary>
        public static IReadOnlyList<string> ResolveClipSequence(string? promptPath, string? responsePath)
        {
            var seq = new List<string>(2);
            if (!string.IsNullOrEmpty(promptPath)) seq.Add(promptPath);
            if (!string.IsNullOrEmpty(responsePath)) seq.Add(responsePath);
            return seq;
        }

        /// <summary>
        /// True while the companion is muted. The chant is HER voice, so "Mute avatar" has to silence
        /// it (#685 — neither mute could touch it). The tube's live flag is the truth while the window
        /// exists; the persisted <c>AvatarMuted</c> setting covers the window not being up yet.
        /// </summary>
        private static bool IsCompanionMuted()
        {
            try
            {
                var tube = App.AvatarWindow;
                if (tube != null) return tube.IsMuted;
            }
            catch { }
            return App.Settings?.Current?.AvatarMuted == true;
        }

        // The mute toggles live on the avatar window and don't notify us, so the loop watches instead:
        // drop the clip the moment she's muted, pick back up when she isn't. Runs only while chanting.
        private void StartMuteWatch()
        {
            if (_disposed || _muteWatch != null) return;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            _muteWatch = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = MuteWatchInterval };
            _muteWatch.Tick += OnMuteWatchTick;
            _muteWatch.Start();
        }

        // Both timer stops below clear the FIELD first and only then hop the dispatcher for the
        // .Stop() itself. DispatcherTimer.Stop() throws VerifyAccess off the UI thread, and doing it
        // the other way round meant the throw ate the null assignment with it — the field kept
        // pointing at a timer that was still enabled (same hazard OnPlaybackStopped calls out).
        // DispatcherHelper.RunOnUI is a BeginInvoke, so it is safe from any thread and runs inline
        // when we are already on the UI thread.
        private void StopGapTimer()
        {
            var timer = _gapTimer;
            _gapTimer = null;
            if (timer == null) return;
            Helpers.DispatcherHelper.RunOnUI(() => { try { timer.Stop(); } catch { } });
        }

        private void StopMuteWatch()
        {
            var timer = _muteWatch;
            _muteWatch = null;
            if (timer == null) return;
            timer.Tick -= OnMuteWatchTick;   // a plain event; no thread affinity, unlike Stop()
            Helpers.DispatcherHelper.RunOnUI(() => { try { timer.Stop(); } catch { } });
        }

        private void OnMuteWatchTick(object? sender, EventArgs e)
        {
            if (_disposed) return;
            lock (_lock) { if (!_running) return; }

            if (IsCompanionMuted())
            {
                // Nothing queued and nothing playing ⇒ already parked, so don't log every tick.
                if (_output == null && _gapTimer == null) return;
                App.Logger?.Debug("MantraChantService: companion muted — dropping the chant clip.");
                StopGapTimer();
                DisposeOutput(); // unsubscribes first, so this doesn't re-queue through OnPlaybackStopped
                _pending.Clear(); // both halves go — she doesn't come back mid-mantra
                lock (_lock) _mutedIdle = true;
                return;
            }

            bool parked;
            lock (_lock) { parked = _mutedIdle; _mutedIdle = false; }
            if (parked)
            {
                App.Logger?.Debug("MantraChantService: companion unmuted — resuming the chant.");
                PlayNext();
            }
        }

        // Play the next clip: the rest of the mantra in flight if there is one, otherwise a freshly
        // picked mantra's pair. If nothing resolves after a bounded number of picks the set is
        // effectively text-only, so the loop self-heals to off.
        private void PlayNext()
        {
            if (_disposed) return;
            lock (_lock) { if (!_running) return; }

            // Muted ⇒ don't open a device at all; drop any half-finished pair and park, then let the
            // mute watch restart us on a whole mantra when she's unmuted.
            if (IsCompanionMuted())
            {
                _pending.Clear();
                lock (_lock) _mutedIdle = true;
                return;
            }

            string? path = _pending.Count > 0 ? _pending.Dequeue() : null;
            for (int i = 0; i < 12 && string.IsNullOrEmpty(path); i++)
            {
                var entry = App.MantraVoice?.NextMantra();
                if (entry == null) break;
                foreach (var clip in PickClips(entry)) _pending.Enqueue(clip);
                if (_pending.Count > 0) path = _pending.Dequeue();
            }

            if (string.IsNullOrEmpty(path))
            {
                App.Logger?.Information("MantraChantService: no voiced clip resolved — stopping.");
                Stop();
                return;
            }

            // The output circuit breaker is open (endpoint dead / Windows audio restarting) —
            // opening a device now would block on the audio-service RPC and re-arm the cooldown
            // on every gap. Stay silent and come back on the next wait (#778/#779).
            if (App.Audio?.IsOutputSuppressed == true)
            {
                _pending.Clear();      // don't resume half a mantra minutes later
                ScheduleNext();
                return;
            }

            try
            {
                DisposeOutput(); // release the previous clip before opening the next

                _reader = new AudioFileReader(path) { Volume = EffectiveVolume() };
                _output = new WaveOutEvent { DesiredLatency = 200 };
                App.Audio?.ApplyPreferredDevice(_output);
                _output.Init(_reader);
                _output.PlaybackStopped += OnPlaybackStopped;
                _output.Play();
                App.Audio?.NoteOutputSuccess();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MantraChantService.PlayNext failed for {Path} — moving on after the next wait.", path);
                DisposeOutput();
                App.Audio?.NoteOutputFailure("mantra-chant", ex.Message);
                ScheduleNext(); // don't wedge the loop on one bad clip
            }
        }

        /// <summary>
        /// The clips for one mantra, in play order.
        ///
        /// #685: this used to play <c>promptAudio</c> alone, which is the Takeover mic mechanic's
        /// "say it with me sweetie~ good girls don't think" ASK. On a loop that's incoherent by
        /// construction — she demands a response, never hears one, and never acknowledges it. But
        /// <c>responseAudio</c> alone is no better: it's the affirmation ("mmm right? thinking's all
        /// yucky... let it go~") and the mantra phrase itself lives only inside the ask. So the chant
        /// plays both — she asks, leaves you a beat to say it, then affirms you.
        /// </summary>
        private static IReadOnlyList<string> PickClips(Models.MantraEntry entry)
            => ResolveClipSequence(App.MantraVoice?.ResolveAudio(entry.PromptAudio),
                                   App.MantraVoice?.ResolveAudio(entry.ResponseAudio));

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            // NAudio raises this on its WaveOut callback thread; marshal to the UI thread before we
            // touch the DispatcherTimer (Stop off-thread silently corrupts it) — same as EnhancementAudioPlayer.
            var ex = e.Exception;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            void Handle()
            {
                if (ex != null)
                    App.Logger?.Warning(ex, "MantraChantService: clip stopped with error");
                bool run;
                lock (_lock) run = _running;
                if (run) ScheduleNext();
            }

            if (dispatcher.CheckAccess()) Handle();
            else { try { dispatcher.BeginInvoke((Action)Handle); } catch { } }
        }

        // Wait, then play the next clip. Single-shot timer on the UI thread. Mid-mantra that wait is
        // the short answer beat; between mantras it's the user's MantraChantGapSeconds.
        private void ScheduleNext()
        {
            if (_disposed) return;
            lock (_lock) { if (!_running) return; }

            StopGapTimer();
            var wait = _pending.Count > 0
                ? PairBeat
                : TimeSpan.FromSeconds(Math.Clamp(App.Settings?.Current?.MantraChantGapSeconds ?? 5, 0, 60));
            // The tick captures its OWN timer: a stop that already replaced the field must not have
            // the newly installed timer nulled out from under it by an older tick draining late.
            var timer = new DispatcherTimer { Interval = wait };
            timer.Tick += (_, _) =>
            {
                try { timer.Stop(); } catch { }   // on the UI thread here, so Stop() is safe inline
                if (ReferenceEquals(_gapTimer, timer)) _gapTimer = null;
                PlayNext();
            };
            _gapTimer = timer;
            timer.Start();
        }

        // Tear down the current output + reader. Unsubscribe FIRST so our own Stop() doesn't re-enter
        // OnPlaybackStopped and re-queue a clip.
        private void DisposeOutput()
        {
            try
            {
                if (_output != null)
                {
                    _output.PlaybackStopped -= OnPlaybackStopped;
                    _output.Stop();
                    _output.Dispose();
                    _output = null;
                }
                _reader?.Dispose();
                _reader = null;
            }
            catch (Exception ex) { App.Logger?.Debug("MantraChantService.DisposeOutput error: {Error}", ex.Message); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
