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
    /// Supplies the FORMATTED strings the view binds to. Static strings now come straight from
    /// {loc:Str key} in the XAML (Localization/StrExtension.cs); only the ones with numbers in
    /// them need code, exactly as in the WPF head where they are set with Loc.GetF.
    /// </summary>
    public sealed class AchievementsTabViewModel
    {
        // The three counters are FORMATTED, not static strings. Keys and arg order taken from
        // MainWindow.AchievementsTab.cs:153/160/196 - I had guessed key names on the first pass
        // and two of them did not exist, so they rendered raw. Read the code-behind that
        // populates a control before inventing its key.
        public string LocUnlockedCount => Loc.GetF("label_0_1_achievements_unlocked", Unlocked, Total);
        public string LocRewardCount => Loc.GetF("achv_reward_count", RewardsEarned, RewardsTotal);
        public string LocPatronCount => Loc.GetF("label_0_1_achievements_unlocked", PatronUnlocked, PatronTotal);

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
