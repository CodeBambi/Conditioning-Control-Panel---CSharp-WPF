using Avalonia.Controls;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// HARNESS-ONLY construction options for <see cref="IntakeLaunchCoordinator"/>, threaded from the
/// <c>--intake-*</c> flags. Every field is at its product default on the user path.
/// </summary>
public sealed record IntakeHarnessOptions(string? Drive = null, bool KillRenderers = false);

/// <summary>
/// THE ONE place an <see cref="IntakeLaunchCoordinator"/> is constructed, and the weekly-pass
/// door in front of it. Both callers go through it: the Graded Intake page's <c>Begin Intake</c>
/// button (the user path, gated) and the <c>--intake-demo</c> demonstrator in
/// <c>App.axaml.cs</c> (the headed-evidence path, which reaches past the gate on purpose — see
/// the comment at that call site and §11 D32). This is the <see cref="Navigation.LoomLaunch"/> /
/// <see cref="Dtrh.DtrhLaunch"/> pattern: one construction site, several callers, no second
/// launcher.
///
/// <para><b>The gate runs on every press, and it runs AFTER the already-open check.</b> That
/// ordering is WPF's, exactly: <c>MainWindow/MainWindow.Lab.cs:112-117</c> refocuses a live run
/// and returns BEFORE <c>:124</c> ever asks about the pass. A user who is already inside an
/// intake is not re-adjudicated mid-run. Resolving per press rather than caching one answer is
/// deliberate — the week rolls over at Monday 00:00 local and a cached verdict would outlive it.</para>
///
/// <para><b>Why there is a gate at all.</b> Unlimited retakes are the patron privilege, said
/// outright upstream: <c>Premium</c> = "Patron. The pass system does not apply - unlimited runs,
/// no week, no door" (<c>Services/Progression/IntakePassService.cs:13</c>), and "The intake is a
/// premium Exclusive … free accounts get ONE run per week … while retakes stay a reason to
/// subscribe" (<c>:26-29</c>). SP-095 first shipped this door ungated, which handed every user
/// the patron privilege — an OVER-GRANT of the same class SP-094 closed at <c>DtrhGate</c>.
/// <see cref="IntakePassGate"/> is the correction; §11 D26 records it.</para>
///
/// <para><b>What is still NOT ported:</b> the second refusal, <c>App.Ai.IsAvailable</c>
/// (<c>MainWindow.Lab.cs:148-153</c>) — §11 D27; and the first-ever-run duck exemption
/// (<c>:155-159</c>) — §11 D28.</para>
/// </summary>
public sealed class IntakeLaunch
{
    private readonly ApplicationHost _host;
    private readonly Window _owner;
    private readonly IntakeHarnessOptions _options;
    private IntakeLaunchCoordinator? _coordinator;

    public IntakeLaunch(ApplicationHost host, Window owner, IntakeHarnessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(owner);
        _host = host;
        _owner = owner;
        _options = options ?? new IntakeHarnessOptions();
    }

    /// <summary>
    /// The ENTITLEMENT seam SP-054 built (<see cref="IntakePassService.IIntakeEntitlementSource"/>),
    /// and the only thing a test substitutes here. Null is this build's real product default —
    /// <see cref="IntakePassService.NoEntitlementSource"/>, no provider at all — which is why
    /// every user of this build reaches
    /// <see cref="IntakePassDecision.RefusedUndeterminable"/>. Nothing stubs the DECISION: the
    /// gate is a pure function over whatever the seam reports, exactly as SP-092 designed the
    /// entitlement capability to be exercised. Must be set before the first
    /// <see cref="Coordinator"/> access, because the context reads it once.
    /// </summary>
    public IntakePassService.IIntakeEntitlementSource? EntitlementSource { get; set; }

    /// <summary>
    /// Where the intake session's stores live. Null is the product default — the install's own
    /// data root through the SP-057 choke point. Set only by tests, and for the same reason
    /// <c>CompositionRoot.SettingsPathFactory</c> exists: preparing the context to ask about the
    /// pass really starts <c>intake_settings.json</c>, <c>intake_punchcard.json</c> and the
    /// subject id, and a test must never write those into a developer's real
    /// <c>%LOCALAPPDATA%/ConditioningControlPanel</c>. Redirecting the ROOT is not the same as
    /// stubbing the store: the real stores start, load and evaluate, just somewhere disposable.
    /// </summary>
    public string? DataDirectory { get; set; }

    /// <summary>
    /// The OPEN seam. Product default: the real coordinator opens the real
    /// <see cref="IntakeHostWindow"/>. Headless tests replace it only because that window binds a
    /// loopback origin and a real embedded browser surface no headless frame can present — the
    /// same reason <c>DtrhLaunch.Descend</c> exists. The seam moves nothing but the final call:
    /// the coordinator handed to it is <see cref="Coordinator"/>, which is what makes "one
    /// construction site" checkable rather than merely asserted.
    /// </summary>
    public Action<IntakeLaunchCoordinator> Open { get; set; } = static coordinator => coordinator.Launch();

