// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.QuestStamps.cs (1034 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (73):
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
//   private void QuestStamps_Click(…)
//   private void QuestStamps_MouseEnter(…)
//   private void QuestStamps_MouseLeave(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.QuestStamps.cs; wired when they move to Core.
        private void QuestStamps_Click(object? sender, global::Avalonia.Input.PointerReleasedEventArgs e) { }

        // ponytail: needs the services in MainWindow.QuestStamps.cs; wired when they move to Core.
        private void QuestStamps_MouseEnter(object? sender, global::Avalonia.Input.PointerEventArgs e) { }

        // ponytail: needs the services in MainWindow.QuestStamps.cs; wired when they move to Core.
        private void QuestStamps_MouseLeave(object? sender, global::Avalonia.Input.PointerEventArgs e) { }

    }
}
