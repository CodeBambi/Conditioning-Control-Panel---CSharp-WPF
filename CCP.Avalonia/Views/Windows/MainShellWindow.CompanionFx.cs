// PORTED-AS-A-NOTE from ConditioningControlPanel/MainWindow/MainWindow.CompanionFx.cs (95 lines).
//
// Nothing is wired here, and re-checked rather than assumed: the WPF file has NO ambient effect
// left to register. Both of its loops moved out in the "Her Room" redesign - the hero disc's
// breathe belongs to CompanionHeroCard.StartAmbientLoop (parked by CompanionRoomView off the
// tab's own visibility, including her asleep state, which a MainWindow clock could not see), and
// the connect sheen was deleted with the AI-brain card it swept. So there is no RegisterTabFx
// call to make on this tab and no canvas in CompanionTabView.axaml to make it with.
//
// What is left in the WPF file is a REPAINT hook, not an effect: a mod switch changes her name,
// her portrait, her flavour line, the mod chip and the five-card picker (a mod can hide a whole
// avatar set), and this is the one place that repaints them.
//
// Half of that hook is now available and half is not:
//   * AVAILABLE - CoreMods.ModChanged (CCP.Core/CoreMods.cs:206) is the seam the head forwards
//     its service's event into. Subscribing is one line, and CoreDispatch covers the
//     Dispatcher.CheckAccess marshalling WPF does by hand.
//   * MISSING - every repaint target. CompanionRoomView on this head has no ViewModel and no
//     Sync(): CompanionTabView's own note records that it needs CompanionRoomRuntimeVm plus the
//     eight zone viewmodels, all still in the WPF head. UpdateCompanionCardsUI and
//     UpdateCompanionPromptLabels do not exist here either. A subscription with nothing to call
//     would be a live event handler that does nothing, which is worse than the note.
//
// Wire this when CompanionRoomRuntimeVm lands: subscribe to CoreMods.ModChanged from an
// EnsureCompanionFx case in EnsureTabFx (MainShellWindow.AmbientFx.cs) and call Room.Sync().
//
// Also dropped with its service: PersistCompanionDrawerStates (a settings write on tab exit) and
// EnsureAwarenessV2Consent (the upgrade consent dialog raised on this page).
//
// Members dropped (4):
//   private bool _companionFxInitialized
//   internal void OnCompanionTabVisibilityChanged(…)
//   private void InitializeCompanionFx(…)
//   private void OnCompanionFxModChanged(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
