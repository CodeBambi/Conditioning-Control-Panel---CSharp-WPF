using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Services.JustDrop
{
    /// <summary>
    /// Host service for JUST DROP. Owns the window the shop and the player run in, and the sign-in
    /// handoff that makes them feel like part of the app instead of a browser someone embedded.
    ///
    /// <para><b>Why this exists.</b> The door used to be a tab holding a WebView2 pointed straight
    /// at the shop, with its own empty cookie jar. Two things followed from that, and both were
    /// wrong: the user had to sign in again inside the app they were already signed into, and a
    /// full-bleed session played inside a tab with the app's chrome around it. This service is the
    /// same shell <see cref="Services.Quiz.IntakeHostService"/> and
    /// <see cref="DtrhHostService"/> use - a real window, natively owned by MainWindow - plus the
    /// handoff below.</para>
    ///
    /// <para><b>The window stays the host's.</b> Unlike those two, this one runs
    /// <see cref="ChaosWebViewHost.Options.HostOwnedFullscreen"/>. The shop's player calls
    /// <c>requestFullscreen()</c> the moment an order opens, and that used to hard-fullscreen the
    /// WPF window: title bar gone, no toggle, Esc unheard because a focused WebView2 never gave it
    /// to WPF, and the taskbar still painted over the page's own exit button. Page fullscreen now
    /// fills the client area and stops there; the window is windowed unless the user says
    /// otherwise, from a toggle that is always on screen.</para>
    ///
    /// <para><b>The sign-in handoff.</b> The app holds a ccp-server token; the site wants a Supabase
    /// session. Rather than ask the user to bridge that gap by typing a password, the window's FIRST
    /// navigation is to <c>/api/auth/desktop-session</c>, which takes the CCP token as a header,
    /// mints the matching Supabase session server-side, sets it as cookies and redirects to wherever
    /// we were actually going. The token rides a HEADER, never the URL, so it stays out of history
    /// and the Referer - which is the whole reason
    /// <see cref="ChaosWebViewHost.Options.OnCoreCreated"/> had to exist: the filter that attaches
    /// it is worthless if it lands after the request has gone.</para>
    ///
    /// <para><b>Doctrine, unchanged.</b> Everything about a drop still lives in the web app. This
    /// service opens a window and hands over a credential; it does not know what a drop is, does not
    /// keep an order list, and re-implements no effect. See <see cref="JustDropService"/>.</para>
    /// </summary>
    internal static class JustDropHostService
    {
        /// <summary>The dashboard app. Bare cclabs.app is the marketing site and serves none of this.</summary>
        public const string SiteHost = "app.cclabs.app";

        private const string SiteOrigin = "https://" + SiteHost;

        /// <summary>The shop: browse, order, wallet.</summary>
        private const string ShopPath = "/dashboard/express";

        /// <summary>The bare player. No sidebar, no header, nothing but the session - and no wallet
        /// and no payout, which is why replaying from the shelf is free by construction.</summary>
        private const string PlayerPath = "/express/play";

        private static ChaosWebViewHost? _host;
        private static bool _disposing;
        private static bool _duckedMainWindow;
        private static WindowState _mainStateBeforeDuck = WindowState.Normal;

        public static bool IsActive => _host != null;

        // ============================== launching ==============================

        /// <summary>
        /// Open the shop. Windowed and WITHOUT ducking the control panel: this is browsing, and
        /// minimizing the app out from under someone who is picking a session reads as a glitch.
        /// The player below is the opposite case.
        ///
        /// <para>And it STAYS windowed: opening an order used to drag the window borderless behind
        /// the page's back. Fullscreen is now a choice the user makes, from the strip.</para>
        /// </summary>
        public static void LaunchShop() => Launch(ShopPath, fullscreen: false, duckMain: false);

        /// <summary>
        /// Replay a delivered order. Fullscreen and ducking, because this one IS the experience -
        /// same posture the Intake takes. Fullscreen here is the host's, so the strip and Esc are
        /// both live from the first frame: cinematic, never a cage.
        ///
        /// <para>Free, and not by our restraint: the bare player mints nothing, and the desktop's
        /// own XP grant is deduplicated per order code (see
        /// <see cref="JustDropService.HandleWebMessage"/>), so a shelf full of replays cannot be
        /// farmed.</para>
        /// </summary>
        public static void LaunchReplay(string orderCode)
        {
            if (string.IsNullOrWhiteSpace(orderCode))
            {
                App.Logger?.Warning("JustDropHost: replay asked for with no order code");
                return;
            }
            Launch($"{PlayerPath}?order={Uri.EscapeDataString(orderCode.Trim())}",
                   fullscreen: true, duckMain: true);
        }

        private static void Launch(string nextPath, bool fullscreen, bool duckMain)
        {
            // A running window is re-focused rather than replaced: two shops open on one account is
            // a wallet racing itself.
            if (_host != null) { _host.FocusWeb(); return; }

            try
            {
                _host = new ChaosWebViewHost(new ChaosWebViewHost.Options
                {
                    StartUrl = BuildStartUrl(nextPath),
                    PrimaryHost = SiteHost,
                    // Nothing is served from disk: the shop is the live site, not a local port.
                    Mappings = Array.Empty<(string, string, CoreWebView2HostResourceAccessKind)>(),
                    UserDataFolderName = "browser_data_justdrop",
                    InputEnabled = true,
                    StartFullscreen = fullscreen,
                    // The window's fullscreen is ours, not the remote page's: a visible toggle, a
                    // live Esc, and a fullscreen that clears the taskbar when it IS asked for.
                    HostOwnedFullscreen = true,
                    // Keep it above MainWindow by native ownership, not Topmost - main gets raised
                    // by things a session does not control and would otherwise bury the page.
                    OwnedByMainWindow = true,
                    WindowTitle = "Just Drop",
                    LogTag = "JustDropHost",
                    // --autoplay-policy: the session's audio bed must start without a user gesture.
                    // --disable-background-timer-throttling / --disable-backgrounding-occluded-windows:
                    //   a drop is a LIVE CLOCK, exactly like a Goon match - one rAF loop drives the
                    //   spiral, the media rotation and the audio ramp, and the feed layer reads a
                    //   clock that did not move as "paused" and stops its videos. Meanwhile the app
                    //   is spending the whole session throwing topmost payload windows over this one
                    //   (bug #980's own log shows flashes, subliminals and bouncing text firing all
                    //   the way through the drop). Chromium throttling a window it thinks is
                    //   backgrounded would not slow the session down, it would stop it.
                    ExtraBrowserArguments =
                        "--autoplay-policy=no-user-gesture-required "
                        + "--disable-background-timer-throttling "
                        + "--disable-backgrounding-occluded-windows",
                    OnCoreCreated = AttachAuthHeader,
                    OnMessage = OnPageMessage,
                    OnReady = () => App.Logger?.Information("JustDropHost: page ready"),
                    OnProcessFailed = OnProcessFailed,
                });
                _host.Show();
                if (_host.Window != null) _host.Window.Closed += (_, _) => DisposeAll();
                if (duckMain) DuckMainWindow();
                App.Logger?.Information("JustDropHost: launched -> {Next}", nextPath);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "JustDropHostService.Launch failed");
                DisposeAll();
            }
        }

        /// <summary>
        /// Where the window opens. With an account in hand that is the sign-in handoff, carrying the
        /// real destination in <c>next</c>; without one it is the destination itself, and the site
        /// asks who is calling exactly as it should - a signed-out user has nothing to hand over,
        /// and pretending otherwise would just be a broken redirect.
        /// </summary>
        private static string BuildStartUrl(string nextPath)
        {
            var unifiedId = SafeUnifiedId();
            if (string.IsNullOrEmpty(unifiedId) || string.IsNullOrEmpty(SafeAuthToken()))
            {
                App.Logger?.Information("JustDropHost: no account state; opening signed-out");
                return SiteOrigin + nextPath;
            }
            return $"{SiteOrigin}/api/auth/desktop-session" +
                   $"?unified_id={Uri.EscapeDataString(unifiedId)}" +
                   $"&next={Uri.EscapeDataString(nextPath)}";
        }

        /// <summary>
        /// Put the CCP token on the sign-in request, and on nothing else. The filter is scoped to
        /// the one path that reads it, so no other request this page makes - to our own site or to
        /// anything it embeds - ever carries the app's credential.
        /// </summary>
        private static void AttachAuthHeader(CoreWebView2 core)
        {
            var token = SafeAuthToken();
            if (string.IsNullOrEmpty(token)) return;

            core.AddWebResourceRequestedFilter(
                SiteOrigin + "/api/auth/desktop-session*",
                CoreWebView2WebResourceContext.Document);

            core.WebResourceRequested += (_, e) =>
            {
                try { e.Request.Headers.SetHeader("X-CCP-Auth-Token", token); }
                catch (Exception ex)
                {
                    // Never log the token itself - only that stamping it failed.
                    App.Logger?.Warning("JustDropHost: could not attach the auth header: {E}", ex.Message);
                }
            };
        }

        /// <summary>The ccp-server token the site's bridge endpoint validates. Empty when signed
        /// out. Never logged, never put in a URL.</summary>
        private static string SafeAuthToken()
        {
            try { return App.Settings?.Current?.AuthToken ?? string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>The account the token belongs to. The server cross-validates the pair and
        /// rejects a mismatch, so sending both is the contract, not belt-and-braces.</summary>
        private static string SafeUnifiedId()
        {
            try { return App.UnifiedUserId ?? string.Empty; }
            catch { return string.Empty; }
        }

        // ============================== the bridge ==============================

        /// <summary>
        /// Every page message except <c>ready</c>/<c>log</c>, which ChaosWebViewHost consumes.
        /// Handed straight to <see cref="JustDropService"/> as the raw envelope so the source/version
        /// checks stay in one place rather than being re-derived per host.
        /// </summary>
        private static void OnPageMessage(JObject o)
        {
            try { JustDropService.HandleWebMessage(o.ToString(Formatting.None)); }
            catch (Exception ex) { App.Logger?.Warning(ex, "JustDropHost: message handling failed"); }
        }

        private static void OnProcessFailed(CoreWebView2ProcessFailedKind kind)
        {
            // No relaunch ladder here, unlike the Intake. That window hosts a run whose state lives
            // in the page and would be lost; this one is a shop and a player keyed by an order code,
            // both of which are exactly where the user left them when they open it again. Coming
            // back on demand beats coming back uninvited.
            App.Logger?.Warning("JustDropHost: WebView2 process failed ({Kind}); closing", kind);
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) { DisposeAll(); return; }
            disp.BeginInvoke(new Action(DisposeAll));
        }

        // ============================== window bookkeeping ==============================

        /// <summary>
        /// Get the control panel out of the way for a session. A plain minimize, not DtRH's tray
        /// tuck: this is a windowed experience and making the app's taskbar button vanish for it
        /// would read as a crash. No-op when main is already minimized or hidden - the user's last
        /// word on their own window stands, and we take no restore debt we did not earn.
        /// </summary>
        private static void DuckMainWindow()
        {
            try
            {
                var main = (Window?)App.MainWindowRef ?? Application.Current?.MainWindow;
                if (main == null || !main.IsVisible || main.WindowState == WindowState.Minimized) return;
                _mainStateBeforeDuck = main.WindowState;
                main.WindowState = WindowState.Minimized;
                _duckedMainWindow = true;
                // Minimizing main hands activation to whatever is next in the z-order; take it back.
                _host?.FocusWeb();
            }
            catch (Exception ex) { App.Logger?.Debug("JustDropHost.DuckMainWindow: {E}", ex.Message); }
        }

        /// <summary>Undo <see cref="DuckMainWindow"/>, from the single funnel every close runs
        /// through - a tool that minimizes the app and leaves it that way is a worse bug than the
        /// one ducking solves.</summary>
        private static void RestoreMainWindow()
        {
            if (!_duckedMainWindow) return;
            _duckedMainWindow = false;
            try
            {
                var disp = Application.Current?.Dispatcher;
                if (disp == null || disp.HasShutdownStarted) return;
                var main = (Window?)App.MainWindowRef ?? Application.Current?.MainWindow;
                if (main == null || !main.IsVisible) return;
                if (main.WindowState != WindowState.Minimized) return;   // the user got there first
                main.WindowState = _mainStateBeforeDuck == WindowState.Maximized
                    ? WindowState.Maximized
                    : WindowState.Normal;
                try { main.Activate(); } catch { }
            }
            catch (Exception ex) { App.Logger?.Debug("JustDropHost.RestoreMainWindow: {E}", ex.Message); }
        }

        /// <summary>Close the window if one is open. Safe to call when there isn't.</summary>
        public static void CloseActive() => DisposeAll();

        private static void DisposeAll()
        {
            if (_disposing) return;   // Dispose closes the window, re-raising Closed -> here
            _disposing = true;
            try
            {
                try { _host?.Dispose(); } catch { }
                _host = null;
                RestoreMainWindow();

                // The drawer almost certainly changed while that window was open - an order was
                // placed, or one was thrown out. Invalidate BEFORE refreshing, or the shelf repaints
                // from the cached answer we are trying to replace.
                try
                {
                    JustDropOrdersService.Invalidate();
                    App.MainWindowRef?.RefreshTakeawayShelf();
                }
                catch (Exception ex) { App.Logger?.Debug("JustDropHost: shelf refresh failed: {E}", ex.Message); }

                App.Logger?.Information("JustDropHost: closed");
            }
            finally { _disposing = false; }
        }
    }
}
