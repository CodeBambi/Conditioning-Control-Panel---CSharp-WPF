using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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
    /// ponytail: the rest needs MainWindow (asset scan, pack install, preset CRUD, remote media
    /// picker), wired when they move to Core. The wiring points, all named in the XAML:
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
            DataContext = new AssetsTabViewModel();
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
        /// <para>ponytail: WPF also fires App.Bark.NotifyUiAction("open_assets") first. BarkService
        /// has no Core seam, so the line is dropped rather than faked - a missing bark costs a
        /// voiced reaction, never a file.</para>
        /// </summary>
        private async void BtnOpenAssetsFolder_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var assets = CorePaths.EffectiveAssets;
                Directory.CreateDirectory(Path.Combine(assets, "images"));
                Directory.CreateDirectory(Path.Combine(assets, "videos"));
                var launcher = TopLevel.GetTopLevel(this)?.Launcher;
                if (launcher != null) await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(assets));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open the assets folder");
            }
        }
    }

    /// <summary>
    /// Placeholder content so the view DRAWS its templated regions. The real lists come from
    /// MainWindow.Assets.cs (asset scan, ContentPackService, AppSettings.AssetPresets), which is
    /// still in the WPF head; filling them is a separate change from proving the view renders.
    /// </summary>
    public sealed class AssetsTabViewModel
    {
        // The packs section is IsVisible="False" in the original ("most packs live outside the app
        // now"), so these never draw unless someone flips it - they exist so the card template is
        // compiled against a real type and can be proved by temporarily un-hiding the section.
        public IReadOnlyList<PackCardViewModel> Packs { get; } = new[]
        {
            new PackCardViewModel { Name = "Starter Pack", Description = "A small first-run set: soft imagery and two short loops.", SizeDisplay = "42 MB", ImageCount = 120, VideoCount = 4, IsDownloaded = true },
            new PackCardViewModel { Name = "Deep Focus", Description = "Slow spirals and long-form video for extended sessions.", SizeDisplay = "310 MB", ImageCount = 640, VideoCount = 22 },
            new PackCardViewModel { Name = "Community Mix", Description = "Hosted off-site; download it yourself and drop it in.", SizeDisplay = "1.2 GB", ImageCount = 2100, VideoCount = 90, IsExternal = true },
        };

        public IReadOnlyList<AssetFolderViewModel> Folders { get; } = new[]
        {
            new AssetFolderViewModel("Assets", 0, isExpanded: true, isChecked: true, children: new[]
            {
                new AssetFolderViewModel("Images", 842, isChecked: true),
                new AssetFolderViewModel("Videos", 37),
                new AssetFolderViewModel("Starter Pack", 124, isChecked: true),
            }),
        };

        public IReadOnlyList<AssetThumbnailViewModel> Thumbnails { get; } = new[]
        {
            new AssetThumbnailViewModel("spiral_01.png", isChecked: true),
            new AssetThumbnailViewModel("spiral_02.png"),
            new AssetThumbnailViewModel("loop_soft.mp4", isVideo: true),
            new AssetThumbnailViewModel("drop_03.png", isChecked: true),
            new AssetThumbnailViewModel("caption_long_filename_that_trims.png"),
            new AssetThumbnailViewModel("loading_now.png", isLoading: true),
        };

        public bool HasNoThumbnails => Thumbnails.Count == 0;

        public IReadOnlyList<AssetPresetViewModel> Presets { get; } = new[]
        {
            new AssetPresetViewModel("everything", "Everything"),
            new AssetPresetViewModel("images-only", "Images only"),
            new AssetPresetViewModel("starter", "Starter Pack"),
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

    /// <summary>A folder row in the asset tree.</summary>
    public sealed class AssetFolderViewModel
    {
        public AssetFolderViewModel(string name, int fileCount, bool isExpanded = false,
            bool isChecked = false, IReadOnlyList<AssetFolderViewModel>? children = null)
        {
            Name = name;
            FileCount = fileCount;
            IsExpanded = isExpanded;
            IsChecked = isChecked;
            Children = children ?? System.Array.Empty<AssetFolderViewModel>();
        }

        public string Name { get; }
        public int FileCount { get; }
        public bool IsExpanded { get; set; }
        public bool IsChecked { get; set; }
        public IReadOnlyList<AssetFolderViewModel> Children { get; }
        public string FileCountDisplay => FileCount > 0 ? $"({FileCount})" : "";
    }

    /// <summary>One tile in the thumbnail grid.</summary>
    public sealed class AssetThumbnailViewModel
    {
        public AssetThumbnailViewModel(string name, bool isVideo = false, bool isChecked = false, bool isLoading = false)
        {
            Name = name;
            IsVideo = isVideo;
            IsChecked = isChecked;
            IsLoadingThumbnail = isLoading;
        }

        public string Name { get; }
        public bool IsVideo { get; }
        public bool IsChecked { get; set; }
        public bool IsLoadingThumbnail { get; }
        /// <summary>Decoded off the file by MainWindow.Assets.cs; null here.</summary>
        public IImage? Thumbnail => null;
    }

    /// <summary>An entry in the preset combo. WPF used DisplayMemberPath/SelectedValuePath, which
    /// Avalonia has neither of, so both live on the item.</summary>
    public sealed class AssetPresetViewModel
    {
        public AssetPresetViewModel(string id, string displayText)
        {
            Id = id;
            DisplayText = displayText;
        }

        public string Id { get; }
        public string DisplayText { get; }
    }
}
