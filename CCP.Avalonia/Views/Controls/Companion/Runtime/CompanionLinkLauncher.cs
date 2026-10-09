using System;
using System.Windows.Input;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Logging;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>
    /// The ONE way a link the companion offered gets opened: the tube speech bubble, the tube chat
    /// log, the chat room watch chip, the conversation page and an ask choice all come here.
    /// Port of WPF <c>Views/Controls/Companion/Runtime/CompanionLinkLauncher.cs</c> (and of
    /// <c>SpeechBubbleHyperlink_RequestNavigate</c>, which is the same sequence there):
    /// refuse while a remote controller is connected, http(s) only, prefer the embedded browser,
    /// fall back to the system browser for https only.
    ///
    /// <para>Differs from WPF: this head has no BrowserMediaService, so there is no
    /// ReplaceSession(AvatarLink, takeover) claim and no release on the fallback; and
    /// NavigateToUrlInBrowser accepts autoPlayFullscreen but cannot honour it (no script channel).
    /// Logs carry the host only, never the full url.</para>
    /// </summary>
    internal static class CompanionLinkLauncher
    {
        internal enum Outcome { Refused, BlockedByRemote, Embedded, External, Failed }

        /// <summary>Test seams. Defaults are the live shell.</summary>
        internal static Func<bool> RemoteControllerConnected = () =>
            RemoteControlTabView.Relay.IsValueCreated && RemoteControlTabView.Relay.Value.ControllerConnected;

        internal static Func<string, bool> NavigateEmbedded = url =>
            MainShellWindow.Current?.NavigateToUrlInBrowser(url, autoPlayFullscreen: true) == true;

        internal static Action<string> OpenExternal = url =>
            _ = Platform.ExternalOpener.OpenAsync(AvatarTube.AvatarTubeWindow.Live, url);

        internal static ICommand CommandFor(string url) => new LinkCommand(url);

        internal static Outcome Open(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return Outcome.Refused;
            try
            {
                // A controller driving the session must not have the browser navigated out from
                // under it mid-playback.
                if (RemoteControllerConnected())
                {
                    Log.Debug("Companion link blocked - remote controller is connected");
                    return Outcome.BlockedByRemote;
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                {
                    Log.Warning("Companion link refused: not http(s)");
                    return Outcome.Refused;
                }

                var target = uri.AbsoluteUri;
                if (NavigateEmbedded(target))
                {
                    Log.Information("Companion link routed to the embedded browser: {Host}", UrlLog.Host(uri));
                    return Outcome.Embedded;
                }

                if (uri.Scheme == Uri.UriSchemeHttps)
                {
                    Log.Warning("Embedded browser unavailable, opening the companion link externally: {Host}", UrlLog.Host(uri));
                    OpenExternal(target);
                    return Outcome.External;
                }
                return Outcome.Refused;
            }
            catch (Exception ex)
            {
                Log.Error("Companion link failed to open: {Type}", ex.GetType().Name);
                return Outcome.Failed;
            }
        }

        private sealed class LinkCommand : ICommand
        {
            private readonly string _url;
            internal LinkCommand(string url) => _url = url;
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => Open(_url);
        }
    }
}
