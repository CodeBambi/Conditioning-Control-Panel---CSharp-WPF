using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Core.Services.Settings;

// Server-contract DTOs for ProfileSyncService. Ported verbatim from the WPF
// ProfileSyncService "#region DTOs" (Services/Settings/ProfileSyncService.cs, ~lines 2682-3011).
// [JsonProperty] names and types are the wire contract and must not drift.
// Unknown server fields are silently dropped (Newtonsoft default), matching WPF.

/// <summary>Easter-egg reader counter response (<c>/v2/easter-egg</c>).</summary>
public class EasterEggResponse
{
    [JsonProperty("count")]
    public int Count { get; set; }
}

/// <summary>V1 profile pull response (<c>/user/profile</c>).</summary>
public class ProfileResponse
{
    [JsonProperty("exists")]
    public bool Exists { get; set; }

    [JsonProperty("user_id")]
    public string? UserId { get; set; }

    [JsonProperty("profile")]
    public CloudProfile? Profile { get; set; }
}

/// <summary>V1 sync response (<c>/user/sync</c>).</summary>
public class SyncResponse
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("user_id")]
    public string? UserId { get; set; }

    [JsonProperty("profile")]
    public CloudProfile? Profile { get; set; }

    [JsonProperty("merged")]
    public bool Merged { get; set; }
}

/// <summary>Cloud profile payload (V1 pull/merge).</summary>
public class CloudProfile
{
    [JsonProperty("xp")]
    public int Xp { get; set; }

    [JsonProperty("level")]
    public int Level { get; set; }

    [JsonProperty("achievements")]
    public List<string>? Achievements { get; set; }

    [JsonProperty("stats")]
    public Dictionary<string, object>? Stats { get; set; }

    [JsonProperty("last_session")]
    public string? LastSession { get; set; }

    [JsonProperty("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonProperty("skill_points")]
    public int? SkillPoints { get; set; }

    [JsonProperty("unlocked_skills")]
    public List<string>? UnlockedSkills { get; set; }

    [JsonProperty("total_conditioning_minutes")]
    public double? TotalConditioningMinutes { get; set; }

    [JsonProperty("companion_progress")]
    public Dictionary<string, CompanionProgress>? CompanionProgress { get; set; }

    [JsonProperty("reset_weekly_quest")]
    public bool? ResetWeeklyQuest { get; set; }

    [JsonProperty("reset_daily_quest")]
    public bool? ResetDailyQuest { get; set; }

    [JsonProperty("force_streak_override")]
    public bool? ForceStreakOverride { get; set; }
}

/// <summary>V1 sync push payload (<c>/user/sync</c>).</summary>
public class ProfileSyncData
{
    [JsonProperty("xp")]
    public int Xp { get; set; }

    [JsonProperty("level")]
    public int Level { get; set; }

    [JsonProperty("achievements")]
    public List<string>? Achievements { get; set; }

    [JsonProperty("stats")]
    public Dictionary<string, object>? Stats { get; set; }

    [JsonProperty("last_session")]
    public string? LastSession { get; set; }

    [JsonProperty("allow_discord_dm")]
    public bool AllowDiscordDm { get; set; }

    [JsonProperty("share_profile_picture")]
    public bool ShareProfilePicture { get; set; }

    [JsonProperty("show_online_status")]
    public bool ShowOnlineStatus { get; set; } = true;

    [JsonProperty("discord_id")]
    public string? DiscordId { get; set; }

    [JsonProperty("avatar_url")]
    public string? AvatarUrl { get; set; }

    [JsonProperty("skill_points")]
    public int SkillPoints { get; set; }

    [JsonProperty("unlocked_skills")]
    public List<string>? UnlockedSkills { get; set; }

    [JsonProperty("total_conditioning_minutes")]
    public double TotalConditioningMinutes { get; set; }

    [JsonProperty("reset_weekly_quest")]
    public bool ResetWeeklyQuest { get; set; }

    [JsonProperty("reset_daily_quest")]
    public bool ResetDailyQuest { get; set; }

    [JsonProperty("force_streak_override")]
    public bool ForceStreakOverride { get; set; }
}

/// <summary>V2 sync response (<c>/v2/user/sync</c>) — the authoritative reconciliation payload.</summary>
public class V2SyncResponse
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("reset_weekly_quest")]
    public bool? ResetWeeklyQuest { get; set; }

    [JsonProperty("reset_daily_quest")]
    public bool? ResetDailyQuest { get; set; }

    [JsonProperty("force_streak_override")]
    public bool? ForceStreakOverride { get; set; }

    [JsonProperty("streak_stats")]
    public V2StreakStats? StreakStats { get; set; }

    [JsonProperty("force_skills_reset")]
    public bool? ForceSkillsReset { get; set; }

    [JsonProperty("skill_points")]
    public int? SkillPoints { get; set; }

    [JsonProperty("unlocked_skills")]
    public List<string>? UnlockedSkills { get; set; }

    [JsonProperty("oopsie_used_season")]
    public string? OopsieUsedSeason { get; set; }

    [JsonProperty("is_season0_og")]
    public bool? IsSeason0Og { get; set; }

