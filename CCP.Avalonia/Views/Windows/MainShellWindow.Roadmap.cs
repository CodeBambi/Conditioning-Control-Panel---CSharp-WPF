// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.Roadmap.cs (500 lines).
// What this file now carries is the head's single RoadmapService instance. What it still does not
// carry is the node painting, and the reasons are unchanged:
//
// 1. THE VIEW ALREADY DOES THE VIEW HALF. WPF's three chrome handlers hung off the shell because
//    the Quests markup was inline in MainWindow.xaml. CCP.Avalonia/Views/Tabs/QuestsTabView.axaml.cs
//    now owns them: ShowDailyWeekly() / ShowRoadmap() swap DailyWeeklyPanel and RoadmapPanel and
//    restyle the two sub-tab buttons, and the three track buttons restyle themselves. Restoring
//    BtnQuestSubDaily_Click / BtnQuestSubRoadmap_Click / BtnTrack_Click here would be a second copy
//    nothing routes to; _currentRoadmapTrack belongs with them, in that view.
//
// 2. THE SERVICE IS NOW IN CORE, so the old note's "needs a CoreRoadmap seam" is wrong and this
//    file no longer waits on Core. RoadmapService lives at CCP.Core/Services/RoadmapService.cs and
//    is head-agnostic: it reads and writes roadmap.json and the diary folder under
//    CorePaths.UserData. What remains is a straight VIEW port - the node-per-step painting and the
//    photo-submission flow - and it is a UI layer, not a Core one:
//
//   RefreshRoadmapUI()          numbers, the locked overlay and the badge, from Roadmap below.
//                               Controls exist in QuestsTabView.axaml (TrackLockedOverlay,
//                               RoadmapScrollContainer, TxtLockReason, BadgeIndicator).
//   GenerateRoadmapNodes() / CreateRoadmapNode(…)   node-per-step, driven by per-step completion.
//   RoadmapNode_Click(…) / ShowPhotoConfirmation(…) file picker plus Roadmap.SubmitPhoto. Note the
//                               picker is async on Avalonia: SubmitPhoto must be awaited behind the
//                               answer, never fired beside it.
//   RefreshRoadmapStats()       aggregates across all three tracks.
//   OnRoadmapStepCompleted(…) / OnRoadmapTrackUnlocked(…)  subscribe to Roadmap.StepCompleted and
//                               Roadmap.TrackUnlocked once the nodes exist to repaint.

using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private static RoadmapService? _roadmap;

        /// <summary>
        /// This head's one <see cref="RoadmapService"/>, the twin of WPF's <c>App.Roadmap</c>.
        ///
        /// <para>It lives here rather than as a static on the service itself precisely because WPF
        /// constructs its own in <c>App</c>: a Core-side singleton would be a SECOND writer to the
        /// same roadmap.json in the WPF process. One instance per head, and no head reaches into
        /// another's.</para>
        ///
        /// <para>Static, not per-window, because the dialogs that need it (RoadmapStepPopup,
        /// RoadmapDiaryDialog) are constructed without a shell in --render-view. Built on first
        /// read on the UI thread, so no lock.</para>
        ///
        /// <para>Nothing disposes it on this head: the shell owns no Closed hook this layer may
        /// edit. The service saves immediately on photo submission and on a note edit, so the only
        /// thing a missed Dispose can lose is a StartStep timestamp within 30 s of exit.</para>
        /// </summary>
        internal static RoadmapService Roadmap => _roadmap ??= new RoadmapService();
    }
}
