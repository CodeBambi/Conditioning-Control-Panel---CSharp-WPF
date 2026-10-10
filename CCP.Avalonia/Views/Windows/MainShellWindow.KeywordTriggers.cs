// ConditioningControlPanel/MainWindow/MainWindow.KeywordTriggers.cs (621 lines) on this head.
//
// WPF's MainWindow owned these handlers because the Awareness markup was inline in MainWindow.xaml.
// Here the controls moved, and so did the handlers - a member restored HERE would be a second copy
// no click can reach:
//   - the two cooldown sliders: CCP.Avalonia/Views/Tabs/AwarenessTabView.axaml.cs
//   - the four detail sliders, the two OCR combos, SyncKeywordRescuePanelUi (incl. the HasAccess
//     gate on the OCR detail), add / import, RefreshKeywordTriggerList, CreateKeywordTriggerRow and
//     all nine per-row editors: CCP.Avalonia/Views/Controls/Companion/KeywordTriggersPanel*.cs
//
// The runtime the list feeds (WPF Services/KeywordTriggerService.cs, ScreenOcrService,
// KeywordHighlightService): Core Services/KeywordTriggers/KeywordTriggerEngine (+ .Ocr) for the rules,
// Platform/KeywordTriggerHead, Platform/ScreenOcrService and Views/Overlays/KeywordHighlightOverlay for
// the head. Typed keys: Windows through the panic key's hook, X11 through X11KeyListener; a native
// Wayland window shows no client another client's keys, so nothing typed there is seen.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
    }
}
