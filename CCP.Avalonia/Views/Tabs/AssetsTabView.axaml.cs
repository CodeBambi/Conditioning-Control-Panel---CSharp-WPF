using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Content;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/AssetsTabView.xaml.cs and the asset-browser,
    /// preset and FX halves of MainWindow/MainWindow.Assets.cs + MainWindow.AssetsFx.cs. The WPF
    /// code-behind forwards every handler to MainWindow; here the tab owns them, and the shell calls
    /// <see cref="OnTabShown"/> where WPF's ShowTab("assets") ran RefreshAssetTree +
    /// InitializeAssetPresets (MainWindow.TabNavigation.cs:433-438).
    ///
    /// ponytail: not on this head yet - the Content Packs tree node and pack cards (no
    /// ContentPackService here; the section is hidden in WPF too), the remote-media picker
    /// (needs an async consent gate, MainShellWindow.Assets.cs), video thumbnails (WPF uses the
    /// Windows shell thumbnailer; videos draw the 🎬 placeholder), BtnCreatorDiscord /
    /// PacksScrollViewer wheel-to-pan and the pack-card sheen (pack cards only).
    /// </summary>
    public partial class AssetsTabView : UserControl
    {
        // MainWindow.Assets.cs:635 (tree/thumbnail scan) and :1985 (the narrower count lists).
        internal static readonly string[] MediaExtensions = { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".avif", ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };
        private static readonly string[] CountImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
        private static readonly string[] CountVideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };

        private readonly ObservableCollection<AssetTreeItem> _assetTree = new();
        private readonly ObservableCollection<AssetThumbnailViewModel> _currentFolderFiles = new();
        private readonly SemaphoreSlim _thumbGate = new(4);
        private AssetTreeItem? _selectedFolder;
        private bool _isLoadingPreset;
        private int _thumbGeneration;
        private int _mediaLogSeenCount;
        private CancellationTokenSource? _mediaLogPulse;

        /// <summary>Every thumbnail decode started by the last folder load (tests await it).</summary>
        internal Task ThumbnailLoads { get; private set; } = Task.CompletedTask;
        internal bool MediaLogPulsing => _mediaLogPulse is { IsCancellationRequested: false };
        internal IReadOnlyList<AssetTreeItem> AssetTree => _assetTree;
        internal IReadOnlyList<AssetThumbnailViewModel> CurrentFolderFiles => _currentFolderFiles;

        // The WPF MessageBox / input-window calls, as seams: (title, prompt, default) -> text or null;
        // (title, message) -> yes; (title, message) -> shown.
        internal Func<string, string, string, Task<string?>> AskText;
        internal Func<string, string, Task<bool>> Confirm;
        internal Func<string, string, Task> Inform;

        public AssetsTabView()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: Load leaves every generated x:Name
            // field null (CLAUDE.md trap 7).
            InitializeComponent();
            DataContext = new AssetsTabViewModel();
            AssetTreeView.ItemsSource = _assetTree;
            ThumbnailsItemsControl.ItemsSource = _currentFolderFiles;
            AskText = async (title, prompt, def) =>
            {
                var dlg = new InputDialog(title, prompt, def);
                return await dlg.ShowDialogSafe<bool?>(TopLevel.GetTopLevel(this) as Window) == true ? dlg.ResultText : null;
            };
            Confirm = (title, msg) => MessageDialog.ConfirmAsync((TopLevel.GetTopLevel(this) as Window)!, title, msg);
            Inform = (title, msg) => MessageDialog.ShowAsync((TopLevel.GetTopLevel(this) as Window)!, title, msg);
            IsVisibleChanged(this);
        }

        private static void IsVisibleChanged(AssetsTabView view)
            => view.PropertyChanged += (_, e) =>
            {
                // MainWindow.AssetsFx.cs:OnAssetsTabVisibilityChanged: hidden stops the motion.
                if (e.Property == IsVisibleProperty && !view.IsVisible) view.StopMediaLogPulse(resetOpacity: true);
            };

        /// <summary>WPF ShowTab("assets"): RefreshAssetTree, InitializeAssetPresets, then the
        /// AssetsFx entrance (the Media Log pulse when entries arrived since it was last opened).</summary>
        internal void OnTabShown()
        {
            RefreshAssetTree();
            InitializeAssetPresets();
            PulseMediaLogIfUnseen();
        }

        private static AppSettings S => CoreSettings.Current;
        private static string Rel(string file) => Path.GetRelativePath(CorePaths.EffectiveAssets, file).Replace('\\', '/');
        private static bool IsMedia(string f) => MediaExtensions.Contains(Path.GetExtension(f).ToLowerInvariant());

        private static List<string> MediaFiles(string dir)
        {
            try { return Directory.GetFiles(dir).Where(IsMedia).ToList(); }
            catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException) { return new List<string>(); }
        }

        // ---- Tree (MainWindow.Assets.cs:461-772) -------------------------------------------

        internal void RefreshAssetTree()
        {
            // :467 one-time heal for extensionless media; the tree it just built predates the renames.
            AssetExtensionRepair.RunOnceInBackground(_ => Dispatcher.UIThread.Post(() =>
            {
                try { RefreshAssetTree(); }
                catch (Exception ex) { Log.Warning("AssetExtensionRepair: post-repair refresh failed: {Error}", ex.Message); }
            }));

            _assetTree.Clear();
            _selectedFolder = null;
            _currentFolderFiles.Clear();
            ShowEmpty("label_select_a_folder_to_view_its_contents");
            var assetsPath = CorePaths.EffectiveAssets;
            // Files added to an unticked folder since it was unticked stay unticked (#1231).
            if (AssetFolderExclusion.ExpandFromDisk(S, assetsPath) > 0) InvalidateAssetPoolsAfterSelectionChange();

            foreach (var name in new[] { "images", "videos" })
            {
                var dir = Path.Combine(assetsPath, name);
                if (!Directory.Exists(dir)) continue;
                var node = BuildFolderTree(dir, name);
                node.IsExpanded = true;
                _assetTree.Add(node);
            }
            UpdateAssetCounts();
        }

        private static AssetTreeItem BuildFolderTree(string path, string name)
        {
            var node = new AssetTreeItem { Name = name, FullPath = path, IsChecked = true };
            var files = MediaFiles(path);
            node.FileCount = files.Count;
            node.CheckedFileCount = files.Count(f => !S.DisabledAssetPaths.Contains(Rel(f)));
            string[] subDirs;
            try { subDirs = Directory.GetDirectories(path); }
            catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException) { subDirs = Array.Empty<string>(); }
            foreach (var dir in subDirs)
            {
                var child = BuildFolderTree(dir, Path.GetFileName(dir));
                child.Parent = node;
                node.Children.Add(child);
            }
            node.UpdateCheckState();
            return node;
        }

        private void AssetTreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0 || e.AddedItems[0] is not AssetTreeItem folder) return;
            SelectFolder(folder);
        }

        internal void SelectFolder(AssetTreeItem folder)
        {
            _selectedFolder = folder;
            if (!string.IsNullOrEmpty(folder.FullPath))
            {
                LoadFolderThumbnails(folder.FullPath);
                RecalculateFolderCheckState(folder);
            }
            else
            {
                _currentFolderFiles.Clear();
                ShowEmpty("label_select_a_subfolder_to_view_files");
            }
        }

        private static void RecalculateFolderCheckState(AssetTreeItem folder)
        {
            if (!string.IsNullOrEmpty(folder.FullPath) && Directory.Exists(folder.FullPath))
                folder.CheckedFileCount = MediaFiles(folder.FullPath).Count(f => !S.DisabledAssetPaths.Contains(Rel(f)));
            foreach (var child in folder.Children) RecalculateFolderCheckState(child);
            folder.UpdateCheckState();
        }

        private void ShowEmpty(string? key)
        {
            TxtThumbnailsEmpty.IsVisible = key != null;
            if (key != null) TxtThumbnailsEmpty.Text = Loc.Get(key);
        }

        private void LoadFolderThumbnails(string folderPath)
        {
            _currentFolderFiles.Clear();
            var generation = ++_thumbGeneration;
            ShowEmpty(null);
            if (!Directory.Exists(folderPath)) { ShowEmpty("label_folder_does_not_exist"); return; }
            var files = MediaFiles(folderPath).OrderBy(Path.GetFileName).ToList();
            if (files.Count == 0) { ShowEmpty("label_no_media_files_in_this_folder"); return; }

            var loads = new List<Task>(files.Count);
            foreach (var file in files)
            {
                var rel = Rel(file);
                var item = new AssetThumbnailViewModel(file, rel) { IsChecked = !S.DisabledAssetPaths.Contains(rel) };
                _currentFolderFiles.Add(item);
                if (!item.IsVideo) loads.Add(LoadThumbnailAsync(item, generation));
            }
            ThumbnailLoads = Task.WhenAll(loads);
        }

        /// <summary>WPF decodes at DecodePixelWidth 100 off the UI thread; same here, four at a time,
        /// and a folder switch drops the old folder's queued decodes.</summary>
        private async Task LoadThumbnailAsync(AssetThumbnailViewModel item, int generation)
        {
            item.IsLoadingThumbnail = true;
            try
            {
                var bmp = await Task.Run(async () =>
                {
                    await _thumbGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        if (generation != Volatile.Read(ref _thumbGeneration)) return null;
                        using var fs = File.OpenRead(item.FullPath);
                        return Bitmap.DecodeToWidth(fs, 100);
                    }
                    catch { return null; }   // unreadable/unsupported: no thumbnail, as WPF
                    finally { _thumbGate.Release(); }
                });
                if (bmp != null) await Dispatcher.UIThread.InvokeAsync(() => item.Thumbnail = bmp);
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => item.IsLoadingThumbnail = false);
            }
        }

        // ---- Checks (MainWindow.Assets.cs:1018-1404) ---------------------------------------

        /// <summary>Click, not IsCheckedChanged: only a user toggle (pointer or Space) writes the
        /// disabled set; binding refreshes and our own repaints never do (WPF's re-entrancy flag).</summary>
        private void FolderCheckBox_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control { DataContext: AssetTreeItem folder }) SetFolderEnabled(folder, folder.IsChecked);
        }

        internal void SetFolderEnabled(AssetTreeItem folder, bool enabled)
        {
            UpdateFolderFilesCheckState(folder, enabled);
            MarkFolderExclusion(folder, enabled);
            folder.SetCheckedRecursive(enabled);
            folder.Parent?.UpdateCheckStateFromChildren();
            RefreshThumbnailCheckboxes();
            UpdateAssetCounts();
            InvalidateAssetPoolsAfterSelectionChange();
        }

        private static void MarkFolderExclusion(AssetTreeItem folder, bool isChecked)
        {
            if (folder.IsPackFolder || string.IsNullOrEmpty(folder.FullPath)) return;
            AssetFolderExclusion.MarkFolder(S.DisabledAssetFolders, Path.GetRelativePath(CorePaths.EffectiveAssets, folder.FullPath), isChecked);
        }

        private static void UpdateFolderFilesCheckState(AssetTreeItem folder, bool isChecked)
        {
            if (Directory.Exists(folder.FullPath))
                foreach (var file in MediaFiles(folder.FullPath))
                {
                    if (isChecked) S.DisabledAssetPaths.Remove(Rel(file));
                    else S.DisabledAssetPaths.Add(Rel(file));
                }
            foreach (var child in folder.Children) UpdateFolderFilesCheckState(child, isChecked);
        }

        private void RefreshThumbnailCheckboxes()
        {
            foreach (var item in _currentFolderFiles) item.IsChecked = !S.DisabledAssetPaths.Contains(item.RelativePath);
        }

        private void ThumbnailCheckBox_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control { DataContext: AssetThumbnailViewModel file }) UpdateFileCheckState(file);
        }

        /// <summary>WPF ThumbnailItem_Click: double-click previews, single click toggles.</summary>
        private void ThumbnailItem_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control { DataContext: AssetThumbnailViewModel file } c) return;
            if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) { OpenAssetPreview(file); e.Handled = true; return; }
            ToggleFile(file);
        }

        internal void ToggleFile(AssetThumbnailViewModel file)
        {
            file.IsChecked = !file.IsChecked;
            UpdateFileCheckState(file);
        }

        private void UpdateFileCheckState(AssetThumbnailViewModel file)
        {
            if (file.IsChecked)
            {
                S.DisabledAssetPaths.Remove(file.RelativePath);
                AssetFolderExclusion.FileEnabled(S.DisabledAssetFolders, file.RelativePath);
            }
            else S.DisabledAssetPaths.Add(file.RelativePath);
            InvalidateAssetPoolsAfterSelectionChange();
            if (_selectedFolder != null)
            {
                _selectedFolder.CheckedFileCount = _currentFolderFiles.Count(f => f.IsChecked);
                _selectedFolder.UpdateCheckState();
            }
            UpdateAssetCounts();
        }

        private void ThumbnailItem_Preview_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control { DataContext: AssetThumbnailViewModel file }) OpenAssetPreview(file);
        }

        private async void ThumbnailItem_OpenInExplorer_Click(object? sender, RoutedEventArgs e)
        {
            // WPF ExplorerLauncher.RevealInExplorer selects the file; the portable open shows its folder.
            if (sender is Control { DataContext: AssetThumbnailViewModel file })
                await Platform.ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), Path.GetDirectoryName(file.FullPath));
        }

        private void OpenAssetPreview(AssetThumbnailViewModel file)
        {
            try
            {
                if (!File.Exists(file.FullPath)) { Log.Warning("File not found for preview: {File}", file.FullPath); return; }
                var win = new Windows.MiniPlayerWindow();
                win.LoadFile(file.FullPath);
                if (TopLevel.GetTopLevel(this) is Window owner) win.Show(owner); else win.Show();
            }
            catch (Exception ex) { Log.Error(ex, "Failed to open asset preview for {File}", file.Name); }
        }

        /// <summary>The media services read DisabledAssetPaths live on this head (FlashOverlay,
        /// MandatoryVideoScheduler.LocalLibrary), so WPF's cache drops reduce to the save.</summary>
        private static void InvalidateAssetPoolsAfterSelectionChange() => CoreSettings.Save();

        private void BtnSelectAllAssets_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectedFolder != null) SetFolderEnabled(_selectedFolder, true);
        }

        private void BtnDeselectAllAssets_Click(object? sender, RoutedEventArgs e)
        {
            if (_selectedFolder != null) SetFolderEnabled(_selectedFolder, false);
        }

        /// <summary>MainWindow.UiUpdates.cs:2384: rescan, then confirm.</summary>
        private async void BtnRefreshAssets_Click(object? sender, RoutedEventArgs e)
        {
            RefreshAssetTree();
            await Inform(Loc.Get("title_success"), Loc.Get("msg_assets_refreshed"));
        }

        private async void BtnGetPacks_Click(object? sender, RoutedEventArgs e)
            => await Platform.ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), DiscordLinks.PackCatalogue);

        private void UpdateAssetCounts()
        {
            var (images, videos) = ActiveCounts(_assetTree);
            TxtAssetCounts.Text = Loc.GetF("label_0_images_1_videos_active", images, videos);
        }

        /// <summary>CountAssetsRecursive's active half (MainWindow.Assets.cs:1983).</summary>
        internal static (int Images, int Videos) ActiveCounts(IEnumerable<AssetTreeItem> items, bool all = false)
        {
            int images = 0, videos = 0;
            foreach (var folder in items)
            {
                if (Directory.Exists(folder.FullPath))
                {
                    string[] files;
                    try { files = Directory.GetFiles(folder.FullPath); } catch { files = Array.Empty<string>(); }
                    foreach (var file in files)
                    {
                        if (!all && S.DisabledAssetPaths.Contains(Rel(file))) continue;
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (CountImageExtensions.Contains(ext)) images++;
                        if (CountVideoExtensions.Contains(ext)) videos++;
                    }
                }
                var (ci, cv) = ActiveCounts(folder.Children, all);
                images += ci; videos += cv;
            }
            return (images, videos);
        }

        // ---- Presets (MainWindow.Assets.cs:1406-1817) ---------------------------------------

        internal void InitializeAssetPresets()
        {
            var presets = S.AssetPresets;
            if (!presets.Any(p => p.IsDefault)) presets.Insert(0, AssetPreset.CreateDefault());
            if (presets.FirstOrDefault(p => p.IsDefault) is { } def)
                (def.EnabledImageCount, def.EnabledVideoCount) = ActiveCounts(_assetTree, all: true);
            RefreshAssetPresetsComboBox();
            UpdatePresetCountsFromCurrentState();
        }

        private static void UpdatePresetCountsFromCurrentState()
        {
            var basePath = CorePaths.EffectiveAssets;
            foreach (var preset in S.AssetPresets.Where(p => !p.IsDefault))
            {
                AssetFolderExclusion.ExpandFromDisk(preset.DisabledAssetPaths, preset.DisabledAssetFolders, basePath);
                int Count(string sub, string[] exts)
                {
                    var dir = Path.Combine(basePath, sub);
                    if (!Directory.Exists(dir)) return 0;
                    try
                    {
                        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count(f =>
                            exts.Contains(Path.GetExtension(f).ToLowerInvariant())
                            && preset.DisabledAssetPaths?.Contains(Rel(f)) != true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
                }
                var images = Count("images", CountImageExtensions);
                var videos = Count("videos", CountVideoExtensions);
                if (preset.EnabledImageCount != images) preset.EnabledImageCount = images;
                if (preset.EnabledVideoCount != videos) preset.EnabledVideoCount = videos;
            }
        }

        private void RefreshAssetPresetsComboBox()
        {
            _isLoadingPreset = true;
            try
            {
                CmbAssetPresets.ItemsSource = null;
                CmbAssetPresets.ItemsSource = S.AssetPresets;
                CmbAssetPresets.SelectedItem = AssetPresetService.Find(S.AssetPresets, S.CurrentAssetPresetId)
                    ?? S.AssetPresets.FirstOrDefault(p => p.IsDefault);
            }
            finally { _isLoadingPreset = false; }
        }

        private void CmbAssetPresets_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingPreset || CmbAssetPresets.SelectedItem is not AssetPreset preset) return;
            ApplyAssetPresetAndRepaint(preset.Id);
        }

        internal void ApplyAssetPresetAndRepaint(string presetId)
        {
            var preset = AssetPresetService.Apply(S, presetId);
            if (preset == null) return;
            RefreshAssetTree();
            RefreshThumbnailCheckboxes();
            InvalidateAssetPoolsAfterSelectionChange();
            Log.Information("Loaded asset preset: {Name}", preset.Name);
        }

        internal async Task SaveAssetPresetAsync()
        {
            var (images, videos) = ActiveCounts(_assetTree);
            var name = await AskText(Loc.Get("title_save_asset_preset"), Loc.Get("msg_enter_a_name_for_your_preset"),
                Loc.GetF("label_asset_preset_default_0", S.AssetPresets.Count));
            if (string.IsNullOrWhiteSpace(name)) return;
            var disabled = S.DisabledAssetPaths.Count;
            var preset = AssetPreset.FromCurrentSettings(name.Trim(), images, videos);
            S.AssetPresets.Add(preset);
            S.CurrentAssetPresetId = preset.Id;
            CoreSettings.Save();
            RefreshAssetPresetsComboBox();
            await Inform(Loc.Get("title_preset_saved"), Loc.GetF("msg_asset_preset_saved_0", preset.Name, images, videos, disabled));
        }

        internal async Task UpdateAssetPresetAsync()
        {
            if (CmbAssetPresets.SelectedItem is not AssetPreset preset)
            { await Inform(Loc.Get("title_no_preset_selected"), Loc.Get("msg_please_select_a_preset_to_update")); return; }
            if (preset.IsDefault)
            { await Inform(Loc.Get("title_cannot_update_default"), Loc.Get("msg_cannot_update_the_default_all_assets_preset_n")); return; }
            if (!await Confirm(Loc.Get("title_update_preset"), Loc.GetF("msg_update_asset_preset_confirm_0", preset.Name))) return;
            var (images, videos) = ActiveCounts(_assetTree);
            var disabled = S.DisabledAssetPaths.Count;
            preset.UpdateFromCurrentSettings(images, videos);
            CoreSettings.Save();
            RefreshAssetPresetsComboBox();
            await Inform(Loc.Get("title_preset_updated"), Loc.GetF("msg_asset_preset_updated_0", preset.Name, images, videos, disabled));
        }

        internal async Task DeleteAssetPresetAsync()
        {
            if (CmbAssetPresets.SelectedItem is not AssetPreset preset)
            { await Inform(Loc.Get("title_no_preset_selected"), Loc.Get("msg_please_select_a_preset_to_delete")); return; }
            if (preset.IsDefault)
            { await Inform(Loc.Get("title_cannot_delete_default"), Loc.Get("msg_cannot_delete_the_default_all_assets_preset")); return; }
            if (!await Confirm(Loc.Get("title_delete_preset"), Loc.GetF("msg_delete_preset_confirm_0", preset.Name))) return;
            S.AssetPresets.Remove(preset);
            S.CurrentAssetPresetId = S.AssetPresets.FirstOrDefault(p => p.IsDefault)?.Id;
            CoreSettings.Save();
            RefreshAssetPresetsComboBox();
        }

        private async void BtnSaveAssetPreset_Click(object? sender, RoutedEventArgs e) => await SaveAssetPresetAsync();
        private async void BtnUpdateAssetPreset_Click(object? sender, RoutedEventArgs e) => await UpdateAssetPresetAsync();
        private async void BtnDeleteAssetPreset_Click(object? sender, RoutedEventArgs e) => await DeleteAssetPresetAsync();

        // ---- FX (MainWindow.AssetsFx.cs) -----------------------------------------------------

        private static bool MotionAllowed()
        {
            try { return AmbientFxCanvas.Env.AllowTransitions; }
            catch { return true; }
        }

        /// <summary>AssetsFx OnAssetTreeRowHover: a 3 px, 130 ms ease-out slide.</summary>
        private void AssetTreeRow_PointerEntered(object? sender, PointerEventArgs e) => NudgeRow(sender as Control, true);
        private void AssetTreeRow_PointerExited(object? sender, PointerEventArgs e) => NudgeRow(sender as Control, false);

        internal static void NudgeRow(Control? row, bool on)
        {
            if (row == null) return;
            if (row.RenderTransform is not TranslateTransform slide)
            {
                if (row.RenderTransform != null) return;   // someone else's transform: leave it
                slide = new TranslateTransform();
                row.RenderTransform = slide;
            }
            slide.Transitions = MotionAllowed()
                ? new Transitions { new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(130), Easing = new QuadraticEaseOut() } }
                : null;
            slide.X = on ? 3.0 : 0;
        }

        /// <summary>AssetsFx PulseMediaLogIfUnseen: three 0.45 s dips to 0.45 opacity and back,
        /// only when the log grew since it was last opened. Finite; stopped on hide or click.</summary>
        private void PulseMediaLogIfUnseen()
        {
            if ((App.MediaHistory?.Count ?? 0) <= _mediaLogSeenCount || !MotionAllowed() || MediaLogPulsing) return;
            var cts = new CancellationTokenSource();
            _mediaLogPulse = cts;
            var anim = new Animation
            {
                Duration = TimeSpan.FromSeconds(0.9),
                IterationCount = new IterationCount(3),
                Easing = new SineEaseInOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1.0) } },
                    new KeyFrame { Cue = new Cue(0.5), Setters = { new Setter(OpacityProperty, 0.45) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1.0) } },
                },
            };
            _ = anim.RunAsync(BtnMediaLog, cts.Token).ContinueWith(_ =>
            {
                if (ReferenceEquals(_mediaLogPulse, cts)) _mediaLogPulse = null;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void StopMediaLogPulse(bool resetOpacity)
        {
            _mediaLogPulse?.Cancel();
            _mediaLogPulse = null;
            if (resetOpacity) BtnMediaLog.Opacity = 1.0;
        }

        /// <summary>
        /// The Media Log. The one WPF handler in this file that is not a forward - it builds the
        /// window itself - and the window is ported. Non-modal and owned, exactly as on WPF; an
        /// owner is only set when there is a real TopLevel, because the render harness hosts this
        /// view without a Window.
        /// </summary>
        private void BtnMediaLog_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // AssetsFx MediaLogButton_Clicked: opening the log marks it seen and ends the pulse.
                _mediaLogSeenCount = App.MediaHistory?.Count ?? 0;
                StopMediaLogPulse(resetOpacity: true);
                var win = new Views.Windows.MediaHistoryWindow();
                if (TopLevel.GetTopLevel(this) is Window owner) win.Show(owner);
                else win.Show();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open Media Log window");
            }
        }

        /// <summary>
        /// Opens the assets folder. WPF's MainWindow.Assets.cs:41 creates images/ and videos/ under
        /// App.EffectiveAssetsPath and hands the path to explorer.exe; CorePaths.EffectiveAssets is
        /// that path on this head and the Launcher is the portable shell open.
        ///
        /// <para>The "open_assets" bark fires first, exactly where WPF fires it, through
        /// <see cref="CoreBark"/>. Unseeded on this head, so it is silent here and voiced on
        /// Windows.</para>
        /// </summary>
        private async void BtnOpenAssetsFolder_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                CoreBark.NotifyUiAction("open_assets");
                var assets = CorePaths.EffectiveAssets;
                Directory.CreateDirectory(Path.Combine(assets, "images"));
                Directory.CreateDirectory(Path.Combine(assets, "videos"));
                await Platform.ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), assets);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open the assets folder");
            }
        }
    }

    /// <summary>
    /// The hidden packs strip's placeholder cards (ContentPackService is not on this head; the
    /// section is IsVisible="False" in WPF too). The asset browser and presets are filled from
    /// code-behind, not from here.
    /// </summary>
    public sealed class AssetsTabViewModel
    {
        public IReadOnlyList<PackCardViewModel> Packs { get; } = new[]
        {
            new PackCardViewModel { Name = "Starter Pack", Description = "A small first-run set: soft imagery and two short loops.", SizeDisplay = "42 MB", ImageCount = 120, VideoCount = 4, IsDownloaded = true },
            new PackCardViewModel { Name = "Deep Focus", Description = "Slow spirals and long-form video for extended sessions.", SizeDisplay = "310 MB", ImageCount = 640, VideoCount = 22 },
            new PackCardViewModel { Name = "Community Mix", Description = "Hosted off-site; download it yourself and drop it in.", SizeDisplay = "1.2 GB", ImageCount = 2100, VideoCount = 90, IsExternal = true },
        };
    }

    /// <summary>One card in the (hidden) content-packs strip.</summary>
    public sealed class PackCardViewModel
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string SizeDisplay { get; set; } = "";
        public int ImageCount { get; set; }
        public int VideoCount { get; set; }
        public bool IsDownloaded { get; set; }
        public bool IsExternal { get; set; }
        public bool IsDownloading { get; set; }
        public double DownloadProgress { get; set; }

        /// <summary>WPF used a MultiBinding with StringFormat "{0} images, {1} videos"; Avalonia has
        /// no MultiBinding StringFormat, and the string was hardcoded English there too.</summary>
        public string CountsDisplay => $"{ImageCount} images, {VideoCount} videos";

        public bool ShowExternalButtons => IsExternal && !IsDownloaded;
        public bool IsNotDownloading => !IsDownloading;
        public string DownloadButtonText => IsDownloaded ? "Uninstall" : "Install";
        public string ActivateButtonText => "Deactivate";

        // IImage, not a URL string: Avalonia will not convert one, and pack:// is WPF-only. Null
        // until the pack service (and its image cache) moves to Core - the "No Preview" branch
        // is what draws meanwhile, which is also the honest state for a pack with no preview.
        public IImage? CurrentPreviewImage => null;
        public IImage? PreviewImage => null;
        public bool HasPreviewImages => false;
        public bool HasAnyPreview => false;
    }

    /// <summary>One tile in the thumbnail grid (WPF Models/AssetFileItem, with an Avalonia bitmap).</summary>
    public sealed class AssetThumbnailViewModel : INotifyPropertyChanged
    {
        private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".m4v", ".flv" };
        private bool _isChecked;
        private bool _isLoadingThumbnail;
        private IImage? _thumbnail;

        public AssetThumbnailViewModel(string fullPath, string relativePath)
        {
            FullPath = fullPath;
            RelativePath = relativePath;
            Name = Path.GetFileName(fullPath);
            IsVideo = VideoExtensions.Contains(Path.GetExtension(fullPath).ToLowerInvariant());
        }

        public string FullPath { get; }
        public string RelativePath { get; }
        public string Name { get; }
        public bool IsVideo { get; }
        public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
        public bool IsLoadingThumbnail { get => _isLoadingThumbnail; set => Set(ref _isLoadingThumbnail, value); }
        public IImage? Thumbnail { get => _thumbnail; set => Set(ref _thumbnail, value); }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
