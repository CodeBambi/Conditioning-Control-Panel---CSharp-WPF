using System;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The tube's "still thinking" bubble while an AI reply is in flight: a phrase from the active
    /// mod's "Thinking" pool (or the defaults) with 0-3 animated dots, re-picked every 4 ticks of
    /// 500ms. Moved verbatim from WPF AvatarTubeWindow.Speech.cs so every head animates the same.
    /// </summary>
    internal static class ThinkingPhrases
    {
        internal static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(500);

        // Default thinking phrases (used when no mod overrides)
        private static readonly string[] Defaults =
        {
            "*POP*",
            "*Poppin bubbles...*",
            "*giggles*",
            "*blink blink*",
            "*~*",
            "*teehee*"
        };

        /// <summary>A random phrase, already stripped of its own trailing dots.</summary>
        internal static string Pick(Random random)
        {
            var modPhrases = CoreMods.GetPhrases("Thinking");
            var phrases = modPhrases != null && modPhrases.Length > 0 ? modPhrases : Defaults;
            return StripTrailingDots(phrases[random.Next(phrases.Length)]);
        }

        /// <summary>The bubble text for tick <paramref name="tick"/> (1-3 dots); tick 0 is the bare phrase.</summary>
        internal static string Frame(string phraseBase, int tick) => phraseBase + new string('.', tick);

        // Matches a run of trailing dots/ellipsis at the end of the phrase, even when
        // tucked just inside closing wrapper chars () [] * _ ~ / whitespace. Removing
        // it (while leaving the wrappers themselves in place) is what stops the static
        // dots in phrases like "(thinking...)" or "[PROCESSING...]" from doubling up
        // with the thinking animation's own dots.
        private static readonly Regex TrailingDotsInsideWrappersRegex =
            new Regex(@"[.…]+(?=[)\]\s*_~]*$)", RegexOptions.Compiled);

        // Strips dots, ellipsis, whitespace, AND markdown emphasis chars (* _ ~) from both ends so
        // the animation's dots aren't duplicated; "(thinking...)" -> "(thinking)" keeps its brackets.
        internal static string StripTrailingDots(string phrase)
        {
            if (string.IsNullOrEmpty(phrase)) return phrase;
            var withoutDots = TrailingDotsInsideWrappersRegex.Replace(phrase, "");
            var trimmed = withoutDots.Trim('.', '…', '*', '_', '~', ' ', '\t');
            // A phrase that was nothing but decoration (e.g. "*~*") keeps the original.
            return string.IsNullOrEmpty(trimmed) ? phrase : trimmed;
        }
    }
}
