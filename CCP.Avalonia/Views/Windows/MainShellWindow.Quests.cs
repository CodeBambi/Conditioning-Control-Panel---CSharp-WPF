// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.Quests.cs (124 lines).
//
// Five members, and both real ones are EVENT HANDLERS for App.Quests
// (ConditioningControlPanel/Services/Progression/QuestService.cs). CoreProgression carries AddXP
// and nothing else, so there is no QuestCompleted / QuestProgressChanged to subscribe to on this
// head - a restored handler would be a method with no publisher. What they draw is not the
// problem: QuestCompleteBanner and TxtQuestComplete are both in QuestsTabView.axaml:437-441 and
// Views/Windows/QuestCompletePopup is ported.
//
//   OnQuestCompleted(sender, QuestCompletedEventArgs)
//       Needs Services.QuestCompletedEventArgs (head-only) for the quest name and XP it prints.
//       Its body then reaches five more head services: App.PerkNotificationsSuppressed for the
//       announce opt-out, App.Flash.PlayRandomSound for the celebration, RefreshQuestUI and
//       RefreshQuestStamps (both stubs - see MainShellWindow.QuestsTab.cs), CelebrateQuestComplete
//       (MainShellWindow.EventFx.cs), and App.ProfileSync.SyncProfileAsync. The banner's own
//       shape ports cleanly when there is an event: IsVisible instead of Visibility, and the
//       5-second auto-hide is Dispatcher.UIThread.Post off a Task.Delay.
//   OnQuestProgressChanged(sender, QuestProgressEventArgs)
//       Same missing event source, and its whole body is a call to RefreshQuestUI.
//   _questCompletePopup
//       Held only so the previous popup can be closed before the next opens. It belongs with
//       OnQuestCompleted and comes back with it.
//   _dailySegmentGold, _dailySegmentGrey
//       Not used in this file at all - they are the lazily-built #FFD700 / #3D3D60 brushes for the
//       daily progress segments, read in MainWindow.QuestsTab.cs:223-224. They land with the
//       segment painter, in MainShellWindow.QuestsTab.cs, not here.
//
// What would unblock it: a quest seam carrying the two events plus the definition/active-quest
// models. That is a Core layer, not this one.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Deliberately empty - see the header. No member of this partial is referenced from
        // MainShellWindow.axaml.
    }
}
