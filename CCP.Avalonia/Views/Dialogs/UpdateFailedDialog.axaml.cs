using System;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    /// <summary>
    /// Shown whenever an automatic update could not be downloaded or installed. The point of this
    /// dialog is the way out: a plain OK box left users stranded on the old build with nowhere to
    /// go, so volunteers ended up pasting the GitHub releases link into support by hand. Every
    /// failure path now offers the manual installer directly.
    ///
    /// PORTED from ConditioningControlPanel/Dialogs/UpdateFailedDialog.xaml.cs. Deviations:
    ///  - <c>DialogResult</c> becomes <c>Close(bool)</c>; Avalonia carries the result through
    ///    <c>ShowDialog&lt;bool?&gt;</c>.
    ///  - <c>App.Logger</c> becomes Serilog's static <c>Log</c>.
    ///  - <c>BrowserLauncher.OpenUrlOrPrompt</c> is a Win32 explorer/cmd/rundll32 chain and will
    ///    never be in Core; <c>TopLevel.Launcher.LaunchUriAsync</c> is this head's equivalent and
    ///    the chain's last resort - copying the link to the clipboard - is kept.
    ///  - <c>UpdateService.ReleasesPageUrl</c> is now <c>ReleaseLinks.ReleasesPageUrl</c> in Core.
    ///  - The copy-link label is swapped by rebinding, not by assigning Text: the TextBlock carries
    ///    a <c>{loc:Str}</c> binding and a local value would be undone on the next language change
    ///    (CLAUDE.md, "setting text from code").
    /// </summary>
    public partial class UpdateFailedDialog : Window
    {
        // The address lives in Core (Services/ReleaseLinks.cs); UpdateService.ReleasesPageUrl in
        // the WPF head aliases the same const, so both heads send the user to one page.
        private const string ReleasesPageUrl = ConditioningControlPanel.Services.ReleaseLinks.ReleasesPageUrl;

        private DispatcherTimer? _copyResetTimer;
        private readonly TextBlock _txtCopyLink;

        /// <summary>
        /// Whether the user opened (or was handed) the manual download link.
        /// </summary>
        public bool ManualDownloadRequested { get; private set; }

        /// <summary>Render/design constructor: sample data so --render-view can draw the dialog.</summary>
        internal UpdateFailedDialog() : this(
            Loc.Get("title_update_failed"),
            "The update could not be installed. The download was interrupted before it finished.",
            "System.IO.IOException: The process cannot access the file because it is being used by another process.")
        { }

        /// <param name="title">Dialog heading, e.g. "Update Failed".</param>
        /// <param name="message">Plain-language explanation of what went wrong.</param>
        /// <param name="detail">Optional technical detail (the exception message). Hidden when empty.</param>
        public UpdateFailedDialog(string title, string message, string? detail = null)
        {
            AvaloniaXamlLoader.Load(this);

            _txtCopyLink = this.FindControl<TextBlock>("TxtCopyLink")!;

            this.FindControl<TextBlock>("TxtTitle")!.Text = title;
            this.FindControl<TextBlock>("TxtMessage")!.Text = message;

            if (!string.IsNullOrWhiteSpace(detail))
            {
                this.FindControl<TextBlock>("TxtDetail")!.Text = detail;
                this.FindControl<Border>("DetailPanel")!.IsVisible = true;
            }

            this.FindControl<Button>("BtnDownload")!.Click += (_, _) => BtnDownload_Click();
            this.FindControl<Button>("BtnCopyLink")!.Click += (_, _) => BtnCopyLink_Click();
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => BtnClose_Click();
        }

        /// <summary>
        /// Shows the dialog modally, parented to <paramref name="owner"/> when it is usable.
        /// Never throws - a broken owner window must not swallow the update failure.
        /// </summary>
        public static void ShowFor(Window? owner, string title, string message, string? detail = null)
        {
            try
            {
                var dialog = new UpdateFailedDialog(title, message, detail) { Topmost = true };

                if (owner is { IsLoaded: true, IsVisible: true })
                {
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    _ = dialog.ShowDialog(owner);
                }
                else
                {
                    // ShowDialog needs an owner on Avalonia; without one this is a modeless window.
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    dialog.Show();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to show update failure dialog; nothing left to show it in");
                // ponytail: this head DOES have a message box now (Dialogs.MessageDialog), but it
                // is a Window like the one that just failed to open, and MessageDialog.ShowAsync
                // needs an owner - which is exactly what this catch has established it does not
                // have. WPF's MessageBox.Show(releasesUrl) needed neither. A second Window here
                // would fail the same way, so the log line is the record until this head grows an
                // ownerless notification (a tray toast, or a Window shown with .Show()).
            }
        }

        private async void BtnDownload_Click()
        {
            ManualDownloadRequested = true;

            // The WPF path went through BrowserLauncher, whose last resort was the clipboard, so
            // a machine with no usable browser still got the link. Same two steps here - and note
            // the launcher REPORTS failure rather than throwing when nothing handles the URI
            // (no xdg-open, no default browser), which is exactly the case that needs the
            // fallback, so the result is checked as well as the exception.
            var opened = false;
            try
            {
                opened = await Launcher.LaunchUriAsync(new Uri(ReleasesPageUrl));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to open the download page");
            }

            if (!opened)
            {
                Log.Warning("Could not open the download page; copying the link instead");
                try
                {
                    if (Clipboard is not null) await Clipboard.SetTextAsync(ReleasesPageUrl);
                }
                catch { /* clipboard may be locked by another app */ }
            }

            Close(true);
        }

        private async void BtnCopyLink_Click()
        {
            ManualDownloadRequested = true;

            try
            {
                if (Clipboard is null) return;
                await Clipboard.SetTextAsync(ReleasesPageUrl);
            }
            catch (Exception ex)
            {
                // Clipboard can be locked by another app - say nothing, the button just won't confirm.
                Log.Warning(ex, "Failed to copy the releases link to the clipboard");
                return;
            }

            SetCopyLinkKey("btn_copied");

            _copyResetTimer?.Stop();
            _copyResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _copyResetTimer.Tick += (_, _) =>
            {
                _copyResetTimer?.Stop();
                try { SetCopyLinkKey("btn_copy_link"); } catch { }
            };
            _copyResetTimer.Start();
        }

        /// <summary>
        /// Rebinds the copy button's label to another loc key. Assigning .Text instead would sit
        /// under the {loc:Str} binding the XAML installed and be undone on the next language change.
        /// </summary>
        private void SetCopyLinkKey(string key) =>
            _txtCopyLink.Bind(TextBlock.TextProperty, new Binding($"[{key}]")
            {
                Source = LocalizationManager.Instance,
                Mode = BindingMode.OneWay,
            });

        private void BtnClose_Click()
        {
            _copyResetTimer?.Stop();
            Close(false);
        }
    }
}
