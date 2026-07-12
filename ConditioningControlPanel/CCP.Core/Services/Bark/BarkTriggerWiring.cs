using System;
using ConditioningControlPanel.Core.Services.Awareness;
using ConditioningControlPanel.Core.Services.Progression;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Core.Services.Bark;

/// <summary>
/// The port's equivalent of the WPF bark <c>WireSubscriptions</c> block (Services/Companion/BarkService.cs:427)
/// plus the awareness pair (:562-577). Owns engine <see cref="BarkEngine.Start"/> and subscribes the
/// readily-available Core event sources (awareness, session, video, progression, achievement, quest,
/// and mod-change reload) into <see cref="BarkEngine.Raise"/>. BARK-1 slice 3; the awareness pair
/// closes AI-10 (the awareness→bark dead seam).
/// </summary>
/// <remarks>
/// <para>Threading: <see cref="IAwarenessService.ActivityChanged"/>/<see cref="IAwarenessService.StillOnActivity"/>
/// fire on a background thread-pool thread; <see cref="BarkEngine.Raise"/> is internally locked and the
/// speaker (<c>AvatarBarkSpeaker</c>) marshals to the UI thread, so no extra marshalling is needed here.</para>
/// <para>Privacy: awareness ctx stamps only the category/service/cluster/app id and minutes-on-activity —
/// never the raw window title (mirrors WPF BarkService.cs:562-577). No new disk/network sinks.</para>
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

    public BarkTriggerWiring(
        BarkEngine engine,
        IAwarenessService? awareness = null,
        IModService? mods = null,
        ISessionService? sessions = null,
        IVideoService? video = null,
        IProgressionService? progression = null,
        IAchievementService? achievements = null,
        IQuestService? quests = null,
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
            if (_quests != null) _quests.QuestCompleted += _onQuestCompleted;
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "BarkTriggerWiring: subscription failed"); }
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
            if (_quests != null) _quests.QuestCompleted -= _onQuestCompleted;
        }
        catch { /* never throw from Dispose */ }
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
