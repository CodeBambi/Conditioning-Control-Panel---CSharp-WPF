using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// What the "Customize AI Companion" editor should show as the starting text for each box.
    ///
    /// <para>It used to show <c>CompanionPromptSettings.GetDefaults()</c> flat: the stock Bambi
    /// sprite paragraphs, whatever persona was actually running. Under the Circe mod that reads as
    /// a bug and leaves nobody a way to see the prompt they are forking from (Kathryn,
    /// 2026-09-14: "it had text about bimbo/bambi sprite or something even though I had circe
    /// enabled"). A mod's personalities.json is plain text by the time
    /// <c>PersonalityService</c> has it, so the active persona's own wording is available and is
    /// the honest thing to put in the box.</para>
    ///
    /// <para>Field by field, not whole-object: a mod persona usually sets Personality and leaves
    /// the knowledge base and output rules alone, and a blank box there would be a worse start
    /// than the stock text it replaced.</para>
    /// </summary>
    public static class ActivePersonaPromptText
    {
        /// <summary>
        /// The six editable prompt fields, taken from <paramref name="activePersona"/> where it
        /// has something to say and from <paramref name="stock"/> where it does not. Neither input
        /// is mutated.
        /// </summary>
        public static CompanionPromptSettings Resolve(CompanionPromptSettings? activePersona,
            CompanionPromptSettings stock)
        {
            stock ??= CompanionPromptSettings.GetDefaults();
            if (activePersona == null) return stock;

            var merged = stock.Clone();
            merged.Personality = Pick(activePersona.Personality, stock.Personality);
            merged.ExplicitReaction = Pick(activePersona.ExplicitReaction, stock.ExplicitReaction);
            merged.SlutModePersonality = Pick(activePersona.SlutModePersonality, stock.SlutModePersonality);
            merged.KnowledgeBase = Pick(activePersona.KnowledgeBase, stock.KnowledgeBase);
            merged.ContextReactions = Pick(activePersona.ContextReactions, stock.ContextReactions);
            merged.OutputRules = Pick(activePersona.OutputRules, stock.OutputRules);
            return merged;
        }

        private static string Pick(string? persona, string? fallback)
            => string.IsNullOrWhiteSpace(persona) ? (fallback ?? string.Empty) : persona!;
    }
}
