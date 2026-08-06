using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>Z8 · BEHAVIOR. See the XAML header. Pure forwarding, like every re-parented cell.</summary>
    public partial class WorkshopBehaviorCell : UserControl
    {
        public WorkshopBehaviorCell()
        {
            InitializeComponent();
        }

        private void SliderIdleInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SliderIdleInterval_ValueChanged(sender, e);
        }

        private void SliderBubbleDuration_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SliderBubbleDuration_ValueChanged(sender, e);
        }

        private void BtnChatShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnChatShortcut_Click(sender, e);
        }

        private void BtnCameraShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnCameraShortcut_Click(sender, e);
        }

        private void ChkMuteWhispers_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkMuteWhispers_Changed(sender, e);
        }

        private void ChkPauseBrowser_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkPauseBrowser_Changed(sender, e);
        }
    }
}
