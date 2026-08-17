using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Tray;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-093. A tray capability is the easiest thing in this codebase to fake, because every one of
/// its failure modes is invisible: an icon that never appears, a restore that never fires, a
/// backend that swallows the call and returns success. A test that only checked the return value
/// of <c>Place</c> would certify all three.
///
/// <para><b>So the shape of every effect fact here is the same:</b> compare a fact about the
/// MACHINE (does this session have a notification area — established by
/// <see cref="TrayShellProbe"/>, independent P/Invokes, never the product's) against a fact about
/// the SHELL (does it hold this icon — <c>Shell_NotifyIcon(NIM_MODIFY)</c>), and only then
/// against the backend's CLAIM. A backend that reports Available without placing an icon fails
/// the second comparison; a backend that degenerates into always refusing fails the first.
/// Neither branch is a skip and no assertion sits behind a conditional.</para>
///
/// <para><b>What these facts do NOT prove.</b> Nothing about rendering, about the icon being
/// visible to a human, about a real mouse click, about the window leaving and returning to the
/// taskbar, about Explorer-restart recovery, or about Linux. Those are headed claims and a
/// named manual gate (SP-093 record.md).</para>
/// </summary>
public class TrayCapabilityTests
{
    [Fact]
    public void TheShellOracle_SaysNoForAnIconThatWasNeverPlaced()
    {
        // The instrument's own control. Every other fact in this file trusts NIM_MODIFY to mean
        // "the notification area holds this (hWnd, uID)". An oracle that had degenerated into
        // "always true" would silently certify a capability that places nothing — so it is asked,
        // on every run, about an id that was never added, and about a window it just destroyed.
        var control = TrayShellProbe.RunNegativeControl();

        Assert.Equal(TrayShellProbe.WindowsHost, control.OwnerWindowCreated);
        Assert.False(control.ShellHoldsNeverAddedIcon,
            "the shell existence oracle claims to hold an icon id that was never added — the instrument every "
            + "other tray fact depends on cannot say 'no', so none of them prove anything");
        Assert.False(control.OwnerWindowExistsAfterCleanup,
            "the probe's scratch owner window survived its own teardown — the window-existence check cannot say 'no'");
    }

    [Fact]
    public void PlacingTheIcon_IsConfirmedByTheShellItself_NotByTheBackendsOwnSayso()
    {
        var run = TrayObservations.PlaceThenRemove();

        Assert.True(run.ShellSawIconAfterPlace == run.MachineHasNotificationArea,
            $"this session has a notification area = {run.MachineHasNotificationArea}, but after Place the shell "
            + $"holding the icon = {run.ShellSawIconAfterPlace}. On a machine with a notification area the icon must "
            + $"really be in it; with no notification area nothing may be claimed. Backend said: "
            + $"{TrayObservations.Describe(run.PlaceState)}");
        Assert.True(run.ClaimedAvailableOnPlace == run.ShellSawIconAfterPlace,
            $"the backend claimed Available = {run.ClaimedAvailableOnPlace} while the shell's own answer to 'do you "
            + $"hold this icon' = {run.ShellSawIconAfterPlace}. A claim that outruns the effect is the exact failure "
            + $"this capability exists to prevent. Backend said: {TrayObservations.Describe(run.PlaceState)}");
        Assert.True(run.BackendReportsPlacedAfterPlace == run.ShellSawIconAfterPlace,
            $"IsPlaced = {run.BackendReportsPlacedAfterPlace} disagrees with the shell ({run.ShellSawIconAfterPlace})");
    }

    [Fact]
    public void RemovingTheIcon_TakesItOutOfTheNotificationAreaForReal()
    {
        var run = TrayObservations.PlaceThenRemove();

        Assert.False(run.ShellSawIconAfterRemove,
            $"after Remove the shell still holds the icon — it was not removed, only reported removed. Backend said: "
            + $"{TrayObservations.Describe(run.RemoveState)}");
        Assert.False(run.BackendReportsPlacedAfterRemove);
        Assert.True(run.ClaimedAvailableOnRemove == run.MachineHasNotificationArea,
            $"Remove claimed Available = {run.ClaimedAvailableOnRemove} on a machine whose notification area presence "
            + $"is {run.MachineHasNotificationArea}; with nothing ever placed there is nothing to succeed at. Backend "
            + $"said: {TrayObservations.Describe(run.RemoveState)}");
    }

    [Fact]
    public void DisposingThePresence_LeavesNoIconAndNoOwnerWindowBehind()
    {
        // Teardown without an explicit Remove: the path a crash-adjacent shutdown takes. A leaked
        // icon is a ghost the user can click into a dead process; a leaked owner window is an
        // invisible top-level window for the life of the process.
        var run = TrayObservations.PlaceThenDispose();

        Assert.True(run.ShellSawIconWhilePlaced == run.MachineHasNotificationArea,
            $"placement was not real before the dispose leg started (notification area = "
            + $"{run.MachineHasNotificationArea}, shell held icon = {run.ShellSawIconWhilePlaced})");
        Assert.False(run.ShellSawIconAfterDispose,
            "Dispose left the icon in the notification area — a ghost icon pointing at a torn-down presence");
        Assert.False(run.OwnerWindowExistsAfterDispose,
            "Dispose left the hidden owner window alive");
        Assert.Null(run.TeardownDiagnostic);
    }

