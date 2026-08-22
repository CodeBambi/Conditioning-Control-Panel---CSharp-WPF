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

    // ---------- The menu ----------

    /// <summary>WPF's four entries in WPF's order (<c>TrayIconService.cs:96-110</c>), with the
    /// actions replaced by counters so a run can say WHICH one fired.</summary>
    internal sealed class MenuFixture
    {
        internal int RestoreInvocations;
        internal int WakeInvocations;
        internal int ExitInvocations;

        internal TrayMenu Menu { get; }

        internal MenuFixture()
        {
            Menu = new TrayMenu(
            [
                TrayMenuItem.Restore("show", "Show Dashboard", () => Interlocked.Increment(ref RestoreInvocations)),
                TrayMenuItem.Command("wake", "Wake Up!", () => Interlocked.Increment(ref WakeInvocations)),
                TrayMenuItem.Separator(),
                TrayMenuItem.Command("exit", "Exit", () => Interlocked.Increment(ref ExitInvocations)),
            ]);
        }
    }

    /// <summary>
    /// Install the menu, then ask the OPERATING SYSTEM what it holds — never the presence.
    /// </summary>
    internal static MenuRun SetMenuThenReadItBack()
    {
        using var presence = TrayPresenceFactory.Create();
        var fixture = new MenuFixture();

        var state = presence.SetMenu(fixture.Menu);
        var handles = HandlesOf(presence);

        return new MenuRun(
            WindowsHost: TrayShellProbe.WindowsHost,
            ClaimedAvailable: state is CapabilityState.Available,
            MenuHandleIsReal: handles.Menu != 0,
            ReadBack: TrayShellProbe.ReadMenu(handles.Menu),
            State: state);
    }

    /// <summary>
    /// Place the icon with the menu on it, post the shell's OWN right-click notification, pump the
    /// owner window's queue so the real WndProc runs, and let the seam stand in for the one call no
    /// test can drive: <c>TrackPopupMenu</c>'s modal loop. The seam is handed the real OS-held
    /// menu handle, and it answers with the command id the OS itself reports for the restore entry
    /// — so the id that travels back into the product is the OS's id, not the test's guess.
    /// </summary>
    internal static RightClickRun PlaceThenSyntheticRightClick()
    {
        using var presence = TrayPresenceFactory.Create();
        var fixture = new MenuFixture();

        var menuState = presence.SetMenu(fixture.Menu);
        var place = presence.Place(Request);
        var handles = HandlesOf(presence);
        var shellSawIcon = TrayShellProbe.ShellHoldsIcon(handles.OwnerWindow, handles.IconId);

        var trackedMenu = (nint)0;
        var trackedOwner = (nint)0;
        if (presence is Win32TrayPresence win32)
        {
            win32.MenuTracker = (menu, _, _, owner) =>
            {
                trackedMenu = menu;
                trackedOwner = owner;
                // The OS's own id for entry 0 — "Show Dashboard". Reading it back rather than
                // assuming 1 is what makes this a route through the real menu.
                var readBack = TrayShellProbe.ReadMenu(menu);
                return readBack.Entries.Count > 0 ? readBack.Entries[0].Id : 0u;
            };
        }

        TrayShellProbe.PostSyntheticRightClick(handles.OwnerWindow, handles.CallbackMessage, handles.IconId);

        return new RightClickRun(
            WindowsHost: TrayShellProbe.WindowsHost,
            MachineHasNotificationArea: TrayShellProbe.MachineHasNotificationArea,
            ClaimedAvailableOnSetMenu: menuState is CapabilityState.Available,
            ClaimedAvailableOnPlace: place is CapabilityState.Available,
            ShellSawIcon: shellSawIcon,
            TrackerInvocations: (presence as Win32TrayPresence)?.MenuTrackerInvocations ?? 0,
            TrackerSawTheRealMenu: trackedMenu != 0 && trackedMenu == handles.Menu,
            TrackerSawTheOwnerWindow: trackedOwner != 0 && trackedOwner == handles.OwnerWindow,
            RestoreInvocations: Volatile.Read(ref fixture.RestoreInvocations),
            WakeInvocations: Volatile.Read(ref fixture.WakeInvocations),
            ExitInvocations: Volatile.Read(ref fixture.ExitInvocations),
            MenuState: menuState,
            PlaceState: place);
    }

    /// <summary>Ask for a balloon with no icon placed, then place one and ask again.</summary>
    internal static BalloonRun BalloonWithoutThenWithAnIcon()
    {
        using var presence = TrayPresenceFactory.Create();

        var withoutIcon = presence.ShowNotification(TrayNotification.FirstMinimize);
        presence.Place(Request);
        var handles = HandlesOf(presence);
        var shellSawIcon = TrayShellProbe.ShellHoldsIcon(handles.OwnerWindow, handles.IconId);
        var withIcon = presence.ShowNotification(TrayNotification.FirstMinimize);

        return new BalloonRun(
            MachineHasNotificationArea: TrayShellProbe.MachineHasNotificationArea,
            ShellSawIcon: shellSawIcon,
            ClaimedAvailableWithoutIcon: withoutIcon is CapabilityState.Available,
            ClaimedAvailableWithIcon: withIcon is CapabilityState.Available,
            WithoutIconState: withoutIcon,
            WithIconState: withIcon);
    }

    /// <summary>Install a menu, dispose the presence, and ask the OS about the menu handle after.</summary>
    internal static MenuTeardownRun SetMenuThenDispose()
    {
        var presence = TrayPresenceFactory.Create();
        var fixture = new MenuFixture();

        var state = presence.SetMenu(fixture.Menu);
        var handles = HandlesOf(presence);
        var whileAlive = TrayShellProbe.ReadMenu(handles.Menu);

        presence.Dispose();

        return new MenuTeardownRun(
            WindowsHost: TrayShellProbe.WindowsHost,
            ClaimedAvailable: state is CapabilityState.Available,
            EntryCountWhileAlive: whileAlive.EntryCount,
            EntryCountAfterDispose: TrayShellProbe.ReadMenu(handles.Menu).EntryCount,
            EntryCountForAHandleThatWasNeverAMenu: TrayShellProbe.ReadMenu(0).EntryCount);
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

    internal sealed record MenuRun(
        bool WindowsHost,
        bool ClaimedAvailable,
        bool MenuHandleIsReal,
        TrayShellProbe.MenuReadBack ReadBack,
        CapabilityState State);

    internal sealed record RightClickRun(
        bool WindowsHost,
        bool MachineHasNotificationArea,
        bool ClaimedAvailableOnSetMenu,
        bool ClaimedAvailableOnPlace,
        bool ShellSawIcon,
        int TrackerInvocations,
        bool TrackerSawTheRealMenu,
        bool TrackerSawTheOwnerWindow,
        int RestoreInvocations,
        int WakeInvocations,
        int ExitInvocations,
        CapabilityState MenuState,
        CapabilityState PlaceState);

    internal sealed record BalloonRun(
        bool MachineHasNotificationArea,
        bool ShellSawIcon,
        bool ClaimedAvailableWithoutIcon,
        bool ClaimedAvailableWithIcon,
        CapabilityState WithoutIconState,
        CapabilityState WithIconState);

    internal sealed record MenuTeardownRun(
        bool WindowsHost,
        bool ClaimedAvailable,
        int EntryCountWhileAlive,
        int EntryCountAfterDispose,
        int EntryCountForAHandleThatWasNeverAMenu);
}
