using System;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Shown whenever an automatic update could not be downloaded or installed. The point of this
    /// dialog is the way out: a plain OK box left users stranded on the old build with nowhere to
    /// go, so volunteers ended up pasting the GitHub releases link into support by hand. Every
    /// failure path now offers the manual installer directly.
    /// </summary>
    public partial class UpdateFailedDialog : Window
    {
        private DispatcherTimer? _copyResetTimer;

        /// <summary>
        /// Whether the user opened (or was handed) the manual download link.
        /// </summary>
        public bool ManualDownloadRequested { get; private set; }

        /// <param name="title">Dialog heading, e.g. "Update Failed".</param>
        /// <param name="message">Plain-language explanation of what went wrong.</param>
        /// <param name="detail">Optional technical detail (the exception message). Hidden when empty.</param>
        public UpdateFailedDialog(string title, string message, string? detail = null)
        {
            InitializeComponent();

            TxtTitle.Text = title;
            TxtMessage.Text = message;

            if (!string.IsNullOrWhiteSpace(detail))
            {
                TxtDetail.Text = detail;
                DetailPanel.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Shows the dialog modally, parented to <paramref name="owner"/> when it is usable.
        /// Never throws - a broken owner window must not swallow the update failure.
        /// </summary>
        public static void ShowFor(Window? owner, string title, string message, string? detail = null)
        {
            try
            {
                var dialog = new UpdateFailedDialog(title, message, detail)
                {
                    Topmost = true
                };

                if (owner != null && owner.IsLoaded && owner.IsVisible)
                {
                    dialog.Owner = owner;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                else
                {
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to show update failure dialog; falling back to a message box");
                try
                {
                    MessageBox.Show($"{message}\n\n{UpdateService.ReleasesPageUrl}", title,
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
            }
        }

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            ManualDownloadRequested = true;

            // BrowserLauncher falls back through explorer/cmd/rundll32 and finally the clipboard,
            // so machines with no default browser still get the link.
            BrowserLauncher.OpenUrlOrPrompt(UpdateService.ReleasesPageUrl, "open the download page");

            DialogResult = true;
            Close();
        }

        private void BtnCopyLink_Click(object sender, RoutedEventArgs e)
        {
            ManualDownloadRequested = true;

            try
            {
                Clipboard.SetText(UpdateService.ReleasesPageUrl);
            }
            catch (Exception ex)
            {
                // Clipboard can be locked by another app - say nothing, the button just won't confirm.
                App.Logger?.Warning(ex, "Failed to copy the releases link to the clipboard");
                return;
            }

            BtnCopyLink.Content = Loc.Get("btn_copied");

            _copyResetTimer?.Stop();
            _copyResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _copyResetTimer.Tick += (s, args) =>
            {
                _copyResetTimer?.Stop();
                try { BtnCopyLink.Content = Loc.Get("btn_copy_link"); } catch { }
            };
            _copyResetTimer.Start();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _copyResetTimer?.Stop();
            DialogResult = false;
            Close();
        }
    }
}
