// PORTED from ConditioningControlPanel/Services/Chaos/ChaosSfx.cs (7.1.5).
using System;
using System.IO;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// One-shot sound effects for Chaos (wave clear, boon reveal, the descent's and the race's
    /// <c>sfx</c> frames). Each cue is an override-then-fallback list under <c>Resources/sounds/</c>;
    /// the active mod's own <c>resources/sounds/</c> file wins (WPF ModResourceResolver.ResolveAudioPath).
    /// A missing file is silence, and nothing here is louder than master volume times the cue's scale.
    /// </summary>
    internal static class ChaosSfx
    {
        /// <summary>Test seam: (path, volume) instead of the audio backend.</summary>
        internal static Action<string, float>? PlayOverride;

        public static void PlayWaveClear() => PlayFirstAvailable(new[] { "chaos/wave_clear.mp3", "lvup.mp3" }, 0.8f);

        public static void PlayBoonReveal(bool isRare) => PlayFirstAvailable(
            isRare ? new[] { "chaos/dling.mp3", "chime1.mp3" }
                   : new[] { "chaos/thud.mp3", "bubbles/Pop2.mp3" },
            isRare ? 0.6f : 0.65f);

        public static void PlayBoonPicked() => PlayFirstAvailable(new[] { "chaos/boon_pick.mp3", "chime2.mp3" }, 0.7f);

        public static void PlayTickTock() => PlayFirstAvailable(new[] { "chaos/ticktock.mp3" }, 0.45f);

        public static void PlayRippleCast() => PlayFirstAvailable(new[] { "chaos/ripple_cast.mp3", "chaos/snap.mp3" }, 0.6f);

        public static void Play(string name, float scale = 0.6f) => PlayFirstAvailable(new[] { $"chaos/{name}.mp3" }, scale);

        /// <summary>A page <c>sfx {name, scale}</c> frame (WPF DtrhHostService.cs:271 / CaucusHostService):
        /// two cues go through their fallback lists, the rest by name. The scale is clamped to 0..1 so a
        /// page can never ask for more than the player's own volume.</summary>
        public static void PlayFrame(string? name, float? scale)
        {
            float s = Math.Clamp(scale ?? 0.6f, 0f, 1f);
            if (name == "wave_clear") PlayWaveClear();
            else if (name == "ripple_cast") PlayRippleCast();
            else if (!string.IsNullOrEmpty(name)) Play(name, s);
        }

        /// <summary>The file a cue plays, or "" when the asset is absent. Never leaves the sounds folder.</summary>
        public static string ResolvePath(string rel)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rel) || rel.Contains("..") || Path.IsPathRooted(rel)
                    || rel.IndexOfAny(new[] { ':', '\0' }) >= 0) return "";
                var mod = CoreModArt.ModAudioFile(rel, CoreMods.ActiveModPackage?.InstalledPath);
                if (!string.IsNullOrEmpty(mod) && File.Exists(mod)) return mod;
                var path = ContentLocator.Resolve(Path.Combine("Resources", "sounds", rel.Replace('/', Path.DirectorySeparatorChar)));
                return !string.IsNullOrEmpty(path) && File.Exists(path) ? path : "";
            }
            catch { return ""; }
        }

        private static void PlayFirstAvailable(string[] candidates, float scale)
        {
            try
            {
                foreach (var rel in candidates)
                {
                    var path = ResolvePath(rel);
                    if (path.Length == 0) continue;
                    var volume = Volume(scale);
                    if (PlayOverride != null) PlayOverride(path, volume);
                    else CoreAudio.PlayOneShot(path, volume, "chaos-sfx");
                    return;
                }
            }
            catch (Exception ex) { Log.Debug("ChaosSfx resolve failed: {E}", ex.Message); }
        }

        private static float Volume(float scale)
        {
            try { return Math.Clamp(CoreSettings.Current.MasterVolume / 100f * scale, 0f, 1f); }
            catch { return Math.Clamp(scale, 0f, 1f); }
        }
    }
}
