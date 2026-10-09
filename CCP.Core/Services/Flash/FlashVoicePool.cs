using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel.Services.Flash
{
    /// <summary>
    /// The clip a flash speaks: WPF FlashService.GetNextSound / BuildVoiceLinePool /
    /// IsCompanionVoiceSilenced / PlaySound's volume curve (Services/Flash/FlashService.cs:4556,
    /// :4838, :4841). Recorded clips only: a pool with nothing in it stays silent, never synthetic.
    ///
    /// <para>The folder is the active mod's voice-line channel (<see cref="ModCompanionContent"/>),
    /// which already applies <see cref="CompanionContentResolver.OwnsBaselineVoiceLines"/>: only the
    /// mods whose voice the shared library is may speak with it (ModAudioPolicy). Both content roots
    /// of the winning rung are merged, as WPF's ContentLocator.EnumerateFiles does, and the phrase
    /// library's Removed/Disabled ids filter every file. Custom VoiceLine phrases with audio join.</para>
    /// </summary>
    internal static class FlashVoicePool
    {
        internal static readonly string[] Extensions = { ".mp3", ".wav", ".ogg" };

        private static readonly object Lock = new();
        private static readonly Random Rng = new();
        private static Queue<string> _queue = new();

        /// <summary>Pool builder (tests swap it).</summary>
        internal static Func<AppSettings?, List<string>> Build = BuildPool;

        /// <summary>The next clip of a shuffled cycle, or null when the pool is empty.</summary>
        internal static string? Next(AppSettings? s)
        {
            lock (Lock)
            {
                if (_queue.Count == 0)
                {
                    var files = Build(s);
                    if (files.Count == 0) return null;
                    _queue = new Queue<string>(files.OrderBy(_ => Rng.Next()));
                }
                return _queue.Count > 0 ? _queue.Dequeue() : null;
            }
        }

        /// <summary>WPF ClearFileCache: the next flash rebuilds the pool (mod switch, asset change).</summary>
        internal static void Reset()
        {
            lock (Lock) _queue = new Queue<string>();
        }

        /// <summary>WPF IsCompanionVoiceSilenced: master mute, "mute avatar", voice lines muted.
        /// AvatarEnabled is deliberately not among them.</summary>
        internal static bool Silenced(AppSettings s) =>
            s.MasterVolume <= 0 || s.AvatarMuted || s.CompanionVoiceLinesMuted;

        /// <summary>True when a flash event plays a clip (WPF ShowImagesAsync's gate).</summary>
        internal static bool ShouldPlay(AppSettings? s) => s != null && s.FlashAudioEnabled && !Silenced(s);

        /// <summary>WPF PlaySound's curve: gentler, 5% floor on the curve (never on mute), x0.85.</summary>
        internal static float Volume(int masterPercent)
        {
            var v = masterPercent / 100.0f;
            return v <= 0f ? 0f : Math.Max(0.05f, (float)Math.Pow(v, 1.5)) * 0.85f;
        }

        /// <summary>WPF: lifetime = sound seconds (or FlashDuration) x 1000 + 1000.</summary>
        internal static int LifetimeMs(double seconds) => (int)(seconds * 1000) + 1000;

        /// <summary>WPF ScheduleUnduck delay: the clip plus 1.5 s.</summary>
        internal static int UnduckDelayMs(double seconds) => (int)(seconds * 1000) + 1500;

        /// <summary>WPF FlashAudioEventArgs.Text: the file name without extension is the caption.</summary>
        internal static string Caption(string path) => Path.GetFileNameWithoutExtension(path) ?? string.Empty;

        private static List<string> BuildPool(AppSettings? s)
        {
            var pool = new List<string>();
            var removed = s?.RemovedPhraseIds ?? new HashSet<string>();
            var disabled = s?.DisabledPhraseIds ?? new HashSet<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in VoiceDirs())
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList(); }
                catch { continue; }
                foreach (var f in files)
                {
                    if (!Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())) continue;
                    if (!seen.Add(Path.GetFileName(f))) continue;   // install + pack copies of one line
                    var id = CompanionPhraseIds.VoiceLineId(f);
                    if (removed.Contains(id) || disabled.Contains(id)) continue;
                    pool.Add(f);
                }
            }

            // WPF GetEnabledVoiceLineFiles: custom VoiceLine phrases that carry a recorded clip.
            var customDir = Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "companion_audio");
            foreach (var c in s?.CustomCompanionPhrases ?? new List<CustomCompanionPhrase>())
            {
                if (c.Category != CompanionPhraseIds.VoiceLineCategory || !c.Enabled || string.IsNullOrEmpty(c.AudioFileName)) continue;
                var full = Path.Combine(customDir, c.AudioFileName);
                if (File.Exists(full)) pool.Add(full);
            }
            return pool;
        }

        /// <summary>The existing folders of the winning voice-line rung: a packaged mod's own folder
        /// alone, else every existing candidate of the same source (install dir + content pack).</summary>
        private static IEnumerable<string> VoiceDirs()
        {
            string? modId = null, installedPath = null;
            try
            {
                modId = CoreMods.ActiveModIdProvider?.Invoke();
                installedPath = CoreMods.ActiveModPackageProvider?.Invoke()?.InstalledPath;
            }
            catch { }
            string install = "", content = "";
            try { install = ContentLocator.InstallRoot; } catch { }
            try { content = ContentLocator.ContentRoot; } catch { }
            var candidates = CompanionContentResolver.Candidates(CompanionChannel.VoiceLines, modId, installedPath, install, content);
            var hit = candidates.FirstOrDefault(c => SafeDirExists(c.Path));
            if (hit.Path == null) return Array.Empty<string>();
            return candidates.Where(c => c.Source == hit.Source || (IsPerMod(c.Source) && IsPerMod(hit.Source)))
                .Select(c => c.Path).Where(SafeDirExists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool IsPerMod(CompanionContentSource s) =>
            s is CompanionContentSource.ModInstallDir or CompanionContentSource.ModContentPack;

        private static bool SafeDirExists(string? p)
        {
            try { return !string.IsNullOrEmpty(p) && Directory.Exists(p); } catch { return false; }
        }
    }
}
