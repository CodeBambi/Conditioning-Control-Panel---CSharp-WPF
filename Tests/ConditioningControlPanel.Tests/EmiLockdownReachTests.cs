using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// EMI has to stay reachable while a lockdown runs (ccp-bugs #1190). Two independent things made
/// her unreachable and both are pinned here:
///
/// <list type="number">
/// <item>the dock chip was an ordinary Possession Button target, so swap / dissolve / melt could
/// take it for the length of a hold - and Possession only ever runs DURING a lockdown, which is
/// why the report reads as "EMI is not available in lockdown" rather than as a haunt;</item>
/// <item>the mute prompt was a modal with no <c>Topmost</c>, so on the one path certain to raise
/// it - a summon while something is already talking - it opened behind the strict video window
/// and its nested message pump held the summon.</item>
/// </list>
///
/// <para><b>Why these are source reads.</b> Both contracts live in a WPF window this suite cannot
/// afford to realize (the same call NavRailFlyoutTests makes for the nav rail), and neither is
/// observable from a rendered tree anyway: <c>PossessionNeverNames</c> is a private static on
/// MainWindow, and a window's z-band does not show up in a static render. What can rot is the
/// wiring, so the wiring is what is pinned.</para>
/// </summary>
public class EmiLockdownReachTests
{
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

    /// <summary>The blocklist's initializer body only, so a name that appears solely in the doc
    /// comment above it cannot make any of this pass on prose.</summary>
    private static string NeverNamesBody()
    {
        var src = AppFile("MainWindow", "MainWindow.Possession.cs");
        var m = Regex.Match(
            src,
            @"PossessionNeverNames\s*=\s*new\(StringComparer\.Ordinal\)\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline);
        Assert.True(m.Success,
            "PossessionNeverNames is gone from MainWindow.Possession.cs, or it is no longer a collection initializer");
        return m.Groups["body"].Value;
    }

    [Theory]
    // The chip. It is the only route into EMI a user can SEE, and WalkPossession stops at the named
    // element, so blocking the UserControl covers the BtnChip inside it too.
    [InlineData("EmiDockChip")]
    // The neighbours it now shares the list with, named so a careless edit to the initializer
    // cannot quietly drop an exit on the way to adding the chip.
    [InlineData("TxtLockdownTimer")]
    [InlineData("TxtLockdownExit")]
    [InlineData("BtnEmergencyExit")]
    [InlineData("EERoot")]
    public void TheBlocklistHolds(string name)
        => Assert.Contains("\"" + name + "\"", NeverNamesBody(), StringComparison.Ordinal);

    /// <summary>The blocklist matches <c>x:Name</c> with <c>StringComparer.Ordinal</c>, so an entry
    /// whose element has been renamed is an entry that silently does nothing.</summary>
    [Fact]
    public void TheChipStillCarriesThatName()
    {
        var xaml = AppFile("MainWindow", "MainWindow.xaml");
        Assert.Contains("<controls:EmiDock x:Name=\"EmiDockChip\"", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mute prompt rides the same z-band as the rest of Windows/EmiDesk. Without it the dialog
    /// is merely owned by MainWindow, which during a lockdown sits under a fullscreen strict video
    /// window that is itself Topmost - so the modal is invisible and the summon waits behind it.
    /// </summary>
    [Fact]
    public void TheMutePromptIsTopmost()
    {
        var xaml = AppFile("Windows", "EmiDesk", "EmiMutePromptWindow.xaml");
        var head = xaml.Substring(0, xaml.IndexOf(">", StringComparison.Ordinal) + 1);
        Assert.Contains("Topmost=\"True\"", head, StringComparison.Ordinal);
    }

    /// <summary>
    /// And it stays a dialog about the AVATAR. A Topmost window over a mandatory video is only
    /// acceptable while it offers no way out of the lockdown, so the three answers are pinned: a
    /// fourth button here would need that z-order argument made again from scratch.
    /// </summary>
    [Fact]
    public void TheMutePromptOffersNoExit()
    {
        var code = AppFile("Windows", "EmiDesk", "EmiMutePromptWindow.xaml.cs");
        Assert.Equal(3, Regex.Matches(code, @"\bBtn\w+\.Click \+=").Count);
        // Escape is "keep the avatar", never an exit and never a mute.
        Assert.Contains("if (e.Key == Key.Escape) Answer(EmiMuteChoice.Keep);", code, StringComparison.Ordinal);
    }
}
