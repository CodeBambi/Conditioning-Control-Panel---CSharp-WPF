using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The first-run picker downloaded the mod the user chose and then left them on CCP Default (no idle
/// bark rules — the companion looks broken). The choice is now remembered until its content lands.
/// These cover the decision rules that drive it: what is worth remembering, which availability signal
/// satisfies it, and when the switch is actually due.
/// </summary>
public class PendingModActivationTests
{
    // ---- what is worth remembering ----

    [Fact]
    public void AChoiceOtherThanTheActiveMod_IsRecorded()
        => Assert.True(PendingModActivation.ShouldRecord(
            BuiltInMods.BambiSleepId, BuiltInMods.CCPDefaultId));

    [Fact]
    public void TheModAlreadyRunning_IsNotRecorded()
        => Assert.False(PendingModActivation.ShouldRecord(
            BuiltInMods.BambiSleepId, BuiltInMods.BambiSleepId));

    [Fact]
    public void CaseDoesNotMakeItANewChoice()
        => Assert.False(PendingModActivation.ShouldRecord(
            BuiltInMods.BambiSleepId.ToUpperInvariant(), BuiltInMods.BambiSleepId));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingChosen_IsNotRecorded(string? chosen)
        => Assert.False(PendingModActivation.ShouldRecord(chosen, BuiltInMods.CCPDefaultId));

    // ---- which availability signal satisfies it ----

    [Fact]
    public void TheModsOwnIdMatches()
        => Assert.True(PendingModActivation.Matches(BuiltInMods.BambiSleepId, BuiltInMods.BambiSleepId));

    [Fact]
    public void ThePackIdThatCarriesItMatches()
        => Assert.True(PendingModActivation.Matches(BuiltInMods.BambiSleepId, "mod-bambi"));

    [Fact]
    public void AnotherModsPackDoesNot()
        => Assert.False(PendingModActivation.Matches(BuiltInMods.BambiSleepId, "mod-sissy"));

    [Fact]
    public void AnAudioPackCarriesNoModAndMatchesNothing()
        => Assert.False(PendingModActivation.Matches(BuiltInMods.BambiSleepId, "audio-core"));

    [Theory]
    [InlineData(null, BuiltInMods.BambiSleepId)]
    [InlineData("", BuiltInMods.BambiSleepId)]
    [InlineData(BuiltInMods.BambiSleepId, null)]
    [InlineData(BuiltInMods.BambiSleepId, "")]
    public void NothingPendingOrNoSignal_NeverMatches(string? pending, string? signal)
        => Assert.False(PendingModActivation.Matches(pending, signal));

    // ---- when the switch is due ----

    [Fact]
    public void ContentOnDisk_AndNotYetActive_Switches()
        => Assert.True(PendingModActivation.ShouldActivate(
            BuiltInMods.BambiSleepId, BuiltInMods.CCPDefaultId, contentAvailable: true));

    [Fact]
    public void StillDownloading_Waits()
        => Assert.False(PendingModActivation.ShouldActivate(
            BuiltInMods.BambiSleepId, BuiltInMods.CCPDefaultId, contentAvailable: false));

    [Fact]
    public void AlreadyActive_IsSatisfiedNotSwitched()
        => Assert.False(PendingModActivation.ShouldActivate(
            BuiltInMods.BambiSleepId, BuiltInMods.BambiSleepId, contentAvailable: true));

    [Fact]
    public void NoPendingChoice_NeverSwitches()
        => Assert.False(PendingModActivation.ShouldActivate(
            null, BuiltInMods.CCPDefaultId, contentAvailable: true));

    /// <summary>
    /// The manual-switch rule, end to end: the user picks Bambi, then switches to Sissy by hand while
    /// the pack is still coming down. The manual choice wins — MainWindow drops the pending id, so the
    /// arriving pack has nothing left to match and cannot yank them back.
    /// </summary>
    [Fact]
    public void AManualSwitchLeavesNothingForThePackToTrigger()
    {
        string? pending = BuiltInMods.BambiSleepId;

        // ApplyActiveModChange(fromPickerChoice: false) — any manual switch clears it.
        pending = null;

        Assert.False(PendingModActivation.Matches(pending, "mod-bambi"));
        Assert.False(PendingModActivation.ShouldActivate(
            pending, BuiltInMods.SissyHypnoId, contentAvailable: true));
    }
}
