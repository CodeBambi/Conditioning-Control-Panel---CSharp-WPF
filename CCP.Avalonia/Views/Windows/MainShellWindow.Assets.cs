// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.Assets.cs (2911 lines).
// Sorted member by member against the fifteen Core seams. Only the tab's own entry point is real;
// everything else is held back, and the two reasons below are different in kind.
//
// A REFUSAL, NOT A WAIT - the remote-media picker (InitializeRemoteMediaPicker,
// BuildRemoteSourceChips, BuildRemoteNicheChips, RefreshRemoteMediaPicker, RemoteSourceChip_Changed,
// RemoteNicheChip_Changed, AskRemoteMediaConsent, SliderRemoteRatio_Changed, AddRemoteCustomSub,
// RemoveRemoteCustomSub, ToggleRemoteSubSelection, PersistRemoteChannelChange,
// RebuildRemoteCustomSubChips, RebuildRemoteNicheSubs, BuildRemoteChip, MutedRemoteNote,
// EndRemoteSubProbe, ShowRemoteSubError, TxtRemoteCustomSub_KeyDown, BtnRemoteAddSub_Click and the
// MediaSrc* / RemoteCustomSubCap constants). Two things make a partial port worse than the stub:
//   1. AskRemoteMediaConsent is a SYNCHRONOUS gate. RemoteSourceChip_Changed writes
//      settings.MediaSource only if MessageBox.Show came back Yes (MainWindow.Assets.cs:2353-2360).
//      This head's MessageDialog.ShowDialog<T> is async, so a straight transcription flips the chip
//      and returns before the answer lands - a consent gate that is SKIPPED rather than degraded,
//      on the one switch that starts fetching third-party adult content.
//   2. Even with the ask solved, the switch calls FypOnlineCoordinator.ResetAllChannels()
//      (ConditioningControlPanel/Services/Fyp/Online/FypOnlineCoordinator.cs) and
//      InvalidateAssetPoolsAfterSelectionChange(). Without both, the media services keep serving
//      pools built for the OLD source while the picker says the new one is live.
// The picker comes back with the coordinator and an async consent gate, together, or not at all.
//
// A WAIT - the asset tree, and worth being exact because one dependency has already moved.
// AssetTreeItem IS in Core (CCP.Core/Models/AssetTreeItem.cs), CorePaths.EffectiveAssets is the
// asset root, and DisabledAssetPaths is a field of CoreSettings.Current. BuildFolderTree therefore
// transcribes line for line, and is still left out because its only caller cannot be honest:
// RefreshAssetTree's third branch is the Content Packs node, built from
// App.ContentPacks.GetActivePackIds (ConditioningControlPanel/Services/Content/ContentPackService.cs), and
// a tree that quietly omits every installed pack's media reads to the user as "those files are
// gone" rather than "packs are not ported". It comes back with the pack service, in one piece.
// The same holds for RefreshAssetTree, BuildPackTree, UpdateAssetCounts, CountAssetsRecursive,
// RecalculateFolderCheckState, RecalculateAllFolderCheckStates, SetFolderAndChildrenChecked,
// UpdateFolderFilesCheckState, UpdateFileCheckState, UpdateParentFolderCheckState,
// FolderCheckBox_Changed, ThumbnailCheckBox_Changed, AssetTreeView_SelectedItemChanged,
// BtnSelectAllAssets_Click, BtnDeselectAllAssets_Click, BtnSaveAssetSelection_Click and
// InvalidateAssetPoolsAfterSelectionChange.
//
// Checked and NOT the blocker anywhere in this file: CoreModArt. It answers a mod's art override
// as a path, and this partial reads no mod art at all - its thumbnails come from the user's own
// library and from downloaded packs. CorePaths.EffectiveAssets IS the right root for the tree, and
// is named above rather than wired, because nothing here is wired yet.
//
// The rest, by blocker:
//   App.ContentPacks - RefreshPacksAsync, BtnRefreshPacks_Click, BtnPackDownload_Click,
//     BtnPackActivate_Click, BtnPackUpgrade_Click, BtnDeleteDownloadedPacks_Click, and the four
//     OnPack* progress/auth/rate-limit callbacks.
//   Thumbnail decode + cache - LoadPackFolderThumbnails, LoadPackThumbnailAsync,
//     LoadFolderThumbnails, LoadThumbnailAsync, RefreshThumbnailCheckboxes and the six static cache
//     fields. WPF decodes to BitmapImage with DecodePixelWidth; the Avalonia twin is
//     new Avalonia.Media.Imaging.Bitmap(path), so this is a rewrite rather than a move, and it
//     belongs with the tree that would display it.
//   The OS - BtnOpenAssetsFolder_Click (Process.Start("explorer.exe")), ThumbnailItem_OpenInExplorer_Click,
//     BtnCreatorDiscord_Click, BtnGetPacks_Click, BtnPackPatreon_Click. The shell already ships the
//     folder PICKER (MainShellWindow.Settings.cs, RequestPickAssetsFolder); what is missing is the
//     "show it to me" half, which is a file-manager launch and belongs in a head helper, not here.
//   Preview windows - OpenAssetPreview, ThumbnailItem_Click, ThumbnailItem_Preview_Click,
//     StartPackPreviewRotation, StopPackPreviewRotation, LoadPreviewImagesFromUrlsAsync,
//     GetPackPreviewFileStem (pure, and held with the rotation that is its only caller).
//   Preset dropdowns - InitializeAssetPresets, RefreshAssetPresetsComboBox,
//     CmbAssetPresets_SelectionChanged, BtnSaveAssetPreset_Click, BtnUpdateAssetPreset_Click,
//     BtnDeleteAssetPreset_Click, UpdatePresetCountsFromCurrentState, CountEnabledFilesRecursive,
//     and the phrase-preset five. Blocked on the asset tree above, which is what they count.
//   WPF input - PacksScrollViewer_PreviewMouseWheel, HorizontalScrollViewer_PreviewMouseWheel,
//     InnerScrollViewer_PreviewMouseWheel. Avalonia has no PreviewMouseWheel; these are the
//     nested-scroller workaround and need re-deriving, not porting.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The rail's Library door. One ShowTab call, exactly as in WPF
        /// (MainWindow.Assets.cs:39). The tab it opens is still an empty shell.</summary>
        private void BtnAssets_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => ShowTab("assets");
    }
}
