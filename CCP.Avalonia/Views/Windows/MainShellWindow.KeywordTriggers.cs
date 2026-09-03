// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.KeywordTriggers.cs (621 lines).
// The old header here claimed every member reaches App.*, a service or Win32. That was wrong
// about half of them and hid where the work now goes.
//
// WHERE THESE MEMBERS LIVE ON THIS HEAD. WPF's MainWindow owned them because the Awareness markup
// was inline in MainWindow.xaml. The port moved that markup twice over:
//   - the two cooldown sliders are on CCP.Avalonia/Views/Tabs/AwarenessTabView.axaml, and that
//     view ALREADY owns their handlers (AwarenessTabView.axaml.cs:66-79 writes
//     KeywordGlobalCooldownSeconds / KeywordPerKeywordCooldownSeconds through CoreSettings, :132-133
//     loads them back). Those two are done, in the right place.
//   - the rest is on CCP.Avalonia/Views/Controls/Companion/KeywordTriggersPanel.axaml, hosted in
//     the Awareness tab; its code-behind already tracks the four value labels with the WPF format
//     strings.
// A member restored HERE would be a second copy no click can reach.
//
// WHAT IS GENUINELY MISSING, and where:
//
//   The PERSISTENCE half of six editors, in KeywordTriggersPanel.axaml.cs. Each is "read the
//   control, write one CoreSettings field, Save" - every field is already in CoreSettings:
//     SliderKeywordBufferTimeout_ValueChanged      -> KeywordBufferTimeoutMs
//     SliderKeywordSessionMultiplier_ValueChanged  -> KeywordSessionMultiplier
//     SliderScreenOcrInterval_ValueChanged         -> ScreenOcrIntervalMs (seconds x 1000)
//     SliderKeywordHighlightDuration_ValueChanged  -> KeywordHighlightDurationMs
//     CmbOcrHighlightMode_SelectionChanged         -> OcrHighlightAll (SelectedIndex == 0)
//     CmbOcrConfirmation_SelectionChanged          -> OcrConfirmationScans (SelectedIndex + 1)
//   The panel currently updates the label and drops the value. Two of the six also push into a
//   live service and cannot be fully restored: SliderScreenOcrInterval calls
//   App.ScreenOcr.UpdateInterval, and the highlight pair feeds App.KeywordHighlight. Whoever does
//   it needs WPF's `if (_isLoading) return;` guard - a Slider/ComboBox raises its change event on
//   a PROGRAMMATIC set too, so without it every load writes settings straight back.
//
//   internal void SyncKeywordRescuePanelUi()
//     The load pass for all of the above plus the two detail gates (ScreenOcrIntervalPanel /
//     TxtScreenOcrOffHint follow ScreenOcrEnabled; HighlightDurationPanel / TxtHighlightOffHint
//     follow KeywordHighlightEnabled). Every value it reads is in CoreSettings; two things are
//     not: KeywordTriggerService.HasAccess() (the premium gate, ANDed into the OCR section's "on")
//     and RefreshKeywordTriggerList. KeywordTriggersPanel.SetScreenOcrDetail / SetHighlightDetail
//     are the setters it would drive - they exist and are called with a hard-coded `true` today.
//
//   The trigger LIST - service-bound throughout:
//     BtnAddKeywordTrigger_Click          KeywordTriggerService.RebuildActionsFromFlatFields
//     BtnImportFromCustomTriggers_Click   App.KeywordTriggers.ImportFromCustomTriggers
//     RefreshKeywordTriggerList / CreateKeywordTriggerRow   build a row per trigger
//   and the seven per-row editors, which only exist once a row does: BtnDeleteKeywordTrigger_Click,
//   ChkKeywordTriggerEnabled_Changed, TxtKeywordTriggerKeyword_LostFocus,
//   BtnKeywordTriggerBrowseAudio_Click (a file picker - Avalonia's StorageProvider covers it),
//   CmbKeywordVisualEffect_SelectionChanged, SliderKeywordTriggerCooldown_ValueChanged,
//   SliderKeywordTriggerVolume_ValueChanged, ChkKeywordTriggerHaptic_Changed,
//   ChkKeywordTriggerDuckAudio_Changed.
//   AppSettings.KeywordTriggers and the KeywordTrigger model ARE in Core; what is not is
//   ConditioningControlPanel/Services/KeywordTriggerService.cs (HasAccess, the flat-fields rebuild,
//   the import).

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Nothing here on purpose. The controls, and the remaining work, moved to
        // CCP.Avalonia/Views/Controls/Companion/KeywordTriggersPanel.axaml.cs and
        // CCP.Avalonia/Views/Tabs/AwarenessTabView.axaml.cs.
    }
}
