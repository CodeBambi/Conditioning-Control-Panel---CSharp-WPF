using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Services.Content
{
    /// <summary>
    /// ONE-TIME heal for libraries that already contain extensionless media.
    ///
    /// Every media scanner in the app is extension-gated, so a file written without an
    /// extension is invisible: it is in the folder, it plays fine in any player, and the app
    /// simply never lists it. <see cref="Helpers.MediaTypeSniffer"/> stops new ones being
    /// written; this walks the existing tree once and renames what is already broken
    /// (<c>7f3a…c1</c> → <c>7f3a…c1.mp4</c>) so an affected install fixes itself.
    ///
    /// Rules it will not break:
    ///   * Runs ONCE ever, gated on a marker file under <c>App.UserDataPath</c> (a settings
    ///     property was deliberately avoided — see the field remarks).
    ///   * Never overwrites: a taken target name gets <c>_1</c>, <c>_2</c>… appended.
    ///   * Never throws. Asset loading must not be able to fail because of a repair.
    ///   * Skips hidden/system folders, anything starting with '.' (<c>.packs</c>,
    ///     <c>.temp</c> — the app's internal folders, same rule
    ///     <c>FocusGameService</c> uses), and hidden/system files.
    ///   * Bytes that match nothing known are LEFT ALONE (logged at Debug). A stray README
    ///     or a partial download is not ours to rename.
    /// </summary>
    internal static class AssetExtensionRepair
    {
        /// <summary>
        /// Marker file, not an <c>AppSettings</c> property, for three reasons: this is a
        /// one-shot migration rather than a user preference, the flag must survive a settings
        /// file that fails to load (which is exactly the sort of install that has odd files in
        /// it), and it keeps the migration out of the settings JSON the user can see and edit.
        /// Versioned in the name so a future, wider repair can ship as ".v2".
        /// </summary>
        private const string MarkerFileName = "assets-extension-repair.v1.done";

        /// <summary>Depth guard for the walk — a symlinked loop must not spin forever.</summary>
        private const int MaxDepth = 12;

        /// <summary>Ceiling on how many files the pass will look at. A library of hundreds of
        /// thousands of files is not going to be fixed on the UI's schedule; the marker is
        /// still written so this never becomes a per-launch tax.</summary>
        private const int MaxFilesInspected = 200_000;

        /// <summary>Belt to the marker's braces — makes a re-entrant call a no-op even before
        /// the marker has been written.</summary>
        private static int _started;

        private static string MarkerPath => Path.Combine(App.UserDataPath, MarkerFileName);

        /// <summary>True when the repair has already been done on this install.</summary>
        private static bool AlreadyDone()
        {
            try { return File.Exists(MarkerPath); }
            catch { return true; }   // can't tell → assume done; never risk a repeat loop
        }

        /// <summary>
        /// Fire-and-forget entry point. Returns immediately; the walk happens on the thread
        /// pool so opening the Assets tab never waits on disk. <paramref name="onRepaired"/>
        /// is invoked (on the thread-pool thread — marshal it yourself) with the number of
        /// files renamed, and ONLY when that number is greater than zero.
        /// </summary>
        public static void RunOnceInBackground(Action<int>? onRepaired = null)
        {
            try
            {
                if (System.Threading.Interlocked.Exchange(ref _started, 1) != 0) return;
                if (AlreadyDone()) return;

                _ = Task.Run(() =>
                {
                    int renamed = 0;
                    try
                    {
                        renamed = Run(App.EffectiveAssetsPath);
                    }
                    catch (Exception ex)
                    {
                        // Can only happen if App.EffectiveAssetsPath itself threw — Run is
                        // already total. Swallowed on purpose: the Assets tab is mid-load.
                        App.Logger?.Warning("AssetExtensionRepair: pass failed: {Error}", ex.Message);
                    }

                    try { if (renamed > 0) onRepaired?.Invoke(renamed); }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug("AssetExtensionRepair: completion callback threw: {Error}", ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AssetExtensionRepair: could not schedule the pass: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// The pass itself: walk <paramref name="root"/>, sniff every extensionless file and
        /// rename the recognised ones in place. Writes the marker when it finishes, whatever
        /// the result — a repair that found nothing must not run again next launch. Returns
        /// the number of files renamed. Never throws.
        /// </summary>
        public static int Run(string? root)
        {
            int renamed = 0, skippedUnknown = 0, inspected = 0, failed = 0;

            try
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    App.Logger?.Debug("AssetExtensionRepair: no assets folder at {Root} — nothing to repair", root);
                    WriteMarker();
                    return 0;
                }

                var started = DateTime.UtcNow;

                foreach (var file in EnumerateCandidates(root!))
                {
                    if (++inspected > MaxFilesInspected)
                    {
                        App.Logger?.Warning("AssetExtensionRepair: stopped after {Max} files — library is larger than the repair budget",
                            MaxFilesInspected);
                        break;
                    }

                    var ext = MediaTypeSniffer.FromFileHead(file);
                    if (string.IsNullOrEmpty(ext))
                    {
                        skippedUnknown++;
                        App.Logger?.Debug("AssetExtensionRepair: {File} matches no known media signature — left alone",
                            Path.GetFileName(file));
                        continue;
                    }

                    var target = FreeTargetPath(file, ext!);
                    if (target == null)
                    {
                        failed++;
                        continue;
                    }

                    try
                    {
                        // No overwrite:true — FreeTargetPath already picked a free name, and if
                        // something claimed it in between we would rather fail than clobber.
                        File.Move(file, target);
                        renamed++;
                        App.Logger?.Information("AssetExtensionRepair: renamed {Old} -> {New}",
                            Path.GetFileName(file), Path.GetFileName(target));
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        App.Logger?.Warning("AssetExtensionRepair: could not rename {Old} -> {New}: {Error}",
                            Path.GetFileName(file), Path.GetFileName(target), ex.Message);
                    }
                }

                var ms = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                if (renamed > 0 || skippedUnknown > 0 || failed > 0)
                {
                    App.Logger?.Information(
                        "AssetExtensionRepair: {Renamed} file(s) renamed, {Unknown} unrecognised, {Failed} failed, {Inspected} inspected in {Ms} ms",
                        renamed, skippedUnknown, failed, inspected, ms);
                }
                else
                {
                    App.Logger?.Debug("AssetExtensionRepair: nothing to repair ({Inspected} extensionless file(s) seen in {Ms} ms)",
                        inspected, ms);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("AssetExtensionRepair: pass aborted: {Error}", ex.Message);
            }

            WriteMarker();
            return renamed;
        }

        /// <summary>
        /// Every extensionless, non-hidden file under <paramref name="root"/>. Iterative so a
        /// deep tree cannot blow the stack, and each directory read is guarded on its own so
        /// one unreadable folder costs that folder and nothing else.
        /// </summary>
        private static IEnumerable<string> EnumerateCandidates(string root)
        {
            var stack = new Stack<(string Path, int Depth)>();
            stack.Push((root, 0));

            while (stack.Count > 0)
            {
                var (dir, depth) = stack.Pop();

                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch (Exception ex)
                {
                    App.Logger?.Debug("AssetExtensionRepair: cannot read {Dir}: {Error}", dir, ex.Message);
                    continue;
                }

                foreach (var file in files)
                {
                    if (!string.IsNullOrEmpty(Path.GetExtension(file))) continue;
                    if (IsHiddenOrSystem(file)) continue;
                    yield return file;
                }

                if (depth >= MaxDepth) continue;

                string[] subs;
                try { subs = Directory.GetDirectories(dir); }
                catch (Exception ex)
                {
                    App.Logger?.Debug("AssetExtensionRepair: cannot list subfolders of {Dir}: {Error}", dir, ex.Message);
                    continue;
                }

                foreach (var sub in subs)
                {
                    var name = Path.GetFileName(sub);
                    // '.'-prefixed = the app's own internals (.packs, .temp). Same rule
                    // FocusGameService's scan uses.
                    if (string.IsNullOrEmpty(name) || name.StartsWith('.')) continue;
                    if (IsHiddenOrSystem(sub)) continue;
                    stack.Push((sub, depth + 1));
                }
            }
        }

        private static bool IsHiddenOrSystem(string path)
        {
            try
            {
                var attr = File.GetAttributes(path);
                if ((attr & FileAttributes.Hidden) != 0) return true;
                if ((attr & FileAttributes.System) != 0) return true;
                if ((attr & FileAttributes.ReparsePoint) != 0) return true;   // don't follow junctions
                return false;
            }
            catch { return true; }   // unreadable attributes → leave it well alone
        }

        /// <summary>
        /// <paramref name="file"/> + <paramref name="ext"/>, or the first free
        /// <c>name_N + ext</c> when that is taken. Null when 100 suffixes were all taken or
        /// the path could not be built — the caller then skips the file rather than guessing.
        /// </summary>
        private static string? FreeTargetPath(string file, string ext)
        {
            try
            {
                var dir = Path.GetDirectoryName(file);
                if (string.IsNullOrEmpty(dir)) return null;
                var name = Path.GetFileName(file);

                var candidate = Path.Combine(dir, name + ext);
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;

                for (int i = 1; i <= 100; i++)
                {
                    candidate = Path.Combine(dir, $"{name}_{i}{ext}");
                    if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
                }

                App.Logger?.Warning("AssetExtensionRepair: no free name for {File} with {Ext} — skipped", name, ext);
                return null;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("AssetExtensionRepair: target path for {File} failed: {Error}", file, ex.Message);
                return null;
            }
        }

        private static void WriteMarker()
        {
            try
            {
                Directory.CreateDirectory(App.UserDataPath);
                File.WriteAllText(MarkerPath,
                    "Assets extension repair completed " + DateTime.UtcNow.ToString("o") + Environment.NewLine +
                    "Delete this file to let the one-time repair pass run again." + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // Worst case the pass runs again next launch; it is idempotent, so that is a
                // wasted walk rather than a bug.
                App.Logger?.Debug("AssetExtensionRepair: could not write the marker: {Error}", ex.Message);
            }
        }
    }
}
