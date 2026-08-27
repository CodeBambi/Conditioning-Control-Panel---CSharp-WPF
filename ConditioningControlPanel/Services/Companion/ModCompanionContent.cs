using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The I/O half of <see cref="CompanionContentResolver"/>: probes the two real roots
    /// (install dir + downloaded content packs) plus the active mod's extracted .ccpmod, and
    /// answers "where is this mod's companion content?" for the running app.
    ///
    /// Why this exists: only Drone and Locked ship a bundled <c>.ccpmod</c>, so
    /// <c>ModPackage.InstalledPath</c> is null for BambiSleep and SissyHypno. Every consumer that
    /// looked ONLY at InstalledPath therefore no-opped for those two and quietly fell through to
    /// the shared baseline - which, for the AI companion, is the DEFAULT mod's voice. Routing all
    /// of them through one ladder makes that fallback explicit and, crucially, LOGGED.
    ///
    /// Nothing here throws: a probe failure degrades to "not present", exactly as the individual
    /// call sites already did.
    /// </summary>
    public static class ModCompanionContent
    {
        /// <summary>Mirrors <c>ModService.ValidateManifest</c>: at most 20 personalities per mod.</summary>
        public const int MaxPersonalities = 20;

        /// <summary>Mirrors <c>ModService.ValidateManifest</c>: personality name cap.</summary>
        public const int MaxPersonalityNameLength = 100;

        /// <summary>Mirrors <c>ModService.ValidateManifest</c>: prompt-section cap.</summary>
        public const int MaxPromptSettingLength = 5000;

        /// <summary>Probe used against the live filesystem.</summary>
        public static bool Exists(CompanionContentCandidate candidate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(candidate.Path)) return false;
                return candidate.IsDirectory
                    ? Directory.Exists(candidate.Path)
                    : File.Exists(candidate.Path);
            }
            catch { return false; }
        }

        private static string InstallRoot
        {
            get { try { return ContentLocator.InstallRoot; } catch { return string.Empty; } }
        }

        private static string ContentRoot
        {
            get { try { return ContentLocator.ContentRoot; } catch { return string.Empty; } }
        }

        /// <summary>Resolve a channel for an explicit mod. Never throws.</summary>
        public static CompanionContentPick Resolve(
            CompanionChannel channel, string? modId, string? installedPath, string? fileName = null)
        {
            try
            {
                return CompanionContentResolver.Resolve(
                    channel, modId, installedPath, InstallRoot, ContentRoot, Exists, fileName);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ModCompanionContent: resolve failed for {Channel}: {Error}", channel, ex.Message);
                return new CompanionContentPick(CompanionContentSource.None, null);
            }
        }

        /// <summary>Resolve a channel for whatever mod is active right now. Never throws.</summary>
        public static CompanionContentPick ResolveActive(CompanionChannel channel, string? fileName = null)
        {
            string? modId = null, installedPath = null;
            try
            {
                modId = App.Mods?.ActiveModId;
                installedPath = App.Mods?.ActiveMod?.InstalledPath;
            }
            catch { /* service not up yet - fall through with nulls */ }
            return Resolve(channel, modId, installedPath, fileName);
        }

        #region Personalities

        /// <summary>
        /// Strip and cap a personality list read from disk. PURE.
        ///
        /// A <c>personalities.json</c> can arrive from a downloaded content pack or a user mod
        /// folder, and its prompt sections land verbatim in the companion's system prompt - so the
        /// same limits <c>ModService.ValidateManifest</c> enforces on a manifest apply here, and
        /// entries with no id or no name are dropped rather than half-registered.
        /// </summary>
        public static List<ModPersonality> SanitizePersonalities(IEnumerable<ModPersonality?>? defs)
        {
            var result = new List<ModPersonality>();
            if (defs == null) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in defs)
            {
                if (result.Count >= MaxPersonalities) break;
                if (d == null) continue;
                if (string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name)) continue;

                var id = d.Id.Trim();
                if (!seen.Add(id)) continue;

                var name = d.Name.Trim();
                if (name.Length > MaxPersonalityNameLength) name = name[..MaxPersonalityNameLength];

                Dictionary<string, string>? prompts = null;
                if (d.PromptSettings != null)
                {
                    prompts = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var kvp in d.PromptSettings)
                    {
                        if (string.IsNullOrWhiteSpace(kvp.Key) || kvp.Value == null) continue;
                        var v = kvp.Value;
                        if (v.Length > MaxPromptSettingLength) v = v[..MaxPromptSettingLength];
                        prompts[kvp.Key] = v;
                    }
                }

                result.Add(new ModPersonality
                {
                    Id = id,
                    Name = name,
                    Description = d.Description,
                    PromptSettings = prompts
                });
            }
            return result;
        }

        /// <summary>
        /// Parse a <c>personalities.json</c> body. PURE - no I/O, never throws; a garbled file
        /// yields an empty list so the caller falls through to the next rung of the ladder.
        /// </summary>
        public static List<ModPersonality> ParsePersonalities(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<ModPersonality>();
            try
            {
                var defs = JsonConvert.DeserializeObject<List<ModPersonality?>>(json);
                return SanitizePersonalities(defs);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("ModCompanionContent: personalities.json is not valid ({Error})", ex.Message);
                return new List<ModPersonality>();
            }
        }

        /// <summary>Cached personality resolution for one mod.</summary>
        private sealed class PersonalityCacheEntry
        {
            public string Key { get; init; } = string.Empty;
            public CompanionContentSource Source { get; init; }
            public List<ModPersonality>? Personalities { get; init; }
        }

        private static readonly object PersonalityGate = new();
        private static PersonalityCacheEntry? _personalityCache;

        /// <summary>
        /// The AI personalities for a mod, walking the ladder: per-mod <c>personalities.json</c>
        /// (packaged mod, install dir, then downloaded content pack) and finally the in-code
        /// manifest. Returns null when the mod defines none anywhere - the caller then uses the
        /// stock presets, i.e. the default mod's voice.
        ///
        /// Cached on (mod id + installed path + manifest count) so the file is read once per mod,
        /// not once per prompt build.
        /// </summary>
        public static List<ModPersonality>? GetPersonalities(
            string? modId, string? installedPath, List<ModPersonality>? manifestPersonalities,
            out CompanionContentSource source)
        {
            var key = (modId ?? "none") + "|" + (installedPath ?? "") + "|" + (manifestPersonalities?.Count ?? 0);

            lock (PersonalityGate)
            {
                if (_personalityCache != null && _personalityCache.Key == key)
                {
                    source = _personalityCache.Source;
                    return _personalityCache.Personalities;
                }
            }

            List<ModPersonality>? resolved = null;
            var pick = Resolve(CompanionChannel.Personalities, modId, installedPath);
            var fileSource = CompanionContentSource.None;

            if (pick.Found)
            {
                try
                {
                    var parsed = ParsePersonalities(File.ReadAllText(pick.Path!));
                    if (parsed.Count > 0)
                    {
                        resolved = parsed;
                        fileSource = pick.Source;
                    }
                    else
                    {
                        App.Logger?.Warning(
                            "ModCompanionContent: {Path} held no usable personalities; falling back", pick.Path);
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning("ModCompanionContent: failed to read {Path}: {Error}", pick.Path, ex.Message);
                }
            }

            if (resolved == null && manifestPersonalities is { Count: > 0 })
            {
                var sanitized = SanitizePersonalities(manifestPersonalities);
                if (sanitized.Count > 0) resolved = sanitized;
            }

            source = CompanionContentResolver.ResolvePersonalitySource(
                fileSource, manifestPersonalities is { Count: > 0 });

            var entry = new PersonalityCacheEntry { Key = key, Source = source, Personalities = resolved };
            lock (PersonalityGate) { _personalityCache = entry; }
            return resolved;
        }

        /// <summary>Drop the cached personality resolution (mod switch, tests).</summary>
        public static void ResetPersonalityCache()
        {
            lock (PersonalityGate) { _personalityCache = null; }
        }

        #endregion

        /// <summary>
        /// One Information line naming, per channel, where the active mod's companion content
        /// actually came from. This is the line to read when a user reports "she only says the
        /// default lines": <c>personalities=stock-presets</c> means the AI persona is the default
        /// mod's, and <c>barkRules=none</c> means the mod contributed no bark overlay at all.
        /// Never throws.
        /// </summary>
        public static void LogResolvedSources(string? modId, string? installedPath, ModManifest? manifest)
        {
            try
            {
                var barks = Resolve(CompanionChannel.BarkRules, modId, installedPath);
                var voice = Resolve(CompanionChannel.VoiceLines, modId, installedPath);
                var events = Resolve(CompanionChannel.EventAudio, modId, installedPath);
                var mantras = Resolve(CompanionChannel.Mantras, modId, installedPath);
                var avatars = Resolve(CompanionChannel.AvatarManifest, modId, installedPath);

                GetPersonalities(modId, installedPath, manifest?.Personalities, out var personalitySource);

                App.Logger?.Information(
                    "CompanionContent[{ModId}]: personalities={Personalities}, barkRules={BarkRules}, " +
                    "voiceLines={VoiceLines}, eventAudio={EventAudio}, mantras={Mantras}, avatarManifest={AvatarManifest}",
                    modId ?? "none",
                    CompanionContentResolver.Describe(personalitySource),
                    CompanionContentResolver.Describe(barks.Source),
                    CompanionContentResolver.Describe(voice.Source),
                    CompanionContentResolver.Describe(events.Source),
                    CompanionContentResolver.Describe(mantras.Source),
                    CompanionContentResolver.Describe(avatars.Source));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ModCompanionContent: source report failed: {Error}", ex.Message);
            }
        }
    }
}
