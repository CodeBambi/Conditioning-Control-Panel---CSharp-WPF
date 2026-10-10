// PORTED from ConditioningControlPanel/MainWindow/MainWindow.AssetsFx.cs (226 lines) into the tab
// (CCP.Avalonia/Views/Tabs/AssetsTabView.axaml.cs, "FX" region): the asset-tree row nudge (3 px,
// 130 ms ease-out, gated on AllowTransitions) and the Media Log pulse (3 x 0.45 s dips to 0.45 when
// App.MediaHistory grew since the log was last opened; stopped on hide and on click).
//
// ponytail: the pack-card sheen (CardSheenAdorner over a hovered pack card) waits for real pack
// cards - the packs section is hidden and has no ContentPackService on this head.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
