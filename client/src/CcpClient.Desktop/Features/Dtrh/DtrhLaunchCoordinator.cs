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
    private readonly DtrhSaveSlots _slots;

    public DtrhLaunchCoordinator(ApplicationHost host, Window owner, string page = "index.html")
    {
        _host = host;
        _owner = owner;
        _page = page;
        _slots = host.Participants.OfType<DtrhSaveSlots>().Single();
    }

    /// <summary>The live host window (null before the first descend / after close).</summary>
    public DtrhHostWindow? HostWindow { get; private set; }

    /// <summary>The live picker (null unless the picker flow is showing).</summary>
    public DtrhSlotPickerWindow? Picker { get; private set; }

    /// <summary>Raised when the host window opens (auto-close timers arm here).</summary>
    public event Action? HostOpened;

    /// <summary>Raised when the whole flow ends without a live host window: host window
    /// closed, or picker cancelled (demo shutdown path).</summary>
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
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var window = new DtrhHostWindow(_host, _page, slot);
                HostWindow = window;
                window.Closed += (_, _) =>
                {
                    HostWindow = null;
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
