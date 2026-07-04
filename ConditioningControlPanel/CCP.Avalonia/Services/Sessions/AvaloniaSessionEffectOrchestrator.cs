using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Avalonia.Services.Auth;
using ConditioningControlPanel.Avalonia.Services.BubbleCount;
using ConditioningControlPanel.Avalonia.Services.Mod;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Autonomy;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.LockCard;
using ConditioningControlPanel.Core.Services.MindWipe;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Quiz;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Services.Sessions;

/// <summary>
/// Avalonia implementation of <see cref="ISessionEffectOrchestrator"/>.
/// Starts/stops feature services according to the current session settings and drives
/// per-tick feature scheduling (delayed pink/spiral/bubble starts, intermittent bubble
/// bursts) mirroring WPF SessionEngine.CheckDelayedFeatures/HandleIntermittentBubbles.
/// Services are resolved lazily on first use so heavy dependencies such as
/// LibVLC are not created during cold startup.
/// </summary>
public sealed class AvaloniaSessionEffectOrchestrator : ISessionEffectOrchestrator
{
    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;
    private readonly ILogger<AvaloniaSessionEffectOrchestrator>? _logger;
    private readonly Random _random = new();

    private IFlashService? _flash;
    private IVideoService? _video;
    private ISubliminalService? _subliminal;
    private IMindWipeService? _mindWipe;
    private IBouncingTextService? _bouncingText;
    private IOverlayService? _overlay;
    private ILockCardService? _lockCard;
    private IBubbleService? _bubbles;
    private IBubbleCountService? _bubbleCount;
    private IPopQuizService? _popQuiz;
    private IInteractionQueueService? _interactionQueue;
    private IAutonomyService? _autonomy;
    private ISystemAudioDucker? _ducker;
    private AvaloniaPatreonProvider? _patreon;

    // Per-session tick-scheduling state (WPF SessionEngine parity). Reset on a fresh
    // StartEffects; deliberately preserved across pause/resume, matching WPF where
    // PauseSession/ResumeSession never re-randomize or re-schedule.
    private readonly List<double> _scheduledBubbleBursts = new();
    private int _bubbleBurstIndex;
    private bool _bubblesCurrentlyActive;
    private DateTime _bubbleBurstEndTime;
    private double _randomizedPinkStartMinute;
    private double _randomizedSpiralStartMinute;

    public AvaloniaSessionEffectOrchestrator(
        IServiceProvider services,
        ISettingsService settings,
        ILogger<AvaloniaSessionEffectOrchestrator>? logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
    }

    private IFlashService? Flash => _flash ??= _services.GetService<IFlashService>();
    private IVideoService? Video => _video ??= _services.GetService<IVideoService>();
    private ISubliminalService? Subliminal => _subliminal ??= _services.GetService<ISubliminalService>();
    private IMindWipeService? MindWipe => _mindWipe ??= _services.GetService<IMindWipeService>();
    private IBouncingTextService? BouncingText => _bouncingText ??= _services.GetService<IBouncingTextService>();
    private IOverlayService? Overlay => _overlay ??= _services.GetService<IOverlayService>();
    private ILockCardService? LockCard => _lockCard ??= _services.GetService<ILockCardService>();
    private IBubbleService? Bubbles => _bubbles ??= _services.GetService<IBubbleService>();
    private IBubbleCountService? BubbleCount => _bubbleCount ??= _services.GetService<IBubbleCountService>();
    private IPopQuizService? PopQuiz => _popQuiz ??= _services.GetService<IPopQuizService>();
    private IInteractionQueueService? InteractionQueue => _interactionQueue ??= _services.GetService<IInteractionQueueService>();
    private IAutonomyService? Autonomy => _autonomy ??= _services.GetService<IAutonomyService>();
    private ISystemAudioDucker? Ducker => _ducker ??= _services.GetService<ISystemAudioDucker>();
    private AvaloniaPatreonProvider? Patreon => _patreon ??= _services.GetService<AvaloniaPatreonProvider>();

