using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class AchievementsTabView : UserControl
    {
        public AchievementsTabView()
        {
            InitializeComponent();
            // FX lifecycle (PR-3a): entrance stagger on the tiles, once per tab show.
            IsVisibleChanged += AchievementsTabView_IsVisibleChanged;
        }

        private void AchievementsTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnAchievementsTabVisibilityChanged(IsVisible);
        }

        private void BtnVisitPatreon_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnVisitPatreon_Click(sender, e);
        }
    }
}
