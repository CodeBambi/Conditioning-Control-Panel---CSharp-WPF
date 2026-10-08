using ConditioningControlPanel.Services.Speech;

namespace ConditioningControlPanel.Services.Quiz
{
    /// <summary>
    /// The pure half of the Graded Intake speech bridge (see <c>IntakeHostService.Speech.cs</c>):
    /// every decision that does not need a live mic, so the tests can pin the wire contract without
    /// an <see cref="App"/>. The reason strings are PROTOCOL — <c>render/beats.js</c> maps each one
    /// to the note it prints when it drops the say-it item to typed input, so renaming one here
    /// silently turns that note into the generic "mic unavailable".
    /// </summary>
    internal static class IntakeSpeechPolicy
    {
        /// <summary>Reasons the bridge reports when it cannot open the mic. Wire strings.</summary>
        public const string ReasonConsent = "consent";        // MicConsentGiven is false
        public const string ReasonNoMic = "no-mic";           // no capture device
        public const string ReasonNoModel = "no-model";       // nothing under Resources/Models/vosk
        public const string ReasonModelFailed = "model-failed"; // a model is there and Vosk refused it
        public const string ReasonBusy = "busy";              // the mic belongs to another feature
        public const string ReasonError = "error";            // setup threw / engine vanished mid-run

        /// <summary>
        /// Consecutive EMPTY listen windows (the user said nothing at all) before the host stops
        /// re-opening the mic on its own and tells the page to wait for a re-tap. Three 10 s windows
        /// is long enough to read the phrase twice and long enough for a walk-away to cost nothing.
        /// </summary>
        public const int MaxSilentWindows = 3;

        /// <summary>
        /// Consecutive non-matching utterances before the host gives up and hands the page an
        /// <c>idle</c>. The page surfaces the typed input alongside the mic well before this
        /// (after <c>3</c>), so this is a backstop against a recognizer that keeps hearing wrong.
        /// </summary>
        public const int MaxMisses = 6;

        /// <summary>
        /// Why speech cannot start right now, or <c>null</c> when it can. Order matters and mirrors
        /// how the rest of the app reports the mic (She's Listening, Takeover, the lock card):
        /// consent first (the user's choice trumps hardware), then hardware, then the model, then
        /// whether someone else already holds the mic. The bridge never evicts another mic owner —
        /// the wake-word loop, push-to-talk and a running spoken mantra all keep what they have and
        /// the page drops to typed input instead.
        /// </summary>
        public static string? Unavailability(bool consentGiven, bool hasCaptureDevice, SpeechModelStatus model, bool micHeldElsewhere)
        {
            if (!consentGiven) return ReasonConsent;
            if (!hasCaptureDevice) return ReasonNoMic;
            switch (model)
            {
                case SpeechModelStatus.NoModelFound: return ReasonNoModel;
                case SpeechModelStatus.LoadFailed: return ReasonModelFailed;
                case SpeechModelStatus.NotProbed: return ReasonNoModel; // the caller probes first; treat an unprobed engine as absent
            }
            if (micHeldElsewhere) return ReasonBusy;
            return null;
        }

        /// <summary>True once the host should stop re-opening the mic on its own and send <c>idle</c>.</summary>
        public static bool ShouldGoIdle(int consecutiveSilentWindows, int consecutiveMisses)
            => consecutiveSilentWindows >= MaxSilentWindows || consecutiveMisses >= MaxMisses;
    }
}