    public void StartEffects(Session session) => StartEffects(session, resuming: false);

    public void StartEffects(Session session, bool resuming)
    {
        if (session?.Settings == null) return;
        var s = session.Settings;
        var appSettings = _settings.Current;

        _logger?.LogInformation("{Action} session effects for {SessionName}",
            resuming ? "Resuming" : "Starting", session.Name);

        if (!resuming)
        {
            ResetTickState(session);
        }

        TryRun("overlay", () => Overlay?.Start());

        if (s.FlashEnabled) TryRun("flash", () => Flash?.Start());
        if (s.MandatoryVideosEnabled) TryRun("video", () => Video?.Start());
        if (s.SubliminalEnabled) TryRun("subliminal", () => Subliminal?.Start());
        if (s.MindWipeEnabled)
        {
            TryRun("mindwipe", () => MindWipe?.Start(appSettings.MindWipeFrequency, appSettings.MindWipeVolume / 100.0));

            // Loop mode rides the engine start when enabled in app settings
            // (WPF MainWindow.StartStop.cs:211-215).
            if (appSettings.MindWipeLoop)
            {
                TryRun("mindwipe loop", () => MindWipe?.StartLoop(appSettings.MindWipeVolume / 100.0));
            }
        }
        if (s.BouncingTextEnabled) TryRun("bouncing text", () => BouncingText?.Start(s.BouncingTextPhrases));

        // Delayed (BubblesStartMinute > 0) and intermittent bubble sessions stay off until
        // the tick scheduler enables them (WPF SessionEngine.ApplySessionSettings:958-971).
        // SessionSettingsScope.Apply has already shaped the live BubblesEnabled flag for a
        // fresh start; on resume it reflects the live burst/delayed state at pause time.
        if (s.BubblesEnabled && appSettings.BubblesEnabled) TryRun("bubbles", () => Bubbles?.Start());
        if (s.BubbleCountEnabled) TryRun("bubble count", () => BubbleCount?.Start());
        if (s.LockCardEnabled) TryRun("lock card", () => LockCard?.Start());
        if (s.PopQuizEnabled) TryRun("pop quiz", () => PopQuiz?.Start());

        // Arm autonomy on session start when the user's settings say it should be armed
        // (WPF MainWindow.StartStop.cs:224-229). Start() is idempotent and re-gates
        // internally. Not re-armed on resume: WPF ResumeSession never touches autonomy.
        if (!resuming && IsAutonomyUserEnabled(appSettings))
        {
            TryRun("autonomy", () => Autonomy?.Start());
        }

        // Refresh overlays after individual effect services have started so that
        // pink/spiral/brain-drain windows reflect current settings.
        TryRun("overlay refresh", () => Overlay?.RefreshOverlays());
    }

    public void StopEffects() => StopEffects(pausing: false);

