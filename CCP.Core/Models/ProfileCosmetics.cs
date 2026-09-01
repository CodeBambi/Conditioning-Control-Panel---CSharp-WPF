using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// The customization a subject has equipped on their Trainer Card (Profile redesign Phase 2,
    /// "Own It"). Rides the profile payload as a single <c>cosmetics</c> object, so the server and
    /// every future render surface (leaderboard rows, duel plates, the web) can take it whole.
    ///
    /// Everything here is nullable/optional by design: this object is written by an older client,
    /// echoed by a server that may not know the field yet, and read back on a machine whose art
    /// pool differs. <see cref="Sanitize"/> is the single funnel that turns whatever arrived into
    /// something renderable - unknown ids become "none", never an exception and never a blocked
    /// card.
    ///
    /// <c>avatar_deco</c> and <c>charms</c> are carried (and validated, and synced) here from day
    /// one but are only RENDERED by Phase 3's wardrobe. Round-tripping them now means a Phase 2
    /// client cannot silently wipe a Phase 3 loadout.
    /// </summary>
    /// <summary>
    /// How a worn wardrobe item is placed (wardrobe editor: drag / resize / rotate / flip). All
    /// values are normalized so every viewer reconstructs the same composition at any render size:
    /// for a decoration, <see cref="X"/>/<see cref="Y"/> are offsets from its centred-on-avatar
    /// position in fractions of the decoration canvas; for a charm they are the sprite's centre in
    /// fractions of the hero card's width/height. <see cref="Scale"/> multiplies the item's base
    /// size, <see cref="Rotation"/> is degrees clockwise, <see cref="Flip"/> mirrors horizontally.
    /// A missing/null transform means "default placement" and renders exactly like pre-editor
    /// builds, so old payloads and old clients stay compatible in both directions.
    /// </summary>
    public class CosmeticTransform
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("s")] public double Scale { get; set; } = 1.0;
        [JsonProperty("r")] public double Rotation { get; set; }
        [JsonProperty("f")] public bool Flip { get; set; }

        public CosmeticTransform Clone() => new()
        {
            X = X, Y = Y, Scale = Scale, Rotation = Rotation, Flip = Flip
        };

        /// <summary>True when this transform changes nothing - not worth carrying in the payload.</summary>
        [JsonIgnore]
        public bool IsIdentity =>
            Math.Abs(X) < 0.0005 && Math.Abs(Y) < 0.0005 &&
            Math.Abs(Scale - 1.0) < 0.0005 && Math.Abs(Rotation) < 0.05 && !Flip;

        /// <summary>
        /// A clamped copy safe to render and to send. <paramref name="positional"/> selects the
        /// charm envelope (centre must stay on the card, [0,1]) over the decoration envelope
        /// (offset from centre, [-0.75, 0.75]). Non-finite garbage degrades to the default.
        /// </summary>
        public static CosmeticTransform? Sanitize(CosmeticTransform? raw, bool positional)
        {
            if (raw == null) return null;

            static double Num(double v, double fallback) =>
                double.IsNaN(v) || double.IsInfinity(v) ? fallback : v;

            var clean = new CosmeticTransform
            {
                X = Math.Clamp(Num(raw.X, positional ? 0.5 : 0), positional ? 0 : -0.75, positional ? 1 : 0.75),
                Y = Math.Clamp(Num(raw.Y, positional ? 0.5 : 0), positional ? 0 : -0.75, positional ? 1 : 0.75),
                Scale = Math.Clamp(Num(raw.Scale, 1), 0.3, 3.0),
                Rotation = Math.Clamp(Num(raw.Rotation, 0), -180, 180),
                Flip = raw.Flip
            };

            // Positional transforms always carry a real placement; offset transforms that end up
            // identity are dropped so the payload stays as small as a pre-editor one.
            if (!positional && clean.IsIdentity) return null;
            return clean;
        }
    }

    public class ProfileCosmetics
    {
        /// <summary>Showcase pins the hero card can display (spec: max 4).</summary>
        public const int MaxPinnedAchievements = 4;

        /// <summary>Card charms the hero card can display (spec: max 2, Phase 3).</summary>
        public const int MaxCharms = 2;

        /// <summary>
        /// The six curated accents, exactly as the spec fixes them. Anything else is rejected by
        /// <see cref="Sanitize"/> - the palette is a design decision, not a colour picker.
        /// </summary>
        public static readonly IReadOnlyList<string> AccentSwatches = new[]
        {
            "#FF69B4", // pink
            "#B478FF", // purple
            "#5EC8F2", // drone cyan
            "#43B581", // presence green
            "#FFD700", // gold
            "#FF5C7A"  // rose
        };

        [JsonProperty("banner_id")]
        public string? BannerId { get; set; }

        /// <summary>
        /// Preset-avatar id (CosmeticsCatalog.AvatarPresets) shown when no Discord picture is
        /// shared. NullValueHandling.Ignore so payloads written before this field existed keep
        /// serializing byte-identically.
        /// </summary>
        [JsonProperty("avatar_id", NullValueHandling = NullValueHandling.Ignore)]
        public string? AvatarId { get; set; }

        [JsonProperty("accent")]
        public string? Accent { get; set; }

        /// <summary>An unlocked achievement id whose localized name is worn as a title.</summary>
        [JsonProperty("title_id")]
        public string? TitleId { get; set; }

        [JsonProperty("pinned_achievements", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> PinnedAchievements { get; set; } = new();

        /// <summary>Phase 3 registry id (Resources/cosmetics/registry.json). Carried, not rendered yet.</summary>
        [JsonProperty("avatar_deco")]
        public string? AvatarDeco { get; set; }

        /// <summary>Phase 3 registry ids, max 2. Carried, not rendered yet.</summary>
        [JsonProperty("charms", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> Charms { get; set; } = new();

        /// <summary>Wardrobe-editor placement of the worn decoration. Null = default (centred).</summary>
        [JsonProperty("deco_transform", NullValueHandling = NullValueHandling.Ignore)]
        public CosmeticTransform? DecoTransform { get; set; }

        /// <summary>
        /// Wardrobe-editor placement per equipped charm, keyed by charm id so the transform
        /// follows the item through reorders. Entries for unequipped ids are dropped by Sanitize.
        /// </summary>
        [JsonProperty("charm_transforms", NullValueHandling = NullValueHandling.Ignore,
                      ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, CosmeticTransform>? CharmTransforms { get; set; }

        /// <summary>True when nothing is equipped - the card renders exactly as it did in Phase 1.</summary>
        [JsonIgnore]
        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(BannerId) &&
            string.IsNullOrWhiteSpace(AvatarId) &&
            string.IsNullOrWhiteSpace(Accent) &&
            string.IsNullOrWhiteSpace(TitleId) &&
            (PinnedAchievements == null || PinnedAchievements.Count == 0) &&
            string.IsNullOrWhiteSpace(AvatarDeco) &&
            (Charms == null || Charms.Count == 0);

        /// <summary>A detached copy - the customize dialog edits a clone so Cancel really cancels.</summary>
        public ProfileCosmetics Clone() => new()
        {
            BannerId = BannerId,
            AvatarId = AvatarId,
            Accent = Accent,
            TitleId = TitleId,
            PinnedAchievements = new List<string>(PinnedAchievements ?? new List<string>()),
            AvatarDeco = AvatarDeco,
            Charms = new List<string>(Charms ?? new List<string>()),
            DecoTransform = DecoTransform?.Clone(),
            CharmTransforms = CharmTransforms?.ToDictionary(
                kv => kv.Key, kv => kv.Value.Clone(), StringComparer.Ordinal)
        };

        /// <summary>
        /// Normalizes whatever arrived (local settings file, /user/sync echo, someone else's
        /// /user/lookup) into something safe to render and safe to send.
        ///
        /// Every "known" set is optional: pass null to skip that check. That matters for VIEWED
        /// profiles - we know which achievements exist, but not which ones THEY unlocked, so the
        /// unlock filter is only applied to our own card where the answer is knowable. A pin we
        /// cannot verify still renders; an id nothing in the app recognises does not.
        ///
        /// Never throws. A null/garbage input yields an empty (all-none) object.
        /// </summary>
        /// <param name="raw">Untrusted cosmetics, possibly null.</param>
        /// <param name="knownBannerIds">Banner ids this build ships art for, or null to skip.</param>
        /// <param name="knownAchievementIds">Every achievement id in the app, or null to skip.</param>
        /// <param name="unlockedAchievementIds">The card owner's unlocks, or null when unknowable.</param>
        /// <param name="knownDecoIds">Phase 3 registry ids of type <c>deco</c>, or null to skip.</param>
        /// <param name="knownCharmIds">Phase 3 registry ids of type <c>charm</c>, or null to skip.</param>
        /// <param name="knownAvatarIds">Preset-avatar ids this build ships art for, or null to skip.</param>
        /// <param name="wardrobeAchievementGates">Registry gates (item id → required achievement id),
        /// or null to skip. Only enforced when <paramref name="unlockedAchievementIds"/> is also
        /// non-null - a gate we cannot check against real unlock data must never strip a loadout
        /// (VIEWED cards, early boot).</param>
        public static ProfileCosmetics Sanitize(
            ProfileCosmetics? raw,
            ISet<string>? knownBannerIds = null,
            ISet<string>? knownAchievementIds = null,
            ISet<string>? unlockedAchievementIds = null,
            ISet<string>? knownDecoIds = null,
            ISet<string>? knownCharmIds = null,
            ISet<string>? knownAvatarIds = null,
            IReadOnlyDictionary<string, string>? wardrobeAchievementGates = null)
        {
            var clean = new ProfileCosmetics();
            if (raw == null) return clean;

            try
            {
                // ---- banner ----
                var banner = Trim(raw.BannerId);
                if (banner != null && (knownBannerIds == null || knownBannerIds.Contains(banner)))
                    clean.BannerId = banner;

                // ---- preset avatar ----
                var avatar = Trim(raw.AvatarId);
                if (avatar != null && (knownAvatarIds == null || knownAvatarIds.Contains(avatar)))
                    clean.AvatarId = avatar;

                // ---- accent ----
                var accent = Trim(raw.Accent);
                if (accent != null)
                {
                    var normalized = accent.StartsWith("#", StringComparison.Ordinal)
                        ? "#" + accent.Substring(1).ToUpperInvariant()
                        : "#" + accent.ToUpperInvariant();
                    if (AccentSwatches.Contains(normalized)) clean.Accent = normalized;
                }

                // ---- title ----
                var title = Trim(raw.TitleId);
                if (title != null && IsWearableAchievement(title, knownAchievementIds, unlockedAchievementIds))
                    clean.TitleId = title;

                // ---- pins ----
                if (raw.PinnedAchievements != null)
                {
                    foreach (var id in raw.PinnedAchievements)
                    {
                        if (clean.PinnedAchievements.Count >= MaxPinnedAchievements) break;
                        var pin = Trim(id);
                        if (pin == null) continue;
                        if (clean.PinnedAchievements.Contains(pin)) continue;
                        if (!IsWearableAchievement(pin, knownAchievementIds, unlockedAchievementIds)) continue;
                        clean.PinnedAchievements.Add(pin);
                    }
                }

                // ---- wardrobe (Phase 3 payload, carried through untouched but validated) ----
                // Validated PER SLOT, not against the union of the registry: the server checks
                // avatar_deco against its deco ids and charms against its charm ids (ccp-server
                // proxy/cosmetics.js). A union check here would let a charm id equip into the
                // decoration slot, render locally, and then be silently stripped on the way up -
                // so the wearer would see it forever and no viewer ever would.
                // Achievement gate, own-card only: enforced solely when BOTH the gate table and
                // the owner's unlock list are knowable (see the parameter doc above).
                bool PassesGate(string itemId)
                {
                    if (wardrobeAchievementGates == null || unlockedAchievementIds == null) return true;
                    return !wardrobeAchievementGates.TryGetValue(itemId, out var required)
                        || unlockedAchievementIds.Contains(required);
                }

                var deco = Trim(raw.AvatarDeco);
                if (deco != null && (knownDecoIds == null || knownDecoIds.Contains(deco)) && PassesGate(deco))
                    clean.AvatarDeco = deco;

                if (raw.Charms != null)
                {
                    foreach (var id in raw.Charms)
                    {
                        if (clean.Charms.Count >= MaxCharms) break;
                        var charm = Trim(id);
                        if (charm == null) continue;
                        if (clean.Charms.Contains(charm)) continue;
                        if (knownCharmIds != null && !knownCharmIds.Contains(charm)) continue;
                        if (!PassesGate(charm)) continue;
                        clean.Charms.Add(charm);
                    }
                }

                // ---- editor transforms (clamped, and only for items actually worn) ----
                if (clean.AvatarDeco != null)
                    clean.DecoTransform = CosmeticTransform.Sanitize(raw.DecoTransform, positional: false);

                if (raw.CharmTransforms != null && clean.Charms.Count > 0)
                {
                    foreach (var (id, t) in raw.CharmTransforms)
                    {
                        var key = Trim(id);
                        if (key == null || !clean.Charms.Contains(key)) continue;
                        var cleanT = CosmeticTransform.Sanitize(t, positional: true);
                        if (cleanT == null) continue;
                        clean.CharmTransforms ??= new Dictionary<string, CosmeticTransform>(StringComparer.Ordinal);
                        clean.CharmTransforms[key] = cleanT;
                    }
                }
            }
            catch
            {
                // A malformed payload degrades to "no cosmetics" - it must never cost the card.
                return new ProfileCosmetics();
            }

            return clean;
        }

        private static bool IsWearableAchievement(
            string id, ISet<string>? known, ISet<string>? unlocked)
        {
            if (known != null && !known.Contains(id)) return false;
            if (unlocked != null && !unlocked.Contains(id)) return false;
            return true;
        }

        private static string? Trim(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }
}
