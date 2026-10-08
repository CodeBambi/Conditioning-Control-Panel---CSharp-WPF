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
    /// <summary>
    /// The Phase 2 cosmetics registry: banner id -&gt; renderable art, the six curated accents, and
    /// the id sets <see cref="ProfileCosmetics.Sanitize"/> validates against.
    ///
    /// Two hard rules, both learned the hard way in this codebase:
    ///   * A missing or broken asset returns null and the card falls back to its Phase 1 gradient.
    ///     Nothing here throws at a caller - a cosmetic can never cost someone their profile.
    ///   * Banner art is pack:// <c>Resource</c> (listed in the csproj as
    ///     <c>Resources\banners\*.png</c>). The Phase 3 wardrobe PNGs are Content loaded off disk
    ///     instead - do not mix the two loading paths.
    ///   * An id this build no longer ships (the program/mood/feature/tier banners offered before
    ///     the pool was cut down to the twelve mod scenes) simply falls out of
    ///     <see cref="BannerIds"/>, and <see cref="ProfileCosmetics.Sanitize"/> nulls it. Anyone
    ///     still wearing one lands back on the hero's default gradient - no throw, no blank card.
    /// </summary>
    public static class CosmeticsCatalog
    {
        private static readonly object _gate = new();
        private static readonly Dictionary<string, ImageSource?> _imageCache = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, ImageSource?> _thumbCache = new(StringComparer.Ordinal);

        /// <summary>
        /// Decode cap for the banner painted on the hero card. Every source is scaled to fill a
        /// strip roughly 1400px wide, so decoding the 2400x620 originals at full size only buys
        /// memory (6MB each). 1024 keeps the strip crisp at 200% DPI for a quarter of the cost.
        /// </summary>
        private const int BannerDecodePixels = 1024;

        /// <summary>
        /// Decode cap for the Customize dialog's picker tiles, which render at 128x52. The dialog
        /// builds EVERY tile the moment it is constructed, so without a separate small cache one
        /// visit to Customize pinned tens of MB of full-resolution bitmaps for the process lifetime.
        /// </summary>
        private const int BannerThumbnailPixels = 256;

        /// <summary>The banner pool (Core <see cref="CosmeticsPool"/>, shared with the Avalonia head).</summary>
        public static IReadOnlyList<BannerOption> Banners => CosmeticsPool.Banners;

        /// <summary>Ids of every banner this build can paint. Feeds Sanitize.</summary>
        public static ISet<string> BannerIds => CosmeticsPool.BannerIds;

        /// <summary>The preset-avatar pool (Core <see cref="CosmeticsPool"/>); art is Content in
        /// <c>Resources\cosmetics\avatars\&lt;id&gt;.png</c>, a missing PNG degrades to the blank circle.</summary>
        public static IReadOnlyList<AvatarPresetOption> AvatarPresets => CosmeticsPool.AvatarPresets;

        /// <summary>Ids of every preset avatar this build ships. Feeds Sanitize.</summary>
        public static ISet<string> AvatarIds => CosmeticsPool.AvatarIds;

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
                    if (AvatarIds.Contains(id!))
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

        public static BannerOption? FindBanner(string? id) => CosmeticsPool.FindBanner(id);

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

            return ProfileCosmetics.Sanitize(raw, BannerIds, AchievementIds, unlocked, DecoIds(), CharmIds(), AvatarIds,
                WardrobeCatalog.AchievementGates());
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
