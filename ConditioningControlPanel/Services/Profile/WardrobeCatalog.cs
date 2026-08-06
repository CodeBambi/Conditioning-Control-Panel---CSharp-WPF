using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>One row of <c>Resources/cosmetics/registry.json</c>.</summary>
    public sealed class WardrobeItem
    {
        /// <summary>Registry id - the value that rides the profile payload.</summary>
        public string Id { get; }

        /// <summary>Mod bucket ("bambi" / "sissy" / "drone"). Drives the picker's tabs.</summary>
        public string Mod { get; }

        /// <summary><c>deco</c> (worn over the avatar) or <c>charm</c> (card corner prop).</summary>
        public string Type { get; }

        /// <summary>
        /// Plain English proper noun, deliberately NOT localized - same rule the banner pool
        /// follows (DESIGN.md: "Item names are plain English proper nouns in the registry").
        /// </summary>
        public string Name { get; }

        /// <summary>Registry-relative art path, e.g. <c>bambi/bambi_silk_bow.png</c>.</summary>
        public string File { get; }

        /// <summary><c>free | achievement:&lt;id&gt; | level:&lt;n&gt; | tier:&lt;n&gt; | event:&lt;slug&gt; | code</c>.
        /// Only <c>free</c> and <c>achievement:</c> are enforced today; the other forms remain
        /// reserved (an unrecognised value degrades to free so a registry typo never locks art).</summary>
        public string Unlock { get; }

        /// <summary>
        /// The achievement id gating this item (<c>"unlock": "achievement:&lt;id&gt;"</c>), or null
        /// for an ungated item.
        /// </summary>
        public string? RequiredAchievementId =>
            Unlock.StartsWith("achievement:", StringComparison.OrdinalIgnoreCase)
                ? Unlock.Substring("achievement:".Length).Trim() is { Length: > 0 } id ? id : null
                : null;

        public bool IsDecoration => string.Equals(Type, "deco", StringComparison.OrdinalIgnoreCase);
        public bool IsCharm => string.Equals(Type, "charm", StringComparison.OrdinalIgnoreCase);

        internal WardrobeItem(string id, string mod, string type, string name, string file, string unlock)
        {
            Id = id;
            Mod = mod;
            Type = type;
            Name = name;
            File = file;
            Unlock = unlock;
        }
    }

    /// <summary>
    /// The Phase 3 wardrobe: <c>Resources/cosmetics/registry.json</c> plus the 512x512 RGBA PNGs
    /// beside it, read off disk from <see cref="AppContext.BaseDirectory"/> (the csproj ships them
    /// as Content, NOT as pack:// Resources - so art that never landed degrades to "no decoration"
    /// instead of throwing a Resource URI at render time).
    ///
    /// Contract, in one line: <b>a missing PNG or an unknown id renders the plain avatar</b>.
    /// Nothing here throws at a caller, and every lookup - including the failures - is cached, so a
    /// broken file is not re-opened on every repaint.
    ///
    /// The art is authored on a fixed canvas where the avatar occupies a centred circle at
    /// <see cref="AvatarCircleRatio"/> of the canvas width. Placement is therefore baked into the
    /// art and NO per-item anchor metadata exists - see <c>AdornedAvatar</c>, which is the only
    /// place that ratio turns into pixels.
    /// </summary>
    public static class WardrobeCatalog
    {
        /// <summary>
        /// The avatar circle's share of a decoration canvas (DESIGN.md Phase 3). A decoration is
        /// drawn at <c>avatarDiameter / 0.70</c>, centred on the avatar, so the art's own circle
        /// lands exactly on the avatar at any render size.
        /// </summary>
        public const double AvatarCircleRatio = 0.70;

        /// <summary>
        /// A charm's base size at Scale = 1, as a fraction of the hero card's height. Shared by
        /// the live renderer (MainWindow.ProfileWardrobe) and the wardrobe editor's stage so the
        /// preview is exactly what viewers get.
        /// </summary>
        public const double CharmBaseHeightFraction = 0.35;

        /// <summary>Default charm centre placements per slot, in card-fraction coordinates.</summary>
        public static readonly IReadOnlyList<(double X, double Y)> DefaultCharmAnchors =
            new (double, double)[] { (0.90, 0.76), (0.965, 0.90) };

        /// <summary>
        /// Decode cap for wardrobe art. The 512 canvases are only ever drawn at 148px (the hero's
        /// 104/0.70 decoration) or smaller, so decoding them at full size would hold ~1MB per item
        /// - 60MB once someone browses the whole picker. 384 stays crisp at 200% DPI and costs a
        /// third of that.
        /// </summary>
        private const int ArtDecodePixels = 384;

        private static readonly object _gate = new();
        private static readonly Dictionary<string, ImageSource?> _imageCache = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, bool> _artExistsCache = new(StringComparer.Ordinal);

        private static List<WardrobeItem>? _items;
        private static Dictionary<string, WardrobeItem>? _byId;
        private static HashSet<string>? _ids;
        private static HashSet<string>? _decoIds;
        private static HashSet<string>? _charmIds;
        private static Dictionary<string, string>? _achievementGates;
        private static List<string>? _mods;
        private static bool _loadAttempted;

        /// <summary>Every registry item, in file order. Empty when the registry is absent.</summary>
        public static IReadOnlyList<WardrobeItem> Items
        {
            get { EnsureLoaded(); return _items ?? (IReadOnlyList<WardrobeItem>)Array.Empty<WardrobeItem>(); }
        }

        /// <summary>Mod buckets in first-seen order - the picker's tab order.</summary>
        public static IReadOnlyList<string> Mods
        {
            get { EnsureLoaded(); return _mods ?? (IReadOnlyList<string>)Array.Empty<string>(); }
        }

        /// <summary>True when a registry with at least one item was found.</summary>
        public static bool IsAvailable
        {
            get { EnsureLoaded(); return _items != null && _items.Count > 0; }
        }

        /// <summary>
        /// Ids the registry knows, or <c>null</c> when there is no readable registry. Null is
        /// meaningful: <see cref="Models.ProfileCosmetics.Sanitize"/> reads it as "cannot validate,
        /// pass the ids through", so a build without the manifest never strips someone's loadout.
        /// </summary>
        public static ISet<string>? KnownIds()
        {
            EnsureLoaded();
            return _ids;
        }

        /// <summary>
        /// Ids wearable in the DECORATION slot, or null when there is no readable registry (read
        /// by Sanitize as "cannot validate"). Split from <see cref="CharmIds"/> because the server
        /// validates the two slots against separate sets - a union check on this side would let a
        /// charm equip as a decoration locally and be stripped server-side, which is invisible to
        /// the wearer and permanent.
        /// </summary>
        public static ISet<string>? DecoIds()
        {
            EnsureLoaded();
            return _decoIds;
        }

        /// <summary>Ids wearable in a CHARM slot, or null when there is no readable registry.</summary>
        public static ISet<string>? CharmIds()
        {
            EnsureLoaded();
            return _charmIds;
        }

        /// <summary>
        /// Achievement gates declared by the registry: item id → required achievement id.
        /// Null when there is no readable registry (read by Sanitize as "cannot validate");
        /// empty when the registry exists but gates nothing.
        /// </summary>
        public static IReadOnlyDictionary<string, string>? AchievementGates()
        {
            EnsureLoaded();
            return _achievementGates;
        }

        /// <summary>
        /// Live unlock check for the CURRENT user: true for ungated items, and for gated items
        /// whose achievement is earned. Defaults to UNLOCKED when achievement progress is not
        /// loadable (early boot / tests) - gating must never brick the picker.
        /// </summary>
        public static bool IsUnlockedForCurrentUser(WardrobeItem? item)
        {
            var gate = item?.RequiredAchievementId;
            if (gate == null) return true;
            try
            {
                var progress = App.Achievements?.Progress;
                return progress == null || progress.IsUnlocked(gate);
            }
            catch
            {
                return true;
            }
        }

        public static WardrobeItem? Find(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            EnsureLoaded();
            return _byId != null && _byId.TryGetValue(id!, out var item) ? item : null;
        }

        /// <summary>Items of one type in one mod, registry order.</summary>
        public static IReadOnlyList<WardrobeItem> ItemsFor(string mod, bool decorations)
        {
            EnsureLoaded();
            if (_items == null) return Array.Empty<WardrobeItem>();
            return _items
                .Where(i => string.Equals(i.Mod, mod, StringComparison.OrdinalIgnoreCase))
                .Where(i => decorations ? i.IsDecoration : i.IsCharm)
                .ToList();
        }

        /// <summary>
        /// The 512x512 art for an id, or null when the id is unknown or its PNG is missing or
        /// unreadable. Callers render nothing in that case - never a placeholder, never a throw.
        /// </summary>
        public static ImageSource? GetImage(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            lock (_gate)
            {
                if (_imageCache.TryGetValue(id!, out var cached)) return cached;

                ImageSource? built = null;
                try
                {
                    var item = Find(id);
                    if (item != null)
                    {
                        var path = ResolveArtPath(item.File);
                        if (path != null && System.IO.File.Exists(path))
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(path, UriKind.Absolute);
                            // OnLoad so the file handle is released immediately: the art stage may
                            // still be rewriting these PNGs while the app is open.
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                            bitmap.DecodePixelWidth = ArtDecodePixels;
                            bitmap.EndInit();
                            if (bitmap.CanFreeze) bitmap.Freeze();
                            built = bitmap;
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("WardrobeCatalog: art for {Id} failed to load: {E}", id, ex.Message);
                    built = null;
                }

                _imageCache[id!] = built;
                return built;
            }
        }

        /// <summary>True when this id has art we can actually paint (decodes it).</summary>
        public static bool HasArt(string? id) => GetImage(id) != null;

        /// <summary>
        /// True when this id's PNG is on disk - existence only, no decode. The picker uses this to
        /// decide which mod tabs are worth showing without pulling all 60 canvases into memory to
        /// answer the question.
        /// </summary>
        public static bool HasArtFile(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            lock (_gate)
            {
                if (_artExistsCache.TryGetValue(id!, out var cached)) return cached;

                var exists = false;
                try
                {
                    var item = Find(id);
                    if (item != null)
                    {
                        var path = ResolveArtPath(item.File);
                        exists = path != null && System.IO.File.Exists(path);
                    }
                }
                catch { exists = false; }

                _artExistsCache[id!] = exists;
                return exists;
            }
        }

        /// <summary>
        /// Drops the cached registry and art. Only exists so a mid-session asset drop (the art
        /// stage writing PNGs under a running app) can be picked up without a restart.
        /// </summary>
        public static void Invalidate()
        {
            lock (_gate)
            {
                _imageCache.Clear();
                _artExistsCache.Clear();
                _items = null;
                _byId = null;
                _ids = null;
                _decoIds = null;
                _charmIds = null;
                _achievementGates = null;
                _mods = null;
                _loadAttempted = false;
            }

            // The silhouette masks are frozen ImageBrushes built off this cache; a stale one would
            // keep stencilling the OLD art after a mid-session asset drop.
            try { Helpers.Silhouette.InvalidateCache(); }
            catch (Exception ex) { App.Logger?.Debug("WardrobeCatalog: silhouette cache flush failed: {E}", ex.Message); }
        }

        // ---------------------------------------------------------------------------------

        private static void EnsureLoaded()
        {
            lock (_gate)
            {
                if (_loadAttempted) return;
                _loadAttempted = true;

                try
                {
                    var path = RegistryPath();
                    if (path == null || !System.IO.File.Exists(path)) return;

                    var root = JObject.Parse(System.IO.File.ReadAllText(path));
                    if (root["items"] is not JArray rows) return;

                    var items = new List<WardrobeItem>();
                    var byId = new Dictionary<string, WardrobeItem>(StringComparer.Ordinal);
                    var mods = new List<string>();

                    foreach (var row in rows)
                    {
                        var id = Str(row?["id"]);
                        var file = Str(row?["file"]);
                        if (id == null || file == null) continue;           // unusable row, skip it
                        if (byId.ContainsKey(id)) continue;                 // first definition wins

                        var type = Str(row?["type"]) ?? "deco";
                        var mod = Str(row?["mod"]) ?? "misc";
                        var item = new WardrobeItem(
                            id, mod, type,
                            Str(row?["name"]) ?? id,
                            file,
                            Str(row?["unlock"]) ?? "free");

                        items.Add(item);
                        byId[id] = item;
                        if (!mods.Any(m => string.Equals(m, mod, StringComparison.OrdinalIgnoreCase)))
                            mods.Add(mod);
                    }

                    if (items.Count == 0) return;

                    _items = items;
                    _byId = byId;
                    _mods = mods;
                    _ids = new HashSet<string>(byId.Keys, StringComparer.Ordinal);
                    _decoIds = new HashSet<string>(
                        items.Where(i => i.IsDecoration).Select(i => i.Id), StringComparer.Ordinal);
                    _charmIds = new HashSet<string>(
                        items.Where(i => i.IsCharm).Select(i => i.Id), StringComparer.Ordinal);
                    _achievementGates = items
                        .Where(i => i.RequiredAchievementId != null)
                        .ToDictionary(i => i.Id, i => i.RequiredAchievementId!, StringComparer.Ordinal);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("WardrobeCatalog: registry unreadable: {E}", ex.Message);
                    _items = null;
                    _byId = null;
                    _ids = null;
                    _decoIds = null;
                    _charmIds = null;
                    _achievementGates = null;
                    _mods = null;
                }
            }
        }

        private static string? RegistryPath()
        {
            try
            {
                return Path.Combine(AppContext.BaseDirectory, "Resources", "cosmetics", "registry.json");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Registry paths use forward slashes; rebuild them with Path.Combine so the separator is
        /// never baked in and a path escape ("../..") cannot walk out of the cosmetics folder.
        /// </summary>
        private static string? ResolveArtPath(string file)
        {
            try
            {
                var root = Path.Combine(AppContext.BaseDirectory, "Resources", "cosmetics");
                var parts = file.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || parts.Any(p => p == "..")) return null;

                var full = Path.GetFullPath(Path.Combine(new[] { root }.Concat(parts).ToArray()));
                var rootFull = Path.GetFullPath(root);
                return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full : null;
            }
            catch
            {
                return null;
            }
        }

        private static string? Str(JToken? token)
        {
            var value = token?.Type == JTokenType.String ? token.ToString() : token?.ToString();
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value!.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }
}
