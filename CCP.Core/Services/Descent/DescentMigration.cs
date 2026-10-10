using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>The server's offer (WPF Services/Descent/DescentMigration.cs). Figures are the SERVER's.</summary>
    public sealed class DescentMigrationOffer
    {
        public double TotalXpEarned { get; init; }
        public int DevotionDays { get; init; }
        public double RestoreBasisXp { get; init; }
    }

    public readonly record struct DescentRelevelResult(int Level, double XpIntoLevel, double LifetimeXp);

    /// <summary>
    /// THE MIGRATION IN ITS RETIRED FORM (owner, 2026-10-06; WPF main 9ffa90127). There is no ceremony,
    /// no window and no question: a build that sends <c>descent_auto: true</c> is offered the migration,
    /// takes "restore" silently and submits it on the next sync. This file is the pure half of WPF
    /// DescentMigration + DescentMigrationService.ApplyOfferNow / ApplyChoice; neither saves nor syncs.
    /// The Cycle door is not offered by this head (it was the ceremony's other answer).
    /// </summary>
    public static class DescentMigration
    {
        /// <summary>Restore: the level the server's basis buys on curve v2 (WPF Resolve).</summary>
        public static DescentRelevelResult Resolve(string choice, DescentMigrationOffer offer)
        {
            double lifetime = offer.TotalXpEarned;
            if (double.IsNaN(lifetime) || lifetime < 0) lifetime = 0;
            if (choice == DescentMigrationChoices.Cycle) return new DescentRelevelResult(1, 0, lifetime);

            var basis = offer.RestoreBasisXp;
            if (double.IsNaN(basis) || basis <= 0) basis = lifetime;
            var (level, into) = XpCurve.DeriveLevelFromLifetimeXp(basis, DescentEpochs.AccountDescent);
            return new DescentRelevelResult(level, into, basis);
        }

        /// <summary>The offer in a <c>/v2/user/sync</c> response (<c>descent_migration.required: true</c>), or null.</summary>
        public static DescentMigrationOffer? ReadOffer(JObject response)
        {
            if (response["descent_migration"] is not JObject block) return null;
            if (block["required"]?.Type != JTokenType.Boolean || !block.Value<bool>("required")) return null;
            return new DescentMigrationOffer
            {
                TotalXpEarned = Number(block["total_xp_earned"]),
                DevotionDays = (int)Number(block["devotion_days"]),
                RestoreBasisXp = Number(block["restore_basis_xp"]),
            };
        }

        /// <summary>True when the response carries the ack (<c>completed: true</c>).</summary>
        public static bool IsAck(JObject response) =>
            response["descent_migration"] is JObject block
            && block["completed"]?.Type == JTokenType.Boolean && block.Value<bool>("completed");

        private static double Number(JToken? t) =>
            t is { Type: JTokenType.Integer or JTokenType.Float } ? t.Value<double>() : 0;

        /// <summary>
        /// WPF DescentMigrationService.SpiralWithheldFor: the withhold, as arithmetic. OUTSTANDING is an
        /// offer in hand, a window on screen (never, now) or the persisted marker from an older build
        /// (<see cref="Models.AppSettings.DescentMigrationOffered"/>). ANSWERED is the server ack or a
        /// valid pending choice. ANSWERED WINS. Null settings read as not withheld.
        /// </summary>
        public static bool SpiralWithheldFor(Models.AppSettings? settings, bool offerInHand, bool ceremonyOpen)
        {
            if (settings is null) return false;

            if (settings.DescentMigrationCompleted) return false;
            if (DescentMigrationChoices.IsValid(settings.PendingDescentMigrationChoice)) return false;

            return offerInHand || ceremonyOpen || settings.DescentMigrationOffered;
        }

        /// <summary>
        /// WPF ApplyOfferNow + ApplyChoice(Restore): rewrite the ledger on curve v2 and arm the submit.
        /// Refused for a migrated account or one whose choice is already waiting for its ack. The pending
        /// choice is written LAST, so a crash before it leaves a client the next offer re-runs to the same
        /// answer. True when it wrote (the caller saves).
        /// </summary>
        /// <param name="stage">The account's stage ladder if a descent block is in hand, for the drip queue.</param>
        public static bool ApplyRestore(Models.AppSettings settings, DescentMigrationOffer offer, DescentStage? stage, DateTime nowUtc)
        {
            if (settings is null || offer is null) return false;
            if (settings.DescentMigrationCompleted) return false;
            if (DescentMigrationChoices.IsValid(settings.PendingDescentMigrationChoice)) return false;

            var result = Resolve(DescentMigrationChoices.Restore, offer);

            settings.DescentPreMigrationLevel = settings.PlayerLevel;
            settings.DescentPreMigrationLifetimeXp = result.LifetimeXp;

            settings.DescentEpoch = DescentEpochs.AccountDescent;
            settings.PlayerLevel = result.Level;
            settings.PlayerXP = result.XpIntoLevel;
            if (settings.PlayerLevel > settings.HighestLevelEver) settings.HighestLevelEver = settings.PlayerLevel;

            settings.DescentCycleXpBonus = DescentCycleXp.CycleXpBonus;   // migrated = bonus
            settings.DescentPendingStageCeremonies = BuildStageDripQueue(offer, stage);
            settings.DescentVeteranArchive = true;
            settings.DescentAnchorUtc = nowUtc;
            settings.DescentLastStageDripDate = null;

            // The watermark is in pre-migration terms: a sanctioned rewrite voids it.
            ProfileAdopt.ClearXpWatermark(settings, "descent migration (auto restore)");
            settings.DescentMigrationOffered = false;
            settings.PendingDescentMigrationChoice = DescentMigrationChoices.Restore;

            Log.Information("[Descent] Migration offered - restored silently on curve v2: Level {Old} -> {New} (basis {Xp} XP). Awaiting server ack.",
                settings.DescentPreMigrationLevel, result.Level, (int)result.LifetimeXp);
            return true;
        }

        /// <summary>WPF BuildStageDripQueue: the stages already reached, one per login day; none without a ladder.</summary>
        internal static List<int> BuildStageDripQueue(DescentMigrationOffer offer, DescentStage? stage)
        {
            int reached;
            if (stage?.Thresholds is { Count: > 0 } thresholds) reached = thresholds.Count(t => offer.DevotionDays >= t);
            else if (stage != null && stage.N > 0) reached = stage.N;
            else return new List<int>();
            return Enumerable.Range(1, Math.Max(0, reached)).ToList();
        }
    }
}
