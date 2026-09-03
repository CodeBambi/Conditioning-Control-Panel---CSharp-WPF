// PORTED-AS-A-NOTE from ConditioningControlPanel/MainWindow/MainWindow.AssetsFx.cs (226 lines).
//
// Nothing is wired here, and unlike the two tabs this batch DID turn on, the reason is not a
// missing service - the Assets tab has no AmbientFxCanvas at all, in WPF or here. All three of
// its effects are interaction motion on controls, and each is blocked on something concrete:
//
//   * the pack-card sheen (CardSheenAdorner over the hovered card). WPF hangs it off an
//     AdornerLayer with a FrameworkElement host. There is no CardSheenAdorner on this head - it
//     was never ported, and it is a control, so it belongs to a controls layer, not to a
//     MainShellWindow partial. Avalonia does have an AdornerLayer, so this is a port and not a
//     reimplementation, but it is somebody else's file.
//   * the asset-tree row nudge (AssetTreeRowNudgePx over AssetTreeRowNudgeMs on hover). The
//     target exists - AssetsTabView.axaml:518, TreeView x:Name="AssetTreeView" - but the hover
//     handler belongs to the row template inside AssetsTabView, which this layer does not own.
//   * the media-log pulse (MediaLogPulseBeats beats down to MediaLogPulseFloor when the log has
//     unseen entries). The button exists (AssetsTabView.axaml:138, x:Name="BtnMediaLog") and the
//     motion is a plain opacity Animation - but "unseen" is _mediaLogSeenCount against the media
//     log service, which is still in the WPF head, and pulsing on a count this head cannot read
//     would be decoration lying about state.
//
// None of the three is a storyboard-only effect: all three have real Avalonia equivalents
// (Animation over OpacityProperty / a TranslateTransform, and AdornerLayer for the sheen). They
// are blocked on ownership and on the media-log service, not on the framework.
//
// Members dropped (18):
//   private const double AssetTreeRowNudgePx
//   private const int AssetTreeRowNudgeMs
//   private const double PackCardCornerRadius
//   private const int MediaLogPulseBeats
//   private const double MediaLogPulseSeconds
//   private const double MediaLogPulseFloor
//   private bool _assetsFxInitialized
//   private CardSheenAdorner? _packCardSheen
//   private AdornerLayer? _packCardSheenLayer
//   private FrameworkElement? _packCardSheenHost
//   private int _mediaLogSeenCount
//   internal void OnAssetsTabVisibilityChanged(…)
//   private void InitializeAssetsFx(…)
//   internal void OnAssetTreeRowHover(…)
//   internal void OnPackCardHover(…)
//   private void DetachPackCardSheen(…)
//   private void PulseMediaLogIfUnseen(…)
//   private void MediaLogButton_Clicked(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
