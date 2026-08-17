using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Tray;

namespace CcpClient.Tests;

/// <summary>
/// Runs a tray backend through a full lifecycle and records, side by side, <b>what the backend
/// claimed</b> and <b>what the shell independently reports</b> (via <see cref="TrayShellProbe"/>).
///
/// <para>Keeping both in one record is what lets the facts assert
/// <c>Assert.Equal(machineFact, shellFact)</c> at statement depth 0 — no conditional, no early
/// return, nothing that can silence an assertion. The claim and the effect are separate fields
/// precisely so a backend that returns success without doing the work shows up as an inequality
/// rather than as a green test.</para>
/// </summary>
internal static class TrayObservations
{
    private static TrayIconRequest Request => new("CCP tray effect observation");

    /// <summary>Place, observe, remove, observe again.</summary>
    internal static PlaceThenRemoveRun PlaceThenRemove()
    {
        using var presence = TrayPresenceFactory.Create();
        var place = presence.Place(Request);
        var handles = HandlesOf(presence);
        var shellAfterPlace = TrayShellProbe.ShellHoldsIcon(handles.OwnerWindow, handles.IconId);
        var placedAfterPlace = presence.IsPlaced;

        var remove = presence.Remove();
        var shellAfterRemove = TrayShellProbe.ShellHoldsIcon(handles.OwnerWindow, handles.IconId);

        return new PlaceThenRemoveRun(
            MachineHasNotificationArea: TrayShellProbe.MachineHasNotificationArea,
            ClaimedAvailableOnPlace: place is CapabilityState.Available,
            ShellSawIconAfterPlace: shellAfterPlace,
            BackendReportsPlacedAfterPlace: placedAfterPlace,
            ClaimedAvailableOnRemove: remove is CapabilityState.Available,
            ShellSawIconAfterRemove: shellAfterRemove,
            BackendReportsPlacedAfterRemove: presence.IsPlaced,
            PlaceState: place,
            RemoveState: remove);
    }

    /// <summary>Place, observe, dispose without an explicit Remove, observe again.</summary>
    internal static PlaceThenDisposeRun PlaceThenDispose()
    {
        var presence = TrayPresenceFactory.Create();
        var place = presence.Place(Request);
        var handles = HandlesOf(presence);
        var shellWhilePlaced = TrayShellProbe.ShellHoldsIcon(handles.OwnerWindow, handles.IconId);

        presence.Dispose();

        return new PlaceThenDisposeRun(
            MachineHasNotificationArea: TrayShellProbe.MachineHasNotificationArea,
            ClaimedAvailableOnPlace: place is CapabilityState.Available,
            ShellSawIconWhilePlaced: shellWhilePlaced,
            ShellSawIconAfterDispose: TrayShellProbe.ShellHoldsIcon(handles.OwnerWindow, handles.IconId),
            OwnerWindowExistsAfterDispose: TrayShellProbe.WindowStillExists(handles.OwnerWindow),
            TeardownDiagnostic: (presence as Win32TrayPresence)?.TeardownDiagnostic);
    }

    /// <summary>
    /// Place, then post the exact notification the shell posts on a left-click and pump the owner
    /// window's queue. Proves the backend turns a shell click notification into an application
    /// event; it does not prove a real mouse click, which needs a headed run.
    /// </summary>
    internal static ClickRoutingRun PlaceThenSyntheticClick()
    {
        using var presence = TrayPresenceFactory.Create();
        var raised = 0;
        presence.Activated += (_, _) => Interlocked.Increment(ref raised);

        var place = presence.Place(Request);
        var handles = HandlesOf(presence);
        var shellSawIcon = TrayShellProbe.ShellHoldsIcon(handles.OwnerWindow, handles.IconId);
        TrayShellProbe.PostSyntheticLeftClick(handles.OwnerWindow, handles.CallbackMessage, handles.IconId);

        return new ClickRoutingRun(
            MachineHasNotificationArea: TrayShellProbe.MachineHasNotificationArea,
            ClaimedAvailableOnPlace: place is CapabilityState.Available,
            ShellSawIcon: shellSawIcon,
            ActivationsRaised: Volatile.Read(ref raised));
    }

    /// <summary>Flattens a state into one diagnostic line, so a failing assertion prints the
    /// backend's own reason instead of leaving the reader to guess why the shell said no.</summary>
    internal static string Describe(CapabilityState state) => state switch
    {
        CapabilityState.Available available => "Available: " + available.Detail,
        CapabilityState.Unavailable unavailable => $"Unavailable({unavailable.Reason.Code}): {unavailable.Reason.Detail}",
        CapabilityState.Faulted faulted => $"Faulted({faulted.Reason.Code}): {faulted.Reason.Detail}",
        _ => state.ToString() ?? "<null state>",
    };

    /// <summary>Zero handles for any backend that is not the Win32 one — which is itself the
    /// truth: a refusing backend has no window and no icon to interrogate.</summary>
    private static TrayNativeHandles HandlesOf(ITrayPresence presence) =>
        presence is Win32TrayPresence win32 ? win32.NativeHandles : default;

    internal sealed record PlaceThenRemoveRun(
        bool MachineHasNotificationArea,
        bool ClaimedAvailableOnPlace,
        bool ShellSawIconAfterPlace,
        bool BackendReportsPlacedAfterPlace,
        bool ClaimedAvailableOnRemove,
        bool ShellSawIconAfterRemove,
        bool BackendReportsPlacedAfterRemove,
        CapabilityState PlaceState,
        CapabilityState RemoveState);

    internal sealed record PlaceThenDisposeRun(
        bool MachineHasNotificationArea,
        bool ClaimedAvailableOnPlace,
        bool ShellSawIconWhilePlaced,
        bool ShellSawIconAfterDispose,
        bool OwnerWindowExistsAfterDispose,
        string? TeardownDiagnostic);

    internal sealed record ClickRoutingRun(
        bool MachineHasNotificationArea,
        bool ClaimedAvailableOnPlace,
        bool ShellSawIcon,
        int ActivationsRaised);
}
