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
    /// Each game's own frames live in a partial beside this shell (GameWindow.BackRoom.cs: station
    /// relay + bank; GameWindow.Dtrh.cs: meta, run config, payout; GameWindow.Host.cs: manifest,
    /// heartbeat, fullscreen; GameWindow.Channel.cs: WebView2's own message channel;
    /// GameWindow.Pbp*.cs: chess friends, online play, pictures, stakes; GameWindow.Goon*.cs: the
    /// Goon host, whose page is the game and reaches the server through net-post, plus stakes, online
    /// pictures and the own-media transfer cache). Racing has a window of its own (RaceWindow*.cs).
    /// Frame ledgers with what is still owed per game: C:/wt-par/progress/*-frames.md.</para>
    /// </summary>
    internal sealed partial class GameWindow : Window
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
            // WPF OpenFypFeed: TierGate.DemandPremium("For You", "fyp") at the door (GameWindow.Fyp.cs).
            [FypId] = new(FypId, "tab_fyp", "fyp/index.html", FypGate),
            // WPF ShowTab("justdrop"): the server's door flag; the page is the live site (GameWindow.JustDrop.cs).
            [JustDropId] = new(JustDropId, "jd_door_title", "", JustDropDoorOpen),
            [LoomId] = new(LoomId, "billboard_loom_title", "dtrh/loom.html"),   // WPF LoomHostService: free, no account (GameWindow.Loom.cs)
            ["arcademy"] = new("arcademy", "launcher_game_arcademy_title", "arcademy/index.html",
                () => TierGate.DemandLab(Loc.Get("launcher_game_arcademy_title")) && ArcademyLaunchAllowed()),
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
        internal WebHost Web { get; }
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
            // WPF gives each game its own WebView2 profile folder; same names here (Platform/WebProfiles).
            Web = new WebHost { Profile = Platform.WebProfiles.ForGame(spec.Id) };
            Title = Loc.Get(spec.TitleKey);
            Width = 1280;
            Height = 800;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = Web;
            Web.WebMessage += OnPageMessage;
            Web.AllowNavigation = url => PageUrl != null && SameOrigin(url, PageUrl);
            Opened += (_, _) => { lock (Open) Open.Add(this); OnGameOpened(); };
            Closing += (_, _) => IsClosedOrClosing = true;
            Closed += (_, _) => { lock (Open) Open.Remove(this); OnGameClosed(); };
        }

        internal void Load(WebAssetServer server)
        {
            if (Spec.Id == JustDropId) { LoadJustDrop(); return; }   // the live site, not a local page
            // WPF's https://ccp.game/<page> on WebView2; the loopback twin (token first) elsewhere.
            PageUrl = new Uri(server.Url(Spec.Page, Spec.Query));
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
            try { o = JObject.Parse(Unwrap(json)); }
            catch (Exception ex) { Log.Debug("[Game] {Id}: unreadable page message: {E}", Spec.Id, ex.Message); return; }
            // Each game's own frames first (Views/Games/GameWindow.<Game>.cs); the shell frames below.
            if (HandleGameMessage(o)) return;
            switch ((string?)o["type"])
            {
                case "ready":
                    IsReady = true;
                    OnPageReady();
                    break;
                case "log":
                    Log.Debug("[Game] {Id} page: {Msg}", Spec.Id, (string?)o["msg"]);
                    break;
                case "boot-error":
                    Log.Warning("[Game] {Id}: page boot-error: {Msg}", Spec.Id, (string?)o["msg"]);
                    // WPF DtrhHostService.OnBootError: the descent falls back to the classic door.
                    if (Spec.Id == "dtrh") { OnDtrhBootError((string?)o["msg"]); break; }
                    Close();
                    break;
                case "exit":
                    // The page winds down and answers exit-done; the WPF watchdog closes it anyway.
                    DispatcherTimer.RunOnce(Close, TimeSpan.FromMilliseconds(1200));
                    break;
                case "exit-done":
                case "close":
                case "pbp:exit":
                    // WPF PieceByPieceHostService.cs:422: Esc / Leave on the chess menu has nothing to
                    // wind down, so the frame IS the close.
                    Close();
                    break;
                case "fire-payload" when Spec.Id == "dtrh":
                    // WPF DtrhHostService.cs:296: the run's video / whisper cross to the desktop.
                    NoteDtrhPayloadFired(Chaos.DtrhPayloadBridge.Fire(json));
                    break;
                case "heartbeat":
                case "pong":
                    NoteHeartbeat();
                    break;
                case "fullscreen-set":
                    // WPF DtrhHostService.ApplyHostFullscreen: C# owns the borderless toggle, echoed back.
                    SetHostFullscreen((bool?)o["on"] ?? false);
                    break;
                default:
                    // WPF BackRoomBridge default: a page waiting on a bridge leaves a line naming it (play#56).
                    Log.Debug("[Game] {Id}: unhandled page message '{Type}'", Spec.Id, (string?)o["type"]);
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
            // WPF BackRoomHostService.BuildInit (:575): the Breakout station and both standalone doors
            // read their access here. Without it access.js falls back to the demo on Windows and to the
            // full game where chrome.webview is missing (play#25).
            breakout = BreakoutEntitlementFor(Spec.Id),
            breakoutStandalone = IsBreakoutPage(Spec.Id),
        };

        private static bool IsBreakoutPage(string id)
            => id.Equals("breakout", StringComparison.OrdinalIgnoreCase) || id.Equals("breakoutdemo", StringComparison.OrdinalIgnoreCase);

        /// <summary>WPF BreakoutAccess.Project: full = story to level 8 + Endless (Lab), demo = 3 levels.
        /// An explicit demo door never expands, whatever the tier. Null for pages that are not Breakout.</summary>
        internal static object? BreakoutEntitlementFor(string id, bool? fullAllowed = null)
        {
            bool demoDoor = id.Equals("breakoutdemo", StringComparison.OrdinalIgnoreCase);
            if (!demoDoor && !IsBreakoutPage(id) && !id.Equals("backroom", StringComparison.OrdinalIgnoreCase)) return null;
            bool full = fullAllowed ?? TierGate.RequiresLab(Loc.Get("launcher_game_breakout_title")).Allowed;
            return full && !demoDoor
                ? new { storyLimit = 8, endless = true, demo = false }
                : new { storyLimit = 3, endless = false, demo = true };
        }

        // ---- panic (PanicSurfaces "games", WPF GameSurfaces) ----------------------------------

        internal static bool IsAnyOpen() { lock (Open) return Open.Count > 0; }

        internal static void CloseAllForPanic()
        {
            GameWindow[] all;
            lock (Open) all = Open.ToArray();
            foreach (var w in all)
            {
                if (w.Spec.Id == "arcademy" && w.ArcademyPanicPress(DateTime.UtcNow)) continue;   // only with the WPF ladder switched on; by default ONE press closes (owner, 10 Oct 2026)
                try { w.Close(); }
                catch (Exception ex) { Log.Warning(ex, "[Game] panic close of {Id} failed", w.Spec.Id); }
            }
        }
    }
}
