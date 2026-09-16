using ConditioningControlPanel;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The mod picker is a ONE-SHOT offer, and for a modular UPGRADER it is the only screen that hands
/// their stripped mod media back. Latching it on a launch with no network burned that offer
/// forever. These cover the two guards that fix it, and the bound that keeps the retry from turning
/// into an every-launch popup for someone deliberately offline.
///
/// <para><b>Two populations, two answers.</b> Everything above the last region is the STANDALONE
/// dialog, which is the upgrader's path and keeps its re-arm. The first-run wizard's flavour step
/// no longer shares it: an offline first run latches, so nothing ever pops up on its own days
/// later. That is "the Circe one" the owner kept seeing, and the last region is its rule.</para>
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

    // ---- the wizard's flavour step: no hand-back, ever ----

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(9)]
    public void TheWizardsFlavourStepLatchesWhateverTheOfferCount(int offersAfterShowing)
        // A first run is offered once. If the box was offline for it, the Mod Manager and the
        // Library own downloads from then on and no picker ever fires standalone.
        => Assert.True(FirstRunGate.ModPickerShownAfterOfflineFlavourStep(offersAfterShowing));

    [Fact]
    public void TheWizardLatchesExactlyWhereTheStandaloneDialogWouldStillReArm()
    {
        // The contrast IS the fix: offer 1 is the case that used to hand the offer back from the
        // wizard and bring the picker round again on a later launch. The standalone dialog still
        // does that on purpose - that population never saw a wizard.
        Assert.True(ModPickerDialog.ShouldReArmAfterOfflineShowing(1));
        Assert.True(FirstRunGate.ModPickerShownAfterOfflineFlavourStep(1));
    }
}
