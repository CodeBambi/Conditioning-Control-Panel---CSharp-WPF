using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.FirstShow;
using ConditioningControlPanel.Services.Fyp;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class FirstShowMediaLoaderTests
{
    private static IReadOnlyList<FypAssetManifest.Entry> Pictures(int count) =>
        Enumerable.Range(0,count).Select(i => new FypAssetManifest.Entry { Url = "picture"+i }).ToArray();

    [Fact]
    public async Task Empty_first_response_is_retried_and_successful_files_are_kept()
    {
        int calls = 0;
        var paths = await FirstShowMediaLoader.LoadAsync(_ => Task.FromResult(++calls == 1 ? Pictures(0) : Pictures(6)),
            (url, _) => Task.FromResult<string?>(url), _ => Assert.Fail("Successful files must stay owned by the caller"), CancellationToken.None);
        Assert.Equal(2,calls); Assert.Equal(6,paths.Count);
    }

    [Fact]
    public async Task Failed_small_rendition_uses_original_and_bad_pictures_do_not_lose_good_ones()
    {
        IReadOnlyList<FypAssetManifest.Entry> entries = new[] {
            new FypAssetManifest.Entry { Url = "original", SmallUrl = "small" },
            new FypAssetManifest.Entry { Url = "timeout" },
            new FypAssetManifest.Entry { Url = "good" } };
        var paths = await FirstShowMediaLoader.LoadAsync(_ => Task.FromResult(entries), (url, _) =>
        {
            if (url == "timeout") throw new OperationCanceledException();
            return Task.FromResult<string?>(url == "small" ? null : url);
        }, _ => Assert.Fail("Good files must survive a failed sibling"), CancellationToken.None);
        Assert.Equal(new[] { "original", "good" },paths);
    }

    [Fact]
    public async Task Closing_during_download_releases_finished_files_and_never_confirms()
    {
        using var stop = new CancellationTokenSource();
        var released = new List<string>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FirstShowMediaLoader.LoadAsync(
            _ => Task.FromResult(Pictures(3)), (url, _) =>
            {
                if (url == "picture1") stop.Cancel();
                return Task.FromResult<string?>(url);
            }, released.Add, stop.Token));
        Assert.Equal(3,released.Count);
    }

    [Fact]
    public async Task Downloads_overlap_but_never_exceed_three_at_once()
    {
        int active = 0, peak = 0;
        var batch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paths = await FirstShowMediaLoader.LoadAsync(_ => Task.FromResult(Pictures(3)), async (url, _) =>
        {
            int count = Interlocked.Increment(ref active);
            peak = Math.Max(peak,count);
            if (count == 3) batch.SetResult();
            await batch.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Interlocked.Decrement(ref active);
            return url;
        }, _ => { }, CancellationToken.None);
        Assert.Equal(3,peak); Assert.Equal(3,paths.Count);
    }
    [Fact]
    public async Task Failed_CDN_batch_fetches_an_alternate_feed_and_reports_real_progress()
    {
        int attempts = 0;
        var progress = new List<FirstShowMediaProgress>();
        var paths = await FirstShowMediaLoader.LoadAsync(_ =>
        {
            attempts++;
            return Task.FromResult(Pictures(3));
        }, (url, _) => Task.FromResult<string?>(attempts == 1 ? null : url), _ => { }, CancellationToken.None, progress.Add);
        Assert.Equal(2,attempts); Assert.Equal(3,paths.Count);
        Assert.Contains(progress,p => p.Finding && p.Attempt == 2);
        Assert.Equal(new[] { 0,1,2,3 },progress.Where(p => !p.Finding && p.Attempt == 2).Select(p => p.Ready));
        Assert.DoesNotContain(progress,p => p.Attempt == 1 && p.Ready > 0);
    }

    [Fact]
    public void Censored_uses_only_the_existing_censored_communities()
    {
        Assert.Contains("censored",FirstShowPresets.All);
        Assert.Equal(new[] { "censoredporn", "Censored_Porn" },FirstShowPresets.Sources("censored"));
    }
}
