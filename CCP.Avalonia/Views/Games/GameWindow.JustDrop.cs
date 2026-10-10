using System;
using System.Linq;
using Avalonia.Controls;
using ConditioningControlPanel.Services.JustDrop;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// JUST DROP's window (WPF 7.1.5 Services/JustDrop/JustDropHostService.cs): the live shop at
    /// app.cclabs.app in a window of its own, signed in by the handoff below. For everyone (Tier 0):
    /// the only gate is the server's door flag, re-checked here on every open.
    ///
    /// <para>Nothing is served from disk and no <c>ccp.*</c> host is involved: the page is the site.
    /// Navigation stays on the site's own https origin (the window's same-origin rule), and page
    /// messages are only read from that origin (GameWindow.OnPageMessage).</para>
    ///
    /// <para><b>The sign-in handoff.</b> The first navigation is <c>/api/auth/desktop-session</c>; the
    /// app's token rides a HEADER on that one request (<see cref="JustDropHostService.AuthHeaderFor(Uri?)"/>
    /// through <c>WebHost.RequestHeader</c>), never the url. Signed out, the shop opens signed out.</para>
    ///
    /// <para>A panic closes it with every other game window (PanicSurfaces "games").</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        internal const string JustDropId = "justdrop";

        /// <summary>Where the next Just Drop window opens (shop, or one order's bare player).</summary>
        private static string _justDropNext = JustDropHostService.ShopPath;
        private static bool _justDropReplay;

        private WindowState _shellStateBeforeDuck;
        private Window? _duckedShell;

        /// <summary>WPF ShowTab("justdrop"): the door's own refusal. A door the account cannot see is
        /// a no-op, never a redirect.</summary>
        private static bool JustDropDoorOpen()
        {
            if (JustDropService.DoorAvailable) return true;
            Log.Debug("[Game] justdrop ignored: the door is not available on this account");
            return false;
        }

        /// <summary>WPF JustDropHostService.LaunchShop: windowed, the panel stays where it is.</summary>
        internal static GameWindow? LaunchJustDropShop() => LaunchJustDrop(JustDropHostService.ShopPath, replay: false);

        /// <summary>WPF LaunchReplay: one delivered order in the bare player, fullscreen, panel ducked.</summary>
        internal static GameWindow? LaunchJustDropReplay(string orderCode) =>
            string.IsNullOrWhiteSpace(orderCode) ? null : LaunchJustDrop(JustDropHostService.ReplayPath(orderCode), replay: true);

        private static GameWindow? LaunchJustDrop(string nextPath, bool replay)
        {
            // A running window is refocused, never replaced: two shops on one account is a wallet racing itself.
            _justDropNext = nextPath;
            _justDropReplay = replay;
            return Launch(JustDropId);
        }

        internal static bool IsJustDropOpen()
        {
            lock (Open) return Open.Any(w => w.Spec.Id == JustDropId);
        }

        internal static void CloseJustDrop()
        {
            GameWindow[] all;
            lock (Open) all = Open.Where(w => w.Spec.Id == JustDropId).ToArray();
            foreach (var w in all)
            {
                try { w.Close(); }
                catch (Exception ex) { Log.Debug("[Game] justdrop close: {E}", ex.Message); }
            }
        }

        /// <summary>The window's load (from <see cref="Load"/>): the handoff url, the header rule, the posture.</summary>
        private void LoadJustDrop()
        {
            bool replay = _justDropReplay;
            // The credential rides ONE request: the handoff path on the site's own origin.
            Web.RequestHeader = uri => JustDropHostService.AuthHeaderFor(uri) is { Length: > 0 } token
                ? (JustDropHostService.AuthHeaderName, token)
                : null;
            PageUrl = new Uri(JustDropHostService.BuildStartUrl(_justDropNext));
            Web.Navigate(PageUrl);
            Log.Information("[Game] justdrop: launched ({Kind})", replay ? "replay" : "shop");   // never the url: it names the account
            if (!replay) return;
            Opened += (_, _) => { SetWindowFullscreen(true); DuckShellForDrop(); };
            Closed += (_, _) => RestoreDuckedShell();
        }

        private void SetWindowFullscreen(bool on)
        {
            try { WindowState = on ? WindowState.FullScreen : WindowState.Normal; }
            catch (Exception ex) { Log.Debug("[Game] justdrop fullscreen: {E}", ex.Message); }
        }

        /// <summary>WPF DuckMainWindow: a plain minimize for a session. No-op when the panel is
        /// already minimized or hidden (the user's last word on their own window stands).</summary>
        private void DuckShellForDrop()
        {
            try
            {
                var shell = Windows.MainShellWindow.Current;
                if (shell == null || !shell.IsVisible || shell.WindowState == WindowState.Minimized) return;
                _shellStateBeforeDuck = shell.WindowState;
                shell.WindowState = WindowState.Minimized;
                _duckedShell = shell;
                Activate();
            }
            catch (Exception ex) { Log.Debug("[Game] justdrop duck: {E}", ex.Message); }
        }

        /// <summary>WPF RestoreMainWindow: undo the duck unless the user got there first.</summary>
        private void RestoreDuckedShell()
        {
            var shell = _duckedShell;
            _duckedShell = null;
            if (shell == null) return;
            try
            {
                if (!shell.IsVisible || shell.WindowState != WindowState.Minimized) return;
                shell.WindowState = _shellStateBeforeDuck == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
                shell.Activate();
            }
            catch (Exception ex) { Log.Debug("[Game] justdrop restore: {E}", ex.Message); }
        }

        /// <summary>Every page message goes to <see cref="JustDropService"/> as the raw envelope, so
        /// the source / version checks stay in one place. Always claimed: the remote site never gets
        /// the local games' init or manifest frames.</summary>
        private bool HandleJustDrop(JObject o)
        {
            try { JustDropService.HandleWebMessage(o.ToString(Formatting.None)); }
            catch (Exception ex) { Log.Warning(ex, "[Game] justdrop: message handling failed"); }
            return true;
        }
    }
}
