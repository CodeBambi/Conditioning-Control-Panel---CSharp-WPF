namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// When the dashboard shows its "right-click a tile to switch it on or off" caption.
    ///
    /// <para>The gesture is the one thing about the dashboard nobody discovers on their own: a
    /// tile looks like a button, a button opens something, and the right-click that actually
    /// flips the feature is invisible until told. The caption tells them, and then gets out of
    /// the way: after <see cref="MaxUses"/> right-click toggles (tiles or premium chips, either
    /// counts) the user has demonstrably learned it and the hint is retired for good.</para>
    /// </summary>
    public static class DashboardToggleHintRule
    {
        /// <summary>Right-click toggles after which the caption is retired.</summary>
        public const int MaxUses = 3;

        public static bool ShouldShow(int usesSoFar) => usesSoFar < MaxUses;
    }
}
