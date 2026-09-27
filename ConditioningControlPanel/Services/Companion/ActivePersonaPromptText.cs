using System;
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

        /// <summary>
        /// What one box should PERSIST into <c>AppSettings.CompanionPrompt</c>.
        ///
        /// <para>The editor has always kept a "blank means follow whatever the default is"
        /// contract: <c>LoadCurrentSettings</c> seeds a blank field from the default and a blank
        /// one is read as "not overridden". Saving every box verbatim was harmless while they were
        /// seeded from the STOCK prompt, because the stock prompt does not move. It is not
        /// harmless now they are seeded from the running persona: open-look-Save under a themed
        /// mod would freeze that mod's paragraph into the user's own settings, and from then on a
        /// mod update, another preset or switching back to CCP Default would never show through
        /// again - and ticking "use custom prompt" later would run one mod's text under whatever
        /// mod is active.</para>
        ///
        /// <para>So a box the user did not touch persists as blank. Clearing a box lands in the
        /// same place, which is the honest reading of an empty field: follow the default.</para>
        /// </summary>
        public static string PersistedField(string? boxText, string? shownDefault)
        {
            var text = boxText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return string.Equals(text, shownDefault ?? string.Empty, StringComparison.Ordinal)
                ? string.Empty
                : text;
        }

        private static string Pick(string? persona, string? fallback)
            => string.IsNullOrWhiteSpace(persona) ? (fallback ?? string.Empty) : persona!;
    }
}
