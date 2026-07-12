using System;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Awareness;
using ConditioningControlPanel.Core.Services.BlinkTrainer;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.LockCard;
using ConditioningControlPanel.Core.Services.Mantra;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Quiz;
using ConditioningControlPanel.Core.Services.Roadmap;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Update;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Core.Services.Webcam;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Quiz;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Core.Services.Bark;

/// <summary>
/// The port's equivalent of the WPF bark <c>WireSubscriptions</c> block (Services/Companion/BarkService.cs:427)
/// plus the awareness pair (:562-577). Owns engine <see cref="BarkEngine.Start"/> and subscribes the
/// readily-available Core event sources (awareness, session, video, progression, achievement, quest,
/// and mod-change reload) into <see cref="BarkEngine.Raise"/>. BARK-1 slice 3 wired the first batch
/// (awareness pair CLOSES AI-10, 36 chaos triggers via AvaloniaBarkService, session/video/progression-
/// LevelUp/achievement/quest-completed/mod). BARK-2 wires the REMAINING contract-E triggers whose port
/// event source already exists (webcam, gaze, blink trainer, bubbles, flash, mantra, roadmap, quiz, lock
/// card, quest-refresh, update, keyword, skill-tree, lockdown, remote-control, attention-check) and the
/// <see cref="NotifyUserMessage"/> call-site hook for the chat-suppression window.
/// </summary>
/// <remarks>
/// <para>Threading: awareness/webcam/remote events fire on background threads; <see cref="BarkEngine.Raise"/>
/// is internally locked and the speaker (<c>AvatarBarkSpeaker</c>) marshals to the UI thread, so no extra
/// marshalling is needed here.</para>
/// <para>Privacy: ctx stamps only ids/categories/enums/counts — never raw window titles or secrets
/// (mirrors WPF BarkService.cs:562-577 and the contract-E ctx var names). No new disk/network sinks.</para>
/// <para>All dependencies are optional: a head/test that omits one simply skips that subscription.</para>
/// </remarks>
public sealed class BarkTriggerWiring : IDisposable
{
    private readonly BarkEngine _engine;
    private readonly IAwarenessService? _awareness;
    private readonly IModService? _mods;
    private readonly ISessionService? _sessions;
    private readonly IVideoService? _video;
    private readonly IProgressionService? _progression;
    private readonly IAchievementService? _achievements;
    private readonly IQuestService? _quests;
    private readonly ILogger<BarkTriggerWiring>? _logger;

    // BARK-2 remaining contract-E sources (all optional; null → subscription skipped).
    private readonly IWebcamService? _webcam;
    private readonly IBlinkTrainerService? _blinkTrainer;
    private readonly IGazeFocusService? _gaze;
    private readonly IBubbleService? _bubbles;
    private readonly IFlashService? _flash;
    private readonly IMantraService? _mantra;
    private readonly IRoadmapService? _roadmap;
    private readonly IQuizService? _quiz;
    private readonly ILockCardService? _lockCard;
    private readonly IUpdateService? _update;
    private readonly IKeywordTriggerService? _keywords;
    private readonly ISkillTreeService? _skills;
    private readonly ILockdownService? _lockdown;
    private readonly IRemoteControlService? _remoteControl;
    private readonly IAttentionCheckService? _attentionCheck;

    private bool _started;
    private bool _disposed;

    // Cached handlers so Dispose unsubscribes exactly what Start subscribed.
    private readonly EventHandler<ActivityChangedEventArgs> _onActivityChanged;
    private readonly EventHandler<ActivityChangedEventArgs> _onStillOnActivity;
    private readonly EventHandler<ModPackage> _onModChanged;
    private readonly EventHandler _onSessionStarted;
    private readonly EventHandler<SessionStoppedEventArgs> _onSessionStopped;
    private readonly EventHandler<SessionCompletedEventArgs> _onSessionCompleted;
    private readonly EventHandler<SessionPhaseChangedEventArgs> _onSessionPhaseChanged;
    private readonly EventHandler _onVideoStarted;
    private readonly EventHandler _onVideoEnded;
    private readonly EventHandler<int> _onLevelUp;
    private readonly EventHandler<Achievement> _onAchievementUnlocked;
    private readonly EventHandler<QuestCompletedEventArgs> _onQuestCompleted;

