namespace CcpClient.Desktop.Navigation;

/// <summary>
/// The declared rail. FOUR doors, and the count is the point: WPF v6.8.1's rail carries six
/// (<c>DoorHome</c>, <c>DoorStudio</c>, <c>DoorCompanion</c>, <c>DoorPlay</c>, <c>DoorYou</c>,
/// <c>DoorLibrary</c> — wpf-surface-reachability.md §8.1), and the port declares only the doors
/// whose destination actually works today. A door that navigates to an empty room is the same
/// unreachability the shell exists to end, so Home / You / Library / The Spiral are ABSENT
/// rather than dead. WPF's own doctrine for a door that is not open is collapse, not lock
/// (<c>MainWindow/MainWindow.PlayTab.cs:117-125</c>).
///
/// <para>SP-094 opened the Play door. SP-091 declared it absent for one reason only — DTRH is
/// Tier-2 gated in WPF (<c>MainWindow.Lab.cs:228,313</c>) and the port had no entitlement
/// service, so the door would have handed out paid content. SP-092 landed the capability and
/// the door now opens onto a page whose launcher is gated by it. The DTRH surface itself is
/// still NOT a door: WPF reaches it in two hops, rail door then hero card, and the port
/// reproduces that (<c>NavigationRouteTableTests</c> keeps "dtrh"/"rabbit" out of every door's
/// id, label and tooltip mechanically).</para>
/// </summary>
public static class ShellRoutes
{
    /// <summary>Studio: the effects rack. Its Spiral Overlay module reaches THE LOOM.</summary>
    public const string Studio = "studio";

    /// <summary>Companion: the companion surface's page (WPF's dashboard companion element
    /// NAVIGATES — <c>Views/Tabs/SettingsTabView.xaml:1864-1887</c> — it does not launch).</summary>
    public const string Companion = "companion";

    /// <summary>Play: games, modes and the deep end. Its hero card reaches DOWN THE RABBIT
    /// HOLE, behind the Tier-2 gate (<c>Features/Dtrh/DtrhGate.cs</c>).</summary>
    public const string Play = "play";

    /// <summary>System: the startup trace and typed capability states (SP-003/SP-006 proofs).</summary>
    public const string System = "system";

    /// <summary>The rail, in rail order — Play sits after Companion because that is WPF's own
    /// rail order (Home, Studio, Companion, Play, You, Library — §8.1); System is the port's
    /// own door (§9 D2) and stays last. The shell's markup declares the same four doors and
    /// <c>NavigationShellHeadlessTests</c> asserts the two agree — so a decorative fifth door
    /// cannot appear in the markup without reddening a named test.</summary>
    public static IReadOnlyList<ShellRoute> Declared { get; } =
    [
        new ShellRoute(Studio, "Studio", "Every effect, one rack"),
        new ShellRoute(Companion, "Companion", "Chat, takeover, awareness and permissions"),
        new ShellRoute(Play, "Play", "Games, modes, and the deep end"),
        new ShellRoute(System, "System", "Startup trace and capability states"),
    ];

    /// <summary>The door the shell opens on. WPF opens on Home; the port has no Home surface,
    /// so it opens on the rack (divergence recorded in wpf-surface-reachability.md §9).</summary>
    public const string Default = Studio;
}