    public void StopEffects(bool pausing)
    {
        _logger?.LogInformation(pausing ? "Pausing all session effects" : "Stopping all session effects");

        // #462: On a full stop, clear the interaction queue BEFORE tearing down the
        // interactive overlays. Unlike WPF's Video CloseAll (which never Completes), the
        // Avalonia Video/LockCard/BubbleCount/PopQuiz teardown paths call
        // InteractionQueue.Complete(), which dequeues and async-posts a queued trigger —
        // re-arming a fullscreen overlay after teardown. Resetting first makes those
        // Complete() calls no-ops (mirrors WPF StopEngine ordering,
        // MainWindow.StartStop.cs:328-337). Pause preserves the queue: WPF PauseSession
        // never resets it (SessionEngine.PauseSession).
        if (!pausing)
        {
            TryRun("interaction queue reset", () => InteractionQueue?.ForceReset());
        }

        // Note: session log begin/end is owned by Core SessionService so the log gets the
        // real duration/XP/completed flag. Stopping effects alone does not end the log.
        TryRun("flash", () => Flash?.Stop());
        // Bubbles and bouncing text stop BEFORE video, mirroring WPF StopEngineCore order
        // (MainWindow.StartStop.cs:298-304, animation timers vs LibVLC cleanup contention).
        TryRun("bubbles", () => Bubbles?.Stop());
        TryRun("bouncing text", () => BouncingText?.Stop());
        TryRun("video", () => Video?.Stop());
        TryRun("subliminal", () => Subliminal?.Stop());
        TryRun("mindwipe", () => MindWipe?.Stop());
        TryRun("bubble count", () => BubbleCount?.Stop());
        TryRun("lock card", () => LockCard?.Stop());
        TryRun("pop quiz", () => PopQuiz?.Stop());
        TryRun("overlay", () => Overlay?.Stop());

        if (!pausing)
        {
            // Stop autonomy unless the user has it independently enabled, so a
            // force-started or Patreon-lapsed autonomy dies with the session while a
            // user-armed Takeover survives it (WPF MainWindow.StartStop.cs:314-321).
            // WPF PauseSession never touches autonomy, hence the !pausing gate.
            if (!IsAutonomyUserEnabled(_settings.Current))
            {
                TryRun("autonomy", () => Autonomy?.Stop());
            }

            // Restore system audio ducked by keyword triggers/remote commands that a
            // stop/panic interrupted (WPF App.Audio.ForceUnduck, StartStop.cs:323).
            // ForceUnduck resets the duck ref count and restores immediately; it is a
            // no-op when nothing is ducked.
            TryRun("unduck", () => Ducker?.ForceUnduck());
        }
    }

    public void TickEffects(Session session, TimeSpan elapsed)
    {
        if (session?.Settings == null) return;
        var elapsedMinutes = elapsed.TotalMinutes;

        // Core SessionService.OnTick has already run UpdateRampingValues before this call,
        // preserving the WPF MainTimer_Tick order: ramps -> delayed features -> bursts
        // (SessionEngine.cs:469-475).
        CheckDelayedFeatures(session, elapsedMinutes);
        HandleIntermittentBubbles(session, elapsedMinutes);
    }

    /// <summary>
    /// Resets the per-session scheduling state at fresh session start: captures the
    /// randomized delayed-start minutes and pre-schedules intermittent bubble bursts
    /// (WPF SessionEngine.StartSessionAsync:164-173).
    /// </summary>
    private void ResetTickState(Session session)
    {
        var settings = session.Settings;

        // Use the randomized minutes Core computed for its ramp gating so the delayed
        // enable and the opacity ramp fire at the same jittered minute (WPF shares one
        // field between RandomizeStartTimes consumers). Falls back to the raw preset
        // minutes when effects are driven outside a Core session (e.g. benchmark runs).
        if (_services.GetService<ISessionService>() is SessionService sessionService
            && ReferenceEquals(sessionService.CurrentSession, session))
        {
            _randomizedPinkStartMinute = sessionService.RandomizedPinkStartMinute;
            _randomizedSpiralStartMinute = sessionService.RandomizedSpiralStartMinute;
        }
        else
        {
            _randomizedPinkStartMinute = settings.PinkFilterStartMinute;
            _randomizedSpiralStartMinute = settings.SpiralStartMinute;
        }

        _scheduledBubbleBursts.Clear();
        _bubbleBurstIndex = 0;
        _bubblesCurrentlyActive = false;
        _bubbleBurstEndTime = DateTime.MinValue;

        if (settings.BubblesEnabled && settings.BubblesIntermittent)
        {
            ScheduleBubbleBursts(session);
        }
    }

