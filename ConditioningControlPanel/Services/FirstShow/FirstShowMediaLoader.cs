using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp;

namespace ConditioningControlPanel.Services.FirstShow;

internal readonly record struct FirstShowMediaProgress(bool Finding, int Attempt, int Ready, int Total);

// A small show should not wait for a whole feed or lose good downloads to one slow CDN file.
internal static class FirstShowMediaLoader
{
    internal const int TargetPictures = 8;
    internal static async Task<List<string>> LoadAsync(
        Func<CancellationToken, Task<IReadOnlyList<FypAssetManifest.Entry>>> fetch,
        Func<string, CancellationToken, Task<string?>> download,
        Action<string> release, CancellationToken cancellation,
        Action<FirstShowMediaProgress>? progress = null)
    {
        var paths = new List<string>();
        bool hadEntries = false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        budget.CancelAfter(TimeSpan.FromSeconds(40));
        try
        {
            // Retry the whole load when every CDN file failed, not just an empty feed response.
            for (int attempt = 1; attempt <= 2 && paths.Count == 0; attempt++)
            {
                budget.Token.ThrowIfCancellationRequested();
                progress?.Invoke(new(true,attempt,0,0));
                IReadOnlyList<FypAssetManifest.Entry> entries = Array.Empty<FypAssetManifest.Entry>();
                using (var query = CancellationTokenSource.CreateLinkedTokenSource(budget.Token))
                {
                    query.CancelAfter(TimeSpan.FromSeconds(18));
                    try { entries = await fetch(query.Token); }
                    catch (OperationCanceledException) when (!budget.IsCancellationRequested) { }
                }
                App.Logger?.Information("First show media attempt {Attempt}: {Count} feed entries",attempt,entries.Count);
                hadEntries |= entries.Count > 0;
                int ready = 0, total = Math.Min(TargetPictures,entries.Count);
                if (total > 0) progress?.Invoke(new(false,attempt,0,total));
                foreach (var batch in entries.DistinctBy(x => x.Url).Take(12).Chunk(3))
                {
                    budget.Token.ThrowIfCancellationRequested();
                    var results = await Task.WhenAll(batch.Select(async entry =>
                    {
                        using var item = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                        item.CancelAfter(TimeSpan.FromSeconds(6));
                        try
                        {
                            var path = await download(entry.SmallUrl ?? entry.Url, item.Token);
                            if (path == null && entry.SmallUrl != null && !item.IsCancellationRequested)
                                path = await download(entry.Url, item.Token);
                            if (path != null) progress?.Invoke(new(false,attempt,Math.Min(Interlocked.Increment(ref ready),total),total));
                            return path;
                        }
                        catch (OperationCanceledException) { return null; }
                        catch (Exception ex) { App.Logger?.Debug("First show image skipped: {Reason}", ex.Message); return null; }
                    }));
                    paths.AddRange(results.OfType<string>());
                    if (paths.Count >= TargetPictures) break;
                }
                App.Logger?.Information("First show media attempt {Attempt}: {Count} downloaded pictures",attempt,paths.Count);
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
        catch
        {
            foreach (var path in paths) release(path);
            throw;
        }
        if (cancellation.IsCancellationRequested)
        {
            foreach (var path in paths) release(path);
            cancellation.ThrowIfCancellationRequested();
        }
        if (paths.Count == 0) throw new IOException(hadEntries
            ? "The feed responded, but no CDN pictures downloaded."
            : "The provider returned no usable feed before the loading deadline.");
        return paths;
    }
}
