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
        /// A 64x64 medallion (the nav door icons). Drawn centred at its own aspect where the
        /// emoji used to sit, never cover-fitted: cropping a square icon to a 1.9:1 chip cuts
        /// the top and bottom off the drawing and leaves a stripe nobody can name.
        /// </summary>
        Icon,
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

        // ─── the table ───────────────────────────────────────────────
        // Keys are SettingsPaletteEntry.Id. Doors and their tab twin share a picture on purpose:
        // they are the same room, and the rail showing two different faces for one place was the
        // thing the palette-row-as-chip rule exists to prevent.
        private static readonly Dictionary<string, RailChipArt?> _map = new(StringComparer.Ordinal)
        {
            // --- the seven doors, as their own nav medallions ------------------------
            ["door.home"] = Icon("nav/door_home.png"),
            ["door.studio"] = Icon("nav/door_studio.png"),
            ["door.companion"] = Icon("nav/door_companion.png"),
            ["door.play"] = Icon("nav/door_play.png"),
            ["door.you"] = Icon("nav/door_you.png"),
            ["door.library"] = Icon("nav/door_library.png"),
            ["door.settings"] = Icon("nav/door_settings.png"),
            // The tab rows that land on the same room as a door wear the same medallion.
            ["tab.settings"] = Icon("nav/door_home.png"),
            ["tab.studio"] = Icon("nav/door_studio.png"),
            ["tab.companion"] = Icon("nav/door_companion.png"),
            ["tab.play"] = Icon("nav/door_play.png"),
            ["tab.discord"] = Icon("nav/door_you.png"),
            ["tab.assets"] = Icon("nav/door_library.png"),
            ["tab.appsettings"] = Icon("nav/door_settings.png"),

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

            // --- explicit fallbacks: the palette glyph stands -------------------------
            // Not an oversight in any of these cases:
            //  * presets / quests / achievements / enhancements / programs / leaderboard /
            //    availablesubjects are rooms full of per-item art (one PNG per quest, per skill,
            //    per program) with no picture that stands for the room;
            //  * the Arcademy's only plate lives under Resources/web, which ships as Content and
            //    is not reachable by a pack:// URI, so it would resolve to nothing at runtime;
            //  * the four Library launchers are dialogs and a website, not features with faces.
            ["tab.presets"] = null,
            ["tab.availablesubjects"] = null,
            ["tab.quests"] = null,
            ["tab.achievements"] = null,
            ["tab.enhancements"] = null,
            ["tab.programs"] = null,
            ["tab.leaderboard"] = null,
            ["card.arcademy"] = null,
            ["launch.mods"] = null,
            ["launch.catalogue"] = null,
            ["launch.phrases"] = null,
            ["launch.medialog"] = null,
        };

        private static RailChipArt Cover(string path) => new(path, RailArtFit.Cover);
        private static RailChipArt Icon(string path) => new(path, RailArtFit.Icon);

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
