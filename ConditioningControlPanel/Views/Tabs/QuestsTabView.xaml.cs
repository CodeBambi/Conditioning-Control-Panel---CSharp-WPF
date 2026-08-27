using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class QuestsTabView : UserControl
    {
        public QuestsTabView()
        {
            InitializeComponent();
            // FX lifecycle (PR-3a): the quest bars are filled from the tab's own show, because
            // RefreshQuestUI runs before the tracks have ever been measured.
            IsVisibleChanged += QuestsTabView_IsVisibleChanged;

            // The three daily seats each own a reroll button; the tab just forwards which seat was
            // pressed. MainWindow spends the reroll - no quest state is touched down here.
            DailyCard0.RerollRequested += OnDailyCardRerollRequested;
            DailyCard1.RerollRequested += OnDailyCardRerollRequested;
            DailyCard2.RerollRequested += OnDailyCardRerollRequested;
        }

        private void OnDailyCardRerollRequested(object? sender, EventArgs e)
        {
            if (sender is Controls.DailyQuestCard card && Window.GetWindow(this) is MainWindow mw)
                mw.RerollDailySlot(card.Slot);
        }

        private void QuestsTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnQuestsTabVisibilityChanged(IsVisible);
        }

        private void BtnFixStreak_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnFixStreak_Click(sender, e);
        }
        private void BtnQuestSubDaily_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnQuestSubDaily_Click(sender, e);
        }
        private void BtnQuestSubRoadmap_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnQuestSubRoadmap_Click(sender, e);
        }
        private void BtnRerollWeekly_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnRerollWeekly_Click(sender, e);
        }
        private void BtnTrack_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnTrack_Click(sender, e);
        }
        private void HorizontalScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.HorizontalScrollViewer_PreviewMouseWheel(sender, e);
        }
        private void StreakCalendarCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.StreakCalendarCanvas_SizeChanged(sender, e);
        }
    }
}
