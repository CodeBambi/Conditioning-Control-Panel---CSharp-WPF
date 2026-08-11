using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Services.Browser;

namespace ConditioningControlPanel.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// Opens a link the app itself issued, from a chat bubble's watch chip.
    ///
    /// <para>Deliberately the same sequence the speech bubble already uses
    /// (<c>AvatarTubeWindow.Speech.cs</c> <c>SpeechBubbleHyperlink_RequestNavigate</c>): refuse while
    /// a remote controller is attached, hand the media session over explicitly, prefer the embedded
    /// browser, and fall back to the system browser for HTTPS only. Two ways to open the same kind
    /// of link would be two sets of rules about remote control and media ownership, and one of them
    /// would drift.</para>
    /// </summary>
    internal static class CompanionLinkLauncher
    {
        internal static ICommand CommandFor(string url) => new RelayCommand(() => Open(url));

        internal static void Open(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            try
            {
                // A controller driving the session must not have the browser navigated out from
                // under it mid-playback.
                if (App.RemoteControl?.ControllerConnected == true)
                {
                    App.Logger?.Debug("Companion watch chip blocked - remote controller is connected");
                    return;
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                {
                    App.Logger?.Warning("Companion watch chip refused a non-http(s) link");
                    return;
                }

                var mainWindow = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();

                // The user clicked it themselves, so never refuse — but take the session over
                // explicitly instead of navigating out from under a previous claim.
                App.BrowserMedia?.ReplaceSession(BrowserMediaService.MediaOwner.AvatarLink, takeover: true);

                if (mainWindow?.NavigateToUrlInBrowser(url, autoPlayFullscreen: true) == true)
                {
                    App.Logger?.Information("Companion watch chip routed to the embedded browser");
                    return;
                }

                // The embedded browser never issued the navigation, so release the session we just
                // claimed — otherwise the app keeps treating a video that is not playing here as an
                // active web-media session.
                App.BrowserMedia?.OnMediaStopped("embedded-browser-unavailable");

                if (uri.Scheme == Uri.UriSchemeHttps)
                {
                    App.Logger?.Warning("Embedded browser unavailable, opening the watch chip externally");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Companion watch chip failed to open its link");
            }
        }

        private sealed class RelayCommand : ICommand
        {
            private readonly Action _run;
            internal RelayCommand(Action run) => _run = run;

            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => _run();
        }
    }
}
