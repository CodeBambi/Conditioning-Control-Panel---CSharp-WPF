using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class AchievementsTabView : UserControl
    {
        public AchievementsTabView()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new AchievementsTabViewModel();
        }
    }

    /// <summary>
    /// Supplies the strings the view binds to. Every one comes from CCP.Core's
    /// <see cref="Loc"/> - the same localization runtime and the same JSON files the WPF head
    /// reads, now that Localization has moved to Core.
    ///
    /// This exists because WPF's {loc:Str key} markup extension (LocExtension) cannot cross: it
    /// derives from System.Windows.Markup.MarkupExtension and stays in the head. The strings do
    /// cross; only the binding mechanism differs. A shared Loc markup extension for Avalonia is
    /// the obvious follow-up and would remove this class entirely.
    /// </summary>
    public sealed class AchievementsTabViewModel
    {
        public string LocSectionAchievements => Loc.Get("section_achievements");
        public string LocSubtitleRewards => Loc.Get("achv_subtitle_rewards");
        // The three counters are FORMATTED, not static strings. Keys and arg order taken from
        // MainWindow.AchievementsTab.cs:153/160/196 - I had guessed key names on the first pass
        // and two of them did not exist, so they rendered raw. Read the code-behind that
        // populates a control before inventing its key.
        public string LocUnlockedCount => Loc.GetF("label_0_1_achievements_unlocked", Unlocked, Total);
        public string LocRewardCount => Loc.GetF("achv_reward_count", RewardsEarned, RewardsTotal);
        public string LocSectionPatronAchievements => Loc.Get("section_patron_achievements");
        public string LocPatronSubtitle => Loc.Get("label_patron_achievements_subtitle");
        public string LocPatronCount => Loc.GetF("label_0_1_achievements_unlocked", PatronUnlocked, PatronTotal);
        public string LocPatronLocked => Loc.Get("label_patron_achievements_locked");
        public string LocVisitPatreon => Loc.Get("btn_visit_patreon");

        // Placeholder counts. The real values come from AchievementService, which is still in
        // the WPF head; wiring them is a separate change from proving the view renders.
        public int Unlocked => 0;
        public int Total => 25;
        public int RewardsEarned => 0;
        public int RewardsTotal => 12;
        public int PatronUnlocked => 0;
        public int PatronTotal => 8;

        /// <summary>Free users see the locked collection behind an overlay; content stays in
        /// the tree, just covered - same contract as the WPF view.</summary>
        public bool PatronLocked => true;
    }
}
