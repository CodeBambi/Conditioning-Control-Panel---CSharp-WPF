using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// Where a companion-content channel was actually resolved from. The distinction matters
    /// because a built-in mod can carry its content in three very different places:
    /// an extracted <c>.ccpmod</c> (Drone/Locked), the per-mod folder bundled in the install dir,
    /// or that same per-mod folder delivered later by a downloaded content pack (Bambi/Sissy).
    /// </summary>
    public enum CompanionContentSource
    {
        /// <summary>Nothing matched - the caller falls back to its own shared/baseline behaviour.</summary>
        None = 0,
        /// <summary>The active mod's extracted .ccpmod folder (<c>ModPackage.InstalledPath</c>).</summary>
        PackagedMod,
        /// <summary>Per-mod folder bundled in the install dir (<c>Resources\sounds\companion_audio\mods\id</c>).</summary>
        ModInstallDir,
        /// <summary>The same per-mod folder, delivered by a downloaded content pack.</summary>
        ModContentPack,
        /// <summary>The shared, mod-independent baseline tree.</summary>
        Baseline,
        /// <summary>The in-code <c>ModManifest</c> (personalities only).</summary>
        BuiltInManifest,
        /// <summary>The stock personality presets - i.e. the DEFAULT mod's companion voice.</summary>
        StockPresets
    }

    /// <summary>One kind of companion content that has to be resolved per mod.</summary>
    public enum CompanionChannel
    {
        /// <summary>The mod's <c>bark_rules.json</c> overlay (merged over the shared base manifest).</summary>
        BarkRules,
        /// <summary>The mod's <c>personalities.json</c> - the AI companion's persona presets.</summary>
        Personalities,
        /// <summary>The mod's idle voice-line folder (<c>flashes_audio</c>).</summary>
        VoiceLines,
        /// <summary>The mod's event-comment audio folder (<c>event_audio</c>).</summary>
        EventAudio,
        /// <summary>The mod's <c>mantras.json</c>.</summary>
        Mantras,
        /// <summary>The mod's <c>avatar_manifest.json</c>.</summary>
        AvatarManifest,
        /// <summary>A single named bark voiceline file.</summary>
        BarkAudio
    }

    /// <summary>One place a channel could live, in priority order.</summary>
    /// <param name="Source">Which root this candidate belongs to.</param>
    /// <param name="Path">Absolute path to probe.</param>
    /// <param name="IsDirectory">True when the candidate is a folder rather than a file.</param>
    public readonly record struct CompanionContentCandidate(
        CompanionContentSource Source, string Path, bool IsDirectory);

    /// <summary>The winning candidate, or <see cref="CompanionContentSource.None"/>.</summary>
    /// <param name="Source">Where the content came from.</param>
    /// <param name="Path">Absolute path, or null when nothing matched.</param>
    public readonly record struct CompanionContentPick(CompanionContentSource Source, string? Path)
    {
        /// <summary>True when a real source was found.</summary>
        public bool Found => Source != CompanionContentSource.None && !string.IsNullOrEmpty(Path);
    }

    /// <summary>
    /// PURE path logic for "where does THIS mod's companion content live?".
    ///
    /// Every consumer (bark rules, voice lines, event audio, mantras, avatar manifest,
    /// personalities) walks the same ladder:
    ///
    ///   1. the extracted .ccpmod, when the mod has one (Drone, Locked)
    ///   2. the per-mod folder in the INSTALL DIR (full installs ship the manifests here)
    ///   3. the per-mod folder under the DOWNLOADED CONTENT ROOT (modular installs - the
    ///      bambi / sissy packs land here, docs/CONTENT_PACKS_PLAN.md section 3)
    ///   4. the shared baseline - which for the AI companion means the DEFAULT mod's voice,
    ///      and is exactly the silent fallback that made BambiSleep/SissyHypno feel dead.
    ///
    /// This class does no I/O: it builds the ladder and picks the first candidate a caller-supplied
    /// probe accepts, which keeps it unit-testable.
    ///
    /// Consumers migrated so far: <see cref="Bark.BarkRuleLoader"/>, <c>BarkService.ResolveBarkAudio</c>
    /// and <c>PersonalityService</c>. <c>CompanionPhraseService</c>, <c>AvatarPortraitLoader</c> and
    /// <c>MantraVoiceService</c> still hand-roll the same ladder (they need the two-root UNION for
    /// enumeration, not a single winner); this type reports on their channels for the activation log
    /// so the answer is at least visible in one place.
    /// </summary>
    public static class CompanionContentResolver
    {
        /// <summary>Install-dir-relative anchor of the shared companion-audio tree.</summary>
        public static readonly string CompanionAudioRelativeDir =
            Path.Combine("Resources", "sounds", "companion_audio");

        /// <summary>Install-dir-relative anchor of the shared voice-line tree.</summary>
        public static readonly string VoiceLinesRelativeDir =
            Path.Combine("Resources", "sounds", "flashes_audio");

        /// <summary>Folder inside a packaged .ccpmod that mirrors the app's own Resources tree.</summary>
        public const string PackagedResourcesFolder = "resources";

        /// <summary>Bark rule manifest file name.</summary>
        public const string BarkRulesFileName = "bark_rules.json";

        /// <summary>Mantra pool file name.</summary>
        public const string MantrasFileName = "mantras.json";

        /// <summary>Avatar portrait manifest file name.</summary>
        public const string AvatarManifestFileName = "avatar_manifest.json";

        /// <summary>
        /// Optional per-mod persona file. Same shape as <c>ModManifest.Personalities</c>
        /// (a JSON array of id / name / description / promptSettings objects), so a mod can ship
        /// AI personalities as DATA in its content pack without the manifest being repacked.
        /// </summary>
        public const string PersonalitiesFileName = "personalities.json";

        /// <summary>Idle voice-line folder name.</summary>
        public const string VoiceLinesFolderName = "flashes_audio";

        /// <summary>Event-comment audio folder name.</summary>
        public const string EventAudioFolderName = "event_audio";

        /// <summary>The per-mod folder for <paramref name="modId"/>, relative to a root.</summary>
        public static string PerModRelativeDir(string modId) =>
            Path.Combine(CompanionAudioRelativeDir, "mods", modId);

        /// <summary>
        /// Whether the shared baseline voice-line library (<c>Resources\sounds\flashes_audio</c>) is
        /// THIS mod's own voice.
        ///
        /// That folder is not a neutral fallback: it is BambiSleep's recorded VO, named line by line
        /// ("be a good girl accept your conditioning.mp3"), and <c>FlashService</c> both plays it and
        /// fires <c>FlashAudioPlaying</c> so the avatar prints that file name in HER speech bubble.
        /// Handing it to another character therefore makes that character speak Bambi's lines in
        /// Bambi's voice - which is exactly what a mod that ships bark audio but no
        /// <c>flashes_audio</c> folder (the Infection Control creator mod) did.
        ///
        /// Only the two mods the library belongs to get it: BambiSleep, and CCP Default (the neutral
        /// baseline persona, which has always spoken with these clips). Every other mod either ships
        /// its own folder - Sissy per-mod, Drone/Locked inside their .ccpmod - or stays silent on
        /// this channel, the same way <see cref="CompanionChannel.EventAudio"/> already leaves a mod
        /// that ships none text-only.
        ///
        /// An unknown/absent mod id (ModService not up yet) keeps the baseline, so startup order can
        /// never take the default companion silent.
        /// </summary>
        public static bool OwnsBaselineVoiceLines(string? modId) =>
            string.IsNullOrWhiteSpace(modId)
            || string.Equals(modId, BuiltInMods.BambiSleepId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(modId, BuiltInMods.CCPDefaultId, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The ordered candidate list for one channel. Roots that are empty (no content root on a
        /// full install, no InstalledPath on a built-in mod) simply contribute no candidates, so the
        /// ladder shortens instead of producing bogus paths.
        /// </summary>
        /// <param name="channel">Which content channel to resolve.</param>
        /// <param name="modId">Active mod id (e.g. builtin-bambisleep).</param>
        /// <param name="installedPath">The mod's extracted .ccpmod folder, or null for a pure built-in.</param>
        /// <param name="installRoot">Install directory root.</param>
        /// <param name="contentRoot">Downloaded content-pack root.</param>
        /// <param name="fileName">Required for <see cref="CompanionChannel.BarkAudio"/>: the voiceline file name.</param>
        public static IReadOnlyList<CompanionContentCandidate> Candidates(
            CompanionChannel channel,
            string? modId,
            string? installedPath,
            string? installRoot,
            string? contentRoot,
            string? fileName = null)
        {
            var list = new List<CompanionContentCandidate>(4);

            // What the channel is called inside a packaged mod / inside the per-mod folder, and
            // whether it is a file or a folder.
            string? leaf;
            bool isDir;
            switch (channel)
            {
                case CompanionChannel.BarkRules: leaf = BarkRulesFileName; isDir = false; break;
                case CompanionChannel.Mantras: leaf = MantrasFileName; isDir = false; break;
                case CompanionChannel.AvatarManifest: leaf = AvatarManifestFileName; isDir = false; break;
                case CompanionChannel.Personalities: leaf = PersonalitiesFileName; isDir = false; break;
                case CompanionChannel.VoiceLines: leaf = VoiceLinesFolderName; isDir = true; break;
                case CompanionChannel.EventAudio: leaf = EventAudioFolderName; isDir = true; break;
                case CompanionChannel.BarkAudio: leaf = fileName; isDir = false; break;
                default: return list;
            }
            if (string.IsNullOrWhiteSpace(leaf)) return list;

            // 1) packaged .ccpmod. Voice lines and event audio live under resources\sounds\ there,
            //    personalities at the resources root, everything else under
            //    resources\sounds\companion_audio\.
            if (!string.IsNullOrWhiteSpace(installedPath))
            {
                var packaged = channel switch
                {
                    CompanionChannel.VoiceLines or CompanionChannel.EventAudio =>
                        Path.Combine(installedPath, PackagedResourcesFolder, "sounds", leaf),
                    CompanionChannel.Personalities =>
                        Path.Combine(installedPath, PackagedResourcesFolder, leaf),
                    _ => Path.Combine(installedPath, PackagedResourcesFolder,
                                      "sounds", "companion_audio", leaf)
                };
                list.Add(new CompanionContentCandidate(CompanionContentSource.PackagedMod, packaged, isDir));
            }

            // 2) + 3) the per-mod folder, install dir first then the downloaded content root.
            if (!string.IsNullOrWhiteSpace(modId))
            {
                var perMod = Path.Combine(PerModRelativeDir(modId), leaf);
                if (!string.IsNullOrWhiteSpace(installRoot))
                    list.Add(new CompanionContentCandidate(
                        CompanionContentSource.ModInstallDir, Path.Combine(installRoot, perMod), isDir));
                if (!string.IsNullOrWhiteSpace(contentRoot))
                    list.Add(new CompanionContentCandidate(
                        CompanionContentSource.ModContentPack, Path.Combine(contentRoot, perMod), isDir));
            }

            // 4) the shared baseline, for the channels that have one. Bark rules deliberately do NOT
            //    get one here: the base manifest is always merged UNDER the mod overlay by
            //    BarkRuleLoader, so listing it as a candidate would report "resolved: baseline" for
            //    a mod that simply has no overlay of its own.
            switch (channel)
            {
                // Voice lines only fall through to the baseline for the mods whose voice that library
                // actually is — see OwnsBaselineVoiceLines. Anyone else stays silent rather than
                // borrowing another character's recordings (and her speech bubble).
                case CompanionChannel.VoiceLines when OwnsBaselineVoiceLines(modId):
                    if (!string.IsNullOrWhiteSpace(installRoot))
                        list.Add(new CompanionContentCandidate(CompanionContentSource.Baseline,
                            Path.Combine(installRoot, VoiceLinesRelativeDir), true));
                    if (!string.IsNullOrWhiteSpace(contentRoot))
                        list.Add(new CompanionContentCandidate(CompanionContentSource.Baseline,
                            Path.Combine(contentRoot, VoiceLinesRelativeDir), true));
                    break;
                case CompanionChannel.BarkAudio:
                    if (!string.IsNullOrWhiteSpace(installRoot))
                        list.Add(new CompanionContentCandidate(CompanionContentSource.Baseline,
                            Path.Combine(installRoot, CompanionAudioRelativeDir, leaf), false));
                    if (!string.IsNullOrWhiteSpace(contentRoot))
                        list.Add(new CompanionContentCandidate(CompanionContentSource.Baseline,
                            Path.Combine(contentRoot, CompanionAudioRelativeDir, leaf), false));
                    break;
            }

            return list;
        }

        /// <summary>
        /// First candidate the probe accepts, or <see cref="CompanionContentSource.None"/>.
        /// A throwing probe is treated as "not there" so one unreadable root can never take the
        /// companion silent.
        /// </summary>
        public static CompanionContentPick Pick(
            IReadOnlyList<CompanionContentCandidate> candidates,
            Func<CompanionContentCandidate, bool> probe)
        {
            if (candidates == null || probe == null)
                return new CompanionContentPick(CompanionContentSource.None, null);

            foreach (var c in candidates)
            {
                bool hit;
                try { hit = probe(c); }
                catch { hit = false; }
                if (hit) return new CompanionContentPick(c.Source, c.Path);
            }
            return new CompanionContentPick(CompanionContentSource.None, null);
        }

        /// <summary>Convenience overload that builds the ladder and picks in one call.</summary>
        public static CompanionContentPick Resolve(
            CompanionChannel channel,
            string? modId,
            string? installedPath,
            string? installRoot,
            string? contentRoot,
            Func<CompanionContentCandidate, bool> probe,
            string? fileName = null)
            => Pick(Candidates(channel, modId, installedPath, installRoot, contentRoot, fileName), probe);

        /// <summary>
        /// Which source the AI personalities came from, given what the two non-path rungs hold.
        /// Kept separate from <see cref="Resolve"/> because two of the three rungs are not files:
        /// the in-code manifest, and the stock preset list.
        ///
        /// <see cref="CompanionContentSource.StockPresets"/> is the answer that means
        /// "this mod's AI companion is speaking in the DEFAULT mod's voice".
        /// </summary>
        public static CompanionContentSource ResolvePersonalitySource(
            CompanionContentSource fileSource, bool manifestHasPersonalities)
        {
            if (fileSource != CompanionContentSource.None) return fileSource;
            return manifestHasPersonalities
                ? CompanionContentSource.BuiltInManifest
                : CompanionContentSource.StockPresets;
        }

        /// <summary>Short, log-friendly name for a source.</summary>
        public static string Describe(CompanionContentSource source) => source switch
        {
            CompanionContentSource.PackagedMod => "packaged-mod",
            CompanionContentSource.ModInstallDir => "mod-install-dir",
            CompanionContentSource.ModContentPack => "mod-content-pack",
            CompanionContentSource.Baseline => "baseline",
            CompanionContentSource.BuiltInManifest => "builtin-manifest",
            CompanionContentSource.StockPresets => "stock-presets",
            _ => "none"
        };
    }
}
