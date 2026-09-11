using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models.Dashboard;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Dashboard
{
    /// <summary>Turns a catalog art path into a <c>data:</c> URI, or null when the art will not
    /// resolve. Injected so the message can be built - and asserted - with no WPF imaging at
    /// all.</summary>
    public delegate string? RolodexArtResolver(string artPath);

    /// <summary>
    /// THE ONE MESSAGE THE ROLODEX EVER NEEDS. The page localizes nothing, gates nothing and
    /// fetches nothing (Resources\web\rolodex\CLAUDE.md §2), so everything it draws is decided
    /// here: the four rings, each face's title and blurb in the user's language, its tier livery,
    /// whether this account may open it, and its picture as bytes.
    ///
    /// <para>Pure apart from the art. Titles, blurbs and entitlement all arrive as functions, so
    /// the shape of the message is testable against a fake catalog reader while the real host
    /// passes the SAME title path the wall's own tiles use - a face that named a feature
    /// differently from the tile it becomes would be a picker for a different app.</para>
    ///
    /// <para>Every <c>init</c> rebuilds the page's scene, so this is also what a mod switch costs:
    /// one more init, and (because the art cache is keyed on the mod) one more encode pass.</para>
    /// </summary>
    public static class RolodexInitBuilder
    {
        /// <summary>What the page's card texture is drawn at (512x288), so a wider decode would be
        /// bytes nobody can see.</summary>
        public const int ArtDecodeWidth = 512;

        /// <summary>Quality 80 puts a tile at roughly 30 KB, which is ~1 MB for all 27 faces on one
        /// message. The same figure the Arcademy's avatar encode settled on, one notch lower
        /// because this is a background plate rather than a face at 2x.</summary>
        public const int ArtJpegQuality = 80;

        private const string DataUriPrefix = "data:image/jpeg;base64,";

        // ---- the message ---------------------------------------------------------------

        /// <summary>
        /// Build the <c>init</c> envelope. <paramref name="slot"/> is display only (the page puts
        /// "slot 5" in its header and nothing else); <paramref name="mode"/> is "edit" or "tour",
        /// and anything else is normalised to "edit" rather than trusted, because it reaches a
        /// page that behaves differently in each.
        /// </summary>
        public static JObject BuildInit(
            string? mode,
            int? slot,
            int picks,
            bool reducedMotion,
            string? lang,
            Func<DashboardFeature, string> title,
            Func<DashboardFeature, string> blurb,
            Func<DashboardFeature, bool> entitled,
            RolodexArtResolver art)
        {
            if (title == null) throw new ArgumentNullException(nameof(title));
            if (blurb == null) throw new ArgumentNullException(nameof(blurb));
            if (entitled == null) throw new ArgumentNullException(nameof(entitled));
            if (art == null) throw new ArgumentNullException(nameof(art));

            var rings = new JArray();
            foreach (var group in FeatureCatalog.All.GroupBy(f => f.Ring).OrderBy(g => g.Key))
            {
                var faces = new JArray();
                foreach (var f in group)
                {
                    var uri = Safe<string?>(() => art(f.ArtPath), null);
                    faces.Add(new JObject
                    {
                        ["key"] = f.Key,
                        ["title"] = Safe(() => title(f), f.Key),
                        ["blurb"] = Safe(() => blurb(f), ""),
                        // Livery, not entitlement: the badge says what the door costs, `locked`
                        // says whether this account is holding the key (page CLAUDE.md §2).
                        ["tier"] = f.Tier,
                        ["locked"] = !Safe(() => entitled(f), true),
                        ["art"] = uri == null ? JValue.CreateNull() : new JValue(uri),
                    });
                }
                rings.Add(new JObject { ["ring"] = group.Key, ["faces"] = faces });
            }

            return new JObject
            {
                ["type"] = "init",
                ["rings"] = rings,
                ["slot"] = slot.HasValue ? new JValue(slot.Value) : JValue.CreateNull(),
                ["mode"] = string.Equals(mode, "tour", StringComparison.OrdinalIgnoreCase) ? "tour" : "edit",
                ["picks"] = Math.Max(1, picks),
                ["reducedMotion"] = reducedMotion,
                ["lang"] = string.IsNullOrWhiteSpace(lang) ? "en" : lang,
            };
        }

        // ---- the art cache -------------------------------------------------------------

        /// <summary>The encoded faces, keyed by art path, for ONE mod. A rolodex open costs 27
        /// decodes and 27 JPEG encodes the first time and nothing at all afterwards, which is the
        /// difference between a picker that appears and a picker that takes a beat.</summary>
        private static readonly Dictionary<string, string?> _artCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Which mod's art is in the cache. A mismatch empties it without anyone having
        /// to remember to - the belt to <see cref="InvalidateArt"/>'s braces.</summary>
        private static string? _artModId;

        /// <summary>
        /// Drop the cached faces. Called from <c>LoadFeatureImages</c>, which is the ONE signal the
        /// wall itself re-skins on (the ctor, and every <c>ApplyActiveModChange</c>), so the picker
        /// and the tiles behind it can never be showing two different mods' art.
        /// </summary>
        public static void InvalidateArt()
        {
            _artCache.Clear();
            _artModId = null;
        }

        /// <summary>
        /// The real resolver: the catalog's own art path, through the mod override chain, decoded
        /// at <see cref="ArtDecodeWidth"/> and re-encoded as a JPEG. Null when the art will not
        /// resolve, which the page draws as its keyed placeholder rather than as a hole.
        /// </summary>
        public static string? ArtDataUri(string artPath)
        {
            if (string.IsNullOrWhiteSpace(artPath)) return null;

            var modId = App.Mods?.ActiveModId;
            if (!string.Equals(modId, _artModId, StringComparison.OrdinalIgnoreCase))
            {
                _artCache.Clear();
                _artModId = modId;
            }

            if (_artCache.TryGetValue(artPath, out var cached)) return cached;

            var uri = Encode(artPath);
            _artCache[artPath] = uri;
            return uri;
        }

        /// <summary>
        /// Decode at the texture's width, flatten to Bgr24, encode. The flatten is deliberate and
        /// the Arcademy's cache explains it best: JPEG has no alpha, so a transparent corner has to
        /// become a defined colour here rather than an encoder's opinion of one.
        /// </summary>
        private static string? Encode(string artPath)
        {
            try
            {
                var src = ModResourceResolver.ResolveImageDecoded(artPath, ArtDecodeWidth) as BitmapSource
                          ?? ModResourceResolver.ResolveImage(artPath) as BitmapSource;
                if (src == null) return null;

                var flat = new FormatConvertedBitmap(src, System.Windows.Media.PixelFormats.Bgr24, null, 0);
                flat.Freeze();

                var enc = new JpegBitmapEncoder { QualityLevel = ArtJpegQuality };
                enc.Frames.Add(BitmapFrame.Create(flat));
                using var ms = new MemoryStream();
                enc.Save(ms);
                var bytes = ms.ToArray();
                return bytes.Length == 0 ? null : DataUriPrefix + Convert.ToBase64String(bytes);
            }
            catch (Exception ex)
            {
                // Debug, not Warning: a face with no picture still reads (the page paints a keyed
                // placeholder with the title's initial), and a mod with one missing override would
                // otherwise file 27 lines on every open.
                App.Logger?.Debug("[Rolodex] art encode failed: {E}", ex.Message);
                return null;
            }
        }

        private static T Safe<T>(Func<T> read, T fallback)
        {
            try { return read(); }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Rolodex] init field: {E}", ex.Message);
                return fallback;
            }
        }
    }
}
