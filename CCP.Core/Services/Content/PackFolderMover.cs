using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// WPF BtnPickAssetsFolder_Click's pack migration (MainWindow.UiUpdates.cs ~2164-2318), portable:
    /// find the installed packs under the OLD assets folder and the default one (packs stranded there
    /// by an earlier move), then copy + delete each <c>.packs/&lt;guid&gt;</c> into the new folder
    /// (copy, not Directory.Move, so it crosses volumes) and register it in settings
    /// (InstalledPackIds, PackGuidMap overwritten, auto-activated). The head asks first.
    /// </summary>
    public static class PackFolderMover
    {
        public sealed record Candidate(string SourceFolder, string PackName, long Bytes);

        /// <summary>WPF: the packs found in <paramref name="oldAssets"/>/.packs and
        /// <paramref name="defaultAssets"/>/.packs, never the destination's own.</summary>
        public static List<Candidate> Find(string oldAssets, string defaultAssets, string newAssets,
            Func<string, string>? loadManifest = null)
        {
            loadManifest ??= PackEncryptionService.LoadEncryptedManifest;
            var result = new List<Candidate>();
            var dest = Full(Path.Combine(newAssets, ".packs"));
            var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Full(Path.Combine(oldAssets, ".packs")),
                Full(Path.Combine(defaultAssets, ".packs")),
            };
            locations.Remove(dest);
            foreach (var packs in locations)
            {
                if (!Directory.Exists(packs)) continue;
                foreach (var dir in Directory.GetDirectories(packs))
                {
                    var manifestPath = Path.Combine(dir, ContentPackStore.ManifestFileName);
                    if (!File.Exists(manifestPath)) continue;
                    var name = Path.GetFileName(dir);
                    try
                    {
                        var m = JsonConvert.DeserializeObject<InstalledPackManifest>(loadManifest(manifestPath));
                        if (!string.IsNullOrEmpty(m?.PackName)) name = m!.PackName;
                    }
                    catch { }
                    long bytes = 0;
                    try { foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) bytes += new FileInfo(f).Length; }
                    catch { }
                    result.Add(new Candidate(dir, name, bytes));
                }
            }
            return result;
        }

        /// <summary>WPF: move each candidate into <paramref name="newAssets"/>/.packs and register it.
        /// A destination that already holds the guid is skipped (still registered). Throws on an I/O
        /// failure mid-move (the head shows msg_could_not_move_packs_0).</summary>
        public static (int Moved, int Registered) Move(IReadOnlyList<Candidate> packs, string newAssets, AppSettings settings,
            Func<string, string>? loadManifest = null)
        {
            loadManifest ??= PackEncryptionService.LoadEncryptedManifest;
            var target = Path.Combine(newAssets, ".packs");
            if (!Directory.Exists(target))
            {
                var di = Directory.CreateDirectory(target);
                try { di.Attributes |= FileAttributes.Hidden; } catch { }
            }
            int moved = 0, registered = 0;
            foreach (var c in packs)
            {
                var guid = Path.GetFileName(c.SourceFolder);
                var dest = Path.Combine(target, guid);
                if (!Directory.Exists(dest))
                {
                    CopyRecursive(c.SourceFolder, dest);
                    Directory.Delete(c.SourceFolder, recursive: true);
                    moved++;
                    Log.Information("Moved pack '{Name}' from {Source} to {Dest}", c.PackName, c.SourceFolder, dest);
                }
                else Log.Warning("Pack folder already exists at destination, skipping: {Dest}", dest);

                var manifestPath = Path.Combine(dest, ContentPackStore.ManifestFileName);
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    var m = JsonConvert.DeserializeObject<InstalledPackManifest>(loadManifest(manifestPath));
                    if (string.IsNullOrEmpty(m?.PackId)) continue;
                    settings.InstalledPackIds ??= new List<string>();
                    settings.PackGuidMap ??= new Dictionary<string, string>();
                    settings.ActivePackIds ??= new List<string>();
                    if (!settings.InstalledPackIds.Contains(m!.PackId)) settings.InstalledPackIds.Add(m.PackId);
                    settings.PackGuidMap[m.PackId] = guid;
                    if (!settings.ActivePackIds.Contains(m.PackId)) settings.ActivePackIds.Add(m.PackId);
                    registered++;
                }
                catch (Exception ex) { Log.Warning(ex, "Failed to register pack from manifest: {Path}", manifestPath); }
            }
            Log.Information("Moved {Moved}/{Total} packs, registered {Registered} in settings", moved, packs.Count, registered);
            return (moved, registered);
        }

        /// <summary>WPF FormatFileSize (MainWindow.UiUpdates.cs:2370), invariant digits.</summary>
        public static string FormatSize(long bytes)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (bytes >= 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("F1", ci) + " GB";
            if (bytes >= 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("F1", ci) + " MB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("F1", ci) + " KB";
            return $"{bytes} bytes";
        }

        private static void CopyRecursive(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.GetFiles(source)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), overwrite: false);
            foreach (var d in Directory.GetDirectories(source)) CopyRecursive(d, Path.Combine(dest, Path.GetFileName(d)));
        }

        private static string Full(string p)
        {
            try { return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return p; }
        }
    }
}
