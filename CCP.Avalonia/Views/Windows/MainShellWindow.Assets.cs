// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.Assets.cs (2911 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (106):
//   private ObservableCollection<AssetTreeItem> _assetTree
//   private ObservableCollection<AssetFileItem> _currentFolderFiles
//   private AssetTreeItem? _selectedFolder
//   private void BtnAssets_Click(…)
//   internal void BtnOpenAssetsFolder_Click(…)
//   internal void BtnDeleteDownloadedPacks_Click(…)
//   private async Task RefreshPacksAsync(…)
//   private void StartPackPreviewRotation(…)
//   private void StopPackPreviewRotation(…)
//   private async Task<List<BitmapImage>> LoadPreviewImagesFromUrlsAsync(…)
//   private static string GetPackPreviewFileStem(…)
//   private void OnPackDownloadProgress(…)
//   private void OnPackDownloadCompleted(…)
//   private void OnPackAuthenticationRequired(…)
//   private void OnPackRateLimitExceeded(…)
//   internal void BtnCreatorDiscord_Click(…)
//   internal void BtnGetPacks_Click(…)
//   internal void PacksScrollViewer_PreviewMouseWheel(…)
//   internal void HorizontalScrollViewer_PreviewMouseWheel(…)
//   internal void InnerScrollViewer_PreviewMouseWheel(…)
//   internal void BtnRefreshPacks_Click(…)
//   private void RefreshAssetTree(…)
//   private AssetTreeItem? BuildPackTree(…)
//   private AssetTreeItem BuildFolderTree(…)
//   internal void AssetTreeView_SelectedItemChanged(…)
//   private void RecalculateFolderCheckState(…)
//   private void RecalculateAllFolderCheckStates(…)
//   private void LoadPackFolderThumbnails(…)
//   private const int MaxThumbnailCacheEntries
//   private const long MaxThumbnailCacheBytes
//   private static readonly Dictionary<string, ImageSource> _packThumbnailCache
//   private static readonly Dictionary<string, long> _packThumbnailLastAccess
//   private static readonly Dictionary<string, long> _packThumbnailSizes
//   private static long _packThumbnailCacheBytes
//   private static long _packThumbnailAccessCounter
//   private static readonly SemaphoreSlim _thumbnailSemaphore
//   private async Task LoadPackThumbnailAsync(…)
//   private void LoadFolderThumbnails(…)
//   private async Task LoadThumbnailAsync(…)
//   private bool _isUpdatingFolderCheckState
//   internal void FolderCheckBox_Changed(…)
//   private void SetFolderAndChildrenChecked(…)
//   private void RefreshThumbnailCheckboxes(…)
//   private void UpdateFolderFilesCheckState(…)
//   internal void ThumbnailCheckBox_Changed(…)
//   internal void ThumbnailItem_Click(…)
//   internal void ThumbnailItem_Preview_Click(…)
//   internal void ThumbnailItem_OpenInExplorer_Click(…)
//   private void OpenAssetPreview(…)
//   private void UpdateFileCheckState(…)
//   private void UpdateParentFolderCheckState(…)
//   private void InvalidateAssetPoolsAfterSelectionChange(…)
//   internal void BtnSelectAllAssets_Click(…)
//   internal void BtnDeselectAllAssets_Click(…)
//   private void BtnSaveAssetSelection_Click(…)
//   private bool _isLoadingPreset
//   private void InitializeAssetPresets(…)
//   private void UpdatePresetCountsFromCurrentState(…)
//   private void CountEnabledFilesRecursive(…)
//   private void RefreshAssetPresetsComboBox(…)
//   internal void CmbAssetPresets_SelectionChanged(…)
//   internal void BtnSaveAssetPreset_Click(…)
//   internal void BtnUpdateAssetPreset_Click(…)
//   internal void BtnDeleteAssetPreset_Click(…)
//   private bool _isLoadingPhrasePreset
//   private void InitializePhrasePresets(…)
//   private void RefreshPhrasePresetsComboBox(…)
//   internal void CmbPhrasePresets_SelectionChanged(…)
//   internal void BtnSavePhrasePreset_Click(…)
//   internal void BtnDeletePhrasePreset_Click(…)
//   private void UpdateAssetCounts(…)
//   private void CountAssetsRecursive(…)
//   internal async void BtnPackDownload_Click(…)
//   internal void BtnPackActivate_Click(…)
//   private void BtnPackUpgrade_Click(…)
//   private void BtnPackPatreon_Click(…)
//   private const string MediaSrcLocal
//   private const string MediaSrcOnline
//   private const string MediaSrcMixed
//   private bool _remotePickerWired
//   private bool _remotePickerSyncing
//   private readonly List<ToggleButton> _remoteSourceChipButtons
//   private readonly List<ToggleButton> _remoteNicheChipButtons
//   private readonly HashSet<string> _remoteNicheExpanded
//   private string? _remoteSubPending
//   private const int RemoteCustomSubCap
//   private void InitializeRemoteMediaPicker(…)
//   private void BuildRemoteSourceChips(…)
//   private void BuildRemoteNicheChips(…)
//   private void RefreshRemoteMediaPicker(…)
//   private void RemoteSourceChip_Changed(…)
//   private bool AskRemoteMediaConsent(…)
//   private void RemoteNicheChip_Changed(…)
//   private void SliderRemoteRatio_Changed(…)
//   private void BtnRemoteAddSub_Click(…)
//   private void TxtRemoteCustomSub_KeyDown(…)
//   private async void AddRemoteCustomSub(…)
//   private void EndRemoteSubProbe(…)
//   private void ShowRemoteSubError(…)
//   private void RemoveRemoteCustomSub(…)
//   private void ToggleRemoteSubSelection(…)
//   private void PersistRemoteChannelChange(…)
//   private void RebuildRemoteCustomSubChips(…)
//   private Border BuildRemoteChip(…)
//   private void RebuildRemoteNicheSubs(…)
//   private static TextBlock MutedRemoteNote(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.Assets.cs; wired when they move to Core.
        private void BtnAssets_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

    }
}
