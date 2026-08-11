using Avalonia.Controls;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: the intake launch coordinator (the DtrhLaunchCoordinator pattern): owns the
/// session-scoped watchdog + relaunch-once state (WPF _relaunchedOnce / _recoveryWindowed
/// parity — survives window recreation; NEVER a restart loop) and the host context
/// (stores/services/transport, coordinator-lifetime). Idempotent Launch (a live instance
/// is only re-focused, IntakeHostService.cs:75 parity).
/// </summary>
public sealed class IntakeLaunchCoordinator
{
    private readonly ApplicationHost _host;
    private readonly Window _owner;
    private readonly string? _drive;
    private readonly bool _killRenderers;
    private readonly IntakePassService.IIntakeEntitlementSource? _entitlement;
    private readonly DtrhWatchdog _watchdog = new();
    private IntakeHostContext? _context;
    private IntakeHostWindow? _closingForRecovery;
    private int _flowEnded;

    public IntakeLaunchCoordinator(ApplicationHost host, Window owner, string? drive = null,
        bool killRenderers = false, IntakePassService.IIntakeEntitlementSource? entitlement = null)
    {
        _host = host;
        _owner = owner;
        _drive = drive;
        _killRenderers = killRenderers;
        _entitlement = entitlement;
    }

    /// <summary>The live host window (null before launch / after close).</summary>
    public IntakeHostWindow? HostWindow { get; private set; }

    /// <summary>Raised when the host window opens (auto-close timers arm here).</summary>
    public event Action? HostOpened;

    /// <summary>Raised when the whole flow ends for real (a close-for-recovery does NOT
    /// end the flow — the DtrhLaunchCoordinator consult-CORRECTION-2 shape). One-shot.</summary>
    public event Action? FlowEnded;

    /// <summary>Idempotent launch (IntakeHostService.cs:74-75 parity — a live instance is
    /// only re-focused).</summary>
    public void Launch()
    {
        if (HostWindow is { IsVisible: true })
        {
            HostWindow.Activate();
            return;
        }

        _context ??= IntakeHostContext.Start(_host, entitlement: _entitlement);
        OpenWindow(recoveryWindowed: false);
    }

    /// <summary>The watchdog's typed recovery outcome (UI thread). Relaunch = tear down +
    /// recreate ONCE, forced WINDOWED (:110-115 — _recoveryWindowed parity), with the
    /// relaunch-path stale-profile recovery BEFORE recreation (the deterministic
    /// 0x800700AA case — SP-023 surprise #7). Exhausted = honest close.</summary>
    private void OnRecoveryRequested(DtrhWatchdog.DtrhRecoveryOutcome outcome)
    {
        switch (outcome)
        {
            case DtrhWatchdog.DtrhRecoveryOutcome.Relaunch relaunch:
            {
                var dead = HostWindow;
                _host.LogDiagnostic(
                    $"intake: recovery ({relaunch.Reason}) — relaunching ONCE windowed (generation {relaunch.Generation})");
                if (dead is not null)
                {
                    _closingForRecovery = dead;
                    dead.CloseForRecovery();
                }

                var recovery = DtrhProfileLock.TryRecover(DtrhProfileLock.WebView2ProfileDir("intake"));
                _host.LogDiagnostic($"intake: relaunch-path stale-profile recovery: {recovery}");
                OpenWindow(recoveryWindowed: true);
                break;
            }
            case DtrhWatchdog.DtrhRecoveryOutcome.Exhausted exhausted:
            {
                _host.LogDiagnostic(
                    $"intake: recovery ({exhausted.Reason}) — relaunch already spent; honest close (WPF 'giving up' parity)");
                HostWindow?.CloseForRecovery();
                break;
            }
        }
    }

    private void OpenWindow(bool recoveryWindowed)
    {
        var context = _context ?? throw new InvalidOperationException("Launch() starts the context first.");
        var window = new IntakeHostWindow(_host, context, _owner, _watchdog, recoveryWindowed, _drive, _killRenderers);
        HostWindow = window;
        window.RecoveryRequested += OnRecoveryRequested;
        window.Closed += (_, _) =>
        {
            HostWindow = null;
            if (ReferenceEquals(_closingForRecovery, window))
            {
                // Close-for-recovery: the relaunch replaces this window; the flow continues.
                _closingForRecovery = null;
                return;
            }

            EndFlow();
        };
        window.Show(_owner);
        HostOpened?.Invoke();
    }

    private void EndFlow()
    {
        if (Interlocked.Exchange(ref _flowEnded, 1) != 0)
        {
            return;
        }

        try { _context?.Dispose(); } catch { /* best-effort — teardown is idempotent */ }
        _context = null;
        FlowEnded?.Invoke();
    }
}
