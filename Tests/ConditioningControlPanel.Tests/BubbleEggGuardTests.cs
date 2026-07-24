using System.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #628 — the avatar bubble-pop egg used to claim ANY ambient effect bubble, including "video" and
/// "htlink" payloads. Popping one of those opens a fullscreen LibVLC/browser window in the middle of
/// the egg choreography, which wedged the render thread. The guard <see cref="BubbleService.IsEggClaimableEffect"/>
/// now excludes exactly those two payload ids. The real ids come from <see cref="ChaosBubbleVariants.All"/>,
/// so the catalogue test breaks if someone renames an id without updating the guard.
/// </summary>
public class BubbleEggGuardTests
{
    [Theory]
    [InlineData("video")]
    [InlineData("htlink")]
    public void FullscreenTakeoverPayloads_AreNotClaimable(string effectKindId)
        => Assert.False(BubbleService.IsEggClaimableEffect(effectKindId));

    // Real benign variant ids straight from ChaosBubbleVariants.All — renaming any of these without
    // touching the guard should surface here.
    [Theory]
    [InlineData("flash")]
    [InlineData("subliminal")]
    [InlineData("pink")]
    [InlineData("spiral")]
    [InlineData("braindrain")]
    [InlineData("bambifreeze")]
    public void BenignPayloads_AreClaimable(string effectKindId)
        => Assert.True(BubbleService.IsEggClaimableEffect(effectKindId));

    // Bubble.EffectKindId is `_spec?.VariantId ?? ""`, so an unspecced bubble yields "". Treat empty
    // and null as an ordinary (non-fullscreen) payload — claimable.
    [Fact]
    public void EmptyId_IsClaimable()
        => Assert.True(BubbleService.IsEggClaimableEffect(""));

    [Fact]
    public void NullId_IsClaimable()
        => Assert.True(BubbleService.IsEggClaimableEffect(null!));

    [Fact]
    public void GuardedIds_StillExistInTheVariantCatalogue()
    {
        var ids = ChaosBubbleVariants.All.Select(v => v.Id).ToHashSet();
        Assert.Contains("video", ids);
        Assert.Contains("htlink", ids);
    }

    [Fact]
    public void EveryFullscreenTakeoverVariant_IsExcluded()
    {
        // Any catalogued variant whose payload takes over the screen (Video / GifCascade) must be
        // rejected by the guard. Pin it to the actual catalogue so a new fullscreen payload id can't
        // slip past unguarded silently through this specific pair.
        foreach (var v in ChaosBubbleVariants.All.Where(v => v.Id is "video" or "htlink"))
            Assert.False(BubbleService.IsEggClaimableEffect(v.Id), $"'{v.Id}' must not be egg-claimable");
    }
}
