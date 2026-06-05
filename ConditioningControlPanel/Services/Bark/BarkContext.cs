using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConditioningControlPanel.Services.Bark
{
    /// <summary>
    /// Per-fire payload handed to the matcher. Subscription handlers stamp the trigger
    /// key plus any event-derived values (e.g. level, repeats, fail-count); the matcher
    /// reads those — together with a few live App reads injected by the service — when
    /// evaluating a rule's conditions.
    /// </summary>
    public class BarkContext
    {
        public string Trigger { get; }

        /// <summary>Flat value bag. Numbers stored as double, flags as bool, labels as string.</summary>
        public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public BarkContext(string trigger)
        {
            Trigger = trigger ?? "";
        }

        public BarkContext Set(string key, object value)
        {
            if (!string.IsNullOrEmpty(key)) Values[key] = value;
            return this;
        }

        public bool TryGetNumber(string key, out double value)
        {
            value = 0;
            if (!Values.TryGetValue(key, out var raw) || raw == null) return false;
            switch (raw)
            {
                case double d: value = d; return true;
                case int i: value = i; return true;
                case long l: value = l; return true;
                case float f: value = f; return true;
                case bool b: value = b ? 1 : 0; return true;
                case string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p):
                    value = p; return true;
                default: return false;
            }
        }

        public bool TryGetBool(string key, out bool value)
        {
            value = false;
            if (!Values.TryGetValue(key, out var raw) || raw == null) return false;
            switch (raw)
            {
                case bool b: value = b; return true;
                case int i: value = i != 0; return true;
                case double d: value = Math.Abs(d) > double.Epsilon; return true;
                case string s when bool.TryParse(s, out var p): value = p; return true;
                default: return false;
            }
        }

        public string? GetString(string key) =>
            Values.TryGetValue(key, out var raw) ? raw?.ToString() : null;
    }
}
