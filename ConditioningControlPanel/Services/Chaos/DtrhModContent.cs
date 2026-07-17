using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Optional per-mod DTRH content: an installed .ccpmod may ship a
/// <c>resources/dtrh/</c> folder that re-skins the descent with the mod's own
/// voice clips, visuals, drone bed and VN portrait. Discovered at Launch time,
/// mapped into the page as the <c>ccp.mod</c> virtual host, and described to the
/// page via the init message's <c>modContent</c> field (null = no mod content,
/// page behaves exactly as before).
///
/// Layout inside the mod:
///   resources/dtrh/
///     dtrh.json                (optional manifest, all fields optional)
///     barks/fall_drift_001.mp3 (descent voice; pool = filename prefix:
///                               fall_{drift|sensory|descent|babble|resolve}_*)
///     images/ videos/          (descent visuals, merged into the media manifest)
///     audio/drone.mp3          (drone-bed override, declared in dtrh.json)
///     portrait/default.png     (+ optional per-emote stills) for the VN overlay
///
/// Same never-throws discipline as <see cref="DtrhAssetManifest"/>: any missing
/// or hostile piece degrades to today's behavior, never a failed launch.
/// </summary>
internal static class DtrhModContent
{
    private const string ModHost = "ccp.mod";
    private static readonly string[] AudioExts = { ".mp3", ".ogg", ".wav" };
    private static readonly string[] PoolIds = { "drift", "sensory", "descent", "babble", "resolve" };
    private const int MaxClipsPerPool = 200;
    private const long MaxPortraitBytes = 10L * 1024 * 1024;
    // Descent media mirrors the preset manifest's own constraints.
    private static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private static readonly string[] VideoExts = { ".mp4", ".webm", ".m4v" };
    private const long MaxImageBytes = 50L * 1024 * 1024;
    private const long MaxVideoBytes = 500L * 1024 * 1024;

    /// <summary>The mod's dtrh.json (all fields optional; unknown values degrade to defaults).</summary>
    public sealed class DtrhModManifest
    {
        [JsonProperty("tintA")] public string? TintA { get; set; }
        [JsonProperty("tintB")] public string? TintB { get; set; }
        [JsonProperty("imageMode")] public string? ImageMode { get; set; }   // mix | replace (default mix)
        [JsonProperty("driftMode")] public string? DriftMode { get; set; }   // replace | mix (default replace)
        [JsonProperty("drone")] public string? Drone { get; set; }           // relative path under dtrh/
    }

    /// <summary>The active mod's resources/dtrh folder, or null when the mod is a
    /// built-in (no InstalledPath) or ships no DTRH content — the exact today-behavior
    /// fallback. Never throws.</summary>
    public static string? ModDtrhRoot()
    {
        try
        {
            var installed = App.Mods?.ActiveMod?.InstalledPath;
            if (string.IsNullOrEmpty(installed)) return null;
            var root = Path.Combine(installed, "resources", "dtrh");
            return Directory.Exists(root) ? root : null;
        }
        catch { return null; }
    }

    /// <summary>Build the init payload describing the mod's DTRH content, or null when
    /// there is none. URLs point at the ccp.mod virtual host (mapped to the dtrh folder),
    /// so the page never sees a disk path. Never throws.</summary>
    public static object? BuildInitPayload()
    {
        try
        {
            var root = ModDtrhRoot();
            if (root == null) return null;
            var mod = App.Mods!.ActiveMod;
            var cfg = LoadManifest(root);

            var pools = CollectDriftPools(root);
            var portrait = CollectPortrait(root);
            var droneUrl = ResolveDrone(root, cfg.Drone);
            var tint = ParseTintPair(cfg.TintA, cfg.TintB);

            bool hasAnything = pools.Values.Any(p => p.Count > 0) || portrait != null || droneUrl != null || tint != null;
            if (!hasAnything && !HasAnyMedia(root)) return null;

            App.Logger?.Information(
                "DtrhModContent: mod '{Id}' ships dtrh content ({Clips} clips, portrait={P}, drone={D}, tint={T})",
                mod.Id, pools.Values.Sum(p => p.Count), portrait != null, droneUrl != null, tint != null);

            return new
            {
                id = mod.Id,
                companion = (mod.Manifest.Identity?.CompanionName ?? mod.Name ?? "").ToUpperInvariant(),
                tint,
                droneUrl,
                drift = new
                {
                    mode = NormalizeMode(cfg.DriftMode, defaultMode: "replace"),
                    pools = pools.ToDictionary(kv => kv.Key, kv => kv.Value.Select(f => new
                    {
                        file = Path.GetFileName(f),
                        url = ToModUrl(root, f),
                    })),
                },
                portrait,
            };
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("DtrhModContent.BuildInitPayload failed: {E}", ex.Message);
            return null;
        }
    }

