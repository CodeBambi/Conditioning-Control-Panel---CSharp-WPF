using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Assets & Packs tab: asset folder management, content packs, asset/phrase presets (nested).
    public partial class MainWindow
    {
        #region Assets & Packs Tab

        private ObservableCollection<AssetTreeItem> _assetTree = new();
        private ObservableCollection<AssetFileItem> _currentFolderFiles = new();
        private AssetTreeItem? _selectedFolder;

        private void BtnAssets_Click(object sender, RoutedEventArgs e) => ShowTab("assets");

        internal void BtnOpenAssetsFolder_Click(object sender, RoutedEventArgs e)
        {
            try { App.Bark?.NotifyUiAction("open_assets"); } catch { }
            var assetsPath = App.EffectiveAssetsPath;
            Directory.CreateDirectory(Path.Combine(assetsPath, "images"));
            Directory.CreateDirectory(Path.Combine(assetsPath, "videos"));
            Process.Start("explorer.exe", assetsPath);
        }

        internal void BtnDeleteDownloadedPacks_Click(object sender, RoutedEventArgs e)
        {
            var installedIds = App.Settings?.Current?.InstalledPackIds;
            if (installedIds == null || installedIds.Count == 0)
            {
                MessageBox.Show(Loc.Get("msg_no_downloaded_packs_to_delete"), Loc.Get("title_delete_downloaded_packs"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                Loc.GetF("msg_delete_downloaded_packs_confirm_0", installedIds.Count),
                Loc.Get("title_delete_downloaded_packs"), MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            foreach (var packId in installedIds.ToList())
                App.ContentPacks?.UninstallPack(packId);

            RefreshAssetTree();
            App.Flash?.LoadAssets();
            App.Video?.ReloadAssets();
            App.BubbleCount?.ReloadAssets();
            MessageBox.Show(Loc.Get("msg_all_downloaded_packs_have_been_deleted_nyour"), Loc.Get("btn_done"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task RefreshPacksAsync()
        {
            try
            {
                // Fetch packs from server (with fallback to built-in)
                var packs = await App.ContentPacks?.GetAvailablePacksAsync() ?? new List<ContentPack>();

                // Set static preview images for original packs (always use embedded resources)
                foreach (var pack in packs)
                {
                    if (pack.Id == "basic-bimbo-starter")
                        pack.PreviewImageUrl = "pack://application:,,,/Resources/pack1.png";
                    else if (pack.Id == "enhanced-bimbodoll-video")
                        pack.PreviewImageUrl = "pack://application:,,,/Resources/pack2.png";
                }

                // Update the observable collection
                _availablePacks.Clear();
                foreach (var pack in packs)
                {
                    _availablePacks.Add(pack);
                }

                // Bind to ItemsControl
                AssetsTab.PackCardsItemsControl.ItemsSource = _availablePacks;

                // Force ScrollViewer to recalculate after items are loaded
                AssetsTab.PacksScrollViewer?.InvalidateMeasure();
                AssetsTab.PacksScrollViewer?.UpdateLayout();

                // Load preview images for all packs
                var loadTasks = new List<Task>();
                foreach (var pack in packs)
                {
                    if (pack.IsDownloaded)
                    {
                        // Load from local encrypted files for installed packs
                        loadTasks.Add(Task.Run(() =>
                        {
                            try
                            {
                                var previewImages = App.ContentPacks?.GetPackPreviewImages(pack.Id, 10, 240, 100);
                                if (previewImages != null && previewImages.Count > 0)
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        pack.PreviewImages = previewImages;
                                        pack.CurrentPreviewIndex = 0;
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Debug("Failed to load preview images for {PackId}: {Error}", pack.Id, ex.Message);
                            }
                        }));
                    }
                    else if (pack.PreviewUrls?.Count > 0)
                    {
                        // Load from server URLs for non-installed packs
                        loadTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                var previewImages = await LoadPreviewImagesFromUrlsAsync(pack.Id, pack.PreviewUrls);
                                if (previewImages.Count > 0)
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        pack.PreviewImages = previewImages;
                                        pack.CurrentPreviewIndex = 0;
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                App.Logger?.Debug("Failed to load preview images from URLs for {PackId}: {Error}", pack.Id, ex.Message);
                            }
                        }));
                    }
                }
                await Task.WhenAll(loadTasks);

                // Start preview rotation timer
                StartPackPreviewRotation();

                // Subscribe to pack events for progress updates
                if (App.ContentPacks != null)
                {
                    App.ContentPacks.PackDownloadProgress -= OnPackDownloadProgress;
                    App.ContentPacks.PackDownloadProgress += OnPackDownloadProgress;
                    App.ContentPacks.PackDownloadCompleted -= OnPackDownloadCompleted;
                    App.ContentPacks.PackDownloadCompleted += OnPackDownloadCompleted;
                    App.ContentPacks.AuthenticationRequired -= OnPackAuthenticationRequired;
                    App.ContentPacks.AuthenticationRequired += OnPackAuthenticationRequired;
                    App.ContentPacks.RateLimitExceeded -= OnPackRateLimitExceeded;
                    App.ContentPacks.RateLimitExceeded += OnPackRateLimitExceeded;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to refresh packs");
            }
        }

        private void StartPackPreviewRotation()
        {
            // Stop existing timer if running
            _packPreviewTimer?.Stop();

            // Create timer to rotate preview images every 1 second
            _packPreviewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _packPreviewTimer.Tick += (s, e) =>
            {
                try
                {
                    foreach (var pack in _availablePacks.Where(p => p.HasPreviewImages))
                    {
                        pack.AdvancePreviewImage();
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Error rotating pack previews: {Error}", ex.Message);
                }
            };
            _packPreviewTimer.Start();
        }

        private void StopPackPreviewRotation()
        {
            _packPreviewTimer?.Stop();
            _packPreviewTimer = null;
        }

        private async Task<List<BitmapImage>> LoadPreviewImagesFromUrlsAsync(string packId, List<string> urls)
        {
            var images = new List<BitmapImage>();
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ConditioningControlPanel", "pack-previews", packId);
            Directory.CreateDirectory(cacheDir);

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            foreach (var url in urls)
            {
                try
                {
                    var fileName = GetPackPreviewFileName(url);
                    var localPath = Path.Combine(cacheDir, fileName);
                    byte[] bytes;

                    if (File.Exists(localPath))
                    {
                        bytes = await File.ReadAllBytesAsync(localPath);
                    }
                    else
                    {
                        bytes = await httpClient.GetByteArrayAsync(url);
                        await File.WriteAllBytesAsync(localPath, bytes);
                    }

                    var bitmap = new BitmapImage();
                    using (var stream = new MemoryStream(bytes))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                    }
                    bitmap.Freeze(); // Required for cross-thread access
                    images.Add(bitmap);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to load preview image from {Url}: {Error}", url, ex.Message);
                }
            }

            return images;
        }

        private static string GetPackPreviewFileName(string url)
        {
            try { return Path.GetFileName(new Uri(url).LocalPath); }
            catch { return "image.png"; }
        }

        private void OnPackDownloadProgress(object? sender, (ContentPack Pack, int Progress) e)
        {
            // Progress is bound via INotifyPropertyChanged, no manual UI update needed
            // Just update the pack's download progress property
            Dispatcher.Invoke(() =>
            {
                e.Pack.DownloadProgress = e.Progress;
            });
        }

        private void OnPackDownloadCompleted(object? sender, ContentPack pack)
        {
            Dispatcher.Invoke(async () =>
            {
                // Properties are bound, just ensure state is correct
                pack.IsDownloaded = true;
                pack.IsDownloading = false;

                // Load preview images for the newly installed pack
                try
                {
                    var previewImages = await Task.Run(() =>
                        App.ContentPacks?.GetPackPreviewImages(pack.Id, 10, 240, 100));
                    if (previewImages != null && previewImages.Count > 0)
                    {
                        pack.PreviewImages = previewImages;
                        pack.CurrentPreviewIndex = 0;
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("Failed to load preview images after install: {Error}", ex.Message);
                }

                RefreshAssetTree();

                // Refresh pack list so button states update (the event's pack instance
                // may differ from the one in _availablePacks)
                await RefreshPacksAsync();
            });
        }

        private void OnPackAuthenticationRequired(object? sender, string message)
        {
            Dispatcher.Invoke(() =>
            {
                // Show login prompt — direct to appropriate login method
                if (App.HasCloudIdentity)
                {
                    MessageBox.Show(message, Loc.Get("title_authentication_required"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(Loc.GetF("msg_0_n_nplease_log_in_from_the_settings_tab", message), Loc.Get("title_login_required"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
            });
        }

        private void OnPackRateLimitExceeded(object? sender, (ContentPack Pack, string Message, DateTime ResetTime) e)
        {
            Dispatcher.Invoke(() =>
            {
                // Reset pack state (bound via INotifyPropertyChanged)
                e.Pack.IsDownloading = false;

                // Calculate time until reset
                var timeUntilReset = e.ResetTime - DateTime.UtcNow;
                var hoursText = timeUntilReset.TotalHours > 1
                    ? Loc.GetF("label_0_hours", (int)timeUntilReset.TotalHours)
                    : Loc.GetF("label_0_minutes", (int)timeUntilReset.TotalMinutes);

                MessageBox.Show(
                    Loc.GetF("msg_download_limit_reached_0_1", e.Message, hoursText),
                    Loc.Get("title_download_limit_reached"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        }

        internal void BtnCreatorDiscord_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://discord.gg/YxVAMt4qaZ") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open Discord link");
            }
        }

        internal void BtnGetPacks_Click(object sender, RoutedEventArgs e)
        {
            BtnCreatorDiscord_Click(sender, e);
        }

        internal void PacksScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Enable horizontal scrolling with mouse wheel
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        /// <summary>
        /// Handler for horizontal-only ScrollViewers that bubbles vertical scroll events to parent.
        /// Prevents "dead zones" where scrolling doesn't work.
        /// </summary>
        internal void HorizontalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Find the parent ScrollViewer and pass the scroll event to it
            if (sender is not DependencyObject element) return;

            // Walk up the visual tree to find the parent ScrollViewer
            var parent = VisualTreeHelper.GetParent(element);
            while (parent != null && parent is not ScrollViewer)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is ScrollViewer parentScrollViewer)
            {
                // Create a new event with the same delta and raise it on the parent
                var eventArgs = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = MouseWheelEvent,
                    Source = sender
                };
                parentScrollViewer.RaiseEvent(eventArgs);
                e.Handled = true;
            }
        }

        internal void InnerScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                var parent = VisualTreeHelper.GetParent(sv) as DependencyObject;
                while (parent != null && parent is not ScrollViewer)
                    parent = VisualTreeHelper.GetParent(parent);
                if (parent is ScrollViewer parentSv)
                {
                    parentSv.ScrollToVerticalOffset(parentSv.VerticalOffset - e.Delta);
                    e.Handled = true;
                }
            }
        }

        internal void BtnRefreshPacks_Click(object sender, RoutedEventArgs e) => _ = RefreshPacksAsync();

        private void RefreshAssetTree()
        {
            _assetTree.Clear();
            var assetsPath = App.EffectiveAssetsPath;

            // Build tree for images folder
            var imagesFolder = Path.Combine(assetsPath, "images");
            if (Directory.Exists(imagesFolder))
            {
                var imagesNode = BuildFolderTree(imagesFolder, "images");
                imagesNode.IsExpanded = true;
                _assetTree.Add(imagesNode);
            }

            // Build tree for videos folder
            var videosFolder = Path.Combine(assetsPath, "videos");
            if (Directory.Exists(videosFolder))
            {
                var videosNode = BuildFolderTree(videosFolder, "videos");
                videosNode.IsExpanded = true;
                _assetTree.Add(videosNode);
            }

            // Add content pack virtual folders for active packs
            var activePackIds = App.ContentPacks?.GetActivePackIds() ?? new List<string>();
            if (activePackIds.Count > 0)
            {
                var packsNode = new AssetTreeItem
                {
                    Name = "📦 Content Packs",
                    FullPath = "",
                    IsChecked = true,
                    IsPackFolder = true,
                    IsExpanded = true
                };

                foreach (var packId in activePackIds)
                {
                    var packNode = BuildPackTree(packId);
                    if (packNode != null)
                    {
                        packNode.Parent = packsNode;
                        packsNode.Children.Add(packNode);
                    }
                }

                if (packsNode.Children.Count > 0)
                {
                    packsNode.FileCount = packsNode.Children.Sum(c => c.FileCount);
                    packsNode.CheckedFileCount = packsNode.Children.Sum(c => c.GetTotalCheckedFileCount());
                    packsNode.IsChecked = packsNode.CheckedFileCount > 0;
                    _assetTree.Add(packsNode);
                }
            }

            AssetsTab.AssetTreeView.ItemsSource = _assetTree;
            UpdateAssetCounts();
        }

        private AssetTreeItem? BuildPackTree(string packId)
        {
            var packFiles = App.ContentPacks?.GetPackFiles(packId);
            if (packFiles == null || packFiles.Count == 0)
                return null;

            // Get pack name from built-in packs
            var packs = App.ContentPacks?.GetBuiltInPacks();
            var packInfo = packs?.FirstOrDefault(p => p.Id == packId);
            var packName = packInfo?.Name ?? packId;

            var packNode = new AssetTreeItem
            {
                Name = packName,
                FullPath = "",
                IsPackFolder = true,
                PackId = packId,
                IsChecked = true,
                IsExpanded = false
            };

            // Images subfolder
            var imageFiles = packFiles.Where(f => f.FileType == "image").ToList();
            if (imageFiles.Count > 0)
            {
                // Count active images (not in DisabledAssetPaths)
                var activeImageCount = imageFiles.Count(f =>
                    !App.Settings.Current.DisabledAssetPaths.Contains($"pack:{packId}/{f.OriginalName}"));

                var imagesNode = new AssetTreeItem
                {
                    Name = "images",
                    FullPath = "",
                    IsPackFolder = true,
                    PackId = packId,
                    PackFileType = "image",
                    IsChecked = activeImageCount > 0,
                    FileCount = imageFiles.Count,
                    CheckedFileCount = activeImageCount,
                    Parent = packNode
                };
                packNode.Children.Add(imagesNode);
            }

            // Videos subfolder
            var videoFiles = packFiles.Where(f => f.FileType == "video").ToList();
            if (videoFiles.Count > 0)
            {
                // Count active videos (not in DisabledAssetPaths)
                var activeVideoCount = videoFiles.Count(f =>
                    !App.Settings.Current.DisabledAssetPaths.Contains($"pack:{packId}/{f.OriginalName}"));

                var videosNode = new AssetTreeItem
                {
                    Name = "videos",
                    FullPath = "",
                    IsPackFolder = true,
                    PackId = packId,
                    PackFileType = "video",
                    IsChecked = activeVideoCount > 0,
                    FileCount = videoFiles.Count,
                    CheckedFileCount = activeVideoCount,
                    Parent = packNode
                };
                packNode.Children.Add(videosNode);
            }

            packNode.FileCount = packFiles.Count;
            packNode.IsChecked = packNode.Children.Any(c => c.IsChecked);
            return packNode;
        }

        private AssetTreeItem BuildFolderTree(string path, string name)
        {
            var node = new AssetTreeItem
            {
                Name = name,
                FullPath = path,
                IsChecked = true // Will be recalculated based on DisabledAssetPaths
            };

            // Count files in this folder.
            // Enumeration is wrapped because a folder can be deleted/renamed or
            // an external/network drive can drop out between the parent's
            // GetDirectories and this recursive call (TOCTOU). An unguarded
            // DirectoryNotFoundException here reached the dispatcher and crashed
            // the app when opening the assets tab. (#379 / #370)
            var validExtensions = new[] { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".avif", ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };
            List<string> files;
            try
            {
                files = Directory.GetFiles(path)
                    .Where(f => validExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
            {
                App.Logger?.Debug("BuildFolderTree: skipping unreadable folder {Path}: {Error}", path, ex.Message);
                node.UpdateCheckState();
                return node;
            }
            node.FileCount = files.Count;

            // Count checked files using blacklist (files NOT in DisabledAssetPaths are active)
            var basePath = App.EffectiveAssetsPath;
            node.CheckedFileCount = files.Count(f =>
            {
                var relativePath = Path.GetRelativePath(basePath, f).Replace('\\', '/');
                return !App.Settings.Current.DisabledAssetPaths.Contains(relativePath);
            });

            // Add subfolders (same TOCTOU guard as above — skip subfolders that
            // throw rather than letting the whole tree build crash).
            string[] subDirs;
            try
            {
                subDirs = Directory.GetDirectories(path);
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
            {
                App.Logger?.Debug("BuildFolderTree: cannot enumerate subfolders of {Path}: {Error}", path, ex.Message);
                subDirs = Array.Empty<string>();
            }
            foreach (var dir in subDirs)
            {
                var child = BuildFolderTree(dir, Path.GetFileName(dir));
                child.Parent = node;
                node.Children.Add(child);
            }

            // Update check state based on children and files
            node.UpdateCheckState();

            return node;
        }

        internal void AssetTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is AssetTreeItem folder)
            {
                _selectedFolder = folder;

                // Handle pack virtual folders differently
                if (folder.IsPackFolder && !string.IsNullOrEmpty(folder.PackId) && !string.IsNullOrEmpty(folder.PackFileType))
                {
                    LoadPackFolderThumbnails(folder.PackId, folder.PackFileType);
                }
                else if (!string.IsNullOrEmpty(folder.FullPath))
                {
                    LoadFolderThumbnails(folder.FullPath);
                    // Recalculate folder's checked state from actual data
                    RecalculateFolderCheckState(folder);
                }
                else
                {
                    // Parent pack folder or root - show empty
                    _currentFolderFiles.Clear();
                    AssetsTab.TxtThumbnailsEmpty.Text = Loc.Get("label_select_a_subfolder_to_view_files");
                    AssetsTab.TxtThumbnailsEmpty.Visibility = Visibility.Visible;
                    AssetsTab.ThumbnailsItemsControl.ItemsSource = _currentFolderFiles;
                }
            }
        }

        /// <summary>
        /// Recalculate a folder's CheckedFileCount and IsChecked from DisabledAssetPaths
        /// </summary>
        private void RecalculateFolderCheckState(AssetTreeItem folder)
        {
            var basePath = App.EffectiveAssetsPath;
            var validExtensions = new[] { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".avif", ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };

            // Handle pack virtual folders
            if (folder.IsPackFolder && !string.IsNullOrEmpty(folder.PackId) && !string.IsNullOrEmpty(folder.PackFileType))
            {
                var packFiles = App.ContentPacks?.GetPackFiles(folder.PackId, folder.PackFileType);
                if (packFiles != null)
                {
                    folder.FileCount = packFiles.Count();
                    folder.CheckedFileCount = packFiles.Count(f =>
                    {
                        var packPath = $"pack:{folder.PackId}/{f.OriginalName}";
                        return !App.Settings.Current.DisabledAssetPaths.Contains(packPath);
                    });
                }
            }
            // Handle local folders
            else if (!string.IsNullOrEmpty(folder.FullPath) && Directory.Exists(folder.FullPath))
            {
                var files = Directory.GetFiles(folder.FullPath)
                    .Where(f => validExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                folder.CheckedFileCount = files.Count(f =>
                {
                    var relativePath = Path.GetRelativePath(basePath, f).Replace('\\', '/');
                    return !App.Settings.Current.DisabledAssetPaths.Contains(relativePath);
                });
            }

            // Recalculate children too
            foreach (var child in folder.Children)
            {
                RecalculateFolderCheckState(child);
            }

            // Update visual state
            folder.UpdateCheckState();
        }

        /// <summary>
        /// Recalculate all folder check states in the tree from DisabledAssetPaths
        /// </summary>
        private void RecalculateAllFolderCheckStates()
        {
            foreach (var root in _assetTree)
            {
                RecalculateFolderCheckState(root);
            }
        }

        private void LoadPackFolderThumbnails(string packId, string fileType)
        {
            _currentFolderFiles.Clear();
            AssetsTab.TxtThumbnailsEmpty.Visibility = Visibility.Collapsed;

            var packFiles = App.ContentPacks?.GetPackFiles(packId, fileType);
            if (packFiles == null || packFiles.Count == 0)
            {
                AssetsTab.TxtThumbnailsEmpty.Text = Loc.Get("label_no_files_in_this_pack_folder");
                AssetsTab.TxtThumbnailsEmpty.Visibility = Visibility.Visible;
                AssetsTab.ThumbnailsItemsControl.ItemsSource = _currentFolderFiles;
                return;
            }

            foreach (var file in packFiles.OrderBy(f => f.OriginalName))
            {
                var packPath = $"pack:{packId}/{file.OriginalName}";
                var isActive = !App.Settings.Current.DisabledAssetPaths.Contains(packPath);

                var item = new AssetFileItem
                {
                    RelativePath = packPath,
                    IsChecked = isActive,
                    IsPackFile = true,
                    PackId = packId,
                    PackFileEntry = file
                };

                // Set properties manually for pack files (don't use FullPath setter)
                item.Name = file.OriginalName;
                item.Extension = file.Extension;
                item.IsVideo = file.FileType == "video";

                _currentFolderFiles.Add(item);

                // Load thumbnail from encrypted pack
                _ = LoadPackThumbnailAsync(item, packId, file);
            }

            AssetsTab.ThumbnailsItemsControl.ItemsSource = _currentFolderFiles;
        }

        // Thumbnail cache for pack files (keyed by packId + obfuscatedName) with LRU eviction
        private const int MaxThumbnailCacheEntries = 50;
        private const long MaxThumbnailCacheBytes = 50 * 1024 * 1024; // 50 MB
        private static readonly Dictionary<string, ImageSource> _packThumbnailCache = new();
        private static readonly Dictionary<string, long> _packThumbnailLastAccess = new();
        private static readonly Dictionary<string, long> _packThumbnailSizes = new();
        private static long _packThumbnailCacheBytes;
        private static long _packThumbnailAccessCounter;
        private static readonly SemaphoreSlim _thumbnailSemaphore = new(4); // Limit concurrent loads

        private async Task LoadPackThumbnailAsync(AssetFileItem item, string packId, Services.PackFileEntry file)
        {
            item.IsLoadingThumbnail = true;
            try
            {
                // Check cache first
                var cacheKey = $"{packId}:{file.ObfuscatedName}";
                if (_packThumbnailCache.TryGetValue(cacheKey, out var cached))
                {
                    _packThumbnailLastAccess[cacheKey] = Interlocked.Increment(ref _packThumbnailAccessCounter);
                    Dispatcher.Invoke(() => item.Thumbnail = cached);
                    return;
                }

                // Limit concurrent thumbnail loads (videos are slow, so limit helps)
                await _thumbnailSemaphore.WaitAsync();
                try
                {
                    // Double-check cache after acquiring semaphore
                    if (_packThumbnailCache.TryGetValue(cacheKey, out cached))
                    {
                        _packThumbnailLastAccess[cacheKey] = Interlocked.Increment(ref _packThumbnailAccessCounter);
                        Dispatcher.Invoke(() => item.Thumbnail = cached);
                        return;
                    }

                    await Task.Run(() =>
                    {
                        try
                        {
                            ImageSource? thumbnail = null;

                            if (file.FileType == "image")
                            {
                                // For images, get decrypted thumbnail directly
                                thumbnail = App.ContentPacks?.GetPackFileThumbnail(packId, file, 100, 100);
                            }
                            else if (file.FileType == "video")
                            {
                                // For videos, decrypt to temp file and get shell thumbnail
                                var tempPath = App.ContentPacks?.GetPackFileTempPath(packId, file);
                                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                                {
                                    thumbnail = Helpers.ShellThumbnailHelper.GetThumbnail(tempPath, 100, 100);
                                    // Clean up temp file
                                    try { File.Delete(tempPath); } catch { }
                                }
                            }

                            if (thumbnail != null)
                            {
                                // Estimate size: width * height * 4 bytes (BGRA)
                                long estimatedBytes = 0;
                                if (thumbnail is System.Windows.Media.Imaging.BitmapSource bmp)
                                    estimatedBytes = (long)bmp.PixelWidth * bmp.PixelHeight * 4;

                                // Evict LRU entries if cache is full
                                while ((_packThumbnailCache.Count >= MaxThumbnailCacheEntries ||
                                        _packThumbnailCacheBytes + estimatedBytes > MaxThumbnailCacheBytes) &&
                                       _packThumbnailCache.Count > 0)
                                {
                                    var lruKey = _packThumbnailLastAccess.MinBy(kv => kv.Value).Key;
                                    _packThumbnailCache.Remove(lruKey);
                                    _packThumbnailLastAccess.Remove(lruKey);
                                    if (_packThumbnailSizes.TryGetValue(lruKey, out var evictedSize))
                                    {
                                        _packThumbnailCacheBytes -= evictedSize;
                                        _packThumbnailSizes.Remove(lruKey);
                                    }
                                }

                                _packThumbnailCache[cacheKey] = thumbnail;
                                _packThumbnailLastAccess[cacheKey] = Interlocked.Increment(ref _packThumbnailAccessCounter);
                                _packThumbnailSizes[cacheKey] = estimatedBytes;
                                _packThumbnailCacheBytes += estimatedBytes;

                                Dispatcher.Invoke(() => item.Thumbnail = thumbnail);
                            }
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Debug("Failed to load pack thumbnail: {Error}", ex.Message);
                        }
                    });
                }
                finally
                {
                    _thumbnailSemaphore.Release();
                }
            }
            finally
            {
                Dispatcher.Invoke(() => item.IsLoadingThumbnail = false);
            }
        }

        private void LoadFolderThumbnails(string folderPath)
        {
            _currentFolderFiles.Clear();
            AssetsTab.TxtThumbnailsEmpty.Visibility = Visibility.Collapsed;

            if (!Directory.Exists(folderPath))
            {
                AssetsTab.TxtThumbnailsEmpty.Text = Loc.Get("label_folder_does_not_exist");
                AssetsTab.TxtThumbnailsEmpty.Visibility = Visibility.Visible;
                AssetsTab.ThumbnailsItemsControl.ItemsSource = _currentFolderFiles;
                return;
            }

            var validExtensions = new[] { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".avif", ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };
            var files = Directory.GetFiles(folderPath)
                .Where(f => validExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => Path.GetFileName(f))
                .ToList();

            if (files.Count == 0)
            {
                AssetsTab.TxtThumbnailsEmpty.Text = Loc.Get("label_no_media_files_in_this_folder");
                AssetsTab.TxtThumbnailsEmpty.Visibility = Visibility.Visible;
                return;
            }

            var basePath = App.EffectiveAssetsPath;

            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(basePath, file).Replace('\\', '/');
                // Item is checked if NOT in DisabledAssetPaths (blacklist approach)
                var isActive = !App.Settings.Current.DisabledAssetPaths.Contains(relativePath);

                var item = new AssetFileItem
                {
                    FullPath = file,
                    RelativePath = relativePath,
                    IsChecked = isActive
                };

                // Get file size
                try { item.SizeBytes = new FileInfo(file).Length; } catch { }

                _currentFolderFiles.Add(item);

                // Load thumbnail asynchronously
                _ = LoadThumbnailAsync(item);
            }

            AssetsTab.ThumbnailsItemsControl.ItemsSource = _currentFolderFiles;
        }

        private async Task LoadThumbnailAsync(AssetFileItem item)
        {
            item.IsLoadingThumbnail = true;
            try
            {
                await Task.Run(() =>
                {
                    try
                    {
                        // Use Windows Shell API for thumbnails - works for both images and videos
                        // This gives us the same thumbnails Windows Explorer shows
                        var thumbnail = Helpers.ShellThumbnailHelper.GetThumbnail(item.FullPath, 100, 100);

                        if (thumbnail != null)
                        {
                            Dispatcher.Invoke(() => item.Thumbnail = thumbnail);
                        }
                        else if (!item.IsVideo)
                        {
                            // Fallback for images: load directly
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(item.FullPath, UriKind.Absolute);
                            bitmap.DecodePixelWidth = 100;
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            Dispatcher.Invoke(() => item.Thumbnail = bitmap);
                        }
                    }
                    catch
                    {
                        // Ignore thumbnail load errors
                    }
                });
            }
            finally
            {
                item.IsLoadingThumbnail = false;
            }
        }

        private bool _isUpdatingFolderCheckState = false;

        internal void FolderCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // Prevent recursive triggering when programmatically updating parent states
            if (_isUpdatingFolderCheckState) return;

            if (sender is CheckBox cb && cb.DataContext is AssetTreeItem folder)
            {
                _isUpdatingFolderCheckState = true;
                try
                {
                    // Get the target state from the checkbox
                    bool targetState = folder.IsChecked;

                    // FIRST: Visually update this folder and ALL subfolders immediately
                    // This gives instant feedback to the user
                    SetFolderAndChildrenChecked(folder, targetState);

                    // SECOND: Update the source of truth (DisabledAssetPaths)
                    UpdateFolderFilesCheckState(folder, targetState);

                    // THIRD: Update parent folder states (they may become partially checked)
                    folder.Parent?.UpdateCheckStateFromChildren();

                    UpdateAssetCounts();

                    // Sync thumbnail checkboxes with current DisabledAssetPaths state
                    RefreshThumbnailCheckboxes();

                    // Invalidate cached file listing and the video/bubble queues so the
                    // folder-level toggle takes effect on the very next flash/video (#130).
                    InvalidateAssetPoolsAfterSelectionChange();
                }
                finally
                {
                    _isUpdatingFolderCheckState = false;
                }
            }
        }

        /// <summary>
        /// Set IsChecked and CheckedFileCount for a folder and all its children recursively.
        /// This provides immediate visual feedback when user clicks a folder checkbox.
        /// </summary>
        private void SetFolderAndChildrenChecked(AssetTreeItem folder, bool isChecked)
        {
            folder.IsChecked = isChecked;
            folder.CheckedFileCount = isChecked ? folder.FileCount : 0;

            foreach (var child in folder.Children)
            {
                SetFolderAndChildrenChecked(child, isChecked);
            }
        }

        /// <summary>
        /// Refresh the IsChecked state of thumbnail items based on DisabledAssetPaths
        /// </summary>
        private void RefreshThumbnailCheckboxes()
        {
            foreach (var item in _currentFolderFiles)
            {
                var isActive = !App.Settings.Current.DisabledAssetPaths.Contains(item.RelativePath);
                if (item.IsChecked != isActive)
                {
                    item.IsChecked = isActive;
                }
            }
        }

        private void UpdateFolderFilesCheckState(AssetTreeItem folder, bool isChecked)
        {
            var basePath = App.EffectiveAssetsPath;
            var validExtensions = new[] { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".avif", ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };

            // Handle pack virtual folders
            if (folder.IsPackFolder && !string.IsNullOrEmpty(folder.PackId) && !string.IsNullOrEmpty(folder.PackFileType))
            {
                var packFiles = App.ContentPacks?.GetPackFiles(folder.PackId, folder.PackFileType);
                if (packFiles != null)
                {
                    foreach (var packFile in packFiles)
                    {
                        // Pack file paths use format: pack:{packId}/{filename}
                        var packPath = $"pack:{folder.PackId}/{packFile.OriginalName}";
                        if (isChecked)
                        {
                            App.Settings.Current.DisabledAssetPaths.Remove(packPath);
                        }
                        else
                        {
                            App.Settings.Current.DisabledAssetPaths.Add(packPath);
                        }
                    }
                }
            }
            // Handle local folders
            else if (Directory.Exists(folder.FullPath))
            {
                var files = Directory.GetFiles(folder.FullPath)
                    .Where(f => validExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(basePath, file).Replace('\\', '/');
                    // Use DisabledAssetPaths (blacklist): unchecked items are in the set
                    if (isChecked)
                    {
                        App.Settings.Current.DisabledAssetPaths.Remove(relativePath);
                    }
                    else
                    {
                        App.Settings.Current.DisabledAssetPaths.Add(relativePath);
                    }
                }
            }

            // Recurse into subfolders
            foreach (var child in folder.Children)
            {
                UpdateFolderFilesCheckState(child, isChecked);
            }

            folder.CheckedFileCount = isChecked ? folder.FileCount : 0;
        }

        internal void ThumbnailCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is AssetFileItem file)
            {
                UpdateFileCheckState(file);
            }
        }

        internal void ThumbnailItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is AssetFileItem file)
            {
                if (e.ClickCount == 2)
                {
                    // Double-click: open preview
                    OpenAssetPreview(file);
                    e.Handled = true;
                }
                else
                {
                    // Single click: toggle selection
                    file.IsChecked = !file.IsChecked;
                    UpdateFileCheckState(file);
                }
            }
        }

        internal void ThumbnailItem_Preview_Click(object sender, RoutedEventArgs e)
        {
            // Open preview from context menu
            if (sender is MenuItem menuItem &&
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is Border border &&
                border.DataContext is AssetFileItem file)
            {
                OpenAssetPreview(file);
            }
        }

        internal void ThumbnailItem_OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            // Open file location in Explorer
            if (sender is MenuItem menuItem &&
                menuItem.Parent is ContextMenu contextMenu &&
                contextMenu.PlacementTarget is Border border &&
                border.DataContext is AssetFileItem file)
            {
                try
                {
                    var filePath = file.FullPath;
                    if (System.IO.File.Exists(filePath))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to open file in Explorer");
                }
            }
        }

        private void OpenAssetPreview(AssetFileItem file)
        {
            try
            {
                var filePath = file.FullPath;

                // For pack files, we need to extract to temp first
                if (file.IsPackFile && file.PackFileEntry != null)
                {
                    var tempPath = App.ContentPacks?.GetPackFileTempPath(file.PackId!, file.PackFileEntry);
                    if (!string.IsNullOrEmpty(tempPath))
                    {
                        filePath = tempPath;
                    }
                    else
                    {
                        App.Logger?.Warning("Failed to extract pack file for preview: {File}", file.Name);
                        return;
                    }
                }

                if (!System.IO.File.Exists(filePath))
                {
                    App.Logger?.Warning("File not found for preview: {File}", filePath);
                    return;
                }

                var previewWindow = new MiniPlayerWindow
                {
                    Owner = this
                };
                previewWindow.Closed += (s, args) =>
                {
                    // Only reactivate if our process still owns the foreground
                    var foreground = GetForegroundWindow();
                    if (foreground != IntPtr.Zero)
                    {
                        GetWindowThreadProcessId(foreground, out uint foregroundPid);
                        uint ourPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                        if (foregroundPid != ourPid) return;
                    }
                    Activate();
                };
                previewWindow.LoadFile(filePath);
                previewWindow.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open asset preview for {File}", file.Name);
            }
        }

        /// <summary>
        /// Update DisabledAssetPaths and folder state when a single file's check state changes.
        /// </summary>
        private void UpdateFileCheckState(AssetFileItem file)
        {
            // Use DisabledAssetPaths (blacklist): unchecked items are in the set
            if (file.IsChecked)
            {
                App.Settings.Current.DisabledAssetPaths.Remove(file.RelativePath);
            }
            else
            {
                App.Settings.Current.DisabledAssetPaths.Add(file.RelativePath);
            }

            // Invalidate cached file listing AND the video/bubble queues — without this
            // the toggle has no effect (Flash for up to a minute via its 60s cache, video
            // until the stale queue drains or a restart), which users perceive as
            // "unchecking does nothing" (#130).
            InvalidateAssetPoolsAfterSelectionChange();

            // Update parent folder state - set flag to prevent FolderCheckBox_Changed from
            // propagating changes to all children when the folder's IsChecked changes
            _isUpdatingFolderCheckState = true;
            try
            {
                UpdateParentFolderCheckState();
                UpdateAssetCounts();
            }
            finally
            {
                _isUpdatingFolderCheckState = false;
            }
        }

        private void UpdateParentFolderCheckState()
        {
            if (_selectedFolder == null) return;

            // Count checked files
            var checkedCount = _currentFolderFiles.Count(f => f.IsChecked);
            _selectedFolder.CheckedFileCount = checkedCount;
            _selectedFolder.UpdateCheckState();
        }

        /// <summary>
        /// Invalidate the in-memory asset pools after a selection toggle so the change
        /// takes effect immediately. The asset-manager count reads live DisabledAssetPaths,
        /// but the media services hold their own caches/queues that only refill lazily —
        /// without this the mandatory video keeps playing from a stale full-pool queue
        /// (count says "1 active" while playback ignores the toggle until restart).
        /// FlashService clears its 60s file-listing cache AND its live selection pools;
        /// Video/BubbleCount just empty their queues (cheap, O(1)) and refill from the
        /// current selection on next use.
        /// DisabledAssetPaths is a HashSet mutated in place, so no PropertyChanged fires and
        /// nothing autosaves — without the explicit save the toggle is lost on restart unless
        /// the user also presses "Save Selection". Settings.Save() is debounced (500ms), so a
        /// rapid run of checkbox clicks still costs one disk write.
        /// </summary>
        private void InvalidateAssetPoolsAfterSelectionChange()
        {
            App.Flash?.ClearFileCache();
            App.Video?.ReloadAssets();
            App.BubbleCount?.ReloadAssets();
            App.Settings.Save();
        }

        internal void BtnSelectAllAssets_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFolder == null) return;

            _isUpdatingFolderCheckState = true;
            try
            {
                // Update DisabledAssetPaths only for selected folder and subfolders
                UpdateFolderFilesCheckState(_selectedFolder, true);

                // Update visual state for selected folder and children
                SetFolderAndChildrenChecked(_selectedFolder, true);

                // Propagate changes up to parent folders
                _selectedFolder.Parent?.UpdateCheckStateFromChildren();

                // Sync thumbnail checkboxes
                RefreshThumbnailCheckboxes();
                UpdateAssetCounts();
                InvalidateAssetPoolsAfterSelectionChange();
            }
            finally
            {
                _isUpdatingFolderCheckState = false;
            }
        }

        internal void BtnDeselectAllAssets_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFolder == null) return;

            _isUpdatingFolderCheckState = true;
            try
            {
                // Update DisabledAssetPaths only for selected folder and subfolders
                UpdateFolderFilesCheckState(_selectedFolder, false);

                // Update visual state for selected folder and children
                SetFolderAndChildrenChecked(_selectedFolder, false);

                // Propagate changes up to parent folders
                _selectedFolder.Parent?.UpdateCheckStateFromChildren();

                // Sync thumbnail checkboxes
                RefreshThumbnailCheckboxes();
                UpdateAssetCounts();
                InvalidateAssetPoolsAfterSelectionChange();
            }
            finally
            {
                _isUpdatingFolderCheckState = false;
            }
        }

        private void BtnSaveAssetSelection_Click(object sender, RoutedEventArgs e)
        {
            App.Settings.Save();

            // Fully reload asset pools so services pick up new selection
            App.Flash?.LoadAssets();
            App.Video?.ReloadAssets();
            App.BubbleCount?.ReloadAssets();

            var disabledCount = App.Settings.Current.DisabledAssetPaths.Count;
            var message = disabledCount > 0
                ? $"Selection saved!\n\n{disabledCount} assets are disabled.\n\nThe changes will take effect on the next flash/video."
                : "Selection saved!\n\nAll assets are active.\n\nThe changes will take effect on the next flash/video.";
            MessageBox.Show(
                message,
                "Selection Saved",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        #region Asset Presets

        private bool _isLoadingPreset = false;

        private void InitializeAssetPresets()
        {
            // Ensure default preset exists
            if (!App.Settings.Current.AssetPresets.Any(p => p.IsDefault))
            {
                App.Settings.Current.AssetPresets.Insert(0, Models.AssetPreset.CreateDefault());
            }

            // Update default preset counts (it should show all assets including packs)
            var defaultPreset = App.Settings.Current.AssetPresets.FirstOrDefault(p => p.IsDefault);
            if (defaultPreset != null)
            {
                // Use the same counting logic as asset counts display
                var totalImages = 0;
                var totalVideos = 0;
                var activeImages = 0;
                var activeVideos = 0;
                CountAssetsRecursive(_assetTree, ref totalImages, ref totalVideos, ref activeImages, ref activeVideos);
                defaultPreset.EnabledImageCount = totalImages;
                defaultPreset.EnabledVideoCount = totalVideos;
            }

            // Refresh the ComboBox
            RefreshAssetPresetsComboBox();

            // Update existing preset counts to match current file counts
            // (in case files were added/removed since preset was saved)
            UpdatePresetCountsFromCurrentState();

            // Same trigger, same tab: ShowTab("assets") is the one place that runs on every
            // visit, so the media-source picker syncs itself here rather than needing its own
            // hook in TabNavigation.
            InitializeRemoteMediaPicker();
        }

        /// <summary>
        /// Recalculates the enabled counts for all non-default presets based on current files.
        /// This ensures preset displays are accurate even if files were added/removed.
        /// </summary>
        private void UpdatePresetCountsFromCurrentState()
        {
            var imageExts = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
            var videoExts = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };
            var basePath = App.EffectiveAssetsPath;

            foreach (var preset in App.Settings.Current.AssetPresets.Where(p => !p.IsDefault))
            {
                var enabledImages = 0;
                var enabledVideos = 0;

                // Count files in images folder that are NOT in this preset's disabled list
                var imagesPath = Path.Combine(basePath, "images");
                if (Directory.Exists(imagesPath))
                {
                    CountEnabledFilesRecursive(imagesPath, basePath, preset.DisabledAssetPaths, imageExts, ref enabledImages);
                }

                // Count files in videos folder that are NOT in this preset's disabled list
                var videosPath = Path.Combine(basePath, "videos");
                if (Directory.Exists(videosPath))
                {
                    CountEnabledFilesRecursive(videosPath, basePath, preset.DisabledAssetPaths, videoExts, ref enabledVideos);
                }

                // Add pack files (check if disabled in preset)
                var activePackIds = App.ContentPacks?.GetActivePackIds() ?? new List<string>();
                foreach (var packId in activePackIds)
                {
                    var packImages = App.ContentPacks?.GetPackFiles(packId, "image");
                    var packVideos = App.ContentPacks?.GetPackFiles(packId, "video");

                    if (packImages != null)
                    {
                        foreach (var packFile in packImages)
                        {
                            var packPath = $"pack:{packId}/{packFile.OriginalName}";
                            if (preset.DisabledAssetPaths == null || !preset.DisabledAssetPaths.Contains(packPath))
                            {
                                enabledImages++;
                            }
                        }
                    }

                    if (packVideos != null)
                    {
                        foreach (var packFile in packVideos)
                        {
                            var packPath = $"pack:{packId}/{packFile.OriginalName}";
                            if (preset.DisabledAssetPaths == null || !preset.DisabledAssetPaths.Contains(packPath))
                            {
                                enabledVideos++;
                            }
                        }
                    }
                }

                // Update preset if counts changed
                if (preset.EnabledImageCount != enabledImages || preset.EnabledVideoCount != enabledVideos)
                {
                    preset.EnabledImageCount = enabledImages;
                    preset.EnabledVideoCount = enabledVideos;
                }
            }
        }

        private void CountEnabledFilesRecursive(string path, string basePath, HashSet<string>? disabledPaths, string[] validExts, ref int count)
        {
            if (!Directory.Exists(path)) return;

            var files = Directory.GetFiles(path);
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!validExts.Contains(ext)) continue;

                var relativePath = Path.GetRelativePath(basePath, file).Replace('\\', '/');
                if (disabledPaths == null || !disabledPaths.Contains(relativePath))
                {
                    count++;
                }
            }

            foreach (var dir in Directory.GetDirectories(path))
            {
                CountEnabledFilesRecursive(dir, basePath, disabledPaths, validExts, ref count);
            }
        }

        private void RefreshAssetPresetsComboBox()
        {
            _isLoadingPreset = true;
            AssetsTab.CmbAssetPresets.ItemsSource = null;
            AssetsTab.CmbAssetPresets.ItemsSource = App.Settings.Current.AssetPresets;

            // Select current preset if set
            if (!string.IsNullOrEmpty(App.Settings.Current.CurrentAssetPresetId))
            {
                AssetsTab.CmbAssetPresets.SelectedValue = App.Settings.Current.CurrentAssetPresetId;
            }
            else
            {
                // Default to "All Assets" preset
                var defaultPreset = App.Settings.Current.AssetPresets.FirstOrDefault(p => p.IsDefault);
                if (defaultPreset != null)
                {
                    AssetsTab.CmbAssetPresets.SelectedValue = defaultPreset.Id;
                }
            }
            _isLoadingPreset = false;
        }

        internal void CmbAssetPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingPreset) return;
            if (AssetsTab.CmbAssetPresets.SelectedItem is not Models.AssetPreset preset) return;

            // Apply preset's disabled paths
            var presetDisabledCount = preset.DisabledAssetPaths?.Count ?? 0;
            preset.ApplyToSettings();
            App.Settings.Current.CurrentAssetPresetId = preset.Id;

            // Refresh tree to show new state
            RefreshAssetTree();
            UpdateAssetCounts();

            // Sync thumbnail checkboxes with new preset state
            RefreshThumbnailCheckboxes();

            // Clear caches so services pick up new selection. A preset switch replaces the whole
            // disabled set, so it needs exactly the invalidation a manual uncheck gets: the video
            // AND bubble-count queues refill only when empty, so skipping them left the previous
            // preset's clips playing until restart, and the applied set was never persisted.
            InvalidateAssetPoolsAfterSelectionChange();
            // The Goon Game transfer cache plans against the ACTIVE pool, so a preset switch is a
            // re-plan (and the "N assets need compressing" nudge). Never blocks - it schedules.
            try { Services.Transfer.TransferCompressionService.Instance.OnPresetChanged(preset.Id); }
            catch (Exception ex) { App.Logger?.Debug("Transfer cache preset hook: {E}", ex.Message); }

            // Get actual counts after applying
            var (activeImages, activeVideos) = GetCurrentActiveAssetCounts();
            App.Logger?.Information("Loaded asset preset: {Name} - Preset had {PresetDisabled} disabled paths, now {ActiveImages} images and {ActiveVideos} videos active",
                preset.Name, presetDisabledCount, activeImages, activeVideos);
        }

        internal void BtnSaveAssetPreset_Click(object sender, RoutedEventArgs e)
        {
            // Get current counts
            var (imageCount, videoCount) = GetCurrentActiveAssetCounts();

            // Simple input dialog using WPF
            var dialog = new System.Windows.Window
            {
                Title = Loc.Get("title_save_asset_preset"),
                Width = 350,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = (SolidColorBrush)Application.Current.Resources["DarkerBgBrush"],
                WindowStyle = WindowStyle.ToolWindow
            };

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = "Enter a name for this preset:",
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            var textBox = new TextBox
            {
                Text = $"Preset {App.Settings.Current.AssetPresets.Count}",
                Background = (SolidColorBrush)Application.Current.Resources["PanelBgBrush"],
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 15)
            };
            textBox.SelectAll();
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new Button
            {
                Content = "Save",
                Width = 80,
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0)
            };
            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 80,
                Padding = new Thickness(8, 5, 8, 5),
                Background = (SolidColorBrush)Application.Current.Resources["PanelAccentBrush"],
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0)
            };

            btnOk.Click += (s, args) => { dialog.DialogResult = true; dialog.Close(); };
            btnCancel.Click += (s, args) => { dialog.DialogResult = false; dialog.Close(); };

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            Grid.SetRow(btnPanel, 2);
            grid.Children.Add(btnPanel);

            dialog.Content = grid;
            textBox.Focus();

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                var disabledCount = App.Settings.Current.DisabledAssetPaths.Count;
                var preset = Models.AssetPreset.FromCurrentSettings(textBox.Text.Trim(), imageCount, videoCount);
                App.Settings.Current.AssetPresets.Add(preset);
                App.Settings.Current.CurrentAssetPresetId = preset.Id;
                App.Settings.Save();

                RefreshAssetPresetsComboBox();
                AssetsTab.CmbAssetPresets.SelectedValue = preset.Id;
                InvalidateAssetPoolsAfterSelectionChange();

                App.Logger?.Information("Saved asset preset: {Name} with {Images} images, {Videos} videos, {Disabled} disabled paths",
                    preset.Name, imageCount, videoCount, disabledCount);

                MessageBox.Show(
                    $"Preset '{preset.Name}' saved!\n\n{imageCount} images, {videoCount} videos enabled.\n{disabledCount} assets disabled.",
                    "Preset Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        internal void BtnUpdateAssetPreset_Click(object sender, RoutedEventArgs e)
        {
            if (AssetsTab.CmbAssetPresets.SelectedItem is not Models.AssetPreset preset)
            {
                MessageBox.Show(Loc.Get("msg_please_select_a_preset_to_update"), "No Preset Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (preset.IsDefault)
            {
                MessageBox.Show(Loc.Get("msg_cannot_update_the_default_all_assets_preset_n"), "Cannot Update Default", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Update preset '{preset.Name}' with the current selection?",
                "Update Preset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var (imageCount, videoCount) = GetCurrentActiveAssetCounts();
                var disabledCount = App.Settings.Current.DisabledAssetPaths.Count;
                preset.UpdateFromCurrentSettings(imageCount, videoCount);
                App.Settings.Save();

                // Refresh display
                RefreshAssetPresetsComboBox();
                AssetsTab.CmbAssetPresets.SelectedValue = preset.Id;
                InvalidateAssetPoolsAfterSelectionChange();

                App.Logger?.Information("Updated asset preset: {Name} with {Images} images, {Videos} videos, {Disabled} disabled paths",
                    preset.Name, imageCount, videoCount, disabledCount);

                MessageBox.Show(
                    $"Preset '{preset.Name}' updated!\n\n{imageCount} images, {videoCount} videos enabled.\n{disabledCount} assets disabled.",
                    "Preset Updated",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        internal void BtnDeleteAssetPreset_Click(object sender, RoutedEventArgs e)
        {
            if (AssetsTab.CmbAssetPresets.SelectedItem is not Models.AssetPreset preset)
            {
                MessageBox.Show(Loc.Get("msg_please_select_a_preset_to_delete"), "No Preset Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (preset.IsDefault)
            {
                MessageBox.Show(Loc.Get("msg_cannot_delete_the_default_all_assets_preset"), "Cannot Delete Default", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Delete preset '{preset.Name}'?\n\nThis cannot be undone.",
                "Delete Preset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                App.Settings.Current.AssetPresets.Remove(preset);

                // Select default preset
                var defaultPreset = App.Settings.Current.AssetPresets.FirstOrDefault(p => p.IsDefault);
                if (defaultPreset != null)
                {
                    App.Settings.Current.CurrentAssetPresetId = defaultPreset.Id;
                }
                else
                {
                    App.Settings.Current.CurrentAssetPresetId = null;
                }

                App.Settings.Save();
                RefreshAssetPresetsComboBox();

                App.Logger?.Information("Deleted asset preset: {Name}", preset.Name);
            }
        }

        private (int imageCount, int videoCount) GetCurrentActiveAssetCounts()
        {
            var totalImages = 0;
            var totalVideos = 0;
            var activeImages = 0;
            var activeVideos = 0;
            CountAssetsRecursive(_assetTree, ref totalImages, ref totalVideos, ref activeImages, ref activeVideos);
            return (activeImages, activeVideos);
        }

        #endregion

        #region Phrase Presets

        private bool _isLoadingPhrasePreset = false;

        private void InitializePhrasePresets()
        {
            RefreshPhrasePresetsComboBox();
        }

        private void RefreshPhrasePresetsComboBox()
        {
            _isLoadingPhrasePreset = true;
            CompanionTab.CmbPhrasePresets.ItemsSource = null;
            CompanionTab.CmbPhrasePresets.ItemsSource = App.Settings.Current.PhrasePresets;

            if (!string.IsNullOrEmpty(App.Settings.Current.CurrentPhrasePresetId))
            {
                CompanionTab.CmbPhrasePresets.SelectedValue = App.Settings.Current.CurrentPhrasePresetId;
            }
            _isLoadingPhrasePreset = false;
        }

        internal void CmbPhrasePresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingPhrasePreset || _isLoading) return;
            if (CompanionTab.CmbPhrasePresets.SelectedItem is not Models.PhrasePreset preset) return;

            preset.ApplyToSettings();
            App.Settings.Current.CurrentPhrasePresetId = preset.Id;
            App.Settings.Save();

            UpdatePhraseCountDisplay();

            App.Logger?.Information("Loaded phrase preset: {Name} ({Count} phrases)",
                preset.Name, preset.ActivePhraseCount);
        }

        internal void BtnSavePhrasePreset_Click(object sender, RoutedEventArgs e)
        {
            var activePhraseCount = App.CompanionPhrases?.GetActivePhraseCount() ?? 0;

            var dialog = new System.Windows.Window
            {
                Title = Loc.Get("title_save_phrase_preset"),
                Width = 350,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = (SolidColorBrush)Application.Current.Resources["DarkerBgBrush"],
                WindowStyle = WindowStyle.ToolWindow
            };

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = "Enter a name for this phrase preset:",
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            var textBox = new TextBox
            {
                Text = $"Preset {App.Settings.Current.PhrasePresets.Count + 1}",
                Background = (SolidColorBrush)Application.Current.Resources["PanelBgBrush"],
                Foreground = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 0, 15)
            };
            textBox.SelectAll();
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new Button
            {
                Content = "Save",
                Width = 80,
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(App.Mods?.GetAccentColorHex() ?? "#FF69B4")),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0)
            };
            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 80,
                Padding = new Thickness(8, 5, 8, 5),
                Background = (SolidColorBrush)Application.Current.Resources["PanelAccentBrush"],
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0)
            };

            btnOk.Click += (s, args) => { dialog.DialogResult = true; dialog.Close(); };
            btnCancel.Click += (s, args) => { dialog.DialogResult = false; dialog.Close(); };

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            Grid.SetRow(btnPanel, 2);
            grid.Children.Add(btnPanel);

            dialog.Content = grid;
            textBox.Focus();

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                var preset = Models.PhrasePreset.FromCurrentSettings(textBox.Text.Trim(), activePhraseCount);
                App.Settings.Current.PhrasePresets.Add(preset);
                App.Settings.Current.CurrentPhrasePresetId = preset.Id;
                App.Settings.Save();

                RefreshPhrasePresetsComboBox();
                CompanionTab.CmbPhrasePresets.SelectedValue = preset.Id;

                App.Logger?.Information("Saved phrase preset: {Name} with {Count} active phrases",
                    preset.Name, activePhraseCount);
            }
        }

        internal void BtnDeletePhrasePreset_Click(object sender, RoutedEventArgs e)
        {
            if (CompanionTab.CmbPhrasePresets.SelectedItem is not Models.PhrasePreset preset)
            {
                MessageBox.Show(Loc.Get("msg_please_select_a_preset_to_delete"), "No Preset Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Delete phrase preset '{preset.Name}'?\n\nThis cannot be undone.",
                "Delete Preset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                App.Settings.Current.PhrasePresets.Remove(preset);
                App.Settings.Current.CurrentPhrasePresetId = null;
                App.Settings.Save();

                RefreshPhrasePresetsComboBox();

                App.Logger?.Information("Deleted phrase preset: {Name}", preset.Name);
            }
        }

        #endregion

        private void UpdateAssetCounts()
        {
            var totalImages = 0;
            var totalVideos = 0;
            var activeImages = 0;
            var activeVideos = 0;

            CountAssetsRecursive(_assetTree, ref totalImages, ref totalVideos, ref activeImages, ref activeVideos);

            // Always show active counts (blacklist system: files NOT in DisabledAssetPaths are active)
            AssetsTab.TxtAssetCounts.Text = $"{activeImages} images, {activeVideos} videos active";
        }

        private void CountAssetsRecursive(IEnumerable<AssetTreeItem> items, ref int totalImages, ref int totalVideos, ref int activeImages, ref int activeVideos)
        {
            var imageExts = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
            var videoExts = new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };

            foreach (var folder in items)
            {
                // Handle pack virtual folders
                if (folder.IsPackFolder && !string.IsNullOrEmpty(folder.PackId) && !string.IsNullOrEmpty(folder.PackFileType))
                {
                    var packFiles = App.ContentPacks?.GetPackFiles(folder.PackId, folder.PackFileType);
                    if (packFiles != null)
                    {
                        foreach (var packFile in packFiles)
                        {
                            // Pack file paths use format: pack:{packId}/{filename}
                            var packPath = $"pack:{folder.PackId}/{packFile.OriginalName}";
                            var isActive = !App.Settings.Current.DisabledAssetPaths.Contains(packPath);

                            if (folder.PackFileType == "image")
                            {
                                totalImages++;
                                if (isActive) activeImages++;
                            }
                            else if (folder.PackFileType == "video")
                            {
                                totalVideos++;
                                if (isActive) activeVideos++;
                            }
                        }
                    }
                }
                // Handle local folders
                else if (Directory.Exists(folder.FullPath))
                {
                    var files = Directory.GetFiles(folder.FullPath);
                    var basePath = App.EffectiveAssetsPath;

                    foreach (var file in files)
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        var isImage = imageExts.Contains(ext);
                        var isVideo = videoExts.Contains(ext);

                        if (isImage) totalImages++;
                        if (isVideo) totalVideos++;

                        // Use blacklist: files NOT in DisabledAssetPaths are active
                        var relativePath = Path.GetRelativePath(basePath, file).Replace('\\', '/');
                        var isActive = !App.Settings.Current.DisabledAssetPaths.Contains(relativePath);

                        if (isActive && isImage) activeImages++;
                        if (isActive && isVideo) activeVideos++;
                    }
                }

                CountAssetsRecursive(folder.Children, ref totalImages, ref totalVideos, ref activeImages, ref activeVideos);
            }
        }

        internal async void BtnPackDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ContentPack pack)
            {
                if (pack.IsDownloaded)
                {
                    // Ask for confirmation to uninstall
                    var sizeStr = pack.SizeBytes > 0 ? $"{pack.SizeBytes / (1024.0 * 1024.0 * 1024.0):F1} GB" : "";
                    var result = MessageBox.Show(
                        $"Uninstall '{pack.Name}'?\n\nThis will delete {sizeStr} of downloaded content from your computer.\n\nYou can reinstall it later if needed.",
                        "Uninstall Content Pack",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                        return;

                    try
                    {
                        // Uninstall pack (deactivate + delete files)
                        App.ContentPacks?.UninstallPack(pack.Id);
                        pack.IsDownloaded = false;
                        pack.IsActive = false;
                        pack.PreviewImages.Clear(); // Clear preview images

                        // UI updates automatically via data binding
                        RefreshAssetTree();
                        App.Flash?.LoadAssets();
                        App.Video?.ReloadAssets();
                        App.BubbleCount?.ReloadAssets();
                        MessageBox.Show($"'{pack.Name}' has been uninstalled.", "Uninstalled", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "Failed to uninstall pack: {Name}", pack.Name);
                        MessageBox.Show($"Failed to uninstall pack: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    // External packs: fetch URL via authenticated endpoint, then open in browser
                    if (pack.IsExternal)
                    {
                        try
                        {
                            var externalUrl = pack.ExternalUrl ?? await App.ContentPacks!.GetExternalPackDownloadUrlAsync(pack.Id);
                            if (!string.IsNullOrEmpty(externalUrl) && Uri.TryCreate(externalUrl, UriKind.Absolute, out var extUri)
                                && extUri.Scheme == Uri.UriSchemeHttps)
                            {
                                Process.Start(new ProcessStartInfo(externalUrl) { UseShellExecute = true });
                            }
                        }
                        catch (Exception ex)
                        {
                            App.Logger?.Error(ex, "Failed to get external pack URL for {PackId}", pack.Id);
                            MessageBox.Show($"Failed to get download link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        return;
                    }

                    // Show confirmation dialog before download
                    var sizeStr = pack.SizeBytes > 0 ? $" ({pack.SizeBytes / (1024.0 * 1024):F0} MB)" : "";
                    var result = MessageBox.Show(
                        $"Download and install '{pack.Name}'?{sizeStr}\n\nThis will download encrypted content to a secure folder on your computer.",
                        "Install Content Pack",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                        return;

                    // Download and install - UI updates automatically via data binding
                    pack.IsDownloading = true;

                    try
                    {
                        var progress = new Progress<int>(p => pack.DownloadProgress = p);
                        await App.ContentPacks!.InstallPackAsync(pack, progress);
                        App.ContentPacks.ActivatePack(pack.Id);
                        pack.IsActive = true;

                        // UI updates automatically via data binding
                        // Preview images are loaded by OnPackDownloadCompleted event handler
                        RefreshAssetTree();
                        App.Flash?.LoadAssets();
                        App.Video?.ReloadAssets();
                        App.BubbleCount?.ReloadAssets();
                        MessageBox.Show(Loc.GetF("msg_0_installed_successfully", pack.Name), Loc.Get("title_success"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Auth error - already handled by OnPackAuthenticationRequired event
                        App.Logger?.Debug("Pack install cancelled - authentication required");
                    }
                    catch (Services.PackRateLimitException)
                    {
                        // Rate limit error - already handled by OnPackRateLimitExceeded event
                        App.Logger?.Debug("Pack install cancelled - rate limit exceeded");
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "Failed to install pack: {Name}", pack.Name);
                        MessageBox.Show($"Installation failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        // UI updates automatically via data binding
                        pack.IsDownloading = false;
                        pack.DownloadProgress = 0;
                    }
                }
            }
        }

        internal void BtnPackActivate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ContentPack pack && pack.IsDownloaded)
            {
                try
                {
                    if (pack.IsActive)
                    {
                        // Deactivate pack (hide but keep downloaded)
                        App.ContentPacks?.DeactivatePack(pack.Id);
                        pack.IsActive = false;
                    }
                    else
                    {
                        // Activate pack (show in assets)
                        App.ContentPacks?.ActivatePack(pack.Id);
                        pack.IsActive = true;
                    }

                    // Refresh asset tree UI and reload asset pools
                    RefreshAssetTree();
                    App.Flash?.LoadAssets();
                    App.Video?.ReloadAssets();
                    App.BubbleCount?.ReloadAssets();
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Failed to toggle pack activation: {Name}", pack.Name);
                    MessageBox.Show($"Failed to update pack: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnPackUpgrade_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ContentPack pack && !string.IsNullOrEmpty(pack.UpgradeUrl)
                && Uri.TryCreate(pack.UpgradeUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                Process.Start(new ProcessStartInfo(pack.UpgradeUrl) { UseShellExecute = true });
            }
        }

        private void BtnPackPatreon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ContentPack pack && !string.IsNullOrEmpty(pack.PatreonUrl)
                && Uri.TryCreate(pack.PatreonUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                Process.Start(new ProcessStartInfo(pack.PatreonUrl) { UseShellExecute = true });
            }
        }

        #region Media Source Picker (local / remote / both)

        // ---------------------------------------------------------------------------------
        // THERE IS NO TierGate HERE, AND THAT IS THE DESIGN.
        //
        // Remote media as an ASSET SOURCE is free for everyone. It exists so that a user who
        // installs the app with an empty assets folder still has something to run - and "has no
        // library yet" is overwhelmingly the free tier, so gating the fix behind the paywall
        // would put it exactly where it cannot do its job.
        //
        // What stays premium is the For You FEED - the browsable surface - which keeps its own
        // gate at MainWindow.Lab.cs:290. Feed = a place you go and scroll; this = where flashes
        // and videos happen to come from. Owner decision 2026-08-12, planning/remote-media
        // PLAN.md blocker B9.
        //
        // If you arrived here to "fix" a missing entitlement check: it is not an oversight.
        // ---------------------------------------------------------------------------------

        private const string MediaSrcLocal = "local";
        private const string MediaSrcOnline = "online";
        private const string MediaSrcMixed = "mixed";

        private bool _remotePickerWired;
        /// <summary>Set while the picker writes settings INTO its controls, so the change
        /// handlers can tell a user click from an echo of their own last click.</summary>
        private bool _remotePickerSyncing;
        private readonly List<ToggleButton> _remoteSourceChipButtons = new();
        private readonly List<ToggleButton> _remoteNicheChipButtons = new();

        /// <summary>Builds the picker once, then re-syncs it to settings on every tab visit.</summary>
        private void InitializeRemoteMediaPicker()
        {
            try
            {
                var tab = AssetsTab;
                if (tab?.RemoteSourceChips == null) return;

                if (!_remotePickerWired)
                {
                    _remotePickerWired = true;
                    BuildRemoteSourceChips();
                    BuildRemoteNicheChips();

                    if (tab.SliderRemoteRatio != null)
                        tab.SliderRemoteRatio.ValueChanged += SliderRemoteRatio_Changed;
                    if (tab.BtnRemoteAddSub != null)
                        tab.BtnRemoteAddSub.Click += BtnRemoteAddSub_Click;
                    if (tab.BtnRemoteClearBlocklist != null)
                        tab.BtnRemoteClearBlocklist.Click += BtnRemoteClearBlocklist_Click;
                    if (tab.TxtRemoteCustomSub != null)
                        tab.TxtRemoteCustomSub.KeyDown += TxtRemoteCustomSub_KeyDown;
                }

                RefreshRemoteMediaPicker();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Remote media picker init failed"); }
        }

        /// <summary>Own assets / Reddit / Both, as chips rather than radio buttons so the whole
        /// panel speaks one shape language (the niche row below uses the same style).</summary>
        private void BuildRemoteSourceChips()
        {
            var host = AssetsTab?.RemoteSourceChips;
            if (host == null || host.Children.Count > 0) return;

            var chipStyle = TryFindResource("AchievementFilterChip") as Style;

            void Add(string key, string locKey, string english, string tipKey, string tipEnglish)
            {
                var chip = new ToggleButton
                {
                    Style = chipStyle,
                    Tag = key,
                    Content = LocOr(locKey, english),
                    ToolTip = LocOr(tipKey, tipEnglish)
                };
                chip.Checked += RemoteSourceChip_Changed;
                chip.Unchecked += RemoteSourceChip_Changed;
                _remoteSourceChipButtons.Add(chip);
                host.Children.Add(chip);
            }

            Add(MediaSrcLocal, "label_media_source_own", "My own assets",
                "tooltip_media_source_own", "Only the images and videos in your assets folder.");
            Add(MediaSrcOnline, "label_media_source_online", "Reddit (via Scrolller)",
                "tooltip_media_source_online", "Stream everything from the subreddits you pick. Nothing is saved to your disk.");
            Add(MediaSrcMixed, "label_media_source_both", "Both",
                "tooltip_media_source_both", "Blend your own media with Reddit, in whatever proportion you like.");
        }

        /// <summary>Niche toggles, straight off the shared catalog. These edit
        /// <c>FypOnlineNiches</c> — the SAME selection the For You feed uses, on purpose: one
        /// taxonomy, one selection, two surfaces (see the note in AppSettings).</summary>
        private void BuildRemoteNicheChips()
        {
            var host = AssetsTab?.RemoteNicheChips;
            if (host == null || host.Children.Count > 0) return;

            var chipStyle = TryFindResource("AchievementFilterChip") as Style;

            foreach (var niche in Services.Fyp.Online.FypOnlineCoordinator.Catalog)
            {
                var chip = new ToggleButton
                {
                    Style = chipStyle,
                    Tag = niche.Id,
                    Content = niche.Label
                };
                chip.Checked += RemoteNicheChip_Changed;
                chip.Unchecked += RemoteNicheChip_Changed;
                _remoteNicheChipButtons.Add(chip);
                host.Children.Add(chip);
            }
        }

        /// <summary>Pushes live settings into every control in the panel. Safe to call as often
        /// as the tab is opened.</summary>
        private void RefreshRemoteMediaPicker()
        {
            var tab = AssetsTab;
            var settings = App.Settings?.Current;
            if (tab?.RemoteSourceChips == null || settings == null) return;

            _remotePickerSyncing = true;
            try
            {
                var source = settings.MediaSource;
                foreach (var chip in _remoteSourceChipButtons)
                    chip.IsChecked = string.Equals(chip.Tag as string, source, StringComparison.Ordinal);

                if (tab.SliderRemoteRatio != null) tab.SliderRemoteRatio.Value = settings.RemoteMediaRatio;
                if (tab.TxtRemoteRatio != null) tab.TxtRemoteRatio.Text = $"{settings.RemoteMediaRatio}%";
                if (tab.RemoteRatioRow != null)
                    tab.RemoteRatioRow.Visibility = string.Equals(source, MediaSrcMixed, StringComparison.Ordinal)
                        ? Visibility.Visible : Visibility.Collapsed;

                var blockedSubs = settings.RemoteMediaBlockedSubs;
                var blockedIds = settings.RemoteMediaBlockedIds;

                // Folded away while the app runs on local files only - except when there is a
                // blocklist, which must stay reviewable whatever the source is set to.
                if (tab.RemoteMediaDetails != null)
                    tab.RemoteMediaDetails.Visibility =
                        !string.Equals(source, MediaSrcLocal, StringComparison.Ordinal)
                        || blockedSubs.Count > 0 || blockedIds.Count > 0
                            ? Visibility.Visible : Visibility.Collapsed;

                var selected = new HashSet<string>(settings.FypOnlineNiches ?? new List<string>(),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var chip in _remoteNicheChipButtons)
                    chip.IsChecked = chip.Tag is string id && selected.Contains(id);

                RebuildRemoteCustomSubChips(settings);
                RebuildRemoteBlocklist(settings, blockedSubs, blockedIds);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Remote media picker refresh failed"); }
            finally { _remotePickerSyncing = false; }
        }

        private void RemoteSourceChip_Changed(object sender, RoutedEventArgs e)
        {
            if (_remotePickerSyncing) return;
            try
            {
                if (sender is not ToggleButton chip || chip.Tag is not string key) return;
                var settings = App.Settings?.Current;
                if (settings == null) return;

                // Un-clicking the live chip would leave the app with no source at all.
                if (chip.IsChecked != true)
                {
                    if (string.Equals(key, settings.MediaSource, StringComparison.Ordinal)) chip.IsChecked = true;
                    return;
                }
                if (string.Equals(key, settings.MediaSource, StringComparison.Ordinal)) return;

                // Anything but "local" starts fetching third-party content, so it is asked for
                // exactly once - and never asked again of someone who already said yes to the
                // For You feed's card (HasRemoteMediaConsent, not the raw flag).
                if (!string.Equals(key, MediaSrcLocal, StringComparison.Ordinal) && !settings.HasRemoteMediaConsent)
                {
                    if (!AskRemoteMediaConsent(settings))
                    {
                        RefreshRemoteMediaPicker();   // puts the chips back where they were
                        return;
                    }
                }

                settings.MediaSource = key;
                // Rotation state was picked for the old source and the media services are
                // holding pools built the old way; both go stale the moment this changes.
                Services.Fyp.Online.FypOnlineCoordinator.ResetAllChannels();
                InvalidateAssetPoolsAfterSelectionChange();   // also persists
                RefreshRemoteMediaPicker();

                App.Logger?.Information("[remote media] source -> {Source}", settings.MediaSource);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Remote media source change failed"); }
        }

        /// <summary>
        /// The one-time ask before anything remote is fetched. It is a confirm rather than the
        /// "remotemedia" intro card because that card is coaching, not a gate: it returns void,
        /// opens on a later dispatcher pass, is one-shot per install and is suppressed by the
        /// tutorial and by pacing — so anyone who hit one of those would find the toggle simply
        /// refusing to move. The card still shows, right afterwards, with the fuller pitch.
        /// </summary>
        private bool AskRemoteMediaConsent(Models.AppSettings settings)
        {
            var answer = MessageBox.Show(this,
                LocOr("msg_remote_media_consent",
                    "Pull media from Reddit?\n\n" +
                    "The app will stream images and clips from the subreddits you pick, straight from your own machine. " +
                    "Nothing is saved to your disk, nothing is uploaded, and none of it goes through our servers.\n\n" +
                    "It is adult content and it is not curated by us - you choose the niches, and you can block anything you don't want to see again.\n\n" +
                    "Turn it on?"),
                LocOr("title_remote_media_consent", "Use Reddit media?"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes) return false;

            settings.RemoteMediaConsented = true;
            App.Settings?.Save();
            MaybeShowFeatureIntro("remotemedia");
            return true;
        }

        private void RemoteNicheChip_Changed(object sender, RoutedEventArgs e)
        {
            if (_remotePickerSyncing) return;
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null) return;

                var selected = new List<string>();
                foreach (var chip in _remoteNicheChipButtons)
                    if (chip.IsChecked == true && chip.Tag is string id) selected.Add(id);

                // An empty selection is allowed: the coordinator falls back to the first catalog
                // niche rather than serving nothing, so "none" degrades instead of breaking.
                settings.FypOnlineNiches = selected;
                PersistRemoteChannelChange();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Remote niche toggle failed"); }
        }

        private void SliderRemoteRatio_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_remotePickerSyncing) return;
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null) return;

                settings.RemoteMediaRatio = (int)Math.Round(e.NewValue);   // property clamps 5..95
                if (AssetsTab?.TxtRemoteRatio != null)
                    AssetsTab.TxtRemoteRatio.Text = $"{settings.RemoteMediaRatio}%";
                // Dragging fires this per pixel; Save is debounced, so a whole drag is one write.
                App.Settings?.Save();
            }
            catch (Exception ex) { App.Logger?.Debug("Remote ratio change failed: {E}", ex.Message); }
        }

        private void BtnRemoteAddSub_Click(object sender, RoutedEventArgs e) => AddRemoteCustomSub();

        private void TxtRemoteCustomSub_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return) return;
            e.Handled = true;
            AddRemoteCustomSub();
        }

        private void AddRemoteCustomSub()
        {
            try
            {
                var box = AssetsTab?.TxtRemoteCustomSub;
                var settings = App.Settings?.Current;
                if (box == null || settings == null) return;

                var clean = Services.Fyp.Online.FypOnlineCoordinator.SanitizeSub(box.Text);
                if (clean == null)
                {
                    MessageBox.Show(this,
                        LocOr("msg_remote_sub_invalid",
                            "That doesn't look like a subreddit. Try a name like r/EroticHypnosis, or paste its link."),
                        LocOr("title_remote_sub_invalid", "Check that name"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var list = new List<string>(settings.FypOnlineCustomSubs ?? new List<string>());
                if (!list.Any(x => string.Equals(x, clean, StringComparison.OrdinalIgnoreCase)))
                    list.Add(clean);
                settings.FypOnlineCustomSubs = list;
                box.Text = "";

                PersistRemoteChannelChange();
                RefreshRemoteMediaPicker();

                // Adding a sub that is on the blocklist looks like the Add button doing nothing:
                // the block wins, everywhere, by design. Say so rather than silently overriding
                // an earlier "never show me this again".
                if (settings.RemoteMediaBlockedSubs.Any(x => string.Equals(x, clean, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(this,
                        string.Format(LocOr("msg_remote_sub_is_blocked_0",
                            "r/{0} is on your blocklist, so it stays hidden until you unblock it below."), clean),
                        LocOr("title_remote_sub_is_blocked", "Still blocked"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Adding a custom subreddit failed"); }
        }

        private void RemoveRemoteCustomSub(string sub)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var list = new List<string>(settings.FypOnlineCustomSubs ?? new List<string>());
            list.RemoveAll(x => string.Equals(x, sub, StringComparison.OrdinalIgnoreCase));
            settings.FypOnlineCustomSubs = list;

            PersistRemoteChannelChange();
            RefreshRemoteMediaPicker();
        }

        private void UnblockRemoteSub(string sub)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;

            var list = new List<string>(settings.RemoteMediaBlockedSubs);
            list.RemoveAll(x => string.Equals(x, sub, StringComparison.OrdinalIgnoreCase));
            settings.RemoteMediaBlockedSubs = list;

            PersistRemoteChannelChange();
            RefreshRemoteMediaPicker();
            App.Logger?.Information("[remote media] unblocked r/{Sub}", sub);
        }

        private void BtnRemoteClearBlocklist_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = App.Settings?.Current;
                if (settings == null) return;
                if (settings.RemoteMediaBlockedSubs.Count == 0 && settings.RemoteMediaBlockedIds.Count == 0) return;

                var confirm = MessageBox.Show(this,
                    LocOr("msg_remote_clear_blocklist_confirm",
                        "Unblock everything?\n\nEvery subreddit and every individual post you have blocked can show up again. This can't be undone."),
                    LocOr("title_remote_clear_blocklist", "Clear blocklist"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;

                settings.RemoteMediaBlockedSubs = new List<string>();
                settings.RemoteMediaBlockedIds = new List<string>();

                PersistRemoteChannelChange();
                RefreshRemoteMediaPicker();
                App.Logger?.Information("[remote media] blocklist cleared");
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Clearing the remote blocklist failed"); }
        }

        /// <summary>Niche / custom-sub / blocklist edits all land the same way: settings do not
        /// autosave (the lists are replaced, not observed) and every consumer's rotation state
        /// was built from the old set.</summary>
        private void PersistRemoteChannelChange()
        {
            App.Settings?.Save();
            Services.Fyp.Online.FypOnlineCoordinator.ResetAllChannels();
        }

        private void RebuildRemoteCustomSubChips(Models.AppSettings settings)
        {
            var host = AssetsTab?.RemoteCustomSubChips;
            if (host == null) return;

            host.Children.Clear();
            foreach (var sub in (settings.FypOnlineCustomSubs ?? new List<string>()).ToList())
            {
                var name = sub;   // captured per chip, not per loop variable
                host.Children.Add(BuildRemoteChip($"r/{name}",
                    LocOr("tooltip_remote_custom_sub_remove", "Remove this subreddit"),
                    () => RemoveRemoteCustomSub(name)));
            }

            if (host.Children.Count == 0)
                host.Children.Add(MutedRemoteNote(LocOr("label_remote_custom_subs_none",
                    "None yet - the niches above are plenty to start with.")));
        }

        private void RebuildRemoteBlocklist(Models.AppSettings settings, List<string> subs, List<string> ids)
        {
            if (AssetsTab?.TxtRemoteBlocklistSummary is TextBlock summary)
            {
                summary.Text = subs.Count == 0 && ids.Count == 0
                    ? LocOr("label_remote_blocklist_empty",
                        "Nothing blocked yet. Whatever you block here is gone from every part of the app.")
                    : string.Format(LocOr("label_remote_blocklist_summary_0_1",
                        "{0} subreddits and {1} individual posts blocked."), subs.Count, ids.Count);
            }
            if (AssetsTab?.BtnRemoteClearBlocklist != null)
                AssetsTab.BtnRemoteClearBlocklist.IsEnabled = subs.Count > 0 || ids.Count > 0;

            var host = AssetsTab?.RemoteBlockedChips;
            if (host == null) return;

            host.Children.Clear();
            foreach (var sub in subs.ToList())
            {
                var name = sub;
                host.Children.Add(BuildRemoteChip($"r/{name}",
                    LocOr("tooltip_remote_unblock_sub", "Unblock this subreddit"),
                    () => UnblockRemoteSub(name)));
            }
            // Blocked post ids have no name worth showing - they are summarised in the line
            // above and cleared with everything else.
        }

        /// <summary>A removable pill. Same look as the niche toggles, with the companion tab's
        /// ✕ affordance; both styles live in AssetsTabView.xaml's own resources.</summary>
        private Border BuildRemoteChip(string label, string removeTip, Action onRemove)
        {
            var chip = new Border { Style = AssetsTab?.TryFindResource("RemoteChipBorder") as Style };
            var row = new StackPanel { Orientation = Orientation.Horizontal };

            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var close = new Button
            {
                Content = "✕",
                ToolTip = removeTip,
                Style = AssetsTab?.TryFindResource("RemoteChipCloseButton") as Style
            };
            close.Click += (_, _) =>
            {
                try { onRemove(); }
                catch (Exception ex) { App.Logger?.Debug("Remote chip removal failed: {E}", ex.Message); }
            };
            row.Children.Add(close);

            chip.Child = row;
            return chip;
        }

        private static TextBlock MutedRemoteNote(string text) => new()
        {
            Text = text,
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = Application.Current?.TryFindResource("TextMutedBrush") as Brush ?? Brushes.Gray
        };

        #endregion

        #endregion
    }
}
