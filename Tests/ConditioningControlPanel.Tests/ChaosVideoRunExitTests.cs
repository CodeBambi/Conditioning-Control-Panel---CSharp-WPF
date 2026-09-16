using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1201: "videos that are playing when a DTRH drop ends often get stuck and continue
/// onto the lobby screen". The mid-run cap in <c>RunTick</c> was the only path that tore a
/// chaos-fired video down, and the tick early-returns while the run is paused, so quitting mid-tape
/// (or ending from a pause) carried the video onto the results card and the lobby behind it. Every
/// run-exit path now calls the same teardown.
///
/// The half of that which must never regress is the OWNERSHIP rule pinned here:
/// <c>App.Video.ForceCleanup()</c> closes every video window the service owns - the user's own
/// mandatory/session video included - so chaos may only fire it when the armed cap says chaos
/// started the tape.
/// </summary>
public class ChaosVideoRunExitTests
{
    [Fact]
    public void ChaosFiredTapeStillPlaying_ComesOff()
        => Assert.True(ChaosModeService.ShouldStopVideoOnRunExit(chaosCapArmed: true, videoPlaying: true));

    /// <summary>
    /// The one that matters: no armed cap means chaos did not start this video, so a user's own
    /// mandatory or session video survives the descent ending on top of it.
    /// </summary>
    [Fact]
    public void VideoChaosDidNotStart_IsLeftAlone()
        => Assert.False(ChaosModeService.ShouldStopVideoOnRunExit(chaosCapArmed: false, videoPlaying: true));

    /// <summary>Nothing playing: no ForceCleanup, so a run exit never yanks the audio duck or the
    /// InteractionQueue slot out from under whatever else is up.</summary>
    [Fact]
    public void NothingPlaying_IsANoOp()
    {
        Assert.False(ChaosModeService.ShouldStopVideoOnRunExit(chaosCapArmed: true, videoPlaying: false));
        Assert.False(ChaosModeService.ShouldStopVideoOnRunExit(chaosCapArmed: false, videoPlaying: false));
    }
}
