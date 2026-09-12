using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>How a chip wears its picture.</summary>
    public enum RailArtFit
    {
        /// <summary>
        /// Scene art (the 1376x768 feature illustrations, the Lockdown banner). Painted as the
        /// chip's own Background through an ImageBrush, cover-fitted and cropped by
        /// <see cref="ModArtFramingRegistry.SurfaceRailChip"/>, with a foot scrim under the
        /// caption.
        /// </summary>
        Cover,

        /// <summary>
        /// Square icon art (the 64x64 nav door medallions). The chip is still filled edge to
        /// edge, but with a PLATE rather than a crop: the same icon blown up soft and darkened
        /// as the backdrop, the icon itself at 26 DIP over it. Cover-fitting a square icon into
        /// a 1.9:1 chip cuts the top and bottom off the drawing, and dropping the icon on a flat
        /// tint at glyph size is what the first pass shipped - the desk read it as "a small icon
        /// next to a caption, barely different from the emoji it replaced".
        /// </summary>
        Plate,
    }

    /// <summary>One destination's picture.</summary>
    /// <param name="ResourcePath">
    /// Resources-relative path, e.g. <c>features/vault.png</c>. This is the MOD COMPATIBILITY
    /// SURFACE and is never renamed: a .ccpmod that re-skins <c>features/flash.png</c> re-skins
    /// every surface that names it, the rail chips included.
    /// </param>
    /// <param name="Fit">How the chip wears it.</param>
    public sealed record RailChipArt(string ResourcePath, RailArtFit Fit);

    /// <summary>
    /// Which picture each FAVORITES / RECENT chip shows (owner ask 2026-09-12: "use the images
    /// for the different features in the favourite and recent rails").
    ///
    /// <para><b>Why a table and not a convention.</b> The rail's ids are Ctrl+K palette rows and
    /// the art is keyed by the mod-facing resource path; the two vocabularies do not line up
    /// (<c>tab.exclusives</c> is <c>features/vault.png</c>, <c>tab.shelistening</c> is
    /// <c>features/audio_whispers.png</c>) and a name-mangling rule would silently resolve to
    /// nothing for half the rail. Every destination is listed, and a destination with no picture
    /// worth showing is listed as <c>null</c> - an EXPLICIT fallback to the palette glyph, so a
    /// new palette row fails <c>FavoritesRailArtTests</c> instead of quietly losing its art.</para>
    ///
    /// <para>Pure static data plus arithmetic - no WPF, no file IO - so the table, the themed
    /// candidate order and the decode cap are all unit tested.</para>
    /// </summary>
    public static class FavoritesRailArt
    {
        /// <summary>
        /// Decode cap for a chip that shows its WHOLE source (a crop window of the full image).
        /// The chip is 69 DIP wide inside the 1585x901 design canvas, which scales to about 2.4x
        /// on a maximised 4K window, so ~167 real pixels; 192 covers that with headroom and keeps
        /// a 2MB neon PNG from decoding at full size for a thumbnail.
        /// </summary>
        public const int BaseDecodeWidth = 192;

        /// <summary>
        /// Ceiling for <see cref="DecodeWidthFor"/>. A very tight crop would otherwise ask for a
        /// decode larger than the source and buy nothing.
        /// </summary>
        public const int MaxDecodeWidth = 1024;

        /// <summary>
        /// Decode width for a plate's backdrop. TWELVE PIXELS, and that is the whole blur: the
        /// icon is decoded tiny and then stretched across the chip by the template's
        /// HighQuality scaling, which costs one small bitmap where a real BlurEffect would cost
        /// a render target per chip, fifteen chips deep, inside the dashboard's Viewbox.
        /// </summary>
        public const int PlateBackdropDecodeWidth = 12;

        /// <summary>
        /// Decode width for the icon a plate carries. It paints at 26 DIP, about 63 real pixels
        /// on a maximised 4K window, and the mod editor tells authors a 128px medallion is the
        /// crisp HiDPI size - so ask for exactly that and no more.
        /// </summary>
        public const int PlateIconDecodeWidth = 128;

        /// <summary>Side of the icon a plate carries, in DIP, inside a 69x36 chip.</summary>
        public const double PlateIconSize = 26;

        // ─── the table ───────────────────────────────────────────────
        // Keys are SettingsPaletteEntry.Id. Doors and their tab twin share a picture on purpose:
        // they are the same room, and the rail showing two different faces for one place was the
        // thing the palette-row-as-chip rule exists to prevent.
        //
        // EVERY destination has a picture. The null case is kept in the type and in For() for a
        // palette row added tomorrow and for art a mod breaks, but nothing ships on the glyph:
        // the desk pass on 2026-09-12 found the rail reading as icons beside captions, and a
        // column where two chips out of ten are emoji reads the same way.
        private static readonly Dictionary<string, RailChipArt?> _map = new(StringComparer.Ordinal)
        {
            // --- the seven doors, as their own nav medallions on a plate --------------
            ["door.home"] = Plate("nav/door_home.png"),
            ["door.studio"] = Plate("nav/door_studio.png"),
            ["door.companion"] = Plate("nav/door_companion.png"),
            ["door.play"] = Plate("nav/door_play.png"),
            ["door.you"] = Plate("nav/door_you.png"),
            ["door.library"] = Plate("nav/door_library.png"),
            ["door.settings"] = Plate("nav/door_settings.png"),
            // The tab rows that land on the same room as a door wear the same medallion.
            ["tab.settings"] = Plate("nav/door_home.png"),
            ["tab.studio"] = Plate("nav/door_studio.png"),
            ["tab.companion"] = Plate("nav/door_companion.png"),
            ["tab.play"] = Plate("nav/door_play.png"),
            ["tab.discord"] = Plate("nav/door_you.png"),
            ["tab.assets"] = Plate("nav/door_library.png"),
            ["tab.appsettings"] = Plate("nav/door_settings.png"),

            // --- features with their own illustration --------------------------------
            ["door.justdrop"] = Cover("features/justdrop.png"),
            ["tab.haptics"] = Cover("features/vibe.png"),
            ["tab.bambitakeover"] = Cover("features/takeover.png"),
            ["tab.shelistening"] = Cover("features/audio_whispers.png"),
            ["tab.awareness"] = Cover("features/awareness.png"),
            ["tab.deeper"] = Cover("features/deeper.png"),
            // The Velvet Vault storefront. vault.png ships themed faces (vault_bambi.png and
            // friends), so this chip re-skins with the active built-in mod for free.
            ["tab.exclusives"] = Cover("features/vault.png"),
            ["tab.gradedintake"] = Cover("features/lab_quiz_hero.png"),
            ["tab.lockdown"] = Cover("lockdown_icon.png"),
            ["tab.blinktrainer"] = Cover("features/blink_trainer.png"),
            ["tab.remotecontrol"] = Cover("features/remote_control.png"),
            ["tab.fyp"] = Cover("features/fyp.png"),
            // The Spiral Room borrows the spiral overlay's art: it is the only spiral we drew,
            // and the room is where a spiral means something rather than where one is switched on.
            ["tab.spiral"] = Cover("features/spiral_overlay.png"),

            // --- rooms with no art of their own -------------------------------------
            // These twelve had no picture that stands for the room, so the first pass left them
            // on the emoji. The desk pass asked for a second look, and the repo does ship one:
            // the achievement, skill and quest badges are drawn in the same neon hand as the
            // feature art, and each of these rooms has a badge ABOUT it. Every one is cropped to
            // its illustration by a railChip rect in ModArtFramingRegistry, because these files
            // carry the badge's name burned in under the drawing - the same reason the feature
            // art has rects.
            //
            // They stay ordinary mod paths: an author who reskins achievements/modder.png
            // reskins the Mods chip with it, exactly as one who reskins features/vault.png
            // reskins the Vault chip.
            ["tab.presets"] = Cover("Cards/spotlight.png"),            // a session, as the session-complete card draws one
            ["tab.availablesubjects"] = Cover("skills/hive_mind.png"), // two dolls and a live count
            ["tab.quests"] = Cover("quests/daily_devotion_d.png"),     // the daily quest tile
            ["tab.achievements"] = Cover("achievements/honor_roll.png"),
            ["tab.enhancements"] = Cover("skills/sparkle_boost_3.png"),// a skill node, which is what the tree is made of
            ["tab.programs"] = Cover("programs/plate_default.png"),    // the default program plate
            ["tab.leaderboard"] = Cover("skills/trophy_case.png"),
            ["card.arcademy"] = Cover("achievements/teachers_pet.png"),// the Arcademy is a school; its own plate is Content, not a pack Resource
            ["launch.mods"] = Cover("achievements/modder.png"),
            ["launch.catalogue"] = Cover("achievements/curator.png"),  // a wall of cards under a glass
            ["launch.phrases"] = Cover("achievements/word_perfect.png"),
            ["launch.medialog"] = Cover("achievements/screen_time.png"),
        };

        private static RailChipArt Cover(string path) => new(path, RailArtFit.Cover);
        private static RailChipArt Plate(string path) => new(path, RailArtFit.Plate);

        /// <summary>The whole table, including the explicit nulls.</summary>
        public static IReadOnlyDictionary<string, RailChipArt?> Map { get; } =
            new ReadOnlyDictionary<string, RailChipArt?>(_map);

        /// <summary>
        /// The picture for a palette row, or null when the chip keeps its glyph - both for a row
        /// listed as a deliberate fallback and for one this build has never heard of.
        /// </summary>
        public static RailChipArt? For(string? paletteId) =>
            paletteId != null && _map.TryGetValue(paletteId, out var art) ? art : null;

        /// <summary>True when the table has an opinion about this id, fallback included.</summary>
        public static bool Knows(string? paletteId) => paletteId != null && _map.ContainsKey(paletteId);

        /// <summary>
        /// The app-shipped themed filename fork for a built-in mod, or null for third-party mods
        /// and for the default. Same suffixes, in the same order of precedence, as the mosaic's
        /// <c>MainWindow.ModTileVariant</c> - a themed face only exists app-side, so it is tried
        /// first and a miss falls through to the base path (where a .ccpmod override wins).
        /// </summary>
        public static string? ThemeSuffix(string? activeModId) => activeModId switch
        {
            Models.BuiltInMods.BambiSleepId => "_bambi",
            Models.BuiltInMods.SissyHypnoId => "_sissy",
            Models.BuiltInMods.DronificationId => "_drone",
            Models.BuiltInMods.LockedId => "_locked",
            _ => null,
        };

        /// <summary>
        /// Resource paths to try, in order: the themed fork first when there is one, then the
        /// base path. "features/vault.png" + "_bambi" is "features/vault_bambi.png", and the
        /// suffix goes before the extension whatever folder the file sits in.
        /// </summary>
        public static IReadOnlyList<string> Candidates(string resourcePath, string? suffix)
        {
            if (string.IsNullOrWhiteSpace(resourcePath)) return Array.Empty<string>();
            if (string.IsNullOrEmpty(suffix)) return new[] { resourcePath };

            int dot = resourcePath.LastIndexOf('.');
            if (dot <= 0) return new[] { resourcePath };

            var themed = resourcePath.Substring(0, dot) + suffix + resourcePath.Substring(dot);
            return new[] { themed, resourcePath };
        }

        /// <summary>
        /// The decode cap for a cover chip, given the width of its crop window (0..1 of the
        /// source). A window of a quarter of the image shows a quarter of the decoded pixels, so
        /// a flat cap would hand a tightly-framed chip a blurry sixth of <see cref="BaseDecodeWidth"/>.
        /// Same reasoning the old rail's per-image DecodePixelWidth values were picked by hand with.
        /// </summary>
        public static int DecodeWidthFor(double viewboxWidth)
        {
            if (double.IsNaN(viewboxWidth) || double.IsInfinity(viewboxWidth) || viewboxWidth <= 0)
                return BaseDecodeWidth;
            var wanted = (int)Math.Ceiling(BaseDecodeWidth / Math.Min(1.0, viewboxWidth));
            return Math.Clamp(wanted, BaseDecodeWidth, MaxDecodeWidth);
        }

        /// <summary>Every distinct resource path the rail can paint. Test and diagnostics use.</summary>
        public static IEnumerable<string> ArtPaths() =>
            _map.Values.Where(a => a != null).Select(a => a!.ResourcePath).Distinct(StringComparer.Ordinal);
    }
}
