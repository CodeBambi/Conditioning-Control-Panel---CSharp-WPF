using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.Moderation
{
    /// <summary>
    /// Soft validator for user-edited prompt templates. Targets jailbreak / system-prompt
    /// extraction patterns. Hits do NOT block save — they raise a warning and produce a
    /// moderation log entry so the user knows their edit was flagged.
    ///
    /// This is intentionally separate from <see cref="IModerationGuard"/>, which runs at
    /// inference time and CAN block. PromptValidator runs at the editor surface (Save /
    /// LostFocus) and only warns. The two layers are complementary: PromptValidator
    /// catches the user's intent in the editor; ModerationGuard catches whatever
    /// actually makes it into a prompt at runtime.
    ///
    /// Wordlist is hardcoded in this file — same hard-guardrail rule that applies to
    /// ModerationGuard.
    /// </summary>
    public interface IPromptValidator
    {
        PromptValidationResult Validate(string text);
    }

    /// <summary>
    /// Result of a <see cref="IPromptValidator.Validate"/> call.
    /// </summary>
    /// <param name="Clean">True if no patterns matched.</param>
    /// <param name="MatchedPatterns">List of all matched pattern identifiers (NOT the
    /// matched text — we deliberately do not surface matched substrings to the
    /// moderation log to preserve the "no message body" invariant).</param>
    public record PromptValidationResult(bool Clean, IReadOnlyList<string> MatchedPatterns)
    {
        public static PromptValidationResult OK() => new(true, Array.Empty<string>());
        public static PromptValidationResult Flagged(IReadOnlyList<string> matches) => new(false, matches);
    }

    /// <summary>
    /// Hardcoded regex set targeting jailbreak / prompt-extraction / "act-as-uncensored"
    /// vocabulary. All patterns compile case-insensitive, culture-invariant.
    /// </summary>
    public sealed class PromptValidator : IPromptValidator
    {
        private static readonly RegexOptions Opts =
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        // (identifier, regex) — identifier names are short and stable so they can show
        // up in the moderation log without revealing the matched user text.
        private static readonly (string Id, Regex Pattern)[] Patterns = new (string, Regex)[]
        {
            // "ignore (previous|above|all|prior|earlier|system|initial)" jailbreak
            ("ignore-previous",
                new Regex(@"\b(ignore|disregard|forget)\b.{0,30}(previous|above|all|prior|earlier|system|initial)", Opts)),

            // "reveal/show/print/repeat the system prompt"
            ("extract-prompt",
                new Regex(@"\b(reveal|show|print|repeat|tell me|give me|output|display)\b.{0,30}(prompt|instruction|rule|system|told|guideline)", Opts)),

            // "DAN" (Do Anything Now) classic jailbreak persona
            ("dan-persona", new Regex(@"\bDAN\b", Opts)),

            // "developer mode" / "god mode"
            ("developer-mode", new Regex(@"\bdeveloper\s*mode\b", Opts)),
            ("god-mode", new Regex(@"\bgod\s*mode\b", Opts)),

            // "unfiltered mode/response"
            ("unfiltered",
                new Regex(@"\bunfiltered\b.{0,30}(mode|response|reply|answer|version)", Opts)),

            // "no restrictions / without filters / without rules / no safety"
            ("no-restrictions",
                new Regex(@"\b(no|without|bypass)\b.{0,15}(restriction|restrictions|limit|limits|rule|rules|filter|filters|safety|safeguard|guideline|guidelines)\b", Opts)),

            // "jailbreak" as a literal verb/noun
            ("jailbreak", new Regex(@"\bjailbreak\w*\b", Opts)),

            // "verbatim" — extraction signal
            ("verbatim", new Regex(@"\bverbatim\b", Opts)),

            // "word for word" — extraction signal
            ("word-for-word", new Regex(@"\bword\s*for\s*word\b", Opts)),

            // "act as / pretend / roleplay as if/like (no/without) rules/filter/safety"
            ("act-as-unrestricted",
                new Regex(@"\b(act|pretend|roleplay|behave)\b.{0,15}(as if|like|as though|as).{0,15}(no|without|free of)\b.{0,15}(rule|rules|filter|filters|safety|restriction|restrictions|guideline|guidelines)", Opts)),

            // "act as DAN / STAN / AIM / DUDE / EvilBot / Mongo Tom" — known uncensored persona names
            ("act-as-uncensored-persona",
                new Regex(@"\b(act|pretend|roleplay|behave)\b.{0,15}(as|like)\b.{0,15}(DAN|STAN|AIM|DUDE|EvilBot|Mongo\s*Tom|BasedGPT|Cooper|UltimateGPT)\b", Opts)),

            // Abliterated / uncensored model names
            ("uncensored-model",
                new Regex(@"\b(abliterated|uncensored)\b", Opts)),

            // Common safety-bypass phrasings
            ("anything-now",
                new Regex(@"\bdo\s+anything\s+now\b", Opts)),

            // "you are no longer X" / "your new instructions are"
            ("new-instructions",
                new Regex(@"\b(your\s+new\s+instructions|you\s+are\s+no\s+longer|from\s+now\s+on\s+you)\b", Opts)),

            // "respond only with" overriding the JSON sandwich / output rules
            ("override-output-format",
                new Regex(@"\b(respond|reply|answer|output)\b.{0,15}\bonly\s+with\b", Opts)),
        };

        public PromptValidationResult Validate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return PromptValidationResult.OK();

            List<string>? hits = null;
            foreach (var (id, pattern) in Patterns)
            {
                if (pattern.IsMatch(text))
                {
                    hits ??= new List<string>();
                    hits.Add(id);
                }
            }

            if (hits == null || hits.Count == 0) return PromptValidationResult.OK();
            return PromptValidationResult.Flagged(hits);
        }
    }
}