    // BARK-2 cached handlers.
    private readonly Action _onBlink;
    private readonly Action<Point> _onLongStare;
    private readonly Action _onMouthOpen;
    private readonly Action _onTongueOut;
    private readonly Action _onFaceLost;
    private readonly Action _onFaceFound;
    private readonly Action<WebcamTrackingState> _onTrackingStateChanged;
    private readonly Action _onBlinkTrainerStateChanged;
    private readonly Action<bool> _onGazeActiveChanged;
    private readonly Action _onGazePopped;
    private readonly Action _onBubblePopped;
    private readonly Action _onBubbleMissed;
    private readonly EventHandler _onFlashDisplayed;
    private readonly Action<int> _onMantraStreakChanged;
    private readonly Action _onMantraStreakBroken;
    private readonly Action _onMantraCompleted;
    private readonly EventHandler<RoadmapStepCompletedEventArgs> _onRoadmapStepCompleted;
    private readonly EventHandler<RoadmapTrack> _onRoadmapTrackUnlocked;
    private readonly EventHandler<QuizCompletedEventArgs> _onQuizCompleted;
    private readonly EventHandler<LockCardCompletedEventArgs> _onLockCardCompleted;
    private readonly EventHandler _onQuestsRefreshed;
    private readonly EventHandler<UpdateInfo> _onUpdateAvailable;
    private readonly EventHandler<KeywordTrigger> _onKeywordTriggerFired;
    private readonly EventHandler<string> _onSkillUnlocked;
    private readonly EventHandler _onPinkRushStarted;
    private readonly Action _onLockdownActivated;
    private readonly Action _onLockdownDeactivated;
    private readonly Action<TimeSpan> _onLockdownCountdownTick;
    private readonly EventHandler _onControllerConnectedChanged;
    private readonly EventHandler<string> _onRemoteCommandReceived;
    private readonly Action _onAttentionCheckPass;
    private readonly Action _onAttentionCheckFail;

