using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Serilog;
using static ConditioningControlPanel.Services.V2AuthService;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The pure half of the cloud profile adopt, moved from WPF ProfileSyncService /
    /// V2AuthServiceHead so every head adopts server values by the same rules: the XP watermark,
    /// take-higher level/XP, the season key and the curve epoch. Mutates the settings it is given
    /// and never saves; the caller saves and repaints. The long rationale for each rule stays on
    /// the WPF delegates in ProfileSyncService (#865, #920, B-7, #1270).
    /// </summary>
    public static class ProfileAdopt
    {
        public const double MeaningfulProgressXp = 100;

        /// <summary>WPF ProgressionService.ActiveCurveEpoch, read off the given settings.</summary>
        public static int Epoch(AppSettings s) =>
            s.DescentEpoch == XpCurve.CurveEpochDescent ? XpCurve.CurveEpochDescent : XpCurve.CurveEpochLegacy;

        /// <summary>WPF App.Progression.GetTotalXP(PlayerLevel, PlayerXP).</summary>
        public static double TotalXp(AppSettings s) => XpCurve.GetTotalXP(s.PlayerLevel, s.PlayerXP, Epoch(s));

        /// <summary>The (account, season)-scoped watermark that applies, or 0. V2 identities only.</summary>
        public static double ActiveXpWatermark(AppSettings settings)
        {
            if (settings.LastConfirmedServerXp <= 0) return 0;
            var account = settings.UnifiedId ?? string.Empty;
            if (account.Length == 0) return 0;
            if (!string.Equals(settings.LastConfirmedServerXpAccount ?? string.Empty, account, StringComparison.Ordinal)) return 0;
            var season = settings.CurrentSeason ?? string.Empty;
            if (!string.Equals(settings.LastConfirmedServerXpSeason ?? string.Empty, season, StringComparison.Ordinal)) return 0;
            return settings.LastConfirmedServerXp;
        }

        /// <summary>Record the total client and server now agree on; a client that kept a higher local does not agree.</summary>
        public static void RecordAgreedServerXp(AppSettings settings, double serverTotalXp, double clientTotalXp, string site)
        {
            if (serverTotalXp < MeaningfulProgressXp) return;
            if (string.IsNullOrEmpty(settings.UnifiedId))
            {
                Log.Debug("[XP watermark] {Site}: not arming — legacy identity has no account/season scope", site);
                return;
            }
            if (clientTotalXp > serverTotalXp + 0.01)
            {
                Log.Debug("[XP watermark] {Site}: not recording {Sx} — this client kept a higher local total ({Cx}), so there is no agreement to record",
                    site, (int)serverTotalXp, (int)clientTotalXp);
                return;
            }
            var account = settings.UnifiedId!;
            var season = settings.CurrentSeason ?? string.Empty;
            var previous = ActiveXpWatermark(settings);
            settings.LastConfirmedServerXpAccount = account;
            settings.LastConfirmedServerXpSeason = season;
            settings.LastConfirmedServerXp = serverTotalXp;
            if (previous > 0 && serverTotalXp < previous)
                Log.Information("[XP watermark] {Site}: LOWERED {Old} -> {New} — this client adopted the server's figure, so that is what both sides now agree on. The send-guard follows it down.",
                    site, (int)previous, (int)serverTotalXp);
            else
                Log.Debug("[XP watermark] {Site}: set to {Xp} for account {Account} season {Season}", site, (int)serverTotalXp, account, season);
        }

        /// <summary>Void the watermark (season rollover, logout/account switch).</summary>
        public static void ClearXpWatermark(AppSettings? settings, string reason)
        {
            if (settings == null) return;
            if (settings.LastConfirmedServerXp <= 0 && settings.LastConfirmedServerXpAccount == null) return;
            Log.Information("[XP watermark] cleared ({Reason}) - was {Xp} for season {Season}",
                reason, (int)settings.LastConfirmedServerXp, settings.LastConfirmedServerXpSeason ?? "(none)");
            settings.LastConfirmedServerXp = 0;
            settings.LastConfirmedServerXpAccount = null;
            settings.LastConfirmedServerXpSeason = null;
        }

        /// <summary><c>user.curve_epoch</c>: 0 or 1, anything else (including absent) is null.</summary>
        public static int? ParseCurveEpoch(JToken? token)
        {
            if (token is null || token.Type != JTokenType.Integer) return null;
            var v = token.Value<long>();
            return v == XpCurve.CurveEpochLegacy || v == XpCurve.CurveEpochDescent ? (int)v : null;
        }

        /// <summary>The re-priced ledger once this install follows the server's curve, or null when nothing moves.</summary>
        public static (int Level, double XpIntoLevel)? CurveEpochReprice(int localEpoch, int? serverEpoch, bool ceremonyPending, int level, double xpIntoLevel)
        {
            if (serverEpoch is null || ceremonyPending) return null;
            if (serverEpoch.Value != XpCurve.CurveEpochLegacy && serverEpoch.Value != XpCurve.CurveEpochDescent) return null;
            if (serverEpoch.Value == localEpoch) return null;
            return XpCurve.RepriceLedger(level, xpIntoLevel, localEpoch, serverEpoch.Value);
        }

        /// <summary>WPF ProfileSyncService.ApplyServerCurveEpoch's settings half. True when the ledger moved.</summary>
        public static bool ApplyCurveEpoch(AppSettings settings, int? serverEpoch)
        {
            if (serverEpoch is null) return false;
            var repriced = CurveEpochReprice(Epoch(settings), serverEpoch,
                DescentMigrationChoices.IsValid(settings.PendingDescentMigrationChoice), settings.PlayerLevel, settings.PlayerXP);
            if (repriced is null) return false;
            settings.DescentEpoch = serverEpoch.Value;
            settings.PlayerLevel = repriced.Value.Level;
            settings.PlayerXP = repriced.Value.XpIntoLevel;
            if (settings.PlayerLevel > settings.HighestLevelEver) settings.HighestLevelEver = settings.PlayerLevel;
            return true;
        }

        /// <summary>True if the string is a "yyyy-MM" month key.</summary>
        public static bool LooksLikeMonthKey(string k) =>
            k.Length == 7 && k[4] == '-' && int.TryParse(k.Substring(0, 4), out _) && int.TryParse(k.Substring(5, 2), out _);

        /// <summary>Adopt only a well-formed server season key strictly after the local one (or when local has none usable).</summary>
        public static bool ShouldAdoptServerSeason(string? serverSeason, string? localSeason)
        {
            if (string.IsNullOrWhiteSpace(serverSeason) || !LooksLikeMonthKey(serverSeason!)) return false;
            if (string.IsNullOrWhiteSpace(localSeason) || !LooksLikeMonthKey(localSeason!)) return true;
            return string.CompareOrdinal(serverSeason!, localSeason!) > 0;
        }

        /// <summary>
        /// The login-path adopt (WPF V2AuthServiceHead.ApplyUserDataToSettings): identity, the linked-tier
        /// grace extension, then level/XP take-higher by TOTAL XP. Does not save.
        /// </summary>
        public static void ApplyUserData(AppSettings settings, V2User user, string? authToken, DateTime nowUtc)
        {
            ApplyIdentity(settings, user, authToken);
            EntitlementTierRule.ExtendGrace(settings, user.PatreonTier, nowUtc);
            if (user.Level <= 0) return;
            var localTotalXp = TotalXp(settings);
            if (user.Xp >= localTotalXp)
            {
                settings.PlayerLevel = user.Level;
                settings.PlayerXP = XpCurve.GetCurrentLevelXP(user.Level, user.Xp, Epoch(settings));
            }
            else
                Log.Information("[V2Auth] Keeping local progress (higher): Local Level {LocalLevel} ({LocalXP} total) > Server Level {ServerLevel} ({ServerXP} total)",
                    settings.PlayerLevel, (int)localTotalXp, user.Level, user.Xp);
        }

        /// <summary>WPF ProfileSyncService.RefuseDescentEraLevelReset: no season reset is legitimate after the Descent
        /// (migrated account, clock past the epoch, or either season key post-Descent). Rationale stays on the WPF delegate.</summary>
        public static bool RefuseDescentEraLevelReset(string? serverSeason, string? localSeason, bool migrationCompleted, DateTime nowUtc) =>
            migrationCompleted || nowUtc >= DescentEpochs.SeasonsEndUtc
            || DescentEpochs.IsPostDescentSeasonKey(serverSeason) || DescentEpochs.IsPostDescentSeasonKey(localSeason);

        /// <summary>WPF sync merge: any server lead is adopted on a clean ledger (local at or under the watermark);
        /// otherwise only a lead over 5000 XP.</summary>
        public static double ServerAheadBand(AppSettings settings, double localTotalXp)
        {
            var watermark = ActiveXpWatermark(settings);
            return watermark > 0 && localTotalXp <= watermark + 0.01 ? 0 : 5000;
        }

        /// <summary>
        /// The <c>/v2/user/sync</c> response rules a head that only knows level/xp/season acts on, in WPF's order
        /// (ProfileSyncService.SyncProfileAsync): curve epoch, season forward, then <c>level_reset</c> (with the
        /// Descent refusal) or the server-ahead adopt, then the agreed watermark. Everything else is ignored.
        /// ponytail: no anti-cheat clamp (a downward write; skipping it errs safe), no skills union, recap or
        /// SeasonResetPending on level_reset - add each with its feature. Does not save.
        /// </summary>
        public static void ApplySyncResponse(AppSettings settings, JObject response, DateTime nowUtc)
        {
            // WPF ProfileSyncService.cs:2017-2046: the admin skills reset, its acknowledgement, else the
            // take-higher wallet adopt.
            ApplySkillsResetOrAdopt(settings, response);
            AdoptConditioningMinutes(settings, response["total_conditioning_minutes"], "V2 sync");   // WPF :2296
            if (response["user"] is not JObject node) return;
            var user = node.ToObject<V2User>()!;
            ApplyCurveEpoch(settings, user.CurveEpoch);
            if (ShouldAdoptServerSeason(user.CurrentSeason, settings.CurrentSeason))
            {
                Log.Information("V2 Sync: season key advanced {Old} -> {New}", settings.CurrentSeason ?? "(none)", user.CurrentSeason);
                settings.CurrentSeason = user.CurrentSeason;
                ClearXpWatermark(settings, "season rollover");
            }
            if (response["level_reset"]?.Type == JTokenType.Boolean && response.Value<bool>("level_reset"))
            {
                if (RefuseDescentEraLevelReset(user.CurrentSeason, settings.CurrentSeason, settings.DescentMigrationCompleted, nowUtc))
                    Log.Warning("[Descent] REFUSED a server level_reset. KEEPING Level {Level}; the server offered Level {ServerLevel} / XP {ServerXp}",
                        settings.PlayerLevel, user.Level, user.Xp);
                else
                {
                    ClearXpWatermark(settings, "admin level_reset");
                    Log.Information("V2 Sync: Level reset by admin — forcing Level {Level}, XP {Xp}", user.Level, user.Xp);
                    settings.PlayerLevel = user.Level;
                    settings.PlayerXP = XpCurve.GetCurrentLevelXP(user.Level, user.Xp, Epoch(settings));
                    settings.HighestLevelEver = user.HighestLevelEver;
                }
            }
            else if (user.Level > 0)
            {
                var localTotalXp = TotalXp(settings);
                if (user.Xp > localTotalXp + ServerAheadBand(settings, localTotalXp))
                {
                    Log.Information("V2 Sync: Server XP higher — adopting Level {ServerLevel} XP {ServerXp} (local was {LocalXp})",
                        user.Level, user.Xp, (int)localTotalXp);
                    settings.PlayerLevel = user.Level;
                    settings.PlayerXP = XpCurve.GetCurrentLevelXP(user.Level, user.Xp, Epoch(settings));
                }
            }
            RecordAgreedServerXp(settings, user.Xp, TotalXp(settings), "V2 sync");
        }

        /// <summary>
        /// <c>force_skills_reset</c> on a sync reply (WPF ProfileSyncService.cs:2017-2046 and
        /// ApplyForceSkillsReset, the admin <c>/admin/reset-skills</c>), in WPF's three branches:
        /// <list type="number">
        /// <item>flag set and not yet acknowledged: the tree is cleared, the refund lands and
        /// <see cref="AppSettings.PendingSkillsResetAck"/> is armed (it is on disk, so a crash cannot apply the
        /// reset twice). The next push carries <c>force_skills_reset: false</c> (<c>SyncPush.Body</c>).</item>
        /// <item>acknowledgement pending and the flag gone: the server took the ack, the flag is dropped.</item>
        /// <item>otherwise the plain take-higher adopt (<see cref="AdoptSkillPoints"/>).</item>
        /// </list>
        /// The refund is the reply's <c>skill_points</c>, or one point per level when the reply names none.
        /// DEVIATION from WPF, on purpose: WPF writes the refund outright; here it only RAISES the wallet
        /// (the wallet rule: only a debited receipt or a balance refusal may lower Sparkles, and a sync
        /// reply is a snapshot). A refund is the spent points coming back, so it is higher in every real
        /// case. True when anything moved (the caller saves).
        /// </summary>
        public static bool ApplySkillsResetOrAdopt(AppSettings settings, JObject response)
        {
            var flagged = response["force_skills_reset"]?.Type == JTokenType.Boolean && response.Value<bool>("force_skills_reset");
            if (flagged && !settings.PendingSkillsResetAck)
            {
                var named = response["skill_points"] is { Type: JTokenType.Integer } sp
                    ? (int)Math.Clamp(sp.Value<long>(), 0, SparklePoints.Cap)
                    : (int?)null;
                var refund = named ?? settings.PlayerLevel * SkillPointsBank.PointsPerLevel;
                var next = SparklePoints.MergeMax(refund, settings.SkillPoints);
                Log.Information("Applying force skills reset: clearing {Count} skills, points {Local} -> {Points} (refund {Refund})",
                    settings.UnlockedSkills?.Count ?? 0, settings.SkillPoints, next, refund);
                settings.UnlockedSkills = new System.Collections.Generic.List<string>();
                settings.SkillPoints = next;
                settings.PendingSkillsResetAck = true;
                return true;
            }
            if (settings.PendingSkillsResetAck && !flagged)
            {
                // Server flag was cleared by our acknowledgment.
                settings.PendingSkillsResetAck = false;
                return true;
            }
            return AdoptSkillPoints(settings, response["skill_points"], "V2 sync");
        }

        /// <summary>
        /// Total conditioning time, take-higher (WPF ProfileSyncService.cs:2296 and :3417): hours earned on
        /// another install are adopted BEFORE this one pushes its own total, so a fresh install never
        /// reports less than the account holds. Raises only. A running tracker's baseline moves with the
        /// lift (WPF :2308), or the stop would credit the gap a second time. Only a JSON number counts.
        /// </summary>
        public static bool AdoptConditioningMinutes(AppSettings settings, JToken? serverMinutes, string site)
        {
            if (serverMinutes is null || (serverMinutes.Type != JTokenType.Integer && serverMinutes.Type != JTokenType.Float)) return false;
            var server = serverMinutes.Value<double>();
            var local = settings.TotalConditioningMinutes;
            if (double.IsNaN(server) || double.IsInfinity(server) || !(server > local)) return false;
            Log.Information("{Site}: conditioning minutes server={Server:F1} > local={Local:F1}, adopting", site, server, local);
            settings.TotalConditioningMinutes = server;
            ConditioningTime.OnTotalLifted(server - local);
            return true;
        }

        /// <summary>
        /// Sparkles earned elsewhere (web Back Room, phone, another install) reach this one: WPF
        /// ProfileSyncService.cs:2040-2046 and :3382, the higher of server and local.
        /// WALLET RULE: a snapshot only RAISES <see cref="AppSettings.SkillPoints"/>. It never lowers it:
        /// only a debited receipt may (SkillPurchase, V2WalletAdoption), plus the one adoption after a
        /// balance refusal (<see cref="SparklePoints.AdoptAfterRefusal"/>), and neither runs here.
        /// Only a JSON integer counts. True when the balance rose (the caller saves).
        /// </summary>
        public static bool AdoptSkillPoints(AppSettings settings, JToken? serverSkillPoints, string site)
        {
            if (serverSkillPoints is null || serverSkillPoints.Type != JTokenType.Integer) return false;
            var server = (int)Math.Clamp(serverSkillPoints.Value<long>(), 0, SparklePoints.Cap);
            var local = settings.SkillPoints;
            var max = SparklePoints.MergeMax(server, local);
            if (max <= local) return false;
            Log.Information("{Site}: Skill points server={Server}, local={Local}, taking max ({Max})", site, server, local, max);
            settings.SkillPoints = max;
            return true;
        }

        /// <summary>
        /// Consent, read before write. <c>/v2/user/profile</c> returns two of the six consent values
        /// (<c>allow_discord_dm</c>, <c>show_online_status</c>); the account's value replaces the local one
        /// BEFORE the first push, so a local default never shares more than the account holds. Skipped while a
        /// change made here is still waiting for its sync (that change is newer than what the server holds).
        /// Only JSON booleans count. True when a setting moved (the caller saves).
        /// </summary>
        public static bool AdoptConsent(AppSettings settings, JObject? userNode)
        {
            if (userNode == null) return false;
            if (settings.ConsentPushPending && string.Equals(settings.ConsentOwnedAccount, settings.UnifiedId, StringComparison.Ordinal))
                return false;
            var changed = false;
            if (userNode["allow_discord_dm"] is { Type: JTokenType.Boolean } dm && dm.Value<bool>() != settings.AllowDiscordDm)
            {
                settings.AllowDiscordDm = dm.Value<bool>();
                changed = true;
            }
            if (userNode["show_online_status"] is { Type: JTokenType.Boolean } online && online.Value<bool>() != settings.ShowOnlineStatus)
            {
                settings.ShowOnlineStatus = online.Value<bool>();
                changed = true;
            }
            if (changed) Log.Information("Profile load: adopted the account's consent values (allow DM {Dm}, show online {Online})",
                settings.AllowDiscordDm, settings.ShowOnlineStatus);
            return changed;
        }

        /// <summary>
        /// The read-before-write adopt (WPF ProfileSyncService.ReadServerProfileBeforePushAsync) after the curve
        /// epoch: level/XP take-higher, the season key forward only (clearing the watermark), and the watermark
        /// when the server's season is the one we sync under. Nothing else. Returns true when the season advanced.
        /// </summary>
        public static bool AdoptReadBeforeWrite(AppSettings settings, V2User user)
        {
            var localTotalXp = TotalXp(settings);
            var keepSeason = settings.CurrentSeason;
            var seasonAdvances = ShouldAdoptServerSeason(user.CurrentSeason, keepSeason);
            var serverTotalXp = (double)user.Xp;
            if (user.Level > 0 && serverTotalXp >= localTotalXp)
            {
                settings.PlayerLevel = user.Level;
                settings.PlayerXP = XpCurve.GetCurrentLevelXP(user.Level, serverTotalXp, Epoch(settings));
            }
            if (seasonAdvances)
            {
                Log.Information("Read-before-write: season key advanced {Old} -> {New}",
                    string.IsNullOrEmpty(keepSeason) ? "(none)" : keepSeason, user.CurrentSeason);
                settings.CurrentSeason = user.CurrentSeason;
                ClearXpWatermark(settings, "season rollover (read-before-write)");
            }
            var scopedSeason = settings.CurrentSeason ?? string.Empty;
            if (string.Equals(user.CurrentSeason ?? string.Empty, scopedSeason, StringComparison.Ordinal))
                RecordAgreedServerXp(settings, serverTotalXp, TotalXp(settings), "read-before-write");
            else
                Log.Debug("Read-before-write: server season {SS} is not the scope we sync under ({LS}) — not recording a watermark",
                    string.IsNullOrEmpty(user.CurrentSeason) ? "(none)" : user.CurrentSeason,
                    string.IsNullOrEmpty(scopedSeason) ? "(none)" : scopedSeason);
            return seasonAdvances;
        }
    }
}
