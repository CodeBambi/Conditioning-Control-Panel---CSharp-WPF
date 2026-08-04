using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class AvailableSubjectsTabView : UserControl
    {
        public AvailableSubjectsTabView()
        {
            InitializeComponent();
            // FX lifecycle (PR-4b): starts the tab's one ambient canvas and staggers the roster in.
            IsVisibleChanged += AvailableSubjectsTabView_IsVisibleChanged;
        }

        private void AvailableSubjectsTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnAvailableSubjectsTabVisibilityChanged(IsVisible);
        }

        private void BtnConnectSubject_PressDown(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnSubjectConnectPress(sender as FrameworkElement, true);
        }

        private void BtnConnectSubject_PressUp(object sender, MouseEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnSubjectConnectPress(sender as FrameworkElement, false);
        }

        private void AvailableSubjectsScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.AvailableSubjectsScroller_PreviewMouseWheel(sender, e);
        }
        private void BtnBecomeASubject_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnBecomeASubject_Click(sender, e);
        }
        private void BtnConnectSubject_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnConnectSubject_Click(sender, e);
        }
    }
}
