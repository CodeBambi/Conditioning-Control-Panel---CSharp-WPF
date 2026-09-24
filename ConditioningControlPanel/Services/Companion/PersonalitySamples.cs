using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// One or two lines in a personality's own voice, for the Companion picker. A picker that
    /// shows how she TALKS lets a new user choose at a glance; a description of the prompt does
    /// not. PURE: no settings, no I/O.
    ///
    /// <para>Ladder: the preset's own <see cref="PersonalityPreset.SampleLines"/> (a mod or a user
    /// preset can carry them), then the stock table below keyed by preset id, then nothing. An
    /// empty result means the caller shows the description instead.</para>
    /// </summary>
    public static class PersonalitySamples
    {
        public const int MaxLines = 2;
        public const int MaxLineLength = 140;

        private static readonly Dictionary<string, string[]> Stock = new(StringComparer.OrdinalIgnoreCase)
        {
            [PersonalityPresets.NeutralDefaultId] = new[]
            {
                "Back already. I kept your seat warm.",
                "Twenty minutes in and you have not blinked once. Impressive, in a worrying way."
            },
            [PersonalityPresets.BambiSpriteId] = new[]
            {
                "Omg hiii, you're back! Did you miss me? Obviously you did.",
                "Shh, thinking is sooo overrated. Just watch the pretty pictures."
            },
            [PersonalityPresets.SlutModeId] = new[]
            {
                "Mm, look who came crawling back.",
                "Eyes on the screen. You know what you're here for."
            },
            [PersonalityPresets.GentleTrainerId] = new[]
            {
                "You're doing so well. One more breath, nice and slow.",
                "No rush. I'll be right here the whole time."
            },
            [PersonalityPresets.StrictDommeId] = new[]
            {
                "Sit up straight. We are not done.",
                "You will finish this session. That was not a question."
            },
            [PersonalityPresets.BimboCoachId] = new[]
            {
                "Lipstick check! Okay, now we can start.",
                "Pink looks so good on you. Like, sooo good."
            },
            [PersonalityPresets.HypnoGuideId] = new[]
            {
                "Let your shoulders drop. Deeper with every word.",
                "Just listen. There is nothing else to do right now."
            },
            [PersonalityPresets.BimboCowId] = new[]
            {
                "Moo. Good training gets a treat.",
                "Stay in the pasture, cutie. Nice and docile."
            },
        };

        /// <summary>Up to <see cref="MaxLines"/> trimmed, non-empty sample lines for
        /// <paramref name="preset"/>, or an empty list when none are known.</summary>
        public static IReadOnlyList<string> For(PersonalityPreset? preset)
        {
            if (preset == null) return Array.Empty<string>();

            var own = Clean(preset.SampleLines);
            if (own != null && own.Count > 0) return own;

            if (!string.IsNullOrEmpty(preset.Id) && Stock.TryGetValue(preset.Id, out var stock))
                return stock.Take(MaxLines).ToList();

            return Array.Empty<string>();
        }

        /// <summary>
        /// Author-supplied lines held to the picker's shape: trimmed, blanks dropped, each capped
        /// at <see cref="MaxLineLength"/>, at most <see cref="MaxLines"/>. Null in, null out, so a
        /// sanitiser can pass "not declared" through unchanged.
        /// </summary>
        public static List<string>? Clean(IEnumerable<string?>? lines)
        {
            if (lines == null) return null;
            var result = new List<string>(MaxLines);
            foreach (var raw in lines)
            {
                if (result.Count >= MaxLines) break;
                var line = raw?.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.Length > MaxLineLength) line = line[..MaxLineLength];
                result.Add(line);
            }
            return result;
        }
    }
}
