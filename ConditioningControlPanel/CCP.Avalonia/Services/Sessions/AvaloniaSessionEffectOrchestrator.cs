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
/// Timeline start events for the remaining features (#483) are queued on Core
/// SessionService's pending-starts list and fired by its tick, mirroring WPF
/// SessionEngine.DeferFeatureStart/CheckDelayedFeatures (SessionEngine.cs:600-620, 857-875).
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

        // Timeline start events (#483): features with StartMinute > 0 are queued on Core's
        // pending-starts list and fired by the session tick instead of starting at t=0
        // (WPF SessionEngine.ApplySessionSettings defer branches, SessionEngine.cs:892-1153;
        // resume pending gates, SessionEngine.cs:434-452). Deferral needs the ticking Core
        // session; effects driven outside one (benchmark, quick-start) have no tick to fire
        // queued entries, so they keep starting immediately.
        var deferral = ResolveDeferralTarget(session);

        TryRun("overlay", () => Overlay?.Start());

        StartOrDefer(deferral, resuming, "flash", s.FlashEnabled, s.FlashStartMinute,
            start: () => Flash?.Start(),
            enableLiveFlag: () => _settings.Current.FlashEnabled = true,
            stopFirst: () => Flash?.Stop());
        StartOrDefer(deferral, resuming, "video", s.MandatoryVideosEnabled, s.MandatoryVideosStartMinute,
            start: () => Video?.Start(),
            enableLiveFlag: () => _settings.Current.MandatoryVideosEnabled = true,
            stopFirst: () => Video?.Stop());
        StartOrDefer(deferral, resuming, "subliminal", s.SubliminalEnabled, s.SubliminalStartMinute,
            start: () => Subliminal?.Start(),
            enableLiveFlag: () => _settings.Current.SubliminalEnabled = true,
            stopFirst: () => Subliminal?.Stop());
        // Mind wipe has no live enable flag and WPF's defer branch has no Stop
        // (SessionEngine.cs:186-200); frequency/volume are read at fire time, matching
        // WPF's lazy read of the captured settings.
        StartOrDefer(deferral, resuming, "mindwipe", s.MindWipeEnabled, s.MindWipeStartMinute,
            start: () =>
            {
                var app = _settings.Current;
                MindWipe?.Start(app.MindWipeFrequency, app.MindWipeVolume / 100.0);

                // Loop mode rides the engine start when enabled in app settings
                // (WPF MainWindow.StartStop.cs:211-215).
                if (app.MindWipeLoop)
                {
                    TryRun("mindwipe loop", () => MindWipe?.StartLoop(app.MindWipeVolume / 100.0));
                }
            });
        // WPF stops bouncing text first to reset state (SessionEngine.cs:996-1013).
        StartOrDefer(deferral, resuming, "bouncing text", s.BouncingTextEnabled, s.BouncingTextStartMinute,
            start: () => BouncingText?.Start(s.BouncingTextPhrases),
            enableLiveFlag: () => _settings.Current.BouncingTextEnabled = true,
            stopFirst: () => BouncingText?.Stop());

        // Audio whispers are flag-driven (no service Start); the scope held SubAudioEnabled
        // off for a delayed start, so queue just the flag flip (WPF SessionEngine.cs:955-965).
        if (!resuming && s.AudioWhispersEnabled && s.AudioWhispersStartMinute > 0)
        {
            deferral?.DeferFeatureStart("audio whispers", s.AudioWhispersStartMinute,
                () => _settings.Current.SubAudioEnabled = true);
        }

        // Delayed (BubblesStartMinute > 0) and intermittent bubble sessions stay off until
        // the tick scheduler enables them (WPF SessionEngine.ApplySessionSettings:958-971).
        // SessionSettingsScope.Apply has already shaped the live BubblesEnabled flag for a
        // fresh start; on resume it reflects the live burst/delayed state at pause time.
        if (s.BubblesEnabled && appSettings.BubblesEnabled) TryRun("bubbles", () => Bubbles?.Start());
        StartOrDefer(deferral, resuming, "bubble count", s.BubbleCountEnabled, s.BubbleCountStartMinute,
            start: () => BubbleCount?.Start(),
            enableLiveFlag: () => _settings.Current.BubbleCountEnabled = true,
            stopFirst: () => BubbleCount?.Stop());
        StartOrDefer(deferral, resuming, "lock card", s.LockCardEnabled, s.LockCardStartMinute,
            start: () => LockCard?.Start(),
            enableLiveFlag: () => _settings.Current.LockCardEnabled = true,
            stopFirst: () => LockCard?.Stop());
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
        // MainWindow.StartStop.cs:328-337).
        // NOTE (#462 review): the pause path currently calls the no-arg StopEffects(), so
        // pausing DOES reset the queue in this head today — the !pausing branch is reachable
        // only by a future caller. That is deliberate-safe here: paused services' Complete()
        // calls would otherwise async-post a queued overlay while paused (the same race this
        // fix closes). If WPF pause-preserves-queue parity is ever wanted, wire the
        // SessionPaused handler to StopEffects(pausing: true) and re-verify that race first.
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
    /// Starts a session feature immediately, or defers it to its timeline start minute.
    /// Fresh start: a feature with <paramref name="startMinute"/> &gt; 0 is stopped first
    /// (a session can subsume an engine-only run that already has the service running,
    /// WPF SessionEngine.cs:900 "engine may already have it running") and queued on Core's
    /// pending list; the fire action re-enables the live flag the scope held off, then
    /// starts the service (WPF SessionEngine.ApplySessionSettings defer branches,
    /// SessionEngine.cs:892-1153). Resume: features still pending are skipped — Core fires
    /// them when due, elapsed session time survives the pause (WPF SessionEngine.cs:434-452).
    /// </summary>
    private void StartOrDefer(
        ISessionService? deferral,
        bool resuming,
        string name,
        bool enabled,
        int startMinute,
        Action start,
        Action? enableLiveFlag = null,
        Action? stopFirst = null)
    {
        if (!enabled) return;

        if (resuming)
        {
            if (deferral?.IsFeaturePending(name) == true) return;
            TryRun(name, start);
            return;
        }

        if (startMinute > 0 && deferral != null)
        {
            if (stopFirst != null) TryRun($"{name} (deferred stop)", stopFirst);
            deferral.DeferFeatureStart(name, startMinute, () => TryRun(name, () =>
            {
                enableLiveFlag?.Invoke();
                start();
            }));
            return;
        }

        TryRun(name, start);
    }

    /// <summary>
    /// The Core session service owning <paramref name="session"/>, if it is the session
    /// currently ticking. Deferred starts are queued on Core's pending list (fired by its
    /// tick, dropped on stop); with no ticking owner there is nothing to fire them, so
    /// callers fall back to immediate starts (same fallback shape as ResetTickState).
    /// </summary>
    private ISessionService? ResolveDeferralTarget(Session session)
    {
        return _services.GetService<ISessionService>() is { } sessionService
            && ReferenceEquals(sessionService.CurrentSession, session)
            ? sessionService
            : null;
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
