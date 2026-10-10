using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Lane c1: the DtRH engagement store (WPF Services/Chaos/DtrhAssetStatsStore.cs) sums the page's
/// asset-stats deltas and ranks favorites (grabs x8, pops x2, weighted attention); and the mandatory video
/// scheduler says when the clip it announced is gone (WPF VideoService.VideoEnded).</summary>
[Collection(SessionStatics.Name)]
public sealed class DtrhAssetStatsStoreTests
{
    [Fact]
    public void Merge_sums_deltas_ignores_junk_and_TopAssets_ranks_by_engagement()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-c1-stats-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            DtrhAssetStatsStore.PathOverride = dir;
            Assert.Empty(DtrhAssetStatsStore.TopAssets(12));
            DtrhAssetStatsStore.Merge(JObject.Parse(
                "{\"stats\":[{\"name\":\"a.png\",\"weighted\":10},{\"name\":\"b.gif\",\"kind\":\"gif\",\"grabs\":2}," +
                "{\"name\":\"\",\"grabs\":99},{\"name\":\"c.png\",\"weighted\":-50,\"pops\":1}]}"));
            DtrhAssetStatsStore.Merge(JObject.Parse("{\"stats\":[{\"name\":\"A.PNG\",\"weighted\":7}]}"));   // names are filenames: case-blind
            DtrhAssetStatsStore.Merge(new JObject());                                                          // no rows: nothing
            Assert.Equal(new List<string> { "a.png", "b.gif", "c.png" }, DtrhAssetStatsStore.TopAssets(12));   // 17, 16, 2
            Assert.Equal(new List<string> { "a.png" }, DtrhAssetStatsStore.TopAssets(1));
            Assert.Empty(DtrhAssetStatsStore.TopAssets(-3));
        }
        finally
        {
            DtrhAssetStatsStore.PathOverride = null;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private sealed class Host : IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }

    [Fact]
    public void VideoEnded_fires_once_per_started_clip_and_never_for_one_cancelled_in_its_pre_roll()
    {
        var clock = new PopQuizSchedulerTests.FakeClock();
        var v = new MandatoryVideoScheduler(new Host(), clock, () => new[] { "/v/a.mp4" });
        int started = 0, ended = 0;
        v.VideoStarted += () => started++;
        v.VideoEnded += () => ended++;

        Assert.True(v.Trigger(strictOverride: false));
        v.ForceCleanup();                                   // gone before it ever showed
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal((0, 0), (started, ended));

        Assert.True(v.Trigger(strictOverride: false));
        clock.Advance(MandatoryVideoScheduler.PreRoll);
        Assert.Equal((1, 0), (started, ended));
        v.End();
        v.End();                                            // a second close is nothing
        Assert.Equal((1, 1), (started, ended));

        Assert.True(v.Trigger(strictOverride: false));
        clock.Advance(MandatoryVideoScheduler.PreRoll);
        v.ForceCleanup();                                   // panic closes it: the page still hears it is gone
        Assert.Equal((2, 2), (started, ended));
    }
}
