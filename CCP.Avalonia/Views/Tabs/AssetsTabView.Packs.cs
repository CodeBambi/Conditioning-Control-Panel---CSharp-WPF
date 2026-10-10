using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The Library tree's third root, "Content Packs" (WPF MainWindow.Assets.cs RefreshAssetTree
    /// :516-546, BuildPackTree, LoadPackFolderThumbnails, LoadPackThumbnailAsync and the pack
    /// branches of the check-state / count / preset code). One node per ACTIVE installed pack, each
    /// with "images" / "videos" virtual folders; a file's tick is the <c>pack:&lt;id&gt;/&lt;name&gt;</c>
    /// key in DisabledAssetPaths, which <see cref="ContentPackStore.GetAllActivePackImages"/> honours.
    /// Thumbnails decrypt in memory, never to disk; a preview decrypts to a fresh temp file.
    /// </summary>
    public partial class AssetsTabView
    {
        /// <summary>Tests point the page at a scratch store; null = <see cref="ContentPackStore.Current"/>.</summary>
        public ContentPackStore? PackStoreOverride { get; set; }

        private ContentPackStore? Packs => PackStoreOverride ?? ContentPackStore.Current;

        private static bool IsPackLeaf(AssetTreeItem folder) =>
            folder.IsPackFolder && !string.IsNullOrEmpty(folder.PackId) && !string.IsNullOrEmpty(folder.PackFileType);

        private void AddContentPacksNode()
        {
            var store = Packs;
            if (store == null) return;
            List<string> active;
            try { active = store.GetActivePackIds(); }
            catch (Exception ex) { Log.Warning(ex, "Content packs: active list failed"); return; }
            if (active.Count == 0) return;

            var root = new AssetTreeItem
            {
                Name = LocOr("label_content_packs", "Content Packs"),
                FullPath = "",
                IsChecked = true,
                IsPackFolder = true,
                IsExpanded = true,
            };
            foreach (var id in active)
            {
                var node = BuildPackTree(store, id);
                if (node == null) continue;
                node.Parent = root;
                root.Children.Add(node);
            }
            if (root.Children.Count == 0) return;
            root.FileCount = root.Children.Sum(c => c.FileCount);
            root.CheckedFileCount = root.Children.Sum(c => c.GetTotalCheckedFileCount());
            root.IsChecked = root.CheckedFileCount > 0;
            Browser.Folders.Add(root);
        }

        private static AssetTreeItem? BuildPackTree(ContentPackStore store, string packId)
        {
            var files = store.GetPackFiles(packId);
            if (files.Count == 0) return null;
            var node = new AssetTreeItem
            {
                Name = store.GetPackName(packId),
                FullPath = "",
                IsPackFolder = true,
                PackId = packId,
                IsChecked = true,
            };
            foreach (var (type, label) in new[] { (ContentPackStore.ImageType, "images"), (ContentPackStore.VideoType, "videos") })
            {
                var typed = files.Where(f => f.FileType == type).ToList();
                if (typed.Count == 0) continue;
                var on = typed.Count(f => !S.DisabledAssetPaths.Contains(ContentPackStore.SelectionKey(packId, f)));
                node.Children.Add(new AssetTreeItem
                {
                    Name = label,
                    FullPath = "",
                    IsPackFolder = true,
                    PackId = packId,
                    PackFileType = type,
                    IsChecked = on > 0,
                    FileCount = typed.Count,
                    CheckedFileCount = on,
                    Parent = node,
                });
            }
            node.FileCount = files.Count;
            node.IsChecked = node.Children.Any(c => c.IsChecked);
            return node;
        }

        /// <summary>WPF LoadPackFolderThumbnails. False when the folder is not a pack leaf.</summary>
        private bool TrySelectPackFolder(AssetTreeItem folder)
        {
            if (!IsPackLeaf(folder)) return false;
            Browser.Thumbnails.Clear();
            Browser.ShowEmpty = false;
            var store = Packs;
            var files = store?.GetPackFiles(folder.PackId!, folder.PackFileType) ?? new List<PackFileEntry>();
            if (files.Count == 0)
            {
                Browser.EmptyText = LocOr("label_no_files_in_this_pack_folder", "No files in this pack folder");
                Browser.ShowEmpty = true;
                return true;
            }
            foreach (var file in files.OrderBy(f => f.OriginalName))
            {
                var key = ContentPackStore.SelectionKey(folder.PackId!, file);
                var item = new AssetThumbnailViewModel(folder.PackId!, file, !S.DisabledAssetPaths.Contains(key));
                Browser.Thumbnails.Add(item);
                if (!item.IsVideo && store != null) _ = LoadPackThumbnailAsync(store, item);
            }
            RecalculateFolderCheckState(folder);
            return true;
        }

        private static async Task LoadPackThumbnailAsync(ContentPackStore store, AssetThumbnailViewModel item)
        {
            item.IsLoadingThumbnail = true;
            try
            {
                var bmp = await Task.Run(async () =>
                {
                    await ThumbnailGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        using var ms = store.GetPackFileStream(item.PackId!, item.PackFile!);
                        return ms == null ? null : Bitmap.DecodeToWidth(ms, 100);
                    }
                    catch { return null; }
                    finally { ThumbnailGate.Release(); }
                });
                if (bmp != null) item.Thumbnail = bmp;
            }
            finally { item.IsLoadingThumbnail = false; }
        }

        /// <summary>Pack branch of RecalculateFolderCheckState. False when not a pack leaf.</summary>
        private bool RecalculatePackFolder(AssetTreeItem folder)
        {
            if (!IsPackLeaf(folder)) return false;
            var files = Packs?.GetPackFiles(folder.PackId!, folder.PackFileType) ?? new List<PackFileEntry>();
            folder.FileCount = files.Count;
            folder.CheckedFileCount = files.Count(f => !S.DisabledAssetPaths.Contains(ContentPackStore.SelectionKey(folder.PackId!, f)));
            return true;
        }

        /// <summary>Pack branch of UpdateFolderFilesCheckState: tick or untick every file of a pack leaf.</summary>
        private void SetPackFolderFiles(AssetTreeItem folder, bool isChecked)
        {
            if (!IsPackLeaf(folder)) return;
            foreach (var f in Packs?.GetPackFiles(folder.PackId!, folder.PackFileType) ?? new List<PackFileEntry>())
            {
                var key = ContentPackStore.SelectionKey(folder.PackId!, f);
                if (isChecked) S.DisabledAssetPaths.Remove(key);
                else S.DisabledAssetPaths.Add(key);
            }
        }

        /// <summary>Pack leaf counts for CountAssets: (total, active, isVideo), or null when not a pack leaf.</summary>
        private (int Total, int Active, bool IsVideo)? CountPackFolder(AssetTreeItem folder)
        {
            if (!IsPackLeaf(folder)) return null;
            var files = Packs?.GetPackFiles(folder.PackId!, folder.PackFileType) ?? new List<PackFileEntry>();
            var active = files.Count(f => !S.DisabledAssetPaths.Contains(ContentPackStore.SelectionKey(folder.PackId!, f)));
            return (files.Count, active, folder.PackFileType == ContentPackStore.VideoType);
        }

        /// <summary>WPF UpdatePresetCountsFromCurrentState pack loop: active pack files a preset leaves on.</summary>
        private (int Images, int Videos) CountPackFilesEnabledIn(HashSet<string>? disabled)
        {
            int images = 0, videos = 0;
            var store = Packs;
            if (store == null) return (0, 0);
            foreach (var id in store.GetActivePackIds())
                foreach (var f in store.GetPackFiles(id))
                {
                    if (disabled != null && disabled.Contains(ContentPackStore.SelectionKey(id, f))) continue;
                    if (f.FileType == ContentPackStore.ImageType) images++;
                    else if (f.FileType == ContentPackStore.VideoType) videos++;
                }
            return (images, videos);
        }

        /// <summary>WPF pack preview: decrypt to a fresh temp file, play it, delete it when the player closes.</summary>
        private void OpenPackPreview(AssetThumbnailViewModel file)
        {
            var store = Packs;
            var temp = store?.GetPackFileTempPath(file.PackId!, file.PackFile!);
            if (store == null || temp == null) return;
            var win = new Windows.MiniPlayerWindow();
            win.Closed += (_, _) => store.DeleteTempFile(temp);
            win.LoadFile(temp);
            if (TopLevel.GetTopLevel(this) is Window owner) win.Show(owner);
            else win.Show();
        }
    }
}
