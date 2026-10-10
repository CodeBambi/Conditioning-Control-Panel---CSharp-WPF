using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Content;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// LIBRARY > Asset Browser, PORTED from WPF 7.1.5 MainWindow.Assets.cs (RefreshAssetTree,
    /// BuildFolderTree, the check-state family, LoadFolderThumbnails / LoadThumbnailAsync,
    /// Select / Deselect all, the asset preset region, UpdateAssetCounts / CountAssetsRecursive).
    ///
    /// <para>Same rules as WPF: the tree has exactly two roots, <c>images</c> and <c>videos</c>
    /// under the effective assets folder (audio, braindrain, mindwipe, wallpapers are NOT media
    /// the flash/video engines pick from and never show); selection is a BLACKLIST
    /// (<see cref="AppSettings.DisabledAssetPaths"/>, keys relative to the assets root with
    /// forward slashes) plus the whole-folder memory <see cref="AssetFolderExclusion"/> (#1231);
    /// every change saves (debounced) and tells the media services through
    /// <see cref="AssetSelection.NotifyChanged"/>.</para>
    ///
    /// <para>The third root, "Content Packs", lives in AssetsTabView.Packs.cs (ContentPackStore).
    /// Video tiles draw the clapper card: WPF used the Windows shell thumbnail, and this head has
    /// no shell thumbnailer.</para>
    /// </summary>
    public partial class AssetsTabView
    {
        /// <summary>Every extension the tree counts and the grid shows (WPF validExtensions).</summary>
        internal static readonly string[] MediaExtensions =
        {
            ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".avif",
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm",
        };

        /// <summary>The narrower lists WPF's header count and preset counts use.</summary>
        private static readonly string[] CountImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
        private static readonly string[] CountVideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };

        private static readonly SemaphoreSlim ThumbnailGate = new(4); // WPF _thumbnailSemaphore

        /// <summary>The page's live state; the DataContext.</summary>
        public AssetsTabViewModel Browser { get; } = new();

        /// <summary>Tests point the page at a scratch folder; null = <see cref="CorePaths.EffectiveAssets"/>.</summary>
        public string? AssetsRootOverride { get; set; }

        private string AssetsRoot => AssetsRootOverride ?? CorePaths.EffectiveAssets ?? "";

        private AssetTreeItem? _selectedFolder;
        private bool _isUpdatingFolderCheckState;
        private bool _isLoadingPreset;
        private bool _repairHooked;

        private static AppSettings S => CoreSettings.Current;

        private void InitializeAssetBrowser()
        {
            Browser.EmptyText = LocOr("label_select_a_folder_to_view_its_contents", "Select a folder to view its contents");
            BtnRefreshAssets.Click += BtnRefreshAssets_Click;
            BtnSelectAllAssets.Click += (_, _) => SelectAllInFolder(true);
            BtnDeselectAllAssets.Click += (_, _) => SelectAllInFolder(false);
            BtnSaveAssetPreset.Click += BtnSaveAssetPreset_Click;
            BtnUpdateAssetPreset.Click += BtnUpdateAssetPreset_Click;
            BtnDeleteAssetPreset.Click += BtnDeleteAssetPreset_Click;
            CmbAssetPresets.SelectionChanged += CmbAssetPresets_SelectionChanged;
            AssetTreeView.SelectionChanged += AssetTreeView_SelectionChanged;

            // WPF ShowTab("assets") runs RefreshAssetTree + InitializeAssetPresets on every visit.
            PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty) return;
                // WPF MainWindow.AssetsFx.cs OnAssetsTabVisibilityChanged: hidden stops the motion.
                if (!IsVisible) { _refreshedOnShow = false; StopMediaLogPulse(resetOpacity: true); return; }
                RefreshAssetBrowser();
                _refreshedOnShow = true;   // the shell's OnTabShown follows the reveal: one scan, not two
            };
        }

        /// <summary>The whole visit: tree, counts, presets. Public so the shell (folder picked)
        /// and the tests can call it.</summary>
        public void RefreshAssetBrowser()
        {
            try
            {
                RefreshAssetTree();
                InitializeAssetPresets();
            }
            catch (Exception ex) { Log.Warning(ex, "Asset browser refresh failed"); }
        }

        /// <summary>WPF MainWindow.UiUpdates.cs BtnRefreshAssets_Click: rescan every consumer
        /// (#336), rebuild the tree, say so.</summary>
        private async void BtnRefreshAssets_Click(object? sender, RoutedEventArgs e)
        {
            AssetSelection.NotifyChanged();
            RefreshAssetBrowser();
            try
            {
                await InformAsync(Loc.Get("title_success"), Loc.Get("msg_assets_refreshed"));
            }
            catch (Exception ex) { Log.Debug("Assets refreshed note: {E}", ex.Message); }
        }

        // =====================================================================================
        //  tree
        // =====================================================================================

        private void RefreshAssetTree()
        {
            HookExtensionRepair();

            var assetsPath = AssetsRoot;
            // WPF keeps the open folder across a rescan or a preset switch; re-found in the new tree below.
            var openPath = _selectedFolder?.FullPath;
            Browser.Folders.Clear();
            Browser.Thumbnails.Clear();
            _selectedFolder = null;
            Browser.EmptyText = LocOr("label_select_a_folder_to_view_its_contents", "Select a folder to view its contents");
            Browser.ShowEmpty = true;

            if (!string.IsNullOrEmpty(assetsPath))
            {
                // Files added to an unticked folder since it was unticked stay unticked (#1231).
                if (AssetFolderExclusion.ExpandFromDisk(S, assetsPath) > 0)
                    InvalidateAssetPoolsAfterSelectionChange();

                foreach (var root in new[] { "images", "videos" })
                {
                    var folder = Path.Combine(assetsPath, root);
                    if (!Directory.Exists(folder)) continue;
                    var node = BuildFolderTree(folder, root, assetsPath);
                    node.IsExpanded = true;
                    Browser.Folders.Add(node);
                }
            }
            AddContentPacksNode();

            UpdateAssetCounts();

            if (!string.IsNullOrEmpty(openPath) && FindFolder(Browser.Folders, openPath) is { } reopen)
            {
                for (var p = reopen.Parent; p != null; p = p.Parent) p.IsExpanded = true;
                AssetTreeView.SelectedItem = reopen;
                if (!ReferenceEquals(_selectedFolder, reopen)) SelectFolder(reopen);
            }
        }

        private static AssetTreeItem? FindFolder(IEnumerable<AssetTreeItem> nodes, string path)
        {
            foreach (var n in nodes)
            {
                if (string.Equals(n.FullPath, path, StringComparison.Ordinal)) return n;
                if (FindFolder(n.Children, path) is { } hit) return hit;
            }
            return null;
        }

        /// <summary>WPF hooks the one-time extension heal here because every scan funnels through it.</summary>
        private void HookExtensionRepair()
        {
            if (_repairHooked || AssetsRootOverride != null) return;
            _repairHooked = true;
            AssetExtensionRepair.RunOnceInBackground(renamed => Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    // WPF MainWindow.Assets.cs:477: a tab torn down before the repair finished is left alone.
                    if (!IsLoaded || VisualRoot == null) return;
                    RefreshAssetTree();
                    InvalidateAssetPoolsAfterSelectionChange();
                    Log.Information("AssetExtensionRepair: reloaded assets after {Count} rename(s)", renamed);
                }
                catch (Exception ex) { Log.Warning("AssetExtensionRepair: post-repair refresh failed: {Error}", ex.Message); }
            }));
        }

        private static List<string> MediaFilesIn(string path) =>
            Directory.GetFiles(path)
                .Where(f => MediaExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

        private static string RelativeKey(string basePath, string file) =>
            Path.GetRelativePath(basePath, file).Replace('\\', '/');

        private static AssetTreeItem BuildFolderTree(string path, string name, string basePath)
        {
            var node = new AssetTreeItem { Name = name, FullPath = path, IsChecked = true };

            // A folder can vanish or a drive drop out between the parent's listing and here
            // (TOCTOU, WPF #379 / #370): skip it rather than take the page down.
            List<string> files;
            try { files = MediaFilesIn(path); }
            catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
            {
                Log.Debug("BuildFolderTree: skipping unreadable folder {Path}: {Error}", path, ex.Message);
                node.UpdateCheckState();
                return node;
            }
            node.FileCount = files.Count;
            var disabled = S.DisabledAssetPaths;
            node.CheckedFileCount = files.Count(f => !disabled.Contains(RelativeKey(basePath, f)));

            string[] subDirs;
            try { subDirs = Directory.GetDirectories(path); }
            catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
            {
                Log.Debug("BuildFolderTree: cannot enumerate subfolders of {Path}: {Error}", path, ex.Message);
                subDirs = Array.Empty<string>();
            }
            foreach (var dir in subDirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var child = BuildFolderTree(dir, Path.GetFileName(dir), basePath);
                child.Parent = node;
                node.Children.Add(child);
            }

            node.UpdateCheckState();
            return node;
        }

        private void AssetTreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (AssetTreeView.SelectedItem is not AssetTreeItem folder) return;
            SelectFolder(folder);
        }

        /// <summary>WPF AssetTreeView_SelectedItemChanged, local folders only (no pack nodes here).</summary>
        public void SelectFolder(AssetTreeItem folder)
        {
            _selectedFolder = folder;
            if (TrySelectPackFolder(folder)) return;
            if (!string.IsNullOrEmpty(folder.FullPath))
            {
                LoadFolderThumbnails(folder.FullPath);
                RecalculateFolderCheckState(folder);
            }
            else
            {
                Browser.Thumbnails.Clear();
                Browser.EmptyText = LocOr("label_select_a_subfolder_to_view_files", "Select a subfolder to view files");
                Browser.ShowEmpty = true;
            }
        }

        private void RecalculateFolderCheckState(AssetTreeItem folder)
        {
            if (RecalculatePackFolder(folder)) { }
            else if (!string.IsNullOrEmpty(folder.FullPath) && Directory.Exists(folder.FullPath))
            {
                try
                {
                    var basePath = AssetsRoot;
                    folder.CheckedFileCount = MediaFilesIn(folder.FullPath)
                        .Count(f => !S.DisabledAssetPaths.Contains(RelativeKey(basePath, f)));
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
            }
            foreach (var child in folder.Children) RecalculateFolderCheckState(child);
            folder.UpdateCheckState();
        }

        // =====================================================================================
        //  thumbnails
        // =====================================================================================

        /// <summary>Every thumbnail decode started by the last folder load (tests await it).</summary>
        internal Task ThumbnailLoads { get; private set; } = Task.CompletedTask;
        internal IReadOnlyList<AssetTreeItem> AssetTree => Browser.Folders;
        internal IReadOnlyList<AssetThumbnailViewModel> CurrentFolderFiles => Browser.Thumbnails;

        private void LoadFolderThumbnails(string folderPath)
        {
            Browser.Thumbnails.Clear();
            Browser.ShowEmpty = false;
            ThumbnailLoads = Task.CompletedTask;

            if (!Directory.Exists(folderPath))
            {
                Browser.EmptyText = LocOr("label_folder_does_not_exist", "Folder does not exist");
                Browser.ShowEmpty = true;
                return;
            }

            List<string> files;
            try { files = MediaFilesIn(folderPath).OrderBy(f => Path.GetFileName(f)).ToList(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                Browser.EmptyText = LocOr("label_folder_does_not_exist", "Folder does not exist");
                Browser.ShowEmpty = true;
                return;
            }

            if (files.Count == 0)
            {
                Browser.EmptyText = LocOr("label_no_media_files_in_this_folder", "No media files in this folder");
                Browser.ShowEmpty = true;
                return;
            }

            var basePath = AssetsRoot;
            var loads = new List<Task>();
            foreach (var file in files)
            {
                var rel = RelativeKey(basePath, file);
                var item = new AssetThumbnailViewModel(file, rel, !S.DisabledAssetPaths.Contains(rel));
                try { item.SizeBytes = new FileInfo(file).Length; } catch { }
                Browser.Thumbnails.Add(item);
                if (!item.IsVideo) loads.Add(LoadThumbnailAsync(item));
            }
            ThumbnailLoads = Task.WhenAll(loads);
        }

        /// <summary>WPF LoadThumbnailAsync: decoded off the UI thread at 100 px wide, four at a
        /// time; a file that will not decode keeps its blank tile.</summary>
        private static async Task LoadThumbnailAsync(AssetThumbnailViewModel item)
        {
            item.IsLoadingThumbnail = true;
            try
            {
                var bmp = await Task.Run(async () =>
                {
                    await ThumbnailGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        using var fs = File.OpenRead(item.FullPath);
                        return Bitmap.DecodeToWidth(fs, 100);
                    }
                    catch { return null; }
                    finally { ThumbnailGate.Release(); }
                });
                if (bmp != null) item.Thumbnail = bmp;
            }
            finally
            {
                item.IsLoadingThumbnail = false;
            }
        }

        private void ThumbnailCheckBox_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is CheckBox { DataContext: AssetThumbnailViewModel file })
                UpdateFileCheckState(file);
        }

        /// <summary>Single click toggles, double click previews (WPF ThumbnailItem_Click).</summary>
        private void ThumbnailItem_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control { DataContext: AssetThumbnailViewModel file } c) return;
            if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) return;
            if (e.Source is Visual v && v.FindAncestorOfType<CheckBox>(includeSelf: true) != null) return;
            if (e.ClickCount == 2)
            {
                OpenAssetPreview(file);
            }
            else
            {
                file.IsChecked = !file.IsChecked;
                UpdateFileCheckState(file);
            }
            e.Handled = true;
        }

        private void ThumbnailItem_Preview_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { DataContext: AssetThumbnailViewModel file }) OpenAssetPreview(file);
        }

        private void ThumbnailItem_OpenInExplorer_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { DataContext: AssetThumbnailViewModel file }) return;
            if (file.IsPackFile) return;   // an encrypted pack file has no folder to show
            try { Platform.ExternalOpener.Open(Path.GetDirectoryName(file.FullPath)); }
            catch (Exception ex) { Log.Warning(ex, "Open in file manager failed for {File}", file.Name); }
        }

        private void OpenAssetPreview(AssetThumbnailViewModel file)
        {
            try
            {
                if (file.IsPackFile) { OpenPackPreview(file); return; }
                if (!File.Exists(file.FullPath)) return;
                var win = new Windows.MiniPlayerWindow();
                win.LoadFile(file.FullPath);
                if (TopLevel.GetTopLevel(this) is Window owner) win.Show(owner);
                else win.Show();
            }
            catch (Exception ex) { Log.Error(ex, "Failed to open asset preview for {File}", file.Name); }
        }

        // =====================================================================================
        //  selection
        // =====================================================================================

        private void FolderCheckBox_Click(object? sender, RoutedEventArgs e)
        {
            if (_isUpdatingFolderCheckState) return;
            if (sender is not CheckBox { DataContext: AssetTreeItem folder }) return;
            SetFolderChecked(folder, folder.IsChecked);
        }

        /// <summary>A whole-folder tick (WPF FolderCheckBox_Changed / Select all / Deselect all).</summary>
        public void SetFolderChecked(AssetTreeItem folder, bool isChecked)
        {
            _isUpdatingFolderCheckState = true;
            try
            {
                SetFolderAndChildrenChecked(folder, isChecked);
                UpdateFolderFilesCheckState(folder, isChecked);
                MarkFolderExclusion(folder, isChecked);
                folder.Parent?.UpdateCheckStateFromChildren();
                UpdateAssetCounts();
                RefreshThumbnailCheckboxes();
                InvalidateAssetPoolsAfterSelectionChange();
            }
            finally { _isUpdatingFolderCheckState = false; }
        }

        private void SelectAllInFolder(bool isChecked)
        {
            if (_selectedFolder == null) return;
            SetFolderChecked(_selectedFolder, isChecked);
        }

        private void MarkFolderExclusion(AssetTreeItem folder, bool isChecked)
        {
            if (folder.IsPackFolder || string.IsNullOrEmpty(folder.FullPath)) return;
            var rel = Path.GetRelativePath(AssetsRoot, folder.FullPath);
            AssetFolderExclusion.MarkFolder(S.DisabledAssetFolders, rel, isChecked);
        }

        private static void SetFolderAndChildrenChecked(AssetTreeItem folder, bool isChecked)
        {
            folder.IsChecked = isChecked;
            folder.CheckedFileCount = isChecked ? folder.FileCount : 0;
            foreach (var child in folder.Children) SetFolderAndChildrenChecked(child, isChecked);
        }

        private void RefreshThumbnailCheckboxes()
        {
            foreach (var item in Browser.Thumbnails)
                item.IsChecked = !S.DisabledAssetPaths.Contains(item.RelativePath);
        }

        private void UpdateFolderFilesCheckState(AssetTreeItem folder, bool isChecked)
        {
            SetPackFolderFiles(folder, isChecked);
            if (!string.IsNullOrEmpty(folder.FullPath) && Directory.Exists(folder.FullPath))
            {
                var basePath = AssetsRoot;
                try
                {
                    foreach (var file in MediaFilesIn(folder.FullPath))
                    {
                        var rel = RelativeKey(basePath, file);
                        if (isChecked) S.DisabledAssetPaths.Remove(rel);
                        else S.DisabledAssetPaths.Add(rel);
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
            }
            foreach (var child in folder.Children) UpdateFolderFilesCheckState(child, isChecked);
            folder.CheckedFileCount = isChecked ? folder.FileCount : 0;
        }

        /// <summary>One file ticked or unticked (WPF UpdateFileCheckState).</summary>
        public void UpdateFileCheckState(AssetThumbnailViewModel file)
        {
            if (file.IsChecked)
            {
                S.DisabledAssetPaths.Remove(file.RelativePath);
                AssetFolderExclusion.FileEnabled(S.DisabledAssetFolders, file.RelativePath);
            }
            else
            {
                S.DisabledAssetPaths.Add(file.RelativePath);
            }
            InvalidateAssetPoolsAfterSelectionChange();

            _isUpdatingFolderCheckState = true;
            try
            {
                if (_selectedFolder != null)
                {
                    _selectedFolder.CheckedFileCount = Browser.Thumbnails.Count(f => f.IsChecked);
                    _selectedFolder.UpdateCheckState();
                    _selectedFolder.Parent?.UpdateCheckStateFromChildren();
                }
                UpdateAssetCounts();
            }
            finally { _isUpdatingFolderCheckState = false; }
        }

        /// <summary>DisabledAssetPaths is a set mutated in place, so nothing autosaves: save
        /// (debounced) and tell the media services their pools are stale. The mandatory-video and
        /// bubble-count schedulers deal from a queue refilled only when empty, so it is dropped here
        /// or an unticked clip keeps playing (WPF MainWindow.Assets.cs:1318, #130).</summary>
        private static void InvalidateAssetPoolsAfterSelectionChange()
        {
            CoreSettings.Save();
            AssetSelection.NotifyChanged();
            try
            {
                Overlays.MandatoryVideoOverlay.Instance.Scheduler.ReloadAssets();
                Windows.BubbleCountHost.Instance.Scheduler.ReloadAssets();
            }
            catch (Exception ex) { Log.Debug("Asset pools reload: {E}", ex.Message); }
        }

        // =====================================================================================
        //  counts
        // =====================================================================================

        private void UpdateAssetCounts()
        {
            var (_, _, activeImages, activeVideos) = CountAssets();
            Browser.CountsText = $"{activeImages} images, {activeVideos} videos active";
        }

        /// <summary>(totalImages, totalVideos, activeImages, activeVideos) over the tree (WPF CountAssetsRecursive).</summary>
        internal (int TotalImages, int TotalVideos, int ActiveImages, int ActiveVideos) CountAssets()
        {
            int ti = 0, tv = 0, ai = 0, av = 0;
            var basePath = AssetsRoot;
            void Walk(IEnumerable<AssetTreeItem> items)
            {
                foreach (var folder in items)
                {
                    if (CountPackFolder(folder) is { } pack)
                    {
                        if (pack.IsVideo) { tv += pack.Total; av += pack.Active; }
                        else { ti += pack.Total; ai += pack.Active; }
                    }
                    else if (!string.IsNullOrEmpty(folder.FullPath) && Directory.Exists(folder.FullPath))
                    {
                        string[] files;
                        try { files = Directory.GetFiles(folder.FullPath); }
                        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { files = Array.Empty<string>(); }
                        foreach (var file in files)
                        {
                            var ext = Path.GetExtension(file).ToLowerInvariant();
                            var isImage = CountImageExtensions.Contains(ext);
                            var isVideo = CountVideoExtensions.Contains(ext);
                            if (!isImage && !isVideo) continue;
                            var active = !S.DisabledAssetPaths.Contains(RelativeKey(basePath, file));
                            if (isImage) { ti++; if (active) ai++; }
                            if (isVideo) { tv++; if (active) av++; }
                        }
                    }
                    Walk(folder.Children);
                }
            }
            Walk(Browser.Folders);
            return (ti, tv, ai, av);
        }

        // =====================================================================================
        //  presets
        // =====================================================================================

        private void InitializeAssetPresets()
        {
            var presets = S.AssetPresets;
            if (!presets.Any(p => p.IsDefault))
                presets.Insert(0, AssetPreset.CreateDefault());

            var def = presets.FirstOrDefault(p => p.IsDefault);
            if (def != null)
            {
                var (ti, tv, _, _) = CountAssets();
                def.EnabledImageCount = ti;
                def.EnabledVideoCount = tv;
            }

            UpdatePresetCountsFromCurrentState();
            RefreshAssetPresetsComboBox();
        }

        private void UpdatePresetCountsFromCurrentState()
        {
            var basePath = AssetsRoot;
            if (string.IsNullOrEmpty(basePath)) return;
            foreach (var preset in S.AssetPresets.Where(p => !p.IsDefault))
            {
                int images = 0, videos = 0;
                AssetFolderExclusion.ExpandFromDisk(preset.DisabledAssetPaths, preset.DisabledAssetFolders, basePath);
                CountEnabledFilesRecursive(Path.Combine(basePath, "images"), basePath, preset.DisabledAssetPaths, CountImageExtensions, ref images);
                CountEnabledFilesRecursive(Path.Combine(basePath, "videos"), basePath, preset.DisabledAssetPaths, CountVideoExtensions, ref videos);
                var (packImages, packVideos) = CountPackFilesEnabledIn(preset.DisabledAssetPaths);
                images += packImages;
                videos += packVideos;
                if (preset.EnabledImageCount != images) preset.EnabledImageCount = images;
                if (preset.EnabledVideoCount != videos) preset.EnabledVideoCount = videos;
            }
        }

        private static void CountEnabledFilesRecursive(string path, string basePath, HashSet<string>? disabled, string[] exts, ref int count)
        {
            if (!Directory.Exists(path)) return;
            try
            {
                foreach (var file in Directory.GetFiles(path))
                {
                    if (!exts.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                    if (disabled == null || !disabled.Contains(RelativeKey(basePath, file))) count++;
                }
                foreach (var dir in Directory.GetDirectories(path))
                    CountEnabledFilesRecursive(dir, basePath, disabled, exts, ref count);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
        }

        private void RefreshAssetPresetsComboBox()
        {
            _isLoadingPreset = true;
            try
            {
                var presets = S.AssetPresets;
                CmbAssetPresets.ItemsSource = null;
                CmbAssetPresets.ItemsSource = presets;
                CmbAssetPresets.SelectedItem =
                    AssetPresetService.Find(presets, S.CurrentAssetPresetId) ?? presets.FirstOrDefault(p => p.IsDefault);
            }
            finally { _isLoadingPreset = false; }
        }

        private void CmbAssetPresets_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingPreset) return;
            if (CmbAssetPresets.SelectedItem is not AssetPreset preset) return;
            ApplyAssetPresetAndRepaint(preset.Id);
        }

        private void ApplyAssetPresetAndRepaint(string presetId)
        {
            var preset = AssetPresetService.Apply(S, presetId);
            if (preset == null) return;
            RefreshAssetTree();
            RefreshThumbnailCheckboxes();
            RefreshRemoteMediaPicker(); // a preset can carry its own niches + source (ccp-bugs #1142)
            InvalidateAssetPoolsAfterSelectionChange();
            var (_, _, ai, av) = CountAssets();
            Log.Information("Loaded asset preset: {Name} - now {Images} images and {Videos} videos active", preset.Name, ai, av);
        }

        private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

        // The preset dialogs as seams (Mich, rows-assets-tab), so a test answers them: (title, prompt,
        // default) -> text or null; (title, message) -> yes; (title, message) -> shown. Null = the
        // real owned dialogs.
        internal Func<string, string, string, Task<string?>>? AskText;
        internal Func<string, string, Task<bool>>? Confirm;
        internal Func<string, string, Task>? Inform;

        private async Task<string?> AskTextAsync(string title, string prompt, string def)
        {
            if (AskText != null) return await AskText(title, prompt, def);
            if (OwnerWindow is not { } owner) return null;
            var dlg = new Dialogs.InputDialog(title, prompt, def);
            return await dlg.ShowDialogSafe<bool?>(owner) == true ? dlg.ResultText : null;
        }

        private async Task<bool> ConfirmAsync(string title, string message, bool defaultToCancel = false)
        {
            if (Confirm != null) return await Confirm(title, message);
            return OwnerWindow is { } owner && await Dialogs.MessageDialog.ConfirmAsync(owner, title, message, defaultToCancel);
        }

        private async Task InformAsync(string title, string message)
        {
            if (Inform != null) { await Inform(title, message); return; }
            if (OwnerWindow is { } owner) await Dialogs.MessageDialog.ShowAsync(owner, title, message);
        }

        private async void BtnSaveAssetPreset_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var (_, _, images, videos) = CountAssets();
                var name = (await AskTextAsync(LocOr("title_save_asset_preset", "Save Asset Preset"),
                    "Enter a name for this preset:", $"Preset {S.AssetPresets.Count}"))?.Trim();
                if (string.IsNullOrWhiteSpace(name)) return;
                SaveAssetPreset(name, images, videos);
                await InformAsync(LocOr("title_preset_saved", "Preset Saved"),
                    $"Preset '{name}' saved!\n\n{images} images, {videos} videos enabled.\n{S.DisabledAssetPaths.Count} assets disabled.");
            }
            catch (Exception ex) { Log.Warning(ex, "Save asset preset failed"); }
        }

        /// <summary>The write half of Save As (no dialog), so a test can reach it.</summary>
        public AssetPreset SaveAssetPreset(string name, int images, int videos)
        {
            var preset = AssetPreset.FromCurrentSettings(name, images, videos);
            S.AssetPresets.Add(preset);
            S.CurrentAssetPresetId = preset.Id;
            CoreSettings.Save();
            RefreshAssetPresetsComboBox();
            InvalidateAssetPoolsAfterSelectionChange();
            return preset;
        }

        private async void BtnUpdateAssetPreset_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CmbAssetPresets.SelectedItem is not AssetPreset preset)
                {
                    await InformAsync(LocOr("title_no_preset_selected", "No Preset Selected"), Loc.Get("msg_please_select_a_preset_to_update"));
                    return;
                }
                if (preset.IsDefault)
                {
                    await InformAsync(LocOr("title_cannot_update_default", "Cannot Update Default"), Loc.Get("msg_cannot_update_the_default_all_assets_preset_n"));
                    return;
                }
                if (!await ConfirmAsync(LocOr("title_update_preset", "Update Preset"),
                        $"Update preset '{preset.Name}' with the current selection?")) return;
                var (_, _, images, videos) = CountAssets();
                preset.UpdateFromCurrentSettings(images, videos);
                CoreSettings.Save();
                RefreshAssetPresetsComboBox();
                InvalidateAssetPoolsAfterSelectionChange();
                await InformAsync(LocOr("title_preset_updated", "Preset Updated"),
                    $"Preset '{preset.Name}' updated!\n\n{images} images, {videos} videos enabled.\n{S.DisabledAssetPaths.Count} assets disabled.");
            }
            catch (Exception ex) { Log.Warning(ex, "Update asset preset failed"); }
        }

        private async void BtnDeleteAssetPreset_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CmbAssetPresets.SelectedItem is not AssetPreset preset)
                {
                    await InformAsync(LocOr("title_no_preset_selected", "No Preset Selected"), Loc.Get("msg_please_select_a_preset_to_delete"));
                    return;
                }
                if (preset.IsDefault)
                {
                    await InformAsync(LocOr("title_cannot_delete_default", "Cannot Delete Default"), Loc.Get("msg_cannot_delete_the_default_all_assets_preset"));
                    return;
                }
                if (!await ConfirmAsync(LocOr("title_delete_preset", "Delete Preset"),
                        $"Delete preset '{preset.Name}'?\n\nThis cannot be undone.", defaultToCancel: true)) return;
                S.AssetPresets.Remove(preset);
                S.CurrentAssetPresetId = S.AssetPresets.FirstOrDefault(p => p.IsDefault)?.Id;
                CoreSettings.Save();
                RefreshAssetPresetsComboBox();
                Log.Information("Deleted asset preset: {Name}", preset.Name);
            }
            catch (Exception ex) { Log.Warning(ex, "Delete asset preset failed"); }
        }
    }
}
