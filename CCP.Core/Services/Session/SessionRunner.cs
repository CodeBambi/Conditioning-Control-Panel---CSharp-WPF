using System;
using System.Diagnostics;
using System.Threading;
using ConditioningControlPanel.Helpers;
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
    /// Also pause/resume (100 XP per pause), the flash ramp and the pink tint (ramp + ±3 min random start).
    ///
    /// Pop quiz follows WPF: the user-level AppSettings toggle, not the per-session PopQuiz* fields
    /// (dead in WPF too, BuiltInPrograms.cs:430).
    ///
    /// ponytail: not driven here - video, bubbles, bubble count, mind wipe, brain drain, spiral,
    /// corner GIF, ducking, phase events,
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
        private TimeSpan _pausedElapsed;
        private readonly Random _random = new();

        public SessionLogService SessionLog { get; }
        public Session? CurrentSession { get; private set; }
        public bool IsRunning { get; private set; }
        public int CurrentPhaseIndex { get; private set; }
        public bool IsPaused { get; private set; }
        public int PauseCount { get; private set; }
        public int XPPenalty => PauseCount * SessionXp.PausePenalty;

        /// <summary>SessionEngine._randomizedPinkStartMinute: the preset's start ±3 min (RandomizeStartTimes).</summary>
        public double PinkStartMinute { get; private set; }

        /// <summary>The ramped pink tint in percent once it has started, else null. WPF drives the overlay
        /// with it directly (SetSustainedOverlayOpacity) and never writes PinkFilterOpacity (#471).</summary>
        public double? PinkOpacity { get; private set; }

        /// <summary>After every live tick: WPF's ProgressUpdated, for the head's clock labels.</summary>
        public event Action? Ticked;

        /// <summary>A session ended; the bool is completed. Raised after the runner is idle again.</summary>
        public event Action<Session, bool>? Stopped;

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

        /// <summary>SessionEngine.ElapsedTime: frozen while paused.</summary>
        public TimeSpan Elapsed => !IsRunning ? TimeSpan.Zero
            : IsPaused ? _pausedElapsed
            : SessionClock.Elapsed(_pausedElapsed, _startTime, DateTime.Now, _stopwatch.Elapsed);

        /// <summary>WPF StartSessionAsync (SessionEngine.cs:159-323), after the caller's engine start
        /// (MainWindow.Presets.cs:1619), which this does itself.</summary>
        public void Start(Session session)
        {
            if (IsRunning) throw new InvalidOperationException("A session is already running. Stop it first.");
            if (!CoreEngine.IsRunning) CoreEngine.Start();
            // SessionEngine.cs:223 (#1304): the session owns Mind Wipe, so the global one the engine
            // just started does not play through it. ponytail: the session's own escalating Mind Wipe
            // (StartSession) is not ported; a session plays none.
            CoreMindWipe.Stop();

            var s = CoreSettings.Current;
            CurrentSession = session;
            IsRunning = true;
            CurrentPhaseIndex = 0;
            IsPaused = false;
            PauseCount = 0;
            PinkOpacity = null;
            _pausedElapsed = _lastElapsed = TimeSpan.Zero;
            PinkStartMinute = RandomizedStart(session.Settings.PinkFilterEnabled, session.Settings.PinkFilterStartMinute, _random);
            _startTime = DateTime.Now;
            _stopwatch.Restart();

            // Ledger persisted before any override, so a crash mid-session keeps it (as WPF) without
            // leaking session values to disk. Immediate: a debounced save would serialise the overrides.
            try { s.RecordSessionStart(s.ActiveModId); } catch { }
            CoreSettings.SaveImmediate();

            _snapshot = SessionSettingsSnapshot.Capture(s);
            _custody = PhrasePoolCustody.Begin(s, session.Settings, CoreMods.ActiveModId, session);
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

            // SessionEngine.cs:1627: pop quiz is a user-level toggle (AppSettings), not per-session.
            if (s.PopQuizEnabled) CoreEngine.PopQuiz?.Start();
            else CoreEngine.PopQuiz?.Stop();

            // SessionEngine.cs:1479: a delayed tint stays off until its randomised minute (Tick).
            s.PinkFilterEnabled = ss.PinkFilterEnabled && ss.PinkFilterStartMinute == 0;
            if (s.PinkFilterEnabled) s.PinkFilterOpacity = ss.PinkFilterStartOpacity;
        }

        /// <summary>SessionEngine.RandomizeStartTimes (SessionEngine.cs:966): a delayed start moves by up to 3 min either way.
        /// </summary>
        internal static double RandomizedStart(bool enabled, int minute, Random random) =>
            enabled && minute > 0 ? Math.Max(0, minute + random.NextDouble() * 6 - 3) : minute;

        /// <summary>SessionEngine.PauseSession (SessionEngine.cs:499): freeze the clock, count the pause,
        /// stop the driven services. The lock-card scheduler only - an open card stays (#875).
        /// The head hides the pink tint (WPF App.Overlay.Stop()).</summary>
        public void Pause()
        {
            if (!IsRunning || IsPaused || CurrentSession == null) return;
            _pausedElapsed = Elapsed;
            IsPaused = true;
            PauseCount++;
            _stopwatch.Stop();
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            CoreFlash.Stop();
            CoreSubliminal.Stop();
            LockCardScheduler.Instance.Stop();
            CoreEngine.PopQuiz?.Stop();   // SessionEngine.cs:527, closes an open quiz
            CoreBouncingText.Stop();
            Log.Information("Session paused (pause #{Count}, -100 XP penalty)", PauseCount);
        }

        /// <summary>SessionEngine.ResumeSession (SessionEngine.cs:554): restart only what has reached its
        /// start minute; a still-deferred start fires from Tick as before.</summary>
        public void Resume()
        {
            if (!IsRunning || !IsPaused || CurrentSession is not { } session) return;
            IsPaused = false;
            _startTime = DateTime.Now;
            _stopwatch.Start();
            _timer?.Change(1000, 1000);
            var ss = session.Settings;
            if (ss.FlashEnabled && !_deferred.IsPending("flash")) CoreFlash.Start();
            if (ss.SubliminalEnabled && !_deferred.IsPending("subliminal")) CoreSubliminal.Start();
            if (ss.LockCardEnabled && !_deferred.IsPending("lock cards")) LockCardScheduler.Instance.Start(Remaining.TotalMinutes);
            if (ss.BouncingTextEnabled && !_deferred.IsPending("bouncing text")) CoreBouncingText.Start();
            if (CoreSettings.Current.PopQuizEnabled) CoreEngine.PopQuiz?.Start();   // SessionEngine.cs:574
            Log.Information("Session resumed");
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
            if (!IsRunning || IsPaused || session == null) return;
            _lastElapsed = elapsed;
            var minutes = elapsed.TotalMinutes;
            if (minutes >= session.DurationMinutes) { Stop(true, elapsed); return; }

            var phase = SessionTimeline.PhaseIndexAt(session.Phases, minutes);
            if (phase != CurrentPhaseIndex)
            {
                CurrentPhaseIndex = phase;
                Log.Information("Phase changed: {Phase}", session.Phases[phase].Name);
            }
            UpdateRamps(session, minutes);
            _deferred.FireDue(minutes);

            // Pink delayed start at its randomised minute (SessionEngine.cs:849); the head shows it.
            var s = CoreSettings.Current;
            if (session.Settings.PinkFilterEnabled && !s.PinkFilterEnabled && minutes >= PinkStartMinute)
            {
                s.PinkFilterEnabled = true;
                Log.Information("Pink filter activated at {Minutes:F1} minutes (target was {Target:F1})", minutes, PinkStartMinute);
            }
            Ticked?.Invoke();
        }

        /// <summary>SessionEngine.UpdateRampingValues (SessionEngine.cs:699), flash trio + pink.</summary>
        private void UpdateRamps(Session session, double minutes)
        {
            var ss = session.Settings;
            double total = session.DurationMinutes;
            var curve = ss.RampCurve ?? CoreSettings.Current.RampCurve;
            var progress = RampCurves.ApplyCurve(minutes / total, curve);

            // Parked as a session overlay, never written to the persisted fields (see SetSessionFlashRamp).
            int? opacity = null, frequency = null, scale = null;
            if (ss.FlashEnabled && ss.FlashOpacity != ss.FlashOpacityEnd)
                opacity = (int)Lerp(ss.FlashOpacity, ss.FlashOpacityEnd, progress);
            if (ss.FlashEnabled && ss.FlashPerHour != ss.FlashPerHourEnd)
                frequency = (int)Lerp(ss.FlashPerHour, ss.FlashPerHourEnd, progress);
            if (ss.FlashEnabled && ss.FlashScale != 100) scale = ss.FlashScale;
            CoreSettings.Current.SetSessionFlashRamp(opacity, frequency, scale);

            if (ss.PinkFilterEnabled && minutes >= PinkStartMinute)
            {
                var pink = RampCurves.ApplyCurve((minutes - PinkStartMinute) / (total - PinkStartMinute), curve);
                PinkOpacity = Lerp(ss.PinkFilterStartOpacity, ss.PinkFilterEndOpacity, pink);
            }
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

        public void Stop(bool completed = false) => Stop(completed, Elapsed);

        /// <summary>WPF StopSession (SessionEngine.cs:332-493) + OnSessionStopped's StopEngine.</summary>
        private void Stop(bool completed, TimeSpan elapsed)
        {
            var session = CurrentSession;
            if (!IsRunning || session == null) return;
            IsRunning = IsPaused = false;
            _custody?.Release();
            _stopwatch.Stop();
            _timer?.Dispose();
            _timer = null;
            _deferred.Clear();
            CoreEngine.PopQuiz?.Stop();   // SessionEngine.cs:372

            var s = CoreSettings.Current;
            s.ClearSessionFlashRamp();   // SessionEngine.cs:390, ahead of the restore
            PinkOpacity = null;
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
                var award = SessionXp.Compute(session.BonusXP, PauseCount, s.PlayerLevel, elapsed);
                // Banked = what the ledger actually gained (a head's gate may refuse it, e.g. signed out).
                var before = ProfileAdopt.TotalXp(s);
                CoreProgression.AddXP(award, "Session");
                xp = (int)Math.Round(ProfileAdopt.TotalXp(s) - before);
                Log.Information("Session completed: {Name}, XP: {XP} (banked {Banked}, paused {PauseCount}x, penalty: -{Penalty})",
                    session.Name, award, xp, PauseCount, XPPenalty);
            }
            else Log.Information("Session stopped early");

            try { SessionLog.EndSession(completed, elapsed, xp); }
            catch (Exception ex) { Log.Error(ex, "SessionLog.EndSession failed"); }
            CurrentSession = null;
            // WPF SessionEngine.SessionStopped + SessionCompleted, as one call (ProgramEngineBridge).
            try { Stopped?.Invoke(session, completed); }
            catch (Exception ex) { Log.Warning(ex, "SessionRunner.Stopped handler failed"); }
        }
    }
}
