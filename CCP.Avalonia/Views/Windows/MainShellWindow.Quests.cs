// PORTED, but not here: MainWindow.Quests.cs's OnQuestCompleted / OnQuestProgressChanged live in
// Views/Tabs/QuestsTabView.Quests.cs, subscribed to App.Quests (the Core QuestService). Every
// control they paint is that view's own x:Name field, so the view owns them.
// ponytail: still missing from OnQuestCompleted - App.Flash.PlayRandomSound (no FlashService audio
// on this head), CelebrateQuestComplete (MainShellWindow.EventFx.cs) and RefreshQuestStamps.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
    }
}
