using System;

namespace ConditioningControlPanel.Services.Companion.Brain
{
    /// <summary>
    /// Who the user is talking to, before and after a mod switch, and what that costs the chat
    /// thread. Pure so both halves are unit-tested without the mod stack.
    ///
    /// <para>Background: a mod switch has always stamped
    /// <c>AppSettings.PersonaVoiceFenceUtc</c>, which fences her own pre-switch replies off the
    /// wire because they out-few-shot any changed persona paragraph. That fence deliberately KEEPS
    /// the user's turns - for a preset change it is the same companion in a different mood, and
    /// dropping the user's own words would throw away the thread for nothing.</para>
    ///
    /// <para>A mod switch is not that. When the companion's NAME changes with the mod, the person
    /// on the other side of the box is gone, and a window that still opens with "hi Circe, ..."
    /// reads to a small model as one unbroken conversation with Circe - so it answers as Circe with
    /// a CCP Default prompt in front of it (Kathryn, 2026-09-14: "I switched from circe back to CCP
    /// Default, but the companion in the 'talk to her' box is still referring to itself as Circe").
    /// That case needs the whole pre-switch conversation off the wire, not half of it.</para>
    /// </summary>
    public static class PersonaSwitchRules
    {
        /// <summary>
        /// True when this mod switch changes who the companion IS, rather than only what they are
        /// wearing. The name is the only identity the model ever sees, so it is the whole test:
        /// two skins that both speak as EMI keep their thread, Bambi to Circe does not.
        /// </summary>
        public static bool ChangesPersona(string? oldModId, string? newModId,
            string? oldCompanionName, string? newCompanionName)
        {
            if (string.Equals(oldModId, newModId, StringComparison.OrdinalIgnoreCase)) return false;

            var before = (oldCompanionName ?? string.Empty).Trim();
            var after = (newCompanionName ?? string.Empty).Trim();
            // An unknown name on either side is not evidence of a change. Cutting the thread on a
            // mod stack that simply had not answered yet would be worse than leaving it.
            if (before.Length == 0 || after.Length == 0) return false;

            return !string.Equals(before, after, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether one turn may still go on the wire after a persona change at
        /// <paramref name="fence"/>. Everything recorded before the switch goes: her replies, the
        /// user's lines addressed to the old companion, and the ambient events they answered.
        ///
        /// <para>Nothing is deleted - the stored session, the visible bubbles and the memory panel
        /// keep the lot. This decides the prompt window only.</para>
        /// </summary>
        public static bool SurvivesIdentityFence(DateTime turnUtc, DateTime? fence)
            => fence == null || turnUtc >= fence.Value;
    }
}
