using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;
namespace ConditioningControlPanel.Tests;
public sealed class EmiTubePreviewTests
{
    [Theory]
    [InlineData(null, 1)]
    [InlineData("unknown, excited", 6)]
    [InlineData("TEASING", 2)]
    [InlineData("sad", 3)]
    [InlineData("dreamy", 7)]
    public void ExistingMoodLabelsDriveBakedPoses(string? mood, int expected)
        => Assert.Equal(expected, EmiTubePreview.PoseForMood(mood));

    [Theory]
    [InlineData(1, 0, true, 8)]
    [InlineData(3, 0, true, 8)]
    [InlineData(4, 1, true, 4)]
    [InlineData(7, 4, true, 7)]
    [InlineData(8, 0, false, 8)]
    [InlineData(3, 0, false, 3)]
    public void DefaultMigrationDoesNotReplaceOtherCharacters(int saved, int companion, bool housePreview, int expected)
        => Assert.Equal(expected, EmiTubePreview.InitialSet(saved, companion, housePreview));

    [Theory]
    [InlineData(true, 8, true, true, true)]
    [InlineData(false, 8, true, true, false)]
    [InlineData(true, 4, true, true, false)]
    [InlineData(true, 8, false, true, false)]
    [InlineData(true, 8, true, false, false)]
    public void DeskReturnsOnModChangeHideOrPopout(bool preview, int set, bool visible, bool attached, bool expected)
        => Assert.Equal(expected, EmiTubePreview.SuppressesDesk(preview, set, visible, attached));

    [Fact] public void OnlyPreviouslyVisibleDeskReturnsOnce()
    {
        var lease = new TubeDeskVisibility();
        Assert.Equal((true, false), lease.Update(true, true));
        Assert.Equal((false, false), lease.Update(true, false));
        Assert.Equal((false, true), lease.Update(false, false));
        Assert.Equal((false, false), lease.Update(false, true));
        Assert.Equal((false, false), lease.Update(true, false));
        Assert.Equal((false, false), lease.Update(false, false));
    }
    [Fact] public void ExplicitDismissalCancelsRestoration()
    {
        var lease = new TubeDeskVisibility();
        lease.Update(true, true);
        lease.Dismiss();
        Assert.Equal((false, false), lease.Update(false, false));
    }
}
