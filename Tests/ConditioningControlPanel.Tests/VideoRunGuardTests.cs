using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1254-#1256: the attention-check "try again" / "watch again" replay. The first video's safety
/// timer survived into the replay and closed it a few seconds in (#1254, #1255), or closed it
/// during the replay's 1.3s pre-roll, after which the pre-roll still put the clip on screen with
/// nothing tracking it and its last frame stayed up (#1256). Every guard and the pre-roll now carry
/// the id of the run that armed them.
/// </summary>
public class VideoRunGuardTests
{
    [Fact]
    public void Preroll_StartsItsOwnLiveRun()
        => Assert.True(VideoService.ShouldStartAfterPreroll(armedRunId: 3, currentRunId: 3, videoPlaying: true));

    [Fact]
    public void Preroll_RunEndedDuringTheDelay_DoesNotStart()
        => Assert.False(VideoService.ShouldStartAfterPreroll(armedRunId: 3, currentRunId: 3, videoPlaying: false));

    [Fact]
    public void Preroll_ReplacedByANewerRun_DoesNotStart()
        => Assert.False(VideoService.ShouldStartAfterPreroll(armedRunId: 3, currentRunId: 4, videoPlaying: true));

    [Fact]
    public void GuardFromTheSameRun_IsNotStale()
        => Assert.False(VideoService.IsStaleGuardTick(armedRunId: 7, currentRunId: 7));

    [Fact]
    public void GuardFromTheFirstVideo_IsStaleDuringTheRetry()
        => Assert.True(VideoService.IsStaleGuardTick(armedRunId: 7, currentRunId: 8));

    // #1258: the VideoView letterbox is a native static control, answered with a black brush.
    [Fact]
    public void BlackFill_AnswersOnlyCtlColorStatic()
    {
        Assert.True(VideoHostBlackFill.Handles(0x0138));
        Assert.False(VideoHostBlackFill.Handles(0x0133)); // WM_CTLCOLOREDIT
        Assert.False(VideoHostBlackFill.Handles(0x0014)); // WM_ERASEBKGND
    }

    // #1257: the browser player must not go back to a second full-screen decoder for the blur.
    [Fact]
    public void PlayerPage_BackdropIsACanvasNotASecondVideo()
    {
        var dir = AppContext.BaseDirectory;
        string? html = null;
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "ConditioningControlPanel", "Resources", "web", "player", "index.html");
            if (File.Exists(p)) { html = File.ReadAllText(p); break; }
        }
        Assert.NotNull(html);
        Assert.Contains("<canvas id=\"bg\"", html);
        Assert.DoesNotContain("<video id=\"bg\"", html);
    }
}
