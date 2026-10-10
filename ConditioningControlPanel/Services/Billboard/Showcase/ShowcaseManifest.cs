using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.Billboard.Showcase
{
    /// <summary>Which plan a showcased feature belongs to.</summary>
    public enum ShowcaseTier { Basic, Prime }

    /// <summary>
    /// One clip in <c>showcase.json</c>. The video and its poster are release assets that sit
    /// beside the manifest, named by <see cref="File"/> and <see cref="Poster"/>.
    /// </summary>
    public sealed record ShowcaseClip(
        string Id,
        ShowcaseTier Tier,
        string File,
        long Bytes,
        string Sha256,
        string? Poster,
        long PosterBytes,
        string? PosterSha256,
        double Seconds);

    /// <summary>The parsed manifest: version plus the clips that passed validation.</summary>
    public sealed record ShowcaseManifest(int Version, IReadOnlyList<ShowcaseClip> Clips)
    {
        public static readonly ShowcaseManifest Empty = new(0, Array.Empty<ShowcaseClip>());
    }

    /// <summary>
    /// Reads <c>showcase.json</c>. Pure and forgiving: a clip with a bad field is dropped, the
    /// rest survive; a manifest that is not JSON at all reads as <see cref="ShowcaseManifest.Empty"/>.
    /// Shape:
    /// <code>
    /// { "version": 1, "clips": [ { "id": "breakout", "tier": "prime", "file": "showcase-breakout.mp4",
    ///   "bytes": 456789, "sha256": "...", "poster": "showcase-breakout.jpg", "posterBytes": 23456,
    ///   "posterSha256": "...", "seconds": 7.0 } ] }
    /// </code>
    /// </summary>
    public static class ShowcaseManifestParser
    {
        /// <summary>A clip larger than this is refused (the plan is 300-600 KB; this is the safety cap).</summary>
        public const long MaxClipBytes = 4L * 1024 * 1024;

        /// <summary>A poster larger than this is dropped (the clip still shows, over a plain field).</summary>
        public const long MaxPosterBytes = 512L * 1024;

        private static readonly Regex IdPattern = new("^[a-z0-9_]{1,32}$", RegexOptions.CultureInvariant);
        private static readonly Regex FilePattern = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$", RegexOptions.CultureInvariant);
        private static readonly Regex ShaPattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

        public static ShowcaseManifest Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return ShowcaseManifest.Empty;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return ShowcaseManifest.Empty;

                int version = root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number
                    && v.TryGetInt32(out var vi) ? vi : 0;

                var clips = new List<ShowcaseClip>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                if (root.TryGetProperty("clips", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        var clip = ReadClip(el);
                        if (clip != null && seen.Add(clip.Id)) clips.Add(clip);
                    }
                }
                return new ShowcaseManifest(version, clips);
            }
            catch (JsonException)
            {
                return ShowcaseManifest.Empty;
            }
        }

        public static bool IsSafeFileName(string? name) =>
            !string.IsNullOrEmpty(name) && FilePattern.IsMatch(name) && !name.Contains("..", StringComparison.Ordinal);

        internal static ShowcaseClip? ReadClip(JsonElement el)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;

            var id = Str(el, "id");
            if (id == null || !IdPattern.IsMatch(id)) return null;

            ShowcaseTier tier;
            switch (Str(el, "tier")?.ToLowerInvariant())
            {
                case "basic": tier = ShowcaseTier.Basic; break;
                case "prime": tier = ShowcaseTier.Prime; break;
                default: return null;
            }

            var file = Str(el, "file");
            if (!IsSafeFileName(file)) return null;

            var sha = Str(el, "sha256")?.ToLowerInvariant();
            if (sha == null || !ShaPattern.IsMatch(sha)) return null;

            long bytes = Long(el, "bytes");
            if (bytes <= 0 || bytes > MaxClipBytes) return null;

            // The poster is optional and travels on its own: a bad poster drops the poster, not the clip.
            string? poster = Str(el, "poster");
            string? posterSha = Str(el, "posterSha256")?.ToLowerInvariant();
            long posterBytes = Long(el, "posterBytes");
            if (!IsSafeFileName(poster) || posterSha == null || !ShaPattern.IsMatch(posterSha)
                || posterBytes <= 0 || posterBytes > MaxPosterBytes
                || string.Equals(poster, file, StringComparison.OrdinalIgnoreCase))
            {
                poster = null;
                posterSha = null;
                posterBytes = 0;
            }

            double seconds = el.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetDouble() : 0;
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;

            return new ShowcaseClip(id, tier, file!, bytes, sha, poster, posterBytes, posterSha, seconds);
        }

        private static string? Str(JsonElement el, string name) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        private static long Long(JsonElement el, string name) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var l) ? l : 0;
    }
}
