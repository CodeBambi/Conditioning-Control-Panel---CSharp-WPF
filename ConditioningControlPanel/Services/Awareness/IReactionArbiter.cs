using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// Who is speaking. The arbiter's cooldown ledger is shared across all of these — that is the whole
    /// point of it (doc 02 §5, MASTER-SCOPE §3.1: one arbiter, not two).
    /// </summary>
    public enum ReactionSource
    {
        /// <summary>An awareness line written by the model.</summary>
        AwarenessLlm = 0,

        /// <summary>A pre-recorded, voiced bark from <c>BarkService</c>.</summary>
        Bark = 1,

        /// <summary>
        /// A keyword/OCR trigger's <c>AvatarComment</c> (System B). Ranks ABOVE awareness: the user
        /// configured it by hand, so it wins the moment rather than being crowded out by a quip.
        /// </summary>
        Keyword = 2
    }

    /// <summary>What the arbiter decided about one frame.</summary>
    /// <param name="Reason">
    /// Short gate name for the <c>[AWARE]</c> log — "cooldown", "same-app", "budget", "dnd",
    /// "over-threshold". Never free text from a data file.
    /// </param>
    public sealed record ArbiterDecision(
        AwarenessVerdict Verdict,
        RarityTier Tier,
        string Reason)
    {
        /// <summary>Nothing is said, and no cooldown is burned.</summary>
        public static ArbiterDecision Silent(string reason) =>
            new(AwarenessVerdict.Silence, RarityTier.Common, reason);
    }

    /// <summary>
    /// THE arbiter: the single owner of the companion's awareness-speech cooldown ledger.
    ///
    /// <para><b>The invariant it exists to enforce:</b> exactly one reaction per moment, from one mouth.
    /// Today a bark and an LLM quip can both fire on one tab switch because <c>BarkService</c> and the
    /// AvatarTube reaction path keep independent cooldowns (doc 02 §1.5). Everything that makes the
    /// companion speak about ambient events — awareness lines, awareness-gated barks, keyword avatar
    /// comments — goes through here, so double-reactions become impossible by construction rather than
    /// by careful wiring.</para>
    ///
    /// <para><b>Cooldowns burn on delivery, never on attempt.</b> A moderated, failed, timed-out or
    /// <c>[PASS]</c>-ed call must leave the budget exactly as it found it.</para>
    /// </summary>
    public interface IReactionArbiter
    {
        /// <summary>
        /// Offers a scored frame. Returns what should happen; the caller performs it and then reports
        /// back through <see cref="RecordExternalLine"/> when (and only when) a line actually reached
        /// the user.
        /// </summary>
        Task<ArbiterDecision> SubmitAsync(ContextFrame frame, CancellationToken cancellationToken = default);

        /// <summary>
        /// Tells the arbiter that a line was delivered by someone else — a bark fired by
        /// <c>BarkService</c>, or a keyword trigger's avatar comment. Without this the "one mouth"
        /// guarantee only holds for lines the arbiter itself chose.
        /// </summary>
        /// <param name="appId">The app the line was about, when it was about one.</param>
        void RecordExternalLine(ReactionSource source, string? appId = null);

        /// <summary>
        /// Whether <paramref name="source"/> is allowed to speak about <paramref name="appId"/> right
        /// now. Lets <c>BarkService</c> and the keyword engine ask before speaking instead of
        /// apologising afterwards.
        /// </summary>
        bool CanSpeak(ReactionSource source, string? appId = null);
    }
}
