using System.Threading.Tasks;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// Glue between the six legacy one-shot <see cref="IAiService"/> methods and
    /// <see cref="CompanionBrain"/>.
    ///
    /// <para>The legacy methods survive Train 1 as <c>[Obsolete]</c> adapters so
    /// <c>KeywordTriggerService</c>, <c>AutonomyService</c> and <c>AvatarTubeWindow.Reactions</c>
    /// compile unchanged. The routing itself lives in <see cref="AiServiceStrategy"/> — the single
    /// object <c>App.Ai</c> actually is — rather than being copied into all three providers, which is
    /// the mistake this whole train is undoing. The providers keep their legacy bodies, which is what
    /// <c>UseCompanionBrain=false</c> falls back to.</para>
    /// </summary>
    internal static class BrainAdapter
    {
        /// <summary>
        /// Whether an ambient one-shot should be expressed as an <c>«event: …»</c> and handed to the
        /// brain, or fall through to the provider's legacy stateless body. All three reasons to fall
        /// through live here so the five call sites cannot disagree:
        /// the kill switch / missing brain (<see cref="CompanionBrain.ShouldRoute(CompanionBrain?, bool)"/>),
        /// no provider entitlement, and a user-authored prompt template
        /// (<see cref="FrameFormatter.CanRouteAmbient"/>).
        /// </summary>
        public static bool ShouldRouteAmbient(CompanionBrain? brain, bool killSwitchOn, bool providerAvailable,
            string? promptTemplate) =>
            CompanionBrain.ShouldRoute(brain, killSwitchOn)
            && providerAvailable
            && FrameFormatter.CanRouteAmbient(promptTemplate);

        /// <summary>
        /// Whether a <c>GetBambiReplyExAsync</c> call is real user speech and should become a brain
        /// chat turn. App-authored prompts (<paramref name="isUserMessage"/> false) never route: the
        /// brain's chat path is <see cref="AiCallOptions.Interactive"/>, and an interactive turn
        /// escalates the user-facing Content Policy Notice — which must only ever be spent on text
        /// the user actually typed.
        /// </summary>
        public static bool ShouldRouteChat(CompanionBrain? brain, bool killSwitchOn, bool isUserMessage) =>
            isUserMessage && CompanionBrain.ShouldRoute(brain, killSwitchOn);

        /// <summary>
        /// Maps a brain reply onto the legacy ambient contract, which is "model text, or
        /// <c>null</c> and the caller uses its own preset phrase".
        ///
        /// <para>Refusals return null: an ambient moment the user did not prompt must never pop a
        /// POLICY bubble (the hit is already in the compliance log). Canned fallbacks return null too —
        /// which also fixes a latent badge bug, since the local provider used to hand its
        /// "Bambi's head is so empty right now~" fallback and its "(Ollama isn't running)" diagnostics
        /// back to reaction call sites that then rendered them under the pink AI badge.</para>
        /// </summary>
        public static string? ToAmbientLine(AiReplyResult? result)
        {
            if (result == null) return null;
            if (result.Refusal != null) return null;
            if (!result.IsAiGenerated) return null;
            return string.IsNullOrWhiteSpace(result.Text) ? null : result.Text;
        }

        /// <summary>
        /// Runs an ambient moment through the brain and maps the result back to the legacy contract.
        /// </summary>
        public static async Task<string?> ReactAsync(CompanionBrain brain, string descriptor)
        {
            var result = await brain.ReactAsync(descriptor).ConfigureAwait(false);
            return ToAmbientLine(result);
        }
    }
}
