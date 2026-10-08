using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The <c>POST /v2/user/sync</c> body, in exactly the property order WPF's anonymous object had
    /// (ProfileSyncService). A key is written only when its <see cref="Known"/> flag is set: WPF sets
    /// <see cref="Field.All"/>, so it still sends its explicit nulls (cosmetics, web_xp_claim_ack,
    /// install_date, force_skills_reset) byte-for-byte; a head that does not know a field leaves the
    /// key out rather than sending null. Goldens: Tests/CCP.Core.Tests/SyncBodyGoldenTests.cs.
    /// </summary>
    public sealed class SyncBody
    {
        [Flags]
        public enum Field : long
        {
            None = 0,
            UnifiedId = 1L << 0, Xp = 1L << 1, Level = 1L << 2, Achievements = 1L << 3, Stats = 1L << 4,
            UnlockedSkills = 1L << 5, SkillPoints = 1L << 6, TotalConditioningMinutes = 1L << 7,
            CompanionProgress = 1L << 8, AllowDiscordDm = 1L << 9, ShowOnlineStatus = 1L << 10,
            ShareProfilePicture = 1L << 11, PublicShareAvatar = 1L << 12, GoonShareAvatar = 1L << 13,
            GoonShareDm = 1L << 14, Cosmetics = 1L << 15, WebXpClaimAck = 1L << 16, InstallDate = 1L << 17,
            DescentEpoch = 1L << 18, ResetWeeklyQuest = 1L << 19, ResetDailyQuest = 1L << 20,
            ForceStreakOverride = 1L << 21, ForceSkillsReset = 1L << 22, DescentAuto = 1L << 23,
            All = (1L << 24) - 1,
        }

        [JsonIgnore] public Field Known { get; set; }
        private bool Has(Field f) => (Known & f) == f;

        [JsonProperty("unified_id")] public string? UnifiedId { get; set; }
        [JsonProperty("xp")] public int Xp { get; set; }
        [JsonProperty("level")] public int Level { get; set; }
        [JsonProperty("achievements")] public List<string>? Achievements { get; set; }
        [JsonProperty("stats")] public Dictionary<string, object>? Stats { get; set; }
        [JsonProperty("unlocked_skills")] public List<string>? UnlockedSkills { get; set; }
        [JsonProperty("skill_points")] public int SkillPoints { get; set; }
        [JsonProperty("total_conditioning_minutes")] public double TotalConditioningMinutes { get; set; }
        [JsonProperty("companion_progress")] public Dictionary<int, CompanionProgress>? CompanionProgress { get; set; }
        [JsonProperty("allow_discord_dm")] public bool AllowDiscordDm { get; set; }
        [JsonProperty("show_online_status")] public bool ShowOnlineStatus { get; set; }
        [JsonProperty("share_profile_picture")] public bool ShareProfilePicture { get; set; }
        [JsonProperty("public_share_avatar")] public bool PublicShareAvatar { get; set; }
        [JsonProperty("goon_share_avatar")] public bool GoonShareAvatar { get; set; }
        [JsonProperty("goon_share_dm")] public bool GoonShareDm { get; set; }
        [JsonProperty("cosmetics")] public ProfileCosmetics? Cosmetics { get; set; }
        [JsonProperty("web_xp_claim_ack")] public string? WebXpClaimAck { get; set; }
        [JsonProperty("install_date")] public string? InstallDate { get; set; }
        [JsonProperty("descent_epoch")] public int DescentEpoch { get; set; }
        // "This build takes a migration offer silently" (main 2026-10-06). Only a head that does
        // sets it: one without the migration must never claim it, or the server offers to it.
        [JsonProperty("descent_auto")] public bool DescentAuto { get; set; }
        [JsonProperty("reset_weekly_quest")] public bool ResetWeeklyQuest { get; set; }
        [JsonProperty("reset_daily_quest")] public bool ResetDailyQuest { get; set; }
        [JsonProperty("force_streak_override")] public bool ForceStreakOverride { get; set; }
        [JsonProperty("force_skills_reset")] public bool? ForceSkillsReset { get; set; }

        public bool ShouldSerializeUnifiedId() => Has(Field.UnifiedId);
        public bool ShouldSerializeXp() => Has(Field.Xp);
        public bool ShouldSerializeLevel() => Has(Field.Level);
        public bool ShouldSerializeAchievements() => Has(Field.Achievements);
        public bool ShouldSerializeStats() => Has(Field.Stats);
        public bool ShouldSerializeUnlockedSkills() => Has(Field.UnlockedSkills);
        public bool ShouldSerializeSkillPoints() => Has(Field.SkillPoints);
        public bool ShouldSerializeTotalConditioningMinutes() => Has(Field.TotalConditioningMinutes);
        public bool ShouldSerializeCompanionProgress() => Has(Field.CompanionProgress);
        public bool ShouldSerializeAllowDiscordDm() => Has(Field.AllowDiscordDm);
        public bool ShouldSerializeShowOnlineStatus() => Has(Field.ShowOnlineStatus);
        public bool ShouldSerializeShareProfilePicture() => Has(Field.ShareProfilePicture);
        public bool ShouldSerializePublicShareAvatar() => Has(Field.PublicShareAvatar);
        public bool ShouldSerializeGoonShareAvatar() => Has(Field.GoonShareAvatar);
        public bool ShouldSerializeGoonShareDm() => Has(Field.GoonShareDm);
        public bool ShouldSerializeCosmetics() => Has(Field.Cosmetics);
        public bool ShouldSerializeWebXpClaimAck() => Has(Field.WebXpClaimAck);
        public bool ShouldSerializeInstallDate() => Has(Field.InstallDate);
        public bool ShouldSerializeDescentEpoch() => Has(Field.DescentEpoch);
        public bool ShouldSerializeDescentAuto() => Has(Field.DescentAuto);
        public bool ShouldSerializeResetWeeklyQuest() => Has(Field.ResetWeeklyQuest);
        public bool ShouldSerializeResetDailyQuest() => Has(Field.ResetDailyQuest);
        public bool ShouldSerializeForceStreakOverride() => Has(Field.ForceStreakOverride);
        public bool ShouldSerializeForceSkillsReset() => Has(Field.ForceSkillsReset);

        /// <summary>WPF's four-field <c>/v2/user/heartbeat</c> body (ProfileSyncService.SendHeartbeatAsync).</summary>
        public static string Heartbeat(string unifiedId, bool isActive, bool inSession, string appVersion) =>
            JsonConvert.SerializeObject(new { unified_id = unifiedId, is_active = isActive, in_session = inSession, app_version = appVersion });

        /// <summary>HMAC-SHA256 hex of <c>"{timestamp}:{body}"</c>, keyed by the unified id plus the app key.</summary>
        public static string Signature(string unifiedId, string timestamp, string body)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes($"{unifiedId}:ccp-anticheat-2026"));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}:{body}"))).ToLowerInvariant();
        }

        /// <summary>Adds X-CCP-Timestamp and X-CCP-Signature. The caller refuses to send without a unified id (#894).</summary>
        public static void SignRequest(HttpRequestMessage request, string unifiedId, string body)
        {
            var timestamp = ServerClock.UtcNow.ToUnixTimeSeconds().ToString(); // server-corrected: a skewed PC clock 403s every sync (release/6.11.5)
            request.Headers.Add("X-CCP-Timestamp", timestamp);
            request.Headers.Add("X-CCP-Signature", Signature(unifiedId, timestamp, body));
        }
    }
}
