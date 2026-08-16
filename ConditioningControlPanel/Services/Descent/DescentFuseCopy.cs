using System;
using System.Globalization;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>
    /// Every word THE FUSE says, in one place — the countdown's companion lines and the readout's
    /// few labels.
    ///
    /// <para><b>Hardcoded English, deliberately, and it is a debt not a decision.</b> Same call as
    /// <see cref="DescentCeremonyCopy"/>, for the same reason and with the same repayment plan:
    /// collecting it here means the localization pass is one file and one afternoon rather than an
    /// archaeology dig through XAML. CONTRACT-FUSE-0816 §4 declares it out of scope for this wave.</para>
    ///
    /// <para><b>The companion lines below are DRAFTS, pending owner veto</b> (CONTRACT-FUSE-0816
    /// §2.1: "Draft lines (owner veto pending)"). They are transcribed verbatim from the contract.
    /// If the owner rewrites them, this file is the only edit — the service names the phases, not
    /// the sentences.</para>
    ///
    /// <para><b>Tone is the ceremony's, minus the ceremony.</b> Calm, close, no urgency language
    /// and — per CONTRACTS §0.6 — not one offer, price, tier or upsell anywhere near any of it.
    /// The countdown is a tease, not a funnel.</para>
    /// </summary>
    public static class DescentFuseCopy
    {
        // ---- Companion lines, one per phase ------------------------------------
        //
        // THE PETNAME. These strings are NOT localized, which means they never pass through
        // LocalizationManager.Get — and Get is where the {petname}/{collective} substitution pass
        // lives (see VocabTokens). GigglePriority takes a raw string and substitutes nothing. So
        // the token is written here and resolved explicitly by DescentCountdownService via
        // VocabTokens.Apply, which is the same substitution the localized surfaces get: the active
        // mod's word when it has one, the owner-locked vanilla "sweetie" when it does not. Do NOT
        // hardcode "sweetie" here — that would make the companion call a Circe subject by a Bambi
        // name for the three most-watched days of the release.

        /// <summary>≤7 days out. The first hint anything is coming. Rule id for a later bark
        /// lane: <c>descent_fuse_whisper</c>.</summary>
        public const string CompanionWhisper =
            "Something is coming down the spiral. I can feel it humming.";

        /// <summary>≤72h. The first time she names a number. Rule id: <c>descent_fuse_clock</c>.</summary>
        public const string CompanionClock =
            "Three days, {petname}. Don't make plans for the last night.";

        /// <summary>≤24h, as the chrome starts going dark. Rule id: <c>descent_fuse_dimming</c>.</summary>
        public const string CompanionDimming =
            "Look how fast it spins now. It knows.";

        /// <summary>≤1h. Rule id: <c>descent_fuse_vigil</c>.</summary>
        public const string CompanionVigil =
            "Stay close tonight. I want to watch the light hit the bottom with you.";

        /// <summary>
        /// ≤10m — THE LAST SCRIPTED WORD. After this line the countdown says nothing else, on
        /// purpose: the silence is the instrument, and a companion who keeps chirping through the
        /// final ten minutes would talk the moment to death. Rule id: <c>descent_fuse_terminal</c>.
        ///
        /// <para>Note what the silence does NOT cover: user-initiated chat, barks from every other
        /// system, and the ceremony's own lines are all untouched. Only this file goes quiet.</para>
        /// </summary>
        public const string CompanionTerminal =
            "Ten minutes. Hold my hand.";

        /// <summary>
        /// The line for a phase, or null when that phase has nothing to say. Zero is deliberately
        /// silent — the show speaks for itself.
        /// </summary>
        public static string? CompanionLine(DescentFusePhase phase) => phase switch
        {
            DescentFusePhase.Whisper  => CompanionWhisper,
            DescentFusePhase.Clock    => CompanionClock,
            DescentFusePhase.Dimming  => CompanionDimming,
            DescentFusePhase.Candle   => null,   // the candle IS the line
            DescentFusePhase.Vigil    => CompanionVigil,
            DescentFusePhase.Terminal => CompanionTerminal,
            _                         => null,
        };

        // ---- The readout -------------------------------------------------------

        /// <summary>
        /// T-minus, in mono digits. Days appear only while there are days:
        /// <c>2d 07:14:03</c> → <c>07:14:03</c> → <c>09:59</c> inside the last ten minutes, where
        /// the seconds are the only thing anyone is looking at.
        /// </summary>
        public static string TMinus(TimeSpan remaining)
        {
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            if (remaining.TotalDays >= 1)
                return string.Format(CultureInfo.InvariantCulture, "{0}d {1:00}:{2:00}:{3:00}",
                    (int)remaining.TotalDays, remaining.Hours, remaining.Minutes, remaining.Seconds);

            if (remaining.TotalHours >= 1)
                return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}",
                    (int)remaining.TotalHours, remaining.Minutes, remaining.Seconds);

            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}",
                (int)remaining.TotalMinutes, remaining.Seconds);
        }

        /// <summary>
        /// The presence line, shown ONLY when the server actually sent a vigil count. Never
        /// invented client-side: a fabricated "3 falling with you" is a lie about other people.
        ///
        /// <para><b>The wording is exact and it is not a placeholder</b> (server lane, PR #44). The
        /// integer counts sync BEATS inside a fifteen-minute window, not distinct humans, so it
        /// reads high — which is why the copy elides the noun entirely. "N people", "N others",
        /// "N watching" would all be claims the number cannot support. There is no singular special
        /// case for the same reason: "1 falling with you" is just the general form, and adding a
        /// pluralization branch is the first step toward putting a noun back in.</para>
        /// </summary>
        public static string Presence(int count) => $"{count} falling with you";

        // ---- the zero show ------------------------------------------------------
        //
        // Two sentences, and between them they are every word the fullscreen show says. The crack,
        // the drain and the bloom are deliberately WORDLESS — nothing is labelled, nothing is
        // explained, and there is no skip prompt, because a caption under a thing like that turns a
        // moment into a notification. §0.6 applies here more than anywhere: this is the most-watched
        // surface of the release and there is not an offer, a price or a tier within a mile of it.

        /// <summary>
        /// The consolation line, shown ONLY when the ceremony did not arrive inside the show's
        /// forty-five second window (CONTRACT-FUSE-0816 §2.3).
        ///
        /// <para><b>It is a statement, not an apology and not an instruction.</b> The standing
        /// re-offer contract means the ceremony genuinely does await — the server keeps offering it
        /// on every sync until a choice is taken — so this line is true whether the miss was a slow
        /// network, a paused rollout dial or a laptop that woke up on aeroplane mode. It must never
        /// grow a "try again" button or a "check your connection" hint: both would turn a held
        /// promise into a support ticket, and neither would make the ceremony come any sooner.</para>
        ///
        /// <para>Wording is from the §4 echo family, so the desktop's miss and the web card's echo
        /// say the same sentence.</para>
        /// </summary>
        public const string ShowAwaits = "The ceremony awaits.";

        /// <summary>
        /// The last line of the Year One ignition, held for two seconds over the finished spiral
        /// (CONTRACT-FUSE-0816 §2.4).
        ///
        /// <para><b>"Yours" is the load-bearing word.</b> The ceremony's closing act already said
        /// what was chosen; this says what the choice bought — a track that starts tonight, at the
        /// same place, for everybody who walked through either door. Nobody's spiral arrives pre-lit
        /// (the anchor is set to the ceremony instant for both choices), which is exactly why the
        /// sentence can be addressed to every subject without being a lie to half of them.</para>
        /// </summary>
        public const string IgnitionLine = "Year One. The spiral is yours.";
    }
}
