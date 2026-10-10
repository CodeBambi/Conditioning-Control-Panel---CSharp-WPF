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

        /// <summary>Is a Discord account linked (avatarState "off"/"shared" instead of "unlinked").
        /// Seeded by the head when it has one; unseeded reads unlinked.</summary>
        public static volatile Func<bool>? DiscordLinkedProvider;

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

        /// <summary>The <c>discord</c> block for init and the echo. The avatar cache
        /// (GoonAvatarCache) is not ported: avatarDataUri is always null on this head.</summary>
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
                bool linked = false;
                try { linked = DiscordLinkedProvider?.Invoke() == true; } catch { }
                block["avatarState"] = !linked ? "unlinked" : (s.GoonShareAvatar ? "shared" : "off");
                block["dmShared"] = s.GoonShareDiscordDm;
                block["richPresence"] = s.GoonRichPresence;
                block["seenSharePrompt"] = s.GoonSeenSharePrompt;
            }
            catch (Exception ex) { Log.Debug("GoonHostService.BuildDiscordBlock: {E}", ex.Message); }
            if (includeLastOpponent) block["lastOpponent"] = BuildLastOpponentBlock();
            return block;
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
                return new JObject
                {
                    ["name"] = name,
                    ["avatarDataUri"] = JValue.CreateNull(),
                    ["dm"] = !string.IsNullOrEmpty((string?)rec["dmId"]),
                    ["ts"] = (long?)rec["ts"] ?? 0L,
                };
            }
            catch (Exception ex)
            {
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
            var block = BuildDiscordBlock(includeLastOpponent: false);
            block["type"] = "discord";
            return block;
        }

        /// <summary>WPF OnLastOpponentClear: the record goes, the echo says so.</summary>
        public static void OnLastOpponentClear()
        {
            try
            {
                var s = CoreSettings.Current;
                if (string.IsNullOrEmpty(s.GoonLastOpponentJson)) return;
                s.GoonLastOpponentJson = "";
                CoreSettings.Save();
            }
            catch (Exception ex) { Log.Debug("GoonHostService.last-opponent-clear: {E}", ex.Message); }
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
                // not ported: MergedAccountRecovery.TryHandle (contract D) is not in Core yet.
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
