using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/AssetsTabView.xaml.cs.
    ///
    /// The WPF code-behind holds NO view logic: every one of its handlers is a two-line forward to
    /// <c>MainWindow</c> (<c>Window.GetWindow(this) is MainWindow mw</c> -> <c>mw.Whatever(...)</c>),
    /// and the tab's real behaviour lives in MainWindow.Assets.cs. So there is nothing here that
    /// "only touches the view" to port, and no handler is wired in the XAML.
    ///
    /// TWO EXCEPTIONS, both restored below. <c>BtnMediaLog_Click</c> is the only handler in the
    /// WPF file that does NOT forward - it opens MediaHistoryWindow itself, and that window is
    /// ported at CCP.Avalonia/Views/Windows/MediaHistoryWindow.axaml.cs with nothing on this head
    /// opening it until now. <c>BtnOpenAssetsFolder</c> forwards, but the whole of what it forwards
    /// to is two Directory.CreateDirectory calls and a shell open, and CorePaths.EffectiveAssets is
    /// the path.
    ///
    /// The asset browser (scan, selection, presets) is ported in AssetsTabView.Browser.cs.
    /// ponytail: pack install still needs ContentPackService on this head. Unwired, named in the XAML:
    ///   BtnRefreshAssets / BtnRefreshPacks / BtnGetPacks /
    ///   BtnDeleteDownloadedPacks /
    ///   BtnSelectAllAssets / BtnDeselectAllAssets / BtnSaveAssetPreset / BtnUpdateAssetPreset /
    ///   BtnDeleteAssetPreset / CmbAssetPresets.SelectionChanged / AssetTreeView.SelectionChanged /
    ///   FolderCheckBox / ThumbnailCheckBox / ThumbnailItem click + context menu /
    ///   BtnPackDownload / BtnPackActivate / BtnCreatorDiscord / PacksScrollViewer wheel-to-pan /
    ///   the PackCard and AssetTreeRow hover FX, and IsVisibleChanged -> OnAssetsTabVisibilityChanged
    ///   (the Media Log unseen-entries pulse; the tab has no ambient loop).
    /// </summary>
    public partial class AssetsTabView : UserControl
    {
        public AssetsTabView()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: Load leaves every generated x:Name
            // field null, so a later `BtnMediaLog?.Focus()` would compile and silently do nothing
            // (CLAUDE.md trap 7). Nothing here dereferences one yet; the handlers below take their
            // sender, and this is the ctor a reader will copy.
            InitializeComponent();
            DataContext = Browser;
            InitializeLibraryPicker();   // the media picker + Folders chip (AssetsTabView.MediaPicker.cs)
            InitializeAssetBrowser();    // folder tree, thumbnails, presets (AssetsTabView.Browser.cs)
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
    /// The Library page's live state. Filled by <see cref="AssetsTabView"/> (AssetsTabView.Browser.cs,
    /// the port of WPF 7.1.5 MainWindow.Assets.cs): the folder tree is built from the real assets
    /// folder, the thumbnails from the selected folder. NO sample rows: a page with nothing in it
    /// shows the empty line, never placeholder files (owner bug 2026-10-09: fake spiral_01.png tiles
    /// and "Images (842)" leaked from the render-proof data that used to live here).
    /// </summary>
    public sealed class AssetsTabViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>The packs strip is IsVisible="False" on 7.1.5 and the pack service is not on
        /// this head: empty, never sample cards.</summary>
        public IReadOnlyList<PackCardViewModel> Packs { get; } = Array.Empty<PackCardViewModel>();

        public ObservableCollection<AssetTreeItem> Folders { get; } = new();

        public ObservableCollection<AssetThumbnailViewModel> Thumbnails { get; } = new();

        private string _emptyText = "";
        /// <summary>The line over an empty grid (WPF TxtThumbnailsEmpty.Text).</summary>
        public string EmptyText
        {
            get => _emptyText;
            set { if (_emptyText != value) { _emptyText = value; Raise(nameof(EmptyText)); } }
        }

        private bool _showEmpty = true;
        /// <summary>WPF toggles TxtThumbnailsEmpty.Visibility by hand; same rules here.</summary>
        public bool ShowEmpty
        {
            get => _showEmpty;
            set { if (_showEmpty != value) { _showEmpty = value; Raise(nameof(ShowEmpty)); } }
        }

        private string _countsText = "0 images, 0 videos active";
        /// <summary>WPF UpdateAssetCounts: "{active} images, {active} videos active" (English there too).</summary>
        public string CountsText
        {
            get => _countsText;
            set { if (_countsText != value) { _countsText = value; Raise(nameof(CountsText)); } }
        }
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

    /// <summary>One tile in the thumbnail grid (WPF AssetFileItem).</summary>
    public sealed class AssetThumbnailViewModel : INotifyPropertyChanged
    {
        private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm" };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public AssetThumbnailViewModel(string fullPath, string relativePath, bool isChecked)
        {
            FullPath = fullPath;
            RelativePath = relativePath;
            _isChecked = isChecked;
        }

        public string FullPath { get; }
        /// <summary>The key in DisabledAssetPaths: path under the assets root, forward slashes.</summary>
        public string RelativePath { get; }
        public string Name => Path.GetFileName(FullPath);
        public bool IsVideo => Array.IndexOf(VideoExtensions, Path.GetExtension(FullPath).ToLowerInvariant()) >= 0;
        public long SizeBytes { get; set; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; Raise(nameof(IsChecked)); } }
        }

        private bool _isLoadingThumbnail;
        public bool IsLoadingThumbnail
        {
            get => _isLoadingThumbnail;
            set { if (_isLoadingThumbnail != value) { _isLoadingThumbnail = value; Raise(nameof(IsLoadingThumbnail)); } }
        }

        private IImage? _thumbnail;
        /// <summary>Decoded off the UI thread at 100 px wide (WPF DecodePixelWidth = 100).</summary>
        public IImage? Thumbnail
        {
            get => _thumbnail;
            set { if (!ReferenceEquals(_thumbnail, value)) { _thumbnail = value; Raise(nameof(Thumbnail)); } }
        }
    }
}
