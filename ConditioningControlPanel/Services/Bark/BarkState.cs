using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Bark
{
    /// <summary>
    /// In-memory (session-scoped) counter/state layer for bark conditions the app does
    /// not already expose as events or readable singletons — cumulative blink tally,
    /// prolonged face-lost timer, rapid mod-switch window, days-away/instant-relaunch.
    /// Fed by <see cref="BarkService"/> handlers; read at evaluate time to stamp a
    /// <see cref="BarkContext"/>. No persistence (PR1 is dry-run only).
    /// </summary>
    public class BarkState
    {
        // --- session engine (MainWindow-owned, attached late) for live elapsed/phase reads ---
        private SessionEngine? _engine;

        public void AttachSessionEngine(SessionEngine engine) => _engine = engine;

        public bool SessionRunning => _engine?.IsRunning == true;
        public double SessionElapsedSeconds => _engine?.IsRunning == true ? _engine.ElapsedTime.TotalSeconds : 0;
        public int SessionPhaseIndex => _engine?.IsRunning == true ? _engine.CurrentPhaseIndex : -1;

        // --- cumulative blink tally (WebcamTracking.OnBlink) ---
        public long BlinkCount { get; private set; }
        public void RegisterBlink() => BlinkCount++;

        // --- prolonged face-lost timer (OnFaceLost / OnFaceFound) ---
        private DateTime? _faceLostSinceUtc;
        public void FaceLost() => _faceLostSinceUtc ??= DateTime.UtcNow;
        public void FaceFound() => _faceLostSinceUtc = null;
        public double FaceLostSeconds =>
            _faceLostSinceUtc.HasValue ? (DateTime.UtcNow - _faceLostSinceUtc.Value).TotalSeconds : 0;

        // --- rapid mod-switch window (ModService.ModChanged) ---
        private readonly List<DateTime> _modSwitches = new();
        public void RegisterModSwitch()
        {
            _modSwitches.Add(DateTime.UtcNow);
            if (_modSwitches.Count > 64) _modSwitches.RemoveRange(0, _modSwitches.Count - 64);
        }
        public int ModSwitchesWithin(TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            int n = 0;
            for (int i = _modSwitches.Count - 1; i >= 0; i--)
            {
                if (_modSwitches[i] >= cutoff) n++;
                else break;
            }
            return n;
        }

        // --- days-away / instant-relaunch, computed once from AppSettings.LastSeenUtc ---
        public double DaysAwayAtLaunch { get; private set; }
        public bool InstantRelaunch { get; private set; }

        public void CaptureLaunchRecency(DateTime? lastSeenUtc, double instantThresholdSeconds = 90)
        {
            if (!lastSeenUtc.HasValue) { DaysAwayAtLaunch = 0; InstantRelaunch = false; return; }
            var gap = DateTime.UtcNow - lastSeenUtc.Value;
            DaysAwayAtLaunch = Math.Max(0, gap.TotalDays);
            InstantRelaunch = gap.TotalSeconds >= 0 && gap.TotalSeconds <= instantThresholdSeconds;
        }

        // --- marathon thresholds already announced this session (fire-once-per-session) ---
        private readonly HashSet<int> _crossedMarathon = new();
        public bool MarkMarathonCrossed(int thresholdSeconds) => _crossedMarathon.Add(thresholdSeconds);

        /// <summary>Reset per-session state (called on session start).</summary>
        public void ResetSessionScoped()
        {
            _crossedMarathon.Clear();
            _faceLostSinceUtc = null;
        }
    }
}
