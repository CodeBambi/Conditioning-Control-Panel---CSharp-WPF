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
    private string? _loggedLayoutProbe;
    private readonly FeaturePopupManager _popups;
    private Features.Companion.CompanionWindow? _companion;
    private DateTimeOffset _lastPanicPress = DateTimeOffset.MinValue;

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
        // still logged whenever the geometry it describes changes, which is the only evidence the
        // Linux leg has on a platform with no UIA. Only the rendering is gated.
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
        // The ONE mantra construction site (Features/Mantra/MantraLaunch.cs). Built HERE and not
        // inside the Play page for the LoomLaunch/Recap reason recorded below: the window is owned
        // by the SHELL and the page is not a window. No entitlement argument, and that is
        // upstream's fact rather than an omission - the typed game is "free by design - no tier
        // bar ... gating the typed game would be gating the cheaper half of something already
        // given away" (MainWindow/MainWindow.PlayTab.cs:282-285).
        Mantra = new Features.Mantra.MantraLaunch(host, this);

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
        _pages[ShellRoutes.Play] = new PlayPage(Dtrh, Goon, Arcademy, Mantra, ToastLayer);
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
        // and written to stderr, which is the ONLY copy Linux has: WSLg publishes no UIA, so
        // capture-wslg.sh crops and clicks at whatever the logged line says.
        //
        // LOGGED WHENEVER THE DESCRIBED VALUES CHANGE, NOT ONCE ON THE FIRST LAYOUT. This used to
        // latch a bool on the first LayoutUpdated, and on X11 the first layout runs BEFORE the
        // scale factor and the window placement land — so the one logged line described a window
        // that no longer existed, while the on-screen copy went on being recomputed and correct.
        // Measured on WSLg at AVALONIA_GLOBAL_SCALE_FACTOR=1.75: the X window was 1925x1330 and
        // the on-screen probe read "174.9x44.0 DIP @ scale 1.75 @ screen 21,79" while stderr
        // still read "175.0x44.0 DIP @ scale 1 @ screen 12,45". A stale probe is worse than no
        // probe, because the harness TRUSTS it: a crop taken from those coordinates photographed
        // pixels that were not a door and still scored 0.926, and a click taken from them landed
        // on the wrong door and photographed the wrong page while scoring 0.982.
        //
        // THE SIGNAL IS THE TEXT CHANGING, NOT THE EVENT FIRING. LayoutUpdated is raised after
        // every layout pass, and logging each one would bury the diagnostic log; identical text
        // means the geometry it describes is unchanged, so there is nothing new to say.
        DoorStudio.LayoutUpdated += (_, _) =>
        {
            var probe = string.Join(Environment.NewLine, ShellRoutes.Declared.Select(ProbeLine));
            LayoutProbeText.Text = probe;
            var line = probe.Replace(Environment.NewLine, " | ");
            if (line == _loggedLayoutProbe)
            {
                return;
            }

            _loggedLayoutProbe = line;
            host.LogDiagnostic(line);
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

    /// <summary>The one typed-mantra launch path (public so tests drive the real seam, and so its
    /// data-directory seam can be pointed at a temporary store rather than the developer's own).
    /// Upstream's counterpart is <c>MainWindow.StartMantraSession</c>, which has had NO CALLER
    /// since the 2026-08-12 relayout dropped the Play page's Mantras card
    /// (<c>MainWindow/MainWindow.PlayTab.cs:262</c>); the port restores the door because that
    /// removal was de-duplication whose premise was false for this one card, recorded in the
    /// relayout's own commit as "MantraWindow entry point orphaned - re-home pending owner
    /// call".</summary>
    public Features.Mantra.MantraLaunch Mantra { get; }

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

    /// <summary>
    /// The emergency-stop chord, once the app has really claimed it from the OS — <c>App</c> sets
    /// this after <see cref="Input.Win32PanicKey.Arm"/> returns Available, and nothing sets it when
    /// the OS refused. It is therefore a promise the process is keeping, never a label: the running
    /// session's own status line names it (<see cref="RenderSessionState"/>), which is the only
    /// place a user finds out the escape exists.
    /// </summary>
    public string? PanicGesture { get; set; }

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
            ? PanicGesture is null ? "Session running." : $"Session running. {PanicGesture} stops everything."
            : string.Empty;
    }

    /// <summary>
    /// Repaint the START/STOP control. Called directly, off no dispatcher check of its own: the
    /// session's <c>Changed</c> is raised through <see cref="Session.EffectSignal"/> and
    /// arrives on the UI thread whenever one exists, so the marshalling this method used to carry
    /// now lives once, in the producer, for every module and every panel that will ever subscribe.
    /// </summary>
    private void OnSessionEngineChanged() => RenderSessionState();

    /// <summary>
    /// How long a second panic press still counts as part of the first. Upstream's window, reached
    /// through the constant the port already derived from it for the Arcademy's own ladder
    /// (<see cref="Features.Arcademy.ArcademySession.PanicDoublePressWindow"/>, WPF
    /// <c>ConditioningControlPanel/MainWindow/MainWindow.xaml.cs:1156</c>).
    /// </summary>
    public static TimeSpan PanicDoublePressWindow => Features.Arcademy.ArcademySession.PanicDoublePressWindow;

    /// <summary>
    /// <b>The emergency stop's ladder, and the shortest thing in this file that matters most.</b>
    /// Public so a test drives the real one rather than a copy of it; also called by
    /// <see cref="Input.Win32PanicKey.Pressed"/> on the UI thread.
    ///
    /// <para>Two rungs, upstream's (<c>ConditioningControlPanel/MainWindow/MainWindow.xaml.cs:1164</c>
    /// and <c>:1227</c>): <b>press 1 while a session is running stops it</b> — every module is
    /// disarmed, which is what posts each surface's withdrawal — and <b>a press within
    /// <see cref="PanicDoublePressWindow"/> with nothing running exits the application</b> through
    /// the same guarded teardown the tray menu's Exit reaches. So the pair "stop, then out" is two
    /// presses of one chord, exactly as it is upstream.</para>
    ///
    /// <para>The scheduler is told the stop was MANUAL before the engine is touched, for the reason
    /// the START button's handler gives at its own call site: without it the next scheduler tick
    /// would start the session the user just panicked out of.</para>
    ///
    /// <para>The shell is un-ducked and raised on every press. A user who has just stopped a session
    /// has to be able to SEE the window that owns it, and the surfaces they were escaping were on
    /// top of it a moment ago.</para>
    ///
    /// <para><b>What this deliberately does NOT do.</b> It does not touch windows the user opened
    /// and can close themselves (DTRH, Goon, Intake, the companion), and it does not reach
    /// <c>ArcademySession.PanicPress</c> — the Arcademy's door is shut in this build, and wiring a
    /// hand-off to a surface nobody can open would be a branch no test could reach through the
    /// product. Both are named rather than silently absent.</para>
    /// </summary>
    public void PanicPress()
    {
        var now = DateTimeOffset.UtcNow;
        var doubleTap = now - _lastPanicPress <= PanicDoublePressWindow;
        _lastPanicPress = now;

        if (Session.Engine.Running)
        {
            _host.LogDiagnostic("panic: session running — stopping every module");
            Scheduler.NoteManualToggle(true);
            Session.Engine.Stop();
            RenderSessionState();
            SurfaceTheShell();
            return;
        }

        if (doubleTap)
        {
            _host.LogDiagnostic("panic: second press with nothing running — exiting");
            RequestApplicationExit();
            return;
        }

        _host.LogDiagnostic("panic: nothing running — shell raised; press again to exit");
        SurfaceTheShell();
    }

    /// <summary>
    /// Put the window that owns the STOP button back in front of the user. Restores a ducked shell
    /// first (that also takes the tray icon down), then un-minimizes and activates.
    /// </summary>
    private void SurfaceTheShell()
    {
        ShellTray.Restore();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
    }

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

    /// <summary>
    /// One rail door's geometry, in the two spaces its two consumers need.
    ///
    /// <para><c>@ screen</c> is the door's origin in SCREEN coordinates, which is what
    /// <c>capture.ps1</c> aims <c>SetCursorPos</c> and <c>CopyFromScreen</c> at on Windows.</para>
    ///
    /// <para><c>@ window</c> is the same origin relative to the window's own client area, and it
    /// exists because <c>@ screen</c> ALONE IS NOT A USABLE CONTRACT ON X11. Measured on WSLg in a
    /// single run at scale 1.75, three successive readings of the same door:
    /// <c>scale 1 @ screen 12,45</c> (before the scale factor lands), then
    /// <c>scale 1.75 @ screen 21,79</c> (scale landed, Avalonia still believes the window sits at
    /// 0,0), then <c>scale 1.75 @ screen 37,116</c> (the window manager's placement landed — root
    /// 16,37 plus 21,79). So the meaning of <c>@ screen</c> CHANGES COORDINATE SPACE during
    /// startup on that platform, and the Linux harness needs window-relative pixels for both of
    /// its jobs: <c>xgetimage.py --crop</c> takes them, and <c>xinput.py --click</c> adds the
    /// window's root origin itself. Reading <c>@ screen</c> and hoping to catch it in the middle
    /// state is exactly the accident this probe was fixed to stop relying on.</para>
    ///
    /// <para>The subtraction is done HERE, against the window's own <c>PointToScreen</c>, so both
    /// numbers come out of the same platform call in the same pass and are self-consistent
    /// whichever space that call is currently answering in.</para>
    /// </summary>
    private string ProbeLine(ShellRoute route)
    {
        var door = _doors[route.Id];
        var topLeft = door.PointToScreen(new Point(0, 0));
        var clientOrigin = this.PointToScreen(new Point(0, 0));
        var inWindow = topLeft - clientOrigin;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"layout-probe: door {route.Id} {door.Bounds.Width:F1}x{door.Bounds.Height:F1} DIP @ scale {RenderScaling:0.##} @ screen {topLeft.X},{topLeft.Y} @ window {inWindow.X},{inWindow.Y}");
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
