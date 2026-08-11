using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Chaos;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Transfer
{
    /// <summary>One unit of work for the compression queue. Derived, never persisted.</summary>
    internal sealed record CompressionJob(
        string SrcKey,
        string Rel,
        string FullPath,
        long Size,
        long MtimeTicks,
        string Kind,
        string Lane,
        int SrcW,
        int SrcH,
        int DurMs,
        long SrcBitrate,
        bool WantsPreview)
    {
        /// <summary>Temp-file stem: unique per job, and safe as a filename.</summary>
        public string JobId => SrcKey;
    }

    /// <summary>What one planning pass found.</summary>
    internal sealed class CompressionPlan
    {
        public List<CompressionJob> Jobs { get; } = new();
        /// <summary>Files already handled by the index (ready/exempt) — nothing to do.</summary>
        public int AlreadyDone { get; set; }
        /// <summary>Files whose last attempt failed and that were not retried this pass.</summary>
        public int PreviouslyFailed { get; set; }
        /// <summary>Total source bytes across <see cref="Jobs"/> — the ETA denominator.</summary>
        public long PendingSourceBytes { get; set; }
        public int PoolCount { get; set; }
    }

    /// <summary>
    /// Turns the user's active asset pool into a job list, using the SAME enumeration the DtRH
    /// manifest uses (<see cref="DtrhAssetManifest.EnumerateActive"/>) so "the pool" can never mean
    /// two different things in one app.
    ///
    /// Lane matrix (see the plan):
    ///   ≤5 MB anything                      → exempt, sha256(original) is the wire id
    ///   video >5 MB, ≤2.2 Mbps and ≤800p    → exempt:already-small, but still gets a micro-preview
    ///   mp4/m4v/webm >5 MB                  → host-mt   (WinRT MediaTranscoder, 720p H.264)
    ///   animated gif/webp >5 MB             → page-wc   (WebCodecs in the goon page)
    ///   still jpg/png/webp/gif >5 MB        → host-skia (SkiaSharp WebP q80 ≤1920)
    ///
    /// Planning is pure bookkeeping plus, for big videos, one WinRT properties probe (cached for the
    /// process lifetime). A probe failure is NOT a classification: an unprobeable video still goes
    /// down host-mt, where PrepareFileTranscodeAsync gets the final word.
    /// </summary>
    internal static class AssetCompressionPlanner
    {
        /// <summary>Below this, the original IS the artifact (and fits the 8 MB exempt wire cap).</summary>
        public const long ExemptMaxBytes = 5L * 1024 * 1024;
        /// <summary>A video already at or under this bitrate gains nothing from a re-encode.</summary>
        public const long AlreadySmallBitrate = 2_200_000;
        /// <summary>...and this tall. 800 rather than 720 so a 768p clip isn't re-encoded for 48 lines.</summary>
        public const int AlreadySmallHeight = 800;

        private static readonly HashSet<string> VideoExts =
            new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".webm" };

        /// <summary>srcKey -> probe result (null = probe failed). Process-lifetime; srcKey already
        /// encodes size+mtime, so a replaced file never reads a stale probe.</summary>
        private static readonly ConcurrentDictionary<string, VideoTranscodeLane.VideoProbe?> _probeCache = new();
        /// <summary>path|size -> "is this an animated container?". Same reasoning, cheaper key.</summary>
        private static readonly ConcurrentDictionary<string, bool> _animatedCache = new();

        /// <summary>
        /// Walk the active pool and classify everything the cache doesn't already know about.
        /// Never throws; a file that disappears mid-walk is simply absent from the plan.
        /// </summary>
        public static async Task<CompressionPlan> PlanAsync(
            TransferCacheStore store, bool retryFailed = false, CancellationToken ct = default)
        {
            var plan = new CompressionPlan();
            try
            {
                foreach (var (full, rel, bytes, isImage) in DtrhAssetManifest.EnumerateActive())
                {
                    ct.ThrowIfCancellationRequested();
                    plan.PoolCount++;

                    long mtime;
                    try { mtime = new FileInfo(full).LastWriteTimeUtc.Ticks; }
                    catch { continue; }

                    var srcKey = TransferCacheHash.ComputeSrcKey(rel, bytes, mtime);
                    if (store.TryGet(srcKey, out var known))
                    {
                        if (known.State != TransferStates.Failed) { plan.AlreadyDone++; continue; }
                        if (!retryFailed) { plan.PreviouslyFailed++; continue; }
                    }

                    var job = await ClassifyAsync(srcKey, full, rel, bytes, mtime, isImage, ct)
                        .ConfigureAwait(false);
                    if (job == null) continue;
                    plan.Jobs.Add(job);
                    plan.PendingSourceBytes += bytes;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                App.Logger?.Warning("AssetCompressionPlanner.PlanAsync failed: {E}", ex.Message);
            }
            return plan;
        }

        private static async Task<CompressionJob?> ClassifyAsync(
            string srcKey, string full, string rel, long bytes, long mtime, bool isImage, CancellationToken ct)
        {
            var ext = Path.GetExtension(full).ToLowerInvariant();
            bool isVideo = !isImage || VideoExts.Contains(ext);

            // Small enough to ship as-is. The worker still has to hash it (that hash IS the wire
            // identity), which is why this is a job and not an index write here.
            if (bytes <= ExemptMaxBytes)
                return new CompressionJob(srcKey, rel, full, bytes, mtime,
                    isVideo ? TransferKinds.Video : KindForStill(full, ext),
                    TransferLanes.Exempt, 0, 0, 0, 0, WantsPreview: false);

            if (isVideo)
            {
                var probe = await ProbeCachedAsync(srcKey, full, ct).ConfigureAwait(false);
                if (probe is { } p && p.Bitrate > 0 && p.Bitrate <= AlreadySmallBitrate
                    && p.Height > 0 && p.Height <= AlreadySmallHeight)
                {
                    // Re-encoding this would cost minutes and save nothing - but the grid still
                    // wants something light to show, so it keeps its micro-preview job.
                    return new CompressionJob(srcKey, rel, full, bytes, mtime, TransferKinds.Video,
                        TransferLanes.Exempt, p.Width, p.Height, p.DurMs, p.Bitrate, WantsPreview: true);
                }
                return new CompressionJob(srcKey, rel, full, bytes, mtime, TransferKinds.Video,
                    TransferLanes.HostMt,
                    probe?.Width ?? 0, probe?.Height ?? 0, probe?.DurMs ?? 0, probe?.Bitrate ?? 0,
                    WantsPreview: true);
            }

            // Stills and animations. Animated containers can't survive a Skia re-encode as one
            // frame, so they go to the page's WebCodecs lane instead.
            if (IsAnimatedCached(full, bytes, ext))
                return new CompressionJob(srcKey, rel, full, bytes, mtime, TransferKinds.Gif,
                    TransferLanes.PageWc, 0, 0, 0, 0, WantsPreview: false);

            return new CompressionJob(srcKey, rel, full, bytes, mtime, TransferKinds.Image,
                TransferLanes.HostSkia, 0, 0, 0, 0, WantsPreview: false);
        }

        private static string KindForStill(string full, string ext) =>
            IsAnimatedCached(full, 0, ext) ? TransferKinds.Gif : TransferKinds.Image;

        private static async Task<VideoTranscodeLane.VideoProbe?> ProbeCachedAsync(
            string srcKey, string full, CancellationToken ct)
        {
            if (_probeCache.TryGetValue(srcKey, out var hit)) return hit;
            var probe = await VideoTranscodeLane.ProbeAsync(full, ct).ConfigureAwait(false);
            _probeCache[srcKey] = probe;
            return probe;
        }

        /// <summary>
        /// Animated-container probe. WebP has a 21-byte header flag (AnimatedWebp.IsAnimated); GIF
        /// needs the codec's frame count, and a single-frame "gif" really does exist in the wild
        /// (exported stills), so it's worth asking rather than assuming.
        /// </summary>
        private static bool IsAnimatedCached(string full, long bytes, string ext)
        {
            if (ext != ".gif" && ext != ".webp") return false;
            var key = full + "|" + bytes;
            if (_animatedCache.TryGetValue(key, out var hit)) return hit;

            bool animated;
            if (ext == ".webp")
            {
                animated = AnimatedWebp.IsAnimated(full);
            }
            else
            {
                try
                {
                    using var codec = SKCodec.Create(full);
                    animated = codec != null && codec.FrameCount > 1;
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("AssetCompressionPlanner: gif probe {Path}: {E}", full, ex.Message);
                    animated = true;   // a gif we can't open is far more likely animated than not
                }
            }
            _animatedCache[key] = animated;
            return animated;
        }

        /// <summary>Forget cached probes (used after a "delete all", so a retry re-measures).</summary>
        public static void ClearProbeCache()
        {
            _probeCache.Clear();
            _animatedCache.Clear();
        }
    }
}
