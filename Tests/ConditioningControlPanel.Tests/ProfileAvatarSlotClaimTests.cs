using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Trainer Card's avatar bubble is ONE slot shared by the real profile picture and the preset
/// "blank subject" avatar, and bug #847 was the preset claiming it on a null check alone: the
/// profile-viewer path empties the bubble, fires an async /user/lookup, then applies cosmetics
/// synchronously - so "empty" there meant "not here yet", and everyone's Discord avatar was
/// replaced by a preset bust. These pin the tri-state rule that replaced the null check.
/// </summary>
public class ProfileAvatarSlotClaimTests
{
    [Fact]
    public void PendingLoadKeepsTheEmptySlot()
        => Assert.False(ProfileAvatarSlot.PresetMayClaim(false, false, ProfilePictureLoad.Pending));

    [Fact]
    public void FinishedLoadWithNoPictureHandsTheSlotOver()
        => Assert.True(ProfileAvatarSlot.PresetMayClaim(false, false, ProfilePictureLoad.None));

    [Fact]
    public void ARealPictureIsNeverPaintedOver()
    {
        Assert.False(ProfileAvatarSlot.PresetMayClaim(false, true, ProfilePictureLoad.Loaded));
        // Even a stale state cannot talk the preset over a picture that is actually in the slot.
        Assert.False(ProfileAvatarSlot.PresetMayClaim(false, true, ProfilePictureLoad.None));
        Assert.False(ProfileAvatarSlot.PresetMayClaim(false, true, ProfilePictureLoad.Pending));
    }

    [Fact]
    public void OurOwnPresetIsAlwaysOursToSwapOrClear()
    {
        // Re-applying cosmetics (dialog save, click-to-pin) must be able to change or remove the
        // preset it painted a moment ago, whatever the load state has since become.
        Assert.True(ProfileAvatarSlot.PresetMayClaim(true, true, ProfilePictureLoad.None));
        Assert.True(ProfileAvatarSlot.PresetMayClaim(true, true, ProfilePictureLoad.Pending));
    }
}
