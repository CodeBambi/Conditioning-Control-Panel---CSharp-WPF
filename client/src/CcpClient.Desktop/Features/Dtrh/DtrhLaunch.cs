using Avalonia.Controls;
using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>Which of the two WPF entries into the hole a gesture came from.</summary>
public enum DtrhEntry
{
    /// <summary>The hero card's FALL IN: the save picker opens first (<c>MainWindow.Lab.cs:246</c>).</summary>
    FallIn,

    /// <summary>Quick Drop: no picker, reuse the last slot (<c>MainWindow.Lab.cs:308,313-323</c>).</summary>
    QuickDrop,
}

/// <summary>
/// HARNESS-ONLY construction options for <see cref="DtrhLaunchCoordinator"/>, threaded from the
/// <c>--dtrh-*</c> flags. Every field is at its product default on the user path.
/// </summary>
public sealed record DtrhHarnessOptions(
    string Page = "index.html",
    string? FxDrive = null,
    bool M2Test = false,
    bool KillRenderers = false);

/// <summary>
/// THE ONE place a <see cref="DtrhLaunchCoordinator"/> is constructed, and the Tier-2 door in
/// front of it. Both callers go through it: the Play page's <c>FALL IN</c> / <c>Quick Drop</c>
/// buttons (the user path, gated) and the <c>--dtrh-demo</c> demonstrator in
/// <c>App.axaml.cs</c> (the headed-evidence path, which reaches past the gate on purpose — see
/// the comment at that call site). This is the <see cref="Navigation.LoomLaunch"/> pattern:
/// one construction site, several callers, no second launcher.
///
/// <para><b>The gate runs FIRST and it runs on every press.</b> WPF's handlers open with
/// <c>if (!TierGate.DemandLab("Down the Rabbit Hole", "dtrh")) return;</c>
/// (<c>MainWindow/MainWindow.Lab.cs:228</c>, and <c>:313</c> on the Quick Start path) — before
/// the already-open check, before the picker, before anything. Resolving per press rather than
/// caching one answer is deliberate: a cached verdict is a claim about a tier nobody re-checked.</para>
///
/// <para><b>That call is KEYED, and the key is a second grant condition this port does not
/// have.</b> The <c>"dtrh"</c> argument is not decoration: it selects the overload that ORs
/// <c>IsFreeToday("dtrh")</c> into the verdict, so on a server-declared drop day WPF opens the
/// door for a free user (<c>Services/TierGate.cs:90-91</c>; <c>MainWindow.Lab.cs:225-227</c>).
/// <see cref="DtrhGate"/> implements the tier term only, because the port has no
/// <c>DailyFreeService</c> to implement the other — divergence and close condition at
/// wpf-surface-reachability.md §10 D24. Quoting the keyed call while describing unkeyed
/// semantics is how that fact stayed invisible through a whole first submission.</para>
///
/// <para><b>The card is never disabled.</b> WPF's lock band is <c>IsHitTestVisible="False"</c>
/// so the click passes through and is refused out loud (<c>Views/Tabs/PlayTabView.xaml:508-512</c>,
/// style at <c>:251-258</c>); the one genuinely disabled part is the checkbox pair, which
/// writes settings through a binding with no handler to refuse in
/// (<c>MainWindow/MainWindow.PlayTab.cs:88-91</c>). So a gated press ARRIVES here, every time,
/// and produces a <see cref="DtrhGateDecision"/> the page renders.</para>
///
/// <para><b>The shell gets out of the way, but is never hidden.</b> WPF tucks the main window
/// into the tray when the hole opens and restores it on close
/// (<c>Services/Chaos/DtrhHostService.cs:156</c> -> <c>MainWindow/MainWindow.RemoteControl.cs:1517</c>
/// -> <c>Services/Notifications/TrayIconService.cs:145-148</c>; restore at
/// <c>DtrhHostService.cs:998</c>). The port does NOT tuck — see
/// wpf-surface-reachability.md §10 D20 for the decision and what a user sees differently. It
/// plain-minimizes and restores instead, reusing the port's own landed shape for exactly this
/// situation (<c>Features/Intake/IntakeHostWindow.axaml.cs:120-162</c>), so the taskbar button
/// is the way back at every instant and no icon has to exist for the window to return.</para>
/// </summary>
public sealed class DtrhLaunch
{
    private readonly ApplicationHost _host;
    private readonly Window _owner;
    private readonly HostLoginEntitlement _entitlement;
    private readonly DtrhHarnessOptions _options;
    private DtrhLaunchCoordinator? _coordinator;
    private WindowState? _ownerStateBeforeDuck;

    public DtrhLaunch(ApplicationHost host, Window owner, HostLoginEntitlement entitlement,
        DtrhHarnessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(entitlement);
        _host = host;
        _owner = owner;
        _entitlement = entitlement;
        _options = options ?? new DtrhHarnessOptions();
    }

