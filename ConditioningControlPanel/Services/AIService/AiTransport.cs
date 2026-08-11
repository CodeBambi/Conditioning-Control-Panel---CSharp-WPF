using System;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// One message on the wire to an LLM provider. Deliberately provider-agnostic: the cloud
    /// proxy, Ollama and OpenAI-compatible transports each map this onto their own DTO.
    ///
    /// Train 1 introduces this as the single transport currency so <c>CompanionBrain</c> can
    /// own conversation state once instead of every provider growing its own history list.
    /// (The nested <c>LocalAiService.ChatMessage</c> is the local provider's mutable internal
    /// buffer and is unrelated — it is being retired as the brain takes over history.)
    /// </summary>
    public sealed record ChatMessage(string Role, string Content)
    {
        public const string RoleSystem = "system";
        public const string RoleUser = "user";
        public const string RoleAssistant = "assistant";

        public static ChatMessage System(string content) => new(RoleSystem, content ?? string.Empty);
        public static ChatMessage User(string content) => new(RoleUser, content ?? string.Empty);
        public static ChatMessage Assistant(string content) => new(RoleAssistant, content ?? string.Empty);
    }

    /// <summary>
    /// Why a request is being made. The server maps this to a model tier and a max_tokens
    /// clamp (MASTER-SCOPE §6.1); until that deploy lands the field is simply ignored by the
    /// proxy, which is why every client value is a *request*, never an instruction.
    /// Unknown/absent purpose is treated as <see cref="Chat"/> on both ends.
    /// </summary>
    public enum AiPurpose
    {
        /// <summary>The user typed something. Highest tier, longest history window.</summary>
        Chat,
        /// <summary>An ambient moment (awareness / video done / keyword / lock card). Cheap tier, tiny window.</summary>
        Reaction,
        /// <summary>Utility: fact extraction. Train 4 — no caller in Train 1.</summary>
        Memory,
        /// <summary>Utility: transcript compaction. Train 4 — no caller in Train 1.</summary>
        Summary
    }

    /// <summary>
    /// Per-call transport knobs for <see cref="IAiService.SendAsync"/>.
    ///
    /// <para><b>Moderation contract.</b> <see cref="IAiService.SendAsync"/> implementations run the
    /// full Layer-1 spine themselves — <c>ModerationGuard.CheckInput</c> on the newest user-role
    /// message, <c>CheckOutput</c> on the reply, <c>ModerationLog</c> for the CCBill record — exactly
    /// as the legacy one-shot methods do. Callers must NOT pre-check the same text or the compliance
    /// log and the escalating <c>ModerationCounter</c> would double-count.</para>
    ///
    /// <para><see cref="Interactive"/> is the successor to the old <c>returnRefusalSentinel</c> flag:
    /// true only when the user actually typed the text, which is the sole case that escalates the
    /// user-facing Content Policy Notice and surfaces a refusal bubble. Background reactions leave it
    /// false and are dropped silently (still logged for compliance).</para>
    /// </summary>
    public sealed record AiCallOptions
    {
        /// <summary>Tier hint sent to the server and stamped on the [AI-METER] line.</summary>
        public AiPurpose Purpose { get; init; } = AiPurpose.Chat;

        /// <summary>Requested response cap. Providers clamp to their own hard ceiling.</summary>
        public int MaxTokens { get; init; } = 100;

        public double Temperature { get; init; } = 0.7;

        /// <summary>
        /// True when the user typed this turn. Drives refusal surfacing + Content Policy
        /// Notice escalation. See the moderation contract on this type.
        /// </summary>
        public bool Interactive { get; init; }

        /// <summary>Chat-box defaults: tier A, interactive, 100 tokens out.</summary>
        public static AiCallOptions Chat { get; } =
            new() { Purpose = AiPurpose.Chat, MaxTokens = 100, Temperature = 0.7, Interactive = true };

        /// <summary>
        /// Chat while the effects envelope is on (AllowAiToControlEffects): the JSON
        /// scaffolding plus one effect already costs ~80-110 tokens, so the 100-token prose
        /// cap guillotined the envelope mid-string and raw JSON leaked into the bubble
        /// (beebee, 2026-08-07). 350 fits several effects; providers still clamp to their
        /// own ceilings (Ollama 512, cloud hard-cap - which never sends the envelope anyway).
        /// </summary>
        public static AiCallOptions ChatWithEffects { get; } =
            new() { Purpose = AiPurpose.Chat, MaxTokens = 350, Temperature = 0.7, Interactive = true };

        /// <summary>Ambient defaults: cheapest tier, non-interactive, short reply.</summary>
        public static AiCallOptions Reaction { get; } =
            new() { Purpose = AiPurpose.Reaction, MaxTokens = 60, Temperature = 0.8, Interactive = false };

        /// <summary>Utility defaults (Train 4): cheapest tier, JSON-sized output.</summary>
        public static AiCallOptions Utility { get; } =
            new() { Purpose = AiPurpose.Memory, MaxTokens = 300, Temperature = 0.2, Interactive = false };

        /// <summary>Wire value of <see cref="Purpose"/> — the exact strings in the server contract.</summary>
        public string PurposeWire => Purpose switch
        {
            AiPurpose.Reaction => "reaction",
            AiPurpose.Memory => "memory",
            AiPurpose.Summary => "summary",
            _ => "chat"
        };

        /// <summary>[AI-METER] <c>purpose=</c> value. Same vocabulary as the wire field.</summary>
        public string MeterPurpose => Purpose switch
        {
            AiPurpose.Reaction => AiMeter.PurposeReaction,
            AiPurpose.Memory => AiMeter.PurposeMemory,
            AiPurpose.Summary => AiMeter.PurposeSummary,
            _ => AiMeter.PurposeChat
        };
    }
}