    /// <summary>
    /// Enables features whose session start minute has been reached.
    /// Port of WPF SessionEngine.CheckDelayedFeatures (SessionEngine.cs:580-666).
    /// </summary>
    private void CheckDelayedFeatures(Session session, double elapsedMinutes)
    {
        var settings = session.Settings;
        var current = _settings.Current;

        // Pink filter delayed start, randomized time (WPF SessionEngine.cs:586-595).
        // Enabling the live flag once is enough: Core's ramp writes PinkFilterOpacity from
        // the same minute, and RefreshOverlays applies flag + opacity to the tint layer.
        if (settings.PinkFilterEnabled && !current.PinkFilterEnabled
            && elapsedMinutes >= _randomizedPinkStartMinute)
        {
            current.PinkFilterEnabled = true;
            TryRun("overlay refresh", () => Overlay?.RefreshOverlays());
            _logger?.LogInformation("Pink filter activated at {Minutes:F1} minutes (target was {Target:F1})",
                elapsedMinutes, _randomizedPinkStartMinute);
        }

        // Spiral delayed start with missing-spiral-file guard (WPF SessionEngine.cs:598-622).
        if (settings.SpiralEnabled && !current.SpiralEnabled)
        {
            if (!HasSpiralFile(current))
            {
                _logger?.LogWarning("Spiral enabled in session but no spiral files found - skipping");
                // Disable in session to prevent repeated warnings (WPF parity).
                settings.SpiralEnabled = false;
                return;
            }

            if (elapsedMinutes >= _randomizedSpiralStartMinute)
            {
                current.SpiralEnabled = true;
                TryRun("overlay refresh", () => Overlay?.RefreshOverlays());
                _logger?.LogInformation("Spiral activated at {Minutes:F1} minutes (target was {Target:F1})",
                    elapsedMinutes, _randomizedSpiralStartMinute);
            }
        }

        // Bubbles delayed start (WPF SessionEngine.cs:625-632). WPF passes
        // bypassLevelCheck:true; the Avalonia bubble service has no level gate, so the
        // bypass is implicit.
        if (settings.BubblesEnabled && !current.BubblesEnabled
            && settings.BubblesStartMinute > 0 && !settings.BubblesIntermittent
            && elapsedMinutes >= settings.BubblesStartMinute)
        {
            current.BubblesEnabled = true;
            TryRun("bubbles", () => Bubbles?.Start());
            _logger?.LogInformation("Bubbles activated at {Minutes:F1} minutes (target was {Target})",
                elapsedMinutes, settings.BubblesStartMinute);
        }

        // Corner GIF delayed start/end (WPF SessionEngine.cs:635-654) is NOT ported:
        // the Avalonia head has no corner GIF surface yet.
        // TODO(corner-gif): wire CornerGifStartMinute/EndMinute once a compositor corner
        // layer or window exists in CCP.Avalonia.
    }

    /// <summary>
    /// Runs the pre-scheduled intermittent bubble bursts.
    /// Port of WPF SessionEngine.HandleIntermittentBubbles (SessionEngine.cs:727-756).
    /// </summary>
    private void HandleIntermittentBubbles(Session session, double elapsedMinutes)
    {
        var settings = session.Settings;
        if (!settings.BubblesEnabled || !settings.BubblesIntermittent) return;

        // End the current burst when its window has elapsed.
        if (_bubblesCurrentlyActive && DateTime.Now >= _bubbleBurstEndTime)
        {
            _bubblesCurrentlyActive = false;
            SetBubblesActive(false);
            _logger?.LogInformation("Bubble burst ended");
        }

        // Start the next scheduled burst.
        if (!_bubblesCurrentlyActive && _bubbleBurstIndex < _scheduledBubbleBursts.Count
            && elapsedMinutes >= _scheduledBubbleBursts[_bubbleBurstIndex])
        {
            _bubblesCurrentlyActive = true;
            var burstDuration = _random.Next(1, 3); // 1-2 minutes
            _bubbleBurstEndTime = DateTime.Now.AddMinutes(burstDuration);
            _bubbleBurstIndex++;

            SetBubblesActive(true, settings.BubblesPerBurst);
            _logger?.LogInformation("Bubble burst started, duration: {Duration}min", burstDuration);
        }
    }

