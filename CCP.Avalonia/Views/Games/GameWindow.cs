using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// One web game in a window of its own: the port's stand-in for WPF's per-game WebView2 hosts
    /// (BackRoomHostService, BreakoutHostService, PieceByPieceHostService, GoonHostService,
    /// ArcademyHostService, DtrhHostService), all of which load https://ccp.game/&lt;page&gt; through
    /// ChaosWebViewHost. Here the page comes off <see cref="WebAssetServer"/> (the ccp.game virtual
    /// host) into a <see cref="WebHost"/>, one live window per game, focused rather than relaunched,
    /// and every one of them is a panic surface (PanicSurfaces "games").
    ///
    /// <para>The bridge is the shell protocol every one of those pages shares: the page says
    /// <c>ready</c>, the host answers <c>init</c> (language + identity) and an empty media
    /// <c>manifest</c>, and <c>exit</c> / <c>exit-done</c> / <c>boot-error</c> close the window.
    /// ponytail: each game's own host frames are not ported (DtRH meta/payout/run-config, Back
    /// Room station calls + bank, Goon signalling + relay, chess friends/media/stakes, Arcademy
    /// wallet sync, the breakout demo/full access flag, the asset media manifest, heartbeat
    /// watchdogs and fullscreen-set). A page asking for one of them gets no answer and runs on its
    /// own timeout fallback.</para>
    /// </summary>
    internal sealed class GameWindow : Window
    {
        /// <summary>A game this head can open: its launcher id, title key, page under Resources/web,
        /// and the WPF gate run before the window is built (null = free).</summary>
        internal sealed record Game(string Id, string TitleKey, string Page, Func<bool>? Gate = null, string? Query = null);

        /// <summary>WPF 7.1.5's game hosts, StartUrl for StartUrl. The Racing card is the mystery
        /// card until a track is owned and plays the Back Room counter, so "race" is not here yet.</summary>
        internal static readonly IReadOnlyDictionary<string, Game> Games = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase)
        {
            ["backroom"] = new("backroom", "launcher_game_backroom_title", "backroom/index.html", NeedsAccount),
            // WPF BreakoutHostService: the demo is free and needs no account; the full game is Lab (BreakoutAccess.FullAllowed).
            ["breakoutdemo"] = new("breakoutdemo", "launcher_game_breakoutdemo_title", "backroom/stations/breakout/play.html", null, "desktop=1"),
            ["breakout"] = new("breakout", "launcher_game_breakout_title", "backroom/stations/breakout/play.html",
                () => NeedsAccount() && TierGate.DemandLab(Loc.Get("launcher_game_breakout_title")), "desktop=1"),
            // WPF LaunchPlayChess: solo is free and needs no account; the board's lobby asks for a sign-in itself.
            ["piecebypiece"] = new("piecebypiece", "launcher_game_piecebypiece_title", "piecebypiece/index.html"),
            // WPF LaunchPlayGoon: an account first, then GoonHostService.Launch.
            ["goon"] = new("goon", "launcher_game_goon_title", "goon/index.html", NeedsAccount),
            // WPF MainWindow.Lab.cs:259 / App.xaml.cs:3422: TierGate.DemandLab("dtrh") before the descent.
            ["dtrh"] = new("dtrh", "launcher_game_dtrh_title", "dtrh/index.html",
                () => TierGate.DemandLab(Loc.Get("launcher_game_dtrh_title"), "dtrh")),
            // WPF ArcademyHostService.cs:175 DemandLab.
            ["arcademy"] = new("arcademy", "launcher_game_arcademy_title", "arcademy/index.html",
                () => TierGate.DemandLab(Loc.Get("launcher_game_arcademy_title"))),
        };

        /// <summary>WPF LauncherCatalogue.NeedsAccount: a signed-out click is refused (the launcher
        /// and the Play card both route a signed-out user to the sign-in dialog first).</summary>
        private static bool NeedsAccount()
        {
            if (CoreAccount.IsLoggedIn) return true;
            Log.Information("[Game] refused: no account");
            return false;
        }

        private static readonly List<GameWindow> Open = new();

        internal Game Spec { get; }
        internal WebHost Web { get; } = new();
        internal Uri? PageUrl { get; private set; }

        /// <summary>WPF LaunchCore: one live window per game, focused rather than relaunched (a
        /// second click mid-match must never restart it). Returns null when the gate refused.</summary>
        internal static GameWindow? Launch(string id)
        {
            if (!Games.TryGetValue(id, out var spec)) { Log.Warning("[Game] no game {Id}", id); return null; }
            GameWindow? live;
            lock (Open) live = Open.FirstOrDefault(w => string.Equals(w.Spec.Id, spec.Id, StringComparison.OrdinalIgnoreCase));
            if (live != null) { live.Activate(); return live; }
            try { if (spec.Gate != null && !spec.Gate()) return null; }
            catch (Exception ex) { Log.Warning(ex, "[Game] gate for {Id} threw; refused", id); return null; }
            var window = new GameWindow(spec);
            window.Load(WebAssetServer.Shared);
            window.Show();
            return window;
        }

        internal GameWindow(Game spec)
        {
            Spec = spec;
            Title = Loc.Get(spec.TitleKey);
            Width = 1280;
            Height = 800;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = Web;
            Web.WebMessage += OnPageMessage;
            Web.AllowNavigation = url => PageUrl != null && SameOrigin(url, PageUrl);
            Opened += (_, _) => { lock (Open) Open.Add(this); };
            Closed += (_, _) => { lock (Open) Open.Remove(this); };
        }

        internal void Load(WebAssetServer server)
        {
            var url = server.Url(Spec.Page);
            if (!string.IsNullOrEmpty(Spec.Query)) url += "&" + Spec.Query;
            PageUrl = new Uri(url);
            Web.Navigate(PageUrl);
        }

        internal static bool SameOrigin(Uri a, Uri b) =>
            a.IsAbsoluteUri && b.IsAbsoluteUri && a.Scheme == b.Scheme && a.Authority == b.Authority;

        private void OnPageMessage(string json)
        {
            if (PageUrl == null || Web.CurrentUrl == null || !SameOrigin(Web.CurrentUrl, PageUrl)) return;
            HandleMessage(json);
        }

        /// <summary>The shared shell frames (WPF ChaosWebViewHost.OnWebMessage + each host's OnPageReady).</summary>
        internal void HandleMessage(string json)
        {
            JObject o;
            try { o = JObject.Parse(json); }
            catch (Exception ex) { Log.Debug("[Game] {Id}: unreadable page message: {E}", Spec.Id, ex.Message); return; }
            switch ((string?)o["type"])
            {
                case "ready":
                    Post(InitMessage());
                    Post(new { type = "manifest", images = Array.Empty<string>(), videos = Array.Empty<string>(), gifs = Array.Empty<string>(), skipped = 0, truncated = false });
                    break;
                case "log":
                    Log.Debug("[Game] {Id} page: {Msg}", Spec.Id, (string?)o["msg"]);
                    break;
                case "boot-error":
                    Log.Warning("[Game] {Id}: page boot-error: {Msg}", Spec.Id, (string?)o["msg"]);
                    Close();
                    break;
                case "exit":
                    // The page winds down and answers exit-done; the WPF watchdog closes it anyway.
                    DispatcherTimer.RunOnce(Close, TimeSpan.FromMilliseconds(1200));
                    break;
                case "exit-done":
                case "close":
                    Close();
                    break;
            }
        }

        /// <summary>The init every one of these pages reads defensively: language + identity
        /// (GoonHostService.OnPageReady's shape, which the others read a subset of).</summary>
        internal object InitMessage() => new
        {
            type = "init",
            protocol = 1,
            lang = CoreSettings.Current.Language,
            solo = !CoreAccount.IsLoggedIn,
            identity = new
            {
                unifiedId = CoreAccount.UnifiedUserId ?? "",
                displayName = CoreAccount.DisplayName ?? "",
                appVersion = CoreReleaseContent.AppVersion,
            },
            modContent = (object?)null,
        };

        /// <summary>Host -&gt; page on either carrier: the string push (web-shim's __ccpRnPush) when
        /// the page installed it, else a message event on chrome.webview (WebView2's object carrier).
        /// ponytail: a page whose bridge only listens on chrome.webview gets the frame only if that
        /// object accepts dispatchEvent; the sure path is CoreWebView2.PostWebMessageAsJson through
        /// NativeWebView.TryGetPlatformHandle.</summary>
        internal void Post(object message)
        {
            var json = JsonConvert.SerializeObject(message);
            var script = "(function(m){try{if(typeof window.__ccpRnPush==='function'){window.__ccpRnPush(JSON.stringify(m));return;}"
                + "var w=window.chrome&&window.chrome.webview;if(w&&w.dispatchEvent){w.dispatchEvent(new MessageEvent('message',{data:m}));}}catch(e){}})("
                + json + ");";
            _ = Web.InvokeScriptAsync(script);
        }

        // ---- panic (PanicSurfaces "games", WPF GameSurfaces) ----------------------------------

        internal static bool IsAnyOpen() { lock (Open) return Open.Count > 0; }

        internal static void CloseAllForPanic()
        {
            GameWindow[] all;
            lock (Open) all = Open.ToArray();
            foreach (var w in all)
            {
                try { w.Close(); }
                catch (Exception ex) { Log.Warning(ex, "[Game] panic close of {Id} failed", w.Spec.Id); }
            }
        }
    }
}
