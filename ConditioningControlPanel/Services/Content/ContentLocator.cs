using System;
using System.Collections.Generic;
using System.IO;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Two-root probe for content that may live EITHER in the install directory
    /// (<see cref="AppContext.BaseDirectory"/> — dev builds and legacy full installs) OR in the
    /// downloaded content-pack tree at <see cref="ContentRoot"/>, which mirrors the install-dir
    /// layout (<c>content\Resources\sounds\...</c>). The install dir always wins, so a machine that
    /// still has the bundled audio behaves exactly as it did before packs existed.
    ///
    /// A folder can legitimately exist in BOTH roots with DIFFERENT files — e.g.
    /// <c>companion_audio\mods\builtin-locked</c> keeps its <c>bark_rules.json</c>/<c>mantras.json</c>
    /// in the install dir while its .mp3s ship in a pack — which is what
    /// <see cref="EnumerateFiles"/> exists for.
    ///
    /// Nothing here ever throws: a probe failure degrades to "the install-dir path", so callers keep
    /// their existing missing-file handling (text-only barks, silent voice lines, …) unchanged.
    /// </summary>
    public static class ContentLocator
    {
        /// <summary>Install directory (bundled content). Probed first, always.</summary>
        public static string InstallRoot { get; } = SafeInstallRoot();

        /// <summary>
        /// Downloaded content-pack root: <c>%LOCALAPPDATA%\ConditioningControlPanel\content</c>.
        /// Mirrors the install-dir layout. Not guaranteed to exist.
        /// </summary>
        public static string ContentRoot { get; } = SafeContentRoot();

        private static string SafeInstallRoot()
        {
            try { return AppContext.BaseDirectory ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeContentRoot()
        {
            try { return Path.Combine(App.UserDataPath, "content"); }
            catch { return string.Empty; }
        }

        /// <summary>
        /// Full path for a resource given its install-dir-relative path (e.g.
        /// <c>Resources\sounds\flashes_audio\clip.mp3</c>): the install-dir copy when it exists, else
        /// the downloaded-pack copy when THAT exists, else the install-dir path so the caller's own
        /// "file missing" branch still fires against a sane path. Absolute paths pass through
        /// untouched.
        /// </summary>
        public static string Resolve(string relPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relPath)) return relPath;
                if (Path.IsPathRooted(relPath)) return relPath;

                var rel = Normalize(relPath);
                var installed = Combine(InstallRoot, rel);
                if (installed != null && File.Exists(installed)) return installed;

                var downloaded = Combine(ContentRoot, rel);
                if (downloaded != null && File.Exists(downloaded)) return downloaded;

                return installed ?? relPath;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ContentLocator.Resolve failed for {Path}: {Error}", relPath, ex.Message);
                return relPath;
            }
        }

        /// <summary>
        /// Directory counterpart of <see cref="Resolve"/>: the install-dir folder when it exists, else
        /// the downloaded-pack folder when THAT exists, else the install-dir path.
        /// </summary>
        public static string ResolveDirectory(string relDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relDir)) return relDir;
                if (Path.IsPathRooted(relDir)) return relDir;

                var rel = Normalize(relDir);
                var installed = Combine(InstallRoot, rel);
                if (installed != null && Directory.Exists(installed)) return installed;

                var downloaded = Combine(ContentRoot, rel);
                if (downloaded != null && Directory.Exists(downloaded)) return downloaded;

                return installed ?? relDir;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ContentLocator.ResolveDirectory failed for {Path}: {Error}", relDir, ex.Message);
                return relDir;
            }
        }

        /// <summary>True when either root has this relative directory.</summary>
        public static bool DirectoryExists(string relDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relDir)) return false;
                if (Path.IsPathRooted(relDir)) return Directory.Exists(relDir);

                var rel = Normalize(relDir);
                var installed = Combine(InstallRoot, rel);
                if (installed != null && Directory.Exists(installed)) return true;

                var downloaded = Combine(ContentRoot, rel);
                return downloaded != null && Directory.Exists(downloaded);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ContentLocator.DirectoryExists failed for {Path}: {Error}", relDir, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Union of the files both roots hold under <paramref name="relDir"/>, deduped by their
        /// root-relative path (case-insensitive) with the install dir winning. This is the primitive
        /// for folders that are SPLIT across roots (manifests bundled, audio downloaded). A root whose
        /// folder doesn't exist is skipped; an unreadable one is logged and skipped.
        /// </summary>
        public static IEnumerable<string> EnumerateFiles(
            string relDir, string pattern, SearchOption opt = SearchOption.TopDirectoryOnly)
        {
            var result = new List<string>();
            try
            {
                if (string.IsNullOrWhiteSpace(relDir)) return result;
                if (Path.IsPathRooted(relDir))
                {
                    result.AddRange(SafeGetFiles(relDir, pattern, opt));
                    return result;
                }

                var rel = Normalize(relDir);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var root in new[] { InstallRoot, ContentRoot })
                {
                    var dir = Combine(root, rel);
                    if (dir == null || !Directory.Exists(dir)) continue;

                    foreach (var file in SafeGetFiles(dir, pattern, opt))
                    {
                        // Key on the path relative to this root so the same logical file in both
                        // roots collapses to one entry (install-dir copy first = install-dir wins).
                        var key = file.Length > dir.Length ? file.Substring(dir.Length).TrimStart(
                            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : file;
                        if (seen.Add(key)) result.Add(file);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ContentLocator.EnumerateFiles failed for {Path}: {Error}", relDir, ex.Message);
            }
            return result;
        }

        /// <summary>
        /// For an absolute path under one root, the SAME relative path under the other root — but only
        /// when that counterpart actually exists; null otherwise. Lets callers that anchor on an
        /// already-resolved absolute folder (e.g. a portrait manifest's directory) still reach sibling
        /// files that shipped in the other root.
        /// </summary>
        public static string? Mirror(string absolutePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(absolutePath) || !Path.IsPathRooted(absolutePath)) return null;

                string? counterpart = null;
                if (!string.IsNullOrEmpty(InstallRoot) &&
                    absolutePath.StartsWith(InstallRoot, StringComparison.OrdinalIgnoreCase))
                    counterpart = Combine(ContentRoot, absolutePath.Substring(InstallRoot.Length));
                else if (!string.IsNullOrEmpty(ContentRoot) &&
                    absolutePath.StartsWith(ContentRoot, StringComparison.OrdinalIgnoreCase))
                    counterpart = Combine(InstallRoot, absolutePath.Substring(ContentRoot.Length));

                if (counterpart == null) return null;
                if (File.Exists(counterpart) || Directory.Exists(counterpart)) return counterpart;
                return null;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ContentLocator.Mirror failed for {Path}: {Error}", absolutePath, ex.Message);
                return null;
            }
        }

        private static string Normalize(string rel) =>
            rel.Replace('/', Path.DirectorySeparatorChar)
               .Trim()
               .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static string? Combine(string root, string rel)
        {
            if (string.IsNullOrEmpty(root)) return null;
            var trimmed = Normalize(rel);
            return trimmed.Length == 0 ? root : Path.Combine(root, trimmed);
        }

        private static IEnumerable<string> SafeGetFiles(string dir, string pattern, SearchOption opt)
        {
            try { return Directory.GetFiles(dir, pattern, opt); }
            catch (Exception ex)
            {
                App.Logger?.Debug("ContentLocator: could not enumerate {Dir}: {Error}", dir, ex.Message);
                return Array.Empty<string>();
            }
        }
    }
}
