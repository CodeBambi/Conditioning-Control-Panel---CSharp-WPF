// PARTLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.QuestStamps.cs (1034 lines) -
// the wax-stamp cluster in the title bar, one stamp per daily/weekly quest.
//
// WHAT IS RESTORED. QuestStamps_Click, the whole cluster's navigation: "the plates do not handle
// the click, so it bubbles here from a stamp and from the gaps between them alike" (WPF's own
// comment), and the destination is ShowTab("quests"), which is real on this head. It is one third
// of the file's user-visible behaviour and it never needed a service.
//
// WHAT THE OTHER TWO HANDLERS NEED, and why they are notes rather than bodies:
//   QuestStamps_MouseEnter/Leave are MotionFx.HoverLift on QuestStampHost, plus - on leave - the
//   per-stamp unzoom and the popup teardown. MotionFx is head-side (the reduced-motion gate reads
//   App.Settings through it), and the lift itself is a RenderTransform tween Avalonia would write
//   as a transition. The leave half additionally unwinds _hoveredStampKey/_stampInfos/_stampPopup,
//   none of which exist without the build pass below. Faking the lift alone would be motion with
//   no state behind it, so both stay named. See Views/Features/FeatureCard.axaml.cs:116/299 for
//   the shape a hover lift takes on this head when it IS wanted.
//
// WHAT IS GENUINELY BLOCKED. Everything that BUILDS a stamp: RefreshQuestStamps / AddStamp /
// BuildQuestStamp read App.Quests for the day's quest list, its progress and its XP, and
// InitializeQuestStamps subscribes to that service's five events (completed, progress, refreshed,
// dismissed, mod changed). CoreProgression is not that seam - it carries AddXP and
// TrackBubbleCountResult, deliberately narrow, and knows nothing about quests. The hover popup
// (EnsureStampPopup / PaintStampPopup / Show / Hide) needs the same quest rows to paint, and the
// twenty-odd frozen Brushes and the stamp geometry are constants that only that builder reads.
// Net: this file draws nothing until a quest seam exists. QuestStampHost is authored empty AND
// IsVisible="False" (MainShellWindow.axaml:2261) rather than filled with placeholder wax, so the
// restored click below is correct-but-unreachable today: it becomes live the moment the builder
// does, with no second edit here.
//
// Members dropped (73 - the one now restored is marked RESTORED):
//   private const double QuestStampDailySize
//   private const double QuestStampWeeklySize
//   private static readonly double[] QuestStampOffsets
//   private static readonly double[] QuestStampAngles
//   private const double QuestStampFillInset
//   private const double QuestStampFillHeight
//   private const double QuestStampHoverScale
//   private const double QuestStampPopWidth
//   private const double QuestStampPopTrackWidth
//   private static readonly Brush QuestStampGoldStroke
//   private static readonly Brush QuestStampGoldFill
//   private static readonly Brush QuestStampGoldInk
//   private static readonly Brush QuestStampGoldWash
//   private static readonly Brush QuestStampPinkStroke
//   private static readonly Brush QuestStampPanelFill
//   private static readonly Brush QuestStampDarkFill
//   private static readonly Brush QuestStampPurpleStroke
//   private static readonly Brush QuestStampPurpleFill
//   private static readonly Brush QuestStampGhostStroke
//   private static readonly Brush QuestStampGhostFill
//   private static readonly Brush QuestStampArtScrim
//   private static readonly Brush QuestStampDoneInk
//   private static readonly Brush QuestStampMutedInk
//   private static readonly Brush QuestStampWhiteInk
//   private static readonly Brush QuestStampChipFill
//   private static Brush Frozen(…)
//   private bool _questStampsWired
//   private sealed class QuestStampInfo
//   private readonly Dictionary<string, QuestStampInfo> _stampInfos
//   private readonly Dictionary<string, bool> _stampCompletedSeen
//   private string? _hoveredStampKey
//   private Popup? _stampPopup
//   private Border? _stampPopCard
//   private TranslateTransform? _stampPopSlide
//   private Image? _stampPopArt
//   private Border? _stampPopArtScrim
//   private TextBlock? _stampPopKind
//   private TextBlock? _stampPopXp
//   private TextBlock? _stampPopIcon
//   private TextBlock? _stampPopName
//   private TextBlock? _stampPopDesc
//   private Border? _stampPopTrack
//   private Border? _stampPopFill
//   private TextBlock? _stampPopProgress
//   private TextBlock? _stampPopRemaining
//   private Border? _stampPopDone
//   private void InitializeQuestStamps(…)
//   private void OnQuestStampsQuestCompleted(…)
//   private void OnQuestStampsProgressChanged(…)
//   private void OnQuestStampsRefreshed(…)
//   private void OnQuestStampsLanguageChanged(…)
//   private void OnQuestStampsDismiss(…)
//   private void OnQuestStampsModChanged(…)
//   private void QueueQuestStampRepaint(…)
//   internal void RefreshQuestStamps(…)
//   private void AddStamp(…)
//   private static double QuestStampFraction(…)
//   private FrameworkElement BuildQuestStamp(…)
//   private static string QuestStampName(…)
//   private static string QuestStampDescription(…)
//   private static void PlayStampStampedPop(…)
//   private static void ZoomStamp(…)
//   private static double QuestStampRestAngle(…)
//   private void EnsureStampPopup(…)
//   private void PaintStampPopup(…)
//   private void ShowStampPopup(…)
//   private void HideStampPopup(…)
//   private void Stamp_MouseEnter(…)
//   private void Stamp_MouseLeave(…)
//   private void RestoreStampHover(…)
//   private void QuestStamps_Click(…)                  RESTORED
//   private void QuestStamps_MouseEnter(…)
//   private void QuestStamps_MouseLeave(…)

using System;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// The whole cluster is one target: a click goes to the Quests tab through the same funnel
        /// the nav button uses, so door expansion and the per-tab FX behave identically. WPF hides
        /// the hover popup first; there is no popup on this head yet, so that line has nothing to
        /// undo rather than being skipped.
        /// </summary>
        private void QuestStamps_Click(object? sender, global::Avalonia.Input.PointerReleasedEventArgs e)
        {
            try
            {
                e.Handled = true;
                ShowTab("quests");
            }
            catch (Exception ex)
            {
                Log.Debug("[QuestStamps] navigation failed: {E}", ex.Message);
            }
        }

        // Both hover handlers are MotionFx.HoverLift plus, on leave, the popup teardown - see the
        // header for why a lift with no stamps under it is not worth faking.
        private void QuestStamps_MouseEnter(object? sender, global::Avalonia.Input.PointerEventArgs e) { }

        private void QuestStamps_MouseLeave(object? sender, global::Avalonia.Input.PointerEventArgs e) { }

    }
}
