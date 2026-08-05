using System.Collections.Generic;
using ConditioningControlPanel.Services.Quiz;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #762/#798/#619: surfaces outside the flash pool used to walk the assets folder RAW, so images
/// the user had unchecked in the Assets tree still showed up (here: the Graded Intake page's media
/// manifest). The filter must match FlashService.GetMediaFiles' normalization exactly — the saved
/// blacklist entry can differ from the runtime relative path by separator and by case — and must
/// stay a pure pass-through when nothing is deselected.
/// </summary>
public class AssetDeselectionScopeTests
{
    private const string Root = @"C:\assets";

    [Fact]
    public void NothingDeselected_EverythingStaysActive()
    {
        var disabled = IntakeHostService.BuildDisabledAssetSet(null);
        Assert.True(IntakeHostService.IsAssetActive(disabled, Root, @"C:\assets\images\a.gif"));

        disabled = IntakeHostService.BuildDisabledAssetSet(new List<string>());
        Assert.True(IntakeHostService.IsAssetActive(disabled, Root, @"C:\assets\images\a.gif"));
    }

    [Fact]
    public void DeselectedFile_IsExcluded()
    {
        var disabled = IntakeHostService.BuildDisabledAssetSet(new[] { "images/nope.gif" });
        Assert.False(IntakeHostService.IsAssetActive(disabled, Root, @"C:\assets\images\nope.gif"));
        Assert.True(IntakeHostService.IsAssetActive(disabled, Root, @"C:\assets\images\yes.gif"));
    }

    [Fact]
    public void BackslashSavedEntry_StillMatches()
    {
        // The Assets tree has written both separator styles over the years.
        var disabled = IntakeHostService.BuildDisabledAssetSet(new[] { @"images\sub\nope.png" });
        Assert.False(IntakeHostService.IsAssetActive(disabled, Root, @"C:\assets\images\sub\nope.png"));
    }

    [Fact]
    public void CaseDiffers_StillMatches()
    {
        // Windows is case-insensitive at the filesystem level; a case-sensitive lookup let the
        // unchecked image slip through.
        var disabled = IntakeHostService.BuildDisabledAssetSet(new[] { "Images/NOPE.gif" });
        Assert.False(IntakeHostService.IsAssetActive(disabled, Root, @"C:\assets\images\nope.gif"));
    }

    [Fact]
    public void UnrelatedFolderEntry_DoesNotOverMatch()
    {
        var disabled = IntakeHostService.BuildDisabledAssetSet(new[] { "videos/clip.mp4" });
        Assert.True(IntakeHostService.IsAssetActive(disabled, Root, @"C:\assets\images\clip.mp4"));
    }
}
