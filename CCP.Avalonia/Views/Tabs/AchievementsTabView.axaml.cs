using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class AchievementsTabView : UserControl
    {
        public AchievementsTabView()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new AchievementsTabViewModel(Engine);
            // WPF MainWindow.xaml.cs:420: the counters refresh on every unlock.
            if (App.Achievements is { } live)
                live.Unlocked += (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(
                    () => DataContext = new AchievementsTabViewModel(live));
        }

        /// <summary>The live engine; on the headless render path a read-only load of the same file
        /// (nothing on that path ever saves it).</summary>
        private static AchievementEngine Engine =>
            App.Achievements ?? new AchievementEngine(new AchievementStore(AchievementStore.DefaultPath));
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

        private readonly AchievementEngine _engine;
        internal AchievementsTabViewModel(AchievementEngine engine) => _engine = engine;

        // Keys and arg order: WPF MainWindow.AchievementsTab.cs:155-172. Free and patron are never summed.
        public int Unlocked => _engine.GetUnlockedCount(exclusive: false);
        public int Total => _engine.GetTotalCount(exclusive: false);
        public int PatronUnlocked => _engine.GetUnlockedCount(exclusive: true);
        public int PatronTotal => _engine.GetTotalCount(exclusive: true);

        // ponytail: reward count needs WardrobeCatalog.AchievementGates (WPF head only); WPF collapses
        // the line when it has no gates, so it is hidden here until the catalog reaches Core.
        public bool RewardCountVisible => false;
        public int RewardsEarned => 0;
        public int RewardsTotal => 0;

        /// <summary>Free users see the locked collection behind an overlay; content stays in
        /// the tree, just covered - same contract as the WPF view.</summary>
        public bool PatronLocked => !CoreEntitlement.HasPremium;
    }
}
