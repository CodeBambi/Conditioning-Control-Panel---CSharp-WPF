namespace ConditioningControlPanel.Models.Dashboard
{
    /// <summary>
    /// Fx: left-click opens the Studio page, right-click toggles. Destination: left-click
    /// navigates or launches, right-click does nothing.
    /// </summary>
    public enum DashboardKind { Fx, Destination }

    /// <summary>
    /// One placeable feature on the Home mosaic. Pure data - no WPF types, no services, no gates.
    /// The catalog is the single keyspace the slot model persists; every outward keyspace (Studio
    /// rack, ShowTab, DailyFree) is a nullable column here rather than a second registry, because
    /// conflating those three is the bug this record exists to prevent (ExclusiveFeature.cs:76-81).
    /// Tier is livery, never an entitlement check: what an account may open is still decided by
    /// TierGate at the destination.
    /// </summary>
    public sealed record DashboardFeature(
        string Key,              // the persisted value, unique across the catalog
        int Ring,                // 1..4, the picker ring
        int Tier,                // 0 free, 1 RequiresPremium, 2 RequiresLab. Test-pinned.
        DashboardKind Kind,
        string? TitleLocKey,     // null when the title is a brand name; see TitleLiteral
        string BlurbLocKey,      // exclusives_tag_* for the Vault ten, dash_blurb_* for the rest
        string ArtPath,          // Resources-relative. Mod override contract - never rename one.
        string? RackKey,         // Fx: OpenStudioModule / ToggleWallFeature. Null for focusgaze.
        string? TabKey,          // Destination: ShowTab key, or null when the open is custom
        string? DailyFreeKey,    // DailyFreeService pool key, or null
        string? TitleLiteral = null);   // brand names stay literal (MainWindow.PlayTab.cs:79-80)
}
