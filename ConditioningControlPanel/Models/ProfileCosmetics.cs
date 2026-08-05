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

        /// <summary>True when nothing is equipped - the card renders exactly as it did in Phase 1.</summary>
        [JsonIgnore]
        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(BannerId) &&
            string.IsNullOrWhiteSpace(Accent) &&
            string.IsNullOrWhiteSpace(TitleId) &&
            (PinnedAchievements == null || PinnedAchievements.Count == 0) &&
            string.IsNullOrWhiteSpace(AvatarDeco) &&
            (Charms == null || Charms.Count == 0);

        /// <summary>A detached copy - the customize dialog edits a clone so Cancel really cancels.</summary>
        public ProfileCosmetics Clone() => new()
        {
            BannerId = BannerId,
            Accent = Accent,
            TitleId = TitleId,
            PinnedAchievements = new List<string>(PinnedAchievements ?? new List<string>()),
            AvatarDeco = AvatarDeco,
            Charms = new List<string>(Charms ?? new List<string>())
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
        /// <param name="knownWardrobeIds">Phase 3 registry ids, or null to skip.</param>
        public static ProfileCosmetics Sanitize(
            ProfileCosmetics? raw,
            ISet<string>? knownBannerIds = null,
            ISet<string>? knownAchievementIds = null,
            ISet<string>? unlockedAchievementIds = null,
            ISet<string>? knownWardrobeIds = null)
        {
            var clean = new ProfileCosmetics();
            if (raw == null) return clean;

            try
            {
                // ---- banner ----
                var banner = Trim(raw.BannerId);
                if (banner != null && (knownBannerIds == null || knownBannerIds.Contains(banner)))
                    clean.BannerId = banner;

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
                var deco = Trim(raw.AvatarDeco);
                if (deco != null && (knownWardrobeIds == null || knownWardrobeIds.Contains(deco)))
                    clean.AvatarDeco = deco;

                if (raw.Charms != null)
                {
                    foreach (var id in raw.Charms)
                    {
                        if (clean.Charms.Count >= MaxCharms) break;
                        var charm = Trim(id);
                        if (charm == null) continue;
                        if (clean.Charms.Contains(charm)) continue;
                        if (knownWardrobeIds != null && !knownWardrobeIds.Contains(charm)) continue;
                        clean.Charms.Add(charm);
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
