using System.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    public partial class UpdateNotificationDialog : Window
    {
        /// <summary>
        /// Whether the user chose to install the update
        /// </summary>
        public bool InstallRequested { get; private set; }

        public UpdateNotificationDialog(UpdateInfo updateInfo)
        {
            InitializeComponent();

            TxtVersionInfo.Text = $"Version {updateInfo.Version} is now available.\n" +
                                  $"You are currently on version {UpdateService.GetCurrentVersion()}.";

            TxtFileSize.Text = $"Download size: {updateInfo.FormattedFileSize}";

            // Use release notes from GitHub (fetched during update check)
            // Don't fallback to CurrentPatchNotes as those are for the CURRENT version, not the new one
            if (!string.IsNullOrWhiteSpace(updateInfo.ReleaseNotes))
            {
                TxtReleaseNotes.Text = updateInfo.ReleaseNotes;
            }
            else
            {
                TxtReleaseNotes.Text = $"Version {updateInfo.Version} is available.\n\nRelease notes were not provided for this update.";
            }
        }

        private void BtnLater_Click(object sender, RoutedEventArgs e)
        {
            InstallRequested = false;
            DialogResult = false;
            Close();
        }

        private void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            InstallRequested = true;
            DialogResult = true;
            Close();
        }
    }
}
