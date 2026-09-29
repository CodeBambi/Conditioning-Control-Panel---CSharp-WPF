// PORTED into Views/Tabs/QuestsTabView.Quests.cs (the view owns every control it paints):
// RerollDailySlot, BtnRerollWeekly_Click, RefreshQuestUI, BuildDailyCardModel,
// ComputeQuestXpDisplay (XpCurve + SkillTreeRules, the maths CompleteQuest pays) and
// RefreshStreakCalendar, all off App.Quests (the Core QuestService).
//
// ponytail, still missing:
//   GetQuestArt / ClearQuestArtCache - resolve pack:// quest art this head does not ship.
//   RefreshPunchCard / BuildPunchHole - IntakePunchCardService is WPF-only.
//   BtnFixStreak_Click / StreakFixDay_Click / ExitStreakFixMode - SpendStreakFix, a server round trip.
//   OnSettingsPropertyChangedForQuests - mod/settings repaint; the tab repaints on entry instead.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
    }
}
