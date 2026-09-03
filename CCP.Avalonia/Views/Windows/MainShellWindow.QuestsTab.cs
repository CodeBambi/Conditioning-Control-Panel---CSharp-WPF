// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.QuestsTab.cs (990 lines).
//
// Sorted member by member: this file is GENUINELY 100% head-side. Its seventeen members are one
// service's presentation layer - every path starts at App.Quests and none of the seams answer for
// it. The card control it feeds IS ported (Views/Controls/DailyQuestCard + DailyQuestCardModel,
// already carrying IImage in place of ImageSource), so what is missing is strictly the data, not
// the drawing.
//
// What each member needs, exactly:
//   RerollDailySlot         - App.Quests.RerollDaily / RerollWeekly and the reroll budget
//   BtnRerollWeekly_Click     (ConditioningControlPanel/Services/Progression/QuestService.cs).
//   RefreshQuestUI          - the same service for the three daily seats, the weekly card, the
//   BuildDailyCardModel       stats tiles and the streak, plus Models.QuestDefinition /
//   _dailyCardQuestIds        Models.ActiveQuest (ConditioningControlPanel/Models/Quest.cs) and
//                             QuestDefinitionService for the roster. Neither model is in Core.
//   ComputeQuestXpDisplay   - ProgressionService.QuestLevelScale
//                             (ConditioningControlPanel/Services/Progression/ProgressionService.cs)
//                             and App.SkillTree.GetRerollBonusMultiplier
//                             (…/Progression/SkillTreeService.cs). CoreProgression carries AddXP
//                             only, so the ONE formula that must match what CompleteQuest actually
//                             pays cannot be reproduced here - and a second, drifting copy of it is
//                             exactly the bug the WPF comment says this method was written to end.
//   GetQuestArt             - GetModeAwareQuestImagePath + LoadQuestImage, both in
//   ClearQuestArtCache        MainWindow.xaml.cs, which resolve mod-aware art to a BitmapImage over
//                             pack:// URIs. CoreModArt.OverridePath answers the mod-override half,
//                             but this head ships no Resources/quests art to fall back to.
//   RefreshPunchCard        - App.Quests' punch-card state; BuildPunchHole draws with
//   BuildPunchHole            System.Windows.Shapes + a DropShadowEffect.
//   RefreshStreakCalendar   - App.Quests' completion history + AppSettings.StreakFixCharges for the
//   BtnFixStreak_Click        button caption. The settings half IS reachable (CoreSettings.Current),
//   ExitStreakFixMode         but a calendar with no history to paint is not a calendar.
//   StreakFixDay_Click      - App.Quests.SpendStreakFix, which is a server round trip.
//   RecalculateDailyQuestStreak - App.Quests.RecalculateStreak. One line, and the line is the service.
//   OnSettingsPropertyChangedForQuests - the only member whose TRIGGER is portable
//                             (AppSettings implements INPC and CoreSettings.Current hands it over,
//                             and DispatcherHelper.RunOnUI maps to Dispatcher.UIThread.Post). It is
//                             not restored because all it does is call RefreshQuestUI, so on this
//                             head it would be a subscription that repaints nothing.
//
// Trap for whoever wires this: Views/Tabs/QuestsTabView loads with AvaloniaXamlLoader.Load, so ITS
// x:Name fields are null - reach every control with tab.FindControl<T>(name).

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Deliberately empty - see the header. No member of this partial is referenced from
        // MainShellWindow.axaml.
    }
}
