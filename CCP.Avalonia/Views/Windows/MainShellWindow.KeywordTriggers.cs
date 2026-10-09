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
// ponytail: still missing is the runtime the list feeds - ConditioningControlPanel/Services/
// KeywordTriggerService.cs (keystroke buffer + matching + dispatch), ScreenOcrService and
// KeywordHighlightService. WPF reads keystrokes through a global Win32 hook; there is no Linux
// keystroke source yet (Wayland forbids global capture), so nothing fires these triggers here.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
    }
}
