using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Manages companion phrases: built-in registry, enable/disable, custom phrases, and audio playback.
    /// </summary>
    public class CompanionPhraseService
    {
        /// <summary>
        /// Folder where custom phrase audio files are stored. Install-dir anchored because it is also
        /// the destination <see cref="CopyAudioToFolder"/> writes user-picked clips to; use
        /// <see cref="ResolveCompanionAudioFile"/> to READ files that may have shipped in a pack.
        /// </summary>
        public static string CompanionAudioFolder =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "companion_audio");

        /// <summary>
        /// Install-dir-relative anchor for <see cref="CompanionAudioFolder"/>, for
        /// <see cref="ContentLocator"/> probes. This tree is SPLIT once audio ships as a content pack:
        /// the manifests (bark_rules.json / mantras.json / avatar_manifest.json) stay in the install
        /// dir while the per-mod .mp3s land under the content root.
        /// </summary>
        public static readonly string CompanionAudioRelativeDir =
            Path.Combine("Resources", "sounds", "companion_audio");

        /// <summary>Install-dir-relative anchor for the baseline voice-line folder.</summary>
        public static readonly string VoiceLineRelativeDir =
            Path.Combine("Resources", "sounds", "flashes_audio");

        /// <summary>
        /// Full path for a file under companion_audio, probing the install dir first and the
        /// downloaded content packs second. Callers keep their own <c>File.Exists</c> check — a miss
        /// in both roots comes back as the install-dir path, so "no audio" degradation is unchanged.
        /// </summary>
        public static string ResolveCompanionAudioFile(params string[] parts) =>
            ContentLocator.Resolve(Path.Combine(CompanionAudioRelativeDir, Path.Combine(parts)));

        /// <summary>
        /// Folder where voice line audio files live (filename = phrase text).
        /// Checks active mod's resources first, falls back to embedded folder.
        /// </summary>
        public static string VoiceLineFolder => ResolveVoiceLineFolder().Absolute;

        /// <summary>
        /// Voice-line folder resolution, handing back BOTH the absolute folder and — when it's one of
        /// ours rather than a packaged mod's own — the install-dir-relative path, so callers that must
        /// scan both roots (bundled install + downloaded pack) can go through
        /// <see cref="ContentLocator.EnumerateFiles"/>.
        /// </summary>
        private static (string Absolute, string? Relative) ResolveVoiceLineFolder()
        {
            var modPath = App.Mods?.ActiveMod?.InstalledPath;
            if (modPath != null)
            {
                var modVoiceDir = Path.Combine(modPath, "resources", "sounds", "flashes_audio");
                if (Directory.Exists(modVoiceDir))
                    return (modVoiceDir, null);
            }
            // Built-in mods (no InstalledPath) may ship their own idle voicelines embedded per-mod,
            // mirroring how bark_rules.json / avatar_manifest.json resolve. Lets Sissy play its own
            // idle "giggle" lines (in the bark voice) instead of the shared old ones.
            var modId = App.Mods?.ActiveModId;
            if (!string.IsNullOrEmpty(modId))
            {
                var rel = Path.Combine(CompanionAudioRelativeDir, "mods", modId, "flashes_audio");
                if (ContentLocator.DirectoryExists(rel))
                    return (ContentLocator.ResolveDirectory(rel), rel);
            }
            return (ContentLocator.ResolveDirectory(VoiceLineRelativeDir), VoiceLineRelativeDir);
        }

        /// <summary>
        /// Per-mod folder for EVENT-COMMENT audio (filename = <see cref="Slugify"/> of the spoken line).
        /// Mirrors <see cref="VoiceLineFolder"/> resolution but for the "event_audio" subfolder, and
        /// returns null when the active mod ships no such folder — so mods without it stay text-only,
        /// exactly as before. Lets currently-silent event comments + autonomy announcements speak in the
        /// mod voice (Sissy ships these), with zero per-mod code.
        /// </summary>
        public static string? EventAudioFolder
        {
            get
            {
                var modPath = App.Mods?.ActiveMod?.InstalledPath;
                if (modPath != null)
                {
                    var dir = Path.Combine(modPath, "resources", "sounds", "event_audio");
                    if (Directory.Exists(dir)) return dir;
                }
                var modId = App.Mods?.ActiveModId;
                if (!string.IsNullOrEmpty(modId))
                {
                    var rel = Path.Combine(CompanionAudioRelativeDir, "mods", modId, "event_audio");
                    if (ContentLocator.DirectoryExists(rel)) return ContentLocator.ResolveDirectory(rel);
                }
                return null;
            }
        }

        /// <summary>
        /// Canonical slug used to match a spoken line to its generated audio file. MUST stay in lockstep
        /// with the Python generator's slugify() (ccp-trailer/sissy_cadence.py): drop {tokens}, lowercase,
        /// collapse every non-alphanumeric run to a single space, trim. e.g. "*giggles* You clicked it~"
        /// -> "giggles you clicked it"; "LEVEL UP! Good girl!~" -> "level up good girl".
        /// </summary>
        public static string Slugify(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var noTokens = System.Text.RegularExpressions.Regex.Replace(text, @"\{[^}]*\}", " ");
            var sb = new System.Text.StringBuilder(noTokens.Length);
            foreach (var ch in noTokens.ToLowerInvariant())
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
                else sb.Append(' ');
            }
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        /// <summary>
        /// Full path to the event-comment audio for a spoken line in the active mod, or null if the mod
        /// ships no matching file. Gated purely on file presence, so it's automatically Sissy-only today.
        /// </summary>
        public static string? ResolveEventAudio(string? text)
        {
            var folder = EventAudioFolder;
            if (folder == null || string.IsNullOrWhiteSpace(text)) return null;
            var slug = Slugify(text);
            if (slug.Length == 0) return null;
            var path = Path.Combine(folder, slug + ".mp3");
            return File.Exists(path) ? path : null;
        }

        public const string VoiceLineCategory = "VoiceLine";

        /// <summary>
        /// Literal text the voiceline generator bakes into FILENAMES wherever a
        /// phrase template had the "{0}" app-name placeholder (see
        /// tools/voicegen/generate_locked_voicelines.py -> filename_clean). The
        /// audio itself was rendered with neutral wording, but the on-screen
        /// bubble uses the filename stem, so it would otherwise read the literal
        /// placeholder. <see cref="ResolveVoiceLinePlaceholder"/> swaps it for the
        /// app currently in focus (or a neutral word when nothing is detected).
        /// </summary>
        public const string VoiceLinePlaceholder = "target application";

        /// <summary>
        /// All registered phrase category names.
        /// </summary>
        private static readonly string[] _categoryNames = new[]
        {
            "Greeting", "StartupGreeting", "Idle", "RandomFloating", "Generic",
            "Gaming", "Browsing", "Shopping", "Social", "Discord",
            "TrainingSite", "HypnoContent", "Working", "Media", "Learning",
            "WindowAwarenessIdle", "EngineStop", "FlashPre", "SubliminalAck",
            "RandomBubble", "BubbleCountMercy", "BubblePop", "GameFailed",
            "BubbleMissed", "FlashClicked", "LevelUp", "MindWipe", "BrainDrain"
        };

        /// <summary>
        /// Gets phrases for a category from the active mod.
        /// </summary>
        private static string[] GetCategoryPhrases(string category)
        {
            return App.Mods?.GetPhrases(category) ?? System.Array.Empty<string>();
        }

        public CompanionPhraseService()
        {
            EnsureAudioFolderExists();
        }

        private void EnsureAudioFolderExists()
        {
            try
            {
                if (!Directory.Exists(CompanionAudioFolder))
                    Directory.CreateDirectory(CompanionAudioFolder);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to create companion audio folder: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Returns all built-in + custom phrases with enabled/audio status resolved.
        /// </summary>
        public List<CompanionPhrase> GetAllPhrases()
        {
            var settings = App.Settings?.Current;
            var disabledIds = settings?.DisabledPhraseIds ?? new HashSet<string>();
            var removedIds = settings?.RemovedPhraseIds ?? new HashSet<string>();
            var audioOverrides = settings?.PhraseAudioOverrides ?? new Dictionary<string, string>();
            var result = new List<CompanionPhrase>();

            // Built-in phrases from mod system
            foreach (var category in _categoryNames)
            {
                var phrases = GetCategoryPhrases(category);
                for (int i = 0; i < phrases.Length; i++)
                {
                    var id = $"{category}:{i}";
                    if (removedIds.Contains(id)) continue;

                    result.Add(new CompanionPhrase
                    {
                        Id = id,
                        Text = phrases[i],
                        Category = category,
                        IsBuiltIn = true,
                        IsEnabled = !disabledIds.Contains(id),
                        AudioFileName = audioOverrides.TryGetValue(id, out var audio) ? audio : null
                    });
                }
            }

            // Voice line phrases (from flashes_audio/ folder - filename is the phrase)
            var voiceLines = GetVoiceLineFiles();
            MigrateLegacyVoiceLineIds(voiceLines);
            foreach (var file in voiceLines)
            {
                var id = VoiceLineId(file);
                if (removedIds.Contains(id)) continue;

                var fileName = Path.GetFileName(file);
                var text = Path.GetFileNameWithoutExtension(file);

                result.Add(new CompanionPhrase
                {
                    Id = id,
                    Text = text,
                    Category = VoiceLineCategory,
                    IsBuiltIn = true,
                    IsEnabled = !disabledIds.Contains(id),
                    AudioFileName = fileName,
                    // The listing can span both roots, so anchor each row on its OWN folder.
                    AudioFolder = Path.GetDirectoryName(file) ?? VoiceLineFolder
                });
            }

            // Custom phrases
            var customPhrases = settings?.CustomCompanionPhrases ?? new List<CustomCompanionPhrase>();
            foreach (var custom in customPhrases)
            {
                result.Add(new CompanionPhrase
                {
                    Id = custom.Id,
                    Text = custom.Text,
                    Category = custom.Category,
                    IsBuiltIn = false,
                    IsEnabled = custom.Enabled,
                    AudioFileName = custom.AudioFileName
                });
            }

            // Reactive "bark" voicelines for the active mod (BarkService). Treated as built-in: the
            // "Bark:" id prefix shares DisabledPhraseIds / RemovedPhraseIds, so toggling/hiding routes
            // through the same path and BarkService.ResolvePool drops disabled lines at speak time.
            var barkLines = App.Bark?.GetAllBarkLines();
            if (barkLines != null)
            {
                foreach (var b in barkLines)
                {
                    if (removedIds.Contains(b.LineId)) continue;
                    result.Add(new CompanionPhrase
                    {
                        Id = b.LineId,
                        Text = b.Text,
                        Category = "Bark",
                        IsBark = true,
                        GroupLabel = "Bark · " + b.Trigger,
                        IsBuiltIn = true,
                        IsEnabled = !disabledIds.Contains(b.LineId),
                        AudioFileName = b.AudioFileName,
                        AudioFolder = b.AudioFolder
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Gets only enabled phrase texts for a specific category (used by AvatarTubeWindow).
        /// </summary>
        public string[] GetEnabledPhrases(string category)
        {
            var settings = App.Settings?.Current;
            var disabledIds = settings?.DisabledPhraseIds ?? new HashSet<string>();
            var removedIds = settings?.RemovedPhraseIds ?? new HashSet<string>();

            var result = new List<string>();

            // Built-in phrases for this category from mod system
            if (_categoryNames.Contains(category))
            {
                var phrases = GetCategoryPhrases(category);
                for (int i = 0; i < phrases.Length; i++)
                {
                    var id = $"{category}:{i}";
                    if (!removedIds.Contains(id) && !disabledIds.Contains(id))
                        result.Add(phrases[i]);
                }
            }

            // Custom phrases in this category
            var customPhrases = settings?.CustomCompanionPhrases ?? new List<CustomCompanionPhrase>();
            foreach (var custom in customPhrases)
            {
                if (custom.Category == category && custom.Enabled)
                    result.Add(custom.Text);
            }

            return result.ToArray();
        }

        /// <summary>
        /// Quick check if a phrase ID is enabled.
        /// </summary>
        public bool IsPhraseEnabled(string id)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return true;
            return !settings.DisabledPhraseIds.Contains(id) && !settings.RemovedPhraseIds.Contains(id);
        }

        /// <summary>
        /// Gets the phrase ID for a given category and text (resolves index at runtime).
        /// </summary>
        public string? GetPhraseId(string category, string text)
        {
            // Check built-in first
            if (_categoryNames.Contains(category))
            {
                var phrases = GetCategoryPhrases(category);
                for (int i = 0; i < phrases.Length; i++)
                {
                    if (phrases[i] == text)
                        return $"{category}:{i}";
                }
            }

            // Check custom
            var customPhrases = App.Settings?.Current?.CustomCompanionPhrases;
            if (customPhrases != null)
            {
                var match = customPhrases.FirstOrDefault(c => c.Text == text && c.Category == category);
                if (match != null) return match.Id;
            }

            return null;
        }

        /// <summary>
        /// Attempts to play phrase audio. Returns true if audio was played, false otherwise.
        /// </summary>
        public bool TryPlayPhraseAudio(string phraseId)
        {
            var audioFile = GetAudioFileName(phraseId);
            if (string.IsNullOrEmpty(audioFile)) return false;

            var audioPath = Path.Combine(CompanionAudioFolder, audioFile);
            if (!File.Exists(audioPath)) return false;

            PlayAudioFile(audioPath);
            return true;
        }

        /// <summary>
        /// Gets the audio filename for a phrase (from overrides for built-in, from custom phrase for custom).
        /// </summary>
        private string? GetAudioFileName(string phraseId)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return null;

            // Check audio overrides (for built-in phrases)
            if (settings.PhraseAudioOverrides.TryGetValue(phraseId, out var overrideFile))
                return overrideFile;

            // Check custom phrases
            var custom = settings.CustomCompanionPhrases.FirstOrDefault(c => c.Id == phraseId);
            return custom?.AudioFileName;
        }

        /// <summary>
        /// Play an audio file using NAudio (same pattern as PlayGiggleSound in AvatarTubeWindow).
        /// </summary>
        private void PlayAudioFile(string path)
        {
            try
            {
                var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                var volume = (float)Math.Pow(masterVolume, 1.5) * 0.7f;
                App.Audio?.PlayOneShot(path, volume, "phrase-audio");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play phrase audio: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Copies an audio file to the companion_audio folder with a sanitized name.
        /// Returns the new filename.
        /// </summary>
        public string? CopyAudioToFolder(string sourcePath, string phraseText)
        {
            try
            {
                EnsureAudioFolderExists();
                var ext = Path.GetExtension(sourcePath);
                var sanitized = SanitizeFileName(phraseText);
                var fileName = $"{sanitized}{ext}";
                var destPath = Path.Combine(CompanionAudioFolder, fileName);

                // Handle duplicate names
                int counter = 1;
                while (File.Exists(destPath))
                {
                    fileName = $"{sanitized}_{counter}{ext}";
                    destPath = Path.Combine(CompanionAudioFolder, fileName);
                    counter++;
                }

                File.Copy(sourcePath, destPath);
                return fileName;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Failed to copy audio file: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Sanitize a string for use as a filename.
        /// </summary>
        private static string SanitizeFileName(string text)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(text.Where(c => !invalid.Contains(c)).ToArray());
            sanitized = sanitized.Replace(' ', '_');
            if (sanitized.Length > 50) sanitized = sanitized[..50];
            if (string.IsNullOrWhiteSpace(sanitized)) sanitized = "phrase";
            return sanitized;
        }

        /// <summary>
        /// Get the total number of enabled (active) phrases.
        /// </summary>
        public int GetActivePhraseCount()
        {
            return GetAllPhrases().Count(p => p.IsEnabled);
        }

        /// <summary>
        /// Get all registered category names (for display in the editor), including VoiceLine.
        /// </summary>
        public static IReadOnlyList<string> GetCategoryNames()
        {
            var names = _categoryNames.ToList();
            names.Add(VoiceLineCategory);
            return names;
        }

        /// <summary>
        /// Gets sorted list of voice line file paths from the flashes_audio/ folder. When the folder
        /// is one of ours (not a packaged mod's) this is the UNION of the bundled install dir and the
        /// downloaded content pack, deduped by filename with the install dir winning.
        /// </summary>
        private static List<string> GetVoiceLineFiles()
        {
            try
            {
                var (absolute, relative) = ResolveVoiceLineFolder();
                var extensions = new[] { "*.mp3", "*.wav", "*.ogg", "*.flac" };
                var files = new List<string>();
                foreach (var ext in extensions)
                {
                    if (relative != null)
                        files.AddRange(ContentLocator.EnumerateFiles(relative, ext));
                    else if (Directory.Exists(absolute))
                        files.AddRange(Directory.GetFiles(absolute, ext));
                }

                // Sort by FILENAME (not full path) so a union across the two roots still reads as one
                // alphabetical list — within a single folder this is identical to the old path sort.
                files.Sort((a, b) => string.Compare(
                    Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
                return files;
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Stable id for a built-in voice line, keyed on the audio FILENAME stem rather than the
        /// line's index in the sorted folder listing. Voice-line audio can now arrive AFTER first run
        /// (content packs download later), and an index-based id silently re-targets every toggle the
        /// user already made the moment the list grows. Mirrors <see cref="BarkService.BarkLineId"/>.
        /// </summary>
        public static string VoiceLineId(string filePath) =>
            VoiceLineCategory + ":" + Path.GetFileNameWithoutExtension(filePath);

        /// <summary>True for the pre-migration positional form, e.g. <c>VoiceLine:12</c>.</summary>
        private static bool IsLegacyVoiceLineId(string id)
        {
            if (string.IsNullOrEmpty(id) ||
                !id.StartsWith(VoiceLineCategory + ":", StringComparison.Ordinal)) return false;
            var tail = id.Substring(VoiceLineCategory.Length + 1);
            return tail.Length > 0 && tail.All(char.IsDigit);
        }

        /// <summary>
        /// One-time (but idempotent, and safe to re-run) migration of positional
        /// <c>VoiceLine:{index}</c> entries in DisabledPhraseIds/RemovedPhraseIds to the
        /// filename-based form, mapping each index through the CURRENT sorted listing.
        ///
        /// Best effort by design: with no voice lines on disk yet (audio not downloaded) the legacy
        /// entries are LEFT UNTOUCHED so a later launch that does have the audio can still migrate
        /// them. An index past the end of the current list is dropped — it can only refer to a line
        /// that no longer exists.
        /// </summary>
        private static void MigrateLegacyVoiceLineIds(List<string> sortedFiles)
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null || sortedFiles.Count == 0) return;

                // A file literally named "12.mp3" would produce an id that LOOKS positional; never
                // rewrite an id that already matches a real file.
                HashSet<string>? currentIds = null;

                var changed = false;
                foreach (var set in new[] { settings.DisabledPhraseIds, settings.RemovedPhraseIds })
                {
                    // Cheap scan first — the migrated (and every fresh) profile allocates nothing here.
                    if (set == null || set.Count == 0 || !set.Any(IsLegacyVoiceLineId)) continue;
                    var legacy = set.Where(IsLegacyVoiceLineId).ToList();

                    currentIds ??= new HashSet<string>(
                        sortedFiles.Select(VoiceLineId), StringComparer.OrdinalIgnoreCase);

                    foreach (var old in legacy)
                    {
                        if (currentIds.Contains(old)) continue;
                        set.Remove(old);
                        changed = true;

                        if (!int.TryParse(old.Substring(VoiceLineCategory.Length + 1), out var idx)) continue;
                        if (idx < 0 || idx >= sortedFiles.Count) continue;
                        set.Add(VoiceLineId(sortedFiles[idx]));
                    }
                }

                if (changed)
                {
                    App.Settings?.Save();
                    App.Logger?.Information(
                        "CompanionPhraseService: migrated positional VoiceLine phrase ids to filename-based ids");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "CompanionPhraseService: failed to migrate legacy VoiceLine phrase ids");
            }
        }

        /// <summary>
        /// Gets enabled voice line file paths (for AvatarTubeWindow to filter).
        /// </summary>
        public List<string> GetEnabledVoiceLineFiles()
        {
            var settings = App.Settings?.Current;
            var disabledIds = settings?.DisabledPhraseIds ?? new HashSet<string>();
            var removedIds = settings?.RemovedPhraseIds ?? new HashSet<string>();

            var allFiles = GetVoiceLineFiles();
            MigrateLegacyVoiceLineIds(allFiles);
            var result = new List<string>();

            foreach (var file in allFiles)
            {
                var id = VoiceLineId(file);
                if (!removedIds.Contains(id) && !disabledIds.Contains(id))
                    result.Add(file);
            }

            // Include custom phrases in VoiceLine category that have audio
            var customPhrases = settings?.CustomCompanionPhrases ?? new List<CustomCompanionPhrase>();
            foreach (var custom in customPhrases)
            {
                if (custom.Category == VoiceLineCategory && custom.Enabled && !string.IsNullOrEmpty(custom.AudioFileName))
                {
                    var fullPath = Path.Combine(CompanionAudioFolder, custom.AudioFileName);
                    if (File.Exists(fullPath))
                        result.Add(fullPath);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns the custom phrase text for a voice line audio path, or null if it's a built-in voice line.
        /// </summary>
        public string? GetVoiceLineDisplayText(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            var customPhrases = App.Settings?.Current?.CustomCompanionPhrases;
            if (customPhrases == null) return null;

            var match = customPhrases.FirstOrDefault(c =>
                c.Category == VoiceLineCategory && string.Equals(c.AudioFileName, fileName, StringComparison.OrdinalIgnoreCase));
            return match?.Text;
        }

        /// <summary>
        /// Replaces the baked-in <see cref="VoiceLinePlaceholder"/> in a voiceline's
        /// display text with the app currently in focus, so an activity line like
        /// "Watching target application, pet?" renders as "Watching YouTube, pet?".
        /// Mirrors the activity-reaction path's name choice (service name first,
        /// then detected name). Falls back to a neutral "that" when nothing is in
        /// focus (e.g. window awareness disabled). Text without the placeholder is
        /// returned unchanged, so ordinary voicelines are untouched.
        /// </summary>
        public static string ResolveVoiceLinePlaceholder(string? text)
        {
            if (string.IsNullOrEmpty(text) ||
                text.IndexOf(VoiceLinePlaceholder, StringComparison.OrdinalIgnoreCase) < 0)
                return text ?? string.Empty;

            var wa = App.WindowAwareness;
            var name = wa == null
                ? string.Empty
                : (!string.IsNullOrWhiteSpace(wa.CurrentServiceName)
                    ? wa.CurrentServiceName
                    : wa.CurrentDetectedName);

            var usedFallback = string.IsNullOrWhiteSpace(name);
            var replacement = usedFallback ? "that" : name.Trim();

            var result = text.Replace(VoiceLinePlaceholder, replacement);

            // Filenames carry the placeholder lowercase, so a sentence-initial swap
            // to the neutral "that" needs capitalizing. (A detected app name is
            // already a proper noun, so leave that case alone.)
            if (usedFallback && result.Length > 0 && char.IsLower(result[0]))
                result = char.ToUpper(result[0]) + result.Substring(1);

            return result;
        }
    }
}
