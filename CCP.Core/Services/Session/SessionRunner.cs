using System;
using System.Diagnostics;
using System.Threading;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// A session run for heads without WPF: a port (not a move) of WPF SessionEngine
    /// (ConditioningControlPanel/Services/Session/SessionEngine.cs) over the pure pieces in SessionRules,
    /// <see cref="SessionSettingsSnapshot"/>, <see cref="PhrasePoolCustody"/>, <see cref="CoreEngine"/> and
    /// <see cref="SessionLogService"/>. Drives flash, subliminal (+ whispers flag), bouncing text and lock cards.
    ///
    /// ponytail: not driven here - video, bubbles, bubble count, pop quiz, mind wipe, brain drain, spiral,
    /// pink tint, corner GIF, ducking, the flash ramp, pause/resume + penalty (U4), phase events,
    /// EMI Desk, Discord, friends, season recap and achievement tracking. No settings are written for them,
    /// so the snapshot restore writes their own values back; each arrives with its Core service.
    /// </summary>
    public sealed class SessionRunner
    {
        private readonly DeferredStartQueue _deferred = new();
        private readonly Stopwatch _stopwatch = new();
        private Timer? _timer;
        private SessionSettingsSnapshot? _snapshot;
        private PhrasePoolCustody? _custody;
        private DateTime _startTime;
        private TimeSpan _lastElapsed;

        public SessionLogService SessionLog { get; }
        public Session? CurrentSession { get; private set; }
        public bool IsRunning { get; private set; }
        public int CurrentPhaseIndex { get; private set; }

        /// <summary>After every live tick: WPF's ProgressUpdated, for the head's clock labels.</summary>
        public event Action? Ticked;

        /// <summary>Seeds IsSessionRunning: one runner per head. The pool delegates are the head's
        /// (PhrasePoolCustody.Seed at startup, CCP.Avalonia/Program.cs).</summary>
        public SessionRunner(SessionLogService sessionLog)
        {
            SessionLog = sessionLog;
            CoreSession.IsSessionRunningProvider = () => IsRunning && CurrentSession != null;
        }

        /// <summary>SessionEngine.RemainingTime.</summary>
        public TimeSpan Remaining => CurrentSession is { } cs && IsRunning
            ? TimeSpan.FromMinutes(cs.DurationMinutes) - Elapsed : TimeSpan.Zero;

        /// <summary>SessionEngine.ElapsedTime (no pause offset until U4).</summary>
        public TimeSpan Elapsed => IsRunning
            ? SessionClock.Elapsed(TimeSpan.Zero, _startTime, DateTime.Now, _stopwatch.Elapsed)
            : TimeSpan.Zero;

        /// <summary>WPF StartSessionAsync (SessionEngine.cs:159-323), after the caller's engine start
        /// (MainWindow.Presets.cs:1619), which this does itself.</summary>
        public void Start(Session session)
        {
            if (IsRunning) throw new InvalidOperationException("A session is already running. Stop it first.");
            if (!CoreEngine.IsRunning) CoreEngine.Start();

            var s = CoreSettings.Current;
            CurrentSession = session;
            IsRunning = true;
            CurrentPhaseIndex = 0;
            _lastElapsed = TimeSpan.Zero;
            _startTime = DateTime.Now;
            _stopwatch.Restart();

            // Ledger persisted before any override, so a crash mid-session keeps it (as WPF) without
            // leaking session values to disk. Immediate: a debounced save would serialise the overrides.
            try { s.RecordSessionStart(s.ActiveModId); } catch { }
            CoreSettings.SaveImmediate();

            _snapshot = SessionSettingsSnapshot.Capture(s);
            _custody = PhrasePoolCustody.Begin(s, session.Settings, CoreMods.ActiveModId);
            Apply(session.Settings, s);

            _timer = new Timer(_ => CoreDispatch.Post(() => Tick(Elapsed)), null, 1000, 1000);
            SessionLog.BeginSession(session);
            Log.Information("Session started: {Name}", session.Name);
        }

        private double RemainingMinutes =>
            Math.Max(0, (CurrentSession?.DurationMinutes ?? 0) - _lastElapsed.TotalMinutes);

        /// <summary>WPF ApplySessionSettings (SessionEngine.cs:1337-1617), driven features only. No end
        /// minute: WPF honours one only for corner GIF and brain drain.</summary>
        private void Apply(SessionSettings ss, AppSettings s)
        {
            _deferred.Clear();

            s.FlashEnabled = ss.FlashEnabled;
            if (ss.FlashEnabled)
            {
                s.FlashFrequency = ss.FlashPerHour;
                s.FlashOpacity = ss.FlashOpacity;
                s.SimultaneousImages = ss.FlashImages;
                s.FlashClickable = ss.FlashClickable;
                s.CorruptionMode = ss.FlashHydra;
                s.FlashAudioEnabled = ss.FlashAudioEnabled;
                StartAt("flash", ss.FlashStartMinute, CoreFlash.Start, CoreFlash.Stop);
            }
            else CoreFlash.Stop();

            // Pools were overridden by PhrasePoolCustody.Begin.
            s.SubliminalEnabled = ss.SubliminalEnabled;
            if (ss.SubliminalEnabled)
            {
                s.SubliminalFrequency = ss.SubliminalPerMin;
                s.SubliminalOpacity = ss.SubliminalOpacity;
                s.SubliminalDuration = ss.SubliminalFrames;
                StartAt("subliminal", ss.SubliminalStartMinute, CoreSubliminal.Start, CoreSubliminal.Stop);
            }
            else CoreSubliminal.Stop();

            // Whispers are flag-driven: a delayed start holds the flag off until its minute.
            s.SubAudioEnabled = ss.AudioWhispersEnabled && ss.AudioWhispersStartMinute == 0;
            if (ss.AudioWhispersEnabled)
            {
                s.SubAudioVolume = ss.WhisperVolume;
                if (ss.AudioWhispersStartMinute > 0)
                    _deferred.Defer("audio whispers", ss.AudioWhispersStartMinute, () => CoreSettings.Current.SubAudioEnabled = true);
            }

            s.BouncingTextEnabled = ss.BouncingTextEnabled;
            CoreBouncingText.Stop();   // WPF stops first to reset state, enabled or not
            if (ss.BouncingTextEnabled)
            {
                s.BouncingTextSpeed = ss.BouncingTextSpeed;
                s.BouncingTextSize = ss.BouncingTextSize;
                s.BouncingTextOpacity = ss.BouncingTextOpacity;
                StartAt("bouncing text", ss.BouncingTextStartMinute, CoreBouncingText.Start, () => { });
            }

            s.LockCardEnabled = ss.LockCardEnabled;
            if (ss.LockCardEnabled)
            {
                if (ss.LockCardFrequency.HasValue) s.LockCardFrequency = ss.LockCardFrequency.Value;
                // #736: the window is read when the start actually fires.
                StartAt("lock cards", ss.LockCardStartMinute,
                    () => LockCardScheduler.Instance.Start(RemainingMinutes), LockCardScheduler.Instance.Stop);
            }
            else LockCardScheduler.Instance.Stop();
        }

        private void StartAt(string name, int minute, Action start, Action stop)
        {
            if (minute == 0) { start(); return; }
            stop();   // the engine may already have it running
            _deferred.Defer(name, minute, start);
        }

        /// <summary>WPF MainTimer_Tick (SessionEngine.cs:609-662): complete, phase, deferred starts.
        /// The timer posts it with the real clock; tests call it with any elapsed.</summary>
        internal void Tick(TimeSpan elapsed)
        {
            var session = CurrentSession;
            if (!IsRunning || session == null) return;
            _lastElapsed = elapsed;
            var minutes = elapsed.TotalMinutes;
            if (minutes >= session.DurationMinutes) { Stop(true, elapsed); return; }

            var phase = SessionTimeline.PhaseIndexAt(session.Phases, minutes);
            if (phase != CurrentPhaseIndex)
            {
                CurrentPhaseIndex = phase;
                Log.Information("Phase changed: {Phase}", session.Phases[phase].Name);
            }
            _deferred.FireDue(minutes);
            Ticked?.Invoke();
        }

        public void Stop(bool completed = false) => Stop(completed, Elapsed);

        /// <summary>WPF StopSession (SessionEngine.cs:332-493) + OnSessionStopped's StopEngine.</summary>
        private void Stop(bool completed, TimeSpan elapsed)
        {
            var session = CurrentSession;
            if (!IsRunning || session == null) return;
            IsRunning = false;
            _custody?.Release();
            _stopwatch.Stop();
            _timer?.Dispose();
            _timer = null;
            _deferred.Clear();

            var s = CoreSettings.Current;
            _snapshot?.RestoreTo(s);
            _snapshot = null;
            _custody?.Restore(s);
            _custody = null;
            CoreEngine.Stop();   // MainWindow.Presets.cs:1951
            CoreSettings.Save();   // WPF leaves the restore to the next save; persist it now

            // XP actually banked: 0 while no head has a progression service (decision log).
            int xp = 0;
            if (completed)
            {
                var award = SessionXp.Compute(session.BonusXP, 0, s.PlayerLevel, elapsed);
                if (CoreProgression.AddXPProvider != null) { CoreProgression.AddXP(award, "Session"); xp = award; }
                Log.Information("Session completed: {Name}, XP: {XP} (banked {Banked})", session.Name, award, xp);
            }
            else Log.Information("Session stopped early");

            try { SessionLog.EndSession(completed, elapsed, xp); }
            catch (Exception ex) { Log.Error(ex, "SessionLog.EndSession failed"); }
            CurrentSession = null;
        }
    }
}
