using System;
using System.Collections.Generic;
using System.Linq;

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
    /// The banner and preset-avatar pools both heads offer: ids, names and where the art lives.
    /// Split out of the WPF head's CosmeticsCatalog, which keeps the WPF decode/cache half and
    /// reads these lists from here; the Avalonia head decodes the same entries in Helpers/ModArt.
    /// </summary>
    public static class CosmeticsPool
    {
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

            // --- per-mod scene banners (Resources\banners\*.png, Resource; owner-approved
            //     one-shot set, authored at 2400x620 for the hero band - see the note above
            //     BannerDecodePixels for how they are framed and why) ---
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

        /// <summary>Disk path of a preset avatar's PNG (exists or not), or null for an unknown id.</summary>
        public static string? AvatarPath(string? id)
            => id != null && _avatarIds.Contains(id)
                ? System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "cosmetics", "avatars", id + ".png")
                : null;

        public static BannerOption? FindBanner(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return Banners.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.Ordinal));
        }
    }
}
