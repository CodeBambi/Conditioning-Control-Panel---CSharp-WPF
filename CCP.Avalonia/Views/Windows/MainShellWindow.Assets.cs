// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.Assets.cs (2911 lines).
// Sorted member by member against the fifteen Core seams. Only the tab's own entry point is real;
// everything else is held back, and the two reasons below are different in kind.
//
// The remote-media picker is PORTED (sync6-media-picker) in CCP.Avalonia/Views/Tabs/AssetsTabView.MediaPicker.cs,
// with the consent ask awaited before MediaSource is written and ResetAllChannels on every change.
// InvalidateAssetPoolsAfterSelectionChange has no target yet: no flash/video service on this head
// consumes online media (open gap, parity row views-tab-assets).
//
// PORTED to the tab itself (CCP.Avalonia/Views/Tabs/AssetsTabView.axaml.cs, entered from
// OnTabShown "assets"): RefreshAssetTree (local images/videos), BuildFolderTree, the folder and
// thumbnail checks, Select/Deselect All, thumbnails (images; videos keep the placeholder), preview
// and reveal, the asset counts and the preset combo + Save As/Update/Delete. AssetPresetService
// .OnlineChannelsReset is seeded in App startup (main 03af6e8bb). Still missing from the tree: the
// Content Packs node (BuildPackTree, App.ContentPacks) - no pack service on this head.
//
// The rest, by blocker:
//   App.ContentPacks - RefreshPacksAsync, BtnRefreshPacks_Click, BtnPackDownload_Click,
//     BtnPackActivate_Click, BtnPackUpgrade_Click, BtnDeleteDownloadedPacks_Click, and the four
//     OnPack* progress/auth/rate-limit callbacks.
//   Pack thumbnails - LoadPackFolderThumbnails, LoadPackThumbnailAsync (App.ContentPacks).
//   The OS - BtnCreatorDiscord_Click, BtnPackPatreon_Click (pack cards only).
//   Pack preview rotation - StartPackPreviewRotation, StopPackPreviewRotation, LoadPreviewImagesFromUrlsAsync,
//     GetPackPreviewFileStem (pure, and held with the rotation that is its only caller).
//   Phrase presets - InitializePhrasePresets and its four handlers (no control on this tab).
//   WPF input - PacksScrollViewer_PreviewMouseWheel, HorizontalScrollViewer_PreviewMouseWheel,
//     InnerScrollViewer_PreviewMouseWheel. Avalonia has no PreviewMouseWheel; these are the
//     nested-scroller workaround and need re-deriving, not porting.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The rail's Library door. One ShowTab call, exactly as in WPF
        /// (MainWindow.Assets.cs:39); the tab refreshes itself from OnTabShown.</summary>
        private void BtnAssets_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => ShowTab("assets");
    }
}
