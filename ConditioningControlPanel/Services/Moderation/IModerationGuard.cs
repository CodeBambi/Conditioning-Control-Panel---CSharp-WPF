namespace ConditioningControlPanel.Services.Moderation
{
    /// <summary>
    /// Result of a moderation check. <see cref="Allow"/> is false ONLY for hard-blocking
    /// categories. <see cref="ProhibitedCategory.ProfessionalAdvice"/> returns Allow=true
    /// with the category set so callers can still log it.
    /// </summary>
    public sealed record ModerationResult(bool Allow, ProhibitedCategory? Category, string? Note)
    {
        public static ModerationResult Pass() => new(true, null, null);

        public static ModerationResult Block(ProhibitedCategory cat, string note) =>
            new(false, cat, note);

        /// <summary>
        /// Soft hit: log-worthy but not blocking. Used for
        /// <see cref="ProhibitedCategory.ProfessionalAdvice"/>.
        /// </summary>
        public static ModerationResult SoftHit(ProhibitedCategory cat, string note) =>
            new(true, cat, note);
    }

    /// <summary>
    /// Substantive content moderation that runs in C# code outside the LLM prompt.
    /// Trust boundary: user-authored prompt sections cannot bypass this — the wordlist
    /// is hardcoded in <see cref="ModerationGuard"/> and applies to every input that
    /// goes to an LLM and every output that comes back.
    ///
    /// See <c>AI_AUDIT.md</c> sections 7, 11, and 13 for the CCBill rationale.
    /// </summary>
    public interface IModerationGuard
    {
        /// <summary>
        /// Scans user-authored content destined for an LLM. Returns a blocking result
        /// if a prohibited category is matched, a soft hit for
        /// <see cref="ProhibitedCategory.ProfessionalAdvice"/>, or a pass otherwise.
        /// </summary>
        ModerationResult CheckInput(string text);

        /// <summary>
        /// Scans LLM-generated content before it is shown to the user. Same semantics
        /// as <see cref="CheckInput"/>.
        /// </summary>
        ModerationResult CheckOutput(string text);
    }
}
