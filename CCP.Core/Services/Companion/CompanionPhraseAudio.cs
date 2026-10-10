using System;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// Which recorded clip speaks a companion phrase. PORTED from WPF 7.1.5
    /// <c>AvatarTubeWindow.Speech.cs:2353 GiggleFromCategory / :2383 GetPhraseAudioFile</c> and
    /// <c>Services/Companion/CompanionPhraseService.cs:97 EventAudioFolder / :140 ResolveEventAudio /
    /// :347 GetPhraseId</c>. Ledger row ai#10.
    ///
    /// <para>Order, as WPF: the Phrase Manager's per-phrase override (<c>PhraseAudioOverrides</c>,
    /// keyed by the phrase id <c>Category:index</c>), then the custom phrase's own clip, both under
    /// companion_audio; else the active mod's event_audio clip named by the line's slug. A phrase
    /// with no recorded clip resolves to null and stays text (no synthetic speech, hard rule 5).</para>
    /// </summary>
    public static class CompanionPhraseAudio
    {
        /// <summary>WPF CompanionPhraseService.CompanionAudioFolder (install dir, where the manager copies picks).</summary>
        public static string CompanionAudioFolder =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "companion_audio");

        /// <summary>WPF GetPhraseId: built-in <c>Category:index</c> in the active mod's pool, else the custom phrase id.</summary>
        public static string? PhraseId(string category, string text, string[]? builtIn = null)
        {
            builtIn ??= CoreMods.GetPhrases(category) ?? Array.Empty<string>();
            for (int i = 0; i < builtIn.Length; i++)
                if (builtIn[i] == text) return $"{category}:{i}";
            var custom = CoreSettings.Current.CustomCompanionPhrases?
                .FirstOrDefault(c => c.Text == text && c.Category == category);
            return custom?.Id;
        }

        /// <summary>WPF GetPhraseAudioFile: the override or custom clip's full path when the file exists.</summary>
        public static string? OverrideClip(string phraseId, string? folder = null)
        {
            var s = CoreSettings.Current;
            folder ??= CompanionAudioFolder;
            if (s.PhraseAudioOverrides != null && s.PhraseAudioOverrides.TryGetValue(phraseId, out var file)
                && !string.IsNullOrEmpty(file))
            {
                var p = Path.Combine(folder, file);
                if (File.Exists(p)) return p;
            }
            var custom = s.CustomCompanionPhrases?.FirstOrDefault(c => c.Id == phraseId);
            if (!string.IsNullOrEmpty(custom?.AudioFileName))
            {
                var p = Path.Combine(folder, custom!.AudioFileName!);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>WPF EventAudioFolder: the mod's own resources/sounds/event_audio, else the bundled per-mod folder.</summary>
        public static string? EventAudioFolder
        {
            get
            {
                var modPath = CoreMods.ActiveModPackage?.InstalledPath;
                if (!string.IsNullOrEmpty(modPath))
                {
                    var dir = Path.Combine(modPath, "resources", "sounds", "event_audio");
                    if (Directory.Exists(dir)) return dir;
                }
                var modId = CoreMods.ActiveModId;
                if (!string.IsNullOrEmpty(modId))
                {
                    var rel = Path.Combine("Resources", "sounds", "companion_audio", "mods", modId, "event_audio");
                    if (ContentLocator.DirectoryExists(rel)) return ContentLocator.ResolveDirectory(rel);
                }
                return null;
            }
        }

        /// <summary>WPF ResolveEventAudio: the event clip whose name is the line's slug, or null.</summary>
        public static string? EventClip(string? text, string? folder = null)
        {
            folder ??= EventAudioFolder;
            if (folder == null || string.IsNullOrWhiteSpace(text)) return null;
            var slug = CompanionPhraseIds.Slugify(text);
            if (slug.Length == 0) return null;
            var path = Path.Combine(folder, slug + ".mp3");
            return File.Exists(path) ? path : null;
        }

        /// <summary>The clip for one line of a category: override / custom clip, else the event clip.</summary>
        public static string? ForLine(string category, string text) =>
            (PhraseId(category, text) is { } id ? OverrideClip(id) : null) ?? EventClip(text);
    }
}
