// PORTED from WPF 7.1.5 ConditioningControlPanel/Services/GoonGame/GoonHostService.cs: the windowless
// half (rules, gates, init payload, the whitelisted net-post proxy, room code, launch entry points).
// The window glue (WebHost, heartbeat timer, fullscreen) lives in the Avalonia head,
// CCP.Avalonia/Views/Games/GameWindow.Goon.cs, which seeds OpenWindowProvider and attaches its Post.
// Deviations: App.* statics become CoreSettings / CoreAccount; public (the head cannot see Core internals).
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// Host service for the Goon Game browser client (Resources/web/goon). The page is the game: match,
    /// signalling (through <c>net-post</c>), WebRTC and scoring all run in it. The host proxies
    /// <c>/v2/goon/*</c> with the account's token, tells the page what this account may do, stores its
    /// options and answers its media frames.
    /// </summary>
    public static class GoonHostService
    {
        public const string ProductName = "Goon Game";
        public const int Protocol = 1;

        /// <summary>Server base for the host-proxied bridge.</summary>
        public const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

        /// <summary>The ONLY path prefix the page may proxy through the app. Without it the page would
        /// be a general-purpose HTTP client wearing the app's auth token.</summary>
        public const string AllowedPathPrefix = "/v2/goon/";

        /// <summary>Seconds of "beats arriving, frame counter frozen, page says it is visible" before
        /// the page is treated as visually frozen (ccp-bugs #1326: 20 s, was 10).</summary>
        public const double PaintStallSeconds = 20;
        public const double WatchIntervalSeconds = 5;
        public const double UiStallSlackSeconds = 3;
        /// <summary>WPF heartbeat watch: silent this long = recover; this long = ping first.</summary>
        public const double SilentRecoverSeconds = 20;
        public const double SilentPingSeconds = 8;

        public enum PaintStallCall { Healthy, UiStalled, Stalled }

        /// <summary>WPF PaintStallVerdict: a late watch tick or a stalled UI thread means OUR thread
        /// was the frozen one, so the paint clock restarts instead of closing a live match.</summary>
        public static PaintStallCall PaintStallVerdict(double frozenSeconds, double tickGapSeconds, long uiStallMs)
        {
            if (tickGapSeconds > WatchIntervalSeconds + UiStallSlackSeconds
                || uiStallMs > UiStallSlackSeconds * 1000)
                return PaintStallCall.UiStalled;
            return frozenSeconds > PaintStallSeconds ? PaintStallCall.Stalled : PaintStallCall.Healthy;
        }

        /// <summary>Joining a Goon match is a Scrolller-based game: the opponent's niches are fetched
        /// unless THIS player switched online pictures off.</summary>
        public static bool PeerFetchAllowed(bool? goonMediaOnline) => goonMediaOnline != false;

        // ---- access (re-checked here, never trusted from the page) ---------------------------

        /// <summary>May the page send its OWN media to the opponent? Any patron (WPF TransferAllowed).</summary>
        public static bool TransferAllowed() { try { return CoreAccount.HasPremiumAccess; } catch { return false; } }

        /// <summary>May the page MINT a room? Any patron; the server enforces it at /v2/goon/invite.</summary>
        public static bool HostingAllowed() { try { return CoreAccount.HasPremiumAccess; } catch { return false; } }

        /// <summary>May the page JOIN a room? Every signed-in account. Practice never asks.</summary>
        public static bool JoiningAllowed() => !string.IsNullOrEmpty(CoreAccount.UnifiedUserId);

        public static bool CanHost => HostingAllowed();

        /// <summary>The page reported boot-error this app session (a genuine load or init failure).</summary>
        public static bool BootFailedThisSession { get; set; }

        // ---- the live window (seeded by the head) --------------------------------------------

        /// <summary>Opens (or focuses) the Goon window; true when one is live afterwards. Seeded by
        /// the head (GameWindow.Goon.cs). Unseeded = no head: every launch is a logged no-op.</summary>
        public static volatile Func<bool>? OpenWindowProvider;

        private static Action<object>? _post;
        private static string? _pendingJoinCode;
        private static bool _pendingAutoHost;
        private static string? _roomCode;
        private static bool _sessionOptIn;

        public static bool IsActive => _post != null;

        /// <summary>The join code of the room this window hosts while nobody has sat down, else null.</summary>
        public static string? RoomCode => _roomCode;

        /// <summary>Raised when <see cref="RoomCode"/> changes, and again when the page re-tells it.</summary>
        public static event Action? RoomCodeChanged;

        /// <summary>The page refused a <c>host-now</c> ask: a match or practice is under way.</summary>
        public static event Action? HostBusy;

        /// <summary>This session's flavour pick (the Scrolller opt-in for this window only).</summary>
        public static bool SessionOptIn => _sessionOptIn;

        /// <summary>Head: the window is up and this is how a frame reaches its page.</summary>
        public static void AttachWindow(Action<object> post) => _post = post;

        /// <summary>Head: the window closed (WPF DisposeAll's state half).</summary>
        public static void DetachWindow()
        {
            _post = null;
            _pendingAutoHost = false;
            _sessionOptIn = false;
            SetRoomCode(null, again: false);
            try { ResetPeerCardState(); } catch { }
        }

        private static void Post(object frame)
        {
            try { _post?.Invoke(frame); }
            catch (Exception ex) { Log.Debug("GoonHostService: post: {E}", ex.Message); }
        }

        /// <summary>Launch the Goon Game window (idempotent; a running one is focused).</summary>
        public static void Launch() => Launch(null);

        /// <summary>Launch straight into joining <paramref name="joinCode"/> (open tables). A fresh
        /// page reads it as the init field <c>joinCode</c>; a page already up gets a <c>join-code</c>
        /// frame. A code that fails <see cref="GoonJoinCode.Normalize"/> is dropped.</summary>
        public static void Launch(string? joinCode)
        {
            var code = GoonJoinCode.Normalize(joinCode);
            if (IsActive)
            {
                if (code != null) Post(new { type = "join-code", code });
                Open();
                return;
            }
            _pendingJoinCode = code;
            if (!Open()) _pendingJoinCode = null;
        }

        /// <summary>Open the game on the host screen (friends invite). A window already up gets a
        /// <c>host-now</c> frame: it opens a room, re-tells one it has, or answers <c>host-busy</c>.</summary>
        public static void LaunchToHost()
        {
            if (IsActive)
            {
                Post(new { type = "host-now" });
                Open();
                return;
            }
            _pendingAutoHost = true;
            if (!Open()) _pendingAutoHost = false;   // launch failed: do not carry it to a later open
        }

        /// <summary>Opens (or reuses) a room and returns its code once the page reports it; null on a
        /// busy page, a refusal or <paramref name="timeout"/>. Call on the UI thread.</summary>
        public static async Task<(string? Code, bool Busy)> OpenRoomForInviteAsync(TimeSpan timeout)
        {
            var existing = _roomCode;
            if (!string.IsNullOrEmpty(existing)) return (existing, false);

            var tcs = new TaskCompletionSource<(string?, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnCode() { var c = _roomCode; if (!string.IsNullOrEmpty(c)) tcs.TrySetResult((c, false)); }
            void OnBusy() => tcs.TrySetResult((null, true));
            RoomCodeChanged += OnCode;
            HostBusy += OnBusy;
            try
            {
                LaunchToHost();
                var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout)).ConfigureAwait(true);
                return done == tcs.Task ? tcs.Task.Result : (null, false);
            }
            finally
            {
                RoomCodeChanged -= OnCode;
                HostBusy -= OnBusy;
            }
        }

        private static bool Open()
        {
            var open = OpenWindowProvider;
            if (open == null) { Log.Information("GoonHostService: no head attached, launch ignored"); return false; }
            try { return open(); }
            catch (Exception ex) { Log.Warning("GoonHostService: launch failed: {E}", ex.Message); return false; }
        }

        /// <summary>The page's <c>room-code</c> frame ('' = none).</summary>
        public static void OnRoomCodeFrame(JObject o)
        {
            var raw = (string?)o["code"];
            var code = string.IsNullOrEmpty(raw) ? null : GoonJoinCode.Normalize(raw);
            SetRoomCode(code, again: code != null);
        }

        /// <summary>The page's <c>host-busy</c> frame.</summary>
        public static void OnHostBusyFrame()
        {
            try { HostBusy?.Invoke(); } catch { }
        }

        /// <summary>A fresh document hosts nothing until it says so (WPF OnPageReady).</summary>
        public static void OnPageReady() => SetRoomCode(null, again: false);

        private static void SetRoomCode(string? code, bool again)
        {
            if (code == _roomCode && !again) return;
            _roomCode = code;
            try { RoomCodeChanged?.Invoke(); }
            catch (Exception ex) { Log.Debug("GoonHostService: RoomCodeChanged: {E}", ex.Message); }
        }

        // ---- init ------------------------------------------------------------------------------

        private static string SafeAuthToken()
        {
            try { var s = CoreSettings.Current; return s.OfflineMode ? string.Empty : s.AuthToken ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeDisplayName()
        {
            try
            {
                var name = CoreAccount.DisplayName;
                if (string.IsNullOrWhiteSpace(name)) name = CoreSettings.Current.UserDisplayName;
                return string.IsNullOrWhiteSpace(name) ? "Player" : name!;
            }
            catch { return "Player"; }
        }

        /// <summary>WPF OnPageReady's init, field for field. Spends the pending join code and the
        /// pending auto-host, so a reload does not rejoin a finished room.</summary>
        public static JObject BuildInit(bool fullscreen)
        {
            var consent = new ConsentSheetMsg();   // the engine's own defaults, never a fork
            var joinCode = _pendingJoinCode ?? "";
            _pendingJoinCode = null;
            var autoHost = _pendingAutoHost;
            _pendingAutoHost = false;
            return JObject.FromObject(new
            {
                type = "init",
                protocol = Protocol,
                lang = CoreSettings.Current.Language ?? "en",
                identity = new
                {
                    unifiedId = CoreAccount.UnifiedUserId ?? "",
                    displayName = SafeDisplayName(),
                    appVersion = CoreReleaseContent.AppVersion,
                },
                net = new
                {
                    serverBase = ProxyBaseUrl,
                    // The CCP auth token (X-Auth-Token), not the Patreon bearer. Every call still
                    // comes back through net-post and the host attaches the header itself.
                    authToken = SafeAuthToken(),
                    viaHost = true,
                },
                caps = new
                {
                    haptics = false,
                    brainDrain = true,        // the in-page veil, not the desktop overlay
                    spiral = true,
                    camera = false,
                    video = true,
                    mediaTransfer = TransferAllowed(),
                    canHost = HostingAllowed(),
                    canJoin = JoiningAllowed(),
                },
                joinCode,
                autoHost,
                consent = new
                {
                    liveDurationSec = consent.LiveDurationSec,
                    toyCap = consent.ToyCap,
                    payloadMinGapMs = consent.PayloadMinGapMs,
                },
                fullscreen,
                discord = BuildDiscordBlock(includeLastOpponent: true),
                media = BuildMediaBlock(),
            });
        }

        /// <summary>The <c>discord</c> block for init and the echo (WPF BuildDiscordBlock). Reads the
        /// avatar from the DISK CACHE only: init is posted synchronously and an avatar may never sit on
        /// a boot path. A stale one is topped up by <see cref="RefreshOwnAvatarEchoAsync"/>.</summary>
        public static JObject BuildDiscordBlock(bool includeLastOpponent)
        {
            var block = new JObject
            {
                ["avatarState"] = "unlinked",
                ["avatarDataUri"] = JValue.CreateNull(),
                ["dmShared"] = false,
                ["richPresence"] = false,
                ["seenSharePrompt"] = false,
            };
            try
            {
                var s = CoreSettings.Current;
                var d = GoonAvatarCache.OwnDiscord();
                bool shareAvatar = s.GoonShareAvatar;
                // Three states: "off" (linked, chose not to share) and "unlinked" read differently in the lobby.
                block["avatarState"] = !d.Linked ? "unlinked" : (shareAvatar ? "shared" : "off");
                block["dmShared"] = s.GoonShareDiscordDm;
                block["richPresence"] = s.GoonRichPresence;
                block["seenSharePrompt"] = s.GoonSeenSharePrompt;
                if (d.Linked && shareAvatar)
                {
                    var uri = GoonAvatarCache.ReadOwnDataUriIfFresh(d.AvatarHash);
                    if (uri != null) block["avatarDataUri"] = uri;
                }
            }
            catch (Exception ex) { Log.Debug("GoonHostService.BuildDiscordBlock: {E}", ex.Message); }
            if (includeLastOpponent) block["lastOpponent"] = BuildLastOpponentBlock();
            return block;
        }

        /// <summary>The <c>discord</c> echo frame (the block minus lastOpponent).</summary>
        public static JObject DiscordEcho()
        {
            var block = BuildDiscordBlock(includeLastOpponent: false);
            block["type"] = "discord";
            return block;
        }

        /// <summary>WPF KickOwnAvatarRefresh: tops the own avatar up off-thread and returns the echo to
        /// post when it landed, else null. A no-op unless the user is linked AND sharing, so a player
        /// who shares nothing never touches the Discord CDN.</summary>
        public static async Task<JObject?> RefreshOwnAvatarEchoAsync()
        {
            try
            {
                if (!CoreSettings.Current.GoonShareAvatar) return null;
                if (!GoonAvatarCache.OwnDiscord().Linked) return null;
                var uri = await GoonAvatarCache.RefreshOwnAvatarAsync().ConfigureAwait(false);
                return uri == null ? null : DiscordEcho();
            }
            catch (Exception ex) { Log.Debug("GoonHostService.RefreshOwnAvatarEchoAsync: {E}", ex.Message); return null; }
        }

        private static JToken BuildLastOpponentBlock()
        {
            try
            {
                var raw = CoreSettings.Current.GoonLastOpponentJson;
                if (string.IsNullOrWhiteSpace(raw)) return JValue.CreateNull();
                var rec = JObject.Parse(raw);
                var name = (string?)rec["name"];
                if (string.IsNullOrWhiteSpace(name)) return JValue.CreateNull();

                string? uri = null;
                // Only the ONE bare filename this cache ever writes is accepted; a record naming
                // anything else is treated as having no picture rather than being followed.
                if ((string?)rec["avatarFile"] == GoonAvatarCache.LastOpponentFile)
                    uri = GoonAvatarCache.ReadDataUri(GoonAvatarCache.LastOpponentFile);

                return new JObject
                {
                    ["name"] = name,
                    ["avatarDataUri"] = uri == null ? JValue.CreateNull() : (JToken)uri,
                    ["dm"] = !string.IsNullOrEmpty((string?)rec["dmId"]),
                    ["ts"] = (long?)rec["ts"] ?? 0L,
                };
            }
            catch (Exception ex)
            {
                // A corrupt record is a missing record: never a boot failure.
                Log.Debug("GoonHostService.BuildLastOpponentBlock: {E}", ex.Message);
                return JValue.CreateNull();
            }
        }

        /// <summary>WPF OnDiscordPrefs, the settings half: stores the flags, returns the echo frame.
        /// <paramref name="sharedChanged"/> = a sharing flag moved (WPF pushes a profile sync then).</summary>
        public static JObject OnDiscordPrefs(JObject o, out bool sharedChanged, out bool richPresenceTurnedOff)
        {
            sharedChanged = false;
            richPresenceTurnedOff = false;
            try
            {
                var s = CoreSettings.Current;
                var a = (bool?)o["shareAvatar"];
                if (a.HasValue && s.GoonShareAvatar != a.Value) { s.GoonShareAvatar = a.Value; sharedChanged = true; }
                var dm = (bool?)o["shareDm"];
                if (dm.HasValue && s.GoonShareDiscordDm != dm.Value) { s.GoonShareDiscordDm = dm.Value; sharedChanged = true; }
                var rp = (bool?)o["richPresence"];
                if (rp.HasValue && s.GoonRichPresence != rp.Value) { s.GoonRichPresence = rp.Value; richPresenceTurnedOff = !rp.Value; }
                var seen = (bool?)o["seenSharePrompt"];
                if (seen.HasValue && s.GoonSeenSharePrompt != seen.Value) s.GoonSeenSharePrompt = seen.Value;
                CoreSettings.Save();
            }
            catch (Exception ex) { Log.Warning("GoonHostService.discord-prefs: {E}", ex.Message); }
            return DiscordEcho();
        }

        // ---- the peer card (contract 4: name + avatar of the opponent, a BOOLEAN for the DM) ------

        /// <summary>The peer-card endpoint. A compile-time constant: the page never names this URL.</summary>
        public const string PeerCardPath = "/v2/goon/peercard";

        // PRIVACY BOUNDARY: the peer's Discord snowflake exists only in this field, in the
        // last-opponent record and in the shell-opened URL. Never posted to the page, never logged.
        private static string? _peerDmId;
        private static string? _peerName;
        private static bool _peerAvatarCached;
        private static bool _peerCardFetched;
        private static int _peerCardInFlight;

        /// <summary>WPF OnPeerCardRequest: one fetch at a time (null = a duplicate, post nothing),
        /// two attempts of 3 s, and the <c>peer-card</c> frame to post. A failure is reason "error".</summary>
        public static async Task<JObject?> PeerCardAsync(JObject o, HttpMessageInvoker? http = null)
        {
            if (Interlocked.CompareExchange(ref _peerCardInFlight, 1, 0) != 0)
            {
                Log.Debug("GoonHostService: peer-card-req ignored (already in flight)");
                return null;
            }
            try
            {
                // A NEW request means a NEW peer: drop the previous one's card first, or a failed fetch
                // for match 2 would let match 1's opponent be written as match 2's last opponent.
                ResetPeerCardState();
                var body = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    unified_id = CoreAccount.UnifiedUserId ?? "",
                    code = SafeShort((string?)o["code"], 32),
                    token = SafeShort((string?)o["token"], 128),
                    role = SafeShort((string?)o["role"], 16),
                });
                var json = await PostPeerCardAsync(body, http).ConfigureAwait(false);
                if (json == null) return PeerCardFrame(null, null, "error", false, null);

                var name = (string?)json["name"];
                var reason = (string?)json["avatar_reason"] ?? "error";
                var dmId = (string?)json["dm_id"];
                _peerName = string.IsNullOrWhiteSpace(name) ? null : name!.Trim();
                // Re-validate what the server sent before it can ever reach a shell command.
                _peerDmId = IsSnowflake(dmId) ? dmId : null;
                _peerAvatarCached = false;

                string? uri = null;
                var bytes = GoonAvatarCache.DecodeDataUri((string?)json["avatar"]);
                if (bytes != null && GoonAvatarCache.Write(GoonAvatarCache.PeerFile, bytes))
                {
                    _peerAvatarCached = true;
                    uri = GoonAvatarCache.ReadDataUri(GoonAvatarCache.PeerFile);
                }
                _peerCardFetched = true;
                Log.Information("GoonHostService: peer card fetched (avatar={A}, reason={R}, dm={D})",
                    uri != null, reason, _peerDmId != null);
                return PeerCardFrame(_peerName, uri, reason, _peerDmId != null, (string?)json["ver"]);
            }
            catch (Exception ex)
            {
                Log.Warning("GoonHostService.peer-card-req: {E}", ex.Message);
                return PeerCardFrame(null, null, "error", false, null);
            }
            finally { Interlocked.Exchange(ref _peerCardInFlight, 0); }
        }

        private static JObject PeerCardFrame(string? name, string? avatarDataUri, string reason, bool dm, string? ver) => new()
        {
            ["type"] = "peer-card",
            ["name"] = name ?? "",
            ["avatarDataUri"] = avatarDataUri == null ? JValue.CreateNull() : (JToken)avatarDataUri,
            ["reason"] = reason,
            ["dm"] = dm,
            ["ver"] = ver == null ? JValue.CreateNull() : (JToken)ver,
        };

        private static async Task<JObject?> PostPeerCardAsync(string body, HttpMessageInvoker? http)
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    // The shared client's 40 s timeout is for the relay long-poll; an avatar gets 3 s.
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    using var request = new HttpRequestMessage(HttpMethod.Post, ProxyBaseUrl + PeerCardPath)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json"),
                    };
                    var auth = SafeAuthToken();
                    if (!string.IsNullOrEmpty(auth)) request.Headers.Add("X-Auth-Token", auth);
                    request.Headers.Add("X-Client-Version", CoreReleaseContent.AppVersion);
                    using var response = await (http ?? Http).SendAsync(request, cts.Token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        // 403/429 are ANSWERS, not transport faults: retrying spends the 6/min gate for nothing.
                        Log.Debug("GoonHostService: peercard HTTP {S}", (int)response.StatusCode);
                        return null;
                    }
                    return JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
                catch (Exception ex) { Log.Debug("GoonHostService: peercard attempt {N}/2 failed: {E}", attempt, ex.Message); }
            }
            return null;
        }

        /// <summary>WPF WriteLastOpponentRecord (on match-result): written by the HOST from the peer
        /// card it already fetched; no page-supplied data.</summary>
        public static void WriteLastOpponentRecord()
        {
            try
            {
                if (!_peerCardFetched || string.IsNullOrWhiteSpace(_peerName)) return;
                var s = CoreSettings.Current;
                string? file = null;
                if (_peerAvatarCached) file = GoonAvatarCache.PromotePeerToLastOpponent();
                // A peer who shared no picture must not inherit the PREVIOUS opponent's one.
                if (file == null) GoonAvatarCache.Delete(GoonAvatarCache.LastOpponentFile);
                var rec = new JObject
                {
                    ["name"] = _peerName,
                    ["dmId"] = _peerDmId == null ? JValue.CreateNull() : (JToken)_peerDmId,
                    ["avatarFile"] = file == null ? JValue.CreateNull() : (JToken)file,   // bare name, never a path
                    ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };
                s.GoonLastOpponentJson = rec.ToString(Newtonsoft.Json.Formatting.None);
                CoreSettings.Save();
                Log.Information("GoonHostService: last-opponent record written (avatar={A}, dm={D})", file != null, _peerDmId != null);
            }
            catch (Exception ex) { Log.Warning("GoonHostService.WriteLastOpponentRecord: {E}", ex.Message); }
        }

        /// <summary>WPF OnLastOpponentClear: the record and its picture go.</summary>
        public static void OnLastOpponentClear()
        {
            try
            {
                var s = CoreSettings.Current;
                if (!string.IsNullOrEmpty(s.GoonLastOpponentJson))
                {
                    s.GoonLastOpponentJson = "";
                    CoreSettings.Save();
                }
                GoonAvatarCache.Delete(GoonAvatarCache.LastOpponentFile);
                Log.Information("GoonHostService: last-opponent record cleared");
            }
            catch (Exception ex) { Log.Warning("GoonHostService.last-opponent-clear: {E}", ex.Message); }
        }

        /// <summary>WPF OnDiscordOpenDm, the lookup half: the profile URL for <c>which</c> = "peer" or
        /// "last" (an enum: a page-supplied id is not a case), or null when there is nothing to open.
        /// The id is never logged: the URL identifies a real person.</summary>
        public static string? DiscordDmUrl(string? which)
        {
            string? id = which switch
            {
                "peer" => _peerDmId,
                "last" => ReadLastOpponentDmId(),
                _ => null,
            };
            return IsSnowflake(id) ? "https://discord.com/users/" + id : null;
        }

        private static string? ReadLastOpponentDmId()
        {
            try
            {
                var raw = CoreSettings.Current.GoonLastOpponentJson;
                if (string.IsNullOrWhiteSpace(raw)) return null;
                var id = (string?)JObject.Parse(raw)["dmId"];
                return IsSnowflake(id) ? id : null;
            }
            catch { return null; }
        }

        /// <summary>WPF OnRichPresenceState, the gate: a state off the enum is refused; with the flag
        /// off the frame is dropped. Returns the state to publish, or null.</summary>
        public static string? RichPresenceState(JObject o)
        {
            var s = (string?)o["s"] ?? "";
            if (s != "lobby" && s != "live" && s != "recap" && s != "off") return null;
            return CoreSettings.Current.GoonRichPresence ? s : null;
        }

        internal static bool IsSnowflake(string? id)
        {
            if (string.IsNullOrEmpty(id) || id!.Length > 20) return false;
            foreach (var c in id) if (c < '0' || c > '9') return false;
            return true;
        }

        private static string SafeShort(string? v, int max)
        {
            if (string.IsNullOrWhiteSpace(v)) return "";
            var t = v!.Trim();
            return t.Length > max ? t.Substring(0, max) : t;
        }

        /// <summary>WPF ResetPeerCardState: a closed window or a new request forgets the peer.</summary>
        public static void ResetPeerCardState()
        {
            _peerDmId = null;
            _peerName = null;
            _peerAvatarCached = false;
            _peerCardFetched = false;
            try { GoonAvatarCache.Delete(GoonAvatarCache.PeerFile); } catch { }
        }

        // ---- online pictures: the stored pick ------------------------------------------------

        /// <summary>WPF BuildMediaBlock: the pick lasts one session; the stored one is only a preselection.</summary>
        public static object BuildMediaBlock()
        {
            var s = CoreSettings.Current;
            var stored = GoonOnlineMediaRules.CleanFlavour(s.GoonMediaFlavour);
            return new
            {
                flavour = _sessionOptIn ? stored : "",
                last = stored,
                custom = GoonOnlineMediaRules.ParseCustom(s.GoonMediaCustom),
                online = s.GoonMediaOnline,
            };
        }

        /// <summary>WPF OnMediaFlavour, the settings half: every niche re-validated and capped, the
        /// pick stored, the session opt-in computed. Returns the cleaned niches.</summary>
        public static IReadOnlyList<string> OnMediaFlavour(JObject o)
        {
            var s = CoreSettings.Current;
            var flavour = GoonOnlineMediaRules.CleanFlavour((string?)o["flavour"]);
            var subs = GoonOnlineMediaRules.CleanSubs(
                (o["subs"] as JArray)?.Select(t => t.Type == JTokenType.String ? (string?)t : null));
            s.GoonMediaFlavour = flavour;
            if (o["custom"] is JObject) s.GoonMediaCustom = GoonOnlineMediaRules.CleanCustom(o["custom"]);
            s.GoonMediaSubs = GoonOnlineMediaRules.JoinSubs(subs);
            if (o["online"]?.Type == JTokenType.Boolean) s.GoonMediaOnline = (bool)o["online"]!;
            _sessionOptIn = GoonOnlineMediaRules.IsSessionOptIn(s.GoonMediaOnline, flavour);
            try { CoreSettings.Save(); } catch (Exception ex) { Log.Debug("GoonHostService: media save: {E}", ex.Message); }
            Log.Information("GoonHostService: media-flavour {F} ({N} niches, online {O})",
                flavour == "" ? "(none)" : flavour, subs.Count, s.GoonMediaOnline);
            return subs;
        }

        // ---- host-proxied HTTP bridge --------------------------------------------------------

        private static readonly HttpClient Http = BuildHttpClient();

        private static HttpClient BuildHttpClient()
        {
            // 40 s: the relay long-polls about 20 s.
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
            try { c.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{CoreReleaseContent.AppVersion}"); } catch { }
            return c;
        }

        /// <summary>Only the duel's own endpoints are ever forwarded. A dot segment is refused too:
        /// the prefix test alone would let "/v2/goon/../x" through to another route.</summary>
        public static bool IsAllowedPath(string? path) =>
            path != null && path.StartsWith(AllowedPathPrefix, StringComparison.Ordinal)
            && !path.Contains("..", StringComparison.Ordinal) && !path.Contains('\\');

        /// <summary>The body of a net-post frame as the string to send (WPF OnNetPost).</summary>
        public static string NetPostBody(JObject o) =>
            o["body"]?.Type == JTokenType.String
                ? (string?)o["body"] ?? ""
                : o["body"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "";

        /// <summary>
        /// WPF OnNetPost: POST <paramref name="path"/> to the proxy with the account's X-Auth-Token and
        /// X-Client-Version. A path off the whitelist fails closed as status 0 / "forbidden_path", the
        /// same shape a transport failure has (status 0, empty body), so the page needs no special case.
        /// </summary>
        public static async Task<(int Status, string Body)> NetPostAsync(string? path, string body, HttpMessageInvoker? http = null)
        {
            if (!IsAllowedPath(path))
            {
                Log.Warning("GoonHostService: net-post REJECTED for path '{Path}'", path);
                return (0, "forbidden_path");
            }
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, ProxyBaseUrl + path)
                {
                    Content = new StringContent(body ?? "", Encoding.UTF8, "application/json"),
                };
                var token = SafeAuthToken();
                if (!string.IsNullOrEmpty(token)) request.Headers.Add("X-Auth-Token", token);
                request.Headers.Add("X-Client-Version", CoreReleaseContent.AppVersion);
                using var response = await (http ?? Http).SendAsync(request, CancellationToken.None).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                global::ConditioningControlPanel.Services.MergedAccountRecovery.TryHandle((int)response.StatusCode, text);   // contract D
                return ((int)response.StatusCode, text);
            }
            catch (Exception ex)
            {
                Log.Warning("GoonHostService: net-post {Path} failed: {E}", path, ex.Message);
                return (0, "");
            }
        }
    }
}