    [Fact]
    public void TheShellsClickNotification_BecomesAnApplicationActivationEvent()
    {
        // The icon is only worth placing if it is the user's way back. This posts the exact
        // message the shell posts on a left-click (WPF admits single-click deliberately —
        // TrayIconService.cs:113-119) and pumps the owner window's queue. It proves the ROUTING.
        // It does not prove a human click landing on a visible icon: that is a headed gate.
        var run = TrayObservations.PlaceThenSyntheticClick();

        Assert.True(run.ShellSawIcon == run.MachineHasNotificationArea,
            $"the click leg needs a really-placed icon first (notification area = "
            + $"{run.MachineHasNotificationArea}, shell held icon = {run.ShellSawIcon})");
        Assert.True((run.ActivationsRaised > 0) == run.ShellSawIcon,
            $"a placed icon must turn the shell's click notification into exactly one Activated event; icon placed = "
            + $"{run.ShellSawIcon}, activations raised = {run.ActivationsRaised}");
        Assert.True(run.ActivationsRaised <= 1,
            $"one click notification raised {run.ActivationsRaised} Activated events");
    }

    [Fact]
    public void TheRefusingBackend_ReportsAReasonAndNeverClaimsAnIconIsPlaced()
    {
        using var presence = new UnsupportedTrayPresence(
            TrayReasonCodes.TrayMechanismAbsent, "no backend for this platform in this build");

        var place = presence.Place(TrayIconRequest.Default);
        var remove = presence.Remove();

        var placeRefusal = Assert.IsType<CapabilityState.Unavailable>(place);
        var removeRefusal = Assert.IsType<CapabilityState.Unavailable>(remove);
        Assert.Equal(TrayReasonCodes.TrayMechanismAbsent, placeRefusal.Reason.Code);
        Assert.Equal(TrayReasonCodes.TrayMechanismAbsent, removeRefusal.Reason.Code);
        Assert.NotEmpty(placeRefusal.Reason.Detail);
        // Remove refusing too is the point: a "successful" removal of an icon that was never
        // placed is a success claim for work that did not happen.
        Assert.False(presence.IsPlaced);
    }

    [Fact]
    public void TheLinuxSelection_RefusesWithTheMechanismAbsentCode_AndCarriesTheManualGate()
    {
        using var presence = TrayPresenceFactory.CreateFor(TrayHostPlatform.Linux);

        var state = presence.Place(TrayIconRequest.Default);

        Assert.IsType<UnsupportedTrayPresence>(presence);
        var refusal = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(TrayReasonCodes.TrayMechanismAbsent, refusal.Reason.Code);
        Assert.False(presence.IsPlaced);
        // The refusal must name the route AND the gate that would settle it — a bare "not
        // supported" is how a platform seam gets quietly forgotten.
        Assert.Contains("StatusNotifierItem", refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("Wayland", refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("MANUAL GATE", refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("RegisteredStatusNotifierItems", refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains(TrayPresenceFactory.LinuxManualGate, refusal.Reason.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBackendSelection_GivesTheRealOneOnlyToTheOnePlatformThisBuildCanDrive()
    {
        using var windows = TrayPresenceFactory.CreateFor(TrayHostPlatform.Windows);
        using var linux = TrayPresenceFactory.CreateFor(TrayHostPlatform.Linux);
        using var macOs = TrayPresenceFactory.CreateFor(TrayHostPlatform.MacOs);
        using var unknown = TrayPresenceFactory.CreateFor(TrayHostPlatform.Unknown);

        Assert.IsType<Win32TrayPresence>(windows);
        Assert.IsType<UnsupportedTrayPresence>(linux);
        Assert.IsType<UnsupportedTrayPresence>(macOs);
        Assert.IsType<UnsupportedTrayPresence>(unknown);
        // Selecting a backend is not availability: the Win32 one starts out claiming nothing and
        // only earns Available from an exercised shell call (runtime-capability-contract §2 rule 2).
        Assert.False(windows.IsPlaced);
    }

    [Fact]
    public void TheTooltip_IsClampedToTheShellsBudgetBeforeItReachesTheMarshaller()
    {
        // szTip is WCHAR[128]. An over-long tooltip must be cut here, visibly, rather than
        // discovered as a marshalling exception or a silently mangled string at the boundary.
        var request = new TrayIconRequest(new string('x', TrayIconRequest.ToolTipBudget + 40));

        Assert.Equal(TrayIconRequest.ToolTipBudget, request.ToolTip.Length);
        Assert.True(request.ToolTipWasClamped);
        Assert.False(TrayIconRequest.Default.ToolTipWasClamped);
        Assert.Equal("Conditioning Control Panel", TrayIconRequest.Default.ToolTip);
    }
}
