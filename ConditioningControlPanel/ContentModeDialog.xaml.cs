using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel
{
    /// <summary>
    /// First-run dialog for selecting content mode (Bambi Sleep vs Sissy Hypno)
    /// </summary>
    public partial class ContentModeDialog : Window
    {
        public ContentMode SelectedMode { get; private set; } = ContentMode.BambiSleep;

        public ContentModeDialog()
        {
            InitializeComponent();
        }

        private void CardBambi_Click(object sender, MouseButtonEventArgs e)
        {
            SelectedMode = ContentMode.BambiSleep;
            DialogResult = true;
            Close();
        }

        private void CardSissy_Click(object sender, MouseButtonEventArgs e)
        {
            SelectedMode = ContentMode.SissyHypno;
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Show content mode selection dialog if user hasn't chosen yet.
        /// Sets the mode in settings and initializes default triggers for that mode.
        /// </summary>
        /// <returns>The selected content mode</returns>
        public static ContentMode ShowIfNeeded()
        {
            if (!App.Settings.Current.ContentModeChosen)
            {
                var dialog = new ContentModeDialog();
                dialog.ShowDialog();

                App.Settings.Current.ContentMode = dialog.SelectedMode;
                App.Settings.Current.ContentModeChosen = true;

                // Initialize default triggers for chosen mode
                App.Settings.Current.SubliminalPool = ContentModeConfig.GetDefaultSubliminalPool(dialog.SelectedMode);
                App.Settings.Current.LockCardPhrases = ContentModeConfig.GetDefaultLockCardPhrases(dialog.SelectedMode);
                App.Settings.Current.CustomTriggers = ContentModeConfig.GetDefaultCustomTriggers(dialog.SelectedMode);

                App.Settings.Save();
                return dialog.SelectedMode;
            }
            return App.Settings.Current.ContentMode;
        }
    }
}
