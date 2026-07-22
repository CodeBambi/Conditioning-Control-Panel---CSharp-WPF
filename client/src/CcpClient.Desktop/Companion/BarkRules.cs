using System.Text.Json;
using System.Text.RegularExpressions;

namespace CcpClient.Desktop.Companion;

/// <summary>Bark rule class (WPF BarkRule.cs:15-20). Safety = panic/abort lines: highest priority, never gated, never preempted.</summary>
public enum BarkClass
{
    Normal,
    EasterEgg,
    Safety,
}

/// <summary>One-shot latch scope (WPF BarkRule.cs:28-32) — meaningful only when the rule is non-repeatable.</summary>
public enum BarkScope
{
    Session,
    Tier,
    Lifetime,
}

/// <summary>One bark line: text + optional audio filename (WPF BarkVariant.cs:19-27). The payload unit is never torn.</summary>
public sealed record BarkVariant(string Text, string? Audio)
{
    /// <summary>Non-whitespace text (WPF HasText).</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// One bark rule (WPF BarkRule.cs:42-118, schema field names verbatim: id, trigger,
/// conditions, priority, cooldown_ms, repeatable, scope, mood, class, variant_pool,
/// pool_ref, chance). Defaults WPF-verbatim: Repeatable true, Chance 1.0.
/// </summary>
public sealed record BarkRule
{
    public required string Id { get; init; }

    public required string Trigger { get; init; }

    /// <summary>Condition key → expected value; operator by key suffix (_gte/_lte/_gt/_lt/_eq, bare = eq).</summary>
    public IReadOnlyDictionary<string, object?> Conditions { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public int Priority { get; init; }

    public int CooldownMs { get; init; }

    public bool Repeatable { get; init; } = true;

    public BarkScope Scope { get; init; } = BarkScope.Session;

    /// <summary>Free-form mood string passed through to the emotion surface untouched (manifest-driven, never personality-driven).</summary>
    public string? Mood { get; init; }

    public BarkClass Class { get; init; } = BarkClass.Normal;

    public IReadOnlyList<BarkVariant> VariantPool { get; init; } = [];

    /// <summary>Reference to a phrase-category pool. The phrase-category service is unported
    /// (named limit): pool_ref rules load but resolve to an EMPTY pool → the "empty-pool"
    /// gate — logged at load, never invented.</summary>
    public string? PoolRef { get; init; }

    /// <summary>Probability [0,1] the rule fires once conditions + gate pass; rolled LAST in the gate (WPF :1386-1388).</summary>
    public double Chance { get; init; } = 1.0;

    /// <summary>WPF IsValid (BarkRule.cs:111-114): id + trigger + (variant_pool OR pool_ref).</summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Trigger)
        && (VariantPool.Count > 0 || !string.IsNullOrWhiteSpace(PoolRef));
}

/// <summary>
/// Tolerant manifest loader (WPF BarkRuleLoader.cs:117-149 parity): a bad rule is logged
/// and skipped, never thrown; the set is partial, never lost. The mod-override field-level
/// merge (JObject.Merge, :62-66) lands with the mods row — named limit.
/// </summary>
public static class BarkRuleLoader
{
    /// <summary>Parse a manifest JSON array. Never throws; invalid entries are logged + skipped.</summary>
    public static IReadOnlyList<BarkRule> Parse(string json, Action<string> log)
    {
        var rules = new List<BarkRule>();
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            log($"bark: manifest unparseable ({ex.GetType().Name}) — empty rule set (typed, never thrown)");
            return rules;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            log($"bark: manifest root is {root.ValueKind}, not an array — empty rule set");
            return rules;
        }

        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                log($"bark: skipping non-object manifest entry ({element.ValueKind})");
                continue;
            }

            var rule = ParseRule(element, log);
            if (rule is null)
            {
                continue;
            }

            if (!rule.IsValid)
            {
                log($"bark: skipping invalid rule '{rule.Id}' (needs id + trigger + variant_pool/pool_ref)");
                continue;
            }

            if (rule.PoolRef is { Length: > 0 } && rule.VariantPool.Count == 0)
            {
                log($"bark: rule '{rule.Id}' uses pool_ref — resolves empty until the phrase-category row (named limit)");
            }

            rules.Add(rule);
        }

