using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>One entry in the banner pool: an id, a display name, and how to paint it.</summary>
    public sealed class BannerOption
    {
        public string Id { get; }

        /// <summary>
        /// Plain English, deliberately NOT localized - same rule the Phase 3 wardrobe registry
        /// follows for item names (Discord-style proper nouns).
        /// </summary>
        public string Name { get; }

        /// <summary>Group label for the picker ("Gradients", "Programs", "Moods", "Patron").</summary>
        public string Group { get; }

        /// <summary>Pack-relative art path, or null for a generated gradient.</summary>
        internal string? PackPath { get; }

        /// <summary>Gradient definition (from/via/to) used when <see cref="PackPath"/> is null.</summary>
        internal (string From, string Via, string To)? Gradient { get; }

        internal BannerOption(string id, string name, string group, string? packPath,
                              (string From, string Via, string To)? gradient = null)
        {
            Id = id;
            Name = name;
            Group = group;
            PackPath = packPath;
            Gradient = gradient;
        }
    }

    /// <summary>One entry in the preset-avatar pool: a featureless "blank subject" bust.</summary>
    public sealed class AvatarPresetOption
    {
        public string Id { get; }

        /// <summary>Plain English proper noun, deliberately NOT localized (same rule as banners).</summary>
        public string Name { get; }

        /// <summary>Mod bucket ("bambi"/"sissy"/"drone"/"circe") - grouping only, no gating.</summary>
        public string Mod { get; }

        internal AvatarPresetOption(string id, string name, string mod)
        {
            Id = id;
            Name = name;
            Mod = mod;
        }
    }

    /// <summary>
    /// The Phase 2 cosmetics registry: banner id -&gt; renderable art, the six curated accents, and
    /// the id sets <see cref="ProfileCosmetics.Sanitize"/> validates against.
    ///
    /// Two hard rules, both learned the hard way in this codebase:
    ///   * A missing or broken asset returns null and the card falls back to its Phase 1 gradient.
    ///     Nothing here throws at a caller - a cosmetic can never cost someone their profile.
    ///   * Banner art is pack:// <c>Resource</c> (already shipped and listed in the csproj:
    ///     <c>Resources\programs\*.png</c>, <c>Resources\features\*.png</c>, and the two tier
    ///     images). The Phase 3 wardrobe PNGs are Content loaded off disk instead - do not mix
    ///     the two loading paths.
    /// </summary>
    public static class CosmeticsCatalog
    {
        private static readonly object _gate = new();
        private static readonly Dictionary<string, ImageSource?> _imageCache = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, ImageSource?> _thumbCache = new(StringComparer.Ordinal);

        /// <summary>
        /// Decode cap for the banner painted on the hero card. The card's banner strip is roughly
        /// 1400x180 and every source is scaled to fill it, so decoding the originals at full size
        /// only buys memory: the six program plates are 2800x113 (1.3MB each) and the mood/feature
        /// art is 1376x768 / 1024x1024 (4MB each). 1024 keeps the strip crisp at 200% DPI and
        /// caps the whole pool at roughly a third of what it cost before.
        /// </summary>
        private const int BannerDecodePixels = 1024;

        /// <summary>
        /// Decode cap for the Customize dialog's picker tiles, which render at 128x52. The dialog
        /// builds ALL 19 tiles the moment it is constructed, so without a separate small cache one
        /// visit to Customize pinned ~42MB of full-resolution bitmaps for the process lifetime.
        /// </summary>
        private const int BannerThumbnailPixels = 256;

        /// <summary>
        /// The banner pool. Seeded from art the installer already ships plus three generated
        /// gradient presets, so Phase 2 needs no new assets at all.
        /// </summary>
        public static readonly IReadOnlyList<BannerOption> Banners = new List<BannerOption>
        {
            // --- generated gradients (no asset, always available) ---
            new("gradient_velvet", "Velvet", "Gradients", null, ("#2A1E4D", "#3B2159", "#1E1E3F")),
            new("gradient_bloom",  "Bloom",  "Gradients", null, ("#5A1B3D", "#8A2B63", "#2A1230")),
            new("gradient_drone",  "Drone",  "Gradients", null, ("#0E2A38", "#164A5E", "#0A1622")),

            // --- Training Programs art (Resources\programs\*.png, Resource) ---
            new("program_default",      "Conditioning",   "Programs", "Resources/programs/banner_default.png"),
            new("program_firmware",     "Firmware",       "Programs", "Resources/programs/banner_firmware_install.png"),
            new("program_first_week",   "First Week",     "Programs", "Resources/programs/banner_first_week.png"),
            new("program_kept",         "Kept",           "Programs", "Resources/programs/banner_kept.png"),
            new("program_presentation", "Presentation",   "Programs", "Resources/programs/banner_presentation.png"),
            new("program_takeover",     "The Takeover",   "Programs", "Resources/programs/banner_the_takeover.png"),

            // --- day-mood plates (same folder) ---
            new("mood_deep",  "Deep",  "Moods", "Resources/programs/hero_deep.png"),
            new("mood_drift", "Drift", "Moods", "Resources/programs/hero_drift.png"),
            new("mood_focus", "Focus", "Moods", "Resources/programs/hero_focus.png"),
            new("mood_pink",  "Pink",  "Moods", "Resources/programs/hero_pink.png"),

            // --- feature art (Resources\features\*.png, Resource) ---
            new("feature_spiral",     "Spiral",      "Features", "Resources/features/spiral_overlay.png"),
            new("feature_pink",       "Pink Filter", "Features", "Resources/features/Pink_filter.png"),
            new("feature_braindrain", "Brain Drain", "Features", "Resources/features/brain_drain.png"),
            new("feature_goon",       "Goon Game",   "Features", "Resources/features/goon_game.png"),

            // --- tier art (unlock gating is deliberately out of scope for this build) ---
            new("tier_pink_filter",   "Subject",     "Patron", "Resources/Pink filter.webp"),
            new("tier_prime_subject", "Prime",       "Patron", "Resources/prime subject.webp"),

            // --- per-mod scene banners (Resources\banners\*.png, Resource; owner-approved
            //     2026-08-06 one-shot set, pre-cropped to the 3:1 hero band) ---
            new("bambi_neon_den",  "Neon Den",        "Bambi",       "Resources/banners/bambi_neon_den.png"),
            new("bambi_arcade",    "Claw Machine",    "Bambi",       "Resources/banners/bambi_arcade.png"),
            new("bambi_diner",     "Milkshake Diner", "Bambi",       "Resources/banners/bambi_diner.png"),
            new("sissy_boudoir",   "Boudoir",         "Sissy",       "Resources/banners/sissy_boudoir.png"),
            new("sissy_wardrobe",  "Walk-In",         "Sissy",       "Resources/banners/sissy_wardrobe.png"),
            new("sissy_canopy",    "Canopy Bed",      "Sissy",       "Resources/banners/sissy_canopy.png"),
            new("drone_bay",       "Conversion Bay",  "Drone",       "Resources/banners/drone_bay.png"),
            new("drone_chargers",  "Charging Bay",    "Drone",       "Resources/banners/drone_chargers.png"),
            new("drone_control",   "Control Room",    "Drone",       "Resources/banners/drone_control.png"),
            new("lock_keywall",    "Key Wall",        "Circe's Lock", "Resources/banners/lock_keywall.png"),
            new("lock_chamber",    "Her Chamber",     "Circe's Lock", "Resources/banners/lock_chamber.png"),
            new("lock_vault",      "The Vault",       "Circe's Lock", "Resources/banners/lock_vault.png"),
        };

        private static readonly HashSet<string> _bannerIds =
            new(Banners.Select(b => b.Id), StringComparer.Ordinal);

        /// <summary>Ids of every banner this build can paint. Feeds Sanitize.</summary>
        public static ISet<string> BannerIds => _bannerIds;

        /// <summary>
        /// The preset-avatar pool: featureless "blank subject" busts shown when no Discord picture
        /// is shared (resolution order everywhere: shared pfp, then preset, then blank circle).
        /// Art ships as Content in <c>Resources\cosmetics\avatars\&lt;id&gt;.png</c> - loaded off
        /// disk like the wardrobe, so a missing PNG degrades to the blank circle, never a throw.
        /// </summary>
        public static readonly IReadOnlyList<AvatarPresetOption> AvatarPresets = new List<AvatarPresetOption>
        {
            new("avatar_bambi_1", "Pink Ponytail",  "bambi"),
            new("avatar_bambi_2", "Twin Tails",     "bambi"),
            new("avatar_bambi_3", "Bunny Bob",      "bambi"),
            new("avatar_sissy_1", "The Updo",       "sissy"),
            new("avatar_sissy_2", "Ringlets",       "sissy"),
            new("avatar_sissy_3", "Braided Crown",  "sissy"),
            new("avatar_drone_1", "Chrome Dome",    "drone"),
            new("avatar_drone_2", "Stealth Unit",   "drone"),
            new("avatar_drone_3", "Visor Unit",     "drone"),
            new("avatar_circe_1", "Obsidian Bob",   "circe"),
            new("avatar_circe_2", "Kept & Sleek",   "circe"),
            new("avatar_circe_3", "High Ponytail",  "circe"),
        };

        private static readonly HashSet<string> _avatarIds =
            new(AvatarPresets.Select(a => a.Id), StringComparer.Ordinal);

        /// <summary>Ids of every preset avatar this build ships. Feeds Sanitize.</summary>
        public static ISet<string> AvatarIds => _avatarIds;

        /// <summary>
        /// Decode cap for preset-avatar art. The bubble renders at 104px (208 at 200% DPI); the
        /// source PNGs are 512. 256 keeps the circle crisp without holding 1MB per browsed tile.
        /// </summary>
        private const int AvatarDecodePixels = 256;

        private static readonly Dictionary<string, ImageSource?> _avatarCache = new(StringComparer.Ordinal);

        /// <summary>
        /// The art for a preset-avatar id, or null when the id is unknown or its PNG is missing -
        /// callers fall back to the blank circle. Results (including nulls) are cached.
        /// </summary>
        public static ImageSource? GetAvatarImage(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            lock (_gate)
            {
                if (_avatarCache.TryGetValue(id!, out var cached)) return cached;

                ImageSource? built = null;
                try
                {
                    if (_avatarIds.Contains(id!))
                    {
                        var path = Path.Combine(AppContext.BaseDirectory,
                            "Resources", "cosmetics", "avatars", id + ".png");
                        if (File.Exists(path))
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(path, UriKind.Absolute);
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.DecodePixelWidth = AvatarDecodePixels;
                            bitmap.EndInit();
                            if (bitmap.CanFreeze) bitmap.Freeze();
                            built = bitmap;
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("CosmeticsCatalog: avatar preset {Id} failed to load: {E}", id, ex.Message);
                    built = null;
                }

                _avatarCache[id!] = built;
                return built;
            }
        }

        /// <summary>The six curated accents (see <see cref="ProfileCosmetics.AccentSwatches"/>).</summary>
        public static IReadOnlyList<string> AccentSwatches => ProfileCosmetics.AccentSwatches;

        /// <summary>Every achievement id the app knows about. Feeds Sanitize's title/pin checks.</summary>
        public static ISet<string> AchievementIds =>
            new HashSet<string>(Achievement.All.Keys, StringComparer.Ordinal);

        public static BannerOption? FindBanner(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return Banners.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.Ordinal));
        }

        /// <summary>
        /// The art for a banner id, or null when the id is unknown or its asset failed to load -
        /// in which case the hero keeps the default gradient painted behind the Image.
        /// Results (including nulls) are cached: a broken asset is not retried on every render.
        /// </summary>
        public static ImageSource? GetBannerImage(string? id)
            => GetBanner(id, _imageCache, BannerDecodePixels);

        /// <summary>
        /// The same art at picker-tile resolution. Kept in its own cache so browsing the Customize
        /// dialog never pulls the card-sized decodes into memory (and vice versa).
        /// </summary>
        public static ImageSource? GetBannerThumbnail(string? id)
            => GetBanner(id, _thumbCache, BannerThumbnailPixels);

        /// <summary>Drops the banner + avatar caches. Exists for parity with WardrobeCatalog.Invalidate.</summary>
        public static void Invalidate()
        {
            lock (_gate)
            {
                _imageCache.Clear();
                _thumbCache.Clear();
                _avatarCache.Clear();
            }
        }

        private static ImageSource? GetBanner(string? id, Dictionary<string, ImageSource?> cache, int decodePixels)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            lock (_gate)
            {
                if (cache.TryGetValue(id!, out var cached)) return cached;

                ImageSource? built = null;
                try
                {
                    var option = FindBanner(id);
                    if (option != null)
                    {
                        // Gradients are vector DrawingImages - they cost nothing and do not decode.
                        built = option.Gradient.HasValue
                            ? BuildGradientImage(option.Gradient.Value)
                            : BuildPackImage(option.PackPath!, decodePixels);
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("CosmeticsCatalog: banner {Id} failed to load: {E}", id, ex.Message);
                    built = null;
                }

                cache[id!] = built;
                return built;
            }
        }

        /// <summary>Parses one of the six accents into a Color. False for anything else.</summary>
        public static bool TryGetAccentColor(string? hex, out Color color)
        {
            color = Colors.Transparent;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            var clean = ProfileCosmetics.Sanitize(new ProfileCosmetics { Accent = hex }).Accent;
            if (clean == null) return false;
            try
            {
                color = (Color)ColorConverter.ConvertFromString(clean);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ids from the Phase 3 wardrobe registry, via <see cref="WardrobeCatalog"/> (which owns
        /// the registry and the art). Returns null when the file is absent or unreadable, which
        /// Sanitize reads as "cannot validate - pass the ids through" so a build without the
        /// manifest never strips a loadout it simply cannot check.
        /// </summary>
        public static ISet<string>? WardrobeIds() => WardrobeCatalog.KnownIds();

        /// <summary>Registry ids wearable in the decoration slot (null = cannot validate).</summary>
        public static ISet<string>? DecoIds() => WardrobeCatalog.DecoIds();

        /// <summary>Registry ids wearable in a charm slot (null = cannot validate).</summary>
        public static ISet<string>? CharmIds() => WardrobeCatalog.CharmIds();

        /// <summary>
        /// Sanitize for the viewer's OWN card: every check available, including the unlock filter
        /// (a title or pin they have not earned is dropped rather than shown).
        /// </summary>
        public static ProfileCosmetics SanitizeOwn(ProfileCosmetics? raw)
        {
            ISet<string>? unlocked = null;
            try
            {
                var progress = App.Achievements?.Progress?.UnlockedAchievements;
                if (progress != null) unlocked = new HashSet<string>(progress, StringComparer.Ordinal);
            }
            catch { /* no achievement service yet (early boot / tests) - skip the unlock filter */ }

            return ProfileCosmetics.Sanitize(raw, BannerIds, AchievementIds, unlocked, DecoIds(), CharmIds(), AvatarIds);
        }

        /// <summary>
        /// Sanitize for SOMEONE ELSE's card. Their unlock list is not knowable from /user/lookup,
        /// so the unlock filter is skipped - ids the app recognises still render.
        /// </summary>
        public static ProfileCosmetics SanitizeViewed(ProfileCosmetics? raw)
            => ProfileCosmetics.Sanitize(raw, BannerIds, AchievementIds, null, DecoIds(), CharmIds(), AvatarIds);

        // ---------------------------------------------------------------------------------

        private static ImageSource? BuildPackImage(string packRelativePath, int decodePixels)
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/{packRelativePath}", UriKind.Absolute);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                // DecodePixelWidth is a target, not a maximum: setting it above the source width
                // UPSCALES into memory (the 400x400 tier art would cost 4MB at a 1024 cap instead
                // of 640KB). So peek the header first - metadata only, no pixels - and cap only
                // the sources that are actually larger than we need.
                var sourceWidth = ProbePixelWidth(uri);
                if (decodePixels > 0 && sourceWidth > decodePixels) bitmap.DecodePixelWidth = decodePixels;
                bitmap.EndInit();
                if (bitmap.CanFreeze) bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("CosmeticsCatalog: pack art {Path} missing: {E}", packRelativePath, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// The source's pixel width read from its header, or 0 when it cannot be determined (in
        /// which case the caller decodes at full size - the safe direction).
        /// </summary>
        private static int ProbePixelWidth(Uri uri)
        {
            try
            {
                var frame = BitmapFrame.Create(uri, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                return frame.PixelWidth;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// A gradient preset painted into a DrawingImage, so gradients and art share one code path
        /// (both are just an ImageSource on the hero's banner Image) and the XAML needs no extra
        /// layer per preset.
        /// </summary>
        private static ImageSource BuildGradientImage((string From, string Via, string To) stops)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stops.From), 0));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stops.Via), 0.5));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stops.To), 1));
            if (brush.CanFreeze) brush.Freeze();

            var drawing = new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(0, 0, 320, 120)));
            var image = new DrawingImage(drawing);
            if (image.CanFreeze) image.Freeze();
            return image;
        }
    }
}
