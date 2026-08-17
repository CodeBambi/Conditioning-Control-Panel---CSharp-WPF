using ConditioningControlPanel.Services;
using Xunit;
using static ConditioningControlPanel.Services.CornerGifService;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The standalone corner-GIF overlay could hard-freeze the app (#954 one slot, #958 the second
/// slot), and the freeze then reproduced on every subsequent launch because the enabled slots were
/// replayed from settings.json. Both reporters had to end the process and hand-edit settings.json;
/// a reinstall did not help, because it keeps settings.
///
/// The escape hatch is a sentinel file that is armed for as long as an overlay is on screen and
/// cleared only when the last one is torn down (or on a clean exit). Its predecessor cleared one
/// dispatcher pass after Show() returned - but the GIF load is asynchronous, so the render thread
/// wedged AFTER the all-clear and the flag never survived to the next launch. These tests lock the
/// startup rule that the flag now feeds.
/// </summary>
public class CornerGifRestoreTests
{
    [Fact]
    public void SurvivingSentinel_WithEnabledSlots_ForcesThemOff()
    {
        // The brick-breaker: the previous run died with an overlay up, so this launch starts clean
        // instead of replaying the wedge.
        Assert.Equal(RestoreAction.ForceDisable,
            ResolveRestoreAction(sentinelSurvived: true, anyEnabled: true));
    }

    [Fact]
    public void CleanPreviousRun_RestoresTheUsersSlots()
    {
        // No sentinel means the last run tore its overlays down properly; the feature still works.
        Assert.Equal(RestoreAction.Restore,
            ResolveRestoreAction(sentinelSurvived: false, anyEnabled: true));
    }

    [Fact]
    public void SurvivingSentinel_WithNothingEnabled_IsStaleBookkeeping()
    {
        // The user already turned the slots off. Nothing to disable, and nothing to warn them about
        // - a "we turned your corner GIFs off" toast here would name a feature they are not using.
        Assert.Equal(RestoreAction.Nothing,
            ResolveRestoreAction(sentinelSurvived: true, anyEnabled: false));
    }

    [Fact]
    public void NothingEnabled_DoesNothing()
    {
        Assert.Equal(RestoreAction.Nothing,
            ResolveRestoreAction(sentinelSurvived: false, anyEnabled: false));
    }
}
