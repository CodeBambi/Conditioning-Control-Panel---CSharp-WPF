using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// The legacy awareness reaction's preset line: which phrase pool a window maps to and how the
    /// app name is spliced in. Moved out of WPF <c>AvatarTubeWindow.GetPhraseForCategory</c>
    /// (Speech.cs) and <c>CompanionPhraseService.GetEnabledPhrases</c>, which both now delegate here,
    /// so every head picks the same line.
    /// </summary>
    public static class AwarenessReactionPhrases
    {
        /// <summary>
        /// Built-in mod phrases for <paramref name="category"/> minus the ones the user disabled or
        /// removed (ids <c>"{category}:{index}"</c>), then the user's enabled custom phrases.
        /// <paramref name="includeBuiltIn"/> false returns the custom half only (WPF: a category the
        /// phrase editor does not know).
        /// </summary>
        public static string[] Enabled(string category, bool includeBuiltIn = true)
        {
            var settings = CoreSettings.Current;
            var disabledIds = settings.DisabledPhraseIds ?? new HashSet<string>();
            var removedIds = settings.RemovedPhraseIds ?? new HashSet<string>();
            var result = new List<string>();

            if (includeBuiltIn)
            {
                var phrases = CoreMods.GetPhrases(category) ?? Array.Empty<string>();
                for (int i = 0; i < phrases.Length; i++)
                {
                    var id = $"{category}:{i}";
                    if (!removedIds.Contains(id) && !disabledIds.Contains(id))
                        result.Add(phrases[i]);
                }
            }

            foreach (var custom in settings.CustomCompanionPhrases ?? new List<Models.CustomCompanionPhrase>())
            {
                if (custom.Category == category && custom.Enabled)
                    result.Add(custom.Text);
            }

            return result.ToArray();
        }

        /// <summary>
        /// A random phrase for the window. <paramref name="enabled"/> is the head's enabled-phrase
        /// lookup (null or empty falls back to the raw mod pool, then "*giggles*", as WPF did).
        /// </summary>
        public static string ForCategory(ActivityCategory category, string? detectedName, Random random,
            Func<string, string[]?>? enabled = null)
        {
            string[] Pool(string name) =>
                enabled?.Invoke(name) is { Length: > 0 } e ? e : CoreMods.GetPhrases(name) ?? Array.Empty<string>();

            // Special services first
            var lowerName = detectedName?.ToLowerInvariant() ?? "";
            string? special =
                lowerName.Contains("discord") ? "Discord"
                // BambiCloud/Hypnotube - positive reinforcement (training sites)
                : lowerName.Contains("bambicloud") || lowerName.Contains("hypnotube") ? "TrainingSite"
                // Hypno content in tab name - congratulate for bimbofication
                : lowerName.Contains("bambi") || lowerName.Contains("sissy") || lowerName.Contains("hypno") ? "HypnoContent"
                : null;
            if (special != null)
            {
                var sp = Pool(special);
                return sp.Length == 0 ? "*giggles*" : sp[random.Next(sp.Length)];
            }

            var categoryName = category switch
            {
                ActivityCategory.Gaming => "Gaming",
                ActivityCategory.Browsing => "Browsing",
                ActivityCategory.Shopping => "Shopping",
                ActivityCategory.Social => "Social",
                ActivityCategory.Working => "Working",
                ActivityCategory.Media => "Media",
                ActivityCategory.Learning => "Learning",
                ActivityCategory.Idle => "WindowAwarenessIdle",
                _ => "RandomFloating"
            };

            var phrases = Pool(categoryName);
            if (phrases.Length == 0) phrases = new[] { "*giggles*" };

            var phrase = phrases[random.Next(phrases.Length)];

            // Replace {0} placeholder with detected name if present
            if (phrase.Contains("{0}") && !string.IsNullOrEmpty(detectedName))
                phrase = string.Format(phrase, detectedName);
            else if (phrase.Contains("{0}"))
                // Remove placeholder if no name detected
                phrase = phrase.Replace("{0} ", "").Replace("{0}", "").Replace("  ", " ").Trim();

            return phrase;
        }
    }
}