        log($"bark: manifest loaded ({rules.Count} rule(s))");
        return rules;
    }

    private static BarkRule? ParseRule(JsonElement e, Action<string> log)
    {
        try
        {
            var conditions = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (e.TryGetProperty("conditions", out var conds) && conds.ValueKind == JsonValueKind.Object)
            {
                foreach (var c in conds.EnumerateObject())
                {
                    conditions[c.Name] = ToValue(c.Value);
                }
            }

            var pool = new List<BarkVariant>();
            if (e.TryGetProperty("variant_pool", out var poolEl) && poolEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var v in poolEl.EnumerateArray())
                {
                    var variant = ParseVariant(v);
                    if (variant.HasText)
                    {
                        pool.Add(variant);
                    }
                }
            }

            return new BarkRule
            {
                Id = GetStr(e, "id") ?? "",
                Trigger = GetStr(e, "trigger") ?? "",
                Conditions = conditions,
                Priority = GetInt(e, "priority") ?? 0,
                CooldownMs = GetInt(e, "cooldown_ms") ?? 0,
                Repeatable = GetBool(e, "repeatable") ?? true,
                Scope = GetStr(e, "scope") switch
                {
                    "lifetime" => BarkScope.Lifetime,
                    "tier" => BarkScope.Tier,
                    _ => BarkScope.Session,
                },
                Mood = GetStr(e, "mood"),
                Class = GetStr(e, "class") switch
                {
                    "safety" => BarkClass.Safety,
                    "easter_egg" or "easteregg" or "egg" => BarkClass.EasterEgg,
                    _ => BarkClass.Normal,
                },
                VariantPool = pool,
                PoolRef = GetStr(e, "pool_ref"),
                Chance = GetDouble(e, "chance") ?? 1.0,
            };
        }
        catch (Exception ex)
        {
            log($"bark: skipping malformed rule ({ex.GetType().Name})");
            return null;
        }
    }

    /// <summary>Bare string or {text|display, audio|file} (WPF BarkVariantJsonConverter, BarkVariant.cs:31-54).</summary>
    private static BarkVariant ParseVariant(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => new BarkVariant(v.GetString() ?? "", null),
        JsonValueKind.Object => new BarkVariant(
            GetStr(v, "text") ?? GetStr(v, "display") ?? "",
            NullIfWhite(GetStr(v, "audio") ?? GetStr(v, "file"))),
        _ => new BarkVariant("", null),
    };

    private static string? NullIfWhite(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static object? ToValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static string? GetStr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) ? v : null;

    private static double? GetDouble(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var v) ? v : null;

    private static bool? GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? p.GetBoolean()
            : null;
}

/// <summary>
/// Trigger-indexed rule set (WPF BarkRuleSet.cs:15-16,35-36,40-43): bucketed by trigger
/// case-insensitively, each bucket sorted priority-descending so the matcher takes the
/// FIRST condition-passing rule — and the gate evaluates that winner ONLY (no fallback to
/// lower-priority rules when the winner is gate-blocked, BarkService.cs:790-794 VERIFIED).
/// </summary>
public sealed class BarkRuleSet
{
    private readonly Dictionary<string, List<BarkRule>> _byTrigger = new(StringComparer.OrdinalIgnoreCase);

    public BarkRuleSet(IEnumerable<BarkRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!_byTrigger.TryGetValue(rule.Trigger, out var list))
            {
                list = [];
                _byTrigger[rule.Trigger] = list;
            }

            list.Add(rule);
        }

        foreach (var list in _byTrigger.Values)
        {
            list.Sort(static (a, b) => b.Priority.CompareTo(a.Priority));
        }
    }

    /// <summary>All rules indexed (session facts).</summary>
    public int Count => _byTrigger.Values.Sum(l => l.Count);

    /// <summary>Rules for a trigger, priority-descending (empty when none).</summary>
    public IReadOnlyList<BarkRule> ForTrigger(string trigger) =>
        _byTrigger.TryGetValue(trigger, out var list) ? list : [];
}

