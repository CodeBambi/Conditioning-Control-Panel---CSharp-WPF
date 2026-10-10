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
