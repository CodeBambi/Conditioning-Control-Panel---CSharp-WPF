using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Stakes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The Goon Game host (WPF Services/GoonGame/GoonHostService.cs, the window half; the rules, the
    /// init payload and the whitelisted <c>/v2/goon/*</c> proxy are Core's <see cref="GoonHostService"/>).
    /// The page is the game: match, signalling, WebRTC, duels and scoring run in it and reach the server
    /// through <c>net-post</c>. Frame list with the WPF line for each: C:/wt-par/progress/g3-frames.md.
    ///
    /// <para>Entry points for other surfaces (WPF GoonHostService.Launch / LaunchToHost /
    /// OpenRoomForInviteAsync): <see cref="LaunchGoon"/>, <see cref="LaunchGoonToHost"/>,
    /// <see cref="OpenGoonRoomForInviteAsync"/>.</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        private bool _goonAttached;
        /// <summary>The watchdog clock; tests drive it.</summary>
        internal Func<DateTime> GoonClock { get; set; } = () => DateTime.UtcNow;
        private DispatcherTimer? _goonWatch;
        private DateTime _goonLastBeatUtc, _goonLastPaintMoveUtc, _goonLastTickUtc;
        private long? _goonLastPaint;
        private bool _goonPaintStallHandled, _goonRecoveredOnce, _goonExiting;
        private GoonOnlineMedia? _goonOnline, _goonPeer;
        private readonly System.Collections.Generic.Dictionary<string, GoonOnlineMedia> _goonNoise = new(StringComparer.Ordinal);

        // ---- entry points (SEAM for the Lobby and the friends invite picker) ---------------------

        /// <summary>WPF GoonHostService.Launch(duck, joinCode): open the game, or with a code go
        /// straight into joining that table. Returns the live window, null when the account gate
        /// refused. UI thread.</summary>
        internal static GameWindow? LaunchGoon(string? joinCode = null)
        {
            SeedGoon();
            GoonHostService.Launch(joinCode);
            return LiveGoon();
        }

        /// <summary>WPF GoonHostService.LaunchToHost: open on the host screen (the page asks the
        /// server for a room; only patrons get one).</summary>
        internal static GameWindow? LaunchGoonToHost()
        {
            SeedGoon();
            GoonHostService.LaunchToHost();
            return LiveGoon();
        }

        /// <summary>WPF GoonHostService.OpenRoomForInviteAsync: the room code for a friends invite,
        /// null on a busy page, a refusal or the timeout.</summary>
        internal static Task<(string? Code, bool Busy)> OpenGoonRoomForInviteAsync(TimeSpan timeout)
        {
            SeedGoon();
            return GoonHostService.OpenRoomForInviteAsync(timeout);
        }

        private static GameWindow? LiveGoon()
        {
            lock (Open) return Open.FirstOrDefault(w => w.Spec.Id == "goon");
        }

        private static void SeedGoon()
        {
            SeedPbpSeams();   // the stakes + online-picture seams are shared with chess; first seeding wins
            GoonHostService.OpenWindowProvider ??= () =>
            {
                var w = Launch("goon");   // the account gate runs here; a live window is focused
                return w != null;
            };
            // WPF App.Discord: linked = authenticated with a user id; the avatar hash + CDN url for the own plate.
            GoonAvatarCache.OwnDiscordProvider ??= () =>
            {
                var d = Platform.AccountSeed.Discord;
                return d != null && d.IsAuthenticated && !string.IsNullOrEmpty(d.UserId)
                    ? (true, d.Avatar, d.GetAvatarUrl(128))
                    : (false, null, null);
            };
        }

        // ---- attach / close --------------------------------------------------------------------

        /// <summary>First frame from the page: bind this window to the Core host. Done here rather
        /// than at window open so a frame for a page that is not up yet stays in Core's pending
        /// fields and rides the init instead of being posted into a loading document.</summary>
        private void EnsureGoon()
        {
            if (_goonAttached) return;
            _goonAttached = true;
            SeedGoon();
            GoonHostService.AttachWindow(Post);
            GoonHostService.RoomCodeChanged += OnGoonRoomCodeChanged;
            Closing += (_, _) => GoonFinalWord();
            Closed += (_, _) => CloseGoon();
            // WPF Launch: the window is BUILT in the remembered mode; a recovery comes back windowed.
            if (CoreSettings.Current.GoonFullscreen && !_goonRecoveredOnce)
            {
                try { WindowState = WindowState.FullScreen; } catch (Exception ex) { Log.Debug("[Goon] fullscreen: {E}", ex.Message); }
            }
            try { Platform.FriendsHead.Service?.EnterActivity(PresenceActivity.Goon); } catch { }
            StartGoonWatch();
            Log.Information("[Goon] host attached");
        }

        /// <summary>WPF DisposeAll's last word: a live match gets the chance to post its own abandon.
        /// Sent on the raw carrier because the shared Post is already shut by the time Closing runs.</summary>
        private void GoonFinalWord()
        {
            try
            {
                var json = JsonConvert.SerializeObject(new { type = "end-run", reason = "dispose" });
                try { Posted?.Invoke(json); } catch { }
                if (TryPostNative(json)) return;
                _ = Web.InvokeScriptAsync("(function(m){try{if(typeof window.__ccpRnPush==='function'){window.__ccpRnPush(JSON.stringify(m));return;}"
                    + "var w=window.chrome&&window.chrome.webview;if(w&&w.dispatchEvent){w.dispatchEvent(new MessageEvent('message',{data:m}));}}catch(e){}})(" + json + ");");
            }
            catch (Exception ex) { Log.Debug("[Goon] end-run: {E}", ex.Message); }
        }

        private void CloseGoon()
        {
            try { _goonWatch?.Stop(); } catch { }
            _goonWatch = null;
            GoonHostService.RoomCodeChanged -= OnGoonRoomCodeChanged;
            // Online pictures: stop fetching and hand back every temp file this window owned.
            try { _goonOnline?.Dispose(); } catch { }
            try { _goonPeer?.Dispose(); } catch { }
            _goonOnline = _goonPeer = null;
            ReleaseGoonNoise();
            GoonHostService.DetachWindow();
            try
            {
                Platform.FriendsHead.Service?.LeaveActivity(PresenceActivity.GoonHosting);
                Platform.FriendsHead.Service?.LeaveActivity(PresenceActivity.Goon);
            }
            catch { }
            Log.Information("[Goon] closed");
        }

        /// <summary>WPF SetRoomCode: an open room nobody sat down in reads "hosting a Goon Game".</summary>
        private void OnGoonRoomCodeChanged()
        {
            try
            {
                if (GoonHostService.RoomCode != null && !IsClosedOrClosing) Platform.FriendsHead.Service?.EnterActivity(PresenceActivity.GoonHosting);
                else Platform.FriendsHead.Service?.LeaveActivity(PresenceActivity.GoonHosting);
            }
            catch (Exception ex) { Log.Debug("[Goon] friends activity: {E}", ex.Message); }
        }

        // ---- frames ----------------------------------------------------------------------------

        /// <summary>WPF GoonHostService.OnPageMessage. True = claimed; log / boot-error / exit /
        /// exit-done fall through to the shell (close ladder).</summary>
        private bool HandleGoon(JObject o)
        {
            EnsureGoon();
            var type = (string?)o["type"];
            // PvP stakes (WPF OnPageMessage :706): stake-limits / stake-offer / stake-state / stake-settle go
            // to the shared Core bridge and nowhere else, before the host's own switch.
            if (StakeBridge.Handles(type))
            {
                _ = GoonStakes.Handle(o);
                return true;
            }
            switch (type)
            {
                case "ready":
                    IsReady = true;
                    OnGoonReady();
                    return true;
                case "heartbeat":
                case "pong":
                    NoteHeartbeat();
                    _goonLastBeatUtc = GoonClock();
                    NoteGoonPaint(o);
                    return true;
                case "exit":
                    _goonExiting = true;      // the watchdog stands down; the shell arms the 1200 ms close
                    return false;
                case "boot-error":
                    // WPF OnBootError also shows a message box naming the failure; here it is the log line
                    // and the shell's close (not ported: the dialog).
                    GoonHostService.BootFailedThisSession = true;
                    return false;
                case "fullscreen-set":
                    // WPF ApplyHostFullscreen: C# owns the toggle, echoes the REAL state, remembers it.
                    SetHostFullscreen((bool?)o["on"] ?? false);
                    if (CoreSettings.Current.GoonFullscreen != IsHostFullscreen)
                    {
                        CoreSettings.Current.GoonFullscreen = IsHostFullscreen;
                        CoreSettings.Save();
                    }
                    return true;
                case "net-post":
                    OnGoonNetPost(o);
                    return true;
                case "room-code":
                    GoonHostService.OnRoomCodeFrame(o);
                    return true;
                case "host-busy":
                    GoonHostService.OnHostBusyFrame();
                    return true;
                case "open-prime":
                    // The page's patron sheet "See the tiers": the app's own refusal and upgrade path.
                    TierGate.DemandPremium(GoonHostService.ProductName);
                    return true;
                case "match-result":
                    // WPF: a stub too (XP wiring comes with the client ledger); logged so a play-test sees the end.
                    Log.Information("[Goon] match-result (not scored, as WPF): {R}", o["result"]?.ToString(Formatting.None));
                    // The last-opponent record is written HERE, by the host, from the peer card it fetched.
                    GoonHostService.WriteLastOpponentRecord();
                    return true;
                case "toy-pattern":
                case "toy-stop":
                    // WPF: a logged no-op as well (caps.haptics is false; the consent sheet's toy cap has no mixer yet).
                    Log.Debug("[Goon] {Type} (haptics stub, no-op as WPF)", type);
                    return true;
                case "discord-prefs":
                {
                    var echo = GoonHostService.OnDiscordPrefs(o, out var sharedChanged, out var rpOff);
                    // not ported: the immediate profile sync push WPF kicks on a change (the flags ride the
                    // next scheduled sync) and the rich presence retract (no Discord RPC on this head).
                    if (rpOff) Log.Information("[Goon] rich presence switched off (no RPC client on this head)");
                    if (sharedChanged) KickGoonAvatarRefresh();
                    Post(echo);
                    return true;
                }
                case "last-opponent-clear":
                    GoonHostService.OnLastOpponentClear();
                    return true;
                case "media-flavour":
                    OnGoonMediaFlavour(o);
                    return true;
                case "media-more":
                    OnGoonMediaMore();
                    return true;
                case "peer-niches":
                    OnGoonPeerNiches(o);
                    return true;
                case "noise-want":
                    OnGoonNoiseWant(o);
                    return true;
                case "goon-recv-begin":
                case "goon-recv-chunk":
                case "goon-recv-commit":
                case "goon-recv-abort":
                case "goon-recv-drop":
                {
                    // not ported: TransferInboxStore (the received-artifact inbox). Answered in the
                    // page's own error vocabulary so receivedStore.js drops the artifact at once.
                    var id = (string?)o["id"] ?? (string?)o["sha256"] ?? "";
                    Log.Information("[Goon] {Type}: not ported (received inbox), answered io-failed", type);
                    Post(new { type = "goon-recv-result", id, ok = type == "goon-recv-abort", url = (string?)null, bytes = 0, error = type == "goon-recv-abort" ? null : "io-failed" });
                    return true;
                }
                case "cache-req":
                case "cache-put":
                case "encode-done":
                    // not ported: GoonCacheBridge + the transfer compression cache. caps.mediaTransfer is
                    // false on this head, so the page's lobby shows sending as off rather than half-working.
                    Log.Information("[Goon] {Type}: not ported (transfer cache)", type);
                    return true;
                case "peer-card-req":
                    // WPF OnPeerCardRequest: the opponent's name + avatar for the VS splash; a duplicate posts nothing.
                    _ = Task.Run(async () =>
                    {
                        var card = await GoonPeerCard(o).ConfigureAwait(false);
                        if (card != null) Post(card);
                    });
                    return true;
                case "discord-open-dm":
                {
                    // WPF OnDiscordOpenDm: "peer" or "last" only; the id never comes from the page and is never logged.
                    var url = GoonHostService.DiscordDmUrl((string?)o["which"]);
                    if (url == null) { Log.Debug("[Goon] discord-open-dm: nothing to open"); return true; }
                    GoonUnFullscreenForShellOpen();
                    _ = GoonOpenUrl(this, url);
                    Log.Information("[Goon] opened Discord DM ({W})", (string?)o["which"]);
                    return true;
                }
                case "discord-link-request":
                    // WPF OnDiscordLinkRequest: out of fullscreen, the panel to the front on its Discord page.
                    GoonUnFullscreenForShellOpen();
                    try
                    {
                        if (Windows.MainShellWindow.Current is { } shell)
                        {
                            if (shell.WindowState == WindowState.Minimized) shell.WindowState = WindowState.Normal;
                            shell.Show();
                            shell.Activate();
                            shell.ShowTab("discord");
                        }
                    }
                    catch (Exception ex) { Log.Warning("[Goon] discord-link-request: {E}", ex.Message); }
                    return true;
                case "rp-state":
                    // not ported: Discord rich presence has no client on this head (SEAM(discord rpc), as the
                    // Settings and Profile pages already note). The gate still runs so the log says what was asked.
                    Log.Information("[Goon] rp-state {S}: not ported (no Discord RPC client)", GoonHostService.RichPresenceState(o) ?? "(dropped)");
                    return true;
                case "share-card":
                    // not ported: GoonShareCard (clipboard / save dialog for the recap PNG).
                    Log.Information("[Goon] share-card: not ported");
                    Post(new { type = "share-card-result", id = (string?)o["id"], ok = false, error = "unavailable" });
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>WPF OnPageReady: init, manifest (the player's own library), the real window state.</summary>
        private void OnGoonReady()
        {
            var now = GoonClock();
            _goonLastBeatUtc = now;
            _goonLastPaint = null;           // a reloaded page counts its frames from zero
            _goonLastPaintMoveUtc = now;
            _goonPaintStallHandled = false;
            GoonHostService.OnPageReady();
            try { Web.Focus(); } catch { }

            var init = GoonHostService.BuildInit(IsHostFullscreen);
            // The transfer cache and the received inbox are not ported: say so in the caps, never
            // advertise a lane that would fire blanks (WPF TransferAllowed is the patron bar).
            ((JObject)init["caps"]!)["mediaTransfer"] = false;
            Post(init);

            var manifest = JObject.FromObject(GameMediaManifest.BuildLive().Frame());
            manifest["received"] = new JArray();   // ephemeral inbox: always empty at boot, as WPF
            Post(manifest);

            // Online pictures: only a pick made in THIS session fetches (a reload inside one window keeps it).
            StartGoonOnlineFromSettings();
            // ...and top the own avatar up off-thread; the page gets a discord echo when it lands.
            KickGoonAvatarRefresh();
            Post(new { type = "fullscreen", on = IsHostFullscreen });
            Log.Information("[Goon] sent init + manifest ({I} images, {V} videos)",
                (manifest["images"] as JArray)?.Count ?? 0, (manifest["videos"] as JArray)?.Count ?? 0);
        }

        /// <summary>WPF OnNetPost + ReplyNetPost: the whitelisted proxy, answered on the UI thread.</summary>
        private void OnGoonNetPost(JObject o)
        {
            var id = (string?)o["id"] ?? "";
            var path = (string?)o["path"];
            var body = GoonHostService.NetPostBody(o);
            _ = Task.Run(async () =>
            {
                var (status, text) = await GoonNetPost(path, body).ConfigureAwait(false);
                Post(new { type = "net-post-result", id, status, body = text });
            });
        }

        // ============================ stakes ============================

        private static StakeBridge? _goonStakes;

        /// <summary>One bridge for the life of the app (WPF _stakes :824): a settle watch it started keeps
        /// running, and books, after the duel window closes; posting to a closed window is a quiet no-op.
        /// Each player stakes against the house, the server settles only when both ledger claims agree,
        /// and Mercy / Esc / abandon settle as void.</summary>
        internal static StakeBridge GoonStakes
        {
            get => _goonStakes ??= StakeBridge.ForApp("goon", PostGoonStake);
            set => _goonStakes = value;
        }

        /// <summary>A <c>stake</c> frame back to the page, on the UI thread (WPF PostStake :827).</summary>
        private static void PostGoonStake(JObject o)
        {
            void Run()
            {
                try { LiveGoon()?.Post(o); }
                catch (Exception ex) { Log.Debug("[Goon] stake post failed: {E}", ex.Message); }
            }
            if (Dispatcher.UIThread.CheckAccess()) Run(); else Dispatcher.UIThread.Post(Run);
        }

        /// <summary>The peer-card fetch and the shell open; tests swap them for fakes.</summary>
        internal Func<JObject, Task<JObject?>> GoonPeerCard { get; set; } = o => GoonHostService.PeerCardAsync(o);
        internal Func<GameWindow, string, Task> GoonOpenUrl { get; set; } =
            async (w, url) => { try { await Platform.ExternalOpener.OpenAsync(w, url); } catch (Exception ex) { Log.Warning("[Goon] open failed: {E}", ex.Message); } };

        private void KickGoonAvatarRefresh() => _ = Task.Run(async () =>
        {
            var echo = await GoonHostService.RefreshOwnAvatarEchoAsync().ConfigureAwait(false);
            if (echo != null) Post(echo);
        });

        /// <summary>WPF UnFullscreenForShellOpen: first, synchronously, or the browser lands underneath.</summary>
        private void GoonUnFullscreenForShellOpen()
        {
            if (IsHostFullscreen) SetHostFullscreen(false);
        }

        /// <summary>The proxy call; tests swap it for a fake.</summary>
        internal Func<string?, string, Task<(int Status, string Body)>> GoonNetPost { get; set; } =
            (path, body) => GoonHostService.NetPostAsync(path, body);

        // ---- heartbeat + paint-stall watchdog (WPF StartHeartbeatWatch) ---------------------------

        private void NoteGoonPaint(JObject o)
        {
            try
            {
                var now = GoonClock();
                var vis = (string?)o["vis"];
                if (!string.IsNullOrEmpty(vis) && vis != "visible") { _goonLastPaintMoveUtc = now; return; }
                var paint = (long?)o["paint"];
                if (paint == null) return;   // no counter on this engine: the rule stays off
                if (_goonLastPaint == null || paint.Value != _goonLastPaint.Value)
                {
                    _goonLastPaint = paint.Value;
                    _goonLastPaintMoveUtc = now;
                }
            }
            catch { }
        }

        private void StartGoonWatch()
        {
            var now = DateTime.UtcNow;
            _goonLastBeatUtc = _goonLastPaintMoveUtc = _goonLastTickUtc = now;
            _goonWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(GoonHostService.WatchIntervalSeconds) };
            _goonWatch.Tick += (_, _) => CheckGoonWatch(GoonClock());
            _goonWatch.Start();
        }

        /// <summary>One watch tick; returns what it did (tests). Silent 20 s or a frozen picture with
        /// live beats = one reload (windowed), a second time the window closes; silent 8 s = a ping.</summary>
        internal string CheckGoonWatch(DateTime nowUtc)
        {
            double tickGap = (nowUtc - _goonLastTickUtc).TotalSeconds;
            _goonLastTickUtc = nowUtc;
            if (!IsReady || _goonExiting || IsClosedOrClosing) return "idle";
            double silent = (nowUtc - _goonLastBeatUtc).TotalSeconds;
            if (silent > GoonHostService.SilentRecoverSeconds) return RecoverGoon("heartbeat-silent", nowUtc);
            if (!_goonPaintStallHandled && _goonLastPaint != null)
            {
                double frozen = (nowUtc - _goonLastPaintMoveUtc).TotalSeconds;
                // WPF feeds VideoDiag.UiStallMs here; this head has no such probe, so the late-tick
                // half of the rule is the UI-stall evidence.
                var verdict = GoonHostService.PaintStallVerdict(frozen, tickGap, 0);
                if (verdict == GoonHostService.PaintStallCall.UiStalled) _goonLastPaintMoveUtc = nowUtc;
                else if (verdict == GoonHostService.PaintStallCall.Stalled)
                {
                    _goonPaintStallHandled = true;
                    return RecoverGoon("paint-stall", nowUtc);
                }
            }
            if (silent > GoonHostService.SilentPingSeconds) { Post(new { type = "ping" }); return "ping"; }
            return "ok";
        }

        private string RecoverGoon(string reason, DateTime nowUtc)
        {
            bool retry = !_goonRecoveredOnce;
            Log.Warning("[Goon] recovery ({Reason}) - {Action}", reason, retry ? "reloading once" : "giving up");
            if (!retry) { Close(); return "closed"; }
            _goonRecoveredOnce = true;
            IsReady = false;
            _goonLastBeatUtc = _goonLastPaintMoveUtc = nowUtc;
            _goonLastPaint = null;
            // Come back WINDOWED whatever the remembered mode: a titled window still has a close button.
            try { WindowState = WindowState.Normal; } catch { }
            if (PageUrl != null) Web.Navigate(PageUrl);
            return "recovered";
        }
    }
}