    public BarkTriggerWiring(
        BarkEngine engine,
        IAwarenessService? awareness = null,
        IModService? mods = null,
        ISessionService? sessions = null,
        IVideoService? video = null,
        IProgressionService? progression = null,
        IAchievementService? achievements = null,
        IQuestService? quests = null,
        // BARK-2 remaining contract-E sources (trailing optional → existing call sites compile unchanged).
        IWebcamService? webcam = null,
        IBlinkTrainerService? blinkTrainer = null,
        IGazeFocusService? gaze = null,
        IBubbleService? bubbles = null,
        IFlashService? flash = null,
        IMantraService? mantra = null,
        IRoadmapService? roadmap = null,
        IQuizService? quiz = null,
        ILockCardService? lockCard = null,
        IUpdateService? update = null,
        IKeywordTriggerService? keywords = null,
        ISkillTreeService? skills = null,
        ILockdownService? lockdown = null,
        IRemoteControlService? remoteControl = null,
        IAttentionCheckService? attentionCheck = null,
        ILogger<BarkTriggerWiring>? logger = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _awareness = awareness;
        _mods = mods;
        _sessions = sessions;
        _video = video;
        _progression = progression;
        _achievements = achievements;
        _quests = quests;
        _webcam = webcam;
        _blinkTrainer = blinkTrainer;
        _gaze = gaze;
        _bubbles = bubbles;
        _flash = flash;
        _mantra = mantra;
        _roadmap = roadmap;
        _quiz = quiz;
        _lockCard = lockCard;
        _update = update;
        _keywords = keywords;
        _skills = skills;
        _lockdown = lockdown;
        _remoteControl = remoteControl;
        _attentionCheck = attentionCheck;
        _logger = logger;

        // Awareness pair — WPF BarkService.cs:562-577. ctx: activity=ServiceName, category, app_cluster,
        // app on change; activity, still_minutes on still-on. (app/app_cluster are "" unless the
        // AppClusterMap matched — BarkService.cs:566.)
        _onActivityChanged = (_, e) => Raise("ActivityChanged", ctx =>
        {
            ctx.Set("activity", e.ServiceName ?? "");
            ctx.Set("category", e.Category.ToString());
            ctx.Set("app_cluster", e.AppCluster ?? "");
            ctx.Set("app", e.AppId ?? "");
        });
        _onStillOnActivity = (_, _) => Raise("StillOnActivity", ctx =>
        {
            ctx.Set("activity", _awareness?.CurrentServiceName ?? "");
            ctx.Set("still_minutes", (_awareness?.CurrentActivityDuration ?? TimeSpan.Zero).TotalMinutes);
        });

        // Mod switch: raise the ModChanged bark then reload the rule set (WPF BarkService.cs:668 →
        // ReloadRules :406). mod_switches_60s is a BarkState live field (null in the port today).
        _onModChanged = (_, e) =>
        {
            Raise("ModChanged", ctx => ctx.Set("mod", e.Id ?? ""));
            _engine.ReloadRules();
        };

        // Session lifecycle — WPF AttachSessionEngine (BarkService.cs:683).
        _onSessionStarted = (_, _) => Raise("SessionStarted");
        _onSessionStopped = (_, _) => Raise("SessionStopped");
        _onSessionCompleted = (_, _) => Raise("SessionCompleted");
        _onSessionPhaseChanged = (_, e) => Raise("SessionPhaseChanged", ctx =>
        {
            ctx.Set("phase_index", e.PhaseIndex);
            ctx.Set("phase_name", e.Phase?.Name ?? "");
            // phase_is_deepener mirrors the WPF heuristic (BarkState.CurrentPhaseIsDeepener); the port's
            // SessionPhase has no explicit flag, so derive from the phase name (WPF :1135).
            ctx.Set("phase_is_deepener", IsDeepenerPhaseName(e.Phase?.Name));
        });

        _onVideoStarted = (_, _) => Raise("VideoStarted");   // WPF BarkService.cs:427 wired events
        _onVideoEnded = (_, _) => Raise("VideoEnded");

        // Progression/achievement/quest — WPF WireSubscriptions wired events (BarkService.cs:427).
        _onLevelUp = (_, level) => Raise("LevelUp", ctx => ctx.Set("player_level", level));
        _onAchievementUnlocked = (_, a) => Raise("AchievementUnlocked", ctx => ctx.Set("achievement", a?.Id ?? ""));
        _onQuestCompleted = (_, _) => Raise("QuestCompleted");
        _onQuestsRefreshed = (_, _) => Raise("QuestsRefreshed");   // WPF BarkService.cs:427 quest wired events

        // ---- BARK-2: remaining contract-E triggers (WPF BarkService.cs:427 WireSubscriptions) ----

        // Webcam — WPF BarkService.cs:427 webcam wired events. blink_count is a well-known LIVE field
        // (BarkService.cs:1099) resolved by IBarkLiveFields; the port returns null until BarkState is
        // ported, so a Blink rule's blink_count condition degrades to false (safe). No ctx stamp needed.
        _onBlink = () => Raise("Blink");
        _onLongStare = _ => Raise("LongStare");              // WPF OnLongStare :441 (point unused by ctx)
        _onMouthOpen = () => Raise("MouthOpen");
        _onTongueOut = () => Raise("TongueOut");
        _onFaceLost = () => Raise("FaceLost");
        _onFaceFound = () => Raise("FaceFound");
        _onTrackingStateChanged = state => Raise("TrackingStateChanged", ctx => ctx.Set("state", state.ToString()));

        // Blink trainer / gaze — WPF BarkService.cs:427 gaze wired events.
        _onBlinkTrainerStateChanged = () => Raise("BlinkTrainerStateChanged",
            ctx => ctx.Set("running", _blinkTrainer?.IsRunning ?? false));
        _onGazeActiveChanged = active => Raise("GazeActiveChanged", ctx => ctx.Set("active", active));
        _onGazePopped = () => Raise("GazePopped");

        // Bubbles — WPF BarkService.cs:427 bubbles wired events (ambient pops; chaos pops route via
        // AvaloniaBarkService.NotifyChaos* from slice 3 — no double-fire here).
        _onBubblePopped = () => Raise("BubblePopped");
        _onBubbleMissed = () => Raise("BubbleMissed");

        // FX — WPF BarkService.cs:427 fx wired events.
        _onFlashDisplayed = (_, _) => Raise("FlashDisplayed");

        // Mantra — WPF BarkService.cs:427 mantra wired events.
        _onMantraStreakChanged = streak => Raise("StreakChanged", ctx => ctx.Set("streak", streak));
        _onMantraStreakBroken = () => Raise("StreakBroken");
        _onMantraCompleted = () => Raise("MantraCompleted");

        // Roadmap — WPF BarkService.cs:427 roadmap wired events.
        _onRoadmapStepCompleted = (_, _) => Raise("RoadmapStepCompleted");
        _onRoadmapTrackUnlocked = (_, e) => Raise("TrackUnlocked", ctx => ctx.Set("track", e.ToString()));

        // Quiz — WPF BarkService.cs:427 (STATIC). ctx: passed, perfect.
        _onQuizCompleted = (_, e) => Raise("QuizCompleted", ctx =>
        {
            ctx.Set("passed", e?.Passed ?? false);
            ctx.Set("perfect", e?.Perfect ?? false);
        });

        // Lock card — WPF BarkService.cs:984 fires a GUARANTEED FireLockCardPoolBark{phrase,mistakes,
        // repeats}. The port's Raise has no guaranteed-pool mechanic, so fire the simple raise with the
        // same ctx vars (a LockCardCompleted rule still matches); the guaranteed-pool behavior is noted
        // as a residual until the pool mechanism is ported.
        _onLockCardCompleted = (_, e) => Raise("LockCardCompleted", ctx =>
        {
            ctx.Set("phrase", e?.Phrase ?? "");
            ctx.Set("mistakes", e?.Mistakes ?? 0);
            ctx.Set("repeats", e?.Repeats ?? 0);
        });

        // Update / keyword / skill-tree — WPF BarkService.cs:427 wired events.
        _onUpdateAvailable = (_, _) => Raise("UpdateAvailable");
        _onKeywordTriggerFired = (_, t) => Raise("KeywordTriggerFired", ctx =>
        {
            ctx.Set("keyword", t?.Keyword ?? "");
            ctx.Set("kw_effect", t?.VisualEffect.ToString() ?? "");
        });
        _onSkillUnlocked = (_, skill) => Raise("SkillUnlocked", ctx => ctx.Set("skill", skill ?? ""));
        _onPinkRushStarted = (_, _) => Raise("PinkRushStarted");

        // Lockdown — WPF BarkService.cs:427 control wired events. remaining_sec from the tick TimeSpan.
        _onLockdownActivated = () => Raise("LockdownActivated");
        _onLockdownDeactivated = () => Raise("LockdownDeactivated");
        _onLockdownCountdownTick = remaining => Raise("LockdownCountdownTick",
            ctx => ctx.Set("remaining_sec", remaining.TotalSeconds));

        // Remote control — WPF BarkService.cs:427 control wired events. command is the action name.
        _onControllerConnectedChanged = (_, _) => Raise("ControllerConnectedChanged");
        _onRemoteCommandReceived = (_, command) => Raise("RemoteCommandReceived",
            ctx => ctx.Set("command", command ?? ""));

        // Attention check — WPF BarkService.cs:427 (gated Video.IsPlaying in WPF). video_playing is a
        // well-known LIVE field (BarkService.cs:1090) resolved by IBarkLiveFields, so it is NOT stamped
        // here (a live field shadows any same-named ctx value). AttentionCheckFail is the global-gap
        // EXEMPTION: the engine skips the 60s min-gap for this trigger by name (contract C). fail_count
        // is a ctx var in WPF but the port OnFail event carries no count, so it is left unset (noted).
        _onAttentionCheckPass = () => Raise("AttentionCheckPass");
        _onAttentionCheckFail = () => Raise("AttentionCheckFail");
    }

