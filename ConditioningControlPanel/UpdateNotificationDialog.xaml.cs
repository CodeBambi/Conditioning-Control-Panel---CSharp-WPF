using System.Windows;
using System.Text.RegularExpressions;
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
                TxtReleaseNotes.Text = ConvertMarkdownToPlainText(updateInfo.ReleaseNotes);
            }
            else
            {
                TxtReleaseNotes.Text = $"Version {updateInfo.Version} is available.\n\nRelease notes were not provided for this update.";
            }
        }

        /// <summary>
        /// Convert GitHub markdown release notes to readable plain text for the TextBlock.
        /// </summary>
        private static string ConvertMarkdownToPlainText(string markdown)
        {
            var text = markdown;

            // Remove horizontal rules
            text = Regex.Replace(text, @"^---+\s*$", "", RegexOptions.Multiline);

            // Convert ### headers to uppercase with newline
            text = Regex.Replace(text, @"^###\s*(.+)$", "\n$1", RegexOptions.Multiline);

            // Convert ## headers to uppercase with newline
            text = Regex.Replace(text, @"^##\s*(.+)$", "\n$1", RegexOptions.Multiline);

            // Remove bold markers **text** -> text
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");

            // Convert markdown list items to bullet points
            text = Regex.Replace(text, @"^- ", "• ", RegexOptions.Multiline);

            // Remove markdown links [text](url) -> text
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");

            // Collapse excessive newlines
            text = Regex.Replace(text, @"\n{3,}", "\n\n");

            return text.Trim();
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
