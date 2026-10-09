using System;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The plain engine: WPF <c>MainWindow.StartEngine</c> / <c>StopEngine</c>
    /// (ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs:294, :444) for the features
    /// this Core drives - flash, subliminal, bouncing text, bubbles and the lock-card schedule. Saved
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

        /// <summary>When the running engine started (WPF MainWindow._emiEngineStartedUtc); null when stopped.</summary>
        public static DateTime? StartedUtc { get; private set; }

        /// <summary>The head's half of StopEngineCore: close what is on screen (overlays, lock
        /// cards, the pink tint). Runs after every Stop, running or not.</summary>
        public static volatile Action? StoppedHook;

        /// <summary>The head's pop-quiz scheduler (it owns the <see cref="IPopQuizHost"/>); null
        /// on a head with no pop-quiz window.</summary>
        public static volatile PopQuizScheduler? PopQuiz;

        /// <summary>The head's mandatory-video scheduler (it owns the <see cref="IMandatoryVideoHost"/>).</summary>
        public static volatile MandatoryVideoScheduler? Video;

        /// <summary>The head's bubble-count game (it owns the <see cref="IBubbleCountHost"/>).</summary>
        public static volatile BubbleCountScheduler? BubbleCount;

        /// <summary>The head's layered audio bed (WPF App.LayeredAudio.Start(ignoreMasterToggle: true)),
        /// started for an Audio-Only Hypno session (#668); null on a head with no layered audio.</summary>
        public static volatile Action? AudioBedStart;

        /// <summary>Stops the head's layered audio bed (WPF App.LayeredAudio.Stop()).</summary>
        public static volatile Action? AudioBedStop;

        /// <summary>WPF StartEngine's arming matrix, minus the services no head here has.
        /// Not idempotent in TotalSessions, exactly as WPF; callers start only when stopped.</summary>
        public static void Start()
        {
            var s = CoreSettings.Current;
            s.TotalSessions++;
            CoreSettings.Save();

            // #668 Audio-Only Hypno (WPF :304): the visual features sit the session out.
            bool audioOnly = s.AudioOnlySession;
            if (!audioOnly) CoreFlash.Start();   // it checks FlashEnabled itself
            if (!audioOnly && s.SubliminalEnabled) CoreSubliminal.Start();
            if (!audioOnly && s.MandatoryVideosEnabled) Video?.Start();   // WPF StartStop.cs:320
            if (!audioOnly && s.BubblesEnabled) CoreBubbles.Start();   // WPF StartStop.cs:339
            if (!audioOnly && s.BubbleCountEnabled) BubbleCount?.Start();   // WPF StartStop.cs:349
            if (!audioOnly && s.LockCardEnabled) LockCardScheduler.Instance.Start();
            if (!audioOnly && s.PopQuizEnabled) PopQuiz?.Start();   // WPF StartStop.cs:393
            if (!audioOnly && s.BouncingTextEnabled) CoreBouncingText.Start();
            else CoreBouncingText.Stop();   // WPF: clean up any leftover state
            // WPF StartStop.cs:333: the audio-only bed plays the layered tracks regardless of the
            // standalone Audio Layers master toggle.
            if (audioOnly) { try { AudioBedStart?.Invoke(); } catch (Exception ex) { Log.Warning(ex, "Audio-only bed failed to start"); } }
            if (!audioOnly && s.MindWipeEnabled)   // WPF StartStop.cs:366
            {
                CoreMindWipe.Start(s.MindWipeFrequency, s.MindWipeVolume / 100.0);
                if (s.MindWipeLoop) CoreMindWipe.StartLoop(s.MindWipeVolume / 100.0);
            }
            if (!audioOnly && s.BrainDrainEnabled) CoreBrainDrain.Start();   // WPF StartStop.cs:378 (studio#3)

            _running = true;
            StartedUtc = DateTime.UtcNow;
            ConditioningTime.OnEngineStarted(DateTime.Now);   // WPF StartStop.cs:423 StartConditioningTimeTracker
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
                CoreBubbles.Stop();   // WPF StartStop.cs:473, before video
                Video?.Stop();   // WPF StartStop.cs:477
                BubbleCount?.Stop();   // WPF StartStop.cs:488
                BubbleCount?.ForceCleanup();   // WPF StartStop.cs:517-518 ForceCloseAll (panic)
                CoreBouncingText.Stop();
                CoreSubliminal.Stop();
                LockCardScheduler.Instance.Stop();
                PopQuiz?.Stop();   // closes an open quiz (WPF StartStop.cs:492)
                CoreMindWipe.Stop();   // WPF StartStop.cs:489, also ends the loop
                CoreBrainDrain.Stop();   // WPF StartStop.cs:490
                // WPF StartStop.cs:493: an audio-only session force-started the layered bed; stop it on
                // session end unless the standalone Audio Layers master is on (then it keeps playing).
                if (CoreSettings.Current?.AudioLayersEnabled != true)
                {
                    try { AudioBedStop?.Invoke(); } catch (Exception ex) { Log.Warning(ex, "Audio-only bed failed to stop"); }
                }
                _running = false;
                StartedUtc = null;
                ConditioningTime.OnEngineStopped(DateTime.Now);   // WPF StartStop.cs:540 StopConditioningTimeTracker
                try { StoppedHook?.Invoke(); }
                catch (Exception ex) { Log.Warning(ex, "Engine stop hook failed"); }
                CoreTubeEvents.RaiseEngineStopped();   // WPF MainWindow.EngineStopped -> the tube's EngineStop line
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
            if (!s.BubblesEnabled) ApplyLive("bubbles", false);
            if (!s.MandatoryVideosEnabled) ApplyLive("video", false);
            if (!s.BubbleCountEnabled) ApplyLive("bubblecount", false);   // WPF Presets.cs:2261
            CoreMindWipe.ApplyRunRule();   // WPF Presets.cs:2265 (#1304)
        }

        /// <summary>A card or wall toggle already wrote its flag; start or stop the matching
        /// service only while running (WPF FlashFeatureControl.xaml.cs:268, SetWallFeature).</summary>
        public static void ApplyLive(string key, bool on)
        {
            // WPF SetWallFeature "mindwipe": the run rule also stops a loop with the engine off (#1304).
            if (key == "mindwipe") { CoreMindWipe.ApplyRunRule(); return; }
            if (!_running) return;
            switch (key)
            {
                case "flash": if (on) CoreFlash.Start(); else CoreFlash.Stop(); break;
                case "subliminal": if (on) CoreSubliminal.Start(); else CoreSubliminal.Stop(); break;
                case "lockcard": if (on) LockCardScheduler.Instance.Start(); else LockCardScheduler.Instance.Stop(); break;
                case "bouncingtext": if (on) CoreBouncingText.Start(); else CoreBouncingText.Stop(); break;
                case "bubbles": if (on) CoreBubbles.Start(); else CoreBubbles.Stop(); break;
                case "video": if (on) Video?.Start(); else Video?.Stop(); break;   // WPF VideoFeatureControl ChkEnable
                case "bubblecount": if (on) BubbleCount?.Start(); else BubbleCount?.Stop(); break;   // WPF BubbleCountFeatureControl ChkEnable
                case "braindrain": if (on) CoreBrainDrain.Start(); else CoreBrainDrain.Stop(); break;   // WPF BrainDrainFeatureControl ChkEnable
            }
        }
    }
}
