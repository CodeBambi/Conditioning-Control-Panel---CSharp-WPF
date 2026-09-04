using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The subliminal seam: the half of <c>SubliminalService</c> that DECIDES, plus the one
    /// delegate a head fills to actually draw.
    ///
    /// <para>A subliminal show is two separable things. Deciding - how long until the next one,
    /// which phrase, is the feature even on - is arithmetic over <see cref="CoreSettings"/> and a
    /// clock, so it lives here and every head gets the same rhythm. Drawing is a full-screen
    /// click-through layered surface, per-monitor DPI and Win32 ex-styles, so it stays in the head
    /// behind <see cref="ShowProvider"/>.</para>
    ///
    /// <para><b>The whole 1,493-line service could not move.</b> A dry-run <c>git mv</c> into Core
    /// produced 68 declaration-pass errors before the compiler reached a single method body -
    /// <c>Window</c>, <c>Grid</c>, <c>Canvas</c>, <c>Color</c>, <c>DispatcherTimer</c>,
    /// <c>System.Windows.Forms.Screen</c>, <c>NAudio</c>, <c>Compositor.SubliminalLayer</c>,
    /// <c>AudioPlaybackHandle</c>. So this is an extraction, not a move: the WPF service keeps its
    /// surface and delegates the schedule and the phrase pick here, and there is exactly one copy
    /// of each rule.</para>
    ///
    /// <para><b>Unseeded is a supported state.</b> With no <see cref="ShowProvider"/> the scheduler
    /// still runs, still picks phrases and still logs - it simply draws nothing. That is honest:
    /// the Avalonia head has no subliminal surface yet, and a feature that decides correctly and
    /// draws nothing is what it should report. Nothing here throws and nothing returns a null a
    /// caller would dereference.</para>
    ///
    /// <para>Volatile for the reason <see cref="CoreMods"/> gives: heads seed on the startup thread
    /// while the timer callback reads from the thread pool.</para>
    /// </summary>
    public static class CoreSubliminal
    {
        /// <summary>
        /// Draw one subliminal for this phrase. The head owns everything visual: the audio lookup,
        /// the haptic anticipation delay, the per-screen surfaces and the fade envelope. Null when
        /// no head has attached a surface; the scheduler then runs silently.
        /// </summary>
        public static volatile Action<string>? ShowProvider;

        /// <summary>
        /// The scheduler armed (true) or disarmed (false). A head hangs its transition work off
        /// this: the WPF head fires its EMI Desk moment on the way up and, on the way down, blanks
        /// the keep-alive windows, pulls hosted cards off the shared host and stops the whisper
        /// audio. Without it a checkbox toggle would silence the schedule and leave a card on
        /// screen with the whisper still playing.
        ///
        /// <para>Fired AFTER <see cref="IsRunning"/> already reads the new value, so a handler that
        /// calls back into <see cref="Start"/>/<see cref="Stop"/> is a no-op rather than a loop.</para>
        /// </summary>
        public static volatile Action<bool>? RunStateChanged;

        private static readonly object Gate = new();
        private static readonly Random Rng = new();
        private static Timer? _timer;
        private static volatile bool _isRunning;

        /// <summary>True while the ambient scheduler is armed.</summary>
        public static bool IsRunning => _isRunning;

        /// <summary>
        /// Seconds until the next subliminal for a given rate, with the randomness supplied by the
        /// caller so the rule can be checked without a clock. <paramref name="roll"/> is a uniform
        /// [0,1). The original: 60/frequency, jittered +-30%, floored at one second.
        /// </summary>
        public static double NextIntervalSeconds(int frequency, double roll)
        {
            var freq = Math.Max(1, frequency);
            var baseInterval = 60.0 / freq;
            var variance = baseInterval * 0.3;
            return Math.Max(1, baseInterval + (roll * variance * 2 - variance));
        }

        /// <summary>
        /// One phrase from the enabled half of the user's pool, or null when nothing is enabled -
        /// which the caller treats as "no show this tick", exactly as the WPF original did.
        /// </summary>
        public static string? PickPhrase()
        {
            List<string> active;
            try
            {
                active = CoreSettings.Current.SubliminalPool
                    .Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
            }
            catch { return null; }

            if (active.Count == 0)
            {
                Log.Debug("No active subliminal texts");
                return null;
            }
            lock (Gate) return active[Rng.Next(active.Count)];
        }

        /// <summary>Arm the ambient scheduler. Idempotent.</summary>
        public static void Start()
        {
            lock (Gate)
            {
                if (_isRunning) return;
                _isRunning = true;
                _timer ??= new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
                ArmLocked();
            }
            Log.Information("SubliminalService started");
            Notify(true);
        }

        /// <summary>Disarm the ambient scheduler. Idempotent; the head still owns taking any card
        /// that is already on screen back off it.</summary>
        public static void Stop()
        {
            lock (Gate)
            {
                if (!_isRunning && _timer == null) return;
                _isRunning = false;
                try { _timer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
            }
            Log.Information("SubliminalService stopped");
            Notify(false);
        }

        /// <summary>
        /// The single authority for toggling the feature from any UI entry point. Persists the flag
        /// and, only while the engine is actually running, starts or stops the scheduler - so a
        /// checkbox and a popup that mirror each other cannot churn Start/Stop between them.
        /// </summary>
        public static void SetEnabled(bool on)
        {
            var s = CoreSettings.Current;
            if (s.SubliminalEnabled != on)
                s.SubliminalEnabled = on;

            if (CoreSession.IsEngineRunning)
            {
                if (on && !_isRunning) Start();
                else if (!on && _isRunning) Stop();
            }

            CoreSettings.Save();
            Log.Information("Subliminals toggled: {Enabled}", on);
        }

        /// <summary>A head whose teardown throws must not take the schedule with it.</summary>
        private static void Notify(bool running)
        {
            try { RunStateChanged?.Invoke(running); }
            catch (Exception ex) { Log.Debug("Subliminal run-state handler failed: {Error}", ex.Message); }
        }

        /// <summary>Caller holds <see cref="Gate"/>. Schedules exactly one tick.</summary>
        private static void ArmLocked()
        {
            if (!_isRunning || !CoreSettings.Current.SubliminalEnabled) return;
            var seconds = NextIntervalSeconds(CoreSettings.Current.SubliminalFrequency, Rng.NextDouble());
            try { _timer?.Change(TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan); } catch { }
        }

        /// <summary>
        /// Thread-pool tick. WPF ran the whole flash on a DispatcherTimer, i.e. on the UI thread,
        /// so the body hops back through <see cref="CoreDispatch"/> - unseeded that runs in place.
        /// The posted body re-checks running/enabled because a Stop can land between the post and
        /// the hop.
        /// </summary>
        private static void OnTick(object? _)
        {
            CoreDispatch.Post(() =>
            {
                if (!_isRunning || !CoreSettings.Current.SubliminalEnabled) return;

                var text = PickPhrase();
                if (text != null)
                {
                    // A head whose surface throws must not kill the schedule.
                    try { ShowProvider?.Invoke(text); }
                    catch (Exception ex) { Log.Debug("Subliminal show provider failed: {Error}", ex.Message); }
                }

                lock (Gate) ArmLocked();
            });
        }
    }
}