    /// <summary>
    /// Pre-schedules bubble bursts across the session: first burst after 2-5 minutes,
    /// then BubblesGapMin..BubblesGapMax minute gaps, capped 2 minutes before the end.
    /// Port of WPF SessionEngine.ScheduleBubbleBursts (SessionEngine.cs:701-725).
    /// </summary>
    private void ScheduleBubbleBursts(Session session)
    {
        _scheduledBubbleBursts.Clear();
        _bubbleBurstIndex = 0;

        var settings = session.Settings;
        var totalMinutes = session.DurationMinutes;
        var minGap = settings.BubblesGapMin;
        var maxGap = settings.BubblesGapMax;

        double currentTime = _random.Next(2, 5); // start after 2-5 minutes

        for (int i = 0; i < settings.BubblesBurstCount && currentTime < totalMinutes - 2; i++)
        {
            _scheduledBubbleBursts.Add(currentTime);
            currentTime += _random.Next(minGap, maxGap + 1);
        }

        _logger?.LogInformation("Scheduled {Count} bubble bursts: {Times}",
            _scheduledBubbleBursts.Count,
            string.Join(", ", _scheduledBubbleBursts.Select(t => $"{t:F1}min")));
    }

    /// <summary>
    /// Toggles the ambient bubble field for a burst window.
    /// Port of WPF MainWindow.SetBubblesActive (MainWindow.Presets.cs:1466-1489).
    /// </summary>
    private void SetBubblesActive(bool active, int bubblesPerBurst = 5)
    {
        var current = _settings.Current;
        if (active)
        {
            current.BubblesEnabled = true;
            current.BubblesFrequency = bubblesPerBurst * 2; // higher frequency during burst

            if (Bubbles?.IsRunning != true)
            {
                TryRun("bubbles", () => Bubbles?.Start());
            }
        }
        else
        {
            TryRun("bubbles", () => Bubbles?.Stop());
            current.BubblesEnabled = false;
        }
    }

    /// <summary>
    /// True when spiral media exists anywhere the overlay service would look for it
    /// (user-selected file, mod override, shipped Spirals folder, assets folder).
    /// Mirrors the WPF missing-spiral guard (SessionEngine.cs:600-613) against the
    /// Avalonia resolution paths (AvaloniaOverlayService.GetSpiralPath).
    /// </summary>
    private bool HasSpiralFile(AppSettings current)
    {
        try
        {
            if (!string.IsNullOrEmpty(current.SpiralPath) && File.Exists(current.SpiralPath))
                return true;

            var modUri = _services.GetService<AvaloniaModResourceResolver>()?.ResolveUri("spiral.gif");
            if (!string.IsNullOrEmpty(modUri) && modUri.StartsWith("file://", StringComparison.Ordinal)
                && File.Exists(modUri.Substring(7)))
                return true;

            var environment = _services.GetService<IAppEnvironment>();
            var spiralsFolder = Path.Combine(environment?.BaseDirectory ?? AppContext.BaseDirectory, "Spirals");
            if (Directory.Exists(spiralsFolder) && Directory.GetFiles(spiralsFolder, "*.gif").Length > 0)
                return true;

            var assetsFallback = environment != null ? Path.Combine(environment.EffectiveAssetsPath, "spiral.gif") : null;
            return assetsFallback != null && File.Exists(assetsFallback);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Spiral file probe failed; assuming a spiral exists");
            return true; // fail open: the overlay service no-ops safely without a file
        }
    }

    /// <summary>
    /// The WPF session-engine autonomy gate: premium access (tier/whitelist/SubscribeStar)
    /// plus user enable plus consent (MainWindow.StartStop.cs:225-226, :316-318).
    /// </summary>
    private bool IsAutonomyUserEnabled(AppSettings? appSettings)
    {
        if (appSettings == null) return false;
        return Patreon?.HasPremiumAccess == true
            && appSettings.AutonomyModeEnabled
            && appSettings.AutonomyConsentGiven;
    }

    private void TryRun(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Session effect '{Name}' failed", name);
        }
    }
}
