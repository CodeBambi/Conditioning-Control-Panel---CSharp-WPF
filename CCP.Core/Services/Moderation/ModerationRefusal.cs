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
    /// Structured moderation refusal carrier used by <see cref="AiReplyResult"/>.
    /// Distinct from the legacy <see cref="ModerationRefusal"/> sentinel class so the
    /// old string-based API and the new typed API can coexist during the WS-C
    /// (P2/C4) migration. <see cref="Category"/> is nullable because the typed
    /// result path crosses the legacy sentinel-string boundary inside
    /// <c>AiService.GetAiResponseAsync</c>; the category is always recorded in
    /// moderation.log at the point of detection, so losing it on the way up here
    /// is non-fatal. The UI only needs <see cref="Source"/> (input vs output) to
    /// pick the right localized bubble.
    /// </summary>
    public sealed record ModerationRefusalInfo(ProhibitedCategory? Category, ModerationSource Source);

    /// <summary>
    /// Discriminated result of an AI reply call. Lets the caller distinguish:
    ///   • a real model reply (<see cref="IsAiGenerated"/> true, <see cref="Refusal"/> null)
    ///   • a canned/fallback string (<see cref="IsAiGenerated"/> false, <see cref="Refusal"/> null)
    ///   • a moderation refusal (<see cref="IsAiGenerated"/> false, <see cref="Refusal"/> non-null;
    ///     <see cref="Text"/> is empty — the UI looks up the localized refusal string by
    ///     <see cref="ModerationRefusalInfo.Source"/>).
    /// Added in P2/C4 so the chat UI only renders the pink AI badge on genuine LLM
    /// replies. Cloud fallbacks, login-required hints, and refusals get no badge.
    /// </summary>
    public sealed record AiReplyResult(string Text, bool IsAiGenerated, ModerationRefusalInfo? Refusal);

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
