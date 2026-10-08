using System.Linq;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The launcher's card table and lifecycle rules, shared by the WPF and Avalonia hosts.</summary>
public class LauncherRulesTests
{
    [Fact]
    public void Cards_KeepLauncherOrderAndIds()
    {
        // The ids are --game arguments and shortcut targets: never renamed, never reordered by accident.
        Assert.Equal(new[] { "backroom", "breakoutdemo", "breakout", "piecebypiece", "race", "dtrh", "arcademy", "goon", "intake" },
            LauncherCards.All.Select(c => c.Id));
        Assert.Equal("launcher_game_intake_title", LauncherCards.Find(" INTAKE ")!.TitleKey);
        Assert.Null(LauncherCards.Find("panel"));
        // Only the free demo and Piece by Piece open signed out.
        Assert.Equal(new[] { "breakoutdemo", "piecebypiece" }, LauncherCards.All.Where(c => !c.RequiresAccount).Select(c => c.Id));
        var race = LauncherCards.Find("race")!;
        Assert.Equal((0xFF, 0xB3, 0x6B), (race.R, race.G, race.B));
    }

    [Fact]
    public void Boot_KnowsTheCardIdsWithoutAHead()
    {
        Assert.Equal(BootDecision.GameFirst("race"), LauncherBoot.Decide(new[] { "--game", "race" }, true, true, false));
        Assert.Equal(BootDecision.LauncherFirst, LauncherBoot.Decide(new[] { "--game", "nope" }, true, true, false));
    }

    [Theory]
    [InlineData(true, false, false, true, LauncherCloseOutcome.Veto)]
    [InlineData(true, true, true, true, LauncherCloseOutcome.Veto)]
    [InlineData(false, true, false, true, LauncherCloseOutcome.Hide)]
    [InlineData(false, false, true, true, LauncherCloseOutcome.Hide)]
    [InlineData(false, false, false, false, LauncherCloseOutcome.Hide)]
    [InlineData(false, false, false, true, LauncherCloseOutcome.Exit)]
    public void Close_VetoesUnderLockdown_HidesWhileAnythingIsUp_ElseExits(
        bool lockdown, bool running, bool panelUp, bool panelExists, LauncherCloseOutcome expected)
        => Assert.Equal(expected, LauncherRules.Close(lockdown, running, panelUp, panelExists));

    [Theory]
    [InlineData(true, true, true, LauncherGameStep.SignIn)]
    [InlineData(false, true, true, LauncherGameStep.Refuse)]
    [InlineData(false, false, true, LauncherGameStep.Leash)]
    [InlineData(false, false, false, LauncherGameStep.Launch)]
    public void Game_ChecksAccountThenLockThenLeash(bool needsAccount, bool locked, bool leash, LauncherGameStep expected)
        => Assert.Equal(expected, LauncherRules.Game(needsAccount, locked, leash));

    [Theory]
    // 9 tiles at the default 1280x800: the overflow is added, under a 1080p work area.
    [InlineData(800, 658, 852, 1032, 995)]
    // A short screen (125% at 1080p): capped at the work area, the scroller stays.
    [InlineData(800, 658, 852, 826, 826)]
    // Already fits: unchanged.
    [InlineData(800, 658, 568, 1032, 800)]
    // Never shrinks, even if the work area reads smaller than the window.
    [InlineData(800, 658, 852, 700, 800)]
    public void Window_grows_to_seat_every_tile_row(double window, double available, double grid, double workArea, double expected)
        => Assert.Equal(expected, LauncherGridLayout.FitWindowHeight(window, available, grid, workArea));

    [Fact]
    public void FadeIsTheTailOfTheBeat()
    {
        Assert.Equal(230, LauncherRules.FadeLeadMs(450));
        Assert.Equal(0, LauncherRules.FadeLeadMs(100));
        Assert.Equal(LauncherRules.MaxHideDelayMs - LauncherRules.FadeOutMs, LauncherRules.FadeLeadMs(99999));
        Assert.Equal(0, LauncherRules.ClampHideDelay(-5));
    }
}
