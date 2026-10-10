using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The media manifest the game pages read on ready (WPF Services/Chaos/DtrhAssetManifest.Build, the
    /// local half): the active library's images/ and videos/, deselected files left out, oversize or
    /// undecodable media counted as skipped, sampled down to 5000 entries keeping the image:video ratio.
    /// URLs are the asset server's ccp.assets path (WPF https://ccp.assets/&lt;rel&gt;).
    /// The remote (online) entries WPF appends after the sample are GameMediaManifest.Remote.cs: only
    /// with MediaSource off "local" AND remote media consent.
    /// </summary>
    internal static partial class GameMediaManifest
    {
        private static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        private static readonly string[] VideoExts = { ".mp4", ".webm", ".m4v" };
        internal const long MaxImageBytes = 50L * 1024 * 1024;
        internal const long MaxVideoBytes = 500L * 1024 * 1024;
        internal const int MaxEntries = 5000;
        private const int MaxWalkDepth = 8;

        internal sealed record Entry(string Name, string Url);

        internal sealed class Manifest
        {
            public List<Entry> Images { get; } = new();
            public List<Entry> Videos { get; } = new();
            public int Skipped { get; set; }
            public bool Truncated { get; set; }

            public object Frame() => new
            {
                type = "manifest",
                images = Images.Select(e => new { name = e.Name, url = e.Url }),
                videos = Videos.Select(e => new { name = e.Name, url = e.Url }),
                skipped = Skipped,
                truncated = Truncated,
            };
        }

        internal static Manifest Build(string? root, Func<string, string> url, IEnumerable<string>? disabledPaths)
        {
            var m = new Manifest();
            if (string.IsNullOrEmpty(root)) return m;
            try
            {
                var disabled = new HashSet<string>((disabledPaths ?? Array.Empty<string>()).Select(p => p.Replace('\\', '/')),
                    StringComparer.OrdinalIgnoreCase);
                int accepted = 0;
                foreach (var isImage in new[] { true, false })
                {
                    var dir = Path.Combine(root, isImage ? "images" : "videos");
                    if (!Directory.Exists(dir)) continue;
                    var exts = isImage ? ImageExts : VideoExts;
                    var otherExts = isImage ? VideoExts : ImageExts;
                    long cap = isImage ? MaxImageBytes : MaxVideoBytes;
                    foreach (var f in Walk(dir, 0))
                    {
                        if (accepted >= MaxEntries * 2) break;
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (!exts.Contains(ext))
                        {
                            if (!otherExts.Contains(ext) && IsMediaLike(ext)) m.Skipped++;
                            continue;
                        }
                        string rel;
                        try { rel = Path.GetRelativePath(root, f).Replace('\\', '/'); }
                        catch { rel = Path.GetFileName(f); }
                        if (disabled.Count > 0 && disabled.Contains(rel)) continue;
                        long len;
                        try { len = new FileInfo(f).Length; } catch { continue; }
                        if (len <= 0 || len > cap) { m.Skipped++; continue; }
                        accepted++;
                        (isImage ? m.Images : m.Videos).Add(new Entry(Path.GetFileName(f), url(rel)));
                    }
                }
                int total = m.Images.Count + m.Videos.Count;
                if (total > MaxEntries)
                {
                    var rng = new Random();
                    int imgKeep = (int)Math.Round(MaxEntries * (double)m.Images.Count / total);
                    Downsample(m.Images, imgKeep, rng);
                    Downsample(m.Videos, MaxEntries - imgKeep, rng);
                    m.Truncated = true;
                }
                Log.Information("[Game] manifest: {I} images, {V} videos, {S} skipped{T}",
                    m.Images.Count, m.Videos.Count, m.Skipped, m.Truncated ? " (truncated)" : "");
            }
            catch (Exception ex) { Log.Warning("[Game] manifest build failed: {E}", ex.Message); }
            return m;
        }

        /// <summary>The same walk as <see cref="Build"/>, as files (WPF DtrhAssetManifest.EnumerateActive):
        /// full path, assets-relative path, bytes, picture or video. Local disk only; the transfer cache's
        /// planner is the consumer, so "the active pool" never means two things on this head.</summary>
        internal static IEnumerable<(string Full, string Rel, long Bytes, bool IsImage)> EnumerateActive(string? root, IEnumerable<string>? disabledPaths)
        {
            if (string.IsNullOrEmpty(root)) yield break;
            var disabled = new HashSet<string>((disabledPaths ?? Array.Empty<string>()).Select(p => p.Replace('\\', '/')),
                StringComparer.OrdinalIgnoreCase);
            int accepted = 0;
            foreach (var isImage in new[] { true, false })
            {
                var dir = Path.Combine(root, isImage ? "images" : "videos");
                if (!Directory.Exists(dir)) continue;
                var exts = isImage ? ImageExts : VideoExts;
                long cap = isImage ? MaxImageBytes : MaxVideoBytes;
                foreach (var f in Walk(dir, 0))
                {
                    if (accepted >= MaxEntries * 2) break;
                    if (!exts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
                    string rel;
                    try { rel = Path.GetRelativePath(root, f).Replace('\\', '/'); }
                    catch { continue; }
                    if (disabled.Contains(rel)) continue;
                    long len;
                    try { len = new FileInfo(f).Length; } catch { continue; }
                    if (len <= 0 || len > cap) continue;
                    accepted++;
                    yield return (f, rel, len, isImage);
                }
            }
        }

        /// <summary>WPF DtrhAssetManifest.Build: the live library through the shared asset server, then
        /// the remote tail ON TOP of it, after the downsample (a bounded handful: sampling it against a
        /// 5000-file library would lose it).</summary>
        internal static Manifest BuildLive()
        {
            var m = BuildLocal();
            int remote = AppendRemote(m, CoreSettings.Current);
            if (remote > 0) Log.Information("[Game] manifest: +{N} remote", remote);
            return m;
        }

        /// <summary>The local half only (WPF EnumerateActive's pool): for a consumer that needs files
        /// behind every entry, never a CDN url.</summary>
        internal static Manifest BuildLocal()
        {
            var server = ConditioningControlPanel.Avalonia.Platform.WebAssetServer.Shared;
            return Build(CorePaths.EffectiveAssets, server.AssetUrl, CoreSettings.Current.DisabledAssetPaths);
        }

        private static IEnumerable<string> Walk(string dir, int depth)
        {
            if (depth > MaxWalkDepth) yield break;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); } catch { yield break; }
            foreach (var f in files) yield return f;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(dir); } catch { yield break; }
            foreach (var d in subs)
            {
                if (Path.GetFileName(d).StartsWith('.')) continue;
                foreach (var f in Walk(d, depth + 1)) yield return f;
            }
        }

        private static bool IsMediaLike(string ext) =>
            ext is ".wmv" or ".avi" or ".mkv" or ".mov" or ".flv" or ".mpg" or ".mpeg" or ".bmp" or ".tiff" or ".heic";

        private static void Downsample<T>(List<T> list, int keep, Random rng)
        {
            if (list.Count <= keep) return;
            for (int i = 0; i < keep; i++)
            {
                int j = i + rng.Next(list.Count - i);
                (list[i], list[j]) = (list[j], list[i]);
            }
            list.RemoveRange(keep, list.Count - keep);
        }
    }
}