/// <summary>Condition evaluation + field coercion (WPF BarkService.cs:1062-1156 verbatim semantics).</summary>
public static class BarkConditions
{
    /// <summary>
    /// Every condition must pass. Operator by key suffix: _gte/_lte/_gt/_lt/_eq (bare = eq).
    /// An UNRESOLVABLE field fails the condition (WPF :1079). Live fields take precedence
    /// over per-fire fills (WPF ResolveField :1099-1137).
    /// </summary>
    public static bool Pass(
        BarkRule rule,
        Func<string, object?> liveField,
        IReadOnlyDictionary<string, object?> fills)
    {
        foreach (var (key, expected) in rule.Conditions)
        {
            var (field, op) = SplitOperator(key);
            var actual = liveField(field) ?? (fills.TryGetValue(field, out var f) ? f : null);
            if (actual is null)
            {
                return false; // unresolvable field → condition fails (WPF :1079)
            }

            if (!Compare(actual, expected, op))
            {
                return false;
            }
        }

        return true;
    }

    private static (string Field, string Op) SplitOperator(string key)
    {
        foreach (var suffix in new[] { "_gte", "_lte", "_gt", "_lt", "_eq" })
        {
            if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return (key[..^suffix.Length], suffix[1..]);
            }
        }

        return (key, "eq");
    }

    private static bool Compare(object actual, object? expected, string op)
    {
        if (op == "eq")
        {
            // WPF :1082-1095: bool equality → numeric (eps 0.0001) → ordinal-ignore-case string.
            if (actual is bool ab && expected is bool eb)
            {
                return ab == eb;
            }

            if (TryDouble(actual, out var an) && TryDouble(expected, out var en))
            {
                return Math.Abs(an - en) < 0.0001;
            }

            return string.Equals(actual.ToString(), expected?.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        // Ordered operators need both sides numeric (WPF :1082-1088).
        if (!TryDouble(actual, out var a) || !TryDouble(expected, out var e))
        {
            return false;
        }

        return op switch
        {
            "gte" => a >= e,
            "lte" => a <= e,
            "gt" => a > e,
            "lt" => a < e,
            _ => false,
        };
    }

    /// <summary>WPF TryDouble (:1138-1156): double/int/long/float, bool → 0/1, invariant numeric strings.</summary>
    public static bool TryDouble(object? value, out double result)
    {
        switch (value)
        {
            case double d: result = d; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case float f: result = f; return true;
            case bool b: result = b ? 1 : 0; return true;
            case string s when double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed):
                result = parsed;
                return true;
            default: result = 0; return false;
        }
    }
}

/// <summary>Text identity + substitution helpers (WPF BarkService.cs:1203-1209,:1638-1656; CompanionPhraseService.cs:84-95).</summary>
public static class BarkText
{
    /// <summary>
    /// Stable line identity: "Bark:{ruleId}:{audio filename stem | "t_"+Slugify(text)}".
    /// Keyed by the audio stem so content reordering never shifts disables (WPF :1203-1209).
    /// </summary>
    public static string LineId(string ruleId, BarkVariant variant) =>
        $"Bark:{ruleId}:{(variant.Audio is { Length: > 0 } audio
            ? System.IO.Path.GetFileNameWithoutExtension(audio)
            : "t_" + Slugify(variant.Text))}";

    /// <summary>WPF Slugify (CompanionPhraseService.cs:84-95): drop {tokens}, lowercase, non-alnum runs → single space, trim.</summary>
    public static string Slugify(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var noTokens = Regex.Replace(text, @"\{[^}]*\}", " ");
        var chars = noTokens.ToLowerInvariant().Select(ch => ch is >= 'a' and <= 'z' or >= '0' and <= '9' ? ch : ' ');
        return Regex.Replace(string.Concat(chars), @"\s+", " ").Trim();
    }

    /// <summary>
    /// WPF ApplySubstitutions (BarkService.cs:1638-1656): {0} → the focused-app live field
    /// (fallback "that"); every fill becomes a {key} token.
    /// </summary>
    public static string Substitute(string text, IReadOnlyDictionary<string, object?> fills, object? focusedApp)
    {
        var result = text.Replace("{0}", focusedApp?.ToString() ?? "that", StringComparison.Ordinal);
        foreach (var (key, value) in fills)
        {
            if (value is not null)
            {
                result = result.Replace("{" + key + "}", value.ToString(), StringComparison.Ordinal);
            }
        }

        return result;
    }
}