    /// <summary>Merge the mod's descent images/videos into the preset media manifest.
    /// imageMode "replace" swaps the preset pool out (only when the mod actually ships
    /// decodable media of that kind); "mix" (default) appends. Entry names get a
    /// "mod:" prefix so the engagement stats store can't collide with preset filenames.
    /// Never throws.</summary>
    public static void MergeMedia(DtrhAssetManifest.Manifest m)
    {
        try
        {
            var root = ModDtrhRoot();
            if (root == null) return;
            var cfg = LoadManifest(root);
            bool replace = NormalizeMode(cfg.ImageMode, defaultMode: "mix") == "replace";

            var images = CollectMedia(Path.Combine(root, "images"), root, ImageExts, MaxImageBytes);
            var videos = CollectMedia(Path.Combine(root, "videos"), root, VideoExts, MaxVideoBytes);
            if (images.Count == 0 && videos.Count == 0) return;

            if (replace && images.Count > 0) m.Images.Clear();
            if (replace && videos.Count > 0) m.Videos.Clear();
            m.Images.AddRange(images);
            m.Videos.AddRange(videos);
            App.Logger?.Information("DtrhModContent: merged {I} mod images, {V} mod videos ({Mode})",
                images.Count, videos.Count, replace ? "replace" : "mix");
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("DtrhModContent.MergeMedia failed: {E}", ex.Message);
        }
    }

    // ============================ pieces ============================

    private static string NormalizeMode(string? mode, string defaultMode)
    {
        var m = mode?.Trim().ToLowerInvariant();
        return m is "mix" or "replace" ? m : defaultMode;
    }

    private static DtrhModManifest LoadManifest(string root)
    {
        try
        {
            var path = Path.Combine(root, "dtrh.json");
            if (!File.Exists(path)) return new DtrhModManifest();
            var parsed = JsonConvert.DeserializeObject<DtrhModManifest>(File.ReadAllText(path));
            if (parsed == null)
            {
                App.Logger?.Warning("DtrhModContent: dtrh.json parsed to null - using defaults");
                return new DtrhModManifest();
            }
            return parsed;
        }
        catch (Exception ex)
        {
            // A corrupt manifest must never take the descent down with it.
            App.Logger?.Warning("DtrhModContent: dtrh.json unreadable ({E}) - using defaults", ex.Message);
            return new DtrhModManifest();
        }
    }

