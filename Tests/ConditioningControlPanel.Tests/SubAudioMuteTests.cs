using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The whisper MUTE is deliberately a separate flag from the whisper ENABLE.
///
/// They used to be one: "Mute whispers" (avatar quick menu + Companion tab) flipped
/// <see cref="AppSettings.SubAudioEnabled"/>, which is the feature's master enable and is
/// prescribed by a session (<c>SessionSettings.AudioWhispersEnabled</c>). Once the session feature
/// lock started locking the prescribed dose, that would have made muting unavailable at exactly the
/// moment it is most wanted - someone walks in and the sound has to stop now. Splitting them keeps
/// the mute reflex free while the dose stays locked.
///
/// These assert the gate every playback path is supposed to read, so a future change cannot quietly
/// re-conflate the two.
/// </summary>
public class SubAudioMuteTests
{
    [Fact]
    public void AudibleOnlyWhenEnabledAndNotMuted()
    {
        var s = new AppSettings { SubAudioEnabled = true, SubAudioMuted = false };
        Assert.True(s.SubAudioAudible);
    }

    [Fact]
    public void MutingSilencesWithoutDisablingTheFeature()
    {
        var s = new AppSettings { SubAudioEnabled = true, SubAudioMuted = true };

        Assert.False(s.SubAudioAudible);
        // The load-bearing half: the prescribed dose is untouched, so a session's
        // AudioWhispersEnabled survives the user muting.
        Assert.True(s.SubAudioEnabled);
    }

    [Fact]
    public void UnmutingRestoresAudibilityWithNoOtherState()
    {
        var s = new AppSettings { SubAudioEnabled = true, SubAudioMuted = true };
        s.SubAudioMuted = false;
        Assert.True(s.SubAudioAudible);
    }

    [Fact]
    public void MuteCannotMakeADisabledFeatureAudible()
    {
        var s = new AppSettings { SubAudioEnabled = false, SubAudioMuted = false };
        Assert.False(s.SubAudioAudible);
    }

    /// <summary>Default state must be "not muted" so existing installs are unaffected on upgrade.</summary>
    [Fact]
    public void DefaultsToNotMuted()
        => Assert.False(new AppSettings().SubAudioMuted);
}
