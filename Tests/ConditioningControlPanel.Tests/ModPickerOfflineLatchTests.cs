using ConditioningControlPanel;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The mod picker is a ONE-SHOT offer, and for a modular upgrader it is the only screen that hands
/// their stripped mod media back. Latching it on a launch with no network burned that offer
/// forever. These cover the two guards that fix it, and the bound that keeps the retry from turning
/// into an every-launch popup for someone deliberately offline.
/// </summary>
public class ModPickerOfflineLatchTests
{
    // ---- guard 1: don't even open a dead picker ----

    [Fact]
    public void Online_Opens()
        => Assert.False(ModPickerDialog.ShouldDeferForOffline(
            offlineMode: false, manifestUnavailable: false, offlineOffers: 0));

    [Fact]
    public void OfflineMode_DefersWithoutSpendingTheOffer()
        => Assert.True(ModPickerDialog.ShouldDeferForOffline(
            offlineMode: true, manifestUnavailable: false, offlineOffers: 0));

    [Fact]
    public void ManifestAlreadyFailedThisSession_Defers()
        => Assert.True(ModPickerDialog.ShouldDeferForOffline(
            offlineMode: false, manifestUnavailable: true, offlineOffers: 0));

    [Fact]
    public void AllowanceSpent_StopsDeferring_SoTheFlagCanFinallyLatch()
        => Assert.False(ModPickerDialog.ShouldDeferForOffline(
            offlineMode: true, manifestUnavailable: true,
            offlineOffers: ModPickerDialog.MaxOfflineOffers));

    // ---- guard 2: hand the offer back after a showing that could not download ----

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]   // MaxOfflineOffers reached — latch and let the Mod Manager take over
    [InlineData(9, false)]
    public void ReArm_OnlyWhileTheAllowanceLasts(int offersAfterShowing, bool expected)
        => Assert.Equal(expected, ModPickerDialog.ShouldReArmAfterOfflineShowing(offersAfterShowing));

    [Fact]
    public void TheLoopTerminates()
    {
        // Walk the worst case: no network, ever, and nothing pre-detected it. Every launch opens
        // the picker, it ends offline, we count it. This must stop.
        var offers = 0;
        var launches = 0;
        while (!ModPickerDialog.ShouldDeferForOffline(false, false, offers)
               && ModPickerDialog.ShouldReArmAfterOfflineShowing(offers + 1))
        {
            offers++;
            launches++;
            Assert.True(launches < 100, "the offline re-arm never latched");
        }

        Assert.Equal(ModPickerDialog.MaxOfflineOffers - 1, launches);
    }
}
