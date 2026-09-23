using ConditioningControlPanel.Services.Homework;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The "Homework due" card waits for an idle moment and never lands on a busy user.</summary>
public class HomeworkGateRuleTests
{
    private static readonly HomeworkGateInputs Idle = new(Due: true, SessionRunning: false, GameUp: false,
        LockCardOpen: false, FullscreenEffect: false, ModalUp: false, PanelAway: false, Watching: false);

    [Fact]
    public void DueAndIdleShowsTheCard() => Assert.True(HomeworkGateRule.ShouldShow(Idle));

    [Fact]
    public void NothingDueShowsNothing() => Assert.False(HomeworkGateRule.ShouldShow(Idle with { Due = false }));

    [Fact]
    public void EveryBusyStateHoldsTheCardBack()
    {
        Assert.False(HomeworkGateRule.ShouldShow(Idle with { SessionRunning = true }));
        Assert.False(HomeworkGateRule.ShouldShow(Idle with { GameUp = true }));
        Assert.False(HomeworkGateRule.ShouldShow(Idle with { LockCardOpen = true }));
        Assert.False(HomeworkGateRule.ShouldShow(Idle with { FullscreenEffect = true }));
        Assert.False(HomeworkGateRule.ShouldShow(Idle with { ModalUp = true }));
        Assert.False(HomeworkGateRule.ShouldShow(Idle with { PanelAway = true }));
        Assert.False(HomeworkGateRule.ShouldShow(Idle with { Watching = true }));
    }

    [Fact]
    public void DueIsTheServersWord_EnabledOptedInPickedAndNotHandedIn()
    {
        var hw = new HomeworkCurrent("2026-09-23", "https://hypnotube.com/video/a-1.html", "A");
        Assert.True(new HomeworkToday(true, true, true, hw, false).Due);
        Assert.False(new HomeworkToday(false, true, true, hw, false).Due);
        Assert.False(new HomeworkToday(true, false, true, hw, false).Due);
        Assert.False(new HomeworkToday(true, true, true, null, false).Due);
        Assert.False(new HomeworkToday(true, true, true, hw, true).Due);
        // Discord is only the censor's half. Without it the card still asks.
        Assert.True(new HomeworkToday(true, true, false, hw, false).Due);
    }
}
