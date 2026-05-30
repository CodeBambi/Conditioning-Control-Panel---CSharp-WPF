using System.IO;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Which detection tier supplied a resolved enhancement. Mirrors the tiers
    /// the Deeper player has always walked for local media; surfaced so callers
    /// can show the right "embedded / sidecar / library" badge.
    /// </summary>
    public enum EnhancementDiscoverySource
    {
        None,
        Embedded,
        Sidecar,
        Library
    }

    /// <summary>
    /// Result of <see cref="EnhancementResolver.ResolveForLocalMedia"/>.
    /// For <see cref="EnhancementDiscoverySource.Embedded"/> the parsed
    /// enhancement is returned in-memory (<see cref="Enhancement"/>); for
    /// Sidecar/Library only the on-disk .ccpenh.json path is returned
    /// (<see cref="FilePath"/>) so the caller loads it through its own host
    /// exactly as before (preserving validation + Loaded events).
    /// </summary>
    public readonly struct ResolvedEnhancement
    {
        public Enhancement? Enhancement { get; }
        public string? FilePath { get; }
        public EnhancementDiscoverySource Source { get; }

        public ResolvedEnhancement(Enhancement? enhancement, string? filePath, EnhancementDiscoverySource source)
        {
            Enhancement = enhancement;
            FilePath = filePath;
            Source = source;
        }

        public bool Found => Source != EnhancementDiscoverySource.None;

        public static ResolvedEnhancement NotFound { get; } =
            new(null, null, EnhancementDiscoverySource.None);
    }

    /// <summary>
    /// Shared local-media detection ladder, lifted verbatim out of
    /// EnhancementPlayerWindow.TryAutoLoadEnhancement so the Deeper player and
    /// the mandatory/asset video bridge resolve enhancements identically.
    ///
    /// Ordered strategy (first hit wins):
    ///   (0) Embedded metadata box in the media file itself (a self-contained
    ///       export beats any stale sidecar sharing its basename).
    ///   (1) Side-by-side sidecar: foo.mp4 -> foo.ccpenh.json next to it.
    ///   (2) Library catalogue match by media_source pattern.
    ///
    /// This method performs NO side effects (no library promotion, no host
    /// loading, no UI). It may throw on unexpected IO errors — callers wrap it
    /// (the player relies on its existing outer try/catch; the bridge guards
    /// its own call).
    /// </summary>
    public static class EnhancementResolver
    {
        /// <summary>
        /// Canonical "is this a local video file" check (by extension). Shared
        /// so the library media_type used for matching stays consistent across
        /// the player and the video bridge.
        /// </summary>
        public static bool IsLocalVideoFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi" or ".m4v";
        }

        public static ResolvedEnhancement ResolveForLocalMedia(string mediaPath)
        {
            if (string.IsNullOrEmpty(mediaPath)) return ResolvedEnhancement.NotFound;

            // 0) Embedded metadata bundled into the media file itself.
            if (EnhancementMediaBundler.IsSupportedExtension(mediaPath)
                && EnhancementMediaBundler.TryExtract(mediaPath, out var embedded, out _)
                && embedded != null)
            {
                return new ResolvedEnhancement(embedded, null, EnhancementDiscoverySource.Embedded);
            }

            // 1) Side-by-side sidecar.
            var dir = Path.GetDirectoryName(mediaPath);
            var baseName = Path.GetFileNameWithoutExtension(mediaPath);
            if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(baseName))
            {
                var candidate = Path.Combine(dir, baseName + ".ccpenh.json");
                if (File.Exists(candidate))
                    return new ResolvedEnhancement(null, candidate, EnhancementDiscoverySource.Sidecar);
            }

            // 2) Library catalogue match by media_source pattern.
            var mediaType = IsLocalVideoFile(mediaPath) ? MediaTypes.Video : MediaTypes.Audio;
            var match = App.EnhancementLibrary?.FindMatch(mediaPath, mediaType);
            if (match != null)
                return new ResolvedEnhancement(null, match.FilePath, EnhancementDiscoverySource.Library);

            return ResolvedEnhancement.NotFound;
        }
    }
}