    /// <summary>The one coordinator, built on first use. Construction is field-only (no store, no
    /// transport, no window): the session context is prepared when someone first asks about the
    /// pass, and its loopback origin binds only when a run actually opens.</summary>
    public IntakeLaunchCoordinator Coordinator => _coordinator ??=
        new IntakeLaunchCoordinator(
            _host, _owner, _options.Drive, _options.KillRenderers, EntitlementSource, DataDirectory);

    /// <summary>How many times the launch gesture arrived (presses, not runs). The refused page
    /// takes the click, so this rises on refusals too — that is the point, and the same reason
    /// <c>DtrhLaunch.GateArrivals</c> counts them.</summary>
    public int LaunchCount { get; private set; }

    /// <summary>What the gate last decided, or null before the first press.</summary>
    public IntakePassDecision? LastDecision { get; private set; }

    /// <summary>Raised after every gate decision, refusals included.</summary>
    public event Action<IntakePassDecision>? Decided;

    /// <summary>
    /// SP-097: raised when the launch flow THREW. WPF wraps its whole handler and shows the user a
    /// warning dialog reading "Couldn't start Graded Intake:" plus the message
    /// (<c>MainWindow/MainWindow.Lab.cs:161-166</c>); the port raises this and
    /// <see cref="Views.Pages.IntakePage"/> renders the fault plate from it. Never an
    /// <see cref="IntakePassDecision"/>: this page already has three refusals whose whole design
    /// is that one may not be rendered as another, and "the app broke" is a fourth kind of thing
    /// again.
    /// </summary>
    public event Action<Exception>? Faulted;

    /// <summary>The exception the last faulted launch threw, or null if none has. Kept so the
    /// detail survives in-process even with nothing subscribed to <see cref="Faulted"/>.</summary>
    public Exception? LastFault { get; private set; }

    /// <summary>
    /// Begin a run, or refocus the one already running. No picker and no setup step: WPF's click
    /// goes straight to <c>IntakeHostService.Launch</c> once the pass allows it
    /// (<c>MainWindow.Lab.cs:159</c>).
    ///
    /// <para>The whole body is wrapped, as WPF's handler is (<c>:109-166</c>). Preparing the
    /// context really starts stores and reads files, and this method is called STRAIGHT from a
    /// click handler — so before SP-097 a throw here escaped into Avalonia's dispatcher with
    /// nothing on screen to show for it.</para>
    /// </summary>
    /// <returns>The gate's decision; <c>null</c> when no decision was taken because a live run was
    /// refocused instead — WPF's own early return (<c>MainWindow.Lab.cs:112-117</c>) — or because
    /// the flow threw and the fault surface was raised instead. Inventing a Proceed for either
    /// path would log a grant nobody asked for.</returns>
    public IntakePassDecision? Launch()
    {
        LaunchCount++;

        try
        {
            // MainWindow.Lab.cs:112-117 — "Already open? Just focus it — never re-launch a live
            // run", and it returns BEFORE the pass check. The coordinator owns the refocus itself,
            // so there is exactly one implementation of that rule.
            if (Coordinator.HostWindow is { IsVisible: true })
            {
                _host.LogDiagnostic("intake: a run is already up — refocusing, gate not re-asked (WPF :112-117 ordering)");
                Open(Coordinator);
                return null;
            }

            var (state, reason) = Coordinator.EnsureContext().Pass.Evaluate();
            var decision = IntakePassGate.Decide(
                state, reason, IntakePassService.DaysUntilNextPass(DateTime.Now));

            LastDecision = decision;
            // State + reason CODE and the decision CLASS — never the message, never a day count
            // attached to a person (the SP-092 logging discipline).
            _host.LogDiagnostic($"intake: pass gate — {state}/{reason} -> {IntakePassGate.Classify(decision)}");
            Decided?.Invoke(decision);

            if (decision is IntakePassDecision.Proceed)
            {
                Open(Coordinator);
            }

            return decision;
        }
        catch (Exception ex)
        {
            // TYPE AND MESSAGE, both, and both in the log: WPF logs its own error (:163) and puts
            // ex.Message in front of the user (:164). Dropping the detail here would replace an
            // invisible failure with a silent one, which is the same defect.
            LastFault = ex;
            _host.LogDiagnostic(
                $"intake: launch FAULTED ({ex.GetType().Name}: {ex.Message}) — no run opened; the page carries the "
                + "failure (WPF MainWindow.Lab.cs:161-166 shows a warning dialog)");
            Faulted?.Invoke(ex);
            return null;
        }
    }
}