    [JsonProperty("patreon_is_whitelisted")]
    public bool? PatreonIsWhitelisted { get; set; }

    [JsonProperty("bonus_daily_rerolls")]
    public int? BonusDailyRerolls { get; set; }

    [JsonProperty("bonus_weekly_rerolls")]
    public int? BonusWeeklyRerolls { get; set; }

    [JsonProperty("level_reset")]
    public bool? LevelReset { get; set; }

    [JsonProperty("lifetime_points_spent")]
    public long? LifetimePointsSpent { get; set; }

    [JsonProperty("total_xp_earned")]
    public double? TotalXpEarned { get; set; }

    [JsonProperty("total_conditioning_minutes")]
    public double? TotalConditioningMinutes { get; set; }

    [JsonProperty("companion_progress")]
    public Dictionary<string, CompanionProgress>? CompanionProgress { get; set; }

    [JsonProperty("user")]
    public V2SyncUser? User { get; set; }
}

/// <summary>The <c>user</c> block of a <see cref="V2SyncResponse"/>.</summary>
public class V2SyncUser
{
    [JsonProperty("display_name")]
    public string? DisplayName { get; set; }

    [JsonProperty("level")]
    public int Level { get; set; }

    [JsonProperty("xp")]
    public int Xp { get; set; }

    [JsonProperty("highest_level_ever")]
    public int? HighestLevelEver { get; set; }

    [JsonProperty("achievements")]
    public List<string>? Achievements { get; set; }

    [JsonProperty("stats")]
    public Dictionary<string, object>? Stats { get; set; }
}

/// <summary>Success response for <c>/v2/user/use-oopsie</c>.</summary>
public class OopsieSuccessResponse
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("new_xp")]
    public int NewXp { get; set; }

    [JsonProperty("oopsie_used_season")]
    public string? OopsieUsedSeason { get; set; }
}

/// <summary>Error response for <c>/v2/user/use-oopsie</c>.</summary>
public class OopsieErrorResponse
{
    [JsonProperty("error")]
    public string? Error { get; set; }
}

/// <summary>Response for <c>/v2/user/purchase-skill</c>.</summary>
public class PurchaseSkillResponse
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("error")]
    public string? Error { get; set; }

    [JsonProperty("skill_points")]
    public int? SkillPoints { get; set; }

    [JsonProperty("unlocked_skills")]
    public List<string>? UnlockedSkills { get; set; }

    [JsonProperty("lifetime_points_spent")]
    public long? LifetimePointsSpent { get; set; }
}

/// <summary>Success response for <c>/v2/user/change-display-name</c>.</summary>
public class ChangeDisplayNameResponse
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("new_display_name")]
    public string? NewDisplayName { get; set; }
}

/// <summary>Error response for <c>/v2/user/change-display-name</c>.</summary>
public class ChangeDisplayNameErrorResponse
{
    [JsonProperty("error")]
    public string? Error { get; set; }
}

/// <summary>Success response for <c>/v2/user/delete-account</c>.</summary>
public class DeleteAccountResponse
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("deleted_unified_id")]
    public string? DeletedUnifiedId { get; set; }

    [JsonProperty("deleted_display_name")]
    public string? DeletedDisplayName { get; set; }
}

/// <summary>Error response for <c>/v2/user/delete-account</c>.</summary>
public class DeleteAccountErrorResponse
{
    [JsonProperty("error")]
    public string? Error { get; set; }
}

/// <summary>Response for <c>/v2/user/settings-backup</c>.</summary>
public class SettingsBackupResponse
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("backup")]
    public SettingsBackupData? Backup { get; set; }
}

/// <summary>The <c>backup</c> block of a <see cref="SettingsBackupResponse"/>.</summary>
public class SettingsBackupData
{
    [JsonProperty("settings_data")]
    public string? SettingsData { get; set; }

    [JsonProperty("app_version")]
    public string? AppVersion { get; set; }

    [JsonProperty("backed_up_at")]
    public string? BackedUpAt { get; set; }

    [JsonProperty("size_bytes")]
    public int SizeBytes { get; set; }
}

/// <summary>V2 streak statistics block of a <see cref="V2SyncResponse"/>.</summary>
public class V2StreakStats
{
    [JsonProperty("daily_quest_streak")]
    public int DailyQuestStreak { get; set; }

    [JsonProperty("last_daily_quest_date")]
    public string? LastDailyQuestDate { get; set; }

    [JsonProperty("quest_completion_dates")]
    public List<string>? QuestCompletionDates { get; set; }

    [JsonProperty("total_daily_quests_completed")]
    public int TotalDailyQuestsCompleted { get; set; }

    [JsonProperty("total_weekly_quests_completed")]
    public int TotalWeeklyQuestsCompleted { get; set; }

    [JsonProperty("total_xp_from_quests")]
    public int TotalXPFromQuests { get; set; }
}

/// <summary>
/// Public metadata about a cloud settings backup (for UI display).
/// </summary>
public class SettingsBackupInfo
{
    public string? AppVersion { get; set; }
    public DateTime? BackedUpAt { get; set; }
    public int SizeBytes { get; set; }
}
