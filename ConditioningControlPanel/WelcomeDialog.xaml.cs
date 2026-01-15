using System.Windows;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Welcome dialog shown on first launch
    /// </summary>
    public partial class WelcomeDialog : Window
    {
        public WelcomeDialog()
        {
            InitializeComponent();
        }

        private void BtnBegin_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Show welcome dialog if user hasn't been welcomed yet
        /// </summary>
        /// <returns>True if welcome was shown (first launch), false otherwise</returns>
        public static bool ShowIfNeeded()
        {
            if (!App.Settings.Current.Welcomed)
            {
                var dialog = new WelcomeDialog();
                dialog.ShowDialog();

                App.Settings.Current.Welcomed = true;
                App.Settings.Save();
                return true;
            }
            return false;
        }
    }
}
