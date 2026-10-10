// PORTED from ConditioningControlPanel/Services/Chaos/ChaosArt.cs (7.1.5).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// Resolves optional Chaos art with a graceful fallback. Every slot (boon chips, portraits, the
    /// hub banner, the recap banner, bubble sprites) asks here; null means "no art, draw the vector
    /// placeholder". Convention: <c>assets/Chaos/{kind}/{id}.png</c> under the user's assets folder
    /// first, then beside the exe (CCP.Avalonia.csproj copies the shared art there).
    /// One decoded bitmap per file, shared by every caller (WPF's frozen-bitmap cache: per-spawn
    /// decodes once climbed to an out-of-memory crash).
    /// </summary>
    internal static class ChaosArt
    {
        private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Test seam: the roots probed, in order. Null = the live roots.</summary>
        internal static Func<IEnumerable<string>>? RootsOverride;

        internal static void ResetForTest() { Cache.Clear(); RootsOverride = null; }

        public static Bitmap? TryLoad(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return Cache.GetOrAdd(path, static p =>
            {
                try { return File.Exists(p) ? new Bitmap(p) : null; }
                catch { return null; }
            });
        }

        public static Bitmap? Resolve(string kind, string id) => TryLoad(PathFor(kind, id));
        public static Bitmap? ResolveBanner() => TryLoad(FilePath("banner.png"));
        public static Bitmap? ResolveMenu() => TryLoad(FilePath("menu.png"));
        public static Bitmap? ResolveMenuFrame(int n) => TryLoad(MenuFramePath(n));
        public static Bitmap? ResolveRecap() => TryLoad(FilePath("recap.png"));

        /// <summary>The first existing convention path for a kind/id, or null.</summary>
        public static string? PathFor(string kind, string id) => FilePath(Path.Combine(kind, id + ".png"));

        /// <summary>First existing path for a file under <c>assets/Chaos/</c>, or null. Never leaves the folder.</summary>
        public static string? FilePath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..") || Path.IsPathRooted(fileName)) return null;
            foreach (var root in Roots())
            {
                var p = Path.Combine(root, fileName);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public static string? MenuFramePath(int n) => FilePath($"menu_{n}.png");

        /// <summary>The Chaos folders: the user's library (assets/Chaos), then the shipped art beside the exe.</summary>
        private static IEnumerable<string> Roots()
        {
            if (RootsOverride != null) { foreach (var r in RootsOverride()) yield return r; yield break; }
            string? user = null;
            try { user = CorePaths.EffectiveAssets; } catch { /* no assets folder yet */ }
            if (!string.IsNullOrEmpty(user)) yield return Path.Combine(user, "Chaos");
            yield return Path.Combine(AppContext.BaseDirectory, "assets", "Chaos");
        }
    }
}
