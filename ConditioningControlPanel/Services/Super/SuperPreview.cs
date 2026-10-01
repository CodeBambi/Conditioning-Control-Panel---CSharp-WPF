using System;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// The weekly free Super try, as a thin service over <see cref="SuperPreviewRule"/>. A free
    /// player may run THIS week's effect for 10 seconds, once a week. While it runs,
    /// <see cref="SuperAccess.IsUnlocked"/> and <see cref="SuperAccess.IsOn"/> say yes for that
    /// effect only; when it ends <see cref="SuperAccess.Changed"/> fires and the effect lane tears
    /// down (a fade on a natural end, a cut when <see cref="EndedByPanic"/>).
    /// Panic and the emergency exit end it at once (MainWindow.HandlePanicKeyPress,
    /// EmergencyExitHostService.Open).
    /// </summary>
    public static class SuperPreview
    {
        private static DispatcherTimer? _timer;

        private static SuperEffect? _trying;
        private static long _startedTick;

        /// <summary>Monotonic seconds since the try started (immune to clock changes and resyncs).</summary>
        private static double Elapsed => (Environment.TickCount64 - _startedTick) / 1000.0;

        /// <summary>
        /// The effect being tried right now, or null. Past the try's length plus a second of slack
        /// this reads null even if the end timer has not fired yet (busy UI thread, sleep), so a
        /// late timer can never stretch a free try.
        /// </summary>
        public static SuperEffect? Trying
            => _trying is SuperEffect e && SuperPreviewRule.TryStillRunning(Elapsed) ? e : null;

        /// <summary>Server-clock time the current try started.</summary>
        public static DateTimeOffset StartedAt { get; private set; }

        /// <summary>The last try was cut by panic or the emergency exit, not run out.</summary>
        public static bool EndedByPanic { get; private set; }

        /// <summary>Raised on the UI thread when a try starts or ends.</summary>
        public static event Action? StateChanged;

        private static DateTimeOffset Now => ServerClock.UtcNow;

        private static int UsedWeek => App.Settings?.Current?.SuperPreviewUsedWeek ?? SuperPreviewRule.NeverUsed;

        /// <summary>This week's free effect.</summary>
        public static SuperEffect ThisWeek => SuperPreviewRule.EffectThisWeek(Now);

        /// <summary>When this week's effect swaps for the next.</summary>
        public static DateTimeOffset NextSwap => SuperPreviewRule.NextSwap(Now);

        /// <summary>The try for this week is spent.</summary>
        public static bool UsedThisWeek => SuperPreviewRule.UsedThisWeek(UsedWeek, Now);

        /// <summary>Seconds left in the running try (0 when none runs).</summary>
        public static double SecondsLeft => Trying == null ? 0 : SuperPreviewRule.SecondsLeftAfter(Elapsed);

        /// <summary>A free account that may start a try of <paramref name="effect"/> now.</summary>
        public static bool CanTry(SuperEffect effect)
            => Trying == null && !TierGate.HasPremium && SuperPreviewRule.CanTry(effect, UsedWeek, Now);

        /// <summary>Start this week's try. False when the rule refuses (wrong effect, used, paid tier).</summary>
        public static bool TryStart(SuperEffect effect)
        {
            if (!CanTry(effect)) return false;
            var s = App.Settings?.Current;
            if (s == null) return false;

            // Spend the try BEFORE the effect starts, so a crash or a restart mid-try cannot hand a
            // second one out.
            s.SuperPreviewUsedWeek = SuperPreviewRule.WeekIndex(Now);
            try { App.Settings?.Save(); } catch (Exception ex) { App.Logger?.Debug("SuperPreview: save failed: {E}", ex.Message); }

            _trying = effect;
            _startedTick = Environment.TickCount64;
            StartedAt = Now;
            EndedByPanic = false;
            App.Logger?.Information("SuperPreview: weekly try of {Effect} started", effect);

            _timer?.Stop();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SuperPreviewRule.TrySeconds) };
            _timer.Tick += (_, _) => End(panic: false);
            _timer.Start();

            StateChanged?.Invoke();
            SuperAccess.RaiseChanged(effect);
            return true;
        }

        /// <summary>
        /// End the running try. <paramref name="panic"/> = the panic key or the emergency exit: the
        /// lane must cut, not fade. Safe to call when nothing runs.
        /// </summary>
        public static void End(bool panic)
        {
            _timer?.Stop();
            _timer = null;
            // Read the field, not Trying: a try the backstop already hides must still raise its end.
            if (_trying is not SuperEffect effect) return;
            _trying = null;
            EndedByPanic = panic;
            App.Logger?.Information("SuperPreview: weekly try of {Effect} ended ({How})", effect, panic ? "panic" : "time");
            StateChanged?.Invoke();
            SuperAccess.RaiseChanged(effect);
        }
    }
}
