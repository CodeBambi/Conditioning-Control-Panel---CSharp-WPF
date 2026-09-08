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
        // Source events fire on assorted threads (webcam tracking is off the UI thread) while the
        // matcher reads this state under BarkService's own, different lock. This lock makes the
        // mutable collections/counters here internally consistent so a concurrent write+read can't
        // tear a counter or throw "Collection was modified".
        private readonly object _lock = new();

        // --- session engine (MainWindow-owned, attached late) for live elapsed/phase reads ---
        private volatile SessionEngine? _engine;

        public void AttachSessionEngine(SessionEngine engine) => _engine = engine;

        public bool SessionRunning => _engine?.IsRunning == true;
        public double SessionElapsedSeconds => _engine?.IsRunning == true ? _engine.ElapsedTime.TotalSeconds : 0;
        public int SessionPhaseIndex => _engine?.IsRunning == true ? _engine.CurrentPhaseIndex : -1;

        // --- cumulative blink tally (WebcamTracking.OnBlink) ---
        private long _blinkCount;
        public long BlinkCount => System.Threading.Interlocked.Read(ref _blinkCount);
        public void RegisterBlink() => System.Threading.Interlocked.Increment(ref _blinkCount);

        // --- prolonged face-lost timer (OnFaceLost / OnFaceFound) ---
        private DateTime? _faceLostSinceUtc;
        public void FaceLost() { lock (_lock) { _faceLostSinceUtc ??= DateTime.UtcNow; } }
        public void FaceFound() { lock (_lock) { _faceLostSinceUtc = null; } }
        public double FaceLostSeconds
        {
            get
            {
                lock (_lock)
                    return _faceLostSinceUtc.HasValue ? (DateTime.UtcNow - _faceLostSinceUtc.Value).TotalSeconds : 0;
            }
        }

        // --- rapid mod-switch window (ModService.ModChanged) ---
        private readonly List<DateTime> _modSwitches = new();
        public void RegisterModSwitch()
        {
            lock (_lock)
            {
                _modSwitches.Add(DateTime.UtcNow);
                if (_modSwitches.Count > 64) _modSwitches.RemoveRange(0, _modSwitches.Count - 64);
            }
        }
        public int ModSwitchesWithin(TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            int n = 0;
            lock (_lock)
            {
                for (int i = _modSwitches.Count - 1; i >= 0; i--)
                {
                    if (_modSwitches[i] >= cutoff) n++;
                    else break;
                }
            }
            return n;
        }

        // --- rapid avatar-click window (AvatarTube/MainWindow click handler) ---
        private readonly List<DateTime> _avatarClicks = new();
        public void RegisterAvatarClick()
        {
            lock (_lock)
            {
                _avatarClicks.Add(DateTime.UtcNow);
                if (_avatarClicks.Count > 256) _avatarClicks.RemoveRange(0, _avatarClicks.Count - 256);
            }
        }
        public int AvatarClicksWithin(TimeSpan window)
        {
            var cutoff = DateTime.UtcNow - window;
            int n = 0;
            lock (_lock)
            {
                for (int i = _avatarClicks.Count - 1; i >= 0; i--)
                {
                    if (_avatarClicks[i] >= cutoff) n++;
                    else break;
                }
            }
            return n;
        }

        // --- setup-screen state for the anticipatory SessionSetupReady bark (item E) ---
        // Counts setting changes since app-open and timestamps the last setup action so the detector
        // can fire when the user "goes quiet" and expose setup_idle_sec for the stall variant.
        private int _settingsChangedThisSession;
        public int SettingsChangedThisSession => System.Threading.Volatile.Read(ref _settingsChangedThisSession);
        private long _lastSetupActionTicks; // DateTime.UtcNow.Ticks of last setup action (0 = none yet)
        public void MarkSettingChanged()
        {
            System.Threading.Interlocked.Increment(ref _settingsChangedThisSession);
            System.Threading.Interlocked.Exchange(ref _lastSetupActionTicks, DateTime.UtcNow.Ticks);
        }
        /// <summary>Seconds since the last setup action, or a large sentinel if none yet.</summary>
        public double SetupIdleSeconds
        {
            get
            {
                var ticks = System.Threading.Interlocked.Read(ref _lastSetupActionTicks);
                return ticks == 0 ? 999999 : (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
            }
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
        public bool MarkMarathonCrossed(int thresholdSeconds)
        {
            lock (_lock) return _crossedMarathon.Add(thresholdSeconds);
        }

        // --- current session phase name (for "deepener" conditions on non-phase events) ---
        private volatile string _currentPhaseName = "";
        public string CurrentPhaseName => _currentPhaseName;
        public void SetPhase(string? name) => _currentPhaseName = name ?? "";
        public bool CurrentPhaseIsDeepener =>
            _currentPhaseName.IndexOf("deep", StringComparison.OrdinalIgnoreCase) >= 0;

        // --- pause / planned length, live off the engine ---
        public int PauseCount => _engine?.IsRunning == true ? _engine.PauseCount : 0;
        public double SessionPlannedMinutes => _engine?.IsRunning == true ? (_engine.CurrentSession?.DurationMinutes ?? 0) : 0;

        // --- refocus: main window lost focus during a session and got it back ---
        // A "refocus" only counts when the window was gone for at least the threshold, so an
        // accidental click on another monitor does not read as "you tabbed away".
        private DateTime? _unfocusedSinceUtc;
        private int _refocusCount;
        public int RefocusCount => System.Threading.Volatile.Read(ref _refocusCount);

        public void RegisterUnfocus()
        {
            lock (_lock) { _unfocusedSinceUtc ??= DateTime.UtcNow; }
        }

        /// <summary>Returns the seconds the window was away, or -1 when nothing counts as a refocus.</summary>
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

        /// <summary>Reset per-session state (called on session start).</summary>
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
