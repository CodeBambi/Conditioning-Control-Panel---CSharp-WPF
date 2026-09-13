using System;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// The one place the Sparkle Points (skill points) ceiling lives on the client.
    ///
    /// The server clamps <c>skill_points</c> to <c>SKILL_POINTS_CAP</c> on every write path; this
    /// constant must match it. It was 9,999 and rose to 99,999 with the Back Room (CONTRACT.md 3.4),
    /// so a slot win can take a balance past four digits. Every client write goes through
    /// <see cref="AppSettings.SkillPoints"/>, which clamps with <see cref="Clamp"/>, so earn paths,
    /// sync merges, restores and purchase responses all agree on the same ceiling.
    /// </summary>
    public static class SparklePoints
    {
        /// <summary>Highest balance an account can hold. Matches server SKILL_POINTS_CAP.</summary>
        public const int Cap = 99_999;

        /// <summary>Clamp a balance into [0, <see cref="Cap"/>].</summary>
        public static int Clamp(int value) => Math.Clamp(value, 0, Cap);

        /// <summary>
        /// Sync merge rule: the balance only rises outside a purchase or an admin reset, so the
        /// higher of server and local wins, clamped to the cap.
        /// </summary>
        public static int MergeMax(int server, int local) => Clamp(Math.Max(server, local));
    }
}
