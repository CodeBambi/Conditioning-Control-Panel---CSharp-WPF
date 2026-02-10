using System.Windows;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Warning dialog shown at startup for the Lockdown version.
    /// User must explicitly consent to the restrictive nature of this version.
    /// </summary>
    public partial class LockdownWarningDialog : Window
    {
        public LockdownWarningDialog()
        {
            InitializeComponent();
        }

        private void ChkAccept_Changed(object sender, RoutedEventArgs e)
        {
            BtnEnter.IsEnabled = ChkAccept.IsChecked == true;
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnEnter_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
