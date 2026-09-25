using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>What a server <c>effective_tier</c> reading means for this install.</summary>
    public enum EntitlementVerdict
    {
        /// <summary>No usable reading (older server, garbage, out of range). Touch nothing.</summary>
        NoSignal,

        /// <summary>The server agrees with what this install already holds.</summary>
        Same,

        /// <summary>The server says more than we hold: unlock now.</summary>
        Rise,

        /// <summary>
        /// The server says less. Recorded, never applied: the 14-day grace stamps run out on
        /// their own (owner-approved default), and a live read must never yank a feature out
        /// from under someone mid-session.
        /// </summary>
        Lower
    }

    /// <summary>
    /// Instant unlock for a subscription bought outside the app (PayPal / Stripe on the site).
    ///
    /// <para>The server folds every provider into one <c>effective_tier</c> (0..2, whitelist is 2)
    /// on the heartbeat and on <c>/v2/user/profile</c>. This class is the pure half: parse it,
    /// compare it with the tier this install last stored, and stamp the same 14-day grace windows
    /// the sign-in path writes. The live half is <see cref="EntitlementTierSync"/>.</para>
    ///
    /// <para>Compared against the STORED <see cref="AppSettings.PatreonTier"/> and never against
    /// live access, on purpose: Patreon's server tier is a frozen snapshot, so a lapsed patron
    /// still reads 1 there. Comparing with live access would re-stamp that snapshot every
    /// heartbeat and hand out permanent premium.</para>
    /// </summary>
    public static class EntitlementTierRule
    {
        public const int MaxTier = 2;
        public const int GraceDays = 14;

        /// <summary>An integer 0..2, or null for anything else (absent, string, float, out of range).</summary>
        public static int? ParseTier(JToken? token)
        {
            if (token is null || token.Type != JTokenType.Integer) return null;
            long v;
            try { v = token.Value<long>(); } catch { return null; }
            return v >= 0 && v <= MaxTier ? (int)v : null;
        }

        /// <summary>Top-level <c>effective_tier</c> off a heartbeat body, or null. Never throws.</summary>
        public static int? ParseHeartbeatTier(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try { return JObject.Parse(body) is JObject root ? ParseTier(root["effective_tier"]) : null; }
            catch { return null; }
        }

        public static EntitlementVerdict Decide(int storedTier, int? serverTier)
        {
            if (serverTier is null || serverTier < 0 || serverTier > MaxTier) return EntitlementVerdict.NoSignal;
            var local = Math.Clamp(storedTier, 0, MaxTier);
            if (serverTier.Value > local) return EntitlementVerdict.Rise;
            return serverTier.Value < local ? EntitlementVerdict.Lower : EntitlementVerdict.Same;
        }

        /// <summary>
        /// The 14-day grace stamps for a server-confirmed tier. Premium at tier 1+, the Lab stamp
        /// only at tier 2. Never shortens an existing longer window. The ONE copy of this rule:
        /// <see cref="V2AuthService.ApplyUserDataToSettings"/> calls it too.
        /// </summary>
        public static void ExtendGrace(AppSettings settings, int tier, DateTime nowUtc)
        {
            if (settings == null || tier < 1) return;
            var until = nowUtc.AddDays(GraceDays);
            if (settings.PatreonPremiumValidUntil == null || settings.PatreonPremiumValidUntil < until)
                settings.PatreonPremiumValidUntil = until;
            if (tier >= 2 && (settings.PatreonLabValidUntil == null || settings.PatreonLabValidUntil < until))
                settings.PatreonLabValidUntil = until;
        }
    }

    /// <summary>
    /// Which "premium unlocked" card an install still owes. One seen-flag per tier, so a
    /// Basic -> Prime upgrade celebrates again. The pre-tier key (<see cref="LegacyKey"/>) still
    /// counts on the launch re-check, so an existing install is not re-celebrated for the tier it
    /// already had; a LIVE rise ignores it, because a rise is a new purchase.
    /// </summary>
    public static class TierCelebration
    {
        public const string LegacyKey = "premium-celebration";
        public const string KeyT1 = "premium-celebration-t1";
        public const string KeyT2 = "premium-celebration-t2";

        public static string? KeyFor(int tier) => tier >= 2 ? KeyT2 : tier == 1 ? KeyT1 : null;

        public static bool IsOwed(ICollection<string>? seen, int tier, bool onRise)
        {
            var key = KeyFor(tier);
            if (key == null) return false;
            if (seen == null) return true;
            if (seen.Contains(key)) return false;
            return onRise || !seen.Contains(LegacyKey);
        }
    }
}
