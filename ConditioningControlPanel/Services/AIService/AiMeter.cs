namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// One Serilog line per LLM request, greppable by the literal prefix <c>[AI-METER]</c>.
    /// Log-only: nothing here changes behaviour, and the emitting call sites must never pass
    /// message content, window/tab titles or user text — counts and enums only.
    ///
    /// Token figures are deliberately approximate (chars / 4). They exist to size the pipeline
    /// (how many requests, how much prompt, how long), not to reconcile a provider's bill.
    ///
    /// Lives in one place so the four emitters (cloud, local, openai_compat, quiz) can't drift
    /// on field names — a meter you have to grep four ways is not a meter.
    /// </summary>
    internal static class AiMeter
    {
        public const string ProviderCloud = "cloud";
        public const string ProviderLocal = "local";
        public const string ProviderOpenAiCompatible = "openai_compat";
        public const string ProviderQuiz = "quiz";

        public const string PurposeChat = "chat";
        public const string PurposeAwareness = "awareness";
        public const string PurposeStillOn = "still_on";
        public const string PurposeKeyword = "keyword";
        public const string PurposeLockScreen = "lockscreen";
        public const string PurposeVideoDone = "video_done";
        public const string PurposeQuiz = "quiz";

        /// <summary>A usable reply came back.</summary>
        public const string OutcomeOk = "ok";
        /// <summary>ModerationGuard blocked the input; no request was sent.</summary>
        public const string OutcomeRefusedInput = "refused_input";
        /// <summary>ModerationGuard blocked the model output; the turn was discarded.</summary>
        public const string OutcomeRefusedOutput = "refused_output";
        /// <summary>Transport / HTTP / parse failure.</summary>
        public const string OutcomeError = "error";
        /// <summary>The request succeeded but produced no usable text.</summary>
        public const string OutcomeEmpty = "empty";

        private static int ApproxTokens(int chars) => chars <= 0 ? 0 : chars / 4;

        public static void Record(string provider, string purpose, int inputChars, int outputChars,
            long elapsedMs, string outcome)
        {
            App.Logger?.Information(
                "[AI-METER] provider={Provider} purpose={Purpose} in_tok~{InTokens} out_tok~{OutTokens} ms={ElapsedMs} outcome={Outcome}",
                provider, purpose, ApproxTokens(inputChars), ApproxTokens(outputChars), elapsedMs, outcome);
        }
    }
}
