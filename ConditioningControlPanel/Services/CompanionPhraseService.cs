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
        /// Folder where custom phrase audio files are stored.
        /// </summary>
        public static string CompanionAudioFolder =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "companion_audio");

        /// <summary>
        /// Folder where voice line audio files live (filename = phrase text).
        /// </summary>
        public static string VoiceLineFolder =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "flashes_audio");

        public const string VoiceLineCategory = "VoiceLine";

        /// <summary>
        /// Registry of all built-in phrase categories mapped to their ContentModeConfig getter.
        /// </summary>
        private static readonly Dictionary<string, Func<ContentMode, string[]>> _categoryRegistry = new()
        {
            { "Greeting", ContentModeConfig.GetGreetingPhrases },
            { "StartupGreeting", ContentModeConfig.GetStartupGreetingPhrases },
            { "Idle", ContentModeConfig.GetIdlePhrases },
            { "RandomFloating", ContentModeConfig.GetRandomFloatingPhrases },
            { "Generic", ContentModeConfig.GetGenericPhrases },
            { "Gaming", ContentModeConfig.GetGamingPhrases },
            { "Browsing", ContentModeConfig.GetBrowsingPhrases },
            { "Shopping", ContentModeConfig.GetShoppingPhrases },
            { "Social", ContentModeConfig.GetSocialPhrases },
            { "Discord", ContentModeConfig.GetDiscordPhrases },
            { "TrainingSite", ContentModeConfig.GetTrainingSitePhrases },
            { "HypnoContent", ContentModeConfig.GetHypnoContentPhrases },
            { "Working", ContentModeConfig.GetWorkingPhrases },
            { "Media", ContentModeConfig.GetMediaPhrases },
            { "Learning", ContentModeConfig.GetLearningPhrases },
            { "WindowAwarenessIdle", ContentModeConfig.GetWindowAwarenessIdlePhrases },
            { "EngineStop", ContentModeConfig.GetEngineStopPhrases },
            { "FlashPre", ContentModeConfig.GetFlashPrePhrases },
            { "SubliminalAck", ContentModeConfig.GetSubliminalAckPhrases },
            { "RandomBubble", ContentModeConfig.GetRandomBubblePhrases },
            { "BubbleCountMercy", ContentModeConfig.GetBubbleCountMercyPhrases },
            { "BubblePop", ContentModeConfig.GetBubblePopPhrases },
            { "GameFailed", ContentModeConfig.GetGameFailedPhrases },
            { "BubbleMissed", ContentModeConfig.GetBubbleMissedPhrases },
            { "FlashClicked", ContentModeConfig.GetFlashClickedPhrases },
            { "LevelUp", ContentModeConfig.GetLevelUpPhrases },
            { "MindWipe", ContentModeConfig.GetMindWipePhrases },
            { "BrainDrain", ContentModeConfig.GetBrainDrainPhrases },
        };

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
        public List<CompanionPhrase> GetAllPhrases(ContentMode mode)
        {
            var settings = App.Settings?.Current;
            var disabledIds = settings?.DisabledPhraseIds ?? new HashSet<string>();
            var removedIds = settings?.RemovedPhraseIds ?? new HashSet<string>();
            var audioOverrides = settings?.PhraseAudioOverrides ?? new Dictionary<string, string>();
            var result = new List<CompanionPhrase>();

            // Built-in phrases from registry
            foreach (var (category, getter) in _categoryRegistry)
            {
                var phrases = getter(mode);
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
            for (int i = 0; i < voiceLines.Count; i++)
            {
                var id = $"{VoiceLineCategory}:{i}";
                if (removedIds.Contains(id)) continue;

                var fileName = Path.GetFileName(voiceLines[i]);
                var text = Path.GetFileNameWithoutExtension(voiceLines[i]);

                result.Add(new CompanionPhrase
                {
                    Id = id,
                    Text = text,
                    Category = VoiceLineCategory,
                    IsBuiltIn = true,
                    IsEnabled = !disabledIds.Contains(id),
                    AudioFileName = fileName,
                    AudioFolder = VoiceLineFolder
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

            return result;
        }

        /// <summary>
        /// Gets only enabled phrase texts for a specific category (used by AvatarTubeWindow).
        /// </summary>
        public string[] GetEnabledPhrases(string category, ContentMode mode)
        {
            var settings = App.Settings?.Current;
            var disabledIds = settings?.DisabledPhraseIds ?? new HashSet<string>();
            var removedIds = settings?.RemovedPhraseIds ?? new HashSet<string>();

            var result = new List<string>();

            // Built-in phrases for this category
            if (_categoryRegistry.TryGetValue(category, out var getter))
            {
                var phrases = getter(mode);
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
        public string? GetPhraseId(string category, string text, ContentMode mode)
        {
            // Check built-in first
            if (_categoryRegistry.TryGetValue(category, out var getter))
            {
                var phrases = getter(mode);
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
            Task.Run(() =>
            {
                try
                {
                    var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                    var volume = (float)Math.Pow(masterVolume, 1.5) * 0.7f;

                    using var audioFile = new NAudio.Wave.AudioFileReader(path);
                    audioFile.Volume = volume;
                    using var outputDevice = new NAudio.Wave.WaveOutEvent();
                    outputDevice.Init(audioFile);
                    outputDevice.Play();
                    while (outputDevice.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                    {
                        System.Threading.Thread.Sleep(50);
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to play phrase audio: {Error}", ex.Message);
                }
            });
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
        public int GetActivePhraseCount(ContentMode mode)
        {
            return GetAllPhrases(mode).Count(p => p.IsEnabled);
        }

        /// <summary>
        /// Get all registered category names (for display in the editor), including VoiceLine.
        /// </summary>
        public static IReadOnlyList<string> GetCategoryNames()
        {
            var names = _categoryRegistry.Keys.ToList();
            names.Add(VoiceLineCategory);
            return names;
        }

        /// <summary>
        /// Gets sorted list of voice line file paths from the flashes_audio/ folder.
        /// </summary>
        private static List<string> GetVoiceLineFiles()
        {
            try
            {
                if (!Directory.Exists(VoiceLineFolder))
                    return new List<string>();

                var extensions = new[] { "*.mp3", "*.wav", "*.ogg", "*.flac" };
                var files = new List<string>();
                foreach (var ext in extensions)
                    files.AddRange(Directory.GetFiles(VoiceLineFolder, ext));

                files.Sort(StringComparer.OrdinalIgnoreCase);
                return files;
            }
            catch
            {
                return new List<string>();
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
            var result = new List<string>();

            for (int i = 0; i < allFiles.Count; i++)
            {
                var id = $"{VoiceLineCategory}:{i}";
                if (!removedIds.Contains(id) && !disabledIds.Contains(id))
                    result.Add(allFiles[i]);
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
    }
}
