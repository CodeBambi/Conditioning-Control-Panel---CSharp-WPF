using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The "post your achievements to Discord?" offer, as pure routing.
///
/// <para>The bug this pins: the offer used to be an ownerless MessageBox fired the instant a
/// Discord link resolved, and a link can resolve minutes after the click. One player linked
/// Discord, started Down the Rabbit Hole, and got the box laid over the two doors with the door
/// he wanted underneath it (ticket 2026-09-15).</para>
/// </summary>
public class AchievementSharePromptRuleTests
{
    private static AchievementSharePromptRouting Decide(bool sharing = false, bool game = false,
        bool launcher = false, bool onScreen = true, bool asked = false)
        => AchievementSharePromptRule.Decide(sharing, game, launcher, onScreen, asked);

    [Fact]
    public void AsksWhenNothingIsInTheWay()
        => Assert.Equal(AchievementSharePromptRouting.Ask, Decide());

    [Fact]
    public void NeverAsksTwiceOnceSharingIsOn()
        => Assert.Equal(AchievementSharePromptRouting.Skip, Decide(sharing: true));

    [Fact]
    public void AGameOnScreenSendsItToTheInbox()
        => Assert.Equal(AchievementSharePromptRouting.Inbox, Decide(game: true));

    [Fact]
    public void TheLauncherHoldingTheScreenSendsItToTheInbox()
    {
        // The launcher's Sign in pill reaches the same login flow, and the panel is in the tray
        // behind it - so the dialog would be owned by a hidden window with no visible parent.
        Assert.Equal(AchievementSharePromptRouting.Inbox, Decide(launcher: true));
    }

    [Fact]
    public void APanelInTheTraySendsItToTheInbox()
    {
        // The general case behind the other two, and the one neither of them catches: the panel
        // closes to the tray, and a link can resolve minutes after the click with no game and no
        // launcher to blame. MessageBox.Show(this, ...) would own it from a hidden window.
        Assert.Equal(AchievementSharePromptRouting.Inbox, Decide(onScreen: false));
    }

    [Fact]
    public void ClickingTheInboxRowOpensItEvenWithTheGameStillUp()
    {
        // Without this the row removes itself, re-enters the rule, is told to wait again and
        // quietly re-posts itself: clicking it would do nothing at all.
        Assert.Equal(AchievementSharePromptRouting.Ask, Decide(game: true, asked: true));
        Assert.Equal(AchievementSharePromptRouting.Ask, Decide(launcher: true, asked: true));
        // Safe against the visibility check too: a row can only be clicked on a panel that is up.
        Assert.Equal(AchievementSharePromptRouting.Ask, Decide(onScreen: false, asked: true));
    }

    [Fact]
    public void AnOptedInUserIsNotGivenAnInboxRowEither()
    {
        // Skip wins over everything, the click included: a row nobody needs is still an
        // interruption, and the Inbox badge is not a place to park an answered question.
        Assert.Equal(AchievementSharePromptRouting.Skip, Decide(sharing: true, game: true));
        Assert.Equal(AchievementSharePromptRouting.Skip, Decide(sharing: true, launcher: true));
        Assert.Equal(AchievementSharePromptRouting.Skip, Decide(sharing: true, onScreen: false));
        Assert.Equal(AchievementSharePromptRouting.Skip, Decide(sharing: true, asked: true));
    }
}
