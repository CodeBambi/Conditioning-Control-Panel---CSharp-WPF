using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Windows;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Content;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Assets partial.
/// Manages the local asset tree, disabled-asset selection, asset presets,
/// and content-pack UI.
/// </summary>
public partial class AssetsTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAppEnvironment? _appEnvironment;
    private readonly IPlatformCapabilities? _platformCapabilities;
    private readonly IFlashService? _flashService;
    private readonly ILogger<AssetsTabViewModel>? _logger;
    private readonly IContentPackService? _contentPackService;

    private static readonly string[] ValidExtensions =
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".avif",
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm"
    };

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
    private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };

    private bool _isUpdatingCheckState;

    // --- Thumbnail decode (off-UI-thread, bounded, LRU-cached, cancellable) ---
    // The old XAML bound each tile's Image.Source to FullPath via PackUriToBitmapConverter,
    // which synchronously ran one full new Bitmap(path) decode PER realized tile on the UI
    // thread. Over a network folder with thousands of files + a non-virtualized WrapPanel,
    // that realized-and-decoded every tile up front and froze the tab. These fields drive
    // the replacement: decode ~120px thumbnails on worker threads, capped to 4 concurrent,
    // cached by full-path+mtime, and cancelled when the selected folder changes.
    private const int ThumbnailDecodeWidth = 120;
    private const int MaxThumbnailCacheEntries = 200;
    private static readonly SemaphoreSlim _thumbnailSemaphore = new(4);
    private static readonly Dictionary<string, ThumbnailCacheEntry> _thumbnailCache = new();
    private static readonly object _thumbnailCacheLock = new();
    private static long _thumbnailAccessCounter;
    private CancellationTokenSource? _thumbnailCts;

    private sealed class ThumbnailCacheEntry
    {
        public Bitmap? Bitmap;
        public long LastAccess;
    }

    public AssetsTabViewModel() : base("assets", "Assets", "📁")
    {
        _assetTree = new ObservableCollection<AssetTreeItem>();
        _currentFiles = new ObservableCollection<AssetFileItem>();
        _availablePacks = new ObservableCollection<PackCardViewModel>();
        _assetPresets = new ObservableCollection<AssetPreset>();
        InitializeDesignTimeData();
    }

    public AssetsTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAppEnvironment appEnvironment,
        IPlatformCapabilities platformCapabilities,
        IFlashService flashService,
        ILogger<AssetsTabViewModel> logger,
        IContentPackService contentPackService) : base("assets", "Assets", "📁")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _appEnvironment = appEnvironment;
        _platformCapabilities = platformCapabilities;
        _flashService = flashService;
        _logger = logger;
        _contentPackService = contentPackService;
        _assetTree = new ObservableCollection<AssetTreeItem>();
        _currentFiles = new ObservableCollection<AssetFileItem>();
        _availablePacks = new ObservableCollection<PackCardViewModel>();
        _assetPresets = new ObservableCollection<AssetPreset>();

        InitializeDefaultPreset();
        _ = RefreshAssetTreeAsync();
        RefreshAssetPresets();
    }

    [ObservableProperty]
    private ObservableCollection<AssetTreeItem> _assetTree;

    [ObservableProperty]
    private ObservableCollection<AssetFileItem> _currentFiles;

    [ObservableProperty]
    private AssetTreeItem? _selectedFolder;

    [ObservableProperty]
    private ObservableCollection<PackCardViewModel> _availablePacks;

    [ObservableProperty]
    private ObservableCollection<AssetPreset> _assetPresets;

    [ObservableProperty]
    private AssetPreset? _selectedAssetPreset;

    [ObservableProperty]
    private string _assetCountsText = "0 images, 0 videos active";

    [ObservableProperty]
    private string _emptyThumbnailsText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPacksSectionVisible;

    [ObservableProperty]
    private bool _hasCurrentFiles;

    private bool _isLoadingPreset;

    partial void OnSelectedFolderChanged(AssetTreeItem? value)
    {
        // Cancel any in-flight thumbnail batch for the previously-selected folder so we do
        // not keep decoding tiles whose items have already been cleared/replaced.
        _thumbnailCts?.Cancel();
        _thumbnailCts = new CancellationTokenSource();
        _ = LoadFolderFilesAsync(value, _thumbnailCts.Token);
    }

    partial void OnSelectedAssetPresetChanged(AssetPreset? value)
    {
        if (_isLoadingPreset || value == null) return;
        _ = ApplyAssetPresetAsync(value);
    }

    #region Asset Tree

    [RelayCommand]
    private async Task RefreshAssetTreeAsync()
    {
        IsBusy = true;
        try
        {
            var assetsPath = _appEnvironment?.EffectiveAssetsPath;
            if (string.IsNullOrWhiteSpace(assetsPath)) return;

            try
            {
                Directory.CreateDirectory(assetsPath);
                Directory.CreateDirectory(Path.Combine(assetsPath, "images"));
                Directory.CreateDirectory(Path.Combine(assetsPath, "videos"));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to ensure asset directories");
            }

            var imagesFolder = Path.Combine(assetsPath, "images");
            var videosFolder = Path.Combine(assetsPath, "videos");
            var basePath = assetsPath;
            var disabled = SnapshotDisabledPaths();
            var imagesLabel = Loc.Get("asset_folder_images");
            var videosLabel = Loc.Get("asset_folder_videos");
            var hasImages = Directory.Exists(imagesFolder);
            var hasVideos = Directory.Exists(videosFolder);

            // Build the recursive folder tree off the UI thread. Directory.EnumerateFiles /
            // GetDirectories over a network drive is the slow path that used to freeze the
            // tab on construction/refresh; the AssetTreeItem graph is plain data and safe to
            // build on a worker, then we attach it to the bound collection in one UI pass.
            AssetTreeItem? imagesNode = null;
            AssetTreeItem? videosNode = null;
            await Task.Run(() =>
            {
                if (hasImages) imagesNode = BuildFolderTree(imagesFolder, imagesLabel, basePath, disabled);
                if (hasVideos) videosNode = BuildFolderTree(videosFolder, videosLabel, basePath, disabled);
            });

            AssetTree.Clear();
            if (imagesNode != null)
            {
                imagesNode.IsExpanded = true;
                AssetTree.Add(imagesNode);
            }
            if (videosNode != null)
            {
                videosNode.IsExpanded = true;
                AssetTree.Add(videosNode);
            }

            AddContentPackVirtualFolders();

            UpdateAssetCounts();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Snapshots the disabled-asset set so off-thread enumeration/classification does not
    /// race with a concurrent UI-thread toggle. StringComparer.Ordinal matches the
    /// DisabledAssetPaths set's semantics.
    /// </summary>
    private HashSet<string>? SnapshotDisabledPaths()
    {
        var src = _settingsService?.Current?.DisabledAssetPaths;
        return src == null ? null : new HashSet<string>(src, StringComparer.Ordinal);
    }

    private void AddContentPackVirtualFolders()
    {
        var activePackIds = _contentPackService?.GetActivePackIds();
        if (activePackIds == null || activePackIds.Count == 0) return;

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
            packsNode.IsChecked = packsNode.Children.Any(c => c.IsChecked);
            AssetTree.Add(packsNode);
        }
    }

    private AssetTreeItem? BuildPackTree(string packId)
    {
        var packFiles = _contentPackService?.GetPackFiles(packId);
        if (packFiles == null || packFiles.Count == 0)
            return null;

        var packs = _contentPackService?.GetBuiltInPacks();
        var packInfo = packs?.FirstOrDefault(p => p.Id == packId);
        var packName = packInfo?.Name ?? packId;

        var disabledPaths = _settingsService?.Current?.DisabledAssetPaths;

        var packNode = new AssetTreeItem
        {
            Name = packName,
            FullPath = "",
            IsPackFolder = true,
            PackId = packId,
            IsChecked = true,
            IsExpanded = false,
            FileCount = packFiles.Count
        };

        var imageFiles = packFiles.Where(f => f.FileType == "image").ToList();
        if (imageFiles.Count > 0)
        {
            var activeImageCount = imageFiles.Count(f => !disabledPaths?.Contains($"pack:{packId}/{f.OriginalName}") ?? true);
            packNode.Children.Add(new AssetTreeItem
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
            });
        }

        var videoFiles = packFiles.Where(f => f.FileType == "video").ToList();
        if (videoFiles.Count > 0)
        {
            var activeVideoCount = videoFiles.Count(f => !disabledPaths?.Contains($"pack:{packId}/{f.OriginalName}") ?? true);
            packNode.Children.Add(new AssetTreeItem
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
            });
        }

        packNode.IsChecked = packNode.Children.Any(c => c.IsChecked);
        return packNode;
    }

    private AssetTreeItem BuildFolderTree(string path, string name, string basePath, HashSet<string>? disabled)
    {
        var node = new AssetTreeItem
        {
            Name = name,
            FullPath = path,
            IsChecked = true
        };

        List<string> files;
        try
        {
            // EnumerateFiles is lazier than GetFiles and keeps peak memory low on huge dirs.
            files = Directory.EnumerateFiles(path)
                .Where(f => ValidExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            _logger?.LogWarning("BuildFolderTree: skipping unreadable folder {Path}: {Error}", path, ex.Message);
            node.UpdateCheckState();
            return node;
        }

        // Classify image/video + active state once, while we already hold the file list,
        // so CountAssetsRecursive can sum stored counts instead of re-enumerating later.
        var imageCount = 0;
        var videoCount = 0;
        var checkedImage = 0;
        var checkedVideo = 0;
        foreach (var f in files)
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            var isImage = ImageExtensions.Contains(ext);
            var isVideo = VideoExtensions.Contains(ext);
            if (isImage) imageCount++;
            if (isVideo) videoCount++;

            var isActive = disabled == null || !disabled.Contains(GetRelativePath(basePath, f));
            if (!isActive) continue;
            if (isImage) checkedImage++;
            if (isVideo) checkedVideo++;
        }

        node.FileCount = files.Count;
        node.ImageCount = imageCount;
        node.VideoCount = videoCount;
        node.CheckedFileCount = checkedImage + checkedVideo;
        node.CheckedImageCount = checkedImage;
        node.CheckedVideoCount = checkedVideo;

        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(path);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            _logger?.LogWarning("BuildFolderTree: cannot enumerate subfolders of {Path}: {Error}", path, ex.Message);
            subDirs = Array.Empty<string>();
        }

        foreach (var dir in subDirs)
        {
            var child = BuildFolderTree(dir, Path.GetFileName(dir), basePath, disabled);
            child.Parent = node;
            node.Children.Add(child);
        }

        node.UpdateCheckState();
        return node;
    }

    private async Task LoadFolderFilesAsync(AssetTreeItem? folder, CancellationToken ct)
    {
        // Invoked on the UI thread from OnSelectedFolderChanged. After each await we resume
        // on the UI thread via the Dispatcher synchronization context, so direct mutations
        // of CurrentFiles (ObservableCollection) and the item INPC properties are safe.
        CurrentFiles.Clear();
        EmptyThumbnailsText = "";

        if (folder == null)
        {
            EmptyThumbnailsText = Loc.Get("label_select_a_folder_to_view_its_contents");
            HasCurrentFiles = false;
            return;
        }

        if (folder.IsPackFolder)
        {
            if (!string.IsNullOrEmpty(folder.PackId) && !string.IsNullOrEmpty(folder.PackFileType))
            {
                LoadPackFolderFiles(folder.PackId, folder.PackFileType);
                return;
            }

            EmptyThumbnailsText = Loc.Get("label_select_a_subfolder_to_view_files");
            HasCurrentFiles = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(folder.FullPath) || !Directory.Exists(folder.FullPath))
        {
            EmptyThumbnailsText = Loc.Get("label_folder_does_not_exist");
            HasCurrentFiles = false;
            return;
        }

        var basePath = _appEnvironment?.EffectiveAssetsPath ?? "";
        var disabled = SnapshotDisabledPaths();
        var fullPath = folder.FullPath;

        List<AssetFileItem> items;
        try
        {
            // Enumerate the folder off the UI thread; a network drive's directory scan is
            // exactly the synchronous I/O that froze the tab before.
            items = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var result = new List<AssetFileItem>();

                IEnumerable<string> raw;
                try
                {
                    raw = Directory.EnumerateFiles(fullPath);
                }
                catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
                {
                    _logger?.LogWarning("LoadFolderFiles: cannot enumerate {Path}: {Error}", fullPath, ex.Message);
                    return result;
                }

                var ordered = raw
                    .Where(f => ValidExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(Path.GetFileName)
                    .ToList();

                foreach (var file in ordered)
                {
                    ct.ThrowIfCancellationRequested();
                    var relativePath = GetRelativePath(basePath, file);
                    var isActive = disabled == null || !disabled.Contains(relativePath);
                    var entry = new AssetFileItem
                    {
                        FullPath = file,
                        RelativePath = relativePath,
                        IsChecked = isActive
                    };
                    try { entry.SizeBytes = new FileInfo(file).Length; }
                    catch { /* size is best-effort */ }
                    result.Add(entry);
                }

                return result;
            }, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LoadFolderFiles failed for {Path}", fullPath);
            EmptyThumbnailsText = Loc.Get("label_no_media_files_in_this_folder");
            HasCurrentFiles = false;
            return;
        }

        if (items.Count == 0)
        {
            EmptyThumbnailsText = Loc.Get("label_no_media_files_in_this_folder");
            HasCurrentFiles = false;
            return;
        }

        // Batch-add in one UI-thread pass so the virtualized panel realizes tiles together.
        foreach (var entry in items)
            CurrentFiles.Add(entry);
        HasCurrentFiles = CurrentFiles.Count > 0;

        // Fire off the thumbnail batch; each task is bounded by _thumbnailSemaphore and
        // observes the folder's cancellation token (cancelled on the next folder change).
        foreach (var entry in items)
            _ = LoadThumbnailAsync(entry, ct);
    }

    private void LoadPackFolderFiles(string packId, string fileType)
    {
        CurrentFiles.Clear();
        EmptyThumbnailsText = "";

        var packFiles = _contentPackService?.GetPackFiles(packId, fileType);
        if (packFiles == null || packFiles.Count == 0)
        {
            EmptyThumbnailsText = Loc.Get("label_no_files_in_this_pack_folder");
            HasCurrentFiles = false;
            return;
        }

        var disabledPaths = _settingsService?.Current?.DisabledAssetPaths;

        foreach (var file in packFiles.OrderBy(f => f.OriginalName))
        {
            var packPath = $"pack:{packId}/{file.OriginalName}";
            var isActive = !(disabledPaths?.Contains(packPath) ?? false);

            var item = new AssetFileItem
            {
                RelativePath = packPath,
                IsChecked = isActive,
                IsPackFile = true,
                PackId = packId,
                PackFileEntry = file,
                Name = file.OriginalName,
                Extension = file.Extension,
                IsVideo = file.FileType == "video",
                IsGif = file.Extension == ".gif"
            };

            CurrentFiles.Add(item);
        }

        HasCurrentFiles = CurrentFiles.Count > 0;
    }

    /// <summary>
    /// Decodes one tile's thumbnail off the UI thread, gated by a bounded semaphore
    /// (max 4 concurrent decodes) and backed by an LRU cache keyed by full path + mtime.
    /// Mirrors the WPF MainWindow.Assets LoadThumbnailAsync pattern. The task is started on
    /// the UI thread and resumes on the UI thread after each await (Dispatcher sync context),
    /// so the INPC writes (item.Thumbnail / IsLoadingThumbnail) are marshalled correctly.
    /// </summary>
    private async Task LoadThumbnailAsync(AssetFileItem item, CancellationToken ct)
    {
        // Videos render the 🎙 overlay (no raster thumbnail to decode).
        if (item.IsVideo)
            return;

        item.IsLoadingThumbnail = true;

        Bitmap? decoded = null;
        try
        {
            await _thumbnailSemaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                decoded = await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    var key = BuildThumbnailKey(item.FullPath);
                    var cached = GetCachedThumbnail(key);
                    if (cached != null)
                        return cached;

                    var bmp = TryDecodeThumbnail(item.FullPath);
                    if (bmp != null)
                        StoreCachedThumbnail(key, bmp);
                    return bmp;
                }, ct);
            }
            finally
            {
                _thumbnailSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Folder changed mid-decode; leave the placeholder state as-is.
            return;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Thumbnail decode failed for {Path}", item.FullPath);
        }

        item.Thumbnail = decoded;
        item.IsLoadingThumbnail = false;
    }

    private static string BuildThumbnailKey(string fullPath)
    {
        try
        {
            var mtime = new FileInfo(fullPath).LastWriteTimeUtc.Ticks;
            return string.Concat(fullPath, "|", mtime);
        }
        catch
        {
            return string.Concat(fullPath, "|0");
        }
    }

    private static Bitmap? GetCachedThumbnail(string key)
    {
        lock (_thumbnailCacheLock)
        {
            if (_thumbnailCache.TryGetValue(key, out var entry))
            {
                entry.LastAccess = Interlocked.Increment(ref _thumbnailAccessCounter);
                return entry.Bitmap;
            }
            return null;
        }
    }

    private static void StoreCachedThumbnail(string key, Bitmap bmp)
    {
        lock (_thumbnailCacheLock)
        {
            if (_thumbnailCache.TryGetValue(key, out var existing))
            {
                existing.Bitmap = bmp;
                existing.LastAccess = Interlocked.Increment(ref _thumbnailAccessCounter);
                return;
            }

            while (_thumbnailCache.Count >= MaxThumbnailCacheEntries)
            {
                var lruKey = _thumbnailCache
                    .Aggregate((a, b) => a.Value.LastAccess <= b.Value.LastAccess ? a : b).Key;
                _thumbnailCache.Remove(lruKey);
            }

            _thumbnailCache[key] = new ThumbnailCacheEntry
            {
                Bitmap = bmp,
                LastAccess = Interlocked.Increment(ref _thumbnailAccessCounter)
            };
        }
    }

    /// <summary>
    /// Decodes a memory-efficient ~120px thumbnail (DecodeToWidth keeps aspect ratio and
    /// only decodes to the requested width). Avalonia Bitmaps are not thread-affine, so this
    /// is safe to run on a worker; the resulting Bitmap is assigned on the UI thread.
    /// </summary>
    private static Bitmap? TryDecodeThumbnail(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath))
                return null;
            using var stream = File.OpenRead(fullPath);
            return Bitmap.DecodeToWidth(stream, ThumbnailDecodeWidth, BitmapInterpolationMode.HighQuality);
        }
        catch
        {
            // Missing/unsupported/corrupt files: silently render no thumbnail.
            return null;
        }
    }

    [RelayCommand]
    private void ToggleFolderCheck(AssetTreeItem? folder)
    {
        if (folder == null || _isUpdatingCheckState) return;

        _isUpdatingCheckState = true;
        try
        {
            SetFolderAndChildrenChecked(folder, folder.IsChecked);
            UpdateFolderFilesCheckState(folder, folder.IsChecked);
            folder.Parent?.UpdateCheckStateFromChildren();
            UpdateAssetCounts();
            RefreshThumbnailCheckboxes();
            InvalidateAssetPools();
        }
        finally
        {
            _isUpdatingCheckState = false;
        }
    }

    [RelayCommand]
    private void ToggleFileCheck(AssetFileItem? file)
    {
        if (file == null) return;
        UpdateFileCheckState(file);
    }

    [RelayCommand]
    private void SelectAllAssets()
    {
        if (SelectedFolder == null) return;
        _isUpdatingCheckState = true;
        try
        {
            UpdateFolderFilesCheckState(SelectedFolder, true);
            SetFolderAndChildrenChecked(SelectedFolder, true);
            SelectedFolder.Parent?.UpdateCheckStateFromChildren();
            RefreshThumbnailCheckboxes();
            UpdateAssetCounts();
            InvalidateAssetPools();
        }
        finally
        {
            _isUpdatingCheckState = false;
        }
    }

    [RelayCommand]
    private void DeselectAllAssets()
    {
        if (SelectedFolder == null) return;
        _isUpdatingCheckState = true;
        try
        {
            UpdateFolderFilesCheckState(SelectedFolder, false);
            SetFolderAndChildrenChecked(SelectedFolder, false);
            SelectedFolder.Parent?.UpdateCheckStateFromChildren();
            RefreshThumbnailCheckboxes();
            UpdateAssetCounts();
            InvalidateAssetPools();
        }
        finally
        {
            _isUpdatingCheckState = false;
        }
    }

    [RelayCommand]
    private async Task SaveAssetSelectionAsync()
    {
        try
        {
            _settingsService?.Save();
            InvalidateAssetPools(fullReload: true);

            var disabledCount = _settingsService?.Current?.DisabledAssetPaths.Count ?? 0;
            var message = disabledCount > 0
                ? string.Format(Loc.Get("msg_selection_saved_disabled_fmt"), disabledCount)
                : Loc.Get("msg_selection_saved_active");

            await (_dialogService?.ShowMessageAsync(Loc.Get("title_selection_saved"), message) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save asset selection");
        }
    }

    private void UpdateFolderFilesCheckState(AssetTreeItem folder, bool isChecked)
    {
        var disabledPaths = _settingsService?.Current?.DisabledAssetPaths;
        if (disabledPaths == null) return;

        if (folder.IsPackFolder && !string.IsNullOrEmpty(folder.PackId) && !string.IsNullOrEmpty(folder.PackFileType))
        {
            var packFiles = _contentPackService?.GetPackFiles(folder.PackId, folder.PackFileType);
            if (packFiles != null)
            {
                foreach (var file in packFiles)
                {
                    var packPath = $"pack:{folder.PackId}/{file.OriginalName}";
                    if (isChecked) disabledPaths.Remove(packPath);
                    else disabledPaths.Add(packPath);
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(folder.FullPath) && Directory.Exists(folder.FullPath))
        {
            var basePath = _appEnvironment?.EffectiveAssetsPath ?? "";
            var files = Directory.GetFiles(folder.FullPath)
                .Where(f => ValidExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            foreach (var file in files)
            {
                var relativePath = GetRelativePath(basePath, file);
                if (isChecked) disabledPaths.Remove(relativePath);
                else disabledPaths.Add(relativePath);
            }
        }

        foreach (var child in folder.Children)
        {
            UpdateFolderFilesCheckState(child, isChecked);
        }

        folder.CheckedFileCount = isChecked ? folder.FileCount : 0;
        folder.CheckedImageCount = isChecked ? folder.ImageCount : 0;
        folder.CheckedVideoCount = isChecked ? folder.VideoCount : 0;
    }

    private void SetFolderAndChildrenChecked(AssetTreeItem folder, bool isChecked)
    {
        folder.IsChecked = isChecked;
        folder.CheckedFileCount = isChecked ? folder.FileCount : 0;
        folder.CheckedImageCount = isChecked ? folder.ImageCount : 0;
        folder.CheckedVideoCount = isChecked ? folder.VideoCount : 0;
        foreach (var child in folder.Children)
        {
            SetFolderAndChildrenChecked(child, isChecked);
        }
    }

    private void UpdateFileCheckState(AssetFileItem file)
    {
        var disabledPaths = _settingsService?.Current?.DisabledAssetPaths;
        if (disabledPaths == null) return;

        if (file.IsChecked) disabledPaths.Remove(file.RelativePath);
        else disabledPaths.Add(file.RelativePath);

        InvalidateAssetPools();

        _isUpdatingCheckState = true;
        try
        {
            UpdateParentFolderCheckState();
            UpdateAssetCounts();
        }
        finally
        {
            _isUpdatingCheckState = false;
        }
    }

    private void UpdateParentFolderCheckState()
    {
        if (SelectedFolder == null) return;
        SelectedFolder.CheckedFileCount = CurrentFiles.Count(f => f.IsChecked);
        SelectedFolder.CheckedImageCount = CurrentFiles.Count(f => f.IsChecked && !f.IsVideo);
        SelectedFolder.CheckedVideoCount = CurrentFiles.Count(f => f.IsChecked && f.IsVideo);
        SelectedFolder.UpdateCheckState();
    }

    private void RefreshThumbnailCheckboxes()
    {
        var disabledPaths = _settingsService?.Current?.DisabledAssetPaths;
        if (disabledPaths == null) return;

        foreach (var item in CurrentFiles)
        {
            item.IsChecked = !disabledPaths.Contains(item.RelativePath);
        }
    }

    private void InvalidateAssetPools(bool fullReload = false)
    {
        try
        {
            if (fullReload)
                _flashService?.LoadAssets();
            else
                _flashService?.ClearFileCache();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "InvalidateAssetPools: flash service cache clear failed");
        }
        _logger?.LogInformation("Asset selection changed; pool invalidation requested (fullReload={FullReload})", fullReload);
    }

    private void UpdateAssetCounts()
    {
        var totalImages = 0;
        var totalVideos = 0;
        var activeImages = 0;
        var activeVideos = 0;
        CountAssetsRecursive(AssetTree, ref totalImages, ref totalVideos, ref activeImages, ref activeVideos);
        AssetCountsText = $"{activeImages} images, {activeVideos} videos active";
    }

    private void CountAssetsRecursive(IEnumerable<AssetTreeItem> items, ref int totalImages, ref int totalVideos, ref int activeImages, ref int activeVideos)
    {
        var disabledPaths = _settingsService?.Current?.DisabledAssetPaths;

        foreach (var folder in items)
        {
            if (folder.IsPackFolder)
            {
                if (!string.IsNullOrEmpty(folder.PackId) && !string.IsNullOrEmpty(folder.PackFileType))
                {
                    var packFiles = _contentPackService?.GetPackFiles(folder.PackId, folder.PackFileType);
                    if (packFiles != null)
                    {
                        foreach (var file in packFiles)
                        {
                            var isImage = file.FileType == "image";
                            var isVideo = file.FileType == "video";
                            if (isImage) totalImages++;
                            if (isVideo) totalVideos++;

                            var packPath = $"pack:{folder.PackId}/{file.OriginalName}";
                            var isActive = !(disabledPaths?.Contains(packPath) ?? false);
                            if (isActive && isImage) activeImages++;
                            if (isActive && isVideo) activeVideos++;
                        }
                    }
                }

                CountAssetsRecursive(folder.Children, ref totalImages, ref totalVideos, ref activeImages, ref activeVideos);
                continue;
            }

            // Disk folders: reuse the image/video counts already classified during the
            // off-thread BuildFolderTree pass (and maintained on toggle). This removes the
            // Directory.GetFiles re-enumeration that used to run on the UI thread here for
            // every refresh / toggle (the duplicate-I/O line called out in the bug report).
            totalImages += folder.ImageCount;
            totalVideos += folder.VideoCount;
            activeImages += folder.CheckedImageCount;
            activeVideos += folder.CheckedVideoCount;

            CountAssetsRecursive(folder.Children, ref totalImages, ref totalVideos, ref activeImages, ref activeVideos);
        }
    }

    #endregion

    #region Asset Presets

    private void InitializeDefaultPreset()
    {
        var presets = _settingsService?.Current?.AssetPresets;
        if (presets == null) return;

        if (!presets.Any(p => p.IsDefault))
        {
            presets.Insert(0, AssetPreset.CreateDefault());
        }

        var defaultPreset = presets.FirstOrDefault(p => p.IsDefault);
        if (defaultPreset != null)
        {
            var (activeImages, activeVideos) = GetCurrentActiveAssetCounts();
            defaultPreset.EnabledImageCount = activeImages;
            defaultPreset.EnabledVideoCount = activeVideos;
        }
    }

    private void RefreshAssetPresets()
    {
        _isLoadingPreset = true;
        AssetPresets.Clear();
        var presets = _settingsService?.Current?.AssetPresets;
        if (presets != null)
        {
            foreach (var preset in presets)
            {
                AssetPresets.Add(preset);
            }
        }

        var currentId = _settingsService?.Current?.CurrentAssetPresetId;
        if (!string.IsNullOrWhiteSpace(currentId))
        {
            SelectedAssetPreset = AssetPresets.FirstOrDefault(p => p.Id == currentId);
        }
        else
        {
            SelectedAssetPreset = AssetPresets.FirstOrDefault(p => p.IsDefault);
        }

        _isLoadingPreset = false;
    }

    private async Task ApplyAssetPresetAsync(AssetPreset preset)
    {
        if (_settingsService?.Current == null) return;
        preset.ApplyToSettings();
        _settingsService.Current.CurrentAssetPresetId = preset.Id;
        _settingsService.Save();

        await RefreshAssetTreeAsync();
        RefreshThumbnailCheckboxes();
        UpdateAssetCounts();
        InvalidateAssetPools();

        var (activeImages, activeVideos) = GetCurrentActiveAssetCounts();
        _logger?.LogInformation("Loaded asset preset: {Name} ({Images} images, {Videos} videos active)",
            preset.Name, activeImages, activeVideos);
    }

    [RelayCommand]
    private async Task SaveAssetPresetAsync()
    {
        if (_settingsService?.Current == null) return;
        var (imageCount, videoCount) = GetCurrentActiveAssetCounts();

        var name = await (_dialogService?.ShowInputDialogAsync(
            Loc.Get("title_save_asset_preset"),
            Loc.Get("msg_enter_a_name_for_your_preset"),
            $"Preset {_settingsService.Current.AssetPresets.Count}") ?? Task.FromResult<string?>(null));
        if (string.IsNullOrWhiteSpace(name)) return;

        var disabledCount = _settingsService.Current.DisabledAssetPaths.Count;
        var preset = AssetPreset.FromCurrentSettings(name.Trim(), imageCount, videoCount);
        _settingsService.Current.AssetPresets.Add(preset);
        _settingsService.Current.CurrentAssetPresetId = preset.Id;
        _settingsService.Save();

        RefreshAssetPresets();
        SelectedAssetPreset = AssetPresets.FirstOrDefault(p => p.Id == preset.Id);
        InvalidateAssetPools();

        await (_dialogService?.ShowMessageAsync(
            "Preset Saved",
            $"Preset '{preset.Name}' saved!\n\n{imageCount} images, {videoCount} videos enabled.\n{disabledCount} assets disabled.") ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task UpdateAssetPresetAsync()
    {
        var preset = SelectedAssetPreset;
        if (preset == null)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_please_select_a_preset_to_update"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        if (preset.IsDefault)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_cannot_update_the_default_all_assets_preset_n"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        var confirm = await (_dialogService?.ShowConfirmationAsync(
            "Update Preset",
            $"Update preset '{preset.Name}' with the current selection?") ?? Task.FromResult(false));
        if (!confirm) return;

        var (imageCount, videoCount) = GetCurrentActiveAssetCounts();
        var disabledCount = _settingsService?.Current?.DisabledAssetPaths.Count ?? 0;
        preset.UpdateFromCurrentSettings(imageCount, videoCount);
        _settingsService?.Save();

        RefreshAssetPresets();
        SelectedAssetPreset = AssetPresets.FirstOrDefault(p => p.Id == preset.Id);
        InvalidateAssetPools();

        await (_dialogService?.ShowMessageAsync(
            "Preset Updated",
            $"Preset '{preset.Name}' updated!\n\n{imageCount} images, {videoCount} videos enabled.\n{disabledCount} assets disabled.") ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task DeleteAssetPresetAsync()
    {
        var preset = SelectedAssetPreset;
        if (preset == null)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_please_select_a_preset_to_delete"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        if (preset.IsDefault)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_cannot_delete_the_default_all_assets_preset"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        var confirm = await (_dialogService?.ShowConfirmationAsync(
            "Delete Preset",
            $"Delete preset '{preset.Name}'?\n\nThis cannot be undone.") ?? Task.FromResult(false));
        if (!confirm) return;

        _settingsService?.Current?.AssetPresets.Remove(preset);
        var defaultPreset = _settingsService?.Current?.AssetPresets.FirstOrDefault(p => p.IsDefault);
        if (_settingsService?.Current != null)
        {
            _settingsService.Current.CurrentAssetPresetId = defaultPreset?.Id;
        }
        _settingsService?.Save();
        RefreshAssetPresets();
    }

    private (int images, int videos) GetCurrentActiveAssetCounts()
    {
        var totalImages = 0;
        var totalVideos = 0;
        var activeImages = 0;
        var activeVideos = 0;
        CountAssetsRecursive(AssetTree, ref totalImages, ref totalVideos, ref activeImages, ref activeVideos);
        return (activeImages, activeVideos);
    }

    #endregion

    #region Content Packs

    [RelayCommand]
    private async Task DeleteDownloadedPacksAsync()
    {
        var installedIds = _contentPackService?.InstalledPacks;
        if (installedIds == null || installedIds.Count == 0)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_delete_downloaded_packs"),
                Loc.Get("msg_no_downloaded_packs_to_delete")) ?? Task.CompletedTask);
            return;
        }

        var confirm = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_delete_downloaded_packs"),
            Loc.GetF("msg_delete_downloaded_packs_confirm_0", installedIds.Count)) ?? Task.FromResult(false));
        if (!confirm) return;

        foreach (var packId in installedIds.ToList())
        {
            _contentPackService?.UninstallPack(packId);
        }

        await RefreshPacksAsync();
    }

    [RelayCommand]
    private void TogglePacksSection()
    {
        IsPacksSectionVisible = !IsPacksSectionVisible;
    }

    [RelayCommand]
    private async Task RefreshPacksAsync()
    {
        IsBusy = true;
        try
        {
            _logger?.LogInformation("Refreshing content packs");
            AvailablePacks.Clear();

            if (_contentPackService == null)
                return;

            var packs = await _contentPackService.GetAvailablePacksAsync();
            foreach (var pack in packs)
            {
                var packVm = new PackCardViewModel(pack)
                {
                    InstallCommand = TogglePackCommand,
                    ActivateCommand = ActivatePackCommand,
                    InstallCommandParameter = null,
                    ActivateCommandParameter = null
                };

                if (pack.IsDownloaded)
                {
                    try
                    {
                        var previews = _contentPackService.GetPackPreviewImages(pack.Id, count: 1, width: 240, height: 100);
                        if (previews.Count > 0 && previews[0] is Bitmap bitmap)
                        {
                            packVm.SetPreviewImage(bitmap);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to load preview for pack {PackId}", pack.Id);
                    }
                }

                AvailablePacks.Add(packVm);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to refresh packs");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task TogglePackAsync(PackCardViewModel? pack)
    {
        if (pack == null || _contentPackService == null) return;
        _logger?.LogInformation("Pack toggle requested: {PackId}", pack.Id);

        try
        {
            if (pack.IsDownloaded)
            {
                var confirm = await (_dialogService?.ShowConfirmationAsync(
                    Loc.Get("title_uninstall_content_pack"),
                    string.Format(Loc.Get("msg_uninstall_content_pack_confirm_fmt"), pack.Name)) ?? Task.FromResult(false));
                if (!confirm) return;

                _contentPackService.UninstallPack(pack.Id);
            }
            else if (pack.IsExternal)
            {
                var confirm = await (_dialogService?.ShowConfirmationAsync(
                    Loc.Get("title_external_content_pack"),
                    string.Format(Loc.Get("msg_external_content_pack_confirm_fmt"), pack.Name)) ?? Task.FromResult(false));
                if (!confirm) return;

                var url = await _contentPackService.GetExternalPackDownloadUrlAsync(pack.Id);
                if (string.IsNullOrEmpty(url))
                {
                    await (_dialogService?.ShowMessageAsync(
                        Loc.Get("title_authentication_required"),
                        Loc.Get("msg_authentication_required_packs")) ?? Task.CompletedTask);
                    return;
                }

                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            else
            {
                var sizeStr = pack.Pack.SizeBytes > 0 ? $" ({pack.Pack.SizeBytes / (1024.0 * 1024):F0} MB)" : "";
                var confirm = await (_dialogService?.ShowConfirmationAsync(
                    Loc.Get("title_install_content_pack"),
                    string.Format(Loc.Get("msg_install_content_pack_confirm_fmt"), pack.Name, sizeStr)) ?? Task.FromResult(false));
                if (!confirm) return;

                var progress = new Progress<int>(p => pack.Pack.DownloadProgress = p);
                await _contentPackService.InstallPackAsync(pack.Pack, progress);
            }

            await RefreshPacksAsync();
        }
        catch (UnauthorizedAccessException)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_authentication_required"),
                Loc.Get("msg_authentication_required_packs")) ?? Task.CompletedTask);
        }
        catch (PackRateLimitException ex)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_rate_limit_exceeded"),
                string.Format(Loc.Get("msg_rate_limited_pack_fmt"), ex.ResetTime)) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to toggle pack {PackId}", pack.Id);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_pack_install_failed")) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task ActivatePackAsync(PackCardViewModel? pack)
    {
        if (pack == null || _contentPackService == null) return;
        _logger?.LogInformation("Pack activation requested: {PackId}", pack.Id);

        if (!pack.IsDownloaded)
            return;

        if (pack.IsActive)
            _contentPackService.DeactivatePack(pack.Id);
        else
            _contentPackService.ActivatePack(pack.Id);

        await RefreshPacksAsync();
    }

    #endregion

    #region Actions

    [RelayCommand]
    private async Task OpenAssetsFolderAsync()
    {
        var path = _appEnvironment?.EffectiveAssetsPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            Directory.CreateDirectory(Path.Combine(path, "images"));
            Directory.CreateDirectory(Path.Combine(path, "videos"));

            if (_platformCapabilities?.IsWindows == true)
            {
                Process.Start("explorer.exe", path);
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }

            _logger?.LogInformation("Opened assets folder: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to open assets folder");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task OpenAssetPreviewAsync(AssetFileItem? file)
    {
        if (file == null) return;
        _logger?.LogInformation("Open asset preview requested: {File}", file.Name);

        string? filePath = file.FullPath;

        if (file.IsPackFile && file.PackFileEntry != null && !string.IsNullOrEmpty(file.PackId))
        {
            filePath = _contentPackService?.GetPackFileTempPath(file.PackId, file.PackFileEntry);
            if (string.IsNullOrEmpty(filePath))
            {
                _logger?.LogWarning("Failed to extract pack file for preview: {File}", file.Name);
                return;
            }
        }

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            _logger?.LogWarning("File not found for preview: {File}", filePath ?? file.Name);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var previewWindow = new MiniPlayerWindow();
            previewWindow.LoadFile(filePath!);
            previewWindow.Show();
        });
    }

    [RelayCommand]
    private async Task OpenAssetInExplorerAsync(AssetFileItem? file)
    {
        if (file == null || !File.Exists(file.FullPath)) return;
        try
        {
            if (_platformCapabilities?.IsWindows == true)
            {
                Process.Start("explorer.exe", $"/select,\"{file.FullPath}\"");
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.GetDirectoryName(file.FullPath),
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to open file in Explorer");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task OpenCreatorDiscordAsync()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://discord.gg/YxVAMt4qaZ",
                UseShellExecute = true
            });
            _logger?.LogInformation("Opened creator Discord link");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to open Discord link");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    #endregion

    #region Design-Time Data

    private void InitializeDesignTimeData()
    {
        if (!global::Avalonia.Controls.Design.IsDesignMode) return;

        AssetCountsText = "12 images, 4 videos active";
        EmptyThumbnailsText = Loc.Get("label_select_a_folder_to_view_its_contents");

        var samplePack = new PackCardViewModel(new ContentPack
        {
            Id = "sample-pack",
            Name = "Sample Pack",
            Description = "A sample content pack for design-time preview.",
            SizeBytes = 15 * 1024 * 1024,
            ImageCount = 24,
            VideoCount = 6,
            IsDownloaded = true,
            IsActive = true
        })
        {
            InstallCommand = TogglePackCommand,
            ActivateCommand = ActivatePackCommand,
            InstallCommandParameter = null,
            ActivateCommandParameter = null
        };
        AvailablePacks.Add(samplePack);

        var externalPack = new PackCardViewModel(new ContentPack
        {
            Id = "external-pack",
            Name = "External Pack",
            Description = "Manual download pack preview.",
            SizeBytes = 42 * 1024 * 1024,
            ImageCount = 50,
            VideoCount = 10,
            IsExternalFlag = true,
            ExternalUrl = "https://example.com/pack"
        })
        {
            InstallCommand = TogglePackCommand,
            ActivateCommand = ActivatePackCommand
        };
        AvailablePacks.Add(externalPack);

        var treeRoot = new AssetTreeItem
        {
            Name = Loc.Get("asset_folder_images"),
            FullPath = "/assets/images",
            FileCount = 12,
            CheckedFileCount = 10,
            IsExpanded = true
        };
        treeRoot.Children.Add(new AssetTreeItem
        {
            Name = "spirals",
            FullPath = "/assets/images/spirals",
            FileCount = 5,
            CheckedFileCount = 5
        });
        AssetTree.Add(treeRoot);

        AssetTree.Add(new AssetTreeItem
        {
            Name = Loc.Get("asset_folder_videos"),
            FullPath = "/assets/videos",
            FileCount = 4,
            CheckedFileCount = 4
        });

        AssetPresets.Add(AssetPreset.CreateDefault());
        AssetPresets.Add(new AssetPreset
        {
            Id = "preset-1",
            Name = "Only Spirals",
            EnabledImageCount = 10,
            EnabledVideoCount = 0
        });
        SelectedAssetPreset = AssetPresets.FirstOrDefault();
    }

    #endregion

    private static string GetRelativePath(string basePath, string filePath)
    {
        var relative = string.IsNullOrWhiteSpace(basePath)
            ? filePath
            : Path.GetRelativePath(basePath, filePath);
        return relative.Replace('\\', '/');
    }
}
