using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Bark
{
    /// <summary>
    /// Loaded, validated set of bark rules indexed by trigger key for O(1) lookup on
    /// each event. Built by <see cref="BarkRuleLoader"/>; treated as immutable once built.
    /// </summary>
    public class BarkRuleSet
    {
        private readonly Dictionary<string, List<BarkRule>> _byTrigger =
            new(StringComparer.OrdinalIgnoreCase);

        public int Count { get; }

        public BarkRuleSet(IEnumerable<BarkRule> rules)
        {
            foreach (var rule in rules)
            {
                if (rule == null || !rule.IsValid()) continue;
                if (!_byTrigger.TryGetValue(rule.Trigger, out var list))
                {
                    list = new List<BarkRule>();
                    _byTrigger[rule.Trigger] = list;
                }
                list.Add(rule);
                Count++;
            }

            // Highest priority first so the matcher can take the first gate-passing rule.
            foreach (var list in _byTrigger.Values)
                list.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        /// <summary>Rules registered for a trigger key, priority-descending. Empty if none.</summary>
        public IReadOnlyList<BarkRule> ForTrigger(string trigger) =>
            _byTrigger.TryGetValue(trigger, out var list)
                ? list
                : (IReadOnlyList<BarkRule>)Array.Empty<BarkRule>();

        public IEnumerable<string> Triggers => _byTrigger.Keys;

        public static BarkRuleSet Empty => new(Enumerable.Empty<BarkRule>());
    }
}
