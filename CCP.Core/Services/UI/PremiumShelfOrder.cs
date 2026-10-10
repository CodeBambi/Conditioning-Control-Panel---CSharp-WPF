using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>The three shelves on the Premium page, in page order.</summary>
    public enum PremiumGroup
    {
        /// <summary>Tier 1, the gold BASIC SUBJECT plate.</summary>
        Basic,
        /// <summary>Tier 2, the cyan PRIME SUBJECT plate.</summary>
        Prime,
        /// <summary>Doors any account opens (the Back Room, Just Drop): on the page so every
        /// roster entry shows once, after the two plans.</summary>
        Free,
    }

    /// <summary>
    /// The Premium page's shelf order (polish 12, owner 2026-10-07: "showing everything we have,
    /// ordered by Basic and Prime"). Pure, so the order is pinned without a window.
    ///
    /// <para>The group comes from the roster's price tag (<c>ExclusiveFeature.Tier</c>), which
    /// agrees with the doors' own gates everywhere but one place: Graded Intake carries no tag
    /// (its weekly pass is open to every account) while its unlimited runs are Prime
    /// (<c>IntakePassService.IsPremium</c> = <c>HasLabAccess</c>, the same bar as
    /// <c>TierGate.RequiresLab</c>). It shelves with Prime.</para>
    ///
    /// <para>Inside each group the doors this account can open come first, then the rest, each
    /// half in roster order.</para>
    /// </summary>
    public static class PremiumShelfOrder
    {
        /// <summary>Untagged roster keys whose full use is sold as Prime.</summary>
        private static readonly HashSet<string> UntaggedPrime = new(StringComparer.OrdinalIgnoreCase)
        {
            "gradedintake",
        };

        /// <summary>The shelf a roster entry stands on.</summary>
        public static PremiumGroup GroupOf(string key, int tier) => tier switch
        {
            1 => PremiumGroup.Basic,
            2 => PremiumGroup.Prime,
            _ => UntaggedPrime.Contains(key ?? string.Empty) ? PremiumGroup.Prime : PremiumGroup.Free,
        };

        /// <summary>The roster as shelves: Basic, Prime, Free (empty ones left out); open doors
        /// first inside each, roster order kept within each half.</summary>
        public static IReadOnlyList<(PremiumGroup Group, IReadOnlyList<T> Items)> Arrange<T>(
            IEnumerable<T> roster, Func<T, string> key, Func<T, int> tier, Func<T, bool> open)
        {
            var list = roster.ToList();
            var result = new List<(PremiumGroup, IReadOnlyList<T>)>();
            foreach (PremiumGroup g in Enum.GetValues(typeof(PremiumGroup)))
            {
                var items = list.Where(f => GroupOf(key(f), tier(f)) == g).ToList();
                if (items.Count == 0) continue;
                var opened = items.Where(open).ToList();
                result.Add((g, opened.Concat(items.Where(f => !open(f))).ToList()));
            }
            return result;
        }
    }
}
