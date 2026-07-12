using System;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Webcam;

namespace ConditioningControlPanel.Core.Services.Bark;

/// <summary>
/// Live-read provider for the bark condition matcher's well-known fields (WPF ResolveField,
/// BarkService.cs:1086-1139). The WPF head resolves these against static singletons
/// (App.Video, App.Webcam, BarkState, App.Settings, ...); the port injects this seam.
///
/// Contract: <see cref="TryResolve"/> returns TRUE for every WELL-KNOWN field name (lowercased),
/// even when its value is currently unavailable — a well-known field must SHADOW any same-named
/// per-fire ctx value exactly as the WPF switch does (ctx fallback only for unknown fields,
/// BarkService.cs:1137-1138). A well-known-but-unavailable field yields <c>value = null</c>,
/// which the matcher treats as condition-false (safe degradation: the rule simply never fires).
/// </summary>
public interface IBarkLiveFields
{
    /// <summary>
    /// Resolve a well-known live field (field name already lowercased by the caller).
    /// Returns false when the field is not a well-known live field (matcher falls back to ctx).
    /// Default implementation knows no fields so bare fakes compile.
    /// </summary>
    bool TryResolve(string field, out object? value)
    {
        value = null;
        return false;
    }
}

/// <summary>
/// Default live fields for the port. Resolves the cheaply-reachable subset (settings-backed
/// progression/time values, video/webcam/session state via existing DI seams) and reports the
/// BarkState-backed counters as null until slice 3 ports BarkState + its event feeders.
/// </summary>
public sealed class BarkLiveFields : IBarkLiveFields
{
    private readonly ISettingsService? _settings;
    private readonly IVideoInfo? _video;
    private readonly IWebcamService? _webcam;
    private readonly ISessionService? _session;

    public BarkLiveFields(
        ISettingsService? settings = null,
        IVideoInfo? video = null,
        IWebcamService? webcam = null,
        ISessionService? session = null)
    {
        _settings = settings;
        _video = video;
        _webcam = webcam;
        _session = session;
    }

    private bool SessionRunning => _session?.State == SessionState.Running;

    public bool TryResolve(string field, out object? value)
    {
        switch (field)
        {
            // --- live via existing seams (WPF BarkService.cs:1090-1098) ---
            case "video_playing": value = _video?.IsPlaying ?? false; return true;             // :1090
            case "webcam_running": value = _webcam?.IsRunning ?? false; return true;           // :1094
            case "session_running": value = SessionRunning; return true;                       // :1095 (BarkState.SessionRunning)
            case "session_elapsed_sec":                                                        // :1097 (BarkState.SessionElapsedSeconds)
                value = SessionRunning ? _session!.ElapsedTime.TotalSeconds : 0d; return true;
            case "session_phase_index":                                                        // :1098 (BarkState.SessionPhaseIndex)
                value = SessionRunning ? _session!.CurrentPhaseIndex : -1; return true;

            // --- settings-backed (WPF BarkService.cs:1105-1110) ---
            case "master_volume": value = (double)(_settings?.Current?.MasterVolume ?? 0); return true;      // :1105
            case "mute": value = (_settings?.Current?.MasterVolume ?? 0) == 0; return true;                  // :1106
            case "player_level": value = (double)(_settings?.Current?.PlayerLevel ?? 0); return true;        // :1107
            case "total_sessions": value = (double)(_settings?.Current?.TotalSessions ?? 0); return true;    // :1108
            case "daily_quest_streak": value = (double)(_settings?.Current?.DailyQuestStreak ?? 0); return true; // :1109
            case "current_streak": value = (double)(_settings?.Current?.CurrentStreak ?? 0); return true;    // :1110

            // --- date / time-of-day (WPF BarkService.cs:1126-1131) ---
            case "is_nye":
            {
                var now = DateTime.Now; // Dec 31 or Jan 1 (local), WPF :1128
                value = (now.Month == 12 && now.Day == 31) || (now.Month == 1 && now.Day == 1);
                return true;
            }
            case "local_hour": value = (double)DateTime.Now.Hour; return true;                 // :1131

            // --- well-known but not yet reachable in the port: BarkState counters + feeders
            //     (slice 3) and achievement/skill-tree totals. null → condition false → the rule
            //     does not fire (safe degradation; WPF would return the live value). ---
            case "setup_idle_sec":            // :1096 (BarkState.SetupIdleSeconds)
            case "blink_count":               // :1099 (BarkState.BlinkCount)
            case "face_lost_sec":             // :1100 (BarkState.FaceLostSeconds)
            case "mod_switches_60s":          // :1101 (BarkState.ModSwitchesWithin)
            case "clicks_60s":                // :1102 (BarkState.AvatarClicksWithin)
            case "days_away":                 // :1103 (BarkState.DaysAwayAtLaunch)
            case "instant_relaunch":          // :1104 (BarkState.InstantRelaunch)
            case "achievements_all_unlocked": // :1113-1117 (App.Achievements totals)
            case "all_skills_unlocked":       // :1118-1122 (App.SkillTree totals)
            case "phase_name":                // :1134 (BarkState.CurrentPhaseName, event-stamped)
            case "phase_is_deepener":         // :1135 (BarkState.CurrentPhaseIsDeepener)
                value = null;
                return true;

            default:
                value = null;
                return false; // not well-known → matcher falls back to ctx.Values (WPF :1137-1138)
        }
    }
}
