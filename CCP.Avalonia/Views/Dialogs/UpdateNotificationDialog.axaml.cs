using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Dialogs/UpdateNotificationDialog.xaml.cs. Deviations:
    ///  - <c>DialogResult</c> becomes <c>Close(bool)</c>; Avalonia carries the result through
    ///    <c>ShowDialog&lt;bool?&gt;</c>.
    ///  - <c>UpdateService.GetCurrentVersion()</c>, <c>UpdateService.ReleasesPageUrl</c> and
    ///    <c>BrowserLauncher</c> live in the WPF head, so they are stubbed (see below).
    ///  - The version/size/notes strings are English literals in the WPF original too - there are
    ///    no loc keys for them - so they are copied verbatim rather than invented.
    /// </summary>
    public partial class UpdateNotificationDialog : Window
    {
        /// <summary>
        /// Whether the user chose to install the update
        /// </summary>
        public bool InstallRequested { get; private set; }

        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog.</summary>
        internal UpdateNotificationDialog() : this(new UpdateInfo
        {
            Version = "7.0.0",
            FileSizeBytes = 320_050_000,
            ReleaseNotes = "## The Spiral\n\n- **Avalonia head** now renders every ported view headless.\n- Fixed a [crash](https://example.invalid) on startup when no audio device was present.\n\n---\n\n### Known issues\n- Chaos overlays are still Windows-only.",
        })
        { }

        public UpdateNotificationDialog(UpdateInfo updateInfo)
        {
            AvaloniaXamlLoader.Load(this);

            // ponytail: needs UpdateService.GetCurrentVersion(), wired when UpdateService moves to Core
            const string currentVersion = "0.0.0";

            this.FindControl<TextBlock>("TxtVersionInfo")!.Text =
                $"Version {updateInfo.Version} is now available.\n" +
                $"You are currently on version {currentVersion}.";

            this.FindControl<TextBlock>("TxtFileSize")!.Text = $"Download size: {updateInfo.FormattedFileSize}";

            // Use release notes from GitHub (fetched during update check)
            // Don't fallback to CurrentPatchNotes as those are for the CURRENT version, not the new one
            var notes = this.FindControl<TextBlock>("TxtReleaseNotes")!;
            notes.Text = !string.IsNullOrWhiteSpace(updateInfo.ReleaseNotes)
                ? ConvertMarkdownToPlainText(updateInfo.ReleaseNotes)
                : $"Version {updateInfo.Version} is available.\n\nRelease notes were not provided for this update.";

            this.FindControl<HyperlinkButton>("LinkManualDownload")!.Click += (_, _) => LinkManualDownload_Click();
            this.FindControl<Button>("BtnLater")!.Click += (_, _) => BtnLater_Click();
            this.FindControl<Button>("BtnInstall")!.Click += (_, _) => BtnInstall_Click();
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

        /// <summary>
        /// Opens the GitHub releases page so the user can install the update by hand. The dialog
        /// stays open - they may still want the automatic install if the browser route falls over.
        /// </summary>
        private void LinkManualDownload_Click()
        {
            // ponytail: needs BrowserLauncher.OpenUrlOrPrompt(UpdateService.ReleasesPageUrl,
            // "open the download page"), wired when both move to Core
        }

        private void BtnLater_Click()
        {
            InstallRequested = false;
            Close(false);
        }

        private void BtnInstall_Click()
        {
            InstallRequested = true;
            Close(true);
        }
    }
}
