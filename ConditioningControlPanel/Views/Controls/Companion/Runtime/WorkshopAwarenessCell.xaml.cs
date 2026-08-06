using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>Z8 · AWARENESS FINE-TUNING. See the XAML header. Pure forwarding.</summary>
    public partial class WorkshopAwarenessCell : UserControl
    {
        public WorkshopAwarenessCell()
        {
            InitializeComponent();
        }

        private void SliderAwarenessCooldown_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SliderAwarenessCooldown_ValueChanged(sender, e);
        }

        private void SliderAwarenessCooldownMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SliderAwarenessCooldownMax_ValueChanged(sender, e);
        }

        private void BtnPrivacySpoiler_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnPrivacySpoiler_Click(sender, e);
        }
    }
}
