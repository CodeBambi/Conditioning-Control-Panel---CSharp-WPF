using Avalonia.Controls;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The two WPF entry points into the hole (SP-024 slice b2; MainWindow.Lab.cs:103-183):
/// <see cref="LaunchWithPickerAsync"/> = the hero card (save picker right before the hole
/// opens; cancel backs out, no launch — :123-127) and <see cref="QuickStartAsync"/> =
/// Quick Start (skips the picker BY DESIGN, reuses the last-chosen slot — :161-165).
/// Both descend through <see cref="DtrhSaveSlots.DescendInto"/> and open the b1 host
/// shell. Owned async: the descend's store writes are SP-004 owned operations whose
/// completions are awaited; failures surface as typed diagnostics, never swallowed.
/// </summary>
public sealed class DtrhLaunchCoordinator
{
    private readonly ApplicationHost _host;
    private readonly Window _owner;
    private readonly string _page;
    private readonly string? _fxDrive;
    private readonly bool _m2Test;
    private readonly bool _killRenderers;
    private readonly DtrhSaveSlots _slots;
    // SP-027 slice b5: the session-scoped watchdog + relaunch-once state (WPF
    // _relaunchedOnce parity — survives window recreation; NEVER a restart loop).
    private readonly DtrhWatchdog _watchdog = new();
    private int _currentSlot;
    private DtrhHostWindow? _closingForRecovery;

    public DtrhLaunchCoordinator(ApplicationHost host, Window owner, string page = "index.html", string? fxDrive = null, bool m2Test = false,
        bool killRenderers = false)
    {
        _host = host;
        _owner = owner;
        _page = page;
        _fxDrive = fxDrive;
        _m2Test = m2Test;
        _killRenderers = killRenderers;
        _slots = host.Participants.OfType<DtrhSaveSlots>().Single();
    }

    /// <summary>The live host window (null before the first descend / after close).</summary>
    public DtrhHostWindow? HostWindow { get; private set; }

    /// <summary>The live picker (null unless the picker flow is showing).</summary>
    public DtrhSlotPickerWindow? Picker { get; private set; }

    /// <summary>Raised when the host window opens (auto-close timers arm here).</summary>
    public event Action? HostOpened;

    /// <summary>Raised when the whole flow ends without a live host window: host window
    /// closed FOR REAL, or picker cancelled (demo shutdown path). A close-for-recovery
    /// (watchdog relaunch) does NOT end the flow (pre-approach consult CORRECTION 2).</summary>
    public event Action? FlowEnded;

    /// <summary>The hero-card path: picker first; DESCEND commits and boots; cancel backs
    /// out (WPF: slot == null → no launch).</summary>
    public async Task LaunchWithPickerAsync()
    {
        var picker = new DtrhSlotPickerWindow(_slots, _host.LogDiagnostic);
        Picker = picker;
        picker.Closed += (_, _) =>
        {
            Picker = null;
            if (picker.ChosenSlot is not int slot)
            {
                _host.LogDiagnostic("dtrh: picker cancelled — backing out (no launch)");
                FlowEnded?.Invoke();
                return;
            }

            _ = DescendAndOpenAsync(slot);
        };
        // Modal parity (WPF: ChaosSlotPickerWindow.Pick is ShowDialog). The returned
        // task completes at close; the Closed handler above drives the flow.
        await picker.ShowDialog<bool>(_owner).ConfigureAwait(false);
    }

    /// <summary>Quick Start: no picker; the last-chosen slot is already the live one
    /// (WPF: "that's the 'quick' part").</summary>
    public Task QuickStartAsync()
    {
        var slot = _slots.ActiveSlot;
        _host.LogDiagnostic($"dtrh: quick start — reusing slot {slot} (picker skipped by design)");
        return DescendAndOpenAsync(slot);
    }

    /// <summary>b5: the watchdog's typed recovery outcome (UI thread). Relaunch = tear
    /// down + recreate the window once on the same slot (WPF DisposeAll+Launch :858-876
    /// parity — the old CoreWebView2 is a zombie, re-navigation is undefined); Exhausted
    /// = honest close, never a restart loop.</summary>
    private void OnRecoveryRequested(DtrhWatchdog.DtrhRecoveryOutcome outcome)
    {
        switch (outcome)
        {
            case DtrhWatchdog.DtrhRecoveryOutcome.Relaunch relaunch:
            {
                var dead = HostWindow;
                _host.LogDiagnostic(
                    $"dtrh: recovery ({relaunch.Reason}) — relaunching ONCE (generation {relaunch.Generation}); slot {_currentSlot}");
                if (dead is not null)
                {
                    _closingForRecovery = dead;
                    dead.CloseForRecovery();
                }

                // Relaunch-into-locked-profile is the deterministic 0x800700AA case
                // (consult (b)2; SP-023 surprise #7): kill the stale children holding
                // OUR profile BEFORE recreating the window, or the one relaunch burns
                // on a lock panic. Typed outcome, logged — never silent.
                var recovery = DtrhProfileLock.TryRecover(DtrhProfileLock.WebView2ProfileDir());
                _host.LogDiagnostic($"dtrh: relaunch-path stale-profile recovery: {DescribeRecovery(recovery)}");
                _ = DescendAndOpenAsync(_currentSlot);
                break;
            }
            case DtrhWatchdog.DtrhRecoveryOutcome.Exhausted exhausted:
            {
                _host.LogDiagnostic(
                    $"dtrh: recovery ({exhausted.Reason}) — relaunch already spent; honest close (WPF 'giving up' :864 parity)");
                HostWindow?.CloseForRecovery();
                break;
            }
        }
    }

    private static string DescribeRecovery(DtrhProfileLock.DtrhProfileRecovery recovery) => recovery switch
    {
        DtrhProfileLock.DtrhProfileRecovery.Recovered r => $"killed {r.KilledCount} stale child(ren)",
        DtrhProfileLock.DtrhProfileRecovery.NothingToKill => "no stale children (profile free)",
        DtrhProfileLock.DtrhProfileRecovery.Unsupported u => $"unavailable — {u.Detail}",
        DtrhProfileLock.DtrhProfileRecovery.Failed f => $"FAILED — {f.Detail}",
        _ => recovery.GetType().Name,
    };

    private async Task DescendAndOpenAsync(int slot)
    {
        try
        {
            var outcome = await _slots.DescendInto(slot).ConfigureAwait(false);
            if (outcome is OperationOutcome.Failed failed)
            {
                _host.LogDiagnostic($"dtrh: descend into slot {slot} failed ({failed.Kind}): {failed.Reason}");
                FlowEnded?.Invoke();
                return;
            }

            _host.LogDiagnostic($"dtrh: descending into slot {slot}");
            _currentSlot = slot;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new DtrhHostWindow(_host, _page, slot, _fxDrive, _m2Test, _watchdog, _killRenderers);
                HostWindow = window;
                window.RecoveryRequested += OnRecoveryRequested;
                window.Closed += (_, _) =>
                {
                    HostWindow = null;
                    if (ReferenceEquals(_closingForRecovery, window))
                    {
                        // Close-for-recovery: the relaunch below replaces this window;
                        // the flow continues (FlowEnded is for real closes only).
                        _closingForRecovery = null;
                        return;
                    }

                    FlowEnded?.Invoke();
                };
                window.Show(_owner);
                HostOpened?.Invoke();
            });
        }
        catch (Exception ex)
        {
            _host.LogDiagnostic($"dtrh: descend into slot {slot} faulted: {ex.GetType().Name}");
            FlowEnded?.Invoke();
        }
    }
}
