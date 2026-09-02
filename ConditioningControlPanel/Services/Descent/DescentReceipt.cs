using System;
using System.Globalization;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>Which receipt a card is owed, if any.</summary>
    public enum DescentReceiptKind
    {
        /// <summary>Never migrated, or somebody else's card. Draw nothing.</summary>
        None = 0,

        /// <summary>"Take it all back" — level re-measured, nothing reset.</summary>
        Restore = 1,

        /// <summary>"Descend again" — Cycle I, and a permanent XP bonus to say so.</summary>
        Cycle = 2,
    }

    /// <summary>
    /// THE RECEIPT — what the ceremony's irreversible choice looks like afterwards, forever.
    ///
    /// <para>The ceremony asks the question once and then closes, and until v6.9.1 that was the
    /// last time the app ever mentioned the answer. Five subjects on launch night said the same
    /// thing in five ways: the Cycle bonus is real (it is applied in
    /// <see cref="ProgressionService.AddXP"/> via <see cref="DescentMigration.ActiveCycleXpBonus"/>)
    /// but nothing renders it, so a permanent choice left no trace and the people who had not
    /// chosen yet could not see what they were choosing between.</para>
    ///
    /// <para>Pure on purpose, exactly like <see cref="DescentMigration.Resolve"/>: the chip that
    /// draws this lives on a WPF card, but nothing about WHICH receipt is owed, or what percent it
    /// prints, needs a dispatcher to decide.</para>
    /// </summary>
    public static class DescentReceipt
    {
        /// <summary>
        /// Which receipt belongs on the viewer's own card, from persisted migration state.
        ///
        /// <para><b>Completion outranks the choice</b>, the same ordering the settings region
        /// documents: a choice that was applied locally but never acked is still in flight, and a
        /// receipt for it would be a promise the server has not made. An unrecognised choice
        /// string draws nothing rather than guessing at a door.</para>
        /// </summary>
        public static DescentReceiptKind Resolve(bool migrationCompleted, string? choice)
        {
            if (!migrationCompleted) return DescentReceiptKind.None;
            return choice switch
            {
                DescentMigrationChoices.Cycle => DescentReceiptKind.Cycle,
                DescentMigrationChoices.Restore => DescentReceiptKind.Restore,
                _ => DescentReceiptKind.None,
            };
        }

        /// <summary>
        /// The percent the Cycle receipt prints, derived from the multiplier that is ACTUALLY
        /// applied rather than from the blessed constant. 1.10 reads "10".
        ///
        /// <para>It tracks the applied number so the chip cannot ever advertise a bonus the ledger
        /// is not paying — <see cref="DescentMigration.CycleXpBonus"/> is explicitly tunable, and a
        /// settings file that lost its bonus should read "+0%" (which is a support signal) rather
        /// than a comfortable lie. Same "0.#" shape as
        /// <see cref="DescentCeremonyCopy.CycleBonusLine"/> so the ceremony and the card agree.</para>
        /// </summary>
        public static string BonusPercentText(double multiplier)
        {
            if (double.IsNaN(multiplier) || multiplier < 1.0) multiplier = 1.0;
            return ((multiplier - 1.0) * 100).ToString("0.#", CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// True when the XP readout should carry the "(+N%)" suffix: a Cycle receipt whose bonus
        /// is actually above 1.0. A zero bonus gets the chip (the choice still happened) but not a
        /// suffix on a number it is not moving.
        /// </summary>
        public static bool ShowsXpMultiplier(DescentReceiptKind kind, double multiplier) =>
            kind == DescentReceiptKind.Cycle
            && !double.IsNaN(multiplier)
            && multiplier > 1.0;
    }
}
