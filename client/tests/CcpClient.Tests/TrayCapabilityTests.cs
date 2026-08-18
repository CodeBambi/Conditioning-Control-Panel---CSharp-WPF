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
[Collection(nameof(RealDesktopCollection))]
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

    // ---------- SP-096: the menu that makes the icon worth clicking ----------

    [Fact]
    public void TheMenuTheOperatingSystemHolds_IsTheOneThatWasAskedFor()
    {
        // The menu's oracle is USER32 itself, read through TrayShellProbe's own second copy of the
        // declarations. "SetMenu returned Available" and "the OS holds four entries with these
        // labels and these ids" are two different facts, and only the second one is worth having.
        var run = TrayObservations.SetMenuThenReadItBack();

        Assert.True(run.ClaimedAvailable == run.WindowsHost,
            $"SetMenu claimed Available = {run.ClaimedAvailable} on a Windows host = {run.WindowsHost}. Backend said: "
            + TrayObservations.Describe(run.State));
        Assert.True(run.MenuHandleIsReal == run.WindowsHost,
            $"a menu handle exists = {run.MenuHandleIsReal} on a Windows host = {run.WindowsHost}");
        Assert.Equal(run.WindowsHost ? 4 : -1, run.ReadBack.EntryCount);

        if (!run.WindowsHost)
        {
            // Off Windows the oracle's -1 above IS the whole fact: nothing was built, and the
            // backend refused rather than claiming a menu it has no mechanism for.
            var refusal = Assert.IsType<CapabilityState.Unavailable>(run.State);
            Assert.Equal(TrayReasonCodes.TrayMechanismAbsent, refusal.Reason.Code);
            return;
        }

        // WPF's four entries, in WPF's order (TrayIconService.cs:98,103,107,109).
        Assert.Equal("Show Dashboard", run.ReadBack.Entries[0].Label);
        Assert.Equal("Wake Up!", run.ReadBack.Entries[1].Label);
        Assert.True(run.ReadBack.Entries[2].IsSeparator,
            "entry 2 must be WPF's ToolStripSeparator (TrayIconService.cs:107) and the OS does not report MFT_SEPARATOR for it");
        Assert.Equal(string.Empty, run.ReadBack.Entries[2].Label);
        Assert.Equal("Exit", run.ReadBack.Entries[3].Label);

        // Ids: distinct, and never 0 — TrackPopupMenu(TPM_RETURNCMD) reports a DISMISSED menu as 0,
        // so an entry with id 0 would be indistinguishable from the user pressing Escape.
        var commandIds = new[] { run.ReadBack.Entries[0].Id, run.ReadBack.Entries[1].Id, run.ReadBack.Entries[3].Id };
        Assert.DoesNotContain(0u, commandIds);
        Assert.Equal(3, commandIds.Distinct().Count());
        Assert.Equal(0u, run.ReadBack.Entries[2].Id);
    }

    [Fact]
    public void TheShellsRightClickNotification_ReachesTheRealMenu_AndTheChosenEntryRunsItsAction()
    {
        // The way back that SP-094 did not have. This posts the exact message the shell posts on a
        // right-click over the icon and pumps the owner window's queue, so the REAL window proc
        // runs; the tracker seam stands in ONLY for TrackPopupMenu's modal loop, is handed the REAL
        // OS-held menu handle, and answers with the id the OS itself reports for "Show Dashboard".
        // What stays unproven is exactly that modal loop and a human's hand — a headed gate.
        var run = TrayObservations.PlaceThenSyntheticRightClick();

        Assert.True(run.ClaimedAvailableOnSetMenu == run.WindowsHost,
            $"the menu leg needs a really-built menu first. Backend said: {TrayObservations.Describe(run.MenuState)}");
        Assert.True(run.ShellSawIcon == run.MachineHasNotificationArea,
            $"the right-click leg needs a really-placed icon first (notification area = "
            + $"{run.MachineHasNotificationArea}, shell held icon = {run.ShellSawIcon}). Backend said: "
            + TrayObservations.Describe(run.PlaceState));
        Assert.True((run.TrackerInvocations > 0) == run.ShellSawIcon,
            $"a placed icon must turn the shell's right-click notification into exactly one menu tracking; icon "
            + $"placed = {run.ShellSawIcon}, trackings = {run.TrackerInvocations}");
        Assert.True(run.TrackerInvocations <= 1,
            $"one right-click notification tracked the menu {run.TrackerInvocations} times");
        Assert.Equal(run.ShellSawIcon, run.TrackerSawTheRealMenu);
        Assert.Equal(run.ShellSawIcon, run.TrackerSawTheOwnerWindow);

        // The dispatch: the chosen id ran the RESTORE entry and nothing else. A menu that ran the
        // wrong entry, or every entry, would be worse than no menu at all.
        Assert.Equal(run.ShellSawIcon ? 1 : 0, run.RestoreInvocations);
        Assert.Equal(0, run.WakeInvocations);
        Assert.Equal(0, run.ExitInvocations);
    }

    [Fact]
    public void AMenuThatCannotBringTheWindowBack_IsRefusedWhereItIsBuilt()
    {
        // The rule SP-094's refusal turns on, made structural. Every shape below is a menu that
        // leaves a user with a right-click and no way home.
        static TrayMenuItem Wake() => TrayMenuItem.Command("wake", "Wake Up!", () => { });
        static TrayMenuItem Show() => TrayMenuItem.Restore("show", "Show Dashboard", () => { });

        Assert.Throws<ArgumentException>(() => new TrayMenu([Wake()]));
        Assert.Throws<ArgumentException>(() => new TrayMenu([Show(), TrayMenuItem.Restore("show2", "Show", () => { })]));
        Assert.Throws<ArgumentException>(() => new TrayMenu([TrayMenuItem.Separator()]));
        Assert.Throws<ArgumentException>(() => new TrayMenu([Show(), TrayMenuItem.Command("show", "Other", () => { })]));

        // And an entry with no label or no action never exists in the first place.
        Assert.Throws<ArgumentException>(() => TrayMenuItem.Command("wake", "   ", () => { }));
        Assert.Throws<ArgumentNullException>(() => TrayMenuItem.Restore("show", "Show Dashboard", null!));

        var menu = new TrayMenu([Show(), Wake(), TrayMenuItem.Separator(), TrayMenuItem.Command("exit", "Exit", () => { })]);
        Assert.Equal("Show Dashboard", menu.RestoreItem.Label);
        Assert.Equal(4, menu.Items.Count);
    }

    [Fact]
    public void TheFirstMinimizeBalloon_CarriesWpfsCode_NotWpfsComment()
    {
        // MainWindow/MainWindow.RemoteControl.cs:1515 says "No notification" about the DTRH tuck.
        // The call it documents reaches MinimizeToTray, whose FIRST-EVER invocation in the process
        // fires this balloon (TrayIconService.cs:150-157). The code is what a user experiences.
        var balloon = TrayNotification.FirstMinimize;

        Assert.Equal("Conditioning Control Panel", balloon.Title);
        Assert.Equal("Running in background. Click the tray icon to restore.", balloon.Message);
        Assert.Equal(2000, balloon.Timeout.TotalMilliseconds);
        Assert.False(balloon.WasClamped);

        // szInfoTitle is WCHAR[64] and szInfo is WCHAR[256]; both are cut here, visibly, rather than
        // discovered as a marshalling throw at the boundary (the TrayIconRequest rule, second surface).
        var over = new TrayNotification(
            new string('t', TrayNotification.TitleBudget + 10),
            new string('m', TrayNotification.MessageBudget + 10),
            TimeSpan.FromSeconds(1));
        Assert.Equal(TrayNotification.TitleBudget, over.Title.Length);
        Assert.Equal(TrayNotification.MessageBudget, over.Message.Length);
        Assert.True(over.WasClamped);
    }

    [Fact]
    public void ABalloonWithNoPlacedIcon_IsRefusedRatherThanClaimed()
    {
        var run = TrayObservations.BalloonWithoutThenWithAnIcon();

        Assert.False(run.ClaimedAvailableWithoutIcon,
            "a balloon was claimed with no icon placed — there is nothing for the shell to attach one to. Backend "
            + "said: " + TrayObservations.Describe(run.WithoutIconState));
        var refusal = Assert.IsType<CapabilityState.Unavailable>(run.WithoutIconState);
        Assert.Contains(refusal.Reason.Code, new[]
        {
            TrayReasonCodes.TrayNotificationWithoutIcon,
            TrayReasonCodes.TrayMechanismAbsent,
        });
        Assert.True(run.ClaimedAvailableWithIcon == run.ShellSawIcon,
            $"with the shell holding the icon = {run.ShellSawIcon}, the balloon request claimed Available = "
            + $"{run.ClaimedAvailableWithIcon}. Backend said: " + TrayObservations.Describe(run.WithIconState));

        if (run.ClaimedAvailableWithIcon)
        {
            // The honest ceiling, kept in the words the user of this capability reads.
            var available = Assert.IsType<CapabilityState.Available>(run.WithIconState);
            Assert.Contains("headed claim", available.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRefusingBackend_RefusesTheMenuAndTheBalloonToo()
    {
        // The branch every non-Windows run gets. It matters that SetMenu refuses: the shell places
        // an icon only after the menu is confirmed, so this refusal is what stops a Linux run from
        // ever reaching a placement it could not honour.
        using var presence = TrayPresenceFactory.CreateFor(TrayHostPlatform.Linux);
        var menu = new TrayMenu([TrayMenuItem.Restore("show", "Show Dashboard", () => { })]);

        var menuState = presence.SetMenu(menu);
        var balloonState = presence.ShowNotification(TrayNotification.FirstMinimize);

        var menuRefusal = Assert.IsType<CapabilityState.Unavailable>(menuState);
        var balloonRefusal = Assert.IsType<CapabilityState.Unavailable>(balloonState);
        Assert.Equal(TrayReasonCodes.TrayMechanismAbsent, menuRefusal.Reason.Code);
        Assert.Equal(TrayReasonCodes.TrayMechanismAbsent, balloonRefusal.Reason.Code);
        Assert.Contains(TrayPresenceFactory.LinuxManualGate, menuRefusal.Reason.Detail, StringComparison.Ordinal);
        Assert.False(presence.IsPlaced);
    }

    [Fact]
    public void DisposingThePresence_DestroysTheMenu_AndTheMenuOracleCanSayNo()
    {
        // A leaked HMENU is a kernel object held for the process lifetime, and — the reason this is
        // a fact rather than hygiene — the oracle every menu assertion depends on must be able to
        // answer "that is not a menu". It is asked here about a destroyed handle and about a handle
        // that was never a menu at all.
        var run = TrayObservations.SetMenuThenDispose();

        Assert.Equal(run.WindowsHost ? 4 : -1, run.EntryCountWhileAlive);
        Assert.Equal(-1, run.EntryCountAfterDispose);
        Assert.Equal(-1, run.EntryCountForAHandleThatWasNeverAMenu);
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
