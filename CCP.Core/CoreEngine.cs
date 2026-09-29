using System;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The plain engine: WPF <c>MainWindow.StartEngine</c> / <c>StopEngine</c>
    /// (ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs:294, :444) for the features
    /// this Core drives - flash, subliminal, bouncing text and the lock-card schedule. Saved
    /// flags take effect here and only here; a card toggle writes its flag and, only while
    /// running, applies it live (<see cref="ApplyLive"/>), as the WPF cards do.
    ///
    /// <para>The WPF head keeps its own StartEngine (frozen reference); a head that uses this seeds
    /// <c>CoreSession.IsEngineRunningProvider = () => CoreEngine.IsRunning</c> and
    /// <see cref="StoppedHook"/>.</para>
    /// </summary>
    public static class CoreEngine
    {
        private static volatile bool _running;
        private static bool _stopInProgress;

        public static bool IsRunning => _running;

        /// <summary>The head's half of StopEngineCore: close what is on screen (overlays, lock
        /// cards, the pink tint). Runs after every Stop, running or not.</summary>
        public static volatile Action? StoppedHook;

        /// <summary>The head's pop-quiz scheduler (it owns the <see cref="IPopQuizHost"/>); null
        /// on a head with no pop-quiz window.</summary>
        public static volatile PopQuizScheduler? PopQuiz;

        /// <summary>WPF StartEngine's arming matrix, minus the services no head here has.
        /// Not idempotent in TotalSessions, exactly as WPF; callers start only when stopped.</summary>
        public static void Start()
        {
            var s = CoreSettings.Current;
            s.TotalSessions++;
            CoreSettings.Save();

            // #668 Audio-Only Hypno (WPF :304): the visual features sit the session out.
            // ponytail: WPF also starts the layered audio bed (LayeredAudio.Start(ignoreMasterToggle: true));
            // no head here has it in Core yet - add it with the audio-layers port.
            bool audioOnly = s.AudioOnlySession;
            if (!audioOnly) CoreFlash.Start();   // it checks FlashEnabled itself
            if (!audioOnly && s.SubliminalEnabled) CoreSubliminal.Start();
            if (!audioOnly && s.LockCardEnabled) LockCardScheduler.Instance.Start();
            if (!audioOnly && s.PopQuizEnabled) PopQuiz?.Start();   // WPF StartStop.cs:393
            if (!audioOnly && s.BouncingTextEnabled) CoreBouncingText.Start();
            else CoreBouncingText.Stop();   // WPF: clean up any leftover state

            _running = true;
            Log.Information("Engine started - Flash: {Flash}, Subliminal: {Sub}, LockCard: {Lock}, BouncingText: {Bt}",
                s.FlashEnabled, s.SubliminalEnabled, s.LockCardEnabled, s.BouncingTextEnabled);
        }

        /// <summary>WPF StopEngine: stop every driven service whether or not the engine ran (panic
        /// relies on that), then the head hook. Saved flags are never touched.</summary>
        public static void Stop()
        {
            if (_stopInProgress) return;   // WPF #364 re-entrancy guard
            _stopInProgress = true;
            try
            {
                CoreFlash.Stop();
                CoreBouncingText.Stop();
                CoreSubliminal.Stop();
                LockCardScheduler.Instance.Stop();
                PopQuiz?.Stop();   // closes an open quiz (WPF StartStop.cs:492)
                _running = false;
                try { StoppedHook?.Invoke(); }
                catch (Exception ex) { Log.Warning(ex, "Engine stop hook failed"); }
                Log.Information("Engine stopped");
            }
            finally { _stopInProgress = false; }
        }

        /// <summary>WPF ReconcileRunningServices (MainWindow.Presets.cs:2245, #872): after a preset
        /// load, stop every running service whose flag is now off. One-way: never starts one.</summary>
        public static void Reconcile()
        {
            var s = CoreSettings.Current;
            if (!s.FlashEnabled) ApplyLive("flash", false);
            if (!s.SubliminalEnabled) ApplyLive("subliminal", false);
            if (!s.LockCardEnabled) ApplyLive("lockcard", false);
            if (!s.BouncingTextEnabled) ApplyLive("bouncingtext", false);
        }

        /// <summary>A card or wall toggle already wrote its flag; start or stop the matching
        /// service only while running (WPF FlashFeatureControl.xaml.cs:268, SetWallFeature).</summary>
        public static void ApplyLive(string key, bool on)
        {
            if (!_running) return;
            switch (key)
            {
                case "flash": if (on) CoreFlash.Start(); else CoreFlash.Stop(); break;
                case "subliminal": if (on) CoreSubliminal.Start(); else CoreSubliminal.Stop(); break;
                case "lockcard": if (on) LockCardScheduler.Instance.Start(); else LockCardScheduler.Instance.Stop(); break;
                case "bouncingtext": if (on) CoreBouncingText.Start(); else CoreBouncingText.Stop(); break;
            }
        }
    }
}
