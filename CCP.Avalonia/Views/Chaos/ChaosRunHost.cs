using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// The run-lifecycle slice of WPF <c>ChaosModeService</c> (App.Chaos): StartRun (:297) opens
    /// the HUD and the overlay and runs the countdown; BeginRun starts the 250 ms RunTick (:929),
    /// which steps <see cref="ChaosRunEngine"/> exactly as WPF does; EndRun (:3200) closes the HUD
    /// and shows the recap; RequestStop (:3148), ForceShutdown (:3161), RunAgain and
    /// OnOverlayClosed tear down the same way.
    ///
    /// <para>ponytail: three things are NOT here, so nothing user-facing starts a run yet (the
    /// hub's FALL IN stays a logged no-op): the bubble field (SpawnTick, pops, score), the boon
    /// draft on a wave boundary (BeginWaveTransition rolls straight on, as WPF does with drafts
    /// off), and the payout (AddXP, AwardRunRewards, RevealService.Sync, the rank card) - the
    /// recap shows the computed XP and never saves. A bubble-less run that banked rewards would
    /// diverge from WPF.</para>
    ///
    /// <para><see cref="Tick"/> is the stepped clock: the DispatcherTimer only calls it, tests call
    /// it directly. It does nothing while the overlay is hidden (P01).</para>
    /// </summary>
    internal static class ChaosRunHost
    {
        private static ChaosRunState? _state;
        private static ChaosHudWindow? _hud;
        private static ChaosOverlayWindow? _overlay;
        private static DispatcherTimer? _runTimer;
        private static bool _active, _spawning, _manualPaused;

        internal static ChaosRunState? State => _state;
        internal static ChaosOverlayWindow? Overlay => _overlay;
        internal static ChaosHudWindow? Hud => _hud;
        internal static bool IsActive => _active;
        /// <summary>WPF IsDescending: the clock is running (countdown over, recap not up).</summary>
        internal static bool IsDescending => _spawning;
        internal static bool IsManuallyPaused => _manualPaused;

        public static void StartRun(ChaosRunConfig? config = null, bool isRestart = false)
        {
            if (_active) return;
            var cfg = config ?? ChaosRunConfig.FromSettings();
            try
            {
                _state = new ChaosRunState(cfg);
                _active = true;
                _hud = new ChaosHudWindow(_state);
                _hud.Show();
                _overlay = ChaosOverlayWindow.ForRun();
                _overlay.OnRunAgain = RunAgain;
                _overlay.OnDismissed = OnOverlayClosed;
                _overlay.Show();
                _overlay.ShowCountdown(BeginRun, shortFlash: isRestart);   // 1s flash on RunAgain
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ChaosRunHost.StartRun failed");
                ForceShutdown();
            }
        }

        /// <summary>The countdown's completion. WPF BeginRun also applies the start boon and
        /// lifetime boons and builds the toys; only the clock half is here.</summary>
        internal static void BeginRun()
        {
            if (!_active || _state == null || _spawning) return;
            ChaosRunEffects.ApplyLifetimeBoons(_state);
            _hud?.SetPreRunExpanded(false);
            _hud?.SetClockVisible(_state.ShowWaveTimer);
            _state.StartShields = _state.Shields;
            _spawning = true;
            _runTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ChaosRunEngine.TICK_SEC) };
            _runTimer.Tick += (_, _) => Tick();
            _runTimer.Start();
        }

        /// <summary>One RunTick, the clock half of WPF ChaosModeService.cs:929-1099.</summary>
        internal static void Tick()
        {
            if (!_spawning || _state == null || _manualPaused) return;
            if (_overlay is not { IsVisible: true }) return;   // P01: nothing ticks while hidden
            double elapsed = ChaosRunEngine.Advance(_state);
            var end = ChaosRunEngine.CheckEnd(_state, elapsed);
            if (end == ChaosRunEngine.EndCheck.RunOver) { EndRun(); return; }
            if (end == ChaosRunEngine.EndCheck.Relapse) _state.PushEvent("☠ relapse. one more loop - everything drips double");
            var (newWave, _) = ChaosRunEngine.WaveAt(_state, elapsed);
            if (newWave > _state.WaveIndex)
            {
                // WPF BeginWaveTransition with drafts off (:1449): roll straight into the next wave.
                _state.AllLiveNextWave = false;
                _state.WaveIndex = newWave;
                _state.ActIndex = ChaosRunEngine.ActFor(newWave);
            }
        }

        /// <summary>WPF ToggleManualPause (:257), the clock half: paused holds the clock.</summary>
        public static void ToggleManualPause()
        {
            if (!_spawning) return;
            _manualPaused = !_manualPaused;
            _state?.PushEvent(_manualPaused ? "⏸ held. the hole waits." : "▶ sinking again");
            _hud?.SetPausedUi(_manualPaused);
        }

        public static void RequestStop()
        {
            if (_manualPaused) { _manualPaused = false; _hud?.SetPausedUi(false); }
            if (_spawning) EndRun();
        }

        /// <summary>Panic / app exit: close everything, no recap, no payout. Safe when idle.</summary>
        public static void ForceShutdown()
        {
            if (!_active && _hud == null && _overlay == null) return;
            _spawning = false;
            _runTimer?.Stop();
            if (_overlay != null)
            {
                _overlay.OnDismissed = null;   // avoid re-entrant cleanup
                _overlay.OnRunAgain = null;
                try { _overlay.Close(); } catch (Exception ex) { Log.Debug("Chaos overlay close: {E}", ex.Message); }
            }
            try { _hud?.Close(); } catch (Exception ex) { Log.Debug("Chaos HUD close: {E}", ex.Message); }
            CleanupAfterRun();
        }

        /// <summary>WPF EndRun (:3200) minus the payout: the same XP arithmetic, shown not banked.</summary>
        private static void EndRun()
        {
            if (!_spawning || _state == null) return;
            _spawning = false;
            _runTimer?.Stop();

            double durMin = Math.Max(1, _state.RunDurationSec) / 60.0;
            double capBase = 250.0 * durMin * _state.Config.DifficultyMult;
            double baseXp = Math.Min(_state.Score, capBase);
            double skillMult = _state.SkillMult;
            double finalXp = baseXp * skillMult;
            long previousBest = ChaosMeta.State.BestScore;

            try { _hud?.Close(); } catch (Exception ex) { Log.Debug("Chaos HUD close: {E}", ex.Message); }
            _hud = null;
            _overlay?.ShowResults(_state, baseXp, skillMult, finalXp, previousBest, sparksEarned: 0);
            Log.Information("Chaos run complete (no payout on this head): base {Base:0} x skill {Mult:0.0} = {Final:0} XP",
                baseXp, skillMult, finalXp);
        }

        private static void RunAgain()
        {
            _overlay?.Close();   // OnOverlayClosed -> CleanupAfterRun
            Dispatcher.UIThread.Post(() => StartRun(isRestart: true));
        }

        private static void OnOverlayClosed()
        {
            _spawning = false;
            _runTimer?.Stop();
            try { _hud?.Close(); } catch (Exception ex) { Log.Debug("Chaos HUD close: {E}", ex.Message); }
            CleanupAfterRun();
        }

        private static void CleanupAfterRun()
        {
            _runTimer = null;
            _hud = null;
            _overlay = null;
            _state = null;
            _active = false;
            _spawning = false;
            _manualPaused = false;
        }
    }
}
