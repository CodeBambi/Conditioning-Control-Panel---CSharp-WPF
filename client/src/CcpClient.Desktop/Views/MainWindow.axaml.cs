using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using CcpClient.Desktop.Features;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Views.Pages;

namespace CcpClient.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, Control> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RadioButton> _doors = new(StringComparer.Ordinal);
    private readonly ApplicationHost _host;
    private bool _layoutProbeLogged;
    private readonly FeaturePopupManager _popups;
    private Features.Companion.CompanionWindow? _companion;

    /// <summary>
    /// SP-091: the navigation shell. A rail of doors, one page host, and — for the first time
    /// in the port — a landed surface reachable by a real gesture from a cold start with no
    /// command-line arguments: Studio -> Spiral Overlay -> THE LOOM
    /// (wpf-surface-reachability.md §8.4, verified against the running v6.8.1 app).
    ///
    /// <para>SP-094 adds the second real route and the port's flagship one: Play -> the DTRH
    /// hero card -> FALL IN / Quick Drop, behind the Tier-2 gate
    /// (<c>Features/Dtrh/DtrhGate.cs</c>, <c>MainWindow/MainWindow.Lab.cs:228,313</c>).</para>
    /// </summary>
    /// <param name="dtrhHarness">HARNESS-ONLY <c>--dtrh-*</c> options; null on every user path.</param>
    public MainWindow(ApplicationHost host, bool popupDemo = false,
        Features.Dtrh.DtrhHarnessOptions? dtrhHarness = null)
    {
        InitializeComponent();

        _host = host;
        Loom = new LoomLaunch(host, this);
        // The entitlement capability comes from the composition root, which is also where its
        // probe is registered — so the state the gate consumes is the SAME state the System
        // page reports. A shell-local instance would let the two drift, and the one place the
        // port tells the truth about what it cannot do would be reporting a different object
        // than the one refusing people.
        Dtrh = new Features.Dtrh.DtrhLaunch(
            host, this,
            host.Entitlement ?? throw new InvalidOperationException(
                "the shell needs the entitlement capability and this host has none — an ungated DTRH "
                + "launcher would hand out paid content, so composition refuses rather than degrading"),
            dtrhHarness);

        // SP-013 demonstrator popup manager. It has no user path now that the demonstrator card
        // is retired: it is infrastructure only (A-014 integration rule), kept because
        // --popup-demo is still the WSLg evidence driver for the W-04 window contract.
        _popups = new FeaturePopupManager(
            this,
            () => popupDemo
                ? new FeaturePopupWindow { DiagnosticSink = host.LogDiagnostic }
                : new FeaturePopupWindow(),
            FeaturePopupManager.CreateFocusRestoration(this));

        _pages[ShellRoutes.Studio] = new StudioPage(Loom);
        _pages[ShellRoutes.Companion] = new CompanionPage(ShowCompanion);
        _pages[ShellRoutes.Play] = new PlayPage(Dtrh);
        _pages[ShellRoutes.System] = new SystemPage(host);

        _doors[ShellRoutes.Studio] = DoorStudio;
        _doors[ShellRoutes.Companion] = DoorCompanion;
        _doors[ShellRoutes.Play] = DoorPlay;
        _doors[ShellRoutes.System] = DoorSystem;

        // The rail's markup and the declared route table must be the same set, in both
        // directions: a door with no page goes nowhere, a page with no door is unreachable.
        ShellRouteBinding.ValidateOrThrow(ShellRoutes.Declared.Select(r => r.Id), _pages.Keys);
        ShellRouteBinding.ValidateOrThrow(ShellRoutes.Declared.Select(r => r.Id), _doors.Keys);

        Router = new ShellRouter(ShellRoutes.Declared, ShellRoutes.Default);
        Router.Navigated += route => Mount(route.Id);

        foreach (var (id, door) in _doors)
        {
            var routeId = id;
            door.IsCheckedChanged += (_, _) =>
            {
                if (door.IsChecked == true)
                {
                    Router.Navigate(routeId);
                }
            };
        }

        Mount(Router.Current.Id);
        _doors[Router.Current.Id].IsChecked = true;

        if (popupDemo)
        {
            // SP-013 WSLg evidence: open the demonstrator popup at startup — WSLg has no input
            // automation (SP-008 named limit), so it must open itself.
            Opened += (_, _) => _popups.Show();
        }

        // SP-007 layout probe: the measured DIP bounds, the actual RenderScaling and the screen
        // origin of every rail door — the headed harness drives real input at these rects
        // (client/tools/verify/capture.ps1). Rendered in the window (UIA-readable on Windows)
        // and logged once on first layout (stderr-readable on Linux).
        DoorStudio.LayoutUpdated += (_, _) =>
        {
            LayoutProbeText.Text = string.Join(Environment.NewLine, ShellRoutes.Declared.Select(ProbeLine));
            if (!_layoutProbeLogged)
            {
                _layoutProbeLogged = true;
                host.LogDiagnostic(LayoutProbeText.Text.Replace(Environment.NewLine, " | "));
            }
        };
    }

    /// <summary>The shell's navigation model (public so tests drive the real rail).</summary>
    public ShellRouter Router { get; }

    /// <summary>The one Loom studio launch path (public so tests observe the real seam).</summary>
    public LoomLaunch Loom { get; }

    /// <summary>The one DTRH gate + launch path (public so tests drive the real gate, and so
    /// <c>--dtrh-demo</c> reaches the SAME coordinator the user path builds).</summary>
    public Features.Dtrh.DtrhLaunch Dtrh { get; }

    /// <summary>Demonstrator popup manager (SP-013); public so tests drive the real wiring.</summary>
    public FeaturePopupManager Popups => _popups;

    /// <summary>The open companion window (SP-046), if any; public so tests assert the real open path.</summary>
    public Features.Companion.CompanionWindow? Companion => _companion;

    /// <summary>The page currently mounted in the host, by route id.</summary>
    public Control PageFor(string routeId) => _pages[routeId];

    private void Mount(string routeId)
    {
        PageHost.Content = _pages[routeId];
        RouteProbeText.Text = $"route: {routeId}";
        // The door may already be checked (the user clicked it); setting it again is a no-op,
        // and setting it from code is what keeps a programmatic Navigate in step with the rail.
        _doors[routeId].IsChecked = true;
    }

    private string ProbeLine(ShellRoute route)
    {
        var door = _doors[route.Id];
        var topLeft = door.PointToScreen(new Point(0, 0));
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"layout-probe: door {route.Id} {door.Bounds.Width:F1}x{door.Bounds.Height:F1} DIP @ scale {RenderScaling:0.##} @ screen {topLeft.X},{topLeft.Y}");
    }

    /// <summary>
    /// SP-046 companion surface: owned, modeless, one-at-a-time (activate if already open —
    /// the W-04 discipline). The window closes with its owner automatically. WPF has no
    /// entitlement gate on showing the companion (wpf-surface-reachability.md §5).
    /// </summary>
    private void ShowCompanion()
    {
        if (_companion is { IsVisible: true })
        {
            _companion.Activate();
            return;
        }

        _companion = new Features.Companion.CompanionWindow(_host);
        _companion.Closed += (_, _) => _companion = null;
        _companion.Show(this);
    }
}
