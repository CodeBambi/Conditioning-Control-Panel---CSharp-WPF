using System;
using System.Collections.Generic;
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
        };

        private static readonly HashSet<string> _bannerIds =
            new(Banners.Select(b => b.Id), StringComparer.Ordinal);

        /// <summary>Ids of every banner this build can paint. Feeds Sanitize.</summary>
        public static ISet<string> BannerIds => _bannerIds;

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
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            lock (_gate)
            {
                if (_imageCache.TryGetValue(id!, out var cached)) return cached;

                ImageSource? built = null;
                try
                {
                    var option = FindBanner(id);
                    if (option != null)
                    {
                        built = option.Gradient.HasValue
                            ? BuildGradientImage(option.Gradient.Value)
                            : BuildPackImage(option.PackPath!);
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("CosmeticsCatalog: banner {Id} failed to load: {E}", id, ex.Message);
                    built = null;
                }

                _imageCache[id!] = built;
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

            return ProfileCosmetics.Sanitize(raw, BannerIds, AchievementIds, unlocked, WardrobeIds());
        }

        /// <summary>
        /// Sanitize for SOMEONE ELSE's card. Their unlock list is not knowable from /user/lookup,
        /// so the unlock filter is skipped - ids the app recognises still render.
        /// </summary>
        public static ProfileCosmetics SanitizeViewed(ProfileCosmetics? raw)
            => ProfileCosmetics.Sanitize(raw, BannerIds, AchievementIds, null, WardrobeIds());

        // ---------------------------------------------------------------------------------

        private static ImageSource? BuildPackImage(string packRelativePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri($"pack://application:,,,/{packRelativePath}", UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
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
