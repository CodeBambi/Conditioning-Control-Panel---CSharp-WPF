using System;
using System.Collections.Generic;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles XP, leveling, and unlockables.
    /// Will be expanded in future sessions.
    /// </summary>
    public class ProgressionService
    {
        public event EventHandler<int>? LevelUp;
        public event EventHandler<double>? XPChanged;

        /// <summary>
        /// One XP award, post-multiplier. <see cref="LeveledUp"/> means <c>SpendXPOnLevels</c>
        /// crossed at least one level inside this same award - which is the whole reason this
        /// carries more than a number. A presentation layer has to be able to yield to the level-up
        /// celebration, and once <see cref="LevelUp"/> has fired there is no way to tell from
        /// <see cref="XPChanged"/> alone which award caused it.
        /// </summary>
        public readonly record struct XpAward(double Amount, XPSource Source, bool LeveledUp);

        /// <summary>
        /// Raised once per award that actually landed, AFTER <see cref="XPChanged"/> and on the same
        /// call stack. Deliberately a second event rather than a fatter <see cref="XPChanged"/>:
        /// XPChanged reports the ledger TOTAL and a dozen things read it, while this reports the
        /// DELTA and its provenance, which is what a celebration needs and nothing else wants.
        ///
        /// <para>Presentation only, and it fires after the ledger is already settled - nothing
        /// downstream of it may touch <c>PlayerXP</c>. A subscriber that throws is swallowed (see
        /// <see cref="RaiseXpAwarded"/>): a decoration must never be able to break an award.</para>
        /// </summary>
        public event EventHandler<XpAward>? XPAwarded;

        // The curve maths lives in Core (XpCurve); these delegate so callers are unchanged.
        public const int CurveEpochLegacy = XpCurve.CurveEpochLegacy;
        public const int CurveEpochDescent = XpCurve.CurveEpochDescent;
        public const int MaxDerivableLevel = XpCurve.MaxDerivableLevel;

        /// <summary>
        /// Whether this run has already announced the Cycle bonus in the log. ONCE PER LAUNCH, not
        /// once per award: the bonus rides every single XP grant, so an unguarded line would bury
        /// the log it is meant to make readable. Support asks "is their bonus actually on?" and
        /// this is the one line that answers it from a bug report's log alone.
        /// </summary>
        private bool _cycleBonusLogged;

        /// <summary>
        /// Awards XP to both the user (for feature unlocks) and the active companion (for companion leveling).
        /// </summary>
        /// <param name="amount">Base XP amount to award.</param>
        /// <param name="source">What action generated this XP (for companion bonuses).</param>
        /// <param name="context">Context about how the XP was earned (for companion modifiers).</param>
        public void AddXP(double amount, XPSource source = XPSource.Other, XPContext? context = null)
        {
            // Allow progression in offline mode with local-only storage
            var isOfflineMode = App.Settings?.Current?.OfflineMode == true;
            var hasOfflineUsername = !string.IsNullOrWhiteSpace(App.Settings?.Current?.OfflineUsername);

            // Require either: logged in (for cloud sync) OR offline mode with username (local only)
            if (!App.IsLoggedIn && !(isOfflineMode && hasOfflineUsername))
            {
                App.Logger?.Debug("XP not awarded - user not logged in and not in offline mode");
                return;
            }

            // Anti-cheat: Suppress passive XP sources when user is idle (AFK farming prevention)
            if (App.ActivityTracker?.IsIdle == true)
            {
                // Only suppress passive (non-interaction) sources
                if (source == XPSource.Flash || source == XPSource.Subliminal || source == XPSource.BouncingText)
                {
                    App.Logger?.Debug("XP suppressed (idle): +{Amount} from {Source}", amount, source);
                    return;
                }
            }

            // Track whether we're in offline mode for skipping cloud features
            var inOfflineMode = isOfflineMode && hasOfflineUsername;

            var settings = App.Settings.Current;

            // Apply skill tree XP multiplier (sparkle boost, time bonuses, pink rush, etc.)
            // times the Cycle bonus — the lasting reward for having chosen "Descend again" at
            // the migration ceremony. DescentCycleXpBonus is 1.0 for everybody who has not, so
            // this is arithmetically invisible until a Cycle mark exists.
            var skillMultiplier = App.SkillTree?.GetTotalXpMultiplier() ?? 1.0;
            var cycleMultiplier = Descent.DescentMigration.ActiveCycleXpBonus;
            var adjustedAmount = amount * skillMultiplier * cycleMultiplier;

            if (cycleMultiplier > 1.0 && !_cycleBonusLogged)
            {
                _cycleBonusLogged = true;
                App.Logger?.Information(
                    "Descent Cycle XP bonus active: x{Mult} applied to every award this run", cycleMultiplier);
            }

            var previousXP = settings.PlayerXP;
            settings.PlayerXP += adjustedAmount;

            // Track total XP earned (for stats)
            App.Achievements?.TrackXPEarned(adjustedAmount);

            if (Math.Abs(skillMultiplier - 1.0) > 0.001)
            {
                App.Logger?.Information("XP awarded: +{Amount} (base {Base} x {Mult:P0} skill bonus) - was {Prev}, now {Now}",
                    adjustedAmount, amount, skillMultiplier, previousXP, settings.PlayerXP);
            }
            else
            {
                App.Logger?.Information("XP awarded: +{Amount} (was {Prev}, now {Now})", adjustedAmount, previousXP, settings.PlayerXP);
            }

            // Route XP to the active companion (v5.3 companion leveling system)
            App.Companion?.AddCompanionXP(amount, source, context);

            // Track for quests (only for "earn X XP" type quests)
            App.Quests?.TrackXPEarned((int)amount);

            // Check for level up
            var leveledUp = SpendXPOnLevels(settings, inOfflineMode);

            XPChanged?.Invoke(this, settings.PlayerXP);
            RaiseXpAwarded(adjustedAmount, source, leveledUp);
        }

        /// <summary>
        /// Awards XP that the server already decided the exact size of - currently only the web XP
        /// claim settled through /v2/user/sync (see ProfileSyncService's claim handshake).
        ///
        /// Deliberately NOT AddXP: the amount is the server's, so it takes no skill-tree multiplier,
        /// no idle suppression, no companion routing and no quest credit - doubling it here would let
        /// a web action out-earn the same action in the app. What it DOES share is the level-up loop,
        /// so a claim that pushes the player over the line gets the full level-up experience
        /// (celebration, haptics, level achievements, skill points, leaderboard sync).
        /// </summary>
        /// <param name="amount">Exact XP to add, as minted by the server.</param>
        public void AddClaimedXP(double amount)
        {
            if (amount <= 0) return;

            // Same gate as AddXP: logged in, or offline mode with a local username
            var isOfflineMode = App.Settings?.Current?.OfflineMode == true;
            var hasOfflineUsername = !string.IsNullOrWhiteSpace(App.Settings?.Current?.OfflineUsername);

            if (!App.IsLoggedIn && !(isOfflineMode && hasOfflineUsername))
            {
                App.Logger?.Debug("Web XP claim not applied - user not logged in and not in offline mode");
                return;
            }

            var inOfflineMode = isOfflineMode && hasOfflineUsername;
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var previousXP = settings.PlayerXP;
            settings.PlayerXP += amount;

            // Track total XP earned (for stats)
            App.Achievements?.TrackXPEarned(amount);

            App.Logger?.Information("Web XP claim applied: +{Amount} (was {Prev}, now {Now})", amount, previousXP, settings.PlayerXP);

            var leveledUp = SpendXPOnLevels(settings, inOfflineMode);

            XPChanged?.Invoke(this, settings.PlayerXP);

            // XPSource.Other because that is the truth: a web claim has no in-app feature behind it,
            // so nothing on screen is the place it came from. A presentation layer reading this will
            // fall back to its neutral origin, which is the honest look for XP earned elsewhere.
            RaiseXpAwarded(amount, XPSource.Other, leveledUp);

            AnnounceFirstWebXpClaim(amount);
        }

        /// <summary>Seen-list key for the one-time web XP notice. Deliberately parked in
        /// <c>SeenFeatureIntros</c> even though this is a toast and not a card: that list is a
        /// generic "this install has been told" set, and borrowing it costs no new settings
        /// property. There is no matching entry in <c>FeatureIntros.All</c>, so nothing can ever
        /// open a modal for it.</summary>
        private const string WebXpNoticeKey = "web-xp";

        /// <summary>
        /// THE FIRST TIME ONLY: a plain toast explaining where the XP came from.
        ///
        /// <para>Web XP arrives silently through the /v2/user/sync handshake, which means a user
        /// who earned it on the site can watch the desktop bar jump - or a whole level land - for
        /// no reason they performed. That reads as a bug, and it has been reported as one before.
        /// One sentence, once per install, and never again.</para>
        ///
        /// <para>A toast rather than a <c>FeatureIntroPopup</c> card on purpose: a sync
        /// can settle at any moment, including mid-session, and the notification surface is the
        /// only one in the app that cannot interrupt anything. It also needs no session guard
        /// for the same reason.</para>
        /// </summary>
        private static void AnnounceFirstWebXpClaim(double amount)
        {
            try
            {
                if (App.Settings?.Current?.SeenFeatureIntros.Contains(WebXpNoticeKey) == true) return;

                // The claim is applied from ProfileSyncService's async pipeline, so the toast
                // (which builds real WPF elements) has to be marshalled - and the seen-flag is
                // spent on that thread too, next to the show, so two syncs racing can only ever
                // produce one notice.
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;

                dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var live = App.Settings?.Current;
                        if (live == null || live.SeenFeatureIntros.Contains(WebXpNoticeKey)) return;
                        live.SeenFeatureIntros.Add(WebXpNoticeKey);
                        App.Settings?.Save();

                        // Literal English, like every other one-shot explainer in the app (see
                        // the note on FeatureIntroContent) - nine translated rows for a string
                        // each install sees exactly once is not a trade worth making.
                        App.Notifications?.Show(
                            $"+{amount:0} XP from your web account - descending is account-wide now.",
                            NotificationType.Success,
                            TimeSpan.FromSeconds(9));
                    }
                    catch (Exception ex) { App.Logger?.Warning(ex, "Web XP notice failed to show"); }
                }));
            }
            catch (Exception ex) { App.Logger?.Debug("Web XP notice gate failed: {E}", ex.Message); }
        }

        /// <summary>
        /// Fire <see cref="XPAwarded"/> for one settled award. Wrapped whole: this is a decoration
        /// hanging off the end of the XP path, and the ledger has already been written by the time
        /// it runs, so a throwing subscriber must be logged and forgotten rather than allowed to
        /// unwind through the caller and take the award's remaining work with it.
        /// </summary>
        private void RaiseXpAwarded(double amount, XPSource source, bool leveledUp)
        {
            try
            {
                XPAwarded?.Invoke(this, new XpAward(amount, source, leveledUp));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("XPAwarded subscriber failed: {E}", ex.Message);
            }
        }

        /// <summary>
        /// Drains banked XP into levels for as long as the player can afford the next one, running
        /// the full level-up experience for each level gained. Shared by AddXP and AddClaimedXP so
        /// the two ways XP can arrive can never drift apart.
        /// </summary>
        /// <returns>
        /// True if at least one level was gained. Reported rather than inferred: a caller cannot
        /// work this out afterwards without re-reading the level it captured beforehand, and the
        /// one consumer that needs it (THE BANK, which must yield to the level-up celebration)
        /// needs it per AWARD, not per level.
        /// </returns>
        private bool SpendXPOnLevels(Models.AppSettings settings, bool inOfflineMode)
        {
            var gained = false;
            var xpNeeded = GetXPForLevel(settings.PlayerLevel);
            while (settings.PlayerXP >= xpNeeded)
            {
                settings.PlayerXP -= xpNeeded;
                settings.PlayerLevel++;
                gained = true;

                // Track highest level ever for permanent unlocks across seasons
                if (settings.PlayerLevel > settings.HighestLevelEver)
                {
                    settings.HighestLevelEver = settings.PlayerLevel;
                }

                xpNeeded = GetXPForLevel(settings.PlayerLevel);
                LevelUp?.Invoke(this, settings.PlayerLevel);
                _ = App.Haptics?.LevelUpPatternAsync();
                App.Logger.Information("Level up! Now level {Level}", settings.PlayerLevel);

                // Check level-based achievements immediately on level up
                App.Achievements?.CheckLevelAchievements(settings.PlayerLevel);

                // Award skill points for the enhancement tree (SkillTreeService.PointsPerLevel)
                App.SkillTree?.OnLevelUp(settings.PlayerLevel);

                // Skip cloud/network features in offline mode
                if (!inOfflineMode)
                {
                    // Update Discord Rich Presence level
                    App.DiscordRpc?.UpdateLevel(settings.PlayerLevel);

                    // Send Discord webhook for level milestones (10, 25, 50, 100, etc.)
                    // Always use CustomDisplayName for privacy - never expose real Discord/Patreon names
                    if (settings.DiscordShareLevelUps && IsLevelMilestone(settings.PlayerLevel))
                    {
                        var displayName = App.Discord?.CustomDisplayName ?? App.Patreon?.DisplayName ?? "Someone";
                        _ = App.Discord?.SendLevelUpWebhookAsync(settings.PlayerLevel, displayName);
                    }

                    // Sync profile to cloud on level up so leaderboard updates
                    _ = App.ProfileSync?.SyncProfileAsync();
                }
                else
                {
                    App.Logger?.Debug("Offline mode: Skipped cloud sync for level up to {Level}", settings.PlayerLevel);
                }
            }

            return gained;
        }

        /// <summary>
        /// Which XP curve THIS install is on right now. <see cref="CurveEpochLegacy"/> for every
        /// account that has not been through the Descent migration ceremony — which today is all
        /// of them, because nothing sets <c>DescentEpoch</c> until the ceremony's submit runs.
        ///
        /// <para>This is the per-USER epoch (AppSettings), deliberately NOT the per-BUILD wire
        /// constant <see cref="Descent.DescentEpochs.ClientEpoch"/>. The build says "I am a client
        /// that understands epoch 1"; this says "this account has crossed over". Conflating them
        /// would recurve everybody the moment they installed the new version.</para>
        /// </summary>
        public static int ActiveCurveEpoch => XpCurve.EpochOf(App.Settings?.Current);

        /// <summary>XP to clear <paramref name="level"/> on the curve this account is currently on.</summary>
        public double GetXPForLevel(int level) => GetXPForLevel(level, ActiveCurveEpoch);

        public static double GetXPForLevel(int level, int epoch) => XpCurve.GetXPForLevel(level, epoch);
        public static double XpForLevelV1(int level) => XpCurve.XpForLevelV1(level);
        public static double XpForLevelV2(int level) => XpCurve.XpForLevelV2(level);
        public static double QuestLevelScale(int level, int epoch) => XpCurve.QuestLevelScale(level, epoch);

        /// <summary>Quest scale on the curve this account is currently on.</summary>
        public static double QuestLevelScale(int level) => QuestLevelScale(level, ActiveCurveEpoch);

        public static double CumulativeXpToReachLevel(int level, int epoch) => XpCurve.CumulativeXpToReachLevel(level, epoch);
        public static (int Level, double XpIntoLevel) DeriveLevelFromLifetimeXp(double lifetimeXp, int epoch) =>
            XpCurve.DeriveLevelFromLifetimeXp(lifetimeXp, epoch);
        public static double CumulativeXpBeforeLevel(int level, int epoch) => XpCurve.CumulativeXpBeforeLevel(level, epoch);
        public static (int Level, double XpIntoLevel) RepriceLedger(int level, double xpIntoLevel, int fromEpoch, int toEpoch) =>
            XpCurve.RepriceLedger(level, xpIntoLevel, fromEpoch, toEpoch);

        /// <summary>
        /// Gets the XP multiplier for session rewards based on player level.
        /// Higher level players earn more XP from sessions to compensate for increased requirements.
        /// </summary>
        public double GetSessionXPMultiplier(int level)
        {
            if (level < 30) return 1.0;
            if (level < 80) return 1.0 + ((level - 30) * 0.01);   // 1.0x → 1.5x
            if (level < 125) return 1.5 + ((level - 80) * 0.02);  // 1.5x → 2.4x
            if (level < 150) return 2.4 + ((level - 125) * 0.03); // 2.4x → 3.15x
            return Math.Min(5.0, 3.15 + ((level - 150) * 0.03));   // 3.15x → 5.0x cap
        }

        /// <summary>Lifetime XP of a ledger on this account's curve. See <see cref="XpCurve.GetTotalXP"/>.</summary>
        public double GetTotalXP(int level, double currentXP) => XpCurve.GetTotalXP(level, currentXP, ActiveCurveEpoch);

        /// <summary>Inverse of GetTotalXP - used when loading from cloud sync.</summary>
        public double GetCurrentLevelXP(int level, double totalXP) => XpCurve.GetCurrentLevelXP(level, totalXP, ActiveCurveEpoch);

        public string GetTitle(int level)
        {
            return level switch
            {
                < 5 => Loc.GetF("rank_beginner", App.Mods?.GetRankSubject() ?? "Subject"),
                < 10 => Loc.GetF("rank_training", App.Mods?.GetRankSubject() ?? "Subject"),
                < 20 => Loc.GetF("rank_eager", App.Mods?.GetRankSubject() ?? "Subject"),
                < 30 => Loc.GetF("rank_devoted", App.Mods?.GetRankSubject() ?? "Subject"),
                < 50 => Loc.GetF("rank_advanced", App.Mods?.GetRankSubject() ?? "Subject"),
                _ => Loc.GetF("rank_perfect", App.Mods?.GetRankSubject() ?? "Subject")
            };
        }

        /// <summary>
        /// Check if a level is a milestone worth announcing (10, 25, 50, 100, 125, 150)
        /// </summary>
        private static bool IsLevelMilestone(int level)
        {
            return level == 10 || level == 25 || level == 50 ||
                   level == 100 || level == 125 || level == 150 ||
                   (level > 150 && level % 25 == 0); // Every 25 levels after 150
        }
    }
}
