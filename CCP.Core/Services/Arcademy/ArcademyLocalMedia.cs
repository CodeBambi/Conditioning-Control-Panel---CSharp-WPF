using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Quiz;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Arcademy
{
    /// <summary>
    /// The Arcademy host's LOCAL media rules, lifted from WPF 7.1.5 ArcademyHostService.cs so both
    /// heads and the unit tests share one copy: the init manifest (BuildLocalAssets :1617), the
    /// folder picker's counts (BuildLocalFolders :1543), the local sampler (SampleLocalAssets :5090),
    /// the request readers (ReadRequestSubs :4816, ReadTag :4838), the path guard
    /// (ResolveAssetsFolder :5185) and the phrase clip resolver (ResolveTriggerAudioUrl :1417).
    /// Pure: paths in, root-relative paths out. The head turns a relative path into its own url.
    /// </summary>
    internal static class ArcademyLocalMedia
    {
        /// <summary>THE ANIMATED-WEBP HINT (ccp-bugs#1086): appended to the url of a local .webp whose
        /// header says it animates, so the page's <c>/\.gif(\?|#|$)/</c> budgets meet it as a loop. A
        /// fragment, so the same bytes load.</summary>
        internal const string AnimatedImageHint = "#.gif";

        internal const int LocalAssetSample = 60;
        internal const int LocalFolderCap = 400;
        internal const int AnimatedProbeBudget = 200;
        internal const int TaggedSubCap = 64;

        internal static readonly string[] LocalLoopExts = { ".gif", ".mp4", ".webm", ".webp" };
        internal static readonly string[] LocalStillExts = { ".png", ".jpg", ".jpeg", ".webp" };
        private static readonly string[] SubAudioExts = { ".mp3", ".wav", ".ogg" };

        /// <summary>One sampled local file. <c>Rel</c> is relative to the assets root, forward slashes.</summary>
        internal sealed record LocalRow(string Rel, string Kind, bool Animated, string Src);

        internal static bool IsAnimatedLocalImage(string file)
            => Path.GetExtension(file).Equals(".webp", StringComparison.OrdinalIgnoreCase)
               && AnimatedWebp.IsAnimated(file);

        private static string Rel(string root, string file)
            => Path.GetRelativePath(root, file).Replace('\\', '/');

        // ---- init.settings.localAssets ---------------------------------------------------------

        /// <summary>A random slice of the library's images: (gifs, stills) as root-relative paths. An
        /// animated webp is moved to the gif list and flagged, so the caller stamps the hint.</summary>
        internal static (List<(string Rel, bool Animated)> Gifs, List<string> Stills) BuildLocalAssets(
            string? assetsRoot, IEnumerable<string>? disabledPaths, Random? rng = null)
        {
            var gifsOut = new List<(string, bool)>();
            var stillsOut = new List<string>();
            if (string.IsNullOrEmpty(assetsRoot)) return (gifsOut, stillsOut);
            var gifs = new List<string>();
            var stills = new List<string>();
            var imagesRoot = Path.Combine(assetsRoot, "images");
            var disabled = IntakeRun.DisabledAssetSet(disabledPaths);
            if (Directory.Exists(imagesRoot))
            {
                foreach (var file in Directory.EnumerateFiles(imagesRoot, "*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".gif" && ext is not (".png" or ".jpg" or ".jpeg" or ".webp")) continue;
                    if (!IntakeRun.IsAssetActive(disabled, assetsRoot, file)) continue;
                    (ext == ".gif" ? gifs : stills).Add(file);
                }
            }

            rng ??= new Random();
            static List<string> Sample(List<string> pool, Random r, int take)
            {
                // partial Fisher-Yates: a random slice without shuffling the whole list
                for (int i = 0; i < Math.Min(take, pool.Count); i++)
                {
                    int j = r.Next(i, pool.Count);
                    (pool[i], pool[j]) = (pool[j], pool[i]);
                }
                return pool.GetRange(0, Math.Min(take, pool.Count));
            }

            foreach (var file in Sample(gifs, rng, LocalAssetSample)) gifsOut.Add((Rel(assetsRoot, file), false));
            // Only the SAMPLED stills are probed: a header read per file across a whole library is
            // exactly the boot cost this manifest exists to avoid.
            foreach (var file in Sample(stills, rng, LocalAssetSample))
            {
                if (IsAnimatedLocalImage(file)) gifsOut.Add((Rel(assetsRoot, file), true));
                else stillsOut.Add(Rel(assetsRoot, file));
            }
            return (gifsOut, stillsOut);
        }

        // ---- init.settings.localFolders --------------------------------------------------------

        /// <summary>Folders under the assets root that hold media, with RECURSIVE counts per kind:
        /// <c>[{path, gifs, stills, videos}]</c>. Honours the deselection blacklist.</summary>
        internal static JArray BuildLocalFolders(string? root, IEnumerable<string>? disabledPaths)
        {
            var arr = new JArray();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return arr;

            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            var disabled = IntakeRun.DisabledAssetSet(disabledPaths);
            var counts = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);   // [gifs, stills, videos]

            int[] Slot(string rel)
            {
                if (!counts.TryGetValue(rel, out var slot)) counts[rel] = slot = new int[3];
                return slot;
            }

            foreach (var top in new[] { "images", "videos" })
            {
                var topAbs = Path.Combine(rootFull, top);
                if (!Directory.Exists(topAbs)) continue;
                Slot(top);   // a root with nothing in it is still a real choice ("all of my images")

                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(topAbs, "*", SearchOption.AllDirectories); }
                catch (Exception ex) { Log.Debug("[Arcademy] local folders {Top}: {E}", top, ex.Message); continue; }

                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    int bucket = ext switch
                    {
                        ".gif" => 0,
                        ".png" or ".jpg" or ".jpeg" or ".webp" => 1,
                        ".mp4" or ".webm" => 2,
                        _ => -1,
                    };
                    if (bucket < 0) continue;
                    if (!IntakeRun.IsAssetActive(disabled, rootFull, file)) continue;

                    // Credit the file to its own folder AND to every folder above it up to the root.
                    var dir = Path.GetDirectoryName(file);
                    while (!string.IsNullOrEmpty(dir))
                    {
                        string rel;
                        try { rel = Path.GetRelativePath(rootFull, dir).Replace('\\', '/'); }
                        catch { break; }
                        if (rel.Length == 0 || rel == "." || rel.StartsWith("..", StringComparison.Ordinal)) break;
                        Slot(rel)[bucket]++;
                        if (string.Equals(rel, top, StringComparison.OrdinalIgnoreCase)) break;
                        dir = Path.GetDirectoryName(dir);
                    }
                }
            }

            var keys = new List<string>(counts.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in keys)
            {
                var slot = counts[rel];
                arr.Add(new JObject { ["path"] = rel, ["gifs"] = slot[0], ["stills"] = slot[1], ["videos"] = slot[2] });
                if (arr.Count >= LocalFolderCap) break;
            }
            return arr;
        }

        // ---- local-sample-request --------------------------------------------------------------

        /// <summary>Sample a local pile: a folder list (or one asset preset) in, rows out. Seeded off
        /// the reqId so a retake of the same ask deals the same slice.</summary>
        internal static List<LocalRow> SampleLocalAssets(string? root, string reqId, int count, string kind,
            List<string> folders, string presetId, IEnumerable<string>? disabledPaths, IEnumerable<AssetPreset>? presets)
        {
            var rows = new List<LocalRow>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return rows;

            var exts = kind == "loop" ? LocalLoopExts : LocalStillExts;

            // Which deselection list applies: a named preset brings its own, otherwise the live tree.
            string? presetSrc = null;
            if (presetId.Length > 0)
            {
                foreach (var p in presets ?? Enumerable.Empty<AssetPreset>())
                {
                    if (p == null || !string.Equals(p.Id, presetId, StringComparison.Ordinal)) continue;
                    disabledPaths = p.DisabledAssetPaths;
                    presetSrc = "preset:" + p.Id;
                    break;
                }
                if (presetSrc == null) Log.Debug("[Arcademy] local sample named unknown preset {Id}", presetId);
            }
            var disabled = IntakeRun.DisabledAssetSet(disabledPaths);

            var searchRoots = new List<string>();
            foreach (var rel in folders)
            {
                var abs = ResolveAssetsFolder(root, rel);
                if (abs != null) searchRoots.Add(abs);
            }
            if (searchRoots.Count == 0)
            {
                foreach (var top in new[] { "images", "videos" })
                {
                    var abs = Path.Combine(root, top);
                    if (Directory.Exists(abs)) searchRoots.Add(abs);
                }
            }

            var pool = new List<string>();
            var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in searchRoots)
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories); }
                catch (Exception ex) { Log.Debug("[Arcademy] local sample walk of {Dir} failed: {E}", dir, ex.Message); continue; }
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (Array.IndexOf(exts, ext) < 0) continue;
                    if (!IntakeRun.IsAssetActive(disabled, root, file)) continue;
                    if (!seenFiles.Add(file)) continue;   // two picked folders can nest
                    pool.Add(file);
                }
            }
            if (pool.Count == 0) return rows;
            pool.Sort(StringComparer.OrdinalIgnoreCase);   // a stable walk order, so the seed means the same slice on any disk

            var rng = new Random(StableSeed(reqId));
            int probed = 0;
            for (int i = 0; i < pool.Count && rows.Count < count; i++)
            {
                int j = rng.Next(i, pool.Count);
                (pool[i], pool[j]) = (pool[j], pool[i]);

                var file = pool[i];
                bool isWebp = Path.GetExtension(file).Equals(".webp", StringComparison.OrdinalIgnoreCase);
                // Bounded: past the budget an unprobed webp reads as "still", the safe direction.
                bool animated = isWebp && probed++ < AnimatedProbeBudget && AnimatedWebp.IsAnimated(file);
                // A still webp has no business in the loop lane.
                if (kind == "loop" && isWebp && !animated) continue;

                rows.Add(new LocalRow(Rel(root, file), animated ? "loop" : kind, animated, presetSrc ?? RelativeFolder(root, file)));
            }
            return rows;
        }

        /// <summary>A page-supplied folder resolved under the assets root, or null when it does not
        /// exist or tries to climb out of it. Never trust a path that arrived over the bridge.</summary>
        internal static string? ResolveAssetsFolder(string root, string rel)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rel)) return null;
                var cleaned = rel.Replace('\\', '/').Trim('/');
                if (cleaned.Length == 0) return null;
                var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
                var abs = Path.GetFullPath(Path.Combine(rootFull, cleaned));
                if (!abs.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(abs, rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warning("[Arcademy] local sample refused a path outside the assets root: {Rel}", rel);
                    return null;
                }
                return Directory.Exists(abs) ? abs : null;
            }
            catch { return null; }
        }

        private static string RelativeFolder(string root, string file)
        {
            try
            {
                var dir = Path.GetDirectoryName(file);
                if (string.IsNullOrEmpty(dir)) return "";
                return Path.GetRelativePath(root, dir).Replace('\\', '/');
            }
            catch { return ""; }
        }

        /// <summary>FNV-1a over the reqId: a stable seed, so the same ask deals the same slice.</summary>
        internal static int StableSeed(string? s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (var ch in s ?? "")
                {
                    h ^= ch;
                    h *= 16777619;
                }
                return (int)(h & 0x7FFFFFFF);
            }
        }

        // ---- request readers -------------------------------------------------------------------

        /// <summary>The request's sub list, sanitized and de-duplicated; null when the message carries no
        /// <c>subs</c> array at all (every other class stays on the app-wide pull), and an EMPTY list when
        /// it carried one that was malformed or sanitized away to nothing (refused, never waved through).</summary>
        internal static List<string>? ReadRequestSubs(JObject o)
        {
            var field = o["subs"];
            if (field == null || field.Type == JTokenType.Null) return null;
            if (field is not JArray arr) return new List<string>();
            var clean = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in arr)
            {
                string? raw;
                try { raw = (string?)t; } catch { continue; }
                var name = FypOnlineCoordinator.SanitizeSub(raw);
                if (name == null || !seen.Add(name)) continue;
                clean.Add(name);
                if (clean.Count >= TaggedSubCap) break;
            }
            return clean;
        }

        /// <summary>The pile name, normalised to something safe as a dictionary key and a tenant suffix.</summary>
        internal static string ReadTag(JObject o)
        {
            string raw;
            try { raw = ((string?)o["tag"] ?? "").Trim().ToLowerInvariant(); } catch { raw = ""; }
            var kept = new string(raw.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
            if (kept.Length == 0) return "untagged";
            return kept.Length > 24 ? kept[..24] : kept;
        }

        internal static string TaggedBufferKey(string tag, string kind) => "sort:" + tag + "|" + kind;

        internal static List<string> ReadStringArray(JToken? token)
        {
            var list = new List<string>();
            if (token is not JArray arr) return list;
            foreach (var t in arr)
            {
                string s;
                try { s = ((string?)t ?? "").Trim(); } catch { continue; }
                if (s.Length > 0) list.Add(s);
            }
            return list;
        }

        internal static string MimeFor(string url, string kind)
        {
            var cut = url.IndexOfAny(new[] { '?', '#' });
            var bare = cut < 0 ? url : url[..cut];
            return Path.GetExtension(bare).ToLowerInvariant() switch
            {
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".gif" => "image/gif",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".jpg" or ".jpeg" => "image/jpeg",
                _ => kind == "loop" ? "video/mp4" : "image/jpeg",
            };
        }

        // ---- init.triggers[].audio -------------------------------------------------------------

        /// <summary>The two folders a phrase clip may come from, already gated: nothing unless the
        /// whisper mute is off; the active mod's own folder first; the bundled Bambi <c>sub_audio</c>
        /// clips only for the mods <see cref="ModAudioPolicy.UsesSharedSubAudio"/> allows (never CCP
        /// Default, never Locked). Recorded clips only: a phrase with no file stays silent.</summary>
        internal static (string? ModDir, string? SharedDir) TriggerAudioDirs(bool audible, string? modId,
            string? modInstalledPath, string sharedSubAudioDir)
        {
            if (!audible) return (null, null);
            string? modDir = null;
            try
            {
                if (!string.IsNullOrEmpty(modInstalledPath))
                {
                    var root = Path.Combine(modInstalledPath, "resources", "sounds", "flashes_audio");
                    if (Directory.Exists(root)) modDir = root;
                }
            }
            catch { modDir = null; }
            return (modDir, ModAudioPolicy.UsesSharedSubAudio(modId) ? sharedSubAudioDir : null);
        }

        /// <summary>Resolve one phrase's whisper clip: (true = the mod's folder, file name) or null.
        /// Exact filename match against the case / apostrophe variants first, then a case-insensitive
        /// scan, the mod's folder winning over the bundled one (SubliminalService.FindLinkedAudio).</summary>
        internal static (bool FromMod, string FileName)? ResolveTriggerAudio(string? text, string? modDir, string? sharedDir)
        {
            var clean = (text ?? string.Empty).Trim();
            if (clean.Length == 0 || clean.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            var variants = new[]
            {
                clean,
                clean.ToUpperInvariant(),
                clean.ToLowerInvariant(),
                clean.Replace('’', '\''),
                clean.Replace('\'', '’'),
                clean.ToUpperInvariant().Replace('’', '\''),
            };
            var exts = new[] { ".mp3", ".wav", ".ogg", ".MP3", ".WAV", ".OGG" };

            foreach (var (dir, fromMod) in new[] { (modDir, true), (sharedDir, false) })
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                foreach (var v in variants)
                    foreach (var ext in exts)
                    {
                        var p = Path.Combine(dir, v + ext);
                        // The name as it is on disk (Windows matches any case; the url must carry the real one).
                        if (File.Exists(p)) return (fromMod, OnDiskName(dir, v + ext));
                    }

                try
                {
                    var norm = clean.ToUpperInvariant().Replace('’', '\'');
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        if (Array.IndexOf(SubAudioExts, Path.GetExtension(f).ToLowerInvariant()) < 0) continue;
                        var name = Path.GetFileNameWithoutExtension(f).ToUpperInvariant().Replace('’', '\'');
                        if (name == norm) return (fromMod, Path.GetFileName(f));
                    }
                }
                catch (Exception ex) { Log.Debug("[Arcademy] trigger clip scan: {E}", ex.Message); }
            }
            return null;
        }

        private static string OnDiskName(string dir, string name)
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, name))
                    return Path.GetFileName(f);
            }
            catch { }
            return name;
        }
    }
}
