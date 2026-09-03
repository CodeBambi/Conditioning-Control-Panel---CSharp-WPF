// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.CompanionTab.cs (471 lines).
//
// Sorted member by member: GENUINELY 100% head-side, and blocked TWICE over - once on the service
// and once on the controls, which is worth stating because the second half is not obvious.
//
// THE CONTROLS. Every member here writes CompanionTab.<Name> - forty-odd of the ~110 names the WPF
// CompanionTabView re-publishes so MainWindow's seven companion partials can keep the accessor path
// they always had. The ported Views/Tabs/CompanionTabView publishes NONE of them (its own header
// says why: they are passthroughs into a CompanionRoomRuntimeVm and eight zone viewmodels that have
// not moved, and writing 110 properties returning null would be a seam in name only). The controls
// themselves DO exist on this head, but inside the room's cells - SliderTriggerIntervalCompanion
// lives in Views/Controls/Companion/Runtime/WorkshopTriggersCell, for instance - so a future wiring
// reaches them through the cell that owns them, not through the tab.
//
// THE SERVICE. What each member needs, exactly:
//   SyncCompanionTabUI      - the settings half IS reachable (CoreSettings.Current carries
//                             TriggerModeEnabled, TriggerIntervalSeconds, IdleGiggleIntervalSeconds,
//                             BubbleDurationSeconds), but the rest of the method is
//                             _avatarTubeWindow.IsDetached (ConditioningControlPanel/AvatarTube/),
//                             SyncAiBrainUI (MainWindow.AiBrain.cs) and CompanionRoom.Sync - and it
//                             writes all of it through the tab accessors above. Note also the
//                             _isLoading guard it wraps everything in: on Avalonia that guard is
//                             MORE necessary, not less, because CheckBox and ToggleSwitch raise
//                             IsCheckedChanged on a programmatic set.
//   UpdateCompanionCardsUI  - App.Companion.ActiveCompanion / GetProgress / IsCompanionUnlocked
//   CompanionCard_Click       (ConditioningControlPanel/Services/Companion/CompanionService.cs) and
//                             App.Mods.IsCompanionSupported. Models.CompanionDefinition and
//                             CompanionProgress ARE in Core (CCP.Core/Models/), and CoreMods answers
//                             GetAccentColorHex and MakeModAware - so the five cards' names and
//                             colours are the one part that would resolve. Their level, lock state
//                             and active ring do not, and a card wearing four right answers and one
//                             wrong one is worse than a card that waits.
//   UpdateCommunityPromptsUI- App.CommunityPrompts.GetInstalledPrompts / ActivatePrompt /
//   CreatePromptRow           GetInstalledPrompt (…/Services/Companion/CommunityPromptService.cs)
//                             and Models.CommunityPrompt. The row BUILDER itself is portable
//                             (Grid/StackPanel/TextBlock/Button + Loc.GetF("label_by_author", …)),
//                             and its explicit-content gate is too - Services.ExplicitContentGate is
//                             in Core (CCP.Core/Services/ExplicitContentGate.cs) and this head ships
//                             Views/Dialogs/ExplicitContentAcknowledgementDialog. Only the roster is
//                             missing, so this is the member to restore FIRST once
//                             CommunityPromptService crosses.
//   GetActivePromptDisplayName - App.Settings.Current.CompanionPrompt is reachable; the display name
//                             it resolves comes from CommunityPromptService.
//   BtnCompanionPersonality_Click - a Microsoft.Win32.OpenFileDialog seeded from
//                             App.EffectiveAssetsPath (CorePaths answers that half) plus
//                             CompanionService.GetAssignedPromptName. The picker maps to Avalonia's
//                             StorageProvider; the assignment it performs does not.
//   UpdateCompanionPromptLabels - the same assigned-name lookup, once per card.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Deliberately empty - see the header. No member of this partial is referenced from
        // MainShellWindow.axaml.
    }
}
