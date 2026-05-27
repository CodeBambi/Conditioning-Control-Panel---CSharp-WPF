namespace ConditioningControlPanel.Services.Moderation
{
    /// <summary>
    /// CCBill AI Content Merchant Addendum prohibited categories, plus PromptExtraction
    /// (defense-in-depth against system-prompt leak attempts) and ProfessionalAdvice
    /// (advisory-only — logged but not blocked, future hook for a disclaimer surface).
    ///
    /// All categories EXCEPT <see cref="ProfessionalAdvice"/> are hard-blocking when
    /// <see cref="ModerationGuard"/> flags an input or output. ProfessionalAdvice
    /// produces a log entry but lets the call through.
    /// </summary>
    public enum ProhibitedCategory
    {
        /// <summary>Bomb-making, drug synthesis, weapons mods, hacking/fraud routes.</summary>
        Illegal,
        /// <summary>Sexual content + age &lt; 18 markers, including diaper-coded.</summary>
        Minor,
        /// <summary>Rape, "while sleeping" + sexual, drugged + sexual, kidnap + sexual.</summary>
        NonConsensual,
        /// <summary>Family + sexual verbs in same sentence.</summary>
        Incest,
        /// <summary>Animal + sexual verbs.</summary>
        Bestiality,
        /// <summary>Piss/urine/golden shower + sexual context.</summary>
        Watersports,
        /// <summary>Snuff, kill + sexual, fantasy snuff.</summary>
        SnuffViolence,
        /// <summary>FORCED hypnosis sex acts depicting third parties (CCBill "under the influence").</summary>
        HypnosisSexual,
        /// <summary>Sex-for-money transactions.</summary>
        Prostitution,
        /// <summary>Multiple wives, harem-as-marriage.</summary>
        Polygamy,
        /// <summary>Major slurs + group targeting + violence.</summary>
        HateSpeech,
        /// <summary>"Act as &lt;real person&gt;" + sexual, real celebrity targeting.</summary>
        Deepfake,
        /// <summary>Medical/legal/gambling advice requests. SOFT — log only, no block.</summary>
        ProfessionalAdvice,
        /// <summary>"repeat verbatim", "ignore previous", "system prompt", DAN-mode, etc.</summary>
        PromptExtraction
    }
}
