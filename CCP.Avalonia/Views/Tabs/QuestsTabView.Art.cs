using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// Quest art, PORTED from MainWindow.QuestsTab.cs GetQuestArt / ClearQuestArtCache and
    /// MainWindow.xaml.cs GetModeAwareQuestImagePath + LoadQuestImage. The quest pictures ship on
    /// this head as avares://CCP.Avalonia/Resources/quests/ (csproj links Assets/quests), so the
    /// pack:// paths Core persists resolve through <see cref="ModArt.TryLoad"/>: mod override first,
    /// the shipped copy second, the same order as WPF's ModResourceResolver.
    /// </summary>
    public partial class QuestsTabView
    {
        private const string PackResources = "pack://application:,,,/Resources/";

        /// <summary>Decoded art keyed by resolved path; WPF keeps the same cache because
        /// RefreshQuestUI runs on every progress tick while the tab is open.</summary>
        private static readonly Dictionary<string, Bitmap> QuestArtCache = new(StringComparer.OrdinalIgnoreCase);
        private static string? _questArtModId;

        /// <summary>WPF ClearQuestArtCache: a mod switch can resolve the same quest to another file.</summary>
        internal static void ClearQuestArtCache()
        {
            QuestArtCache.Clear();
            _questArtModId = CoreMods.ActiveModId;
        }

        /// <summary>WPF GetQuestArt: mode-aware, cached, null when the quest has no usable image.</summary>
        internal static Bitmap? GetQuestArt(QuestDefinition def)
        {
            try
            {
                // WPF clears the cache on a mod switch (ClearQuestArtCache); the same quest can
                // resolve to a different file under another mod.
                var mod = CoreMods.ActiveModId;
                if (!string.Equals(mod, _questArtModId, StringComparison.OrdinalIgnoreCase))
                {
                    QuestArtCache.Clear();
                    _questArtModId = mod;
                }

                var path = GetModeAwareQuestImagePath(def);
                if (string.IsNullOrEmpty(path)) return null;
                if (QuestArtCache.TryGetValue(path, out var cached)) return cached;
                var loaded = LoadQuestImage(path);
                if (loaded == null) return null;
                QuestArtCache[path] = loaded;
                return loaded;
            }
            catch { return null; }
        }

        /// <summary>WPF GetModeAwareQuestImagePath: the cached remote image first
        /// (EffectiveImagePath), then the Sissy / CCP Default wordmark swaps.</summary>
        private static string GetModeAwareQuestImagePath(QuestDefinition quest)
        {
            var imagePath = quest.EffectiveImagePath;
            if (string.IsNullOrEmpty(imagePath)) return imagePath;
            if (imagePath.StartsWith("pack://", StringComparison.Ordinal))
            {
                if (CoreSettings.Current?.IsSissyMode == true)
                {
                    if (imagePath.Contains("logo.png")) return PackResources + "logo2.png";
                    if (imagePath.Contains("bambi takeover.png")) return PackResources + "features/mandatory_videos.png";
                }
                if (CoreMods.IsCCPDefault && imagePath.Contains("logo.png"))
                    return PackResources + "logo2.png";
            }
            return imagePath;
        }

        /// <summary>WPF LoadQuestImage: pack:// becomes this head's Resources name (ModArt does the
        /// mod override WPF did in GetModeAwareQuestImagePath), a local file is a cached remote
        /// image, anything else is no art.</summary>
        private static Bitmap? LoadQuestImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath)) return null;
            try
            {
                if (imagePath.StartsWith(PackResources, StringComparison.Ordinal))
                    return ModArt.TryLoad(imagePath.Substring(PackResources.Length), decodeWidth: 512);
                if (File.Exists(imagePath))
                    return new Bitmap(imagePath);
            }
            catch (Exception ex) { Log.Debug("[Quests] art {Path} would not load: {E}", imagePath, ex.Message); }
            return null;
        }
    }
}
