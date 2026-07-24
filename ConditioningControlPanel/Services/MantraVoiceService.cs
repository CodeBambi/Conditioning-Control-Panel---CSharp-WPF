using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Loads and serves the per-mod Spoken Mantras dataset for the Takeover "say it for me" mechanic.
    ///
    /// Each active mod ships a <c>mantras.json</c> (see <see cref="MantraSet"/>) next to its bark audio.
    /// This service picks an enabled entry with simple no-repeat rotation, hands back the shared
    /// retry/timeout pools, and resolves voiced-clip paths (and their durations) the same way barks do
    /// (<see cref="BarkService"/>'s tiered lookup). Empty/missing file ⇒ <see cref="HasMantras"/> false,
    /// so the whole feature self-skips and classic Takeover is unaffected.
    /// </summary>
    public sealed class MantraVoiceService
    {
        private readonly Random _random = new();
        private readonly object _lock = new();

        // Cache the parsed set per active mod; reload when the active mod changes.
        private string? _loadedModId;
        private MantraSet? _set;

        // No-repeat rotation: remember recent ids so she doesn't ask the same thing twice in a row.
        private readonly Queue<string> _recent = new();
        private const int RecentMemory = 8;

        /// <summary>True when the active mod has at least one enabled mantra to ask.</summary>
        public bool HasMantras()
        {
            var set = ActiveSet();
            return set != null && set.Mantras.Any(m => m.Enabled && !string.IsNullOrWhiteSpace(m.Phrase));
        }

        /// <summary>
        /// True when the active mod has at least one enabled mantra whose <c>promptAudio</c> actually
        /// resolves to a file on disk. The Mantra Chant loop keys off this so a text-only set leaves
        /// the feature dark rather than looping silence.
        /// </summary>
        public bool HasVoicedMantras()
        {
            var set = ActiveSet();
            if (set == null) return false;
            foreach (var m in set.Mantras)
            {
                if (m.Enabled && !string.IsNullOrWhiteSpace(m.Phrase) && ResolveAudio(m.PromptAudio) != null)
                    return true;
            }
            return false;
        }

        /// <summary>Pick an enabled mantra with no-repeat rotation. Null when none are available.</summary>
        public MantraEntry? NextMantra()
        {
            lock (_lock)
            {
                var set = ActiveSet();
                if (set == null) return null;

                var pool = set.Mantras.Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.Phrase)).ToList();
                if (pool.Count == 0) return null;

                // Prefer entries not in the recent set; fall back to the full pool if all are recent.
                var fresh = pool.Where(m => !_recent.Contains(m.Id)).ToList();
                var choices = fresh.Count > 0 ? fresh : pool;
                var pick = choices[_random.Next(choices.Count)];

                if (!string.IsNullOrEmpty(pick.Id))
                {
                    _recent.Enqueue(pick.Id);
                    while (_recent.Count > Math.Min(RecentMemory, Math.Max(1, pool.Count - 1)))
                        _recent.Dequeue();
                }
                return pick;
            }
        }

        /// <summary>A shared "louder / again" line, or null if the set ships none.</summary>
        public MantraLine? GetRetry() => PickLine(ActiveSet()?.Retry);

        /// <summary>A shared "too shy?" line, or null if the set ships none.</summary>
        public MantraLine? GetTimeout() => PickLine(ActiveSet()?.Timeout);

        private MantraLine? PickLine(List<MantraLine>? lines)
        {
            if (lines == null || lines.Count == 0) return null;
            return lines[_random.Next(lines.Count)];
        }

        // ── Audio resolution (mirrors BarkService.ResolveBarkAudio) ───────────────

        /// <summary>Resolve a clip filename to a full path under the active mod, or null if absent.</summary>
        public string? ResolveAudio(string? file)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;

            // 1) packaged mod (InstalledPath)
            var modPath = App.Mods?.ActiveMod?.InstalledPath;
            if (!string.IsNullOrEmpty(modPath))
            {
                var p = Path.Combine(modPath, "resources", "sounds", "companion_audio", file);
                if (File.Exists(p)) return p;
            }
            // 2) embedded per-mod folder
            var modId = App.Mods?.ActiveModId;
            if (!string.IsNullOrEmpty(modId))
            {
                var pm = Path.Combine(CompanionPhraseService.CompanionAudioFolder, "mods", modId, file);
                if (File.Exists(pm)) return pm;
            }
            // 3) embedded shared fallback
            var embedded = Path.Combine(CompanionPhraseService.CompanionAudioFolder, file);
            return File.Exists(embedded) ? embedded : null;
        }

        /// <summary>
        /// Best-effort duration of an mp3/wav clip so callers can wait for her to finish speaking
        /// before opening the mic (otherwise the recognizer hears her own delivery). Null if unknown.
        /// </summary>
        public TimeSpan? GetAudioDuration(string? fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return null;
            try
            {
                var ext = Path.GetExtension(fullPath).ToLowerInvariant();
                if (ext == ".wav")
                {
                    using var r = new NAudio.Wave.WaveFileReader(fullPath);
                    return r.TotalTime;
                }
                using var mp3 = new NAudio.Wave.Mp3FileReader(fullPath);
                return mp3.TotalTime;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug(ex, "MantraVoiceService: could not read duration of {Path}", fullPath);
                return null;
            }
        }

        // ── Loading ───────────────────────────────────────────────────────────────

        private MantraSet? ActiveSet()
        {
            lock (_lock)
            {
                var modId = App.Mods?.ActiveModId ?? "";
                if (_set != null && _loadedModId == modId) return _set;

                _set = Load(modId);
                _loadedModId = modId;
                _recent.Clear(); // rotation is per-mod
                return _set;
            }
        }

        private static MantraSet? Load(string modId)
        {
            try
            {
                var path = ResolveSetPath(modId);
                if (path == null)
                {
                    App.Logger?.Information("MantraVoiceService: no mantras.json for mod {Mod}", modId);
                    return null;
                }
                var json = File.ReadAllText(path);
                var set = JsonConvert.DeserializeObject<MantraSet>(json);
                App.Logger?.Information("MantraVoiceService: loaded {Count} mantras for mod {Mod} from {Path}",
                    set?.Mantras.Count ?? 0, modId, path);
                return set;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MantraVoiceService: failed to load mantras for mod {Mod}", modId);
                return null;
            }
        }

        private static string? ResolveSetPath(string modId)
        {
            // 1) packaged mod (InstalledPath)
            var modPath = App.Mods?.ActiveMod?.InstalledPath;
            if (!string.IsNullOrEmpty(modPath))
            {
                var p = Path.Combine(modPath, "resources", "sounds", "companion_audio", "mantras.json");
                if (File.Exists(p)) return p;
            }
            // 2) embedded per-mod folder
            if (!string.IsNullOrEmpty(modId))
            {
                var pm = Path.Combine(CompanionPhraseService.CompanionAudioFolder, "mods", modId, "mantras.json");
                if (File.Exists(pm)) return pm;
            }
            return null;
        }
    }
}
