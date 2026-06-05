using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Bark
{
    /// <summary>
    /// Class of a bark. Drives priority banding and conflict handling:
    /// <list type="bullet">
    /// <item><c>Safety</c> — panic/abort lines. Highest priority, never a taunt, never preempted.</item>
    /// <item><c>EasterEgg</c> — rare, fire-once / very-long-cooldown novelty lines.</item>
    /// <item><c>Normal</c> — ordinary reactive barks.</item>
    /// </list>
    /// </summary>
    public enum BarkClass
    {
        Normal = 0,
        EasterEgg = 1,
        Safety = 2
    }

    /// <summary>
    /// Scope of a one-shot (only meaningful when <see cref="BarkRule.Repeatable"/> is false).
    /// PR1 (dry-run) only enforces <c>Session</c> in memory; <c>Tier</c>/<c>Lifetime</c>
    /// persistence (AppSettings.BarkLifetimeFired) arrives with the speak path.
    /// </summary>
    public enum BarkScope
    {
        Session = 0,
        Tier = 1,
        Lifetime = 2
    }

    /// <summary>
    /// A single reactive-dialogue ("bark") rule, loaded as data from a mod-resource
    /// JSON manifest (see <see cref="BarkRuleLoader"/>). Content (the actual lines)
    /// is supplied separately as data; this type only describes shape + matching.
    /// </summary>
    public class BarkRule
    {
        /// <summary>Stable identifier. Used as the key for cooldown, one-fire latch and variant rotation.</summary>
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        /// <summary>Trigger key — must match a key the <see cref="BarkService"/> subscription block raises.</summary>
        [JsonProperty("trigger")]
        public string Trigger { get; set; } = "";

        /// <summary>
        /// Optional condition map (all must pass). Keys are evaluated against the per-fire
        /// <see cref="BarkContext"/> plus a handful of live reads. Supported operator suffixes:
        /// <c>_gte</c> <c>_lte</c> <c>_gt</c> <c>_lt</c> <c>_eq</c>; a bare key is equality.
        /// </summary>
        [JsonProperty("conditions")]
        public Dictionary<string, object>? Conditions { get; set; }

        /// <summary>Higher wins when multiple rules match the same trigger. Safety reserves the top band.</summary>
        [JsonProperty("priority")]
        public int Priority { get; set; }

        /// <summary>Per-bark cooldown in milliseconds (reuses the last-fired-dictionary primitive).</summary>
        [JsonProperty("cooldown_ms")]
        public int CooldownMs { get; set; }

        /// <summary>true = rotate an unused variant, no repeat in-session. false = one-shot per <see cref="Scope"/>.</summary>
        [JsonProperty("repeatable")]
        public bool Repeatable { get; set; } = true;

        [JsonProperty("scope")]
        public string ScopeRaw { get; set; } = "session";

        [JsonProperty("mood")]
        public string Mood { get; set; } = "";

        [JsonProperty("class")]
        public string ClassRaw { get; set; } = "normal";

        /// <summary>Inline line pool. Either this or <see cref="PoolRef"/> supplies variants.</summary>
        [JsonProperty("variant_pool")]
        public List<string>? VariantPool { get; set; }

        /// <summary>Optional reference to an existing CompanionPhraseService category (reuses recorded voicelines).</summary>
        [JsonProperty("pool_ref")]
        public string? PoolRef { get; set; }

        [JsonIgnore]
        public BarkClass Class =>
            ClassRaw?.Trim().ToLowerInvariant() switch
            {
                "safety" => BarkClass.Safety,
                "easter_egg" or "easteregg" or "egg" => BarkClass.EasterEgg,
                _ => BarkClass.Normal
            };

        [JsonIgnore]
        public BarkScope Scope =>
            ScopeRaw?.Trim().ToLowerInvariant() switch
            {
                "lifetime" => BarkScope.Lifetime,
                "tier" => BarkScope.Tier,
                _ => BarkScope.Session
            };

        public bool IsValid() =>
            !string.IsNullOrWhiteSpace(Id) &&
            !string.IsNullOrWhiteSpace(Trigger) &&
            ((VariantPool != null && VariantPool.Count > 0) || !string.IsNullOrWhiteSpace(PoolRef));
    }
}
