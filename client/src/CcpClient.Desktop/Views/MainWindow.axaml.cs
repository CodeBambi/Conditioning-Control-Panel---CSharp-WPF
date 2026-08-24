using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using CcpClient.Desktop.Features;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Tray;
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
    /// The navigation shell. A rail of doors, one page host, and — for the first time
    /// in the port — a landed surface reachable by a real gesture from a cold start with no
    /// command-line arguments: Studio -> Spiral Overlay -> THE LOOM
    /// (wpf-surface-reachability.md §8.4 @ 7527243e7, verified against the running v6.8.1 app).
    ///
    /// <para>The second real route is the port's flagship one: Play -> the DTRH
    /// hero card -> FALL IN / Quick Drop, behind the Tier-2 gate
    /// (<c>Features/Dtrh/DtrhGate.cs</c>, <c>MainWindow/MainWindow.Lab.cs:228,313</c>).</para>
    ///
    /// <para>The third is Graded Intake -> <c>Begin Intake</c>, the port's analogue of
    /// WPF's rail sub-entry <c>BtnNavGradedIntake</c> (<c>MainWindow/MainWindow.xaml:811-812</c>
    /// -> <c>MainWindow.TabNavigation.cs:947</c>) and of the destination page that entry opens.
    /// It also settles the two surfaces that do NOT get a door — the Chaos tunnel backdrop and
    /// the AvatarTube demonstrator; wpf-surface-reachability.md §11 @ 7527243e7 carries the evidence.</para>
    /// </summary>
    /// <param name="dtrhHarness">HARNESS-ONLY <c>--dtrh-*</c> options; null on every user path.</param>
    /// <param name="intakeHarness">HARNESS-ONLY <c>--intake-*</c> options; null on every user path.</param>
    public MainWindow(ApplicationHost host, bool popupDemo = false,
        Features.Dtrh.DtrhHarnessOptions? dtrhHarness = null,
        Features.Intake.IntakeHarnessOptions? intakeHarness = null)
    {
        InitializeComponent();

        // THE DIAGNOSTIC FOOTER IS INSTRUMENTATION AND A USER HAS NO USE FOR IT. It carries the
        // route and the rail layout probe, and it used to render unconditionally on every page —
        // caught by a headed capture, because no fact here asserts on whether somebody should be
        // LOOKING at the probe. The channel stays: it is still built, still UIA-readable, and
        // still logged once on first layout, which is the only evidence the Linux leg has on a
        // platform where every capture comes back black. Only the rendering is gated.
        DiagnosticFooter.IsVisible = DiagnosticFooterPolicy.Rendered;

        _host = host;
        Loom = new LoomLaunch(host, this);
        // The shell's duck/restore and the ONE tray owner. Built here because the two
        // non-restore menu entries are shell verbs — WPF wires the same two on the same object
        // (MainWindow/MainWindow.xaml.cs:323-351). It is handed to DtrhLaunch rather than built
        // inside it so there is exactly one tray presence per window, whatever ends up ducking.
        ShellTray = new ShellTray(this, host.LogDiagnostic, ShowCompanion, RequestApplicationExit);
        // The backend owns a native window created on this thread; Dispose is thread-affine and
        // Closed is the last moment this thread is still the UI thread.
        Closed += (_, _) => ShellTray.Dispose();
        // The entitlement capability comes from the composition root, which is also where its
        // probe is registered — so the state the gate consumes is the SAME state the System
        // page reports. A shell-local instance would let the two drift, and the one place the
        // port tells the truth about what it cannot do would be reporting a different object
        // than the one refusing people.
        var entitlement = host.Entitlement ?? throw new InvalidOperationException(
            "the shell needs the entitlement capability and this host has none — an ungated DTRH "
            + "launcher would hand out paid content, so composition refuses rather than degrading");
        Dtrh = new Features.Dtrh.DtrhLaunch(host, this, entitlement, ShellTray, dtrhHarness);
        // The ONE Arcademy construction site (Features/Arcademy/ArcademyLaunch.cs), on the SAME
        // entitlement object the DTRH door and the System page use. It takes one anyway even
        // though its door is shut and the tier bar behind it is never reached: a launcher that
        // could be built without one is a launcher that could hand out paid content the day the
        // door opens.
        Arcademy = new Features.Arcademy.ArcademyLaunch(host, entitlement.ResolveAsync);
        // The ONE intake construction site (Features/Intake/IntakeLaunch.cs). The --intake-demo
        // flag reaches this same object's coordinator rather than building a second one, which is
        // the LoomLaunch/DtrhLaunch convention two waves already depend on.
        Intake = new Features.Intake.IntakeLaunch(host, this, intakeHarness);
        // The ONE Goon construction site (Features/Goon/GoonLaunch.cs). Same
        // convention: any second caller would reach THIS object rather than build another.
        // (--goon-demo was granted and is NOT built -- D259; see GoonLaunch's class doc.)
        // No entitlement argument, and that is upstream's fact rather than an omission --
        // the Goon card is an unconditional door (Views/Tabs/PlayTabView.xaml:547-549) and
        // the paid rungs live inside, on hosting and sending (GoonHostService.cs:894,:909),
        // where Features/Goon/GoonDoors.cs refuses them.
        Goon = new Features.Goon.GoonLaunch(host, this);

        // The demonstrator popup manager. It has no user path now that the demonstrator card
        // is retired: it is infrastructure only (A-014 integration rule), kept because
        // --popup-demo is still the WSLg evidence driver for the W-04 window contract.
        _popups = new FeaturePopupManager(
            this,
            () => popupDemo
                ? new FeaturePopupWindow { DiagnosticSink = host.LogDiagnostic }
                : new FeaturePopupWindow(),
            FeaturePopupManager.CreateFocusRestoration(this));

        // The conditioning session, composed once by the composition root and reached
        // through the host — the same rule the entitlement capability follows. A shell-local
        // second engine would let the START button and the rack row drive different sessions.
        Session = host.Participants.OfType<Session.SessionParticipant>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                "the shell needs the conditioning session and this host has none — a START button "
                + "wired to nothing is the flag-instead-of-a-session shape the spine exists to prevent");

        // The scheduler, composed once by the composition root beside the session and
        // reached through the host — the same rule the session and the entitlement capability
        // follow. A shell-local second scheduler would poll a different engine than the one the
        // START button drives, which is precisely how a session gets started that nobody can stop
        // from the surface that started it.
        Scheduler = (host.Participants.OfType<Scheduling.SchedulerParticipant>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                "the shell needs the scheduler and this host has none — the START button would then "
                + "be unable to tell it the user stopped by hand, and it would restart the session "
                + "on its next tick")).Scheduler;

        // The haptic sink's app-lifetime owner, composed once by the composition root beside
        // the session and the scheduler and reached through the host — the same rule all three
        // follow. A shell-local second one would hold a second entitlement decision and a second
        // sink, so the switch on the page and the all-stop at teardown would be about different
        // objects, and the one that outlives the process is the one still holding a level.
        Haptics = host.Participants.OfType<Haptics.HapticParticipant>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                "the shell needs the haptic capability and this host has none — an ungated switch is "
                + "the fake-available shape the capability contract bans, and it would be ungated on "
                + "the one paid feature in the rack");

        // The ONE session-recap / session-history construction site
        // (Navigation/SessionRecapLaunch.cs), on the SAME log store the running session writes
        // into. Built here rather than inside the page for the LoomLaunch reason: the windows are
        // owned by the SHELL, and the page is not a window.
        Recap = new Navigation.SessionRecapLaunch(Session.MediaLog, this, host.LogDiagnostic);

        // The app-wide audio owner, composed once by the composition root beside the session, the
        // scheduler and the haptic sink, and reached through the host — the same rule all three
        // follow. A shell-local second one would mean a second native engine on the same endpoint,
        // which is precisely the defect Audio/AudioParticipant.cs was lifted out of the DTRH window
        // to remove: the Studio door's endpoint picker and the sound the app actually plays have to
        // be the same one device.
        Audio = CcpClient.Desktop.Audio.AudioParticipant.Of(host)
            ?? throw new InvalidOperationException(
                "the shell needs the app-wide audio owner and this host has none — the Studio door's "
                + "volume dials and endpoint picker would then move numbers no device ever reads");

        _pages[ShellRoutes.Studio] = new StudioPage(Loom, Session, Scheduler, Haptics, Recap, Audio);
        _pages[ShellRoutes.Companion] = new CompanionPage(ShowCompanion);
        _pages[ShellRoutes.Play] = new PlayPage(Dtrh, Goon, Arcademy);
        _pages[ShellRoutes.Intake] = new IntakePage(Intake);
        _pages[ShellRoutes.System] = new SystemPage(host, Session, ToastLayer);

        _doors[ShellRoutes.Studio] = DoorStudio;
        _doors[ShellRoutes.Companion] = DoorCompanion;
        _doors[ShellRoutes.Play] = DoorPlay;
        _doors[ShellRoutes.Intake] = DoorIntake;
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

        // The ONE start/stop control (WPF's BtnStart_Click, MainWindow/MainWindow.StartStop.cs:34):
        // it reads the session's state and branches. It is never disabled — a stop the user
        // cannot press is exactly the failure a panic button exists to prevent — and it lives on
        // the shell so a session started here survives every navigation (§8.6).
        SessionStartButton.Click += (_, _) =>
        {
            // WPF's BtnStart_Click reads _isRunning and writes the scheduler's manual-stop
            // flag on the way past, BEFORE it calls StopEngine/StartEngine
            // (MainWindow/MainWindow.StartStop.cs:52, :98-101, :106-107). So the scheduler is told
            // what the press MEANS while the session's state still says which press it was.
            //
            // This is the line that makes "I pressed STOP and it came back" impossible: a stop
            // inside an active window latches the flag, and the scheduler will not start anything
            // again until that window closes.
            Scheduler.NoteManualToggle(Session.Engine.Running);
            Session.Engine.Toggle();
            RenderSessionState();
        };
        Session.Engine.Changed += OnSessionEngineChanged;

        // WPF minimizes to tray inside the same invoke as a scheduled start
        // (MainWindow/MainWindow.StartStop.cs:615). Duck is the port's landed analogue — the shell
        // is minimized and a tray icon with the full menu goes up, and nothing is ever HIDDEN
        // (ShellTray, §12 D35). The event arrives already marshalled onto this thread.
        Scheduler.AutoStarted += () => ShellTray.Duck();
        RenderSessionState();

        Mount(Router.Current.Id);
        _doors[Router.Current.Id].IsChecked = true;

        if (popupDemo)
        {
            // WSLg evidence: open the demonstrator popup at startup — WSLg has no input
            // automation (a named limit), so it must open itself.
            Opened += (_, _) => _popups.Show();
        }

        // The layout probe: the measured DIP bounds, the actual RenderScaling and the screen
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

    /// <summary>The one session-recap and session-history launch path, same reason.</summary>
    public Navigation.SessionRecapLaunch Recap { get; }

    /// <summary>The one DTRH gate + launch path (public so tests drive the real gate, and so
    /// <c>--dtrh-demo</c> reaches the SAME coordinator the user path builds).</summary>
    public Features.Dtrh.DtrhLaunch Dtrh { get; }

    /// <summary>The one Graded Intake launch path (public so tests drive the real seam, and so
    /// <c>--intake-demo</c> reaches the SAME coordinator the user path builds).</summary>
    public Features.Intake.IntakeLaunch Intake { get; }

    /// <summary>The one Goon practice launch path (public so tests drive the real seam, and so any
    /// future second caller reaches the SAME launcher the user path builds — <c>--goon-demo</c> was
    /// granted and is NOT built, D259).</summary>
    public Features.Goon.GoonLaunch Goon { get; }

    /// <summary>The one Arcademy launch path (public so tests drive the real seam). Its door is a
    /// <c>static readonly false</c> with no override seam, so this refuses before it allocates
    /// anything — and the Play page's strip that reaches it is hidden from the same flag.</summary>
    public Features.Arcademy.ArcademyLaunch Arcademy { get; }

    /// <summary>Demonstrator popup manager; public so tests drive the real wiring.</summary>
    public FeaturePopupManager Popups => _popups;

    /// <summary>
    /// The shell's ONE in-app toast surface (census #54/#83) — how this app says anything without
    /// a modal. WPF has exactly one too, attached at the top-right of its root grid
    /// (<c>MainWindow/MainWindow.xaml.cs:2745</c>); a page-local second one would put a refusal
    /// somewhere the user has already navigated away from.
    /// </summary>
    public ToastHost Toasts => ToastLayer;

    /// <summary>The open companion window, if any; public so tests assert the real open path.</summary>
    public Features.Companion.CompanionWindow? Companion => _companion;

    /// <summary>The shell's ONE duck/restore and tray owner; public so tests drive the
    /// real menu and the real window transitions.</summary>
    public ShellTray ShellTray { get; }

    /// <summary>The conditioning session START drives; public so tests drive the real
    /// engine and the real effect rather than a shell-local copy of either.</summary>
    public Session.SessionParticipant Session { get; }

    /// <summary>The one scheduler; public so tests drive the real decision machine rather
    /// than a shell-local copy of it. It is APP-lifetime, not session-lifetime: it runs while
    /// nothing is running, which is the whole feature.</summary>
    public Scheduling.SessionScheduler Scheduler { get; }

    /// <summary>The one haptic sink's owner; public so tests drive the real gate and the
    /// real refusal rather than a shell-local copy of either. APP-lifetime, like the scheduler and
    /// unlike every rack module: upstream's is a static built at startup and never engine-started
    /// (<c>App.xaml.cs:533</c>, <c>:2060</c>).</summary>
    public Haptics.HapticParticipant Haptics { get; }

    /// <summary>The one app-wide audio owner; public so tests read the real device seam and the
    /// real settings document rather than a shell-local copy of either. APP-lifetime, like the
    /// scheduler and the haptic sink: upstream's audio service is a field on the application built
    /// once at startup (<c>App.xaml.cs:1798</c>) that outlives every window and every run.</summary>
    public Audio.AudioParticipant Audio { get; }

    /// <summary>
    /// WPF's <c>UpdateStartButton</c> (<c>MainWindow/MainWindow.StartStop.cs:751-796</c>): one
    /// control, two states. Running paints it red (<c>Color.FromRgb(255,107,107)</c>, <c>:756</c>)
    /// and captions it <c>STOP</c> (<c>:762</c>); idle restores the pink and <c>START</c>
    /// (<c>:779,787</c>). The glyphs (<c>■</c>/<c>▶</c>) are dropped under the §9 D8
    /// emoji-stripping rule; the words are WPF's literals, not its <c>btn_start</c>/<c>btn_stop</c>
    /// localization keys, which this code path does not use.
    /// </summary>
    private void RenderSessionState()
    {
        var running = Session.Engine.Running;
        SessionStartButton.Content = running ? "STOP" : "START";
        SessionStartButton.SetValue(Avalonia.Automation.AutomationProperties.NameProperty, running ? "STOP" : "START");
        SessionStartButton.Classes.Set("running", running);
        SessionStatusText.Text = running
            ? "Session running."
            : string.Empty;
    }

    /// <summary>
    /// Repaint the START/STOP control. Called directly, off no dispatcher check of its own: the
    /// session's <c>Changed</c> is raised through <see cref="Session.EffectSignal"/> and
    /// arrives on the UI thread whenever one exists, so the marshalling this method used to carry
    /// now lives once, in the producer, for every module and every panel that will ever subscribe.
    /// </summary>
    private void OnSessionEngineChanged() => RenderSessionState();

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
    /// The companion surface: owned, modeless, one-at-a-time (activate if already open —
    /// the W-04 discipline). The window closes with its owner automatically. WPF has no
    /// entitlement gate on showing the companion (wpf-surface-reachability.md §5 @ 7527243e7).
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

    /// <summary>
    /// The tray menu's "Exit" (WPF <c>TrayIconService.cs:109-111</c> -> <c>OnExitRequested</c> ->
    /// <c>MainWindow/MainWindow.xaml.cs:323-343</c>, which ends in <c>Application.Current.Shutdown()</c>).
    ///
    /// <para>The port's counterpart of that final call is the classic desktop lifetime's
    /// <c>Shutdown()</c>, which raises <c>desktop.Exit</c> and reaches the ONE guarded teardown
    /// entry point — the settings flush, the operation drain and the reverse-order participant stop
    /// (<c>App.axaml.cs:88-95</c>). The port deliberately does not reproduce WPF's preamble
    /// (lockdown check, engine stop, audio kill, overlay dispose): none of those subsystems exists
    /// here, and its settings save is what <c>ShutdownAsync</c> already does first.</para>
    ///
    /// <para>A host with no classic lifetime (the headless test application) gets a logged no-op
    /// rather than a throw: exiting is not something to half-do, and a menu entry that killed a
    /// test runner would be worse than one that says it could not.</para>
    /// </summary>
    private void RequestApplicationExit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _host.LogDiagnostic("tray: Exit chosen — requesting application shutdown");
            desktop.Shutdown();
            return;
        }

        _host.LogDiagnostic(
            "tray: Exit chosen, but this process has no classic desktop lifetime; nothing was shut down");
    }
}
