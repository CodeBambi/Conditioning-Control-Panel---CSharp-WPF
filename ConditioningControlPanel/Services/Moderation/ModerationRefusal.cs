namespace ConditioningControlPanel.Services.Moderation
{
    /// <summary>
    /// Source of a moderation refusal — used by UI to pick the right localized string.
    /// </summary>
    public enum ModerationSource
    {
        /// <summary>The user's message tripped the guard before it was sent.</summary>
        Input,
        /// <summary>The AI's response tripped the guard before it was shown.</summary>
        Output
    }

    /// <summary>
    /// Sentinel strings used to bubble a moderation refusal up through the existing
    /// <see cref="ConditioningControlPanel.Services.AIService.IAiService"/> string-returning API
    /// without breaking the interface. The chat UI in <c>AvatarTubeWindow</c> detects
    /// these sentinels and renders the localized refusal bubble + POLICY badge.
    ///
    /// Awareness / lockscreen / video paths that receive a sentinel from one of the
    /// nullable-string methods (<c>GetKeywordCommentAsync</c>, etc.) should convert
    /// them back to <c>null</c> at the call site so the caller silently drops the
    /// reaction (no out-of-context refusal bubble).
    /// </summary>
    public static class ModerationRefusal
    {
        public const string InputSentinel = "##CCP_MODERATION_REFUSAL_INPUT##";
        public const string OutputSentinel = "##CCP_MODERATION_REFUSAL_OUTPUT##";

        public static bool IsRefusal(string? text) =>
            text == InputSentinel || text == OutputSentinel;

        public static ModerationSource? GetSource(string? text) =>
            text == InputSentinel ? ModerationSource.Input :
            text == OutputSentinel ? ModerationSource.Output :
            (ModerationSource?)null;
    }
}
