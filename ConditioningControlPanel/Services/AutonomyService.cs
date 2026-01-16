using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

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
        BubbleCount
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
    public class AutonomyService : IDisposable
    {
        private const int LEVEL_REQUIREMENT = 100;

        // Timers
        private DispatcherTimer? _idleTimer;
        private DispatcherTimer? _randomTimer;
        private DispatcherTimer? _cooldownTimer;

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
        private int _pulseGeneration = 0; // Increment on each pulse to track stale callbacks

        // Mood system
        private AutonomyMood _currentMood = AutonomyMood.Playful;

        // Events
        public event EventHandler<AutonomyActionEventArgs>? ActionTriggered;
        public event EventHandler<string>? AnnouncementMade;

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
            }}
        };

        public bool IsEnabled => _isEnabled;

        /// <summary>
        /// Manually trigger an autonomous action for testing
        /// </summary>
        public void TestTrigger()
        {
            App.Logger?.Information("AutonomyService: TEST TRIGGER called manually!");

            if (!_isEnabled)
            {
                App.Logger?.Warning("AutonomyService: Test failed - service not enabled. Enable Autonomy Mode first!");
                System.Windows.MessageBox.Show("Autonomy Mode is not enabled!\n\nEnable the toggle first.", "Test Failed");
                return;
            }

            ExecuteAutonomousAction(AutonomyTriggerSource.Random, "Manual test trigger");
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
                App.Logger?.Warning("AutonomyService: Cannot start - requirements not met (need Level 100+, consent, and enabled)");
                return;
            }

            _isEnabled = true;
            _lastUserActivity = DateTime.Now;
            _lastActionTime = DateTime.MinValue;
            _isOnCooldown = false;

            StartIdleTimer();
            StartRandomTimer();
            UpdateMood();

            App.Logger?.Information("AutonomyService: Started successfully! (Intensity: {Intensity}, IdleEnabled: {Idle}, RandomEnabled: {Random})",
                settings?.AutonomyIntensity ?? 5,
                settings?.AutonomyIdleTriggerEnabled,
                settings?.AutonomyRandomTriggerEnabled);
        }

        /// <summary>
        /// Stop autonomous behavior
        /// </summary>
        public void Stop()
        {
            _isEnabled = false;
            StopAllTimers();

            // Reset all pulse flags to prevent stale callbacks from running
            _spiralPulseActive = false;
            _pinkFilterPulseActive = false;
            _bubblesPulseActive = false;
            _bouncingTextPulseActive = false;
            _pulseGeneration++; // Invalidate any pending callbacks

            App.Logger?.Information("AutonomyService: Stopped");
        }

        /// <summary>
        /// Check if autonomy can start
        /// </summary>
        private bool CanStart()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return false;

            return settings.AutonomyModeEnabled &&
                   settings.AutonomyConsentGiven &&
                   settings.PlayerLevel >= LEVEL_REQUIREMENT;
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
            if (settings == null || !settings.AutonomyIdleTriggerEnabled) return;

            var intervalMinutes = settings.AutonomyIdleTimeoutMinutes;

            _idleTimer?.Stop();
            _idleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(intervalMinutes)
            };
            _idleTimer.Tick += OnIdleTick;
            _idleTimer.Start();

            App.Logger?.Debug("AutonomyService: Idle timer started ({Minutes} min)", intervalMinutes);
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
            if (settings == null || !settings.AutonomyRandomTriggerEnabled) return;

            ScheduleNextRandomTick();
        }

        private void ScheduleNextRandomTick()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Add variance: 50% to 150% of base interval
            var baseMinutes = settings.AutonomyRandomIntervalMinutes;
            var variance = 0.5 + _random.NextDouble(); // 0.5 to 1.5
            var actualMinutes = baseMinutes * variance;

            _randomTimer?.Stop();
            _randomTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(actualMinutes)
            };
            _randomTimer.Tick += OnRandomTick;
            _randomTimer.Start();

            App.Logger?.Debug("AutonomyService: Random timer scheduled for {Minutes:F1} min", actualMinutes);
        }

        private void StopAllTimers()
        {
            _idleTimer?.Stop();
            _idleTimer = null;

            _randomTimer?.Stop();
            _randomTimer = null;

            _cooldownTimer?.Stop();
            _cooldownTimer = null;
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

            // Schedule next tick regardless of whether we take action
            ScheduleNextRandomTick();

            if (!CanTakeAction())
            {
                App.Logger?.Warning("AutonomyService: Random tick - cannot take action (cooldown or busy)");
                return;
            }

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
            if (!_isEnabled) return false;
            if (_isOnCooldown) return false;

            // Don't interrupt active fullscreen interaction (video, bubble count, lock card)
            if (App.InteractionQueue?.IsBusy == true) return false;

            // Check cooldown
            var settings = App.Settings?.Current;
            if (settings == null) return false;

            var timeSinceLast = (DateTime.Now - _lastActionTime).TotalSeconds;
            return timeSinceLast >= settings.AutonomyCooldownSeconds;
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

                if (shouldAnnounce)
                {
                    AnnounceAction(actionType.Value);

                    // Delay action after announcement
                    Task.Delay(2000).ContinueWith(_ =>
                    {
                        if (Application.Current?.Dispatcher == null) return;
                        Application.Current.Dispatcher.BeginInvoke(() =>
                        {
                            PerformAction(actionType.Value, source, context);
                        });
                    });
                }
                else
                {
                    PerformAction(actionType.Value, source, context);
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

            if (settings.AutonomyCanTriggerBrainDrain && settings.PlayerLevel >= 70)
                candidates.Add((AutonomyActionType.BrainDrainPulse, 10));

            if (settings.AutonomyCanTriggerBubbles && settings.PlayerLevel >= 20)
                candidates.Add((AutonomyActionType.StartBubbles, 15));

            if (settings.AutonomyCanComment)
                candidates.Add((AutonomyActionType.Comment, 20));

            // New progression features
            if (settings.AutonomyCanTriggerMindWipe && settings.PlayerLevel >= 80)
                candidates.Add((AutonomyActionType.MindWipe, 15));

            if (settings.AutonomyCanTriggerLockCard && settings.PlayerLevel >= 35)
                candidates.Add((AutonomyActionType.LockCard, 10)); // Lower weight - very disruptive

            if (settings.AutonomyCanTriggerSpiral && settings.PlayerLevel >= 10)
                candidates.Add((AutonomyActionType.SpiralPulse, 20));

            if (settings.AutonomyCanTriggerPinkFilter && settings.PlayerLevel >= 10)
                candidates.Add((AutonomyActionType.PinkFilterPulse, 20));

            if (settings.AutonomyCanTriggerBouncingText && settings.PlayerLevel >= 60)
                candidates.Add((AutonomyActionType.BouncingText, 15));

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

        private void PerformAction(AutonomyActionType actionType, AutonomyTriggerSource source, string? context)
        {
            if (Application.Current?.Dispatcher == null) return;

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    switch (actionType)
                    {
                        case AutonomyActionType.Flash:
                            App.Flash?.TriggerFlashOnce();
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
                                    Application.Current.Dispatcher.BeginInvoke(() =>
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
                                    Application.Current.Dispatcher.BeginInvoke(() =>
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
                    }

                    App.Logger?.Information("Autonomy: Performed {Action} (Source: {Source})",
                        actionType, source);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Autonomy: Failed to perform {Action}", actionType);
                }
            });
        }

        /// <summary>
        /// Trigger video safely - NEVER uses strict mode
        /// </summary>
        private void TriggerVideoSafely()
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Store original strict mode state
            var wasStrict = settings.StrictLockEnabled;

            // Temporarily disable strict mode for autonomous video
            settings.StrictLockEnabled = false;

            App.Video?.TriggerVideo();

            // Restore strict mode after a delay (after video starts)
            Task.Delay(3000).ContinueWith(_ =>
            {
                if (Application.Current?.Dispatcher == null) return;
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (App.Settings?.Current != null)
                    {
                        App.Settings.Current.StrictLockEnabled = wasStrict;
                    }
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
                Application.Current.Dispatcher.BeginInvoke(() =>
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
            // Prevent overlapping spiral pulses
            if (_spiralPulseActive)
            {
                App.Logger?.Debug("AutonomyService: Spiral pulse skipped - already active");
                return;
            }

            var settings = App.Settings?.Current;
            if (settings == null) return;

            _spiralPulseActive = true;
            var currentGeneration = ++_pulseGeneration;

            // Save current state
            var wasEnabled = settings.SpiralEnabled;
            var baseOpacity = settings.SpiralOpacity;
            var wasPinkEnabled = settings.PinkFilterEnabled;

            // Enable spiral with higher opacity
            settings.SpiralEnabled = true;
            settings.SpiralOpacity = Math.Min(100, Math.Max(30, baseOpacity + 20));

            // Temporarily disable pink filter so only spiral shows during this pulse
            settings.PinkFilterEnabled = false;

            // Start overlay service if needed
            if (App.Overlay?.IsRunning != true)
            {
                App.Overlay?.Start();
            }
            App.Overlay?.RefreshOverlays();

            App.Logger?.Debug("AutonomyService: Spiral pulse started (gen {Gen})", currentGeneration);

            // Return to original state after 30 seconds
            Task.Delay(30000).ContinueWith(_ =>
            {
                if (Application.Current?.Dispatcher == null) return;
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    // Only restore if this is still the active pulse
                    if (!_spiralPulseActive)
                    {
                        App.Logger?.Debug("AutonomyService: Spiral restore skipped - no longer active");
                        return;
                    }

                    _spiralPulseActive = false;

                    if (App.Settings?.Current != null)
                    {
                        // Restore all settings
                        App.Settings.Current.SpiralEnabled = wasEnabled;
                        App.Settings.Current.SpiralOpacity = baseOpacity;
                        App.Settings.Current.PinkFilterEnabled = wasPinkEnabled;
                        App.Overlay?.RefreshOverlays();

                        // Stop overlay if nothing needs it
                        if (!wasEnabled && !wasPinkEnabled && !App.Settings.Current.BrainDrainEnabled)
                        {
                            App.Overlay?.Stop();
                        }
                    }
                    App.Logger?.Debug("AutonomyService: Spiral pulse ended (gen {Gen})", currentGeneration);
                });
            });
        }

        /// <summary>
        /// Temporarily pulse pink filter on then off
        /// </summary>
        private void PulsePinkFilter()
        {
            // Prevent overlapping pink filter pulses
            if (_pinkFilterPulseActive)
            {
                App.Logger?.Debug("AutonomyService: Pink filter pulse skipped - already active");
                return;
            }

            var settings = App.Settings?.Current;
            if (settings == null) return;

            _pinkFilterPulseActive = true;
            var currentGeneration = ++_pulseGeneration;

            // Save current state
            var wasEnabled = settings.PinkFilterEnabled;
            var baseOpacity = settings.PinkFilterOpacity;
            var wasSpiralEnabled = settings.SpiralEnabled;

            // Enable pink filter with higher opacity
            settings.PinkFilterEnabled = true;
            settings.PinkFilterOpacity = Math.Max(30, baseOpacity + 15);

            // Temporarily disable spiral so only pink filter shows during this pulse
            settings.SpiralEnabled = false;

            // Start overlay service if needed
            if (App.Overlay?.IsRunning != true)
            {
                App.Overlay?.Start();
            }
            App.Overlay?.RefreshOverlays();

            App.Logger?.Debug("AutonomyService: Pink filter pulse started (gen {Gen}), opacity={Opacity}",
                currentGeneration, settings.PinkFilterOpacity);

            // Return to original state after 30 seconds
            Task.Delay(30000).ContinueWith(_ =>
            {
                if (Application.Current?.Dispatcher == null) return;
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    // Only restore if this is still the active pulse
                    if (!_pinkFilterPulseActive)
                    {
                        App.Logger?.Debug("AutonomyService: Pink filter restore skipped - no longer active");
                        return;
                    }

                    _pinkFilterPulseActive = false;

                    if (App.Settings?.Current != null)
                    {
                        // Restore all settings
                        App.Settings.Current.PinkFilterEnabled = wasEnabled;
                        App.Settings.Current.PinkFilterOpacity = baseOpacity;
                        App.Settings.Current.SpiralEnabled = wasSpiralEnabled;
                        App.Overlay?.RefreshOverlays();

                        // Stop overlay if nothing needs it
                        if (!wasEnabled && !wasSpiralEnabled && !App.Settings.Current.BrainDrainEnabled)
                        {
                            App.Overlay?.Stop();
                        }
                    }
                    App.Logger?.Debug("AutonomyService: Pink filter pulse ended (gen {Gen})", currentGeneration);
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

                var response = await App.Ai!.GetBambiReplyAsync(prompt);
                if (!string.IsNullOrEmpty(response))
                {
                    Application.Current?.Dispatcher?.BeginInvoke(() =>
                    {
                        App.AvatarWindow?.GigglePriority(response, false);
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Autonomy: AI comment failed: {Error}", ex.Message);
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

            App.AvatarWindow?.GigglePriority(phrase, false);
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
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
