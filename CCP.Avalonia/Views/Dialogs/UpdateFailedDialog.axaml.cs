using System;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;

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
    ///  - <c>UpdateService.ReleasesPageUrl</c>, <c>BrowserLauncher</c> and <c>App.Logger</c> all
    ///    live in the WPF head, so they are stubbed here (see the ponytail notes below).
    ///  - The copy-link label is swapped by rebinding, not by assigning Text: the TextBlock carries
    ///    a <c>{loc:Str}</c> binding and a local value would be undone on the next language change
    ///    (CLAUDE.md, "setting text from code").
    /// </summary>
    public partial class UpdateFailedDialog : Window
    {
        // ponytail: needs UpdateService.ReleasesPageUrl, wired when UpdateService moves to Core
        private const string ReleasesPageUrl =
            "https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/latest";

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

                if (owner is { IsVisible: true })
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
            catch
            {
                // ponytail: needs App.Logger and a MessageBox equivalent for the fallback path,
                // wired when the logger moves to Core. Swallowed for now, as WPF did.
            }
        }

        private void BtnDownload_Click()
        {
            ManualDownloadRequested = true;

            // ponytail: needs BrowserLauncher.OpenUrlOrPrompt(ReleasesPageUrl, "open the download page"),
            // wired when BrowserLauncher moves to Core

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
            catch
            {
                // Clipboard can be locked by another app - say nothing, the button just won't confirm.
                // ponytail: needs App.Logger for the warning, wired when the logger moves to Core
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
