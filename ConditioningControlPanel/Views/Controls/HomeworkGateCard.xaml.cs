using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace ConditioningControlPanel.Views.Controls
{
    /// <summary>The "Homework due" card. Dumb: it shows a title and raises two events; the panel
    /// (MainWindow.Homework.cs) decides when it is up and what the buttons do.</summary>
    public partial class HomeworkGateCard : UserControl
    {
        public event Action? WatchRequested;
        public event Action? LeaveRequested;

        public HomeworkGateCard() => InitializeComponent();

        public bool IsUp => Visibility == Visibility.Visible;

        public void Present(string title)
        {
            TxtVideoTitle.Text = title;
            if (IsUp) return;
            Visibility = Visibility.Visible;
            BeginAnimation(OpacityProperty, Services.MotionFx.AllowTransitions
                ? new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) : null);
            if (!Services.MotionFx.AllowTransitions) Opacity = 1;
            BtnWatch.Focus();
        }

        public void Dismiss()
        {
            if (!IsUp) return;
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            Visibility = Visibility.Collapsed;
        }

        private void BtnWatch_Click(object sender, RoutedEventArgs e) => WatchRequested?.Invoke();
        private void BtnLeave_Click(object sender, RoutedEventArgs e) => LeaveRequested?.Invoke();
    }
}
