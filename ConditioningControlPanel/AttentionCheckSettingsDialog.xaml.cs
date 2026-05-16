using System.Windows;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Modal wrapper around <see cref="Features.AttentionCheckFeatureControl"/>.
    /// Opened from the Lab webcam-debug card's "Configure..." button so the
    /// full setting surface (cadence, grace, fail mode, scope, XP values)
    /// is reachable without bloating the card itself. The card keeps just
    /// the master toggle for fast on/off.
    /// </summary>
    public partial class AttentionCheckSettingsDialog : Window
    {
        public AttentionCheckSettingsDialog()
        {
            InitializeComponent();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnTestNow_Click(object sender, RoutedEventArgs e)
        {
            // Close the dialog first so the popup isn't behind it. Fire on
            // the next dispatcher pass so the close fade completes before
            // the test popup appears.
            Close();
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                try { App.AttentionCheck?.FireNow(); }
                catch (System.Exception ex)
                {
                    App.Logger?.Warning(ex, "Test now: FireNow failed");
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
