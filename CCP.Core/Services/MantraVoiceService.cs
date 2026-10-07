using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Serilog;
using static ConditioningControlPanel.Services.Companion.CompanionContentResolver;

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
            var modPath = CoreMods.ActiveModPackage?.InstalledPath;
            if (!string.IsNullOrEmpty(modPath))
            {
                var p = Path.Combine(modPath, "resources", "sounds", "companion_audio", file);
                if (File.Exists(p)) return p;
            }
            // 2) embedded per-mod folder (install dir, or the downloaded content pack)
            var modId = CoreMods.ActiveModIdProvider?.Invoke();
            if (!string.IsNullOrEmpty(modId))
            {
                var pm = ResolveCompanionAudioFile("mods", modId, file);
                if (File.Exists(pm)) return pm;
            }
            // 3) embedded shared fallback
            var embedded = ResolveCompanionAudioFile(file);
            return File.Exists(embedded) ? embedded : null;
        }

        // GetAudioDuration (NAudio) stays in the WPF head: MantraVoiceAudio.cs.

        // ── Loading ───────────────────────────────────────────────────────────────

        private MantraSet? ActiveSet()
        {
            lock (_lock)
            {
                var modId = CoreMods.ActiveModIdProvider?.Invoke() ?? "";
                if (_set != null && _loadedModId == modId) return _set;

                _set = Load(modId);
                _loadedModId = modId;
                _recent.Clear(); // rotation is per-mod
                return _set;
            }
        }

        private static string ResolveCompanionAudioFile(params string[] parts) =>
            ContentLocator.Resolve(Path.Combine(CompanionAudioRelativeDir, Path.Combine(parts)));

        private static MantraSet? Load(string modId)
        {
            try
            {
                var path = ResolveSetPath(modId);
                if (path == null)
                {
                    Log.Information("MantraVoiceService: no mantras.json for mod {Mod}", modId);
                    return null;
                }
                var json = File.ReadAllText(path);
                var set = JsonConvert.DeserializeObject<MantraSet>(json);
                Log.Information("MantraVoiceService: loaded {Count} mantras for mod {Mod} from {Path}",
                    set?.Mantras.Count ?? 0, modId, path);
                return set;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MantraVoiceService: failed to load mantras for mod {Mod}", modId);
                return null;
            }
        }

        private static string? ResolveSetPath(string modId)
        {
            // 1) packaged mod (InstalledPath)
            var modPath = CoreMods.ActiveModPackage?.InstalledPath;
            if (!string.IsNullOrEmpty(modPath))
            {
                var p = Path.Combine(modPath, "resources", "sounds", "companion_audio", "mantras.json");
                if (File.Exists(p)) return p;
            }
            // 2) embedded per-mod folder (manifest stays bundled, but probe the content root too so a
            //    pack-only mod folder still resolves)
            if (!string.IsNullOrEmpty(modId))
            {
                var pm = ResolveCompanionAudioFile("mods", modId, "mantras.json");
                if (File.Exists(pm)) return pm;
            }
            return null;
        }
    }
}
