namespace CcpClient.Desktop.Navigation;

/// <summary>
/// The declared rail. FIVE doors, and the count is the point: WPF v6.8.1's rail carries six
/// TOP-LEVEL doors (<c>DoorHome</c>, <c>DoorStudio</c>, <c>DoorCompanion</c>, <c>DoorPlay</c>,
/// <c>DoorYou</c>, <c>DoorLibrary</c> — wpf-surface-reachability.md §8.1) plus a sub-entry under
/// each, and the port declares only the doors whose destination actually works today. A door
/// that navigates to an empty room is the same unreachability the shell exists to end, so Home /
/// You / Library / The Spiral are ABSENT rather than dead. WPF's own doctrine for a door that is
/// not open is collapse, not lock (<c>MainWindow/MainWindow.PlayTab.cs:117-125</c>).
///
/// <para>The Play door is open. It was declared absent for one reason only — DTRH is
/// Tier-2 gated in WPF (<c>MainWindow.Lab.cs:228,313</c>) and the port had no entitlement
/// service, so the door would have handed out paid content. The entitlement capability landed and
/// the door now opens onto a page whose launcher is gated by it. The DTRH surface itself is
/// still NOT a door: WPF reaches it in two hops, rail door then hero card, and the port
/// reproduces that (<c>NavigationRouteTableTests</c> keeps "dtrh"/"rabbit" out of every door's
/// id, label and tooltip mechanically).</para>
///
/// <para>The Graded Intake door is open — the first port door whose WPF counterpart is a
/// rail SUB-entry rather than a top-level medallion (<c>BtnNavGradedIntake</c>,
/// <c>MainWindow/MainWindow.xaml:811-812</c>, tooltip <c>tab_gradedintake</c> = "Graded Intake",
/// handler <c>MainWindow.TabNavigation.cs:947</c> = <c>ShowTab("gradedintake")</c>). The port's
/// rail has no sub-entries, so the entry becomes a door; divergence §11 D25. The two surfaces
/// that were investigated and REFUSED a door are recorded there too: the Chaos tunnel is a backdrop
/// WPF renders under a running descent and navigates to from nowhere
/// (<c>Chaos/ChaosTunnelService.cs:20,34</c>), and the AvatarTube demonstrator is not the ported
/// companion surface — the Companion door already answers that question.</para>
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

    /// <summary>Graded Intake: the banded descent that drafts a session from how you answer. Its
    /// page carries the ONE launcher, as WPF's does (<c>BtnStartIntake</c>,
    /// <c>Views/Tabs/GradedIntakeTabView.xaml:152</c> -> <c>MainWindow.Lab.cs:108-167</c>).</summary>
    public const string Intake = "intake";

    /// <summary>System: the startup trace and typed capability states.</summary>
    public const string System = "system";

    /// <summary>The rail, in rail order — Play sits after Companion because that is WPF's own
    /// rail order (Home, Studio, Companion, Play, You, Library — §8.1), and Graded Intake sits
    /// after Play because that is where WPF keeps it: <c>"gradedintake"</c> is an entry inside
    /// the PLAY door's list (<c>MainWindow/MainWindow.TabNavigation.cs:600-601</c>). System is
    /// the port's own door (§9 D2) and stays last. The shell's markup declares the same five
    /// doors and <c>NavigationShellHeadlessTests</c> asserts the two agree — so a decorative
    /// sixth door cannot appear in the markup without reddening a named test.</summary>
    public static IReadOnlyList<ShellRoute> Declared { get; } =
    [
        new ShellRoute(Studio, "Studio", "Every effect, one rack"),
        new ShellRoute(Companion, "Companion", "Chat, takeover, awareness and permissions"),
        new ShellRoute(Play, "Play", "Games, modes, and the deep end"),
        // The tooltip is the destination page's own sub-line, verbatim
        // (Views/Tabs/GradedIntakeTabView.xaml:67) — the same rule the other doors follow.
        new ShellRoute(Intake, "Graded Intake",
            "A banded descent that reads how you answer - and how long you take - and drafts a personalised session from it."),
        new ShellRoute(System, "System", "Startup trace and capability states"),
    ];

    /// <summary>The door the shell opens on. WPF opens on Home; the port has no Home surface,
    /// so it opens on the rack (divergence recorded in wpf-surface-reachability.md §9).</summary>
    public const string Default = Studio;
}
