using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
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

        private static void SeedGoon() => GoonHostService.OpenWindowProvider ??= () =>
        {
            var w = Launch("goon");   // the account gate runs here; a live window is focused
            return w != null;
        };

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
            switch (type)
            {
                case "ready":
                    IsReady = true;
                    OnGoonReady();
                    return true;
                case "heartbeat":
                case "pong":
                    NoteHeartbeat();
                    _goonLastBeatUtc = DateTime.UtcNow;
                    NoteGoonPaint(o);
                    return true;
                case "exit":
                    _goonExiting = true;      // the watchdog stands down; the shell arms the 1200 ms close
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
                    // not ported: WriteLastOpponentRecord (needs the peer card + avatar cache).
                    return true;
                case "toy-pattern":
                case "toy-stop":
                    // WPF: a logged no-op as well (caps.haptics is false; the consent sheet's toy cap has no mixer yet).
                    Log.Debug("[Goon] {Type} (haptics stub, no-op as WPF)", type);
                    return true;
                case "discord-prefs":
                {
                    var echo = GoonHostService.OnDiscordPrefs(o, out var sharedChanged, out _);
                    // not ported: the profile sync push and the own-avatar refresh WPF kicks on a change.
                    if (sharedChanged) Log.Information("[Goon] discord sharing flags changed (stored; sync push not ported)");
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
                case "stake-limits":
                case "stake-offer":
                case "stake-state":
                case "stake-settle":
                    // SEAM(stakes): Services/Stakes/StakeBridge (shared with chess) is not in Core yet.
                    // Nothing is offered, booked or settled; the page's stake row stays off.
                    Log.Information("[Goon] {Type}: not ported (StakeBridge)", type);
                    return true;
                case "peer-card-req":
                case "discord-open-dm":
                case "discord-link-request":
                case "rp-state":
                case "share-card":
                    // not ported: Discord peer card / DM / link, rich presence, the recap share card.
                    Log.Information("[Goon] {Type}: not ported", type);
                    if (type == "share-card") Post(new { type = "share-card-result", id = (string?)o["id"], ok = false, error = "unavailable" });
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>WPF OnPageReady: init, manifest (the player's own library), the real window state.</summary>
        private void OnGoonReady()
        {
            var now = DateTime.UtcNow;
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

        /// <summary>The proxy call; tests swap it for a fake.</summary>
        internal Func<string?, string, Task<(int Status, string Body)>> GoonNetPost { get; set; } =
            (path, body) => GoonHostService.NetPostAsync(path, body);

        // ---- heartbeat + paint-stall watchdog (WPF StartHeartbeatWatch) ---------------------------

        private void NoteGoonPaint(JObject o)
        {
            try
            {
                var now = DateTime.UtcNow;
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
            _goonWatch.Tick += (_, _) => CheckGoonWatch(DateTime.UtcNow);
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
