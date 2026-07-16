using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Types of autonomous actions the companion can take
    /// </summary>
    public enum AutonomyActionType
    {
        Flash,
        Video,
        Subliminal,
        BrainDrainPulse,
        StartBubbles,
        Comment,
        MindWipe,
        LockCard,
        SpiralPulse,
        PinkFilterPulse,
        BouncingText,
        BubbleCount,
        WebVideo,
        WallpaperShuffle,
        SpokenMantra
    }

    /// <summary>
    /// What triggered the autonomous action
    /// </summary>
    public enum AutonomyTriggerSource
    {
        Idle,
        Random,
        Context,
        TimeOfDay
    }

    /// <summary>
    /// Time-of-day mood affecting behavior style
    /// </summary>
    public enum AutonomyMood
    {
        Gentle,     // Morning - softer, less frequent
        Attentive,  // Afternoon - moderate
        Playful,    // Evening - more active
        Mischievous // Night - most active
    }

    /// <summary>
    /// Event args for when an autonomous action is triggered
    /// </summary>
    public class AutonomyActionEventArgs : EventArgs
    {
        public AutonomyActionType ActionType { get; }
        public AutonomyTriggerSource Source { get; }
        public string? Context { get; }

        public AutonomyActionEventArgs(AutonomyActionType actionType, AutonomyTriggerSource source, string? context = null)
        {
            ActionType = actionType;
            Source = source;
            Context = context;
        }
    }

    /// <summary>
    /// Service that enables autonomous companion behavior.
    /// The avatar can trigger effects on her own based on idle time, random intervals,
    /// context awareness, and time of day.
    /// </summary>
    public partial class AutonomyService : IDisposable
    {
        // Timers
        private DispatcherTimer? _idleTimer;
        private DispatcherTimer? _randomTimer;
        private DispatcherTimer? _cooldownTimer;
        private DispatcherTimer? _heartbeatTimer;
        private bool _forceTestMode = false; // When true, use 30s interval instead of settings

        // When a random tick fires but she can't act yet (cooldown, a fullscreen interaction, or a
        // web video), we re-check after this short window instead of waiting a whole fresh interval —
        // so she acts the moment she's free and the countdown bar stays honest.
        private const double RetryIntervalSeconds = 12;

        // State
        private DateTime _lastActionTime = DateTime.MinValue;
        private DateTime _lastUserActivity = DateTime.Now;
        private bool _isOnCooldown = false;
        private bool _isEnabled = false;
        private bool _disposed = false;
        private readonly Random _random = new();

        // Pulse state tracking - prevent overlapping pulses
        private bool _spiralPulseActive = false;
        private bool _pinkFilterPulseActive = false;
        private bool _bubblesPulseActive = false;
        private bool _bouncingTextPulseActive = false;
        private bool _webVideoActive = false; // Blocks all actions while web video plays fullscreen

        /// <summary>
        /// True while a fullscreen browser/HypnoTube web video is on screen. The mandatory
        /// VideoService scheduler reads this so it won't stack a mandatory video (and its audio)
        /// on top of a web video — and vice versa (BUG-XRFQH4AHDN).
        /// </summary>
        public bool IsWebVideoActive => _webVideoActive;
        private int _webVideoWatchdogGeneration = 0; // Invalidates stale watchdog callbacks
        private HashSet<string> _shownWebVideos = new(); // Track shown videos to avoid repeats
        // Separate generation counters for each pulse type to avoid cross-invalidation
        private int _spiralPulseGeneration = 0;
        private int _pinkFilterPulseGeneration = 0;
        private int _globalPulseGeneration = 0; // Only incremented by Stop() to invalidate ALL pulses

        // Original settings before pulse modifications (for restoration on cancel)
        private bool? _originalSpiralEnabled = null;
        private int? _originalSpiralOpacity = null;
        private bool? _originalPinkFilterEnabled = null;
        private int? _originalPinkFilterOpacity = null;

        // Mood system
        private AutonomyMood _currentMood = AutonomyMood.Playful;

        // Events
        public event EventHandler<AutonomyActionEventArgs>? ActionTriggered;
        public event EventHandler<string>? AnnouncementMade;

        /// <summary>Raised when Takeover arms (true) or disarms (false), so the UI orb/state can follow.</summary>
        public event EventHandler<bool>? EnabledChanged;
        /// <summary>Raised when a "say it for me" voice prompt begins, carrying the target phrase.</summary>
        public event EventHandler<string>? VoicePromptStarted;
        /// <summary>Raised when a voice prompt resolves, carrying its result.</summary>
        public event EventHandler<Services.Speech.PhraseResult>? VoicePromptFinished;

        // Announcement phrases by action type
        private readonly Dictionary<AutonomyActionType, string[]> _announcementPhrases = new()
        {
            { AutonomyActionType.Flash, new[] {
                "Time for a little surprise~",
                "Here comes something pretty!",
                "Look at the screen, good girl~",
                "Ooh, I want to show you something~",
                "Pretty picture time~"
            }},
            { AutonomyActionType.Video, new[] {
                "Video time! Get comfy~",
                "I have something to show you...",
                "Time to watch and absorb~",
                "Sit back and watch~",
                "Let's watch something together~"
            }},
            { AutonomyActionType.Subliminal, new[] {
                "Just a little message for you~",
                "*giggles* Did you see that?",
                "Shhh, just let it sink in~",
                "A little reminder~",
                "Don't think, just absorb~"
            }},
            { AutonomyActionType.BrainDrainPulse, new[] {
                "Let me blur your thoughts~",
                "Time to get fuzzy~",
                "Thinking is overrated~",
                "Let it all go blurry~"
            }},
            { AutonomyActionType.StartBubbles, new[] {
                "Pop pop pop!",
                "Let's play~",
                "Bubble time!",
                "Click the bubbles~"
            }},
            { AutonomyActionType.Comment, new[] {
                "*giggles*",
                "Teehee~",
                "Just thinking about you~"
            }},
            { AutonomyActionType.MindWipe, new[] {
                "Let me wipe your thoughts~",
                "Shhh... empty mind~",
                "No more thinking~",
                "Time to forget~"
            }},
            { AutonomyActionType.LockCard, new[] {
                "Time to earn a reward~",
                "Complete this for me~",
                "Show me how good you are~",
                "Task time~"
            }},
            { AutonomyActionType.SpiralPulse, new[] {
                "Watch the pretty spiral~",
                "Spirals are so pretty...",
                "Look at the swirls~",
                "Round and round~"
            }},
            { AutonomyActionType.PinkFilterPulse, new[] {
                "Everything looks better in pink~",
                "Pink is your color~",
                "So pretty and pink~",
                "Pink thoughts~"
            }},
            { AutonomyActionType.BouncingText, new[] {
                "Read the pretty words~",
                "Follow the bouncing text~",
                "Words to remember~",
                "Let them sink in~"
            }},
            { AutonomyActionType.BubbleCount, new[] {
                "Count with me~",
                "How many bubbles?",
                "Test your focus~",
                "Counting game time~"
            }},
            { AutonomyActionType.WebVideo, new[] {
                "Time to watch something special~",
                "I picked a video just for you~",
                "Sit back and let it sink in~",
                "Watch and absorb~",
                "Fullscreen time~"
            }},
            { AutonomyActionType.WallpaperShuffle, new[] {
                "New scenery for you~",
                "Let me redecorate~",
                "A little change of view~",
                "How about this one?~"
            }}
        };

        public bool IsEnabled => _isEnabled;
        public bool IsIdleTimerRunning => _idleTimer?.IsEnabled == true;
        public bool IsRandomTimerRunning => _randomTimer?.IsEnabled == true;

        // Countdown-bar support: remember when the current random interval will fire and how
        // long that interval was, so the UI can render "fraction of time left" each frame.
        private DateTime? _nextRandomFireTime;
        private double _lastRandomIntervalSeconds;

        /// <summary>
        /// Fraction (0..1) of the current random-interval window still remaining before the next
        /// Takeover action fires. 1.0 = just scheduled, 0.0 = about to fire. Null when the random
        /// timer isn't running (nothing to count down to). Drives the avatar countdown bar.
        /// </summary>
        public double? NextRandomFireFraction
        {
            get
            {
                if (_randomTimer?.IsEnabled != true) return null;
                if (_nextRandomFireTime == null || _lastRandomIntervalSeconds <= 0) return null;
                var remaining = (_nextRandomFireTime.Value - DateTime.Now).TotalSeconds;
                var frac = remaining / _lastRandomIntervalSeconds;
                if (frac < 0) return 0;
                if (frac > 1) return 1;
                return frac;
            }
        }

        /// <summary>
        /// True when autonomy is currently executing an action.
        /// Used by XPContext to give Cult Bunny the +50% bonus.
        /// </summary>
        public bool IsActionInProgress { get; private set; }

        /// <summary>
        /// Get diagnostic status string for debugging
        /// </summary>
        public string GetDiagnosticStatus()
        {
            var settings = App.Settings?.Current;
            var lines = new List<string>
            {
                $"Service Enabled: {_isEnabled}",
                $"Idle Timer Running: {_idleTimer?.IsEnabled == true}",
                $"Random Timer Running: {_randomTimer?.IsEnabled == true}",
                $"On Cooldown: {_isOnCooldown}",
                $"Interaction Queue Busy: {App.InteractionQueue?.IsBusy == true}",
                $"Last Action: {(_lastActionTime == DateTime.MinValue ? "Never" : _lastActionTime.ToString("HH:mm:ss"))}",
                $"Current Mood: {_currentMood}",
                "",
                "Settings:",
                $"  AutonomyModeEnabled: {settings?.AutonomyModeEnabled}",
                $"  AutonomyConsentGiven: {settings?.AutonomyConsentGiven}",
                $"  PlayerLevel: {settings?.PlayerLevel}",
                $"  IdleTriggerEnabled: {settings?.AutonomyIdleTriggerEnabled}",
                $"  RandomTriggerEnabled: {settings?.AutonomyRandomTriggerEnabled}",
                $"  RandomIntervalMinutes: {settings?.AutonomyRandomIntervalMinutes}",
                $"  CooldownSeconds: {settings?.AutonomyCooldownSeconds}",
                "",
                "Enabled Actions:",
                $"  Flash: {settings?.AutonomyCanTriggerFlash}",
                $"  Video: {settings?.AutonomyCanTriggerVideo}",
                $"  Subliminal: {settings?.AutonomyCanTriggerSubliminal}",
                $"  BrainDrain: {settings?.AutonomyCanTriggerBrainDrain}",
                $"  Bubbles: {settings?.AutonomyCanTriggerBubbles}",
                $"  Comment: {settings?.AutonomyCanComment}",
                $"  MindWipe: {settings?.AutonomyCanTriggerMindWipe}",
                $"  LockCard: {settings?.AutonomyCanTriggerLockCard}",
                $"  Spiral: {settings?.AutonomyCanTriggerSpiral}",
                $"  PinkFilter: {settings?.AutonomyCanTriggerPinkFilter}",
                $"  BouncingText: {settings?.AutonomyCanTriggerBouncingText}"
            };
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Manually trigger an autonomous action for testing
        /// </summary>
        public void TestTrigger()
        {
            App.Logger?.Information("AutonomyService: TEST TRIGGER called manually!");

            // Show diagnostic status
            var status = GetDiagnosticStatus();
            App.Logger?.Information("AutonomyService Diagnostic Status:\n{Status}", status);

            if (!_isEnabled)
            {
                App.Logger?.Warning("AutonomyService: Test failed - service not enabled. Enable Autonomy Mode first!");

                var hasPatreon = App.Patreon?.HasPremiumAccess == true;
                var reason = !hasPatreon
                    ? "Bambi Takeover requires Patreon access."
                    : "Click the green \"Start\" button to enable it, then press Test again.";

                System.Windows.MessageBox.Show(
                    $"Autonomy Mode isn't running yet.\n\n{reason}\n\nDiagnostic Status:\n{status}",
                    "Autonomy Not Running");
                return;
            }

            // Show status before triggering
            System.Windows.MessageBox.Show($"Triggering test action...\n\nCurrent Status:\n{status}", "Autonomy Test");

            App.Logger?.Information("AutonomyService: Test trigger - executing action (bypassing cooldown check)");

            // Force execute, bypassing cooldown
            _isOnCooldown = false;
            ExecuteAutonomousAction(AutonomyTriggerSource.Random, "Manual test trigger");
        }

        /// <summary>
        /// Fire the "say it for me" voice action on demand, bypassing the weighted picker and the
        /// _isEnabled gate (dev/test affordance). Still requires the speech engine to be available
        /// and the avatar to be present; surfaces a friendly message if not.
        /// </summary>
        public void TestVoiceCommand()
        {
            if (App.Speech?.IsAvailable != true)
            {
                System.Windows.MessageBox.Show(
                    "Speech isn't available.\n\nDrop a Vosk model into Resources/Models/vosk/ (see the README there) and make sure a microphone is connected, then try again.",
                    "Voice Test — Not Available");
                return;
            }
            if (App.AvatarWindow == null)
            {
                System.Windows.MessageBox.Show(
                    "The companion avatar needs to be visible for the voice prompt. Show the avatar, then try again.",
                    "Voice Test — No Avatar");
                return;
            }
            if (App.MantraVoice?.HasMantras() != true)
            {
                System.Windows.MessageBox.Show(
                    "No spoken mantras are available for the active mod.\n\nAdd a mantras.json under the mod's companion_audio folder, then try again.",
                    "Voice Test — No Mantras");
                return;
            }
            // Privacy gate: the mic must never open until the user has accepted the consent dialog.
            // Every other voice entry point gates on this; this dev/test button is no exception.
            // Prompt here (once everything else is ready) and bail unless consent is actually granted.
            if (App.Settings?.Current?.MicConsentGiven != true)
            {
                try
                {
                    var dlg = new MicConsentDialog { Owner = Application.Current?.MainWindow };
                    var ok = dlg.ShowDialog() == true && dlg.ConsentGiven;
                    if (!ok || App.Settings?.Current?.MicConsentGiven != true)
                    {
                        App.Logger?.Information("AutonomyService: TestVoiceCommand — mic consent declined, not opening mic");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("AutonomyService: TestVoiceCommand consent dialog failed: {E}", ex.Message);
                    return;
                }
            }

            App.Logger?.Information("AutonomyService: TestVoiceCommand invoked manually");
            TriggerSpokenMantra();
        }

        /// <summary>
        /// Force start the service (for debugging) - bypasses all checks
        /// </summary>
        public void ForceStart()
        {
            App.Logger?.Warning("AutonomyService: FORCE START called - bypassing all checks!");

            DispatcherHelper.RunOnUISync(() =>
            {
                _isEnabled = true;
                _forceTestMode = true; // Keep using 30s interval even after timer fires
                _lastUserActivity = DateTime.Now;
                _lastActionTime = DateTime.MinValue;
                _isOnCooldown = false;

                // Force create timers using the new ScheduleNextRandomTick which respects _forceTestMode
                ScheduleNextRandomTick();
                StartHeartbeatTimer();

                App.Logger?.Information("AutonomyService: FORCE STARTED - Test mode enabled (30s intervals), IsEnabled={Enabled}, TimerRunning={Running}",
                    _isEnabled, _randomTimer?.IsEnabled == true);

                System.Windows.MessageBox.Show(
                    $"Force started in TEST MODE!\n\nRandom timer set to 30 seconds (will stay at 30s).\nCheck logs for HEARTBEAT messages.\n\nTimers running:\n- Random: {_randomTimer?.IsEnabled == true}\n- Heartbeat: {_heartbeatTimer?.IsEnabled == true}",
                    "Force Start Complete");
            });
        }

        public AutonomyService()
        {
            UpdateMood();
        }

        /// <summary>
        /// Start autonomous behavior if all conditions are met
        /// </summary>
        public void Start()
        {
            var settings = App.Settings?.Current;
            App.Logger?.Information("AutonomyService: Start() called - Enabled: {Enabled}, Consent: {Consent}, Level: {Level}",
                settings?.AutonomyModeEnabled, settings?.AutonomyConsentGiven, settings?.PlayerLevel);

            if (!CanStart())
            {
                App.Logger?.Warning("AutonomyService: Cannot start - requirements not met (need Patreon, consent, and enabled)");
                return;
            }

            // CRITICAL: Must create timers on UI thread or they won't fire!
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                App.Logger?.Error("AutonomyService: Cannot start - Application.Dispatcher is null!");
                return;
            }

            if (!dispatcher.CheckAccess())
            {
                App.Logger?.Information("AutonomyService: Start() called from non-UI thread, dispatching to UI thread...");
                DispatcherHelper.RunOnUISync(() => Start());
                return;
            }

            // Re-entrancy guard: Start() has three entry points (toggle / startup / panic-restart)
            // plus remote/chat paths. If we're already running, do nothing rather than spin up a
            // second set of timers on top of the live ones.
            if (_isEnabled)
            {
                App.Logger?.Information("AutonomyService: Start() ignored — already running");
                return;
            }

            _isEnabled = true;
            _lastUserActivity = DateTime.Now;
            _lastActionTime = DateTime.MinValue;
            _isOnCooldown = false;

            StartIdleTimer();
            StartRandomTimer();
            StartHeartbeatTimer();
            UpdateMood();

            // Arm the opt-in user-driven mic modes (wake-word / push-to-talk) if configured.
            RefreshVoiceInputModes();

            try { EnabledChanged?.Invoke(this, true); } catch { }

            App.Logger?.Information("AutonomyService: Started successfully! Timers: Idle={IdleRunning}, Random={RandomRunning}",
                _idleTimer?.IsEnabled == true,
                _randomTimer?.IsEnabled == true);
            App.Logger?.Information("AutonomyService: Settings - Intensity: {Intensity}, IdleEnabled: {Idle}, RandomEnabled: {Random}, Interval: {Interval}min",
                settings?.AutonomyIntensity ?? 5,
                settings?.AutonomyIdleTriggerEnabled,
                settings?.AutonomyRandomTriggerEnabled,
                settings?.AutonomyRandomIntervalMinutes);

            // Verify timers are actually running after a short delay
            var verifyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            verifyTimer.Tick += (s, e) =>
            {
                verifyTimer.Stop();
                App.Logger?.Information("AutonomyService: VERIFICATION - IsEnabled={Enabled}, IdleTimer={Idle}, RandomTimer={Random}, OnCooldown={Cooldown}",
                    _isEnabled,
                    _idleTimer?.IsEnabled == true,
                    _randomTimer?.IsEnabled == true,
                    _isOnCooldown);
            };
            verifyTimer.Start();
        }

        /// <summary>
        /// Stop autonomous behavior
        /// </summary>
        public void Stop()
        {
            _isEnabled = false;
            _forceTestMode = false; // Reset test mode
            StopAllTimers();

            // NOTE: the mic modes (wake word / push-to-talk) are NOT torn down here anymore — they're
            // decoupled from Takeover and owned by "She's Listening". Re-arm/disarm follows their own
            // toggles. (Full teardown still happens on Dispose and via StopVoiceInput / the privacy pill.)

            // Restore any active pulse settings (spiral, pink filter, etc.) before cleanup
            CancelActivePulses();

            // Reset all pulse flags to prevent stale callbacks from running
            _spiralPulseActive = false;
            _pinkFilterPulseActive = false;
            _bubblesPulseActive = false;
            _bouncingTextPulseActive = false;
            _globalPulseGeneration++; // Invalidate ALL pending pulse callbacks

            try { EnabledChanged?.Invoke(this, false); } catch { }

            App.Logger?.Information("AutonomyService: Stopped");
        }

        /// <summary>
        /// Cancel all active pulses and restore original settings.
        /// Called by panic key handler to immediately clear autonomy effects.
        /// Does NOT stop the autonomy service itself - just cancels current pulses.
        /// </summary>
        public void CancelActivePulses()
        {
            App.Logger?.Information("AutonomyService: CancelActivePulses called");

            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Invalidate all pending pulse callbacks
            _globalPulseGeneration++;

            // Restore spiral settings if a pulse was active
            if (_spiralPulseActive && _originalSpiralEnabled.HasValue)
            {
                App.Logger?.Information("AutonomyService: Restoring spiral - enabled={Enabled}, opacity={Opacity}",
                    _originalSpiralEnabled.Value, _originalSpiralOpacity ?? settings.SpiralOpacity);
                settings.SpiralEnabled = _originalSpiralEnabled.Value;
                if (_originalSpiralOpacity.HasValue)
                    settings.SpiralOpacity = _originalSpiralOpacity.Value;
            }
            _spiralPulseActive = false;
            _originalSpiralEnabled = null;
            _originalSpiralOpacity = null;

            // Restore pink filter settings if a pulse was active
            if (_pinkFilterPulseActive && _originalPinkFilterEnabled.HasValue)
            {
                App.Logger?.Information("AutonomyService: Restoring pink filter - enabled={Enabled}, opacity={Opacity}",
                    _originalPinkFilterEnabled.Value, _originalPinkFilterOpacity ?? settings.PinkFilterOpacity);
                settings.PinkFilterEnabled = _originalPinkFilterEnabled.Value;
                if (_originalPinkFilterOpacity.HasValue)
                    settings.PinkFilterOpacity = _originalPinkFilterOpacity.Value;
            }
            _pinkFilterPulseActive = false;
            _originalPinkFilterEnabled = null;
            _originalPinkFilterOpacity = null;

            // Stop bubbles if started by autonomy
            if (_bubblesPulseActive)
            {
                App.Logger?.Information("AutonomyService: Stopping autonomy-started bubbles");
                App.Bubbles?.Stop();
            }
            _bubblesPulseActive = false;

            // Stop bouncing text if started by autonomy
            if (_bouncingTextPulseActive)
            {
                App.Logger?.Information("AutonomyService: Stopping autonomy-started bouncing text");
                App.BouncingText?.Stop();
            }
            _bouncingTextPulseActive = false;

            // Refresh overlays to apply restored settings
            App.Overlay?.RefreshOverlays();

            App.Logger?.Information("AutonomyService: All active pulses cancelled");
        }

        /// <summary>
        /// Check if autonomy can start (requires Patreon + consent)
        /// </summary>
        private bool CanStart()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return false;

            var hasPatreon = App.Patreon?.HasPremiumAccess == true;
            return settings.AutonomyModeEnabled &&
                   settings.AutonomyConsentGiven &&
                   hasPatreon;
        }

        /// <summary>
        /// Report user activity to reset idle timer
        /// </summary>
        public void ReportUserActivity()
        {
            _lastUserActivity = DateTime.Now;
            ResetIdleTimer();
        }

        #region Timers

        private void StartIdleTimer()
        {
            var settings = App.Settings?.Current;
            if (settings == null)
            {
                App.Logger?.Warning("AutonomyService: StartIdleTimer - settings is null!");
                return;
            }
            if (!settings.AutonomyIdleTriggerEnabled)
            {
                App.Logger?.Information("AutonomyService: Idle timer NOT started - AutonomyIdleTriggerEnabled is false");
                return;
            }

            var intervalMinutes = settings.AutonomyIdleTimeoutMinutes;

            _idleTimer?.Stop();
            _idleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(intervalMinutes)
            };
            _idleTimer.Tick += OnIdleTick;
            _idleTimer.Start();

            App.Logger?.Information("AutonomyService: Idle timer started - triggers after {Minutes} min of inactivity", intervalMinutes);
        }

        private void ResetIdleTimer()
        {
            if (_idleTimer != null && _idleTimer.IsEnabled)
            {
                _idleTimer.Stop();
                _idleTimer.Start();
            }
        }

        private void StartRandomTimer()
        {
            var settings = App.Settings?.Current;
            if (settings == null)
            {
                App.Logger?.Warning("AutonomyService: StartRandomTimer - settings is null!");
                return;
            }
            if (!settings.AutonomyRandomTriggerEnabled)
            {
                App.Logger?.Information("AutonomyService: Random timer NOT started - AutonomyRandomTriggerEnabled is false");
                return;
            }

            ScheduleNextRandomTick();
        }

        private void ScheduleNextRandomTick(bool retry = false)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            double actualSeconds;
            string modeInfo;

            if (retry)
            {
                // A tick fired but she couldn't act (cooldown / fullscreen interaction / web video).
                // Re-check soon instead of burning a whole fresh interval — otherwise the countdown
                // bar completes with nothing happening and her real cadence drifts far below the
                // slider. ~12–24s jittered so it doesn't hammer while a long video is on screen.
                actualSeconds = RetryIntervalSeconds + _random.NextDouble() * RetryIntervalSeconds;
                modeInfo = "retry (was blocked)";
            }
            else if (_forceTestMode)
            {
                // Force test mode: always use 30 seconds
                actualSeconds = 30;
                modeInfo = "FORCE TEST MODE";
            }
            else
            {
                // Normal mode: jitter ±~33% AROUND the configured interval, so the slider value is the
                // midpoint (e.g. 30 → 20–40s, 60 → 40–80s). Tightened from the old 0.5–1.5× spread,
                // which let a 60s setting fire as early as 30s.
                var baseSeconds = settings.AutonomyRandomIntervalSeconds;
                var variance = (2.0 / 3.0) + _random.NextDouble() * (2.0 / 3.0); // 0.667 to 1.333
                actualSeconds = baseSeconds * variance;

                // Time-of-day pacing: when "time aware" is on, a higher activity multiplier means she's
                // MORE active, so the gap shrinks at night and stretches in the morning. Until now
                // GetTimeMultiplier() was computed but never applied anywhere, so the toggle (and every
                // morning/afternoon/evening/night multiplier) did nothing at all.
                var timeMult = GetTimeMultiplier();
                if (timeMult > 0)
                    actualSeconds /= timeMult;

                // Keep the result sane no matter how jitter and the multiplier combine.
                actualSeconds = Math.Clamp(actualSeconds, 15, 900);
                modeInfo = $"base: {baseSeconds}s (±33%, timeMult {timeMult:0.00})";
            }

            _randomTimer?.Stop();
            _randomTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(actualSeconds)
            };
            _randomTimer.Tick += OnRandomTick;
            _randomTimer.Start();

            // Anchor for the avatar countdown bar (DispatcherTimer exposes no "time remaining").
            _lastRandomIntervalSeconds = actualSeconds;
            _nextRandomFireTime = DateTime.Now.AddSeconds(actualSeconds);

            App.Logger?.Information("AutonomyService: Random timer scheduled - next tick in {Seconds:F0}s ({Mode})",
                actualSeconds, modeInfo);
        }

        private void StopAllTimers()
        {
            _idleTimer?.Stop();
            _idleTimer = null;

            _randomTimer?.Stop();
            _randomTimer = null;

            _cooldownTimer?.Stop();
            _cooldownTimer = null;

            _heartbeatTimer?.Stop();
            _heartbeatTimer = null;
        }

        /// <summary>
        /// Start a heartbeat timer that logs every 30 seconds to confirm the service is alive
        /// </summary>
        private void StartHeartbeatTimer()
        {
            _heartbeatTimer?.Stop();
            _heartbeatTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _heartbeatTimer.Tick += (s, e) =>
            {
                var settings = App.Settings?.Current;
                var nextRandomTick = _randomTimer?.IsEnabled == true ? "active" : "STOPPED";
                var nextIdleTick = _idleTimer?.IsEnabled == true ? "active" : "STOPPED";

                var timeSinceLast = (DateTime.Now - _lastActionTime).TotalSeconds;
                var cooldownSec = settings?.AutonomyCooldownSeconds ?? 0;
                var timeCooldownActive = timeSinceLast < cooldownSec;

                App.Logger?.Information(
                    "AutonomyService HEARTBEAT: Enabled={Enabled}, RandomTimer={Random}, IdleTimer={Idle}, Cooldown={Cooldown}, TimeCooldown={TimeCooldown} ({Elapsed:F0}s/{Required}s), WebVideo={WebVideo}, QueueBusy={Busy}",
                    _isEnabled,
                    nextRandomTick,
                    nextIdleTick,
                    _isOnCooldown,
                    timeCooldownActive,
                    timeSinceLast,
                    cooldownSec,
                    _webVideoActive,
                    App.InteractionQueue?.IsBusy == true);
            };
            _heartbeatTimer.Start();
            App.Logger?.Information("AutonomyService: Heartbeat timer started (logs every 30s)");
        }

        /// <summary>
        /// Refresh idle timer when settings change
        /// </summary>
        public void RefreshIdleTimer()
        {
            if (!_isEnabled) return;

            _idleTimer?.Stop();
            _idleTimer = null;

            if (App.Settings?.Current?.AutonomyIdleTriggerEnabled == true)
            {
                StartIdleTimer();
            }
            else
            {
                App.Logger?.Debug("AutonomyService: Idle timer disabled");
            }
        }

        /// <summary>
        /// Refresh random timer when settings change
        /// </summary>
        public void RefreshRandomTimer()
        {
            if (!_isEnabled) return;

            _randomTimer?.Stop();
            _randomTimer = null;

            if (App.Settings?.Current?.AutonomyRandomTriggerEnabled == true)
            {
                StartRandomTimer();
            }
            else
            {
                App.Logger?.Debug("AutonomyService: Random timer disabled");
            }
        }

        private void OnIdleTick(object? sender, EventArgs e)
        {
            if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
            if (!_isEnabled) return;

            var settings = App.Settings?.Current;
            if (settings == null || !settings.AutonomyIdleTriggerEnabled) return;

            if (!CanTakeAction()) return;

            App.Logger?.Debug("AutonomyService: Idle timeout triggered");
            ExecuteAutonomousAction(AutonomyTriggerSource.Idle);
        }

        private void OnRandomTick(object? sender, EventArgs e)
        {
            App.Logger?.Information("AutonomyService: Random timer FIRED!");

            if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
            if (!_isEnabled)
            {
                App.Logger?.Warning("AutonomyService: Random tick ignored - not enabled");
                return;
            }

            var settings = App.Settings?.Current;
            if (settings == null || !settings.AutonomyRandomTriggerEnabled)
            {
                App.Logger?.Warning("AutonomyService: Random tick ignored - random trigger disabled");
                return;
            }

            if (!CanTakeAction())
            {
                // Temporarily blocked — schedule a short retry rather than a full fresh interval, so
                // she acts as soon as she's free instead of skipping a whole cycle every time she's
                // briefly busy (which made the interval feel much slower than the slider).
                App.Logger?.Debug("AutonomyService: Random tick - cannot take action (cooldown or busy), retrying soon");
                ScheduleNextRandomTick(retry: true);
                return;
            }

            // Free to act — schedule the next full interval, then go.
            ScheduleNextRandomTick();
            App.Logger?.Information("AutonomyService: Random interval triggered - executing action!");
            ExecuteAutonomousAction(AutonomyTriggerSource.Random);
        }

        #endregion

        #region Action Execution

        /// <summary>
        /// Check if we can take an autonomous action right now
        /// </summary>
        private bool CanTakeAction()
        {
            if (!_isEnabled)
            {
                App.Logger?.Debug("AutonomyService: CanTakeAction=false - not enabled");
                return false;
            }
            if (_isOnCooldown)
            {
                App.Logger?.Debug("AutonomyService: CanTakeAction=false - on cooldown");
                return false;
            }

            // Don't take actions while web video is playing fullscreen
            if (_webVideoActive)
            {
                App.Logger?.Debug("AutonomyService: CanTakeAction=false - web video playing fullscreen");
                return false;
            }

            // Don't interrupt active fullscreen interaction (video, bubble count, lock card)
            if (App.InteractionQueue?.IsBusy == true)
            {
                App.Logger?.Debug("AutonomyService: CanTakeAction=false - interaction queue busy ({Type})",
                    App.InteractionQueue?.CurrentInteraction);
                return false;
            }

            // Check time-based cooldown
            var settings = App.Settings?.Current;
            if (settings == null) return false;

            var timeSinceLast = (DateTime.Now - _lastActionTime).TotalSeconds;
            if (timeSinceLast < settings.AutonomyCooldownSeconds)
            {
                App.Logger?.Debug("AutonomyService: CanTakeAction=false - time cooldown ({Elapsed:F0}s / {Required}s)",
                    timeSinceLast, settings.AutonomyCooldownSeconds);
                return false;
            }
            return true;
        }

        /// <summary>
        /// Execute an autonomous action
        /// </summary>
        private void ExecuteAutonomousAction(AutonomyTriggerSource source, string? context = null)
        {
            try
            {
                App.Logger?.Information("AutonomyService: ExecuteAutonomousAction called (Source: {Source})", source);

                var actionType = SelectAction(source, context);
                if (actionType == null)
                {
                    App.Logger?.Warning("AutonomyService: No valid action available - check if any actions are enabled!");
                    return;
                }

                App.Logger?.Information("AutonomyService: Selected action: {Action}", actionType);

                var shouldAnnounce = ShouldAnnounce();
                App.Logger?.Information("AutonomyService: Will announce: {Announce}", shouldAnnounce);

                if (shouldAnnounce)
                {
                    AnnounceAction(actionType.Value);
                    App.Logger?.Information("AutonomyService: Announcement made, scheduling action in 2 seconds...");

                    // Delay action after announcement
                    var capturedAction = actionType.Value;
                    Task.Delay(2000).ContinueWith(_ =>
                    {
                        App.Logger?.Information("AutonomyService: 2 second delay complete, executing {Action}...", capturedAction);
                        if (Application.Current?.Dispatcher == null)
                        {
                            App.Logger?.Warning("AutonomyService: Cannot execute action - Dispatcher is null after delay");
                            return;
                        }
                        Application.Current?.Dispatcher?.BeginInvoke(() =>
                        {
                            PerformAction(capturedAction, source, context, announce: true);
                        });
                    });
                }
                else
                {
                    App.Logger?.Information("AutonomyService: No announcement, executing action immediately...");
                    PerformAction(actionType.Value, source, context, announce: false);
                }

                // Start cooldown
                StartCooldown();
                _lastActionTime = DateTime.Now;

                ActionTriggered?.Invoke(this, new AutonomyActionEventArgs(actionType.Value, source, context));
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "AutonomyService: Failed to execute action");
            }
        }

        /// <summary>
        /// Select an action based on settings, weights, and mood
        /// </summary>
        private AutonomyActionType? SelectAction(AutonomyTriggerSource source, string? context)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return null;

            var candidates = new List<(AutonomyActionType type, int weight)>();

            // Build weighted list of enabled actions
            // Note: Autonomy works independently of engine - only checks Autonomy-specific settings
            if (settings.AutonomyCanTriggerFlash)
                candidates.Add((AutonomyActionType.Flash, 30));

            if (settings.AutonomyCanTriggerVideo)
                candidates.Add((AutonomyActionType.Video, 15)); // Lower weight - more disruptive

            if (settings.AutonomyCanTriggerSubliminal)
                candidates.Add((AutonomyActionType.Subliminal, 25));

            // Note: BrainDrainPulse removed from autonomy — Brain Drain is being kept out of user-facing
            // surfaces while the blur feature is still being verified (mirrors its removal from the
            // Deeper editor + session creator). Enum/label/PulseBrainDrain kept for a clean restore later.

            if (settings.AutonomyCanTriggerBubbles)
                candidates.Add((AutonomyActionType.StartBubbles, 15));

            if (settings.AutonomyCanComment)
                candidates.Add((AutonomyActionType.Comment, 20));

            // New progression features
            if (settings.AutonomyCanTriggerMindWipe)
                candidates.Add((AutonomyActionType.MindWipe, 15));

            if (settings.AutonomyCanTriggerLockCard)
                candidates.Add((AutonomyActionType.LockCard, 10)); // Lower weight - very disruptive

            // Note: SpiralPulse removed from autonomy - can interfere with user experience

            if (settings.AutonomyCanTriggerPinkFilter)
                candidates.Add((AutonomyActionType.PinkFilterPulse, 20));

            if (settings.AutonomyCanTriggerBouncingText)
                candidates.Add((AutonomyActionType.BouncingText, 15));

            // Web video - plays random HypnoTube video fullscreen in browser.
            // Exclude while a mandatory video is on screen so we pick a different action
            // rather than stacking two videos (BUG-XRFQH4AHDN).
            if (settings.AutonomyCanTriggerWebVideo && !_webVideoActive && App.Video?.IsPlaying != true)
                candidates.Add((AutonomyActionType.WebVideo, 20));

            // Wallpaper shuffle - subtle desktop wallpaper change
            if (settings.AutonomyCanTriggerWallpaper)
                candidates.Add((AutonomyActionType.WallpaperShuffle, 10));

            // Spoken mantra - "say it for me" (offline speech). Self-gating: only ever a candidate
            // when the speech engine is actually available (model + mic), the user consented to the
            // mic, AND the active mod ships mantra content — so it's purely additive: no engine or no
            // content => it simply never appears.
            // When the user opted into wake-word or push-to-talk, the mic only opens on her own
            // initiative — so we suppress the surprise auto-trigger ("overrides auto-listen").
            if (settings.AutonomyCanTriggerVoiceCommand
                && settings.MicConsentGiven
                && !settings.SpeechWakeWordEnabled
                && !settings.SpeechPushToTalkEnabled
                && App.Speech?.IsAvailable == true
                && App.Speech?.IsListening != true
                && !_voiceBusy
                && App.AvatarWindow != null
                && App.MantraVoice?.HasMantras() == true)
                candidates.Add((AutonomyActionType.SpokenMantra, 18));

            // Note: BubbleCount removed from autonomy - too disruptive and unreliable

            if (candidates.Count == 0) return null;

            // Apply mood modifiers
            ApplyMoodWeights(candidates);

            // Apply intensity scaling
            ApplyIntensityScaling(candidates, settings.AutonomyIntensity);

            // Weighted random selection
            var totalWeight = candidates.Sum(c => c.weight);
            if (totalWeight <= 0) return null;

            var roll = _random.Next(totalWeight);
            var cumulative = 0;

            foreach (var (type, weight) in candidates)
            {
                cumulative += weight;
                if (roll < cumulative)
                {
                    return type;
                }
            }

            return candidates.FirstOrDefault().type;
        }

        private void ApplyMoodWeights(List<(AutonomyActionType type, int weight)> candidates)
        {
            UpdateMood();

            // Mood affects which actions are more likely
            for (int i = 0; i < candidates.Count; i++)
            {
                var (type, weight) = candidates[i];
                var modifier = 1.0;

                switch (_currentMood)
                {
                    case AutonomyMood.Gentle:
                        // Prefer comments, reduce disruptive actions
                        modifier = type switch
                        {
                            AutonomyActionType.Comment => 1.5,
                            AutonomyActionType.Video => 0.5,
                            AutonomyActionType.BrainDrainPulse => 0.5,
                            _ => 1.0
                        };
                        break;

                    case AutonomyMood.Playful:
                        // Prefer bubbles and flashes
                        modifier = type switch
                        {
                            AutonomyActionType.StartBubbles => 1.5,
                            AutonomyActionType.Flash => 1.3,
                            _ => 1.0
                        };
                        break;

                    case AutonomyMood.Mischievous:
                        // More likely to do "naughty" things
                        modifier = type switch
                        {
                            AutonomyActionType.Video => 1.5,
                            AutonomyActionType.BrainDrainPulse => 1.5,
                            AutonomyActionType.Subliminal => 1.3,
                            _ => 1.0
                        };
                        break;
                }

                candidates[i] = (type, (int)(weight * modifier));
            }
        }

        private void ApplyIntensityScaling(List<(AutonomyActionType type, int weight)> candidates, int intensity)
        {
            // Higher intensity = more disruptive actions become more likely
            var disruptiveBonus = (intensity - 5) * 0.1; // -0.4 to +0.5

            for (int i = 0; i < candidates.Count; i++)
            {
                var (type, weight) = candidates[i];

                // Disruptive actions scale with intensity
                if (type == AutonomyActionType.Video || type == AutonomyActionType.BrainDrainPulse)
                {
                    var modifier = 1.0 + disruptiveBonus;
                    candidates[i] = (type, Math.Max(1, (int)(weight * modifier)));
                }
            }
        }

        /// <summary>
        /// Display label for the on-screen takeover cue, or <c>null</c> to suppress it.
        /// Comment is suppressed — it's just the avatar giggling, not a screen effect.
        /// </summary>
        private static string? TakeoverEffectLabel(AutonomyActionType t) => t switch
        {
            AutonomyActionType.Flash            => "FLASH",
            AutonomyActionType.Video            => "VIDEO",
            AutonomyActionType.Subliminal       => "SUBLIMINAL",
            AutonomyActionType.BrainDrainPulse  => "BRAIN DRAIN",
            AutonomyActionType.StartBubbles     => "BUBBLES",
            AutonomyActionType.MindWipe         => "MIND WIPE",
            AutonomyActionType.LockCard         => "LOCK CARD",
            AutonomyActionType.SpiralPulse      => "SPIRAL",
            AutonomyActionType.PinkFilterPulse  => "PINK FILTER",
            AutonomyActionType.BouncingText     => "BOUNCING TEXT",
            AutonomyActionType.BubbleCount      => "BUBBLE COUNT",
            AutonomyActionType.WebVideo         => "WEB VIDEO",
            AutonomyActionType.WallpaperShuffle => "WALLPAPER",
            AutonomyActionType.SpokenMantra     => "MANTRA",
            AutonomyActionType.Comment          => null,   // avatar giggle — no banner
            _                                   => null,
        };

        private void PerformAction(AutonomyActionType actionType, AutonomyTriggerSource source, string? context, bool announce)
        {
            App.Logger?.Information("AutonomyService: PerformAction starting - {Action}", actionType);

            DispatcherHelper.RunOnUI(() =>
            {
                try
                {
                    // Mark that autonomy is triggering this action (for Cult Bunny XP bonus)
                    IsActionInProgress = true;
                    App.Logger?.Information("AutonomyService: Executing action {Action}...", actionType);

                    // On-screen cue so a takeover-driven effect is visibly distinct from an
                    // ordinary engine effect. Fires for every effect except Comment (just a giggle).
                    // Gated on the announce roll (AutonomyAnnouncementChance): at 0% the user wants
                    // silent, unannounced takeover, so the HUD banner is suppressed too (#534).
                    if (announce)
                    {
                        try
                        {
                            var cue = TakeoverEffectLabel(actionType);
                            if (cue != null) TakeoverAnnouncerOverlay.Announce(cue);
                        }
                        catch (Exception cueEx) { App.Logger?.Debug("AutonomyService: takeover cue failed: {E}", cueEx.Message); }
                    }

                    switch (actionType)
                    {
                        case AutonomyActionType.Flash:
                            if (App.Flash == null)
                            {
                                App.Logger?.Warning("AutonomyService: Flash service is null!");
                            }
                            else
                            {
                                App.Flash.TriggerFlashOnce();
                                App.Logger?.Information("AutonomyService: Flash triggered");
                            }
                            break;

                        case AutonomyActionType.Video:
                            TriggerVideoSafely();
                            break;

                        case AutonomyActionType.Subliminal:
                            App.Subliminal?.FlashSubliminal();
                            break;

                        case AutonomyActionType.BrainDrainPulse:
                            PulseBrainDrain();
                            break;

                        case AutonomyActionType.StartBubbles:
                            if (!_bubblesPulseActive && App.Bubbles?.IsRunning != true)
                            {
                                _bubblesPulseActive = true;
                                App.Bubbles?.Start(bypassLevelCheck: true);
                                // Stop bubbles after 30 seconds
                                Task.Delay(30000).ContinueWith(_ =>
                                {
                                    if (Application.Current?.Dispatcher == null) return;
                                    Application.Current?.Dispatcher?.BeginInvoke(() =>
                                    {
                                        if (_bubblesPulseActive)
                                        {
                                            _bubblesPulseActive = false;
                                            App.Bubbles?.Stop();
                                            App.Logger?.Debug("AutonomyService: Bubbles auto-stopped after 30 seconds");
                                        }
                                    });
                                });
                            }
                            break;

                        case AutonomyActionType.Comment:
                            MakeComment(context);
                            break;

                        case AutonomyActionType.MindWipe:
                            App.MindWipe?.TriggerOnce();
                            break;

                        case AutonomyActionType.LockCard:
                            // Use ShowLockCard() directly to trigger a single lock card
                            // without requiring the continuous service to be enabled
                            App.LockCard?.ShowLockCard();
                            break;

                        case AutonomyActionType.SpiralPulse:
                            PulseSpiralOverlay();
                            break;

                        case AutonomyActionType.PinkFilterPulse:
                            PulsePinkFilter();
                            break;

                        case AutonomyActionType.BouncingText:
                            if (!_bouncingTextPulseActive && App.BouncingText?.IsRunning != true)
                            {
                                _bouncingTextPulseActive = true;
                                App.BouncingText?.Start(bypassLevelCheck: true);
                                // Stop after 30 seconds
                                Task.Delay(30000).ContinueWith(_ =>
                                {
                                    if (Application.Current?.Dispatcher == null) return;
                                    Application.Current?.Dispatcher?.BeginInvoke(() =>
                                    {
                                        if (_bouncingTextPulseActive)
                                        {
                                            _bouncingTextPulseActive = false;
                                            App.BouncingText?.Stop();
                                            App.Logger?.Debug("AutonomyService: Bouncing text auto-stopped after 30 seconds");
                                        }
                                    });
                                });
                            }
                            break;

                        case AutonomyActionType.BubbleCount:
                            // Use TriggerGame directly to show a single game
                            // forceTest: true bypasses running/level checks
                            App.BubbleCount?.TriggerGame(forceTest: true);
                            break;

                        case AutonomyActionType.WebVideo:
                            TriggerWebVideoFullscreen();
                            break;

                        case AutonomyActionType.SpokenMantra:
                            TriggerSpokenMantra();
                            break;

                        case AutonomyActionType.WallpaperShuffle:
                            if (App.Wallpaper != null)
                            {
                                if (App.Wallpaper.IsActive)
                                {
                                    // Already active — just shuffle
                                    App.Wallpaper.Shuffle();
                                }
                                else
                                {
                                    // Pulse: activate, wait 30s, deactivate (unless user toggled it on manually)
                                    App.Wallpaper.Activate();
                                    var userEnabled = App.Settings?.Current?.WallpaperEnabled == true;
                                    if (!userEnabled)
                                    {
                                        _ = Task.Delay(30000).ContinueWith(_ =>
                                        {
                                            try
                                            {
                                                if (Application.Current?.Dispatcher == null) return;
                                                // Only deactivate if user hasn't manually enabled it since
                                                if (App.Settings?.Current?.WallpaperEnabled != true)
                                                    App.Wallpaper?.Deactivate();
                                            }
                                            catch { }
                                        });
                                    }
                                }
                            }
                            break;
                    }

                    App.Logger?.Information("Autonomy: Performed {Action} (Source: {Source})",
                        actionType, source);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Autonomy: Failed to perform {Action}", actionType);
                }
                finally
                {
                    // Clear the flag after action completes (XP is awarded during service calls)
                    IsActionInProgress = false;
                }
            });
        }

        /// <summary>
        /// Trigger video with takeover strictness (non-strict unless the user opted in
        /// via TakeoverVideosStrict). Strictness is passed per-call instead of the old
        /// "flip global StrictLockEnabled off for 3 seconds" dance — that window made
        /// engine-scheduled videos come up non-strict and raced every other reader of
        /// the setting (see SessionEngine's session-start snapshot workaround).
        /// </summary>
        private void TriggerVideoSafely()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Skip mandatory video if a web video is currently active — playing both
            // simultaneously stacks two videos on screen, which is what users reported
            // in BUG-XRFQH4AHDN. Advisory fast-path; the interaction queue inside
            // TriggerVideo is the authoritative arbiter.
            if (_webVideoActive) return;

            App.Video?.TriggerVideo(strictOverride: settings.TakeoverVideosStrict);
        }

        /// <summary>
        /// Trigger a random web video from HypnoTube to play fullscreen in the browser
        /// </summary>
        private void TriggerWebVideoFullscreen()
        {
            if (_webVideoActive)
            {
                App.Logger?.Information("AutonomyService: Web video already active, skipping");
                return;
            }

            // Don't stack a fullscreen web video on top of a mandatory video that's already
            // playing — that's the doubled video + doubled audio users reported (BUG-XRFQH4AHDN).
            // The reverse (mandatory over web) is guarded in VideoService's scheduler and
            // TriggerVideoSafely().
            if (App.Video?.IsPlaying == true)
            {
                App.Logger?.Information("AutonomyService: Mandatory video playing, skipping web video to avoid stacked playback");
                return;
            }

            // Authoritative claim: atomically reserve the fullscreen slot BEFORE picking a
            // video or navigating. The IsPlaying check above races VideoService's ~2s
            // pre-roll window (freeze delay + pre-announce) where a committed mandatory
            // video still reads as not playing; the queue slot is claimed synchronously at
            // trigger time so TryStart cannot. Never queue web videos — autonomy's own
            // retry tick re-attempts within RetryIntervalSeconds anyway.
            if (App.InteractionQueue != null && !App.InteractionQueue.TryStart(
                    InteractionQueueService.InteractionType.WebVideo, () => { }, queue: false))
            {
                App.Logger?.Information("AutonomyService: Fullscreen slot busy ({Current}), skipping web video",
                    App.InteractionQueue.CurrentInteraction);
                return;
            }

            var videoLinks = AvatarTubeWindow.KnownVideoLinks;
            if (videoLinks == null || videoLinks.Count == 0)
            {
                App.Logger?.Warning("AutonomyService: No known video links available");
                App.InteractionQueue?.CompleteIfCurrent(InteractionQueueService.InteractionType.WebVideo);
                return;
            }

            // Pick a random video that hasn't been shown yet
            var videoList = videoLinks.ToList();
            var availableVideos = videoList.Where(v => !_shownWebVideos.Contains(v.Key)).ToList();
            
            // If all videos have been shown, reset the list
            if (availableVideos.Count == 0)
            {
                App.Logger?.Information("AutonomyService: All {Count} web videos have been shown, resetting list", videoList.Count);
                _shownWebVideos.Clear();
                availableVideos = videoList;
            }
            
            var randomVideo = availableVideos[_random.Next(availableVideos.Count)];
            var videoName = randomVideo.Key;
            var videoUrl = randomVideo.Value;
            
            // Mark this video as shown
            _shownWebVideos.Add(videoName);
            App.Logger?.Debug("AutonomyService: Shown {Shown}/{Total} web videos", _shownWebVideos.Count, videoList.Count);

            App.Logger?.Information("AutonomyService: Playing web video '{Name}' at {Url}", videoName, videoUrl);

            // Find MainWindow - try multiple approaches
            var mainWindow = Application.Current?.MainWindow as MainWindow
                ?? Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();

            if (mainWindow == null)
            {
                App.Logger?.Warning("AutonomyService: MainWindow not found, cannot play web video");
                App.InteractionQueue?.CompleteIfCurrent(InteractionQueueService.InteractionType.WebVideo);
                return;
            }

            // Mark video as active - blocks other autonomy actions
            _webVideoActive = true;
            var watchdogGen = ++_webVideoWatchdogGeneration;

            // Safety watchdog: auto-reset after 30 seconds if OnWebVideoEnded never fires
            // (page should load within ~10s; 30s is generous but doesn't block autonomy for ages)
            Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ =>
            {
                if (Application.Current?.Dispatcher == null) return;
                try
                {
                    if (_webVideoActive && _webVideoWatchdogGeneration == watchdogGen)
                    {
                        _webVideoActive = false;
                        App.InteractionQueue?.CompleteIfCurrent(InteractionQueueService.InteractionType.WebVideo);
                        App.Logger?.Warning("AutonomyService: Web video watchdog fired - resetting stuck _webVideoActive after 30s");
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("AutonomyService: web video watchdog failed: {Error}", ex.Message);
                }
            });

            // Navigate to video with fullscreen autoplay
            if (mainWindow.NavigateToUrlInBrowser(videoUrl, autoPlayFullscreen: true))
            {
                App.Logger?.Information("AutonomyService: Web video navigation initiated for '{Name}'", videoName);
            }
            else
            {
                // Navigation failed - reset state
                _webVideoActive = false;
                App.InteractionQueue?.CompleteIfCurrent(InteractionQueueService.InteractionType.WebVideo);
                App.Logger?.Warning("AutonomyService: Failed to navigate to web video - browser not available");
            }
        }

        /// <summary>
        /// Called by MainWindow when the injected JS confirms a web video has actually begun
        /// playing. Cancels the load-failure watchdog so it can't reset _webVideoActive while
        /// the video is still on screen, and ensures the flag is set even when the navigation
        /// was triggered by a user-clicked link rather than by autonomy itself.
        /// </summary>
        public void OnWebVideoStarted()
        {
            // Bump generation so any pending Task.Delay watchdog from TriggerWebVideoFullscreen
            // becomes a no-op when it fires.
            _webVideoWatchdogGeneration++;

            if (!_webVideoActive)
            {
                _webVideoActive = true;
                // Best-effort claim: the navigation already happened (user-clicked link),
                // so we can't skip it — but registering with the queue keeps scheduled
                // interactions from stacking on top for the rest of the playback.
                App.InteractionQueue?.TryStart(
                    InteractionQueueService.InteractionType.WebVideo, () => { }, queue: false);
                App.Logger?.Information("AutonomyService: Web video started (external trigger), blocking autonomy actions");
            }
            else
            {
                App.Logger?.Debug("AutonomyService: Web video confirmed playing, watchdog cancelled");
            }

            // Pending-duration leash: the queue's default stuck window is 5 minutes, which
            // would force-end a healthy video that never reports a duration (live streams,
            // JS injection miss). Give it an hour up front; OnWebVideoDuration tightens
            // this to the real length + grace. Type-guarded: no-ops unless WE hold the slot.
            App.InteractionQueue?.ExtendTimeout(3600, InteractionQueueService.InteractionType.WebVideo);
        }

        /// <summary>
        /// Called by MainWindow when a web video finishes or exits fullscreen.
        /// Resets the web video active state to allow other autonomy actions.
        /// </summary>
        public void OnWebVideoEnded()
        {
            // Also bump the watchdog generation - if a stale watchdog is still pending,
            // we don't want it firing later and overwriting state for a new video.
            _webVideoWatchdogGeneration++;
            _webVideoDurationGeneration++;   // cancel the pending duration hard stop too

            if (_webVideoActive)
            {
                _webVideoActive = false;
                App.Logger?.Information("AutonomyService: Web video ended, autonomy actions resumed");
            }

            // Release the fullscreen slot (dequeues any waiting interaction). Idempotent
            // and type-guarded — the JS 'ended', fullscreen-exit and duration hard stop
            // can all fire for the same video; only the first release matters.
            App.InteractionQueue?.CompleteIfCurrent(InteractionQueueService.InteractionType.WebVideo);
        }

        /// <summary>
        /// Hard-terminates a fullscreen web-video takeover from outside the normal JS
        /// lifecycle (interaction-queue stuck recovery, user force-reset): ends the
        /// browser takeover and runs the normal ended bookkeeping (flag + slot release).
        /// Marshals to the UI thread if needed; on the UI thread it runs synchronously.
        /// </summary>
        public void ForceEndWebVideoTakeover()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                OnWebVideoEnded(); // no UI to tear down; still release the flag + queue slot
                return;
            }
            if (dispatcher.CheckAccess())
                ForceEndWebVideoTakeoverCore();
            else
                dispatcher.BeginInvoke(ForceEndWebVideoTakeoverCore);
        }

        /// <summary>
        /// Synchronous body of <see cref="ForceEndWebVideoTakeover"/> — UI thread only.
        /// Callers that must guarantee "teardown happened before the queue slot frees"
        /// (stuck recovery, the duration hard stop's generation re-check) call this
        /// directly so no extra dispatcher hop can interleave a new video in between.
        /// </summary>
        internal void ForceEndWebVideoTakeoverCore()
        {
            try
            {
                var mainWindow = App.MainWindowRef
                    ?? Application.Current?.MainWindow as MainWindow
                    ?? Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
                mainWindow?.EndWebVideoTakeover();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AutonomyService: force-ending web video takeover failed");
            }
            finally
            {
                OnWebVideoEnded();
            }
        }

        // Generation counter for the duration-based hard stop. Separate from the load
        // watchdog generation: OnWebVideoStarted bumps that one (and started/duration
        // messages can arrive in either order), while only OnWebVideoEnded should cancel
        // a scheduled hard stop.
        private int _webVideoDurationGeneration;

        /// <summary>
        /// Called by MainWindow when the injected JS reports the video's real length.
        /// Schedules a hard stop at duration + grace: relying on the JS 'ended' event
        /// alone let the takeover run past the video — sites auto-advance to the next
        /// clip after 'ended' consumes the once-only handlers (#484). The 'ended' path
        /// is still the normal, earlier terminator.
        /// </summary>
        public void OnWebVideoDuration(double seconds)
        {
            if (!_webVideoActive) return;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return; // live streams etc.

            var gen = ++_webVideoDurationGeneration;
            var cap = TimeSpan.FromSeconds(Math.Min(seconds + 15, 3600)); // +15s grace, 1h sanity ceiling
            App.Logger?.Information("AutonomyService: Web video reports {Seconds:F0}s - hard stop scheduled at +{Cap:F0}s",
                seconds, cap.TotalSeconds);

            // Keep the interaction queue's stuck-recovery from force-ending a long video
            // mid-play (its default window is 5 minutes) — mirrors the native VLC path.
            // Type-guarded: if our best-effort claim failed (slot held by something else),
            // this must NOT stretch that other interaction's recovery window.
            App.InteractionQueue?.ExtendTimeout(cap.TotalSeconds, InteractionQueueService.InteractionType.WebVideo);

            Task.Delay(cap).ContinueWith(_ =>
            {
                if (!_webVideoActive || _webVideoDurationGeneration != gen) return;
                if (Application.Current?.Dispatcher == null) return;
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    // Re-check on the UI thread: the video may have ended (and a new one
                    // started) between the timer firing and this dispatch. The teardown
                    // must run synchronously HERE — another dispatcher hop between this
                    // check and the teardown would let a fresh video start and be killed.
                    if (!_webVideoActive || _webVideoDurationGeneration != gen) return;
                    App.Logger?.Information("AutonomyService: Web video ran past its reported length - force-ending takeover");
                    ForceEndWebVideoTakeoverCore();
                });
            });
        }

        /// <summary>
        /// Temporarily pulse brain drain to higher intensity
        /// </summary>
        private void PulseBrainDrain()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var baseIntensity = settings.BrainDrainIntensity;
            var pulseIntensity = Math.Min(100, baseIntensity + 30);

            // Increase intensity
            App.Overlay?.UpdateBrainDrainBlurOpacity(pulseIntensity);

            // Return to normal after 5 seconds
            Task.Delay(5000).ContinueWith(_ =>
            {
                if (Application.Current?.Dispatcher == null) return;
                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    App.Overlay?.UpdateBrainDrainBlurOpacity(baseIntensity);
                });
            });
        }

        /// <summary>
        /// Temporarily pulse spiral overlay on then off
        /// </summary>
        private void PulseSpiralOverlay()
        {
            // Skip if AI session is running - it controls overlays itself
            if (App.IsSessionRunning)
            {
                App.Logger?.Information("AutonomyService: Spiral pulse skipped - AI session is running");
                return;
            }

            // Prevent overlapping spiral pulses
            if (_spiralPulseActive)
            {
                App.Logger?.Information("AutonomyService: Spiral pulse skipped - already active");
                return;
            }

            var settings = App.Settings?.Current;
            if (settings == null)
            {
                App.Logger?.Warning("AutonomyService: Spiral pulse failed - settings is null");
                return;
            }

            _spiralPulseActive = true;
            var currentGeneration = ++_spiralPulseGeneration;
            var globalGen = _globalPulseGeneration;

            // Save current state - ONLY for spiral, don't touch pink filter
            // Also save to tracking fields for CancelActivePulses
            var wasEnabled = settings.SpiralEnabled;
            var baseOpacity = settings.SpiralOpacity;
            _originalSpiralEnabled = wasEnabled;
            _originalSpiralOpacity = baseOpacity;

            App.Logger?.Information("AutonomyService: Spiral pulse starting (gen {Gen}, global {Global}) - wasEnabled={Was}, baseOpacity={Opacity}",
                currentGeneration, globalGen, wasEnabled, baseOpacity);

            // Enable spiral with higher opacity
            // NOTE: We no longer disable pink filter - let both overlays coexist if needed
            settings.SpiralEnabled = true;
            settings.SpiralOpacity = Math.Min(100, Math.Max(30, baseOpacity + 20));

            // Start overlay service if needed
            if (App.Overlay?.IsRunning != true)
            {
                App.Overlay?.Start();
                App.Logger?.Information("AutonomyService: Spiral pulse - started overlay service");
            }
            App.Overlay?.RefreshOverlays();

            App.Logger?.Information("AutonomyService: Spiral pulse active (gen {Gen}), will restore in 30s", currentGeneration);

            // Return to original state after 30 seconds
            var capturedGeneration = currentGeneration;
            var capturedGlobalGen = globalGen;
            Task.Delay(30000).ContinueWith(_ =>
            {
                App.Logger?.Information("AutonomyService: Spiral pulse 30s delay complete (gen {Gen})", capturedGeneration);

                if (Application.Current?.Dispatcher == null)
                {
                    App.Logger?.Warning("AutonomyService: Spiral restore failed - Dispatcher is null");
                    return;
                }

                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    // Check if Stop() was called (global generation changed)
                    if (_globalPulseGeneration != capturedGlobalGen)
                    {
                        App.Logger?.Information("AutonomyService: Spiral restore skipped - Stop() was called");
                        return;
                    }

                    if (!_spiralPulseActive)
                    {
                        App.Logger?.Information("AutonomyService: Spiral restore skipped - no longer active");
                        return;
                    }

                    App.Logger?.Information("AutonomyService: Spiral pulse restoring original state (wasEnabled={Was})", wasEnabled);
                    _spiralPulseActive = false;

                    if (App.Settings?.Current != null)
                    {
                        // Restore ONLY spiral settings - don't touch pink filter
                        App.Settings.Current.SpiralEnabled = wasEnabled;
                        App.Settings.Current.SpiralOpacity = baseOpacity;
                        App.Overlay?.RefreshOverlays();

                        // Stop overlay if nothing needs it
                        var pinkOn = App.Settings.Current.PinkFilterEnabled;
                        var brainDrainOn = App.Settings.Current.BrainDrainEnabled;
                        if (!wasEnabled && !pinkOn && !brainDrainOn && !_pinkFilterPulseActive)
                        {
                            App.Overlay?.Stop();
                            App.Logger?.Information("AutonomyService: Spiral pulse - stopped overlay service");
                        }
                    }
                    App.Logger?.Information("AutonomyService: Spiral pulse ended (gen {Gen})", capturedGeneration);
                });
            });
        }

        /// <summary>
        /// Temporarily pulse pink filter on then off
        /// </summary>
        private void PulsePinkFilter()
        {
            // Skip if AI session is running - it controls overlays itself
            if (App.IsSessionRunning)
            {
                App.Logger?.Information("AutonomyService: Pink filter pulse skipped - AI session is running");
                return;
            }

            // Prevent overlapping pink filter pulses
            if (_pinkFilterPulseActive)
            {
                App.Logger?.Information("AutonomyService: Pink filter pulse skipped - already active");
                return;
            }

            var settings = App.Settings?.Current;
            if (settings == null)
            {
                App.Logger?.Warning("AutonomyService: Pink filter pulse failed - settings is null");
                return;
            }

            if (App.Overlay == null)
            {
                App.Logger?.Warning("AutonomyService: Pink filter pulse failed - App.Overlay is null");
                return;
            }

            _pinkFilterPulseActive = true;
            var currentGeneration = ++_pinkFilterPulseGeneration;
            var globalGen = _globalPulseGeneration;

            // Save current state - ONLY for pink filter, don't touch spiral
            // Also save to tracking fields for CancelActivePulses
            var wasEnabled = settings.PinkFilterEnabled;
            var baseOpacity = settings.PinkFilterOpacity;
            _originalPinkFilterEnabled = wasEnabled;
            _originalPinkFilterOpacity = baseOpacity;

            App.Logger?.Information("AutonomyService: Pink filter pulse starting (gen {Gen}, global {Global}) - wasEnabled={Was}, baseOpacity={Opacity}",
                currentGeneration, globalGen, wasEnabled, baseOpacity);

            // Enable pink filter with higher opacity
            // NOTE: We no longer disable spiral - let both overlays coexist if needed
            settings.PinkFilterEnabled = true;
            settings.PinkFilterOpacity = Math.Max(30, baseOpacity + 15);
            // The setter clamps to 0..50, so read back what it actually became. On restore we revert
            // ONLY if the value is still this — i.e. the user didn't move the slider during the pulse.
            // Otherwise frequent pulses keep snapping the user's chosen opacity back up, so it "can't
            // be lowered, always goes to 50" (#441a).
            var appliedOpacity = settings.PinkFilterOpacity;

            App.Logger?.Information("AutonomyService: Pink filter pulse - enabling overlay (wasRunning={WasRunning})",
                App.Overlay.IsRunning);

            // Start overlay service if needed
            if (!App.Overlay.IsRunning)
            {
                App.Overlay.Start();
                App.Logger?.Information("AutonomyService: Pink filter pulse - started overlay service");
            }

            App.Overlay.RefreshOverlays();

            App.Logger?.Information("AutonomyService: Pink filter pulse started (gen {Gen}), opacity={Opacity}%, duration=30s",
                currentGeneration, settings.PinkFilterOpacity);

            // Return to original state after 30 seconds
            var capturedGeneration = currentGeneration;
            Task.Delay(30000).ContinueWith(_ =>
            {
                App.Logger?.Information("AutonomyService: Pink filter pulse 30s delay complete (gen {Gen})", capturedGeneration);

                if (Application.Current?.Dispatcher == null)
                {
                    App.Logger?.Warning("AutonomyService: Pink filter restore failed - Dispatcher is null");
                    return;
                }

                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    // Check if Stop() was called (global generation changed)
                    if (_globalPulseGeneration != globalGen)
                    {
                        App.Logger?.Information("AutonomyService: Pink filter restore skipped - Stop() was called");
                        return;
                    }

                    if (!_pinkFilterPulseActive)
                    {
                        App.Logger?.Information("AutonomyService: Pink filter restore skipped - no longer active");
                        return;
                    }

                    App.Logger?.Information("AutonomyService: Pink filter pulse restoring original state (wasEnabled={Was})", wasEnabled);
                    _pinkFilterPulseActive = false;

                    if (App.Settings?.Current != null)
                    {
                        // Restore ONLY pink filter settings - don't touch spiral
                        App.Settings.Current.PinkFilterEnabled = wasEnabled;
                        // Undo our boost only if the user hasn't changed opacity during the pulse window;
                        // otherwise respect their manual value instead of snapping it back up (#441a).
                        if (Math.Abs(App.Settings.Current.PinkFilterOpacity - appliedOpacity) < 0.5)
                            App.Settings.Current.PinkFilterOpacity = baseOpacity;
                        App.Overlay?.RefreshOverlays();

                        // Stop overlay if nothing needs it
                        var spiralOn = App.Settings.Current.SpiralEnabled;
                        var brainDrainOn = App.Settings.Current.BrainDrainEnabled;
                        if (!wasEnabled && !spiralOn && !brainDrainOn && !_spiralPulseActive)
                        {
                            App.Overlay?.Stop();
                            App.Logger?.Information("AutonomyService: Pink filter pulse - stopped overlay service");
                        }
                    }
                    App.Logger?.Information("AutonomyService: Pink filter pulse ended (gen {Gen})", capturedGeneration);
                });
            });
        }

        /// <summary>
        /// Make an AI-generated comment through the avatar
        /// </summary>
        private void MakeComment(string? context)
        {
            var avatar = App.AvatarWindow;
            if (avatar == null) return;

            // Use AI if available
            if (App.Settings?.Current?.AiChatEnabled == true && App.Ai?.IsAvailable == true)
            {
                _ = MakeAICommentAsync(context);
            }
            else
            {
                // Fall back to preset phrase
                var phrases = new[]
                {
                    "*giggles* I love being with you~",
                    "You're doing so well~",
                    "Such a good girl~",
                    "Teehee~",
                    "I'm always watching~",
                    "*bounces* Pay attention to me~"
                };
                avatar.Giggle(phrases[_random.Next(phrases.Length)]);
            }
        }

        private async Task MakeAICommentAsync(string? context)
        {
            try
            {
                var prompt = context != null
                    ? $"Make a short teasing comment about {context}. Be playful and flirty."
                    : "Say something random and teasing to get attention. Be playful.";

                // R2-NEW-H-1: migrate to typed AI API. Refusals are silently dropped
                // on this autonomy surface (the user did not directly prompt — a
                // POLICY bubble out of nowhere is jarring; downstream guard already
                // logged via ModerationLog). IsAiGenerated now flows through so
                // canned fallbacks (offline, login-required) no longer wear the AI badge.
                var result = await App.Ai!.GetBambiReplyExAsync(prompt);
                if (result.Refusal == null && !string.IsNullOrEmpty(result.Text))
                {
                    Application.Current?.Dispatcher?.BeginInvoke(() =>
                    {
                        App.AvatarWindow?.GigglePriority(result.Text, false, aiGenerated: result.IsAiGenerated);
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Autonomy: AI comment failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Spoken Mantra — she voices an in-theme line that asks you to repeat a phrase, opens the mic
        /// once she's finished speaking, and answers with the entry's bespoke response. Per-mod content
        /// comes from <see cref="MantraVoiceService"/>. Fully additive and self-protecting: bails if
        /// speech isn't available, the mic is busy, or the active mod ships no mantras.
        /// </summary>
        private void TriggerSpokenMantra()
        {
            if (App.Speech?.IsAvailable != true || App.AvatarWindow == null)
            {
                App.Logger?.Information("AutonomyService: SpokenMantra skipped — speech unavailable");
                return;
            }
            if (App.MantraVoice?.HasMantras() != true)
            {
                App.Logger?.Information("AutonomyService: SpokenMantra skipped — no mantras for active mod");
                return;
            }
            // All four initiators (auto timer / wake-word / push-to-talk / dev test) funnel through
            // RequestVoiceCommand so the mic's single-session guard is never violated.
            RequestVoiceCommand();
        }

        private async Task RunSpokenMantraAsync()
        {
            try
            {
                var mantra = App.MantraVoice?.NextMantra();
                if (mantra == null)
                {
                    App.Logger?.Information("AutonomyService: SpokenMantra — no mantra to ask");
                    return;
                }

                var phrase = mantra.Phrase;
                try { VoicePromptStarted?.Invoke(this, phrase); } catch { }

                // She delivers the whole prompt (voiced if the clip ships, else text-only). Marshaled to
                // the UI thread since the funnel may invoke us off-thread (wake-word / PTT).
                var promptAudio = App.MantraVoice?.ResolveAudio(mantra.PromptAudio);
                Speak(mantra.PromptText, promptAudio);

                // CRITICAL: open the mic only AFTER she finishes saying the phrase — otherwise the
                // recognizer hears her own delivery and self-matches. Wait for the clip's measured
                // duration (+ a beat), then spin briefly until the avatar reports it's done speaking.
                var dur = App.MantraVoice?.GetAudioDuration(promptAudio);
                var settleMs = dur.HasValue ? (int)dur.Value.TotalMilliseconds + 600 : 1400;
                await Task.Delay(settleMs).ConfigureAwait(false);
                for (int i = 0; i < 40 && (App.AvatarWindow?.IsSpeaking ?? false); i++)
                    await Task.Delay(75).ConfigureAwait(false);

                // A beat longer than the old 8s so it's easier to get the whole phrase out in time.
                var listenWindow = TimeSpan.FromSeconds(10);

                var result = await App.Speech!.RecognizePhraseAsync(
                    phrase, new Services.Speech.RecognizeOptions { Timeout = listenWindow })
                    .ConfigureAwait(false);

                // One gentle retry on ANY non-match — too quiet, misheard, or nothing said — as long as
                // the engine is still available. Makes it much easier to land the phrase; only a clean
                // match (or an unavailable engine) skips the second try. The prompt fits the reason.
                if (!result.Matched && !result.Unavailable)
                {
                    var retryLine = result.LoudEnough == false
                        ? "Louder for me~ say it like you mean it."
                        : result.TimedOut && string.IsNullOrWhiteSpace(result.Transcript)
                            ? "Take your time~ say it again for me."
                            : "Mmm, almost~ say it once more, just for me.";
                    SpeakLine(App.MantraVoice?.GetRetry(), retryLine);
                    await Task.Delay(900).ConfigureAwait(false);
                    for (int i = 0; i < 40 && (App.AvatarWindow?.IsSpeaking ?? false); i++)
                        await Task.Delay(75).ConfigureAwait(false);
                    result = await App.Speech!.RecognizePhraseAsync(
                        phrase, new Services.Speech.RecognizeOptions { Timeout = listenWindow })
                        .ConfigureAwait(false);
                }

                if (result.Unavailable)
                {
                    App.Logger?.Information("AutonomyService: SpokenMantra — speech went unavailable mid-action");
                    return;
                }

                try { VoicePromptFinished?.Invoke(this, result); } catch { }

                if (result.Matched)
                {
                    // Bespoke voiced success response for this exact mantra.
                    var respAudio = App.MantraVoice?.ResolveAudio(mantra.ResponseAudio);
                    Speak(string.IsNullOrWhiteSpace(mantra.Response) ? "Good girl~" : mantra.Response, respAudio);
                    App.Mantra?.TryCompleteMantra();
                    App.Logger?.Information("AutonomyService: SpokenMantra matched '{Phrase}' (score={Score:0.00}, conf={Conf:0.00})",
                        phrase, result.Score, result.Confidence);
                }
                else if (result.TimedOut && string.IsNullOrWhiteSpace(result.Transcript))
                {
                    SpeakLine(App.MantraVoice?.GetTimeout(), "Too shy? I'll ask again later~");
                    App.Logger?.Information("AutonomyService: SpokenMantra timed out (no speech) for '{Phrase}'", phrase);
                }
                else
                {
                    SpeakLine(App.MantraVoice?.GetRetry(), "Mmm, not quite. Next time say it just for me~");
                    App.Logger?.Information("AutonomyService: SpokenMantra miss for '{Phrase}' — heard '{Heard}' (score={Score:0.00})",
                        phrase, result.Transcript, result.Score);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("AutonomyService: SpokenMantra failed: {Error}", ex.Message);
            }

            // Play a shared retry/timeout line (voiced if it ships audio), else fall back to plain text.
            void SpeakLine(Models.MantraLine? line, string fallback)
            {
                if (line != null && !string.IsNullOrWhiteSpace(line.Text))
                    Speak(line.Text, App.MantraVoice?.ResolveAudio(line.Audio));
                else
                    Speak(fallback, null);
            }

            void Speak(string text, string? audioPath)
            {
                if (Application.Current?.Dispatcher == null) return;
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        // Voiced when a clip resolved (bark voice path); text-only otherwise.
                        App.AvatarWindow?.GigglePriority(text, playSound: audioPath != null, aiGenerated: false,
                            phraseAudioPath: audioPath, barkVoice: audioPath != null);
                    }
                    catch { }
                });
            }
        }

        #endregion

        #region Announcements

        private bool ShouldAnnounce()
        {
            var chance = App.Settings?.Current?.AutonomyAnnouncementChance ?? 50;
            return _random.Next(100) < chance;
        }

        private void AnnounceAction(AutonomyActionType actionType)
        {
            if (!_announcementPhrases.TryGetValue(actionType, out var phrases))
                return;

            var phrase = phrases[_random.Next(phrases.Length)];

            // Voice the announcement when the active mod ships matching event audio (Sissy does);
            // otherwise it stays text-only as before. playSound mirrors whether audio was found.
            var audioPath = CompanionPhraseService.ResolveEventAudio(phrase);
            App.AvatarWindow?.GigglePriority(phrase, audioPath != null, aiGenerated: false, phraseAudioPath: audioPath);
            AnnouncementMade?.Invoke(this, phrase);
        }

        #endregion

        #region Mood System

        private void UpdateMood()
        {
            var hour = DateTime.Now.Hour;

            _currentMood = hour switch
            {
                >= 22 or < 6 => AutonomyMood.Mischievous,
                >= 18 => AutonomyMood.Playful,
                >= 12 => AutonomyMood.Attentive,
                _ => AutonomyMood.Gentle
            };
        }

        /// <summary>
        /// Get time-of-day intensity multiplier
        /// </summary>
        public double GetTimeMultiplier()
        {
            var settings = App.Settings?.Current;
            if (settings == null || !settings.AutonomyTimeAwareEnabled)
                return 1.0;

            var hour = DateTime.Now.Hour;

            return hour switch
            {
                >= 22 or < 6 => settings.AutonomyNightMultiplier,
                >= 18 => settings.AutonomyEveningMultiplier,
                >= 12 => settings.AutonomyAfternoonMultiplier,
                _ => settings.AutonomyMorningMultiplier
            };
        }

        #endregion

        #region Cooldown

        private void StartCooldown()
        {
            _isOnCooldown = true;

            var cooldownMs = (App.Settings?.Current?.AutonomyCooldownSeconds ?? 30) * 1000;

            _cooldownTimer?.Stop();
            _cooldownTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(cooldownMs)
            };
            _cooldownTimer.Tick += (s, e) =>
            {
                _cooldownTimer?.Stop();
                _isOnCooldown = false;
            };
            _cooldownTimer.Start();
        }

        #endregion

        #region Context Triggers

        /// <summary>
        /// Called by awareness system when context suggests an autonomous action
        /// </summary>
        public void OnContextTrigger(string context, string category)
        {
            var settings = App.Settings?.Current;
            if (settings == null || !settings.AutonomyContextTriggerEnabled) return;
            if (!CanTakeAction()) return;

            App.Logger?.Debug("AutonomyService: Context trigger ({Category}: {Context})", category, context);
            ExecuteAutonomousAction(AutonomyTriggerSource.Context, context);
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();
            // Stop() no longer tears down the mic (it's decoupled from Takeover) — but shutdown must.
            StopVoiceInputModes();
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