    /// <summary>
    /// The DESCENT seam. Product default: the real coordinator, the real picker, the real
    /// descend. Headless tests replace it only where the alternative is impossible —
    /// <see cref="DtrhLaunchCoordinator.QuickStartAsync"/> goes straight to a
    /// <see cref="DtrhHostWindow"/>, a real WebView2 host no headless frame can present. The
    /// FALL IN branch is exercised through THIS default and really opens the real picker, so
    /// "Entitled launches" is not a claim about a seam.
    /// </summary>
    public Func<DtrhEntry, DtrhLaunchCoordinator, Task> Descend { get; set; } =
        static (entry, coordinator) => entry == DtrhEntry.FallIn
            ? coordinator.LaunchWithPickerAsync()
            : coordinator.QuickStartAsync();

    /// <summary>The one coordinator, built on first use. Construction reaches into the host's
    /// participants, so it is deliberately not done before someone actually wants the hole.</summary>
    public DtrhLaunchCoordinator Coordinator => _coordinator ??= CreateCoordinator();

    /// <summary>How many times a launch gesture reached the gate (presses, not launches). The
    /// gated card takes the click, so this rises on refusals too — that is the point.</summary>
    public int GateArrivals { get; private set; }

    /// <summary>What the gate last decided, or null before the first press.</summary>
    public DtrhGateDecision? LastDecision { get; private set; }

    /// <summary>Raised after every gate decision, refusals included.</summary>
    public event Action<DtrhGateDecision>? Decided;

    /// <summary>The hero card's FALL IN (<c>Views/Tabs/PlayTabView.xaml:455</c>).</summary>
    public Task<DtrhGateDecision> FallInAsync(CancellationToken cancellationToken = default) =>
        GateThenDescendAsync(DtrhEntry.FallIn, cancellationToken);

    /// <summary>Quick Drop (<c>Views/Tabs/PlayTabView.xaml:468</c>).</summary>
    public Task<DtrhGateDecision> QuickDropAsync(CancellationToken cancellationToken = default) =>
        GateThenDescendAsync(DtrhEntry.QuickDrop, cancellationToken);

    private async Task<DtrhGateDecision> GateThenDescendAsync(DtrhEntry entry, CancellationToken cancellationToken)
    {
        GateArrivals++;

        EntitlementOutcome outcome;
        try
        {
            outcome = await _entitlement.ResolveAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // WPF wraps the whole handler and tells the user rather than dying silently
            // (MainWindow.Lab.cs:269). A throw here is still "could not tell" — the one thing
            // it must never become is a dead control or a refusal of the account. Type name
            // only: an exception message on this path can carry a path or a bearer.
            outcome = new EntitlementOutcome.Unavailable(new EntitlementReason(
                EntitlementReasonCodes.TierAuthorityFault,
                "resolving the entitlement threw " + ex.GetType().Name));
        }

        var decision = DtrhGate.Decide(outcome);
        LastDecision = decision;
        // Outcome CLASS plus reason CODE, never the detail and never anything token-derived
        // (the SP-092 logging discipline; EntitlementOutcome.Describe is that rendering).
        _host.LogDiagnostic($"dtrh: gate on {entry} — entitlement {outcome.Describe()} -> {Classify(decision)}");
        Decided?.Invoke(decision);

        if (decision is DtrhGateDecision.Proceed)
        {
            await Descend(entry, Coordinator).ConfigureAwait(true);
        }

        return decision;
    }

    /// <summary>Log-safe decision class. Never the message: it is authored text, but the log
    /// line's job is classification and one vocabulary beats two.</summary>
    private static string Classify(DtrhGateDecision decision) => decision switch
    {
        DtrhGateDecision.Proceed proceed => "proceed(" + proceed.Tier.ToString().ToLowerInvariant() + ")",
        DtrhGateDecision.RefusedNotEntitled => "refused(not-entitled)",
        DtrhGateDecision.RefusedUnverified unverified => "refused(unverified:" + unverified.ReasonCode + ")",
        _ => "refused(unknown-decision)",
    };

    private DtrhLaunchCoordinator CreateCoordinator()
    {
        var coordinator = new DtrhLaunchCoordinator(
            _host, _owner, _options.Page, _options.FxDrive, _options.M2Test, _options.KillRenderers);
        coordinator.HostOpened += DuckOwner;
        coordinator.FlowEnded += RestoreOwner;
        return coordinator;
    }

    /// <summary>
    /// Plain minimize, recording the prior state so a maximized shell comes back maximized.
    /// Explicitly NOT a tray tuck: <c>Hide()</c> without a tray icon strands the user, and the
    /// port's tray capability has no menu to bring the window back with (§10 D20).
    /// </summary>
    private void DuckOwner()
    {
        if (_owner.WindowState == WindowState.Minimized)
        {
            return;
        }

        _ownerStateBeforeDuck = _owner.WindowState;
        _owner.WindowState = WindowState.Minimized;
        _host.LogDiagnostic("dtrh: shell minimized while the hole is open (prior state recorded; NOT a tray tuck)");
    }

    /// <summary>The single restore funnel. Fires on every real flow end — host window closed,
    /// picker cancelled, descend failed — and no-ops when nothing was ducked.</summary>
    private void RestoreOwner()
    {
        if (_ownerStateBeforeDuck is not { } prior)
        {
            return;
        }

        _ownerStateBeforeDuck = null;
        if (_owner.WindowState == WindowState.Minimized)
        {
            _owner.WindowState = prior == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            _owner.Activate();
            _host.LogDiagnostic($"dtrh: shell restored ({_owner.WindowState})");
        }
    }
}
