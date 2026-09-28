namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>The two doors of §6. Wire values are lowercase and exact — the server matches on them.</summary>
    public static class DescentMigrationChoices
    {
        /// <summary>Option A, "Take it all back": level re-derived from lifetime XP under curve v2.</summary>
        public const string Restore = "restore";

        /// <summary>Option B, "Descend again": Cycle I, level 1, permanent mark, lasting XP bonus.</summary>
        public const string Cycle = "cycle";

        public static bool IsValid(string? choice) =>
            choice == Restore || choice == Cycle;
    }

    public static class DescentCycleXp
    {
        /// <summary>
        /// The Cycle XP bonus. TUNABLE AND UNBLESSED — CONTRACTS §3 records that the owner has
        /// not signed off on 1.10, only on "there is a lasting bonus". It ships dark with the
        /// ceremony; changing it is a one-line edit here and nowhere else.
        /// </summary>
        public const double CycleXpBonus = 1.10;
    }
}
