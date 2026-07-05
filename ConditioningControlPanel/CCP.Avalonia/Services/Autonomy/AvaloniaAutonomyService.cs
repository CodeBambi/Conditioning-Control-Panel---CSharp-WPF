using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Core;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Autonomy;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.LockCard;
using ConditioningControlPanel.Core.Services.MindWipe;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Services.Video;

namespace ConditioningControlPanel.Avalonia.Services.Autonomy;

/// <summary>
/// Avalonia implementation of autonomous companion behavior ("Bambi Takeover").
/// Schedules idle/random actions based on settings and dispatches them to the
/// cross-platform effect services. Mobile heads are excluded because overlays
/// and multi-window effects are not supported there.
/// </summary>
public sealed partial class AvaloniaAutonomyService : IAutonomyService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IInteractionQueueService _interactionQueue;
    private readonly IInputHook? _inputHook;
    private readonly IAppEnvironment? _appEnvironment;
    private readonly IModService? _mods;
    private readonly ILogger<AvaloniaAutonomyService>? _logger;
    private readonly Random _random = new();

    private readonly IFlashService? _flash;
    private readonly IVideoService? _video;
    private readonly ISubliminalService? _subliminal;
    private readonly IMindWipeService? _mindWipe;
    private readonly ILockCardService? _lockCard;
    private readonly IBubbleService? _bubbles;
    private readonly IBouncingTextService? _bouncingText;
    private readonly IBubbleCountService? _bubbleCount;
    private readonly IOverlayService? _overlay;
    private readonly IWallpaperProvider? _wallpaper;
    private readonly IAvatarWindowService? _avatar;

    // Voice ("Hey Bambi") dependencies — see AvaloniaAutonomyService.Voice.cs for the wake loop + dispatch.
    private readonly ConditioningControlPanel.Core.Services.Speech.ISpeechRecognitionService? _speech;
    private readonly ConditioningControlPanel.Core.Services.Speech.ISpeechWakeService? _wakeWord;
    private readonly ConditioningControlPanel.Core.Services.Bark.IBarkManifestService? _barkManifest;
    private readonly ConditioningControlPanel.Core.Services.Mantra.IMantraVoiceService? _mantraVoice;
    private readonly ConditioningControlPanel.IKeywordTriggerService? _keywordTriggers;
    private readonly ConditioningControlPanel.Core.Services.Quiz.IPopQuizService? _popQuiz;

    private bool _isEnabled;
    private bool _forceTestMode;
    private bool _disposed;
    private DateTime _lastActionTime = DateTime.MinValue;
    private DateTime _lastUserActivity = DateTime.Now;
    private bool _isOnCooldown;
    private bool _webVideoActive;
    private int _webVideoWatchdogGeneration;
    private readonly HashSet<string> _shownWebVideos = new(StringComparer.OrdinalIgnoreCase);

    private DispatcherTimer? _idleTimer;
    private DispatcherTimer? _randomTimer;
    // Anchor for the avatar countdown bar: when the next random fire is due, and the interval it
    // was scheduled over (so NextRandomFireFraction can report remaining/total).
    private DateTime _nextRandomFireAt;
    private double _nextRandomIntervalSeconds;
    private DispatcherTimer? _cooldownTimer;
    private DispatcherTimer? _heartbeatTimer;

    private AutonomyMood _currentMood = AutonomyMood.Playful;

    public bool IsEnabled => _isEnabled;
    public bool IsIdleTimerRunning => _idleTimer != null;
    public bool IsRandomTimerRunning => _randomTimer != null;

    /// <inheritdoc />
    public event EventHandler<bool>? EnabledChanged;

    /// <inheritdoc />
    public double? NextRandomFireFraction
    {
        get
        {
            if (_randomTimer == null || _nextRandomIntervalSeconds <= 0) return null;
            var remaining = (_nextRandomFireAt - DateTime.UtcNow).TotalSeconds;
            if (remaining <= 0) return 0;
            return Math.Clamp(remaining / _nextRandomIntervalSeconds, 0.0, 1.0);
        }
    }
    public bool IsActionInProgress { get; private set; }

    public event EventHandler<AutonomyActionEventArgs>? ActionTriggered;
    public event EventHandler<string>? AnnouncementMade;

    private static readonly Dictionary<AutonomyActionType, string[]> AnnouncementPhrases = new()
    {
        { AutonomyActionType.Flash, new[] { "Time for a little surprise~", "Here comes something pretty!", "Look at the screen, good girl~", "Ooh, I want to show you something~", "Pretty picture time~" } },
        { AutonomyActionType.Video, new[] { "Video time! Get comfy~", "I have something to show you...", "Time to watch and absorb~", "Sit back and watch~", "Let's watch something together~" } },
        { AutonomyActionType.Subliminal, new[] { "Just a little message for you~", "*giggles* Did you see that?", "Shhh, just let it sink in~", "A little reminder~", "Don't think, just absorb~" } },
        { AutonomyActionType.BrainDrainPulse, new[] { "Let me blur your thoughts~", "Time to get fuzzy~", "Thinking is overrated~", "Let it all go blurry~" } },
        { AutonomyActionType.StartBubbles, new[] { "Pop pop pop!", "Let's play~", "Bubble time!", "Click the bubbles~" } },
        { AutonomyActionType.Comment, new[] { "*giggles*", "Teehee~", "Just thinking about you~" } },
        { AutonomyActionType.MindWipe, new[] { "Let me wipe your thoughts~", "Shhh... empty mind~", "No more thinking~", "Time to forget~" } },
        { AutonomyActionType.LockCard, new[] { "Time to earn a reward~", "Complete this for me~", "Show me how good you are~", "Task time~" } },
        { AutonomyActionType.SpiralPulse, new[] { "Watch the pretty spiral~", "Spirals are so pretty...", "Look at the swirls~", "Round and round~" } },
        { AutonomyActionType.PinkFilterPulse, new[] { "Everything looks better in pink~", "Pink is your color~", "So pretty and pink~", "Pink thoughts~" } },
        { AutonomyActionType.BouncingText, new[] { "Read the pretty words~", "Follow the bouncing text~", "Words to remember~", "Let them sink in~" } },
        { AutonomyActionType.BubbleCount, new[] { "Count with me~", "How many bubbles?", "Test your focus~", "Counting game time~" } },
        { AutonomyActionType.WebVideo, new[] { "Time to watch something special~", "I picked a video just for you~", "Sit back and let it sink in~", "Watch and absorb~", "Fullscreen time~" } },
        { AutonomyActionType.WallpaperShuffle, new[] { "New scenery for you~", "Let me redecorate~", "A little change of view~", "How about this one?~" } }
    };

    public AvaloniaAutonomyService(
        ISettingsService settings,
        IInteractionQueueService interactionQueue,
        IInputHook? inputHook = null,
        IAppEnvironment? appEnvironment = null,
        IModService? mods = null,
        IFlashService? flash = null,
        IVideoService? video = null,
        ISubliminalService? subliminal = null,
        IMindWipeService? mindWipe = null,
        ILockCardService? lockCard = null,
        IBubbleService? bubbles = null,
        IBouncingTextService? bouncingText = null,
        IBubbleCountService? bubbleCount = null,
        IOverlayService? overlay = null,
        IWallpaperProvider? wallpaper = null,
        IAvatarWindowService? avatar = null,
        ConditioningControlPanel.Core.Services.Speech.ISpeechRecognitionService? speech = null,
        ConditioningControlPanel.Core.Services.Speech.ISpeechWakeService? wakeWord = null,
        ConditioningControlPanel.Core.Services.Bark.IBarkManifestService? barkManifest = null,
        ConditioningControlPanel.Core.Services.Mantra.IMantraVoiceService? mantraVoice = null,
        ConditioningControlPanel.IKeywordTriggerService? keywordTriggers = null,
        ConditioningControlPanel.Core.Services.Quiz.IPopQuizService? popQuiz = null,
        ILogger<AvaloniaAutonomyService>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _interactionQueue = interactionQueue ?? throw new ArgumentNullException(nameof(interactionQueue));
        _inputHook = inputHook;
        _appEnvironment = appEnvironment;
        _mods = mods;
        _flash = flash;
        _video = video;
        _subliminal = subliminal;
        _mindWipe = mindWipe;
        _lockCard = lockCard;
        _bubbles = bubbles;
        _bouncingText = bouncingText;
        _bubbleCount = bubbleCount;
        _overlay = overlay;
        _wallpaper = wallpaper;
        _avatar = avatar;
        _speech = speech;
        _wakeWord = wakeWord;
        _barkManifest = barkManifest;
        _mantraVoice = mantraVoice;
        _keywordTriggers = keywordTriggers;
        _popQuiz = popQuiz;
        _logger = logger;
    }

    public void Start()
    {
        if (_isEnabled) return;
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            _logger?.LogDebug("AvaloniaAutonomyService: overlays are not supported on mobile; Start is a no-op");
            return;
        }

        var settings = _settings.Current;
        if (settings == null || !settings.AutonomyModeEnabled || !settings.AutonomyConsentGiven)
        {
            _logger?.LogWarning("AvaloniaAutonomyService: Cannot start - requirements not met (need enabled + consent)");
            return;
        }

        _isEnabled = true;
        _forceTestMode = false;
        _lastUserActivity = DateTime.Now;
        UpdateMood();
        StartIdleTimer();
        StartRandomTimer();
        StartHeartbeatTimer();
        SubscribeInputHook();

        _logger?.LogInformation("AvaloniaAutonomyService started. Intensity={Intensity}, Idle={Idle}, Random={Random}, Interval={Interval}s",
            settings.AutonomyIntensity,
            settings.AutonomyIdleTriggerEnabled,
            settings.AutonomyRandomTriggerEnabled,
            settings.AutonomyRandomIntervalSeconds);

        // Arm the "Hey Bambi" wake loop if mic consent is given + the engine is available. Voice is
        // otherwise decoupled from Takeover (the She's Listening UI also calls RefreshVoiceInputModes).
        RefreshVoiceInputModes();

        try { EnabledChanged?.Invoke(this, true); } catch { }
    }

    public void Stop()
    {
        if (!_isEnabled) return;
        _isEnabled = false;
        try { EnabledChanged?.Invoke(this, false); } catch { }
        _forceTestMode = false;

        _idleTimer?.Stop();
        _idleTimer = null;
        _randomTimer?.Stop();
        _randomTimer = null;
        _heartbeatTimer?.Stop();
        _heartbeatTimer = null;
        _cooldownTimer?.Stop();
        _cooldownTimer = null;

        UnsubscribeInputHook();
        CancelActivePulses();
        _webVideoActive = false;

        _logger?.LogInformation("AvaloniaAutonomyService stopped");
    }

    public void ForceStart()
    {
        _logger?.LogWarning("AvaloniaAutonomyService: FORCE START called - bypassing all checks!");

        Stop();
        _isEnabled = true;
        _forceTestMode = true;
        _lastUserActivity = DateTime.Now;
        UpdateMood();

        StartIdleTimer();
        StartRandomTimer(testIntervalSeconds: 30);
        StartHeartbeatTimer();

        _logger?.LogInformation("AvaloniaAutonomyService: FORCE STARTED - Test mode enabled (30s intervals)");
    }

    public void TestTrigger()
    {
        _logger?.LogInformation("AvaloniaAutonomyService: TEST TRIGGER called manually!");
        if (!_isEnabled)
        {
            _logger?.LogWarning("AvaloniaAutonomyService: Test failed - service not enabled. Enable Autonomy Mode first!");
            return;
        }
        ExecuteAutonomousAction(AutonomyTriggerSource.Context, "Test trigger");
    }

    public void CancelActivePulses()
    {
        _logger?.LogInformation("AvaloniaAutonomyService: CancelActivePulses called");
        // Timed overlays self-cancel; sustained pulses are not currently used.
    }

    public string GetDiagnosticStatus()
    {
        var settings = _settings.Current;
        var lines = new List<string>
        {
            $"Service Enabled: {_isEnabled}",
            $"Idle Timer Running: {IsIdleTimerRunning}",
            $"Random Timer Running: {IsRandomTimerRunning}",
            $"On Cooldown: {_isOnCooldown}",
            $"Interaction Queue Busy: {_interactionQueue.IsBusy}",
            $"Last Action: {(_lastActionTime == DateTime.MinValue ? "Never" : _lastActionTime.ToString("HH:mm:ss"))}",
            $"Current Mood: {_currentMood}",
            "",
            "Settings:",
            $"  AutonomyModeEnabled: {settings?.AutonomyModeEnabled}",
            $"  AutonomyConsentGiven: {settings?.AutonomyConsentGiven}",
            $"  IdleTriggerEnabled: {settings?.AutonomyIdleTriggerEnabled}",
            $"  RandomTriggerEnabled: {settings?.AutonomyRandomTriggerEnabled}",
            $"  RandomIntervalSeconds: {settings?.AutonomyRandomIntervalSeconds}",
            $"  CooldownSeconds: {settings?.AutonomyCooldownSeconds}",
            "",
            "Enabled Actions:",
            $"  Flash: {settings?.AutonomyCanTriggerFlash}",
            $"  Video: {settings?.AutonomyCanTriggerVideo}",
            $"  Subliminal: {settings?.AutonomyCanTriggerSubliminal}",
            $"  Bubbles: {settings?.AutonomyCanTriggerBubbles}",
            $"  MindWipe: {settings?.AutonomyCanTriggerMindWipe}",
            $"  LockCard: {settings?.AutonomyCanTriggerLockCard}",
            $"  Spiral: {settings?.AutonomyCanTriggerSpiral}",
            $"  PinkFilter: {settings?.AutonomyCanTriggerPinkFilter}",
            $"  BouncingText: {settings?.AutonomyCanTriggerBouncingText}",
            $"  BubbleCount: {settings?.AutonomyCanTriggerBubbleCount}"
        };
        return string.Join("\n", lines);
    }

    private void StartIdleTimer()
    {
        var settings = _settings.Current;
        if (settings == null || !settings.AutonomyIdleTriggerEnabled)
        {
            _logger?.LogDebug("AvaloniaAutonomyService: Idle timer NOT started - AutonomyIdleTriggerEnabled is false");
            return;
        }

        // Interval ticks every 10s; idle is approximated as no autonomy action for the configured idle period.
        _idleTimer = StartPeriodicTimer(TimeSpan.FromSeconds(10), () =>
        {
            if (!_isEnabled) return;
            var s = _settings.Current;
            if (s == null || !s.AutonomyIdleTriggerEnabled) return;

            var idleMinutes = (DateTime.Now - _lastUserActivity).TotalMinutes;
            var requiredMinutes = GetIdleMinutesForIntensity(s.AutonomyIntensity);
            if (idleMinutes >= requiredMinutes)
            {
                _logger?.LogDebug("AvaloniaAutonomyService: Idle timeout triggered");
                ExecuteAutonomousAction(AutonomyTriggerSource.Idle);
            }
        });
    }

    private void StartRandomTimer(int? testIntervalSeconds = null)
    {
        var settings = _settings.Current;
        if (settings == null || (!testIntervalSeconds.HasValue && !settings.AutonomyRandomTriggerEnabled))
        {
            _logger?.LogDebug("AvaloniaAutonomyService: Random timer NOT started - AutonomyRandomTriggerEnabled is false");
            return;
        }

        ScheduleNextRandomTick(testIntervalSeconds);
    }

    private void ScheduleNextRandomTick(int? testIntervalSeconds = null)
    {
        if (!_isEnabled) return;

        double intervalSeconds;
        if (testIntervalSeconds.HasValue)
        {
            intervalSeconds = testIntervalSeconds.Value;
        }
        else
        {
            var settings = _settings.Current;
            if (settings == null || !settings.AutonomyRandomTriggerEnabled) return;

            var baseInterval = settings.AutonomyRandomIntervalSeconds;
            var variance = baseInterval * 0.3;
            intervalSeconds = baseInterval + (_random.NextDouble() * variance * 2 - variance);
            intervalSeconds = Math.Max(10, intervalSeconds);
        }

        _nextRandomFireAt = DateTime.UtcNow.AddSeconds(intervalSeconds);
        _nextRandomIntervalSeconds = intervalSeconds;

        _randomTimer = StartOneShotTimer(TimeSpan.FromSeconds(intervalSeconds), () =>
        {
            if (!_isEnabled) return;
            _logger?.LogInformation("AvaloniaAutonomyService: Random timer FIRED!");

            var s = _settings.Current;
            if (s == null || (!_forceTestMode && !s.AutonomyRandomTriggerEnabled)) return;

            if (CanTakeAction())
            {
                ExecuteAutonomousAction(AutonomyTriggerSource.Random);
            }
            else
            {
                _logger?.LogDebug("AvaloniaAutonomyService: Random tick - cannot take action (cooldown or busy)");
            }

            ScheduleNextRandomTick(testIntervalSeconds);
        });
    }

    private void StartHeartbeatTimer()
    {
        _heartbeatTimer = StartPeriodicTimer(TimeSpan.FromSeconds(30), () =>
        {
            if (!_isEnabled) return;
            _logger?.LogDebug("AvaloniaAutonomyService: heartbeat - IsEnabled={Enabled}, Idle={Idle}, Random={Random}, Cooldown={Cooldown}, Busy={Busy}",
                _isEnabled,
                IsIdleTimerRunning,
                IsRandomTimerRunning,
                _isOnCooldown,
                _interactionQueue.IsBusy);
        });
    }

    private void SubscribeInputHook()
    {
        if (_inputHook == null) return;
        _inputHook.KeyPressed += OnInputHookKeyPressed;
        _inputHook.MouseMoved += OnInputHookMouseMoved;
    }

    private void UnsubscribeInputHook()
    {
        if (_inputHook == null) return;
        _inputHook.KeyPressed -= OnInputHookKeyPressed;
        _inputHook.MouseMoved -= OnInputHookMouseMoved;
    }

    private void OnInputHookKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        _lastUserActivity = DateTime.Now;
    }

    private void OnInputHookMouseMoved(object? sender, MouseHookEventArgs e)
    {
        _lastUserActivity = DateTime.Now;
    }

    private double GetIdleMinutesForIntensity(int intensity)
    {
        // Higher intensity = shorter idle timeout.
        var baseMinutes = 10 - intensity / 10.0;
        return Math.Clamp(baseMinutes, 1, 20);
    }

    private bool CanTakeAction()
    {
        if (!_isEnabled) return false;
        if (_isOnCooldown) return false;
        if (_webVideoActive) return false;
        if (_interactionQueue.IsBusy) return false;

        var settings = _settings.Current;
        if (settings == null) return false;

        var elapsed = (DateTime.Now - _lastActionTime).TotalSeconds;
        if (elapsed < settings.AutonomyCooldownSeconds) return false;

        return true;
    }

    private void ExecuteAutonomousAction(AutonomyTriggerSource source, string? context = null)
    {
        _logger?.LogInformation("AvaloniaAutonomyService: ExecuteAutonomousAction called (Source: {Source})", source);

        var actionType = SelectAction();
        if (actionType == null)
        {
            _logger?.LogWarning("AvaloniaAutonomyService: No valid action available - check if any actions are enabled!");
            return;
        }

        var settings = _settings.Current;
        var announceChance = settings?.AutonomyAnnouncementChance ?? 50;
        var shouldAnnounce = _random.Next(100) < announceChance;

        _logger?.LogInformation("AvaloniaAutonomyService: Selected action: {Action}, Will announce: {Announce}", actionType, shouldAnnounce);

        if (shouldAnnounce && actionType != AutonomyActionType.Comment)
        {
            var phrase = GetRandomPhrase(actionType.Value);
            AnnouncementMade?.Invoke(this, phrase);
            _logger?.LogInformation("AvaloniaAutonomyService: Announcement made, scheduling action in 2 seconds...");

            StartOneShotTimer(TimeSpan.FromSeconds(2), () =>
            {
                if (!_isEnabled) return;
                PerformAction(actionType.Value, source, context);
            });
        }
        else
        {
            PerformAction(actionType.Value, source, context);
        }
    }

    private AutonomyActionType? SelectAction()
    {
        var settings = _settings.Current;
        if (settings == null) return null;

        var candidates = new List<AutonomyActionType>();
        if (settings.AutonomyCanTriggerFlash) candidates.Add(AutonomyActionType.Flash);
        if (settings.AutonomyCanTriggerVideo) candidates.Add(AutonomyActionType.Video);
        if (settings.AutonomyCanTriggerSubliminal) candidates.Add(AutonomyActionType.Subliminal);
        if (settings.AutonomyCanTriggerBubbles) candidates.Add(AutonomyActionType.StartBubbles);
        if (settings.AutonomyCanComment) candidates.Add(AutonomyActionType.Comment);
        if (settings.AutonomyCanTriggerMindWipe) candidates.Add(AutonomyActionType.MindWipe);
        if (settings.AutonomyCanTriggerLockCard) candidates.Add(AutonomyActionType.LockCard);
        if (settings.AutonomyCanTriggerSpiral) candidates.Add(AutonomyActionType.SpiralPulse);
        if (settings.AutonomyCanTriggerPinkFilter) candidates.Add(AutonomyActionType.PinkFilterPulse);
        if (settings.AutonomyCanTriggerBouncingText) candidates.Add(AutonomyActionType.BouncingText);
        if (settings.AutonomyCanTriggerBubbleCount) candidates.Add(AutonomyActionType.BubbleCount);
        if (settings.AutonomyCanTriggerWebVideo) candidates.Add(AutonomyActionType.WebVideo);
        if (settings.AutonomyCanTriggerWallpaper) candidates.Add(AutonomyActionType.WallpaperShuffle);

        // Brain drain is not a configurable flag in current settings; omit unless explicitly added.

        if (candidates.Count == 0) return null;
        return candidates[_random.Next(candidates.Count)];
    }

    private string GetRandomPhrase(AutonomyActionType actionType)
    {
        if (AnnouncementPhrases.TryGetValue(actionType, out var phrases) && phrases.Length > 0)
            return phrases[_random.Next(phrases.Length)];
        return "*giggles*";
    }

    private string? GetRandomSubliminalPhrase()
    {
        var settings = _settings.Current;
        if (settings == null) return null;
        var active = settings.SubliminalPool.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
        return active.Count > 0 ? active[_random.Next(active.Count)] : null;
    }

    private void PerformAction(AutonomyActionType actionType, AutonomyTriggerSource source, string? context)
    {
        _logger?.LogInformation("AvaloniaAutonomyService: PerformAction starting - {Action}", actionType);
        ActionTriggered?.Invoke(this, new AutonomyActionEventArgs(actionType, source, context));
        IsActionInProgress = true;

        try
        {
            switch (actionType)
            {
                case AutonomyActionType.Flash:
                    _flash?.TriggerFlashOnce(null, 2000, true, false);
                    break;
                case AutonomyActionType.Subliminal:
                    var subliminalPhrase = GetRandomSubliminalPhrase();
                    if (!string.IsNullOrEmpty(subliminalPhrase))
                        _subliminal?.FlashSubliminalCustom(subliminalPhrase);
                    break;
                case AutonomyActionType.MindWipe:
                    _mindWipe?.TriggerOnce();
                    break;
                case AutonomyActionType.LockCard:
                    _lockCard?.ShowLockCard(isTest: true);
                    break;
                case AutonomyActionType.BubbleCount:
                    _bubbleCount?.TriggerGame(forceTest: true);
                    break;
                case AutonomyActionType.StartBubbles:
                    if (_bubbles?.IsRunning != true)
                    {
                        _bubbles?.Start();
                        AutoStopAfter("bubbles", () => _bubbles?.Stop(), TimeSpan.FromSeconds(30));
                    }
                    break;
                case AutonomyActionType.BouncingText:
                    if (_bouncingText?.IsRunning != true)
                    {
                        _bouncingText?.Start();
                        AutoStopAfter("bouncing text", () => _bouncingText?.Stop(), TimeSpan.FromSeconds(30));
                    }
                    break;
                case AutonomyActionType.SpiralPulse:
                    _overlay?.ShowOverlayTimed("spiral", 3000, 0.7);
                    break;
                case AutonomyActionType.PinkFilterPulse:
                    _overlay?.ShowOverlayTimed("pink", 3000, 0.7);
                    break;
                case AutonomyActionType.BrainDrainPulse:
                    _overlay?.ShowOverlayTimed("braindrain", 3000, 0.7);
                    break;
                case AutonomyActionType.Comment:
                    _avatar?.Giggle(GetRandomPhrase(AutonomyActionType.Comment));
                    break;
                case AutonomyActionType.Video:
                    _video?.PlayRandomVideo();
                    break;
                case AutonomyActionType.WebVideo:
                    PlayRandomWebVideo();
                    break;
                case AutonomyActionType.WallpaperShuffle:
                    ShuffleWallpaper();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AvaloniaAutonomyService: Action {Action} failed", actionType);
        }
        finally
        {
            _lastActionTime = DateTime.Now;
            _lastUserActivity = DateTime.Now;
            StartCooldown();
            IsActionInProgress = false;
        }
    }

    private void PlayRandomWebVideo()
    {
        if (_video == null) return;

        var links = _mods?.GetVideoLinks();
        if (links == null || links.Count == 0)
        {
            _logger?.LogDebug("AvaloniaAutonomyService: No web video links available");
            return;
        }

        var available = links.Where(kvp => !_shownWebVideos.Contains(kvp.Key)).ToList();
        if (available.Count == 0)
        {
            _shownWebVideos.Clear();
            available = links.ToList();
        }

        var selected = available[_random.Next(available.Count)];
        _shownWebVideos.Add(selected.Key);

        _webVideoActive = true;
        var watchdogGen = ++_webVideoWatchdogGeneration;
        StartOneShotTimer(TimeSpan.FromSeconds(30), () =>
        {
            if (_webVideoActive && _webVideoWatchdogGeneration == watchdogGen)
            {
                _webVideoActive = false;
                _logger?.LogWarning("AvaloniaAutonomyService: Web video watchdog fired - resetting stuck active flag");
            }
        });

        try
        {
            // The URL is routed to the compositor video layer via IVideoService (a single
            // cross-platform render surface; no more IMultiMonitorVideoService).
            _video.VideoEnded += OnWebVideoEnded;
            _video.PlayUrl(selected.Value);
            _logger?.LogInformation("AvaloniaAutonomyService: Playing web video '{Name}' at {Url}", selected.Key, selected.Value);
        }
        catch (Exception ex)
        {
            _webVideoActive = false;
            _video.VideoEnded -= OnWebVideoEnded;
            _logger?.LogWarning(ex, "AvaloniaAutonomyService: Failed to play web video");
        }
    }

    private void OnWebVideoEnded(object? sender, EventArgs e)
    {
        if (_video != null)
            _video.VideoEnded -= OnWebVideoEnded;
        _webVideoWatchdogGeneration++;
        if (_webVideoActive)
        {
            _webVideoActive = false;
            _logger?.LogInformation("AvaloniaAutonomyService: Web video ended, autonomy actions resumed");
        }
    }

    private void ShuffleWallpaper()
    {
        if (_wallpaper == null || _appEnvironment == null) return;

        var path = GetRandomWallpaperPath();
        if (string.IsNullOrEmpty(path))
        {
            _logger?.LogDebug("AvaloniaAutonomyService: No wallpapers available to shuffle");
            return;
        }

        try
        {
            _wallpaper.SetWallpaper(path);
            _logger?.LogInformation("AvaloniaAutonomyService: Shuffled wallpaper to {Path}", path);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AvaloniaAutonomyService: Failed to shuffle wallpaper");
        }
    }

    private string? GetRandomWallpaperPath()
    {
        try
        {
            var folders = new[]
            {
                Path.Combine(_appEnvironment!.EffectiveAssetsPath, "wallpapers"),
                Path.Combine(_appEnvironment.BaseDirectory, "Resources", "wallpapers")
            };

            var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };
            var files = new List<string>();
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var ext in extensions)
                {
                    try
                    {
                        files.AddRange(Directory.GetFiles(folder, $"*{ext}", SearchOption.AllDirectories));
                    }
                    catch { }
                }
            }

            if (files.Count == 0) return null;
            return files[_random.Next(files.Count)];
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AvaloniaAutonomyService: Failed to enumerate wallpapers");
            return null;
        }
    }

    private void AutoStopAfter(string name, Action stopAction, TimeSpan delay)
    {
        StartOneShotTimer(delay, () =>
        {
            if (!_isEnabled) return;
            try
            {
                stopAction();
                _logger?.LogDebug("AvaloniaAutonomyService: {Name} auto-stopped after {Delay}s", name, delay.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AvaloniaAutonomyService: Failed to auto-stop {Name}", name);
            }
        });
    }

    private void StartCooldown()
    {
        var settings = _settings.Current;
        var cooldownSeconds = _forceTestMode ? 5 : (settings?.AutonomyCooldownSeconds ?? 30);
        _isOnCooldown = true;

        _cooldownTimer?.Stop();
        _cooldownTimer = StartOneShotTimer(TimeSpan.FromSeconds(cooldownSeconds), () =>
        {
            _isOnCooldown = false;
            _logger?.LogDebug("AvaloniaAutonomyService: Cooldown expired");
        });
    }

    private void UpdateMood()
    {
        var hour = DateTime.Now.Hour;
        _currentMood = hour switch
        {
            >= 5 and < 12 => AutonomyMood.Gentle,
            >= 12 and < 17 => AutonomyMood.Attentive,
            >= 17 and < 22 => AutonomyMood.Playful,
            _ => AutonomyMood.Mischievous
        };
    }

    private static DispatcherTimer StartPeriodicTimer(TimeSpan interval, Action callback)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) => callback();
        timer.Start();
        return timer;
    }

    private static DispatcherTimer StartOneShotTimer(TimeSpan dueTime, Action callback)
    {
        var timer = new DispatcherTimer { Interval = dueTime };
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= handler;
            callback();
        };
        timer.Tick += handler;
        timer.Start();
        return timer;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopVoiceInput();
        Stop();
    }
}
