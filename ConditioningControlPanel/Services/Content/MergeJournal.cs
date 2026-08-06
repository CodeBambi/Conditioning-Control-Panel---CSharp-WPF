using System;
using System.Collections.Generic;
using System.IO;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Record of what a content-pack merge wrote into the live content tree, so a merge that throws
    /// part-way can be taken back out.
    ///
    /// Without this, a failed install left whatever it had already copied behind, and that debris is
    /// exactly what makes a broken pack read as "present" to the on-disk floor probes — the pack is
    /// then never re-fetched and the user stays voiceless. Deleting the destination TREE instead is
    /// not an option: every loose-media pack merges into the shared
    /// <c>content\Resources</c>, so a wholesale delete would take other packs' files with it. Only
    /// what this merge created comes back out.
    ///
    /// Overwritten files are removed along with new ones: a half-updated file is not a file worth
    /// keeping, and driving the pack all the way back to "clearly missing" is the whole point.
    /// </summary>
    internal sealed class MergeJournal
    {
        private readonly List<string> _files = new();
        private readonly List<string> _directories = new();
        private readonly List<string> _trees = new();

        // A retry merges on top of the first attempt's work and re-records the same paths, so
        // membership is deduped while the lists keep write order.
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Destination files this merge wrote, in write order.</summary>
        internal IReadOnlyList<string> Files => _files;

        /// <summary>Destination directories this merge had to create, in creation order.</summary>
        internal IReadOnlyList<string> Directories => _directories;

        /// <summary>
        /// Destination trees this merge created wholesale by renaming the staging folder onto a
        /// target that did not exist. Nothing else can own them, so they come out recursively.
        /// </summary>
        internal IReadOnlyList<string> Trees => _trees;

        internal bool IsEmpty => _files.Count == 0 && _directories.Count == 0 && _trees.Count == 0;

        internal void RecordFile(string path)
        {
            if (!string.IsNullOrEmpty(path) && _seen.Add("f:" + path)) _files.Add(path);
        }

        internal void RecordDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path) && _seen.Add("d:" + path)) _directories.Add(path);
        }

        internal void RecordTree(string path)
        {
            if (!string.IsNullOrEmpty(path) && _seen.Add("t:" + path)) _trees.Add(path);
        }

        /// <summary>
        /// Deletes everything this merge wrote, best-effort, and returns how many files actually
        /// went. Per-item IO errors are expected here (an antivirus scanner holding a freshly
        /// written file is the usual reason the merge failed in the first place) and are swallowed
        /// so one locked file cannot abort the rest of the cleanup.
        /// </summary>
        internal int Rollback()
        {
            var removed = 0;

            foreach (var tree in _trees)
            {
                try
                {
                    if (!Directory.Exists(tree)) continue;
                    removed += CountFilesUnder(tree);
                    Directory.Delete(tree, true);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("MergeJournal: could not remove tree {Path}: {Error}", tree, ex.Message);
                }
            }

            foreach (var file in _files)
            {
                try
                {
                    if (!File.Exists(file)) continue;
                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("MergeJournal: could not remove {Path}: {Error}", file, ex.Message);
                }
            }

            // Longest path first. A child path is always its parent plus a separator and a name, so
            // length descending guarantees descendants are visited before their ancestors — which is
            // the only order in which a parent can have become empty by the time we reach it.
            var deepestFirst = new List<string>(_directories);
            deepestFirst.Sort((a, b) => b.Length.CompareTo(a.Length));

            foreach (var dir in deepestFirst)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    // Non-recursive on purpose: anything still in there is not ours.
                    Directory.Delete(dir, false);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("MergeJournal: left {Path} in place: {Error}", dir, ex.Message);
                }
            }

            return removed;
        }

        private static int CountFilesUnder(string dir)
        {
            try
            {
                var count = 0;
                foreach (var _ in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) count++;
                return count;
            }
            catch { return 0; }
        }
    }
}
