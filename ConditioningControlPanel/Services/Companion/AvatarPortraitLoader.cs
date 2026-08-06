using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Loads a mod's emotive-portrait avatar manifest (<c>avatar_manifest.json</c>) from disk,
    /// mirroring <see cref="Bark.BarkRuleLoader"/>'s two-tier path resolution. Returns an
    /// <see cref="AvatarPortraitSet"/> (manifest + on-disk portrait loader) or <c>null</c> when the
    /// active mod ships no manifest — in which case the avatar falls back to the legacy 4-pose path.
    /// Portraits are loaded as frozen <see cref="BitmapImage"/> straight from the absolute file path
    /// (NOT via pack:// — these are on-disk Content, like bark audio).
    /// </summary>
    public static class AvatarPortraitLoader
    {
        public const string ManifestFileName = "avatar_manifest.json";

        /// <summary>Packaged mod's manifest (InstalledPath/resources/sounds/companion_audio/). Null if none.</summary>
        public static string? ActiveModManifestPath
        {
            get
            {
                var modPath = App.Mods?.ActiveMod?.InstalledPath;
                if (string.IsNullOrEmpty(modPath)) return null;
                var p = Path.Combine(modPath, "resources", "sounds", "companion_audio", ManifestFileName);
                return File.Exists(p) ? p : null;
            }
        }

        /// <summary>Built-in mod's manifest from the embedded per-mod folder (.../companion_audio/mods/{modId}/). Null if none.</summary>
        public static string? EmbeddedModManifestPath
        {
            get
            {
                var modId = App.Mods?.ActiveModId;
                if (string.IsNullOrEmpty(modId)) return null;
                var p = CompanionPhraseService.ResolveCompanionAudioFile("mods", modId, ManifestFileName);
                return File.Exists(p) ? p : null;
            }
        }

        /// <summary>Resolve the active mod's manifest path (packaged first, then embedded). Null = no portrait system.</summary>
        public static string? ResolveManifestPath() => ActiveModManifestPath ?? EmbeddedModManifestPath;

        /// <summary>
        /// True if the active mod ships a portrait manifest AND its portraits are actually on disk.
        /// The manifest ships inside the app but the portrait PNGs arrive in a downloadable content
        /// pack, so a failed/skipped pack download leaves a valid manifest with zero images — and the
        /// avatar used to commit to portrait mode anyway, freezing on one undersized legacy pose.
        /// The disk check is cached per manifest path: this gate is polled on every avatar-set switch.
        /// </summary>
        public static bool HasManifestForActiveMod()
        {
            var path = ResolveManifestPath();
            return path != null && PortraitsPresent(path);
        }

        /// <summary>
        /// Load + parse the active mod's manifest into an <see cref="AvatarPortraitSet"/>. Never throws —
        /// a missing/garbled manifest, or one whose portraits never landed, returns null and the caller
        /// uses the legacy avatar path.
        /// </summary>
        public static AvatarPortraitSet? Load()
        {
            var path = ResolveManifestPath();
            if (path == null) return null;

            var manifest = ReadManifest(path);
            if (manifest == null) return null;

            var baseDir = Path.GetDirectoryName(path) ?? "";
            if (!PortraitsPresent(path, manifest)) return null;

            App.Logger?.Information(
                "AvatarPortraitLoader: loaded {Emotions} emotions, {Skins} skins, {Lines} lines from {Path}",
                manifest.Emotions.Count, manifest.Skins.Count, manifest.Lines.Count, path);
            return new AvatarPortraitSet(manifest, baseDir);
        }

        /// <summary>Read + validate a manifest file. Never throws; null = unusable.</summary>
        private static AvatarPortraitManifest? ReadManifest(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var manifest = JsonConvert.DeserializeObject<AvatarPortraitManifest>(json);
                if (manifest == null || manifest.Emotions.Count == 0 || manifest.Skins.Count == 0)
                {
                    App.Logger?.Warning("AvatarPortraitLoader: manifest at {Path} is empty/invalid", path);
                    return null;
                }
                return manifest;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "AvatarPortraitLoader: failed to load manifest at {Path}", path);
                return null;
            }
        }

        /// <summary>
        /// A half-unpacked pack is as unusable as a missing one, so require a few files rather than
        /// exactly one — plus the default/idle pose, which is what portrait mode shows first.
        /// </summary>
        private const int MinPortraitFiles = 4;

        // Verified-once cache. Only the last-resolved manifest is remembered because exactly one mod
        // is active at a time; a mod switch re-verifies. Invalidated when a content pack lands.
        private static string? _verifiedManifestPath;
        private static bool _verifiedHasPortraits;
        private static readonly object _verifyLock = new();

        /// <summary>
        /// Drop the cached "are the portraits on disk?" answer so a pack that lands mid-session is
        /// picked up. The avatar re-enters portrait mode on the next mod/avatar-set switch.
        /// </summary>
        public static void InvalidateAvailabilityCache()
        {
            lock (_verifyLock) { _verifiedManifestPath = null; }
        }

        private static bool PortraitsPresent(string manifestPath, AvatarPortraitManifest? preloaded = null)
        {
            lock (_verifyLock)
            {
                if (_verifiedManifestPath == manifestPath) return _verifiedHasPortraits;

                var manifest = preloaded ?? ReadManifest(manifestPath);
                bool present;
                try
                {
                    present = manifest != null &&
                              CountsAsPresent(manifest, Path.GetDirectoryName(manifestPath) ?? "", manifestPath);
                }
                catch (Exception ex)
                {
                    // This gate is polled from paths with no try/catch of their own; an odd manifest
                    // must degrade to the legacy avatar, not take the tube down.
                    App.Logger?.Warning(ex, "AvatarPortraitLoader: portrait availability check failed for {Path}", manifestPath);
                    present = false;
                }

                _verifiedManifestPath = manifestPath;
                _verifiedHasPortraits = present;
                return present;
            }
        }

        /// <summary>
        /// Stat the manifest's portraits until enough are found (early exit) — a full sweep only ever
        /// happens on the failure path, which is also the only path that needs an exact count to log.
        /// </summary>
        private static bool CountsAsPresent(AvatarPortraitManifest manifest, string baseDir, string manifestPath)
        {
            // The floor scales down for small manifests - a creator mod declaring 3 portraits must
            // still be able to enter portrait mode. A missing base pose is deliberately NOT a gate:
            // GetBucket's skin-0 -> idle fallback chain resolves a pose from whatever portraits
            // exist, which is exactly the case that chain was built for.
            int totalDeclared = 0;
            foreach (var skin in manifest.Skins)
                foreach (var kv in manifest.Emotions)
                    foreach (var p in kv.Value.Portraits)
                        if (!string.IsNullOrEmpty(p.File)) totalDeclared++;

            int floor = Math.Min(MinPortraitFiles, totalDeclared);
            if (floor == 0)
            {
                App.Logger?.Information(
                    "AvatarPortraitLoader: portrait avatar skipped for {Path} - the manifest declares no portrait files; using the animated avatar instead",
                    manifestPath);
                return false;
            }

            int found = 0;
            foreach (var skin in manifest.Skins)
            {
                var skinDir = Path.Combine(baseDir, (skin.Dir ?? "").Replace('/', Path.DirectorySeparatorChar));
                foreach (var kv in manifest.Emotions)
                {
                    foreach (var p in kv.Value.Portraits)
                    {
                        if (string.IsNullOrEmpty(p.File)) continue;
                        var abs = Path.Combine(skinDir, p.File);
                        // Same install-dir/content-pack split GetBucket handles.
                        if (!File.Exists(abs) && ContentLocator.Mirror(abs) == null) continue;
                        found++;
                        if (found >= floor) return true;
                    }
                }
            }

            App.Logger?.Information(
                "AvatarPortraitLoader: portrait avatar skipped for {Path} - {Count} of {Declared} declared portrait image file(s) on disk " +
                "(floor {Floor}; content pack missing?); using the animated avatar instead", manifestPath, found, totalDeclared, floor);
            return false;
        }
    }

    /// <summary>
    /// A loaded portrait avatar: the manifest plus lazy, frozen <see cref="BitmapImage"/> buckets
    /// keyed by (skin index, emotion). Missing files are skipped; an empty bucket falls back to the
    /// same emotion in skin 0, then to the idle emotion, so the avatar never shows nothing.
    /// </summary>
    public class AvatarPortraitSet
    {
        private readonly AvatarPortraitManifest _m;
        private readonly string _baseDir;
        private readonly Dictionary<string, BitmapImage[]> _cache = new();

        public AvatarPortraitSet(AvatarPortraitManifest manifest, string baseDir)
        {
            _m = manifest;
            _baseDir = baseDir;
        }

        public AvatarPortraitManifest Manifest => _m;
        public IReadOnlyList<AvatarSkin> Skins => _m.Skins;
        public int SkinCount => _m.Skins.Count;
        public string IdleEmotion => _m.IdleEmotion;
        public string DefaultEmotion => _m.DefaultEmotion;
        public AvatarDirector Director => _m.Director;

        /// <summary>Clamp an avatar-set index (0-based) into the valid skin range.</summary>
        public int ClampSkin(int skinIndex) =>
            SkinCount == 0 ? 0 : Math.Clamp(skinIndex, 0, SkinCount - 1);

        /// <summary>Emotion for a bark line (audio-filename stem), or null if unknown.</summary>
        public string? EmotionForLine(string? lineId)
        {
            if (string.IsNullOrEmpty(lineId)) return null;
            return _m.Lines.TryGetValue(lineId, out var line) ? line.Emotion : null;
        }

        /// <summary>
        /// Fallback emotion for a bark whose stem has no explicit <c>lines{}</c> override: map its
        /// free-text <c>mood</c> to a portrait emotion. Compound moods (e.g. "soft, possessive") are
        /// split on comma and the FIRST recognized token wins; anything unrecognized defaults to
        /// <c>neutral</c>. This is the base layer for the expanded bark set — hand-authored
        /// <c>lines{}</c> entries still override it. Returned name need not exist in this manifest;
        /// <see cref="GetBucket"/> falls back gracefully if a skin lacks it.
        /// </summary>
        public string EmotionForMood(string? mood)
        {
            if (string.IsNullOrWhiteSpace(mood)) return "neutral";
            foreach (var raw in mood.Split(','))
            {
                var token = raw.Trim().ToLowerInvariant();
                if (token.Length == 0) continue;
                if (MoodEmotionMap.TryGetValue(token, out var emo)) return emo;
            }
            return "neutral";
        }

        /// <summary>
        /// mood-token → portrait-emotion lookup. Includes the emotion names themselves (self-map) plus
        /// the descriptive mood words the bark manifests actually use. Tokens absent here are skipped so
        /// the next token in a compound mood gets a chance before falling through to neutral.
        /// </summary>
        private static readonly Dictionary<string, string> MoodEmotionMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // direct emotion names (so "teasing"/"dreamy"/… resolve to themselves)
            ["neutral"] = "neutral", ["happy"] = "happy", ["excited"] = "excited", ["praise"] = "praise",
            ["affectionate"] = "affectionate", ["teasing"] = "teasing", ["giggly"] = "giggly", ["wink"] = "wink",
            ["alluring"] = "alluring", ["inviting"] = "inviting", ["entrancing"] = "entrancing", ["dreamy"] = "dreamy",
            ["shy"] = "shy", ["tender"] = "tender", ["adoring"] = "adoring", ["surprised"] = "surprised",
            ["curious"] = "curious", ["greeting"] = "greeting", ["pleased"] = "pleased", ["overwhelmed"] = "overwhelmed",
            // descriptive mood words used across the bark content
            ["soft"] = "tender", ["possessive"] = "adoring", ["knowing"] = "teasing", ["warm"] = "affectionate",
            ["playful"] = "giggly", ["vain"] = "wink", ["amused"] = "giggly", ["indulgent"] = "pleased",
            ["patient"] = "tender", ["intimate"] = "alluring", ["deepening"] = "entrancing", ["affirming"] = "praise",
            ["smug"] = "wink", ["fond"] = "adoring", ["reverent"] = "adoring", ["afterglow"] = "dreamy",
            ["seductive"] = "alluring", ["proud"] = "praise", ["dominant"] = "adoring",
        };

        /// <summary>Fx tags (blush/hearts) declared for an emotion — parsed now, rendered later.</summary>
        public IReadOnlyList<string> FxForEmotion(string emotion) =>
            _m.Emotions.TryGetValue(emotion, out var e) ? e.Fx : Array.Empty<string>();

        /// <summary>
        /// Get the frozen portrait array for (skin, emotion). Lazy-loaded and cached. Skips files that
        /// don't exist; if the bucket ends up empty, falls back to skin 0, then to the idle emotion.
        /// Returns an empty array only if even the idle emotion has no files (degenerate manifest).
        /// </summary>
        public BitmapImage[] GetBucket(int skinIndex, string emotion)
        {
            skinIndex = ClampSkin(skinIndex);
            var key = skinIndex + ":" + emotion;
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var images = LoadBucket(skinIndex, emotion);

            // Fallback 1: same emotion in skin 0.
            if (images.Length == 0 && skinIndex != 0)
                images = GetBucket(0, emotion);

            // Fallback 2: the idle emotion (guard against recursing on idle itself).
            if (images.Length == 0 && !string.Equals(emotion, _m.IdleEmotion, StringComparison.OrdinalIgnoreCase))
                images = GetBucket(skinIndex, _m.IdleEmotion);

            _cache[key] = images;
            return images;
        }

        private BitmapImage[] LoadBucket(int skinIndex, string emotion)
        {
            if (!_m.Emotions.TryGetValue(emotion, out var emo) || emo.Portraits.Count == 0)
                return Array.Empty<BitmapImage>();
            if (skinIndex < 0 || skinIndex >= _m.Skins.Count)
                return Array.Empty<BitmapImage>();

            var skinDir = Path.Combine(_baseDir, _m.Skins[skinIndex].Dir.Replace('/', Path.DirectorySeparatorChar));
            var list = new List<BitmapImage>(emo.Portraits.Count);
            foreach (var p in emo.Portraits)
            {
                if (string.IsNullOrEmpty(p.File)) continue;
                var abs = Path.Combine(skinDir, p.File);
                if (!File.Exists(abs))
                {
                    // The manifest can sit in the install dir while the portraits themselves shipped
                    // in a downloaded content pack (or vice versa) — check the mirrored root before
                    // treating this as a per-skin gap (e.g. L3).
                    abs = ContentLocator.Mirror(abs) ?? "";
                    if (abs.Length == 0) continue;
                }
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(abs, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    list.Add(bmp);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "AvatarPortraitSet: failed to load portrait {Path}", abs);
                }
            }
            return list.ToArray();
        }
    }
}
