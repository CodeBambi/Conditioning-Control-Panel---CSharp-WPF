// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.Roadmap.cs (500 lines). Two
// unrelated reasons, neither of them "wired when the models move to Core" - the models are ALREADY
// there (CCP.Core/Models/RoadmapDefinition.cs carries RoadmapTrack and
// RoadmapTrackDefinition.GetByTrack; CCP.Core/Models/RoadmapProgress.cs carries IsTrackUnlocked).
//
// 1. THE VIEW ALREADY DOES THE VIEW HALF. WPF's three chrome handlers hung off the shell because
//    the Quests markup was inline in MainWindow.xaml. CCP.Avalonia/Views/Tabs/QuestsTabView.axaml.cs
//    now owns them: ShowDailyWeekly() / ShowRoadmap() swap DailyWeeklyPanel and RoadmapPanel and
//    restyle the two sub-tab buttons (lines 64-95), and the three track buttons restyle themselves.
//    Restoring BtnQuestSubDaily_Click / BtnQuestSubRoadmap_Click / BtnTrack_Click here would be a
//    second copy nothing routes to; _currentRoadmapTrack belongs with them, in that view.
//
// 2. EVERYTHING ELSE NEEDS THE SERVICE, NOT THE MODEL. App.Roadmap is
//    ConditioningControlPanel/Services/RoadmapService.cs and has no seam: it owns the loaded
//    RoadmapProgress instance, GetTrackProgress, IsTrackUnlocked against live progress, the
//    photo-submission flow and the StepCompleted / TrackUnlocked events. Core has the SHAPES; it has
//    no instance and no persistence for them, and RoadmapProgress is not a field of AppSettings. A
//    CoreRoadmap seam over those four reads plus the two events would be the right fix - and it is
//    a Core layer, not this one.
//
// Member by member:
//   _currentRoadmapTrack, BtnQuestSubDaily_Click, BtnQuestSubRoadmap_Click, BtnTrack_Click
//       -> QuestsTabView, per (1).
//   RefreshRoadmapUI()
//       Paints TxtRoadmapTrackName / TxtRoadmapTrackSubtitle from RoadmapTrackDefinition (Core,
//       available) but needs App.Roadmap for GetTrackProgress, IsTrackUnlocked and
//       Progress.HasCertifiedBlowdollBadge - the numbers, the locked overlay and the badge. The
//       controls all exist in QuestsTabView.axaml (TrackLockedOverlay, RoadmapScrollContainer,
//       TxtLockReason, BadgeIndicator).
//   GenerateRoadmapNodes() / CreateRoadmapNode(…)   node-per-step, driven by per-step completion.
//   RoadmapNode_Click(…) / ShowPhotoConfirmation(…) the photo-submission flow: a file picker plus
//                                                   App.Roadmap's submit/confirm calls.
//   RefreshRoadmapStats()                           aggregates across all three tracks.
//   OnRoadmapStepCompleted(…) / OnRoadmapTrackUnlocked(…)  handlers for RoadmapService's two
//                                                   events. Nothing to subscribe to.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Nothing here on purpose. Chrome: CCP.Avalonia/Views/Tabs/QuestsTabView.axaml.cs.
        // Data: needs a RoadmapService seam in Core.
    }
}
