using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE RAIL LETS GO. Cover for the v6.9.5 report "the left nav rail stops minimizing" (Nardda;
/// reproduced by Wobberjockey, who noted it "worked after restarting the app" - the signature of a
/// process-lifetime latch, not a layout bug).
///
/// <para><b>The bug.</b> 6.9.4 gave the rail rows a right-click pin menu, and it took the rail's
/// "stay open" claim in <c>ContextMenuOpening</c> while releasing it in the menu's <c>Closed</c>.
/// ContextMenuOpening is a REQUEST to show a menu and fires on every right-click WPF routes to the
/// element; Closed is only raised for a menu that actually opened, because WPF opens by setting
/// <c>IsOpen = true</c> and that is a no-op on a menu that is already open. One unmatched request
/// left the claim count at one, every collapse trigger in MainWindow.NavRail.cs bails out on a
/// standing claim first, and the flyout sat over the dashboard for the rest of the session. Until
/// 6.9.4 the only caller of the hold was the offscreen door shooter, which shuts the app down when
/// it is finished - which is why this arrived with the Favorites rail and not before.</para>
///
/// <para>Two halves are pinned here. The LATCH and the WATCHDOG RULE are real classes and get real
/// unit tests. The WIRING - which event the hold hangs off, that the watchdog is armed on window
/// deactivation - is a source read for the same reason NavRailFlyoutTests is: realizing MainWindow
/// in this suite is not affordable, and "the hold is taken on the wrong event" looks identical in
/// a rendered tree to the fix.</para>
/// </summary>
public class NavRailHoldWatchdogTests
{
    // ============================== the latch ==============================

    [Fact]
    public void AnOwnersSecondClaimIsTheSameClaim()
    {
        var latch = new NavRailHoldLatch();
        var menu = new object();

        Assert.True(latch.Take(menu));      // new claim: the caller raises the rail
        Assert.False(latch.Take(menu));     // the repeat that used to leak
        Assert.False(latch.Take(menu));
        Assert.Equal(1, latch.Count);

        // ...and ONE release still hands the rail back, however many requests arrived.
        Assert.True(latch.Release(menu));
        Assert.Equal(0, latch.Count);
        Assert.False(latch.Held);
    }

    [Fact]
    public void TwoHoldersMeanTheFirstOneOutFreesNothing()
    {
        var latch = new NavRailHoldLatch();
        var tutorial = new object();
        var menu = new object();

        Assert.True(latch.Take(tutorial));
        Assert.True(latch.Take(menu));
        Assert.Equal(2, latch.Count);

        // The whole reason the rail counts instead of holding a bool.
        Assert.False(latch.Release(tutorial));
        Assert.True(latch.Held);
        Assert.True(latch.Holds(menu));

        Assert.True(latch.Release(menu));
        Assert.False(latch.Held);
    }

    [Fact]
    public void AReleaseFromSomebodyWhoHoldsNothingCannotFreeSomebodyElse()
    {
        var latch = new NavRailHoldLatch();
        var menu = new object();
        latch.Take(menu);

        // A Closed with no Opened in front of it, and a stray release after the real one.
        Assert.False(latch.Release(new object()));
        Assert.False(latch.Release(null));
        Assert.Equal(1, latch.Count);

        Assert.True(latch.Release(menu));
        Assert.False(latch.Release(menu));   // the old int would have gone to -1
        Assert.Equal(0, latch.Count);
    }

    [Fact]
    public void ClearReportsTheClaimsItHadToTakeAway()
    {
        var latch = new NavRailHoldLatch();
        latch.Take(new object());
        latch.Take(new object());

        // The watchdog logs this number: anything above zero is a caller that never released.
        Assert.Equal(2, latch.Clear());
        Assert.False(latch.Held);
        Assert.Equal(0, latch.Clear());
    }

    // ============================== the watchdog rule ==============================

    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(1500);

    [Fact]
    public void AnOpenRailNobodyIsNearIsCollapsedOnceTheGraceIsUp()
    {
        Assert.True(NavRailWatchdogRule.ShouldForceCollapse(
            ready: true, expanded: true, popupOpen: false, pointerAway: true,
            awayFor: Grace, grace: Grace));
        Assert.True(NavRailWatchdogRule.ShouldForceCollapse(
            ready: true, expanded: true, popupOpen: false, pointerAway: true,
            awayFor: TimeSpan.FromSeconds(30), grace: Grace));
    }

    [Theory]
    // The rail never finished wiring itself - it is as MainWindow.xaml authored it.
    [InlineData(false, true, false, true, 5000)]
    // Already shut.
    [InlineData(true, false, false, true, 5000)]
    // A pin menu is genuinely on screen: that hold is doing its job.
    [InlineData(true, true, true, true, 5000)]
    // The pointer is still on the flyout.
    [InlineData(true, true, false, false, 5000)]
    // Away, but not for long enough - a pointer crossing the rail's edge and coming back takes
    // tens of milliseconds, and collapsing under it would be the flicker this rail already fixed.
    [InlineData(true, true, false, true, 1400)]
    public void EveryReasonToLeaveTheRailAloneIsHonoured(
        bool ready, bool expanded, bool popupOpen, bool pointerAway, int awayMs)
    {
        Assert.False(NavRailWatchdogRule.ShouldForceCollapse(
            ready, expanded, popupOpen, pointerAway,
            TimeSpan.FromMilliseconds(awayMs), Grace));
    }

    // ============================== the wiring ==============================

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string AppFile(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine("ConditioningControlPanel", Path.Combine(parts))));

    /// <summary>Source with every comment line dropped: these files EXPLAIN the bug at length, and
    /// the explanation must not be what satisfies the assertions.</summary>
    private static string CodeOf(params string[] parts)
        => string.Join("\n", Array.FindAll(
            AppFile(parts).Split('\n'), l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Fact]
    public void ThePinMenuTakesItsHoldOnAnOpenNotOnARequestToOpen()
    {
        var code = CodeOf("MainWindow", "MainWindow.FavoritesRail.cs");

        var attach = code.Substring(code.IndexOf("private void AttachPinMenu", StringComparison.Ordinal));
        Assert.Contains("menu.Opened", attach, StringComparison.Ordinal);
        Assert.Contains("HoldNavRailOpen(menu)", attach, StringComparison.Ordinal);
        Assert.Contains("ReleaseNavRailOpen(menu)", attach, StringComparison.Ordinal);

        // THE REGRESSION: the hold must not sit inside the ContextMenuOpening handler again.
        var opening = attach.Substring(attach.IndexOf("ContextMenuOpening", StringComparison.Ordinal));
        opening = opening.Substring(0, opening.IndexOf("menu.Opened", StringComparison.Ordinal));
        Assert.DoesNotContain("HoldNavRailOpen", opening, StringComparison.Ordinal);

        // And the menu is known to the rail, so the watchdog can tell a live popup from a stale hold.
        Assert.Contains("RegisterNavRailPopup(menu)", attach, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRailKeepsNoBareHoldCounterAnyMore()
    {
        var code = CodeOf("MainWindow", "MainWindow.NavRail.cs");

        // The int is deleted, not merely unused: it is what a keyed latch cannot be talked back into.
        Assert.DoesNotContain("_navRailHoldCount", code, StringComparison.Ordinal);
        Assert.Contains("NavRailHoldLatch", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWatchdogIsArmedAndItsGraceIsSane()
    {
        var code = CodeOf("MainWindow", "MainWindow.NavRail.cs");

        // A clock, and the two events that mean "nobody is using this rail".
        Assert.Contains("_navRailWatchdog", code, StringComparison.Ordinal);
        Assert.Contains("NavRailWatchdogRule.ShouldForceCollapse", code, StringComparison.Ordinal);
        Assert.Contains("Deactivated +=", code, StringComparison.Ordinal);
        Assert.Contains("ForceCollapseNavRail", code, StringComparison.Ordinal);

        // It drops the latches rather than politely asking them to collapse.
        var force = code.Substring(code.IndexOf("private void ForceCollapseNavRail", StringComparison.Ordinal));
        Assert.Contains("_navRailHolds.Clear()", force, StringComparison.Ordinal);

        // Long enough not to race a hover, short enough that a stuck rail is not a wait.
        var grace = ConstValue(AppFile("MainWindow", "MainWindow.NavRail.cs"), "NavRailWatchdogGraceMs");
        Assert.InRange(grace, 500, 5000);
        var tick = ConstValue(AppFile("MainWindow", "MainWindow.NavRail.cs"), "NavRailWatchdogTickMs");
        Assert.InRange(tick, 50, grace);
    }

    [Fact]
    public void TheLastReleaseAsksTheCursorRatherThanIsMouseOver()
    {
        var code = CodeOf("MainWindow", "MainWindow.NavRail.cs");
        var start = code.IndexOf("internal void ReleaseNavRailOpen(object owner)", StringComparison.Ordinal);
        Assert.True(start > 0, "ReleaseNavRailOpen(object) is gone - re-read the rail, then fix this scrape");
        var end = code.IndexOf("ReleaseNavRailOpen()", start, StringComparison.Ordinal);
        Assert.True(end > start, "the unkeyed release no longer follows the keyed one - fix this scrape");
        var release = code.Substring(start, end - start);

        // IsMouseOver reports TRUE while the element's own ContextMenu popup has the pointer, so
        // the pin menu released its hold and then declined to collapse - and the MouseLeave it
        // handed the rail to was never raised, because WPF thought the pointer had never left.
        Assert.DoesNotContain("IsMouseOver", release, StringComparison.Ordinal);
        Assert.Contains("NavRailPointerIsAway()", release, StringComparison.Ordinal);
    }

    private static double ConstValue(string src, string name)
    {
        var m = Regex.Match(src, @"const\s+(?:int|double)\s+" + Regex.Escape(name) + @"\s*=\s*(-?[\d.]+)");
        Assert.True(m.Success, name + " is gone from MainWindow.NavRail.cs");
        return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
