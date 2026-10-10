// The controller's play_hypnotube on this head (owner, 2026-10-10: ported, site-locked).
// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Browser.cs PlayHypnotubeFromRemote /
// StopBrowserVideoFromRemote (:3069-3170). The address reaching this file already passed Core
// RemoteVideoLink (https, the one site, a video page) and is its Uri.AbsoluteUri.
// not ported: the forced fullscreen autoplay (WebHost has no script channel, see MainShellWindow.Browser.cs
// deviation 2) and BrowserMediaService's session hand-over (no such service on this head).

using System;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        internal const string GameOnScreen = "a game is on screen";
        internal const string BrowserCouldNotOpen = "the browser could not open it";

        // True only while a controller's video link is up: the stop below never touches a page the
        // subject opened (WPF _remoteBrowserVideoActive).
        private bool _remoteBrowserVideoActive;
        internal bool IsRemoteBrowserVideoActive => _remoteBrowserVideoActive;

        internal void MarkRemoteVideoForTest() => _remoteBrowserVideoActive = true;

        /// <summary>Test seam: what "a game owns the screen" reads.</summary>
        internal static Func<bool> RemoteVideoScreenOwned { get; set; } = () => Games.GameWindow.IsAnyOpen();

        string? RemoteCommands.IRemoteHead.PlayVideoLink(string absoluteUri) => PlayVideoLinkFromRemote(absoluteUri);

        void RemoteCommands.IRemoteHead.StopVideoLink() => StopBrowserVideoFromRemote();

        internal string? PlayVideoLinkFromRemote(string absoluteUri)
        {
            // Belt and braces: this head opens nothing the Core rule would not.
            if (!RemoteVideoLink.TryParse(absoluteUri, out var link)) return RemoteVideoLink.Refused;
            // WPF ScreenOwningWebHostName: a controller's verb is no authority to cover someone's game, and
            // the controller is told why nothing happened (ccp-bugs#1138).
            if (RemoteVideoScreenOwned()) return GameOnScreen;
            _remoteBrowserVideoActive = true;
            RevealBrowserForRemoteVideo(true);
            if (!NavigateToUrlInBrowser(link.AbsoluteUri, autoPlayFullscreen: true, userInitiated: false))
            {
                _remoteBrowserVideoActive = false;
                RevealBrowserForRemoteVideo(false);
                return BrowserCouldNotOpen;
            }
            Log.Information("[RemoteControl] play_hypnotube id={Id}", global::ConditioningControlPanel.Helpers.HtUrlHelper.TryExtractHtVideoId(link.AbsoluteUri));
            return null;
        }

        /// <summary>WPF StopBrowserVideoFromRemote: panic, the remote stop paths and the controller leaving.
        /// Back to the site's home page (the video element is torn down, the browser stays usable).</summary>
        internal void StopBrowserVideoFromRemote()
        {
            if (!_remoteBrowserVideoActive) return;
            _remoteBrowserVideoActive = false;
            RevealBrowserForRemoteVideo(false);
            try { NavigateBrowser(SiteHomeUrl()); }
            catch (Exception ex) { Log.Debug("[RemoteControl] video link stop failed: {Error}", ex.Message); }
        }
    }
}
