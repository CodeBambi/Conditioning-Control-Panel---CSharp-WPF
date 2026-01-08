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

            TxtReleaseNotes.Text = string.IsNullOrWhiteSpace(updateInfo.ReleaseNotes)
                ? "No release notes available."
                : updateInfo.ReleaseNotes;
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