    private static Dictionary<string, List<string>> CollectDriftPools(string root)
    {
        var pools = PoolIds.ToDictionary(id => id, _ => new List<string>());
        var dir = Path.Combine(root, "barks");
        if (!Directory.Exists(dir)) return pools;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir); } catch { return pools; }
        foreach (var f in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (!AudioExts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
            var pool = PoolFor(Path.GetFileNameWithoutExtension(f));
            if (pools[pool].Count >= MaxClipsPerPool) continue;
            pools[pool].Add(f);
        }
        return pools;
    }

    /// <summary>fall_drift_* → drift, fall_sensory_* → sensory, ... anything else → drift,
    /// so an audio-only creator can drop mp3s named by slot and never write metadata.</summary>
    private static string PoolFor(string stem)
    {
        foreach (var id in PoolIds)
            if (stem.StartsWith("fall_" + id + "_", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("fall_" + id, StringComparison.OrdinalIgnoreCase))
                return id;
        return "drift";
    }

    private static object? CollectPortrait(string root)
    {
        try
        {
            var dir = Path.Combine(root, "portrait");
            if (!Directory.Exists(dir)) return null;
            string? defaultUrl = null;
            var emotes = new Dictionary<string, string>();
            foreach (var f in Directory.EnumerateFiles(dir, "*.png"))
            {
                long len;
                try { len = new FileInfo(f).Length; } catch { continue; }
                if (len <= 0 || len > MaxPortraitBytes) continue;
                var stem = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                var url = ToModUrl(root, f);
                if (stem == "default") defaultUrl = url;
                else emotes[stem] = url;
            }
            // No default still = no portrait override (an emote-only folder can't
            // guarantee a frame for every beat).
            if (defaultUrl == null) return null;
            return new { defaultUrl, emotes };
        }
        catch { return null; }
    }

    private static string? ResolveDrone(string root, string? rel)
    {
        if (string.IsNullOrWhiteSpace(rel)) return null;
        try
        {
            // Same spirit as ModResourceResolver: relative, inside the dtrh folder, audio only.
            if (rel.Contains("..") || Path.IsPathRooted(rel))
            {
                App.Logger?.Warning("DtrhModContent: rejected drone path '{P}'", rel);
                return null;
            }
            var full = Path.GetFullPath(Path.Combine(root, rel));
            if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) return null;
            if (!AudioExts.Contains(Path.GetExtension(full).ToLowerInvariant())) return null;
            if (!File.Exists(full)) return null;
            return ToModUrl(root, full);
        }
        catch { return null; }
    }

    /// <summary>#RRGGBB pair → { a: "r,g,b", b: "r,g,b" } (the page's --vnA/--vnB format),
    /// or null when either color is missing/invalid.</summary>
    private static object? ParseTintPair(string? a, string? b)
    {
        var ca = ParseHexRgb(a);
        var cb = ParseHexRgb(b) ?? ca;   // one color is enough - reuse it for both stops
        if (ca == null) return null;
        return new { a = ca, b = cb };
    }

    private static string? ParseHexRgb(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.Trim();
        if (hex.Length != 7 || hex[0] != '#') return null;
        try
        {
            int r = Convert.ToInt32(hex.Substring(1, 2), 16);
            int g = Convert.ToInt32(hex.Substring(3, 2), 16);
            int bl = Convert.ToInt32(hex.Substring(5, 2), 16);
            return $"{r},{g},{bl}";
        }
        catch { return null; }
    }

    private static bool HasAnyMedia(string root)
    {
        try
        {
            foreach (var sub in new[] { "images", "videos" })
            {
                var dir = Path.Combine(root, sub);
                if (Directory.Exists(dir) && Directory.EnumerateFiles(dir).Any()) return true;
            }
        }
        catch { }
        return false;
    }

    private static List<DtrhAssetManifest.Entry> CollectMedia(string dir, string root, string[] exts, long cap)
    {
        var list = new List<DtrhAssetManifest.Entry>();
        if (!Directory.Exists(dir)) return list;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir); } catch { return list; }
        foreach (var f in files)
        {
            if (!exts.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
            long len;
            try { len = new FileInfo(f).Length; } catch { continue; }
            if (len <= 0 || len > cap) continue;
            list.Add(new DtrhAssetManifest.Entry("mod:" + Path.GetFileName(f), ToModUrl(root, f)));
        }
        return list;
    }

    private static string ToModUrl(string root, string fullPath)
    {
        var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        var escaped = string.Join('/', rel.Split('/').Select(Uri.EscapeDataString));
        return "https://" + ModHost + "/" + escaped;
    }
}
