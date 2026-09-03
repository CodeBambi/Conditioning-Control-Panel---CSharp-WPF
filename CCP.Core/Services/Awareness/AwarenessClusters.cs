namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// Cluster ids the awareness code branches on by name. A constant rather than a literal in four
    /// files: a typo in the adult-cluster string would silently widen the cloud projection, which is
    /// the one bug in this feature that cannot be walked back after it ships.
    ///
    /// <para>Lifted out of <c>ContextFrame.cs</c> when the privacy rules moved here: the rules branch
    /// on <see cref="Adult"/>, and the rest of that file still needs <c>ActivityCategory</c> and
    /// <c>HabitRecord</c>, which are head types today.</para>
    /// </summary>
    public static class AwarenessClusters
    {
        /// <summary>Adult content. Cluster id only ever crosses the wire — never a service name or title.</summary>
        public const string Adult = "site_eh";

        /// <summary>The infinite-feed cluster. Source of <c>TrendKind.Backslide</c>.</summary>
        public const string Doomscroll = "site_doomscroll";
    }
}
