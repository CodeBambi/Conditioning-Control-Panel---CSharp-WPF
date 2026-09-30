using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Serilog;
using ConditioningControlPanel.Services.Content;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The portable rules behind a subliminal's whisper and the Bambi Freeze / Reset pair
    /// (WPF <c>SubliminalService</c> FlashPhrase :237, TriggerBambiFreeze :329, ScheduleBambiReset :410,
    /// FindLinkedAudio :484, PlayWhisperAudio :579; <c>BackRoomVoice</c> neutral clips :212-260).
    /// Each head plays, ducks and draws; which clip, how loud and when is decided here once.
    /// </summary>
    public static class SubliminalWhisper
    {
        /// <summary>Chance a Reset follows a Freeze (WPF: skip when roll &gt; 0.90).</summary>
        public const double ResetChance = 0.90;
        /// <summary>Whisper starts, haptic 50 ms later, the card 250 ms after that.</summary>
        public const int HapticLeadMs = 50, VisualAfterHapticMs = 250;
        /// <summary>Unduck this long after the whisper ends.</summary>
        public const int UnduckDelayMs = 500;

        public static bool ResetFollows(double roll) => roll <= ResetChance;
        /// <summary>Freeze -> Reset gap: 4-8 s.</summary>
        public static int ResetDelayMs(Random r) => r.Next(4000, 8000);
        /// <summary>Video end -> deferred Reset: 1-2 s.</summary>
        public static int DeferredResetDelayMs(Random r) => r.Next(1000, 2000);
        /// <summary>WPF volume curve: (sub x master)^1.5, both 0-100.</summary>
        public static float Volume(int master, int sub) => (float)Math.Pow(sub / 100.0f * (master / 100.0f), 1.5);
        /// <summary>The ambient phrase that pulls a Reset after it.</summary>
        public static bool IsFreezePhrase(string text) => text.Equals("Bambi Freeze", StringComparison.OrdinalIgnoreCase);

        /// <summary>The active mod's own whisper folder, when it ships one.</summary>
        public static string? ModAudioDir(string? installedPath)
        {
            if (installedPath == null) return null;
            var dir = Path.Combine(installedPath, "resources", "sounds", "flashes_audio");
            return Directory.Exists(dir) ? dir : null;
        }

        private static readonly string[] Extensions = { ".mp3", ".wav", ".ogg", ".MP3", ".WAV", ".OGG" };

        /// <summary>
        /// The clip for a phrase, or null: the mod's folder, then the shared Bambi-voiced
        /// <paramref name="subAudioDir"/> (only for mods that may borrow it), else the neutral voice.
        /// <paramref name="listFiles"/> is the case-insensitive fallback's directory listing (WPF caches it).
        /// </summary>
        public static string? FindLinkedAudio(string text, string? modAudioDir, string subAudioDir, string? modId,
            Func<string, bool, string[]?>? listFiles = null)
        {
            listFiles ??= (dir, _) => Directory.Exists(dir) ? Directory.GetFiles(dir) : null;
            var clean = text.Trim();
            if (modAudioDir != null && Search(modAudioDir, clean, dir => listFiles(dir, true)) is { } m) return m;
            if (!ModAudioPolicy.UsesSharedSubAudio(modId)) return FindNeutralClip(text);
            return Search(subAudioDir, clean, dir => listFiles(dir, false));
        }

        private static string? Search(string directory, string clean, Func<string, string[]?> list)
        {
            var variants = new[]
            {
                clean, clean.ToUpper(), clean.ToLower(),
                clean.Replace("\u2019", "'"), clean.Replace("'", "\u2019"),
                clean.ToUpper().Replace("\u2019", "'"),
            };
            foreach (var v in variants)
                foreach (var ext in Extensions)
                {
                    var path = Path.Combine(directory, v + ext);
                    if (File.Exists(path)) return path;
                }
            try
            {
                var want = clean.ToUpperInvariant().Replace("\u2019", "'");
                foreach (var file in list(directory) ?? Array.Empty<string>())
                    if (Path.GetFileNameWithoutExtension(file).ToUpperInvariant().Replace("\u2019", "'") == want)
                        return file;
            }
            catch (Exception ex) { Log.Debug("Error searching audio directory {Dir}: {Error}", directory, ex.Message); }
            return null;
        }

        // ---- the neutral (Circe) voice: Resources/Audio/backroom/words + words.json ----

        public const string WordsFolder = @"Resources\Audio\backroom\words";
        public const string ManifestName = "words.json";
        private static readonly Regex SafeFileName = new("^[a-z0-9][a-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant);
        private static readonly Regex NotWord = new(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant);
        private static readonly object ManifestGate = new();
        private static Dictionary<string, string>? _manifest;
        private static DateTime _manifestAt;

        /// <summary>Lower case, punctuation stripped, runs collapsed: "Let Go!" == "let  go".</summary>
        public static string Normalize(string? text)
            => string.IsNullOrWhiteSpace(text) ? string.Empty
               : NotWord.Replace(text.Trim().ToLowerInvariant(), " ").Trim();

        public static string WordsRoot() => ContentLocator.ResolveDirectory(WordsFolder);

        /// <summary><c>words.json</c> as normalised phrase -&gt; file name, re-read at most once a minute.</summary>
        public static IReadOnlyDictionary<string, string> Manifest()
        {
            lock (ManifestGate)
            {
                if (_manifest != null && DateTime.UtcNow - _manifestAt < TimeSpan.FromMinutes(1)) return _manifest;
                _manifestAt = DateTime.UtcNow;
                _manifest = ReadManifest(Path.Combine(WordsRoot(), ManifestName));
                return _manifest;
            }
        }

        /// <summary>Parse <c>{ "version": 1, "words": { "let go": "let-go.mp3" } }</c>; unsafe names dropped, never throws.</summary>
        public static Dictionary<string, string> ReadManifest(string file)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(file)) return map;
                if (JObject.Parse(File.ReadAllText(file))["words"] is not JObject words) return map;
                foreach (var row in words.Properties())
                {
                    var key = Normalize(row.Name);
                    var name = (row.Value as JValue)?.Value as string;
                    if (key.Length == 0 || string.IsNullOrWhiteSpace(name)) continue;
                    name = name.Trim();
                    if (!SafeFileName.IsMatch(name.ToLowerInvariant()) || name.Contains("..", StringComparison.Ordinal)) continue;
                    map[key] = name;
                }
            }
            catch (Exception ex) { Log.Debug("BackRoomVoice: words.json unreadable ({Type})", ex.GetType().Name); }
            return map;
        }

        /// <summary>The bundled clip for an already-normalised key, or null.</summary>
        public static string? FindPresetClip(string key)
        {
            if (!Manifest().TryGetValue(key, out var name)) return null;
            var path = Path.Combine(WordsRoot(), name);
            return File.Exists(path) ? path : null;
        }

        /// <summary>The neutral clip for a phrase, for mods that may not borrow the Bambi clips.</summary>
        public static string? FindNeutralClip(string? phrase)
        {
            var key = Normalize(phrase);
            return key.Length == 0 ? null : FindPresetClip(key);
        }
    }
}