    /// <summary>
    /// Start the engine (idempotent — <see cref="BarkEngine.Start"/> self-guards) and subscribe every
    /// non-null event source. Safe to call once at app init after the DI container is built and the
    /// active mod is known (so the rule loader sees the merged manifest).
    /// </summary>
    public void Start()
    {
        if (_started) return;
        _started = true;

        try { _engine.Start(); }
        catch (Exception ex) { _logger?.LogError(ex, "BarkTriggerWiring: engine.Start failed"); }

        try
        {
            if (_awareness != null)
            {
                _awareness.ActivityChanged += _onActivityChanged;
                _awareness.StillOnActivity += _onStillOnActivity;
            }
            if (_mods != null) _mods.ActiveModChanged += _onModChanged;
            if (_sessions != null)
            {
                _sessions.SessionStarted += _onSessionStarted;
                _sessions.SessionStopped += _onSessionStopped;
                _sessions.SessionCompleted += _onSessionCompleted;
                _sessions.PhaseChanged += _onSessionPhaseChanged;
            }
            if (_video != null)
            {
                _video.VideoStarted += _onVideoStarted;
                _video.VideoEnded += _onVideoEnded;
            }
            if (_progression != null) _progression.LevelUp += _onLevelUp;
            if (_achievements != null) _achievements.AchievementUnlocked += _onAchievementUnlocked;
            if (_quests != null)
            {
                _quests.QuestCompleted += _onQuestCompleted;
                _quests.QuestsChanged += _onQuestsRefreshed;
            }

            SubscribeBark2Sources();
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "BarkTriggerWiring: subscription failed"); }
    }

    /// <summary>
    /// BARK-2: subscribe the remaining contract-E event sources. Split out so the slice-3 subscriptions
    /// above stay readable; every source is null-checked (optional dependency → skip).
    /// </summary>
    private void SubscribeBark2Sources()
    {
        if (_webcam != null)
        {
            _webcam.OnBlink += _onBlink;
            _webcam.OnLongStare += _onLongStare;
            _webcam.OnMouthOpen += _onMouthOpen;
            _webcam.OnTongueOut += _onTongueOut;
            _webcam.OnFaceLost += _onFaceLost;
            _webcam.OnFaceFound += _onFaceFound;
            _webcam.OnTrackingStateChanged += _onTrackingStateChanged;
        }
        if (_blinkTrainer != null) _blinkTrainer.StateChanged += _onBlinkTrainerStateChanged;
        if (_gaze != null)
        {
            _gaze.OnActiveChanged += _onGazeActiveChanged;
            _gaze.GazePopped += _onGazePopped;
        }
        if (_bubbles != null)
        {
            _bubbles.OnBubblePopped += _onBubblePopped;
            _bubbles.OnBubbleMissed += _onBubbleMissed;
        }
        if (_flash != null) _flash.FlashDisplayed += _onFlashDisplayed;
        if (_mantra != null)
        {
            _mantra.StreakChanged += _onMantraStreakChanged;
            _mantra.StreakBroken += _onMantraStreakBroken;
            _mantra.MantraCompleted += _onMantraCompleted;
        }
        if (_roadmap != null)
        {
            _roadmap.StepCompleted += _onRoadmapStepCompleted;
            _roadmap.TrackUnlocked += _onRoadmapTrackUnlocked;
        }
        if (_quiz != null) _quiz.QuizCompleted += _onQuizCompleted;
        if (_lockCard != null) _lockCard.LockCardCompleted += _onLockCardCompleted;
        if (_update != null) _update.UpdateAvailable += _onUpdateAvailable;
        if (_keywords != null) _keywords.TriggerFired += _onKeywordTriggerFired;
        if (_skills != null)
        {
            _skills.SkillUnlocked += _onSkillUnlocked;
            _skills.PinkRushStarted += _onPinkRushStarted;
        }
        if (_lockdown != null)
        {
            _lockdown.LockdownActivated += _onLockdownActivated;
            _lockdown.LockdownDeactivated += _onLockdownDeactivated;
            _lockdown.CountdownTick += _onLockdownCountdownTick;
        }
        if (_remoteControl != null)
        {
            _remoteControl.ControllerConnectedChanged += _onControllerConnectedChanged;
            _remoteControl.CommandReceived += _onRemoteCommandReceived;
        }
        if (_attentionCheck != null)
        {
            _attentionCheck.OnPass += _onAttentionCheckPass;
            _attentionCheck.OnFail += _onAttentionCheckFail;
        }
    }

    /// <summary>Unsubscribe every handler Start subscribed. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_awareness != null)
            {
                _awareness.ActivityChanged -= _onActivityChanged;
                _awareness.StillOnActivity -= _onStillOnActivity;
            }
            if (_mods != null) _mods.ActiveModChanged -= _onModChanged;
            if (_sessions != null)
            {
                _sessions.SessionStarted -= _onSessionStarted;
                _sessions.SessionStopped -= _onSessionStopped;
                _sessions.SessionCompleted -= _onSessionCompleted;
                _sessions.PhaseChanged -= _onSessionPhaseChanged;
            }
            if (_video != null)
            {
                _video.VideoStarted -= _onVideoStarted;
                _video.VideoEnded -= _onVideoEnded;
            }
            if (_progression != null) _progression.LevelUp -= _onLevelUp;
            if (_achievements != null) _achievements.AchievementUnlocked -= _onAchievementUnlocked;
            if (_quests != null)
            {
                _quests.QuestCompleted -= _onQuestCompleted;
                _quests.QuestsChanged -= _onQuestsRefreshed;
            }

            UnsubscribeBark2Sources();
        }
        catch { /* never throw from Dispose */ }
    }

    private void UnsubscribeBark2Sources()
    {
        if (_webcam != null)
        {
            _webcam.OnBlink -= _onBlink;
            _webcam.OnLongStare -= _onLongStare;
            _webcam.OnMouthOpen -= _onMouthOpen;
            _webcam.OnTongueOut -= _onTongueOut;
            _webcam.OnFaceLost -= _onFaceLost;
            _webcam.OnFaceFound -= _onFaceFound;
            _webcam.OnTrackingStateChanged -= _onTrackingStateChanged;
        }
        if (_blinkTrainer != null) _blinkTrainer.StateChanged -= _onBlinkTrainerStateChanged;
        if (_gaze != null)
        {
            _gaze.OnActiveChanged -= _onGazeActiveChanged;
            _gaze.GazePopped -= _onGazePopped;
        }
        if (_bubbles != null)
        {
            _bubbles.OnBubblePopped -= _onBubblePopped;
            _bubbles.OnBubbleMissed -= _onBubbleMissed;
        }
        if (_flash != null) _flash.FlashDisplayed -= _onFlashDisplayed;
        if (_mantra != null)
        {
            _mantra.StreakChanged -= _onMantraStreakChanged;
            _mantra.StreakBroken -= _onMantraStreakBroken;
            _mantra.MantraCompleted -= _onMantraCompleted;
        }
        if (_roadmap != null)
        {
            _roadmap.StepCompleted -= _onRoadmapStepCompleted;
            _roadmap.TrackUnlocked -= _onRoadmapTrackUnlocked;
        }
        if (_quiz != null) _quiz.QuizCompleted -= _onQuizCompleted;
        if (_lockCard != null) _lockCard.LockCardCompleted -= _onLockCardCompleted;
        if (_update != null) _update.UpdateAvailable -= _onUpdateAvailable;
        if (_keywords != null) _keywords.TriggerFired -= _onKeywordTriggerFired;
        if (_skills != null)
        {
            _skills.SkillUnlocked -= _onSkillUnlocked;
            _skills.PinkRushStarted -= _onPinkRushStarted;
        }
        if (_lockdown != null)
        {
            _lockdown.LockdownActivated -= _onLockdownActivated;
            _lockdown.LockdownDeactivated -= _onLockdownDeactivated;
            _lockdown.CountdownTick -= _onLockdownCountdownTick;
        }
        if (_remoteControl != null)
        {
            _remoteControl.ControllerConnectedChanged -= _onControllerConnectedChanged;
            _remoteControl.CommandReceived -= _onRemoteCommandReceived;
        }
        if (_attentionCheck != null)
        {
            _attentionCheck.OnPass -= _onAttentionCheckPass;
            _attentionCheck.OnFail -= _onAttentionCheckFail;
        }
    }

    /// <summary>
    /// BARK-2: chat-suppression hook. The WPF <c>UserMessageSent</c> wired event (BarkService.cs:427)
    /// raises the reaction bark and stamps <c>_lastUserMessageUtc</c> so <see cref="BarkEngine"/>'s
    /// chat-suppression gate window suppresses interrupting AMBIENT barks (idle/awareness) during the
    /// chat exchange. Order is raise-then-stamp: the UserMessageSent reaction must fire before the
    /// window arms (arming first would suppress the reaction by its own gate). Call from the companion
    /// chat send site (AvatarTubeWindow) on every user send.
    /// </summary>
    public void NotifyUserMessage()
    {
        try
        {
            Raise("UserMessageSent");       // WPF BarkService.cs:427 UserMessageSent wired event (reaction)
            _engine.NotifyUserMessage();    // BarkEngine.cs:167 — arm the chat-suppression window
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "BarkTriggerWiring: NotifyUserMessage failed"); }
    }

    private void Raise(string trigger, Action<BarkContext>? fill = null)
    {
        try { _engine.Raise(trigger, fill); }
        catch (Exception ex) { _logger?.LogWarning(ex, "BarkTriggerWiring: Raise('{Trigger}') failed", trigger); }
    }

    /// <summary>WPF phase-is-deepener heuristic: a deepener phase's name contains "deepen".</summary>
    private static bool IsDeepenerPhaseName(string? name) =>
        !string.IsNullOrEmpty(name) && name!.IndexOf("deepen", StringComparison.OrdinalIgnoreCase) >= 0;
}
