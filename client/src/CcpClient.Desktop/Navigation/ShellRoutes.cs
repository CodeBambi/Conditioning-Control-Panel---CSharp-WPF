namespace CcpClient.Desktop.Navigation;

/// <summary>
/// The declared rail. THREE doors, and the count is the point: WPF v6.8.1's rail carries six
/// (<c>DoorHome</c>, <c>DoorStudio</c>, <c>DoorCompanion</c>, <c>DoorPlay</c>, <c>DoorYou</c>,
/// <c>DoorLibrary</c> — wpf-surface-reachability.md §8.1), and the port declares only the doors
/// whose destination actually works today. A door that navigates to an empty room is the same
/// unreachability the shell exists to end, so Home / Play / You / Library / The Spiral are
/// ABSENT rather than dead. WPF's own doctrine for a door that is not open is collapse, not
/// lock (<c>MainWindow/MainWindow.PlayTab.cs:117-125</c>).
///
/// <para>There is deliberately NO DTRH door: DTRH is Tier-2 gated in WPF
/// (<c>MainWindow.Lab.cs:228,313</c>) and the port has no entitlement service until SP-092.
/// <c>NavigationRouteTableTests</c> makes that boundary mechanical.</para>
/// </summary>
public static class ShellRoutes
{
    /// <summary>Studio: the effects rack. Its Spiral Overlay module reaches THE LOOM.</summary>
    public const string Studio = "studio";

    /// <summary>Companion: the companion surface's page (WPF's dashboard companion element
    /// NAVIGATES — <c>Views/Tabs/SettingsTabView.xaml:1864-1887</c> — it does not launch).</summary>
    public const string Companion = "companion";

    /// <summary>System: the startup trace and typed capability states (SP-003/SP-006 proofs).</summary>
    public const string System = "system";

    /// <summary>The rail, in rail order. The shell's markup declares the same three doors and
    /// <c>NavigationShellHeadlessTests</c> asserts the two agree — so a decorative fourth door
    /// cannot appear in the markup without reddening a named test.</summary>
    public static IReadOnlyList<ShellRoute> Declared { get; } =
    [
        new ShellRoute(Studio, "Studio", "Every effect, one rack"),
        new ShellRoute(Companion, "Companion", "Chat, takeover, awareness and permissions"),
        new ShellRoute(System, "System", "Startup trace and capability states"),
    ];

    /// <summary>The door the shell opens on. WPF opens on Home; the port has no Home surface,
    /// so it opens on the rack (divergence recorded in wpf-surface-reachability.md §9).</summary>
    public const string Default = Studio;
}
