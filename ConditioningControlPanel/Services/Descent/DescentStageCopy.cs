using System.Globalization;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.Descent
{
    // ============================================================================
    // THE STAGE NAMES — the seven rungs, said in words.
    //
    // OWNER-LOCKED 2026-08-11 and already shipping on web (cclabs-web
    // src/lib/descent/stages.ts, STAGE_NAMES). This is the desktop port of that
    // same locked set, so the two clients cannot disagree about what a rung is
    // called. The ladder itself lives on the wire (DescentStage.Thresholds) and
    // falls back to 1 / 7 / 21 / 50 / 100 / 180 / 300; only the COPY lives here.
    //
    // KEYED BY THE ORDINAL, NEVER BY THE WIRE'S `key` STRING. The server ships a
    // `stage.key` and it would be tempting to look up on it, but a typo or a rung
    // this build has no copy for would then land a veteran on stage 0's "nothing
    // has been banked yet". The ordinal is clamped to a rung that always exists;
    // a string off the wire is not. Web takes the same care for the same reason.
    //
    // LOCALIZED, unlike the 56 hardcoded topics in HelpContentService: these read
    // through Loc so the nine language files carry them from day one.
    // ============================================================================
    public static class DescentStageCopy
    {
        /// <summary>0..7, the only rungs this client has copy for. n = 0 is the real pre-begin rung.</summary>
        private static int Clamp(int n) => n < 0 ? 0 : (n > DescentReader.MaxStage ? DescentReader.MaxStage : n);

        /// <summary>"Downward" - the stage's name, localized.</summary>
        public static string Name(int n) =>
            Loc.Get(string.Format(CultureInfo.InvariantCulture, "descent_stage_{0}_name", Clamp(n)));

        /// <summary>The one line of depth imagery that sits under the name.</summary>
        public static string Flavor(int n) =>
            Loc.Get(string.Format(CultureInfo.InvariantCulture, "descent_stage_{0}_flavor", Clamp(n)));
    }
}
