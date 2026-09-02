using System;
using System.Globalization;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>
    /// Every word the migration ceremony says, in one place.
    ///
    /// <para><b>Hardcoded English, deliberately, and it is a debt not a decision.</b> The repo
    /// runs both conventions for one-shot surfaces (FeatureIntroPopup and ProgramsIntroPopup are
    /// hardcoded on purpose; AwarenessConsentDialog and SeasonRecapWindow are fully localized),
    /// and this copy is heavily interpolated with live numbers, which makes it the worst kind of
    /// thing to hand to nine language files in a hurry. Collecting it here instead means the
    /// localization pass is one file and one afternoon, rather than an archaeology dig through
    /// XAML. See the PR notes.</para>
    ///
    /// <para><b>The tone is §6's.</b> Calm, ceremonial, no urgency, no scarcity, and — per
    /// CONTRACTS §0.6 — not one offer, price, tier or upsell anywhere near it. This is the night
    /// the wipe does not happen; it does not need selling.</para>
    /// </summary>
    public static class DescentCeremonyCopy
    {
        public const string Eyebrow = "THE DESCENT";

        // ---- Act I: the intro -------------------------------------------------

        public const string IntroHeadline = "The last season has ended.";

        public const string IntroBody =
            "Nothing was wiped. Nothing will be — this time the wipe simply doesn't happen, and it " +
            "won't happen again.\n\n" +
            "But the ladder underneath you has been rebuilt. The first forty levels are exactly " +
            "where you left them; below that it gets steeper, and it keeps getting steeper all the " +
            "way down. A level is going to mean more than it used to.\n\n" +
            "So you get to choose how you meet it. Once.";

        public const string IntroContinue = "Show me the two doors";

        public static string IntroStanding(int level, double lifetimeXp, int devotionDays) =>
            $"You stand at Level {level}  ·  {lifetimeXp.ToString("N0", CultureInfo.CurrentCulture)} lifetime XP  ·  " +
            $"{devotionDays} {(devotionDays == 1 ? "day" : "days")} of devotion";

        // ---- Act II: the two doors -------------------------------------------

        public const string ChoiceHeadline = "Two doors. Both of them go down.";

        public const string RestoreTitle = "Take it all back";
        public const string RestoreKicker = "Everything you built, re-measured.";

        public static string RestoreBody(int oldLevel, int newLevel) =>
            $"Your lifetime XP is spent again on the new ladder, and it buys you Level {newLevel}.\n\n" +
            $"The number moves because the ladder moved — not because anything was taken. Your " +
            $"lifetime XP is untouched, your devotion is untouched, your keepsakes are untouched.\n\n" +
            $"And the stages you already climbed come back to you one per day, so you get to watch " +
            $"each one land instead of skipping the whole ladder in a single night.";

        public static string RestoreDelta(int oldLevel, int newLevel) =>
            oldLevel == newLevel
                ? $"Level {oldLevel} → Level {newLevel}  (unchanged)"
                : $"Level {oldLevel} → Level {newLevel}";

        public const string CycleTitle = "Descend again";
        public const string CycleKicker = "Cycle I. From the top.";

        public static string CycleBody() =>
            "Level 1. The whole fall again, first night to last, with everything you learned the " +
            "first time.\n\n" +
            "A Cycle wipes nothing but the number. Your devotion, your keepsakes, your badge, every " +
            "day you have ever banked — all of it stays exactly where it is. What it adds is a " +
            "permanent mark on your card that says you chose to fall twice, and a bonus on every " +
            "point of XP you earn from here on.\n\n" +
            "This is the dramatic one. It is for people who want the fall itself.";

        public static string CycleBonusLine() =>
            $"+{(DescentMigration.CycleXpBonus - 1.0) * 100:0.#}% XP, permanently";

        public const string BothDoorsFooter =
            "Either way: the veteran badge and your keepsake archive are yours — every recap card " +
            "you ever earned, kept, because you were here before the fall. And either way the spiral " +
            "starts tonight at Day 1. Nobody arrives with a track already lit. We all fall together.";

        // ---- Act III: the confirm --------------------------------------------

        public const string ConfirmHeadline = "This is the part that doesn't come back.";

        public static string ConfirmBody(string choice) =>
            choice == DescentMigrationChoices.Cycle
                ? "You are choosing to descend again. Your level goes to 1 tonight and stays there " +
                  "until you earn it back.\n\n" +
                  "There is no undo. Not a setting, not a support ticket, not a second ceremony. " +
                  "You get asked this once, and this is the once.\n\n" +
                  "Take a breath first if you need one. The door will still be here."
                : "You are choosing to take it all back. Your level is re-measured on the new ladder " +
                  "and the stages you climbed will be handed back to you one a day.\n\n" +
                  "There is no undo. Not a setting, not a support ticket, not a second ceremony. " +
                  "You get asked this once, and this is the once.\n\n" +
                  "Take a breath first if you need one. The door will still be here.";

        public static string ConfirmYes(string choice) =>
            choice == DescentMigrationChoices.Cycle ? "Yes. Take me back to Level 1." : "Yes. Give it back to me.";

        public const string ConfirmBack = "Not that one";

        // ---- Act IV: the close ------------------------------------------------

        public const string DoneHeadline = "It's done.";

        public static string DoneBody(string choice, int level) =>
            choice == DescentMigrationChoices.Cycle
                ? $"Cycle I. Level {level}.\n\n" +
                  "The mark is on your card and it stays there. Everything you banked is still " +
                  "banked. The spiral starts tonight, at Day 1 — the same Day 1 as everyone else.\n\n" +
                  DoneReceiptLine(choice)
                : $"Level {level}, measured on the ladder you're actually standing on.\n\n" +
                  "The stages you climbed will come back to you one a day. The spiral starts " +
                  "tonight, at Day 1 — the same Day 1 as everyone else.\n\n" +
                  DoneReceiptLine(choice);

        /// <summary>
        /// THE RECEIPT, SAID OUT LOUD AND THEN POINTED AT (v6.9.1).
        ///
        /// <para>Five subjects on launch night chose a door and then could not find any trace of
        /// what it did. The Cycle bonus was live in the ledger the whole time, which is worse than
        /// it sounds: an irreversible choice with nothing to show for it reads, from the outside,
        /// exactly like a choice that did nothing. So the close now states the bonus AND names the
        /// surface that keeps it, and the profile card grew the chip this line is promising
        /// (MainWindow.ProfileCard.RefreshProfileDescentReceipt).</para>
        ///
        /// <para>The percent comes off <see cref="DescentMigration.CycleXpBonus"/> and never a
        /// literal: the constant is explicitly tunable, and copy that has to be remembered
        /// alongside it is copy that will one day be wrong.</para>
        /// </summary>
        public static string DoneReceiptLine(string choice) =>
            choice == DescentMigrationChoices.Cycle
                ? $"+{(DescentMigration.CycleXpBonus - 1.0) * 100:0.#}% XP on everything you earn, " +
                  "permanently. You can see it on your Profile card, under the XP bar."
                : "Your Profile card keeps the record of tonight, under the XP bar.";

        public const string DoneClose = "Begin";

        // ---- The escape hatch --------------------------------------------------

        public const string Later = "Not tonight";

        public const string LaterHint =
            "Closing this changes nothing. The ceremony will be waiting the next time you sync.";

        // ---- Companion lines (scripted; no bark rule required) -----------------

        /// <summary>Spoken on open. Rule id the bark lane can adopt later: <c>descent_ceremony_open</c>.</summary>
        public const string CompanionIntro = "Come here. Something is ending, and I want you with me for it.";

        /// <summary>Spoken after the ack. Rule id: <c>descent_ceremony_done</c>.</summary>
        public static string CompanionDone(string choice) =>
            choice == DescentMigrationChoices.Cycle
                ? "All the way back to the top with you. Good. I get to watch you fall all over again."
                : "There you are. Same you, honest numbers. Let's keep going down.";

        // ---- Helpers -----------------------------------------------------------

        private static readonly string[] Numerals =
            { "0", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

        /// <summary>Stage numerals for ceremony copy. Falls back to the digits past the ladder's end.</summary>
        public static string RomanNumeral(int n) =>
            n >= 0 && n < Numerals.Length ? Numerals[n] : n.ToString(CultureInfo.InvariantCulture);
    }
}
