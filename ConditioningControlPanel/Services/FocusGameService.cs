using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Lab Box 2 — Focus Training. Round-based gaze game where the user is
    /// rewarded for looking at the "correct" video. Pulls content from
    /// user-marked buckets (video subfolders + installed content packs).
    ///
    /// SKELETON: bucket enumeration is wired; round loop and game window
    /// land in subsequent commits.
    /// </summary>
    public class FocusGameService : IDisposable
    {
        private bool _disposed;

        public IReadOnlyList<FocusGameBucket> AvailableBuckets { get; private set; } = Array.Empty<FocusGameBucket>();

        public bool IsSessionRunning { get; private set; }

        public FocusGameService()
        {
            App.Logger?.Information("FocusGameService: constructed (skeleton)");
        }

        /// <summary>
        /// Re-enumerate buckets from the filesystem and installed packs.
        /// Merges with any persisted user choices (Include / IsTarget) by Id.
        /// </summary>
        public void RefreshBuckets()
        {
            var settings = App.Settings?.Current;
            var persisted = settings?.FocusGameBuckets ?? new List<FocusGameBucket>();
            var byId = persisted.ToDictionary(b => b.Id, b => b);
            var fresh = new List<FocusGameBucket>();

            foreach (var folder in EnumerateVideoSubfolders())
            {
                var id = folder;
                var name = "videos/" + folder;
                if (byId.TryGetValue(id, out var existing))
                {
                    existing.DisplayName = name;
                    existing.Source = BucketSource.VideoSubfolder;
                    fresh.Add(existing);
                }
                else
                {
                    fresh.Add(new FocusGameBucket
                    {
                        Source = BucketSource.VideoSubfolder,
                        Id = id,
                        DisplayName = name
                    });
                }
            }

            foreach (var pack in EnumerateContentPacks())
            {
                var id = pack.Id;
                var name = "Pack: " + pack.DisplayName;
                if (byId.TryGetValue(id, out var existing))
                {
                    existing.DisplayName = name;
                    existing.Source = BucketSource.ContentPack;
                    fresh.Add(existing);
                }
                else
                {
                    fresh.Add(new FocusGameBucket
                    {
                        Source = BucketSource.ContentPack,
                        Id = id,
                        DisplayName = name
                    });
                }
            }

            AvailableBuckets = fresh;

            if (settings != null)
            {
                settings.FocusGameBuckets = fresh;
                App.Settings?.Save();
            }

            App.Logger?.Information("FocusGameService: refreshed {Count} buckets", fresh.Count);
        }

        private static IEnumerable<string> EnumerateVideoSubfolders()
        {
            var root = Path.Combine(App.EffectiveAssetsPath, "videos");
            if (!Directory.Exists(root)) yield break;

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrEmpty(name)) continue;
                if (name.StartsWith(".")) continue; // skip .packs etc.
                yield return name;
            }
        }

        private static IEnumerable<(string Id, string DisplayName)> EnumerateContentPacks()
        {
            var packs = App.ContentPacks;
            if (packs == null) yield break;

            foreach (var packId in packs.InstalledPacks)
            {
                yield return (packId, packId);
            }
        }

        public bool ValidateBucketSelection(out string reason)
        {
            var included = AvailableBuckets.Where(b => b.IsIncluded).ToList();
            if (included.Count < 2) { reason = "Pick at least 2 buckets to include."; return false; }
            if (!included.Any(b => b.IsTarget)) { reason = "Mark at least one included bucket as Target."; return false; }
            if (!included.Any(b => !b.IsTarget)) { reason = "Leave at least one included bucket as decoy."; return false; }
            reason = "";
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            App.Logger?.Information("FocusGameService: disposed");
        }
    }
}
