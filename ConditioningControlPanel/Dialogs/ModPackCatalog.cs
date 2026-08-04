using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// One built-in mod's presentation data for the pack UIs (first-run picker + Mod Manager).
    /// </summary>
    public sealed class ModPackEntry
    {
        /// <summary>Built-in mod id (<see cref="BuiltInMods"/>).</summary>
        public string ModId { get; init; } = "";

        /// <summary>Release-content pack id, or null for CCP Default (nothing to download).</summary>
        public string? PackId { get; init; }

        public string NameLocKey { get; init; } = "";
        public string DescriptionLocKey { get; init; } = "";

        /// <summary>
        /// Pack URI of the card art. These are <c>&lt;Resource&gt;</c> items (compiled INTO the
        /// assembly), so they survive the content-pack strip in the csproj — see the class remarks.
        /// </summary>
        public string ArtUri { get; init; } = "";

        /// <summary>Mod accent colour, for the card's art frame.</summary>
        public string AccentHex { get; init; } = "#FF69B4";

        /// <summary>
        /// Fallback download size used before (or instead of) a manifest fetch — the picker must show
        /// honest numbers offline too. Overridden by the manifest's real sizeBytes when available.
        /// </summary>
        public long ApproxBytes { get; init; }

        /// <summary>The mod is free, but its flagship multi-day training program needs Premium.</summary>
        public bool PremiumProgramNote { get; init; }

        /// <summary>The mod ships no companion_audio — its companion is text-only.</summary>
        public bool NoVoiceNote { get; init; }
    }

    /// <summary>
    /// THE mapping between built-in mod ids and release-content pack ids, plus the card art/size/copy
    /// that the first-run <see cref="ModPickerDialog"/> and <see cref="ModManagerDialog"/> both draw
    /// from. Deliberately lives next to the dialogs (a UI concern) rather than in Services\ — the pack
    /// service knows only ids.
    ///
    /// Art note: every <see cref="ModPackEntry.ArtUri"/> points at a file declared as
    /// <c>&lt;Resource Include=...&gt;</c> in ConditioningControlPanel.csproj (Resources\intake\
    /// pass_card_*.png and Resources\logo.png). Resource items are compiled into the assembly and are
    /// untouched by $(ContentPackSoundsExclude)/$(ContentPackWebExclude), which only strip
    /// &lt;Content&gt; globs — so these thumbnails can never be the thing that is missing on a fresh
    /// modular install.
    /// </summary>
    public static class ModPackCatalog
    {
        private const long Mb = 1024L * 1024L;

        /// <summary>Display order: baseline first, then the four optional mods.</summary>
        public static readonly IReadOnlyList<ModPackEntry> All = new[]
        {
            new ModPackEntry
            {
                ModId = BuiltInMods.CCPDefaultId,
                PackId = null, // ships in the box — the neutral baseline is what "skip everything" gives you
                NameLocKey = "modpicker_name_ccp_default",
                DescriptionLocKey = "modpicker_desc_ccp_default",
                ArtUri = "pack://application:,,,/Resources/logo.png",
                AccentHex = "#E84393",
                ApproxBytes = 0
            },
            new ModPackEntry
            {
                ModId = BuiltInMods.BambiSleepId,
                PackId = ReleaseContentService.PackModBambi,
                NameLocKey = "label_bambi_sleep",
                DescriptionLocKey = "modpicker_desc_bambi",
                ArtUri = "pack://application:,,,/Resources/intake/pass_card_bambi.png",
                AccentHex = "#FF69B4",
                ApproxBytes = 77 * Mb
            },
            new ModPackEntry
            {
                ModId = BuiltInMods.SissyHypnoId,
                PackId = ReleaseContentService.PackModSissy,
                NameLocKey = "label_sissy_hypno",
                DescriptionLocKey = "modpicker_desc_sissy",
                ArtUri = "pack://application:,,,/Resources/intake/pass_card_sissy.png",
                AccentHex = "#9B59B6",
                ApproxBytes = 331 * Mb
            },
            new ModPackEntry
            {
                ModId = BuiltInMods.LockedId,
                PackId = ReleaseContentService.PackModLocked,
                NameLocKey = "modpicker_name_circe",
                DescriptionLocKey = "modpicker_desc_circe",
                ArtUri = "pack://application:,,,/Resources/intake/pass_card_circe.png",
                AccentHex = "#E81CA8",
                ApproxBytes = 329 * Mb,
                PremiumProgramNote = true // the "kept" program is Premium; the mod itself is free
            },
            new ModPackEntry
            {
                ModId = BuiltInMods.DronificationId,
                PackId = ReleaseContentService.PackModDrone,
                NameLocKey = "modpicker_name_drone",
                DescriptionLocKey = "modpicker_desc_drone",
                ArtUri = "pack://application:,,,/Resources/intake/pass_card_drone.png",
                AccentHex = "#00FF41",
                ApproxBytes = 184 * Mb,
                PremiumProgramNote = true, // "firmware_install" is Premium
                NoVoiceNote = true         // drone-mode.ccpmod carries no companion_audio at all
            },
        };

        /// <summary>The four optional mods (everything with a downloadable pack).</summary>
        public static IEnumerable<ModPackEntry> Optional => All.Where(e => !string.IsNullOrEmpty(e.PackId));

        public static ModPackEntry? ForMod(string? modId) =>
            string.IsNullOrEmpty(modId)
                ? null
                : All.FirstOrDefault(e => string.Equals(e.ModId, modId, StringComparison.OrdinalIgnoreCase));

        public static ModPackEntry? ForPack(string? packId) =>
            string.IsNullOrEmpty(packId)
                ? null
                : All.FirstOrDefault(e => string.Equals(e.PackId, packId, StringComparison.OrdinalIgnoreCase));

        /// <summary>Pack id for a built-in mod id, or null (CCP Default and every user mod).</summary>
        public static string? PackIdForMod(string? modId) => ForMod(modId)?.PackId;

        /// <summary>
        /// Best known download size: the manifest's real number when it has been fetched, otherwise the
        /// baked-in approximation so an offline picker still tells the truth about the cost.
        /// </summary>
        public static long SizeBytesFor(ModPackEntry entry)
        {
            try
            {
                if (entry == null || string.IsNullOrEmpty(entry.PackId)) return 0;
                var info = App.ReleaseContent?.GetPackInfo(entry.PackId!);
                if (info != null && info.SizeBytes > 0) return info.SizeBytes;
                return entry.ApproxBytes;
            }
            catch { return entry?.ApproxBytes ?? 0; }
        }

        /// <summary>"331 MB" / "1.2 GB". Empty for a zero size.</summary>
        public static string FormatSize(long bytes)
        {
            try
            {
                if (bytes <= 0) return "";
                var mb = bytes / (double)Mb;
                if (mb >= 1024)
                    return Loc.GetF("modpicker_size_gb", (mb / 1024.0).ToString("0.0"));
                return Loc.GetF("modpicker_size_mb", Math.Max(1, (int)Math.Round(mb)));
            }
            catch { return ""; }
        }

        /// <summary>
        /// True when this mod's media has to come off the network: a modular install whose pack is not
        /// stamped yet. False for CCP Default, for full/dev layouts, and when the service is missing.
        /// </summary>
        public static bool NeedsDownload(string? modId)
        {
            try
            {
                var packId = PackIdForMod(modId);
                if (string.IsNullOrEmpty(packId)) return false;

                var svc = App.ReleaseContent;
                if (svc == null) return false;
                if (svc.IsFullInstall) return false;

                return !svc.IsInstalled(packId!);
            }
            catch { return false; }
        }
    }
}
