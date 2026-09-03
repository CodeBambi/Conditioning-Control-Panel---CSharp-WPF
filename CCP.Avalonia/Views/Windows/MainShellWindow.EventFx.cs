// PORTED-AS-A-NOTE from ConditioningControlPanel/MainWindow/MainWindow.EventFx.cs (460 lines).
//
// This one is NOT blocked on the framework, and that is worth stating plainly so a later layer
// does not re-derive it: AmbientFxCanvas.Burst (Controls/AmbientFxCanvas.cs:333) is a full port,
// so the particle burst every Celebrate* method fires can be drawn on this head today. What is
// missing is every CALLER - the progression moments themselves:
//
//   CelebrateLevelUp                <- the XP/level path (MainWindow.ProfileBubble.cs)
//   CelebrateAchievementUnlock      <- MainWindow.AchievementsTab.cs:782
//   CelebrateQuestComplete          <- MainWindow.Quests.cs:77 (a quest-completed event)
//   CelebrateProgramDayComplete     <- MainWindow.ProgramsTab.cs:1020
//   CelebrateEnhancementPurchase    <- MainWindow.Enhancements.cs:2059
//   CelebratePrestige               <- the prestige rank roll
//
// Not one of those six sites exists on this head. Restoring the six methods now would add ~200
// lines of overlay plumbing that nothing can call and nothing can prove: the whole file is
// "when X happens, burst here", and X does not happen yet. It comes back with the progression
// wiring that fires it, not before - and when it does, EnsureEventBurstLayer is a Panel insert
// into the shell's root grid plus one Burst call, because the canvas is already here.
//
// The one piece that WOULD need thought at that point, named so it is not a surprise: the
// achievement tile reveal (AchievementRevealMs / RevealAchievementTile) composes a blur radius
// with a scale as the tile lands. Avalonia has BlurEffect and can animate it, but a blur
// animation per unlocked tile is a real cost at the Performance tier, and the tier gate
// (EventFxAllowed) is MotionFx + PerformanceProfile, still in the WPF head.
//
// Members dropped (28):
//   private const double BurstBoxPx
//   private const int LevelUpBurstCount
//   private const int AchievementBurstCount
//   private const int QuestBurstCount
//   private const int ProgramDayBurstCount
//   private const int EnhancementBurstCount
//   private const int PrestigeBurstCount
//   private const double PrestigeSheenSeconds
//   private const double PrestigeSheenPeak
//   private const int AchievementRevealMs
//   private const double AchievementRevealBlurRadius
//   private const double AchievementRevealScale
//   private AmbientFxCanvas? _eventBurstLayer
//   private bool _eventBurstLayerFailed
//   private Border? _prestigeRowBorder
//   private static long PrestigeRankNow(…)
//   private bool EventFxAllowed
//   private AmbientFxCanvas? EnsureEventBurstLayer(…)
//   internal bool FireBurstAt(…)
//   private void FireBurstAtFirstVisible(…)
//   internal void CelebrateLevelUp(…)
//   internal void CelebrateAchievementUnlock(…)
//   private void RevealAchievementTile(…)
//   internal void CelebrateQuestComplete(…)
//   private Views.Controls.DailyQuestCard? FindDailyCard(…)
//   internal void CelebrateProgramDayComplete(…)
//   internal void CelebrateEnhancementPurchase(…)
//   internal void CelebratePrestige(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
