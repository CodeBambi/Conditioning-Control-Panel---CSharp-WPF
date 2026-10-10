// ConditioningControlPanel/MainWindow/MainWindow.Awareness.cs (7.1.5, 1200 lines) on this head.
//
// WPF's MainWindow owned these members because the Awareness markup was inline in MainWindow.xaml.
// Here the controls moved to the tab, and so did the members - one restored HERE would be a second
// copy no click can reach. Where each one lives (lane w2, 10 Oct 2026):
//
//   SyncAwarenessTabUI, the master / OCR / keyboard / own-UI / loop / highlight / capture switches,
//   the app-scope combo + list, the highlight swatches and hex box:
//       Views/Tabs/AwarenessTabView.axaml.cs
//   OnAwarenessTriggerFired, RefreshAwarenessPulseFeed, BuildPulseRow, BuildActionChipStrip,
//   GetActionChipDisplay, FormatTimeAgo, RefreshAwarenessSeenAppChips, AwarenessSeenAppChip_Click,
//   the status dot's breath:
//       Views/Tabs/AwarenessTabView.Live.cs
//   The preset grid, new-preset tile, advanced link, OnPresetsChanged:
//       Views/Tabs/AwarenessTabView.Presets.cs
//   What the switches start and stop (WPF App.KeywordTriggers / App.ScreenOcr / _keyboardHook /
//   App.KeywordHighlight):
//       Platform/KeywordTriggerHead.cs   typed keys (Windows: the panic key's hook; X11: X11KeyListener),
//                                        dispatch, SyncSources
//       Platform/ScreenOcrService.cs     the screen reader's timer (Windows: WinRtScreenReader)
//       Views/Overlays/KeywordHighlightOverlay.cs
//   The shell's own hooks: RefreshEntitlementVeils calls KeywordTriggerHead.SyncSources (access moved),
//   SetAwarenessStatusPulse (MainShellWindow.TabFxTakeoverLabStatus.cs).

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
