using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The two decisions behind #685 ("Mantra Chant loops the say-it-with-me PROMPT forever and ignores
/// every mute"): what the chant plays, and whether it's allowed to make a sound. Both are pure, so
/// they pin down here without WPF, an audio device, or a mod on disk.
/// </summary>
public class MantraChantGateTests
{
    // ── Mute gate ────────────────────────────────────────────────────────────

    [Fact]
    public void CompanionMuted_SilencesTheChant_WhateverTheSlidersSay()
        => Assert.Equal(0f, MantraChantService.ResolveVolume(companionMuted: true, chantVolume: 100, masterVolume: 100));

    [Fact]
    public void Unmuted_FoldsChantVolumeWithMaster()
        => Assert.Equal(0.25f, MantraChantService.ResolveVolume(companionMuted: false, chantVolume: 50, masterVolume: 50), 3);

    [Fact]
    public void MasterMute_StillSilencesIt()
        => Assert.Equal(0f, MantraChantService.ResolveVolume(companionMuted: false, chantVolume: 100, masterVolume: 0));

    [Theory]
    [InlineData(200, 100)]   // a settings file edited past the slider's range
    [InlineData(100, 400)]
    public void VolumeNeverExceedsUnity(double chant, double master)
        => Assert.Equal(1f, MantraChantService.ResolveVolume(companionMuted: false, chantVolume: chant, masterVolume: master));

    // ── Clip sequence (the call-and-response pair) ───────────────────────────

    private const string Say = @"C:\mods\mantra_bambi_01_say.mp3";   // the ask — carries the phrase
    private const string Resp = @"C:\mods\mantra_bambi_01_resp.mp3"; // her affirmation

    [Fact]
    public void PlaysTheAskThenTheAffirmation_InThatOrder()
        => Assert.Equal(new[] { Say, Resp }, MantraChantService.ResolveClipSequence(Say, Resp));

    [Fact]
    public void OnlyTheAskVoiced_StillChantsTheAsk()
        => Assert.Equal(new[] { Say }, MantraChantService.ResolveClipSequence(Say, null));

    [Fact]
    public void OnlyTheAffirmationVoiced_StillChantsIt()
        => Assert.Equal(new[] { Resp }, MantraChantService.ResolveClipSequence(null, Resp));

    [Fact]
    public void NothingVoiced_ResolvesToNothing_SoTheLoopCanSelfHealToOff()
        => Assert.Empty(MantraChantService.ResolveClipSequence(null, null));

    [Fact]
    public void EmptyPathsCountAsAbsent()
        => Assert.Equal(new[] { Say }, MantraChantService.ResolveClipSequence(Say, ""));
}
