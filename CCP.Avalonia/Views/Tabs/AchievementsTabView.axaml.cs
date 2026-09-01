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
        public string LocUnlockedCount => Loc.Get("label_0_25_achievements_unlocked");
        public string LocRewardCount => Loc.Get("label_reward_count");
        public string LocSectionPatronAchievements => Loc.Get("section_patron_achievements");
        public string LocPatronSubtitle => Loc.Get("label_patron_achievements_subtitle");
        public string LocPatronCount => Loc.Get("label_patron_achievements_unlocked");
        public string LocPatronLocked => Loc.Get("label_patron_achievements_locked");
        public string LocVisitPatreon => Loc.Get("btn_visit_patreon");

        /// <summary>Free users see the locked collection behind an overlay; content stays in
        /// the tree, just covered - same contract as the WPF view.</summary>
        public bool PatronLocked => true;
    }
}
