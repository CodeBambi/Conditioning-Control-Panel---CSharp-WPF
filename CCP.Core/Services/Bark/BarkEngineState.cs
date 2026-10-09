using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Bark
{
    /// <summary>
    /// Port of WPF 7.1.5 <c>Services/Bark/BarkState.cs</c> (the bark engine's counters), with the WPF
    /// <c>SessionEngine</c> reads behind one snapshot seam so it lives in Core. Named apart from the WPF
    /// type so the WPF head (which references Core) never sees two BarkStates.
    /// </summary>
    public sealed class BarkEngineState
    {
        /// <summary>Live session read: running, elapsed seconds, phase index, pause count, planned minutes.
        /// Unseeded = no session engine (never running).</summary>
        public Func<(bool Running, double ElapsedSec, int PhaseIndex, int Pauses, double PlannedMin)>? SessionProbe;

        private readonly object _lock = new();

        private (bool Running, double ElapsedSec, int PhaseIndex, int Pauses, double PlannedMin) Probe()
        {
            try { return SessionProbe?.Invoke() ?? default; } catch { return default; }
        }

        public bool SessionRunning => Probe().Running;
        public double SessionElapsedSeconds { get { var p = Probe(); return p.Running ? p.ElapsedSec : 0; } }
        public int SessionPhaseIndex { get { var p = Probe(); return p.Running ? p.PhaseIndex : -1; } }
        public int PauseCount { get { var p = Probe(); return p.Running ? p.Pauses : 0; } }
        public double SessionPlannedMinutes { get { var p = Probe(); return p.Running ? p.PlannedMin : 0; } }

        private long _blinkCount;
        public long BlinkCount => System.Threading.Interlocked.Read(ref _blinkCount);
        public void RegisterBlink() => System.Threading.Interlocked.Increment(ref _blinkCount);

        private DateTime? _faceLostSinceUtc;
        public void FaceLost() { lock (_lock) { _faceLostSinceUtc ??= DateTime.UtcNow; } }
        public void FaceFound() { lock (_lock) { _faceLostSinceUtc = null; } }
        public double FaceLostSeconds
        {
            get { lock (_lock) return _faceLostSinceUtc.HasValue ? (DateTime.UtcNow - _faceLostSinceUtc.Value).TotalSeconds : 0; }
        }

        private readonly List<DateTime> _modSwitches = new();
        public void RegisterModSwitch()
        {
            lock (_lock)
            {
                _modSwitches.Add(DateTime.UtcNow);
                if (_modSwitches.Count > 64) _modSwitches.RemoveRange(0, _modSwitches.Count - 64);
            }
        }
        public int ModSwitchesWithin(TimeSpan window) => CountWithin(_modSwitches, window);

        private readonly List<DateTime> _avatarClicks = new();
        public void RegisterAvatarClick()
        {
            lock (_lock)
            {
                _avatarClicks.Add(DateTime.UtcNow);
                if (_avatarClicks.Count > 256) _avatarClicks.RemoveRange(0, _avatarClicks.Count - 256);
            }
        }
        public int AvatarClicksWithin(TimeSpan window) => CountWithin(_avatarClicks, window);

        private int CountWithin(List<DateTime> list, TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            int n = 0;
            lock (_lock)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] >= cutoff) n++;
                    else break;
                }
            }
            return n;
        }

        private int _settingsChangedThisSession;
        public int SettingsChangedThisSession => System.Threading.Volatile.Read(ref _settingsChangedThisSession);
        private long _lastSetupActionTicks;
        public void MarkSettingChanged()
        {
            System.Threading.Interlocked.Increment(ref _settingsChangedThisSession);
            System.Threading.Interlocked.Exchange(ref _lastSetupActionTicks, DateTime.UtcNow.Ticks);
        }
        public double SetupIdleSeconds
        {
            get
            {
                var ticks = System.Threading.Interlocked.Read(ref _lastSetupActionTicks);
                return ticks == 0 ? 999999 : (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
            }
        }

        public double DaysAwayAtLaunch { get; private set; }
        public bool InstantRelaunch { get; private set; }

        public void CaptureLaunchRecency(DateTime? lastSeenUtc, double instantThresholdSeconds = 90)
        {
            if (!lastSeenUtc.HasValue) { DaysAwayAtLaunch = 0; InstantRelaunch = false; return; }
            var gap = DateTime.UtcNow - lastSeenUtc.Value;
            DaysAwayAtLaunch = Math.Max(0, gap.TotalDays);
            InstantRelaunch = gap.TotalSeconds >= 0 && gap.TotalSeconds <= instantThresholdSeconds;
        }

        private readonly HashSet<int> _crossedMarathon = new();
        public bool MarkMarathonCrossed(int thresholdSeconds) { lock (_lock) return _crossedMarathon.Add(thresholdSeconds); }

        private volatile string _currentPhaseName = "";
        public string CurrentPhaseName => _currentPhaseName;
        public void SetPhase(string? name) => _currentPhaseName = name ?? "";
        public bool CurrentPhaseIsDeepener => _currentPhaseName.IndexOf("deep", StringComparison.OrdinalIgnoreCase) >= 0;

        private DateTime? _unfocusedSinceUtc;
        private int _refocusCount;
        public int RefocusCount => System.Threading.Volatile.Read(ref _refocusCount);
        public void RegisterUnfocus() { lock (_lock) { _unfocusedSinceUtc ??= DateTime.UtcNow; } }
        public double RegisterRefocus(double minAwaySeconds = 20)
        {
            lock (_lock)
            {
                if (!_unfocusedSinceUtc.HasValue) return -1;
                var away = (DateTime.UtcNow - _unfocusedSinceUtc.Value).TotalSeconds;
                _unfocusedSinceUtc = null;
                if (away < minAwaySeconds) return -1;
                _refocusCount++;
                return away;
            }
        }

        public void ResetSessionScoped()
        {
            lock (_lock)
            {
                _crossedMarathon.Clear();
                _faceLostSinceUtc = null;
                _unfocusedSinceUtc = null;
                _refocusCount = 0;
            }
            _currentPhaseName = "";
        }
    }
}
