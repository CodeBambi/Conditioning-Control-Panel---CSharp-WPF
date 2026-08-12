using CcpClient.Desktop.Features.Chaos;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-061: the tunnel page protocol state machine (WPF ChaosTunnelService.cs parity):
/// pending FIFO until ready, streak dedup, RunAgain re-arm, exit-done fast path vs the
/// watchdog force, typed sfx handling, malformed/unknown tolerance. All synchronous — no
/// waits at all (the exit watchdog's elapsed path is invoked DIRECTLY; SP-059 discipline).
/// </summary>
public sealed class ChaosTunnelCoreTests
{
    [Fact]
    public void Show_BeforeReady_QueuesRunStart()
    {
        var core = new ChaosTunnelCore();
        Assert.Null(core.Show());
        Assert.Equal(1, core.PendingCount);
        Assert.True(core.RunActive);
    }

    [Fact]
    public void Ready_FlushesPendingInOrder()
    {
        var core = new ChaosTunnelCore();
        core.Show();
        core.SetIntensity(0.5);
        core.SendZoneHint(3, 0.9);
        Assert.Equal(3, core.PendingCount);

        var outcome = Assert.IsType<ChaosTunnelCore.PageOutcome.Ready>(
            core.HandlePageMessage("{\"type\":\"ready\"}"));
        Assert.True(core.Ready);
        Assert.Equal(0, core.PendingCount);
        Assert.Equal(
            ["{\"type\":\"run-start\"}", "{\"type\":\"intensity\",\"value\":0.5}", "{\"type\":\"zone-hint\",\"depth\":3,\"intensity\":0.9}"],
            outcome.Flush);
    }

    [Fact]
    public void AfterReady_FramesPassThroughImmediately()
    {
        var core = new ChaosTunnelCore();
        core.HandlePageMessage("{\"type\":\"ready\"}");
        Assert.Equal("{\"type\":\"run-start\"}", core.Show());
        Assert.Equal(0, core.PendingCount);
    }

    [Fact]
    public void Streak_DedupesOnCombo_AndShowResetsTheDedup()
    {
        var core = new ChaosTunnelCore();
        core.HandlePageMessage("{\"type\":\"ready\"}");
        Assert.NotNull(core.SetStreak(4, 1.5));
        Assert.Null(core.SetStreak(4, 1.6));  // same combo — deduped even when mult moved (WPF :123-125)
        Assert.NotNull(core.SetStreak(5, 1.6));
        core.Show();                           // fresh run: _lastStreak = -1 (WPF :88)
        Assert.NotNull(core.SetStreak(5, 1.6));
    }

    [Fact]
    public void CloseActive_WhenNeverReady_DisposesImmediately()
    {
        var core = new ChaosTunnelCore();
        core.Show(); // queued, never ready
        var (plan, runEnd) = core.CloseActive();
        Assert.Equal(ChaosTunnelCore.ClosePlan.DisposeImmediately, plan);
        Assert.Null(runEnd);
        Assert.False(core.RunActive);
    }

    [Fact]
    public void CloseActive_WhenReady_PostsRunEnd()
    {
        var core = new ChaosTunnelCore();
        core.HandlePageMessage("{\"type\":\"ready\"}");
        core.Show();
        var (plan, runEnd) = core.CloseActive();
        Assert.Equal(ChaosTunnelCore.ClosePlan.RunEndPosted, plan);
        Assert.Equal("{\"type\":\"run-end\"}", runEnd);
    }

    [Fact]
    public void ExitDone_WithNoActiveRun_ClosesNow()
    {
        var core = new ChaosTunnelCore();
        core.HandlePageMessage("{\"type\":\"ready\"}");
        core.Show();
        core.CloseActive();
        var exit = Assert.IsType<ChaosTunnelCore.PageOutcome.ExitDone>(
            core.HandlePageMessage("{\"type\":\"exit-done\"}"));
        Assert.True(exit.CloseNow);
    }

    [Fact]
    public void ExitDone_AfterRunAgainRearm_KeepsTheWindow()
    {
        // WPF :289-294: a RunAgain inside the exit window re-arms RunActive — exit-done
        // must NOT kill the window the new run is about to fade back in.
        var core = new ChaosTunnelCore();
        core.HandlePageMessage("{\"type\":\"ready\"}");
        core.Show();
        core.CloseActive();
        core.Show(); // RunAgain
        var exit = Assert.IsType<ChaosTunnelCore.PageOutcome.ExitDone>(
            core.HandlePageMessage("{\"type\":\"exit-done\"}"));
        Assert.False(exit.CloseNow);
    }

    [Fact]
    public void Sfx_IsCountedAndTyped_NeverResolved()
    {
        var core = new ChaosTunnelCore();
        var cue = Assert.IsType<ChaosTunnelCore.PageOutcome.SfxCue>(
            core.HandlePageMessage("{\"type\":\"sfx\",\"name\":\"tunnel_zone\",\"scale\":0.25}"));
        Assert.Equal(0.25, cue.Scale);
        Assert.Equal(1, core.SfxCueCount);
        // Missing scale falls back to the payload's own default (main.js:13).
        Assert.IsType<ChaosTunnelCore.PageOutcome.SfxCue>(
            core.HandlePageMessage("{\"type\":\"sfx\",\"name\":\"tunnel_exit\"}"));
        Assert.Equal(2, core.SfxCueCount);
    }

    [Fact]
    public void Unknown_AndMalformed_AreToleratedTyped()
    {
        var core = new ChaosTunnelCore();
        Assert.Equal("future-frame",
            Assert.IsType<ChaosTunnelCore.PageOutcome.Unknown>(
                core.HandlePageMessage("{\"type\":\"future-frame\",\"x\":1}")).Type);
        Assert.IsType<ChaosTunnelCore.PageOutcome.Malformed>(core.HandlePageMessage("not json"));
        Assert.IsType<ChaosTunnelCore.PageOutcome.Malformed>(core.HandlePageMessage("[1,2]"));
        Assert.IsType<ChaosTunnelCore.PageOutcome.Malformed>(core.HandlePageMessage("{\"noType\":1}"));
    }

    [Fact]
    public void VideoPlaying_AndPowerup_PassThrough()
    {
        var core = new ChaosTunnelCore();
        core.HandlePageMessage("{\"type\":\"ready\"}");
        Assert.Equal("{\"type\":\"video-playing\",\"on\":true}", core.SetVideoPlaying(true));
        Assert.Equal("{\"type\":\"spawn-powerup\",\"id\":null,\"ahead\":90}", core.SpawnPowerup());
        Assert.IsType<ChaosTunnelCore.PageOutcome.PowerupClick>(
            core.HandlePageMessage("{\"type\":\"powerup-click\",\"id\":\"p1\"}"));
        var log = Assert.IsType<ChaosTunnelCore.PageOutcome.PageLog>(
            core.HandlePageMessage("{\"type\":\"log\",\"msg\":\"bloom disabled: x\"}"));
        Assert.Equal("bloom disabled: x", log.Message);
    }
}
