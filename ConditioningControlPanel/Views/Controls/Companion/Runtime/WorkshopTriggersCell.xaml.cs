using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>Z8 · TRIGGERS &amp; PHRASES. See the XAML header. Pure forwarding.</summary>
    public partial class WorkshopTriggersCell : UserControl
    {
        public WorkshopTriggersCell()
        {
            InitializeComponent();
        }

        private void ChkTriggerMode_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ChkTriggerMode_Changed(sender, e);
        }

        private void SliderTriggerInterval_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.SliderTriggerInterval_ValueChanged(sender, e);
        }

        private void BtnEditTriggers_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnEditTriggers_Click(sender, e);
        }

        private void BtnManagePhrases_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnManagePhrases_Click(sender, e);
        }

        private void CmbPhrasePresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.CmbPhrasePresets_SelectionChanged(sender, e);
        }

        private void BtnSavePhrasePreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnSavePhrasePreset_Click(sender, e);
        }

        private void BtnDeletePhrasePreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.BtnDeletePhrasePreset_Click(sender, e);
        }
    }
}
