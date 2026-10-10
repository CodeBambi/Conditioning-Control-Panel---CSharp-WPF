// PORTED from ConditioningControlPanel/Services/PieceByPiece/PieceByPieceHostService.cs (+ .Friends.cs)
// at WPF 7.1.5. The pure half (net whitelist, frames, reservoir, intents, watchdog ladder) is Core
// Services/PieceByPiece/PbpHostRules; this is the window glue. Online pictures are GameWindow.Pbp.Media.cs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Motion;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Services.Stakes;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The Piece by Piece host (WPF PieceByPieceHostService). Frames, exactly as WPF sends and answers them:
    /// <list type="bullet">
    /// <item>host -&gt; page once per boot, at <c>ready</c>: <c>pbp:settings</c> then <c>pbp:identity</c>
    /// (account UnifiedId + the CCP auth token, <c>online:false</c> with no cloud session so solo play
    /// stays free), then <c>pbp:media-state</c>, the online set, and a waiting friend / Lobby intent.</item>
    /// <item>page -&gt; host <c>pbp:net</c> / host -&gt; page <c>pbp:net-result</c>: HTTP for the page, the
    /// token attached HERE, only <c>/v2/pbp/*</c> and only GET / POST ever forwarded.</item>
    /// <item><c>pbp:media-request</c> -&gt; <c>pbp:media</c>: the player's own library, local disk only.</item>
    /// <item><c>pbp:friend</c> (challenge / accept / join / host) and the page's <c>pbp:friend-challenge</c>.</item>
    /// <item><c>stake-*</c> -&gt; <c>stake</c>: the shared Core <see cref="StakeBridge"/> (game <c>pbp</c>).</item>
    /// <item><c>heartbeat</c> / <c>pong</c> / <c>ping</c>: 20 s silence, one ping, then close; 45 s boot deadline.</item>
    /// </list>
    /// <c>pbp:exit</c>, <c>boot-error</c> and <c>pbp:escape</c> are the shell's (GameWindow.cs, GameWindow.Escape.cs).
    /// The host never reads or relays a rating: a player's view of the opponent's IQ is the page's rule and
    /// nothing here can hand one over.
    /// </summary>
    internal sealed partial class GameWindow
    {
        internal const string PbpId = "piecebypiece";

        /// <summary>One client for the app session. 30 s covers the events long poll (server caps at 8 s).</summary>
        private static readonly HttpClient PbpHttp = BuildPbpHttp();

        private static HttpClient BuildPbpHttp()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            try { c.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{CoreReleaseContent.AppVersion}"); }
            catch (Exception ex) { Diag.Swallowed(ex); }
            return c;
        }

        /// <summary>Test seam: the transport the net lane sends through.</summary>
        internal static Func<HttpClient> PbpHttpClient { get; set; } = () => PbpHttp;

        /// <summary>Where <c>/v2/pbp/*</c> lives on this head: the proxy, or null in a sandbox with no
        /// loopback (the head's one rule, FriendsHead.BaseUrl), which answers every call status 0.</summary>
        internal static Func<string?> PbpServerBase { get; set; } = () =>
            FriendsHead.BaseUrl(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"), null);

        private static readonly Random PbpRng = new();

        private bool _pbpOpen;
        private bool _pbpSettingsPosted;   // pbp:settings goes out exactly once per boot
        private bool _pbpIdentityPosted;   // and so does pbp:identity
        private bool _pbpPinged;
        private DateTime _pbpLastBeatUtc = DateTime.UtcNow;
        private DateTime _pbpLastProgressUtc = DateTime.UtcNow;
        private DispatcherTimer? _pbpWatch;
        private JObject? _pbpIntent;

        /// <summary>The page's LAST boot attempt failed (WPF BootFailedThisSession): a last-attempt flag,
        /// cleared by the next boot that reaches ready.</summary>
        internal static bool PbpBootFailedThisSession { get; private set; }

        // ============================ launch / close ============================

        /// <summary>WPF Launch, the part after the window exists. Free for everyone since 2026-09-27:
        /// no tier gate and no account needed on the board.</summary>
        private void OpenPbp()
        {
            if (_pbpOpen) return;
            _pbpOpen = true;
            SeedPbpSeams();
            _pbpSettingsPosted = false;
            _pbpIdentityPosted = false;
            _pbpPinged = false;
            _pbpLastBeatUtc = _pbpLastProgressUtc = DateTime.UtcNow;
            Closed += (_, _) => ClosePbp();
            StartPbpWatch();
            try { FriendsHead.Service?.EnterActivity(PresenceActivity.Chess); } catch (Exception ex) { Diag.Swallowed(ex); }
            // ponytail: WPF also calls SeasonRecapService.TrackFeature(PieceByPiece); that service is head-side on WPF.
            Log.Information("PieceByPieceHost: launched");
        }

        /// <summary>The one funnel every exit reaches (title-bar X, pbp:exit, boot error, watchdog, panic).
        /// Nothing here is authoritative over anything, so there is nothing to flush.</summary>
        private void ClosePbp()
        {
            if (!_pbpOpen) return;
            _pbpOpen = false;
            StopPbpWatch();
            _pbpSettingsPosted = false;
            _pbpIdentityPosted = false;
            _pbpPinged = false;
            DisposePbpOnlineMedia();
            DropPbpFriendIntent();
            try { FriendsHead.Service?.LeaveActivity(PresenceActivity.Chess); } catch (Exception ex) { Diag.Swallowed(ex); }
            Log.Information("PieceByPieceHost: closed");
        }

        // ============================ page messages ============================

        /// <summary>WPF OnPageMessage. True = claimed; the shell frames (ready's init, pbp:exit, boot-error,
        /// heartbeat bookkeeping) fall through to GameWindow.HandleMessage.</summary>
        private bool HandlePbp(JObject o)
        {
            // Every message is a sign of life, whatever it says: the boot deadline is PROGRESS-aware.
            _pbpLastProgressUtc = DateTime.UtcNow;
            var type = (string?)o["type"];
            try
            {
                // PvP stakes (Core Services/Stakes): the shared bridge talks to /v2/stakes/* itself and
                // books a lost time stake once the match settles. Online PvP only.
                if (StakeBridge.Handles(type))
                {
                    _ = PbpStakes.Handle(o);
                    return true;
                }
                switch (type)
                {
                    case "ready":
                        OnPbpReady();
                        return false;
                    case "heartbeat":
                    case "pong":
                        _pbpLastBeatUtc = DateTime.UtcNow;
                        _pbpPinged = false;
                        return false;
                    case "boot-error":
                        PbpBootFailedThisSession = true;
                        return false;
                    case "pbp:media-request":
                        OnPbpMediaRequest(o);
                        return true;
                    case "pbp:net":
                        OnPbpNetRequest(o);
                        return true;
                    case "pbp:media-flavour":
                        OnPbpMediaFlavour(o);
                        return true;
                    case "pbp:media-more":
                        OnPbpMediaMore();
                        return true;
                    case "pbp:friend-challenge":
                        OnPbpFriendChallenge(o);
                        return true;
                }
            }
            catch (Exception ex)
            {
                // Crash-safe by construction: one malformed frame must never take the window with it.
                Log.Warning("PieceByPiece: message handler threw ({Type}): {E}", type, ex.Message);
                return true;
            }
            return false;
        }

        // ============================ boot ============================

        private void OnPbpReady()
        {
            try
            {
                PbpBootFailedThisSession = false;   // a boot that succeeds clears the last attempt's flag
                _pbpLastBeatUtc = _pbpLastProgressUtc = DateTime.UtcNow;
                _pbpPinged = false;
                try { Web.Focus(); } catch (Exception ex) { Diag.Swallowed(ex); }
                PostPbpSettings();
                PostPbpIdentity();
                AdoptSavedPbpMediaChoice();
                PostPbpMediaState();
                StartPbpOnlineMedia();
                PostPbpFriendIntent();
            }
            catch (Exception ex) { Log.Warning("PieceByPieceHost.OnPageReady: {E}", ex.Message); }
        }

        /// <summary>Who is playing, and how the page reaches the server. Once per boot, behind settings.</summary>
        private void PostPbpIdentity()
        {
            if (_pbpIdentityPosted) return;
            _pbpIdentityPosted = true;
            try
            {
                Post(PbpHostRules.IdentityFrame(CoreAccount.UnifiedUserId, PbpAuthToken(),
                    SafePbp(() => CoreSettings.Current.UserDisplayName), CoreReleaseContent.AppVersion));
            }
            catch (Exception ex) { Log.Debug("PieceByPiece: identity post failed: {E}", ex.Message); }
        }

        /// <summary>The one host -&gt; page settings frame, sent once per boot.</summary>
        private void PostPbpSettings()
        {
            if (_pbpSettingsPosted) return;
            _pbpSettingsPosted = true;
            try
            {
                int hold = 0;
                try { hold = CoreSettings.Current.VideoMinDurationSeconds; } catch (Exception ex) { Diag.Swallowed(ex); }
                Post(PbpHostRules.SettingsFrame(hold, PbpReducedMotion(), PbpWhispers()));
            }
            catch (Exception ex) { Log.Debug("PieceByPiece: settings post failed: {E}", ex.Message); }
        }

        /// <summary>The CCP auth token (CoreSecrets behind AppSettings.AuthToken), NOT the Patreon bearer.
        /// Empty with no cloud session, which the page reads as "online play is not available".</summary>
        private static string PbpAuthToken() => SafePbp(() => CoreSettings.Current.AuthToken) ?? string.Empty;

        private static string? SafePbp(Func<string?> read)
        {
            try { return read(); } catch { return null; }
        }

        /// <summary>The app's motion setting, capped by the OS animation switch. Reduced and Off both read
        /// as reduced motion on the page: it has no third state.</summary>
        private static bool PbpReducedMotion()
        {
            try
            {
                return MotionGate.ResolveLevel(CoreSettings.Current.MotionLevel,
                    global::ConditioningControlPanel.Avalonia.Controls.Fx.OsReducedMotion.AnimationsEnabled) != MotionLevel.Full;
            }
            catch { return false; }
        }

        /// <summary>Distraction's whisper clips (Core PbpWhisperClips, WPF SafeWhisperClips): the player's
        /// brain drain folder first. Never synthetic speech; empty = the page plays no whispers.</summary>
        private static IReadOnlyList<string> PbpWhispers()
        {
            try
            {
                static IEnumerable<string>? Names(string? dir)
                    => !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? Directory.GetFiles(dir).Select(Path.GetFileName)! : null;
                var s = CoreSettings.Current;
                bool subAudio = s.SubAudioAudible && ModAudioPolicy.UsesSharedSubAudio(CoreMods.ActiveModId);
                var clips = PbpWhisperClips.Build(
                    Names(Path.Combine(CorePaths.EffectiveAssets, PbpWhisperClips.BrainDrainFolder)),
                    subAudio, null, null);
                // SEAM(shared WebAssetServer): WPF maps two more virtual hosts for the fallback clips
                // (ccp.subaudio = Resources/sub_audio, ccp.words = the neutral Circe words). The loopback
                // server serves only the page root and ccp.assets, so with no brain drain clips of the
                // player's own the list is empty and the page stays silent (never synthetic speech).
                if (clips.Count == 0) Log.Debug("PieceByPiece: no brain drain clips; fallback whisper hosts are not served on this head");
                return clips.Select(PbpPageUrl).Where(u => u != null).Select(u => u!).ToList();
            }
            catch (Exception ex)
            {
                Log.Debug("PieceByPiece: whisper clips failed: {E}", ex.Message);
                return Array.Empty<string>();
            }
        }

        /// <summary>A WPF <c>https://ccp.assets/&lt;rel&gt;</c> url as this head serves it (the loopback
        /// server's ccp.assets prefix, same origin as the page). Null for a host this head cannot serve.</summary>
        internal static string? PbpPageUrl(string wpfUrl)
        {
            const string assets = "https://" + PbpWhisperClips.AssetsHost + "/";
            if (!wpfUrl.StartsWith(assets, StringComparison.Ordinal)) return null;
            var rel = string.Join('/', wpfUrl[assets.Length..].Split('/').Select(Uri.UnescapeDataString));
            return WebAssetServer.Shared.AssetUrl(rel);
        }

        // ============================ the player's own library ============================

        /// <summary>Answer <c>pbp:media-request</c> from the player's own library. Any list may come back
        /// empty. Online pictures never ride this reply: they come as <c>pbp:online-media</c>.</summary>
        private void OnPbpMediaRequest(JObject o)
        {
            int count = PbpHostRules.ReadCount(o);
            var kinds = PbpHostRules.ReadKinds(o["kinds"]);
            var media = PbpHostRules.SampleMedia(PbpLibrary(), kinds, count, PbpRng);
            Post(PbpHostRules.MediaFrame(media));
            Log.Debug("PieceByPiece: media reply {I} images, {G} gifs, {V} videos",
                media.Images.Length, media.Gifs.Length, media.Videos.Length);
        }

        /// <summary>Test seam: the active pool (WPF DtrhAssetManifest.EnumerateActive; here the port's one
        /// authority on it, GameMediaManifest: decodable extensions, unchecked assets honoured, size caps).</summary>
        internal static Func<IEnumerable<(string Rel, string Url, bool IsImage)>> PbpLibrary { get; set; } = () =>
        {
            var m = GameMediaManifest.BuildLocal();   // local disk only, as WPF EnumerateActive
            return m.Images.Select(e => (e.Name, e.Url, true)).Concat(m.Videos.Select(e => (e.Name, e.Url, false)));
        };

        // ============================ the net lane ============================

        /// <summary><c>pbp:net</c> -&gt; one HTTP call -&gt; <c>pbp:net-result</c>. Never leaves a call
        /// unanswered: an id with no reply would sit in the page's pending map for 45 s.</summary>
        private void OnPbpNetRequest(JObject o)
        {
            var call = PbpHostRules.ReadNet(o);
            if (call.Refusal != null)
            {
                Log.Warning("PieceByPiece: net REJECTED ({Why}) for {Method} '{Path}'", call.Refusal, call.Method, call.Path);
                Post(PbpHostRules.NetResult(call.Id, 0, call.Refusal));
                return;
            }
            var token = PbpAuthToken();
            var baseUrl = PbpServerBase();
            _ = Task.Run(async () =>
            {
                var (status, body) = await PbpHostRules.SendAsync(PbpHttpClient(), baseUrl, call, token, CoreReleaseContent.AppVersion)
                    .ConfigureAwait(false);
                global::ConditioningControlPanel.Services.MergedAccountRecovery.TryHandle(status, body);   // contract D (WPF PieceByPieceHostService:604)
                PbpOnUi(() => Post(PbpHostRules.NetResult(call.Id, status, body)));
            });
        }

        private void PbpOnUi(Action act)
        {
            void Run()
            {
                if (IsClosedOrClosing) return;
                try { act(); } catch (Exception ex) { Diag.Swallowed(ex); }
            }
            if (Dispatcher.UIThread.CheckAccess()) Run(); else Dispatcher.UIThread.Post(Run);
        }

        // ============================ stakes ============================

        private static StakeBridge? _pbpStakes;

        /// <summary>One bridge for the life of the app (WPF _stakes): a settle watch it started keeps
        /// running, and books, after the window closes; posting to a closed board is a quiet no-op.</summary>
        internal static StakeBridge PbpStakes
        {
            get => _pbpStakes ??= StakeBridge.ForApp("pbp", PostPbpStake);
            set => _pbpStakes = value;
        }

        private static void PostPbpStake(JObject o)
        {
            void Run()
            {
                try { LivePbp()?.Post(o); }
                catch (Exception ex) { Log.Debug("PieceByPiece: stake post failed: {E}", ex.Message); }
            }
            if (Dispatcher.UIThread.CheckAccess()) Run(); else Dispatcher.UIThread.Post(Run);
        }

        /// <summary>The head's doors for the Core stakes and online-picture seams. First seeding wins, so
        /// the Goon host may seed the same ones.</summary>
        private static void SeedPbpSeams()
        {
            StakeApi.DefaultIdentity ??= FriendsHead.Identity;
            StakeApi.DefaultBaseUrl ??= () => PbpServerBase();
            StakeBridge.ChasterState ??= () => ChasterHead.Service is { } c ? (c.IsLinked, c.SafetyHoldRemaining) : null;
            StakeBridge.OnUi ??= a => { if (Dispatcher.UIThread.CheckAccess()) a(); else Dispatcher.UIThread.Invoke(a); };
            StakeSettlement.BookTime ??= (row, seconds) => ChasterHead.Service?.NoteSeconds(row, seconds).AppliedSeconds ?? 0;
            StakeSettlement.Account ??= () => FriendsHead.Identity()?.UnifiedId;
            StakeSettlement.OnUi ??= a => Dispatcher.UIThread.Post(a);
            SeedPbpMediaSeams();
        }

        // ============================ watchdogs ============================

        private void StartPbpWatch()
        {
            StopPbpWatch();
            _pbpWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _pbpWatch.Tick += (_, _) => CheckPbpWatch(DateTime.UtcNow);
            _pbpWatch.Start();
        }

        private void StopPbpWatch()
        {
            try { _pbpWatch?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _pbpWatch = null;
        }

        /// <summary>One 5 s tick of both WPF watchdogs: the boot deadline (a page that never runs a line of
        /// script) and the heartbeat ladder (silent past 20 s: one ping, then close). Returns what it did.</summary>
        internal string CheckPbpWatch(DateTime nowUtc)
        {
            if (!_pbpOpen || IsClosedOrClosing) return "idle";
            if (PbpHostRules.BootTimedOut(IsReady, nowUtc - _pbpLastProgressUtc))
            {
                PbpBootFailedThisSession = true;
                Log.Warning("PieceByPiece: page boot-error: boot deadline: no progress for {S:0}s", PbpHostRules.BootDeadline.TotalSeconds);
                Close();
                return "boot-timeout";
            }
            switch (PbpHostRules.HeartbeatStep(IsReady, (nowUtc - _pbpLastBeatUtc).TotalSeconds, _pbpPinged))
            {
                case PbpHostRules.WatchStep.Ping:
                    _pbpPinged = true;
                    try { Post(new { type = "ping" }); } catch (Exception ex) { Diag.Swallowed(ex); }
                    return "ping";
                case PbpHostRules.WatchStep.Close:
                    Log.Warning("PieceByPiece: page heartbeat silent >20s and no pong - closing");
                    Close();
                    return "closed";
                case PbpHostRules.WatchStep.Ok:
                    return "ok";
                default:
                    return "idle";
            }
        }

        // ============================ friends drawer + Lobby (WPF .Friends.cs) ============================

        private static TaskCompletionSource<string?>? _pbpChallenge;
        private static string? _pbpChallengeFor;

        /// <summary>Test seam: open the board (or focus the open one).</summary>
        internal static Func<GameWindow?> PbpLaunch { get; set; } = () => Launch(PbpId);

        private static GameWindow? LivePbp()
        {
            lock (Open) return Open.FirstOrDefault(w => w.Spec.Id == PbpId && !w.IsClosedOrClosing);
        }

        /// <summary>True while the board is open (WPF IsActive).</summary>
        internal static bool PbpIsActive => LivePbp() != null;

        /// <summary>Open the board (or use the open one) and challenge <paramref name="friendId"/>. Resolves
        /// with the server's challenge id, or null when the page could not make one in time. One at a time:
        /// a second call for the same friend while one is out answers the same task.</summary>
        internal static Task<string?> PbpChallengeFriendAsync(string friendId, TimeSpan timeout)
        {
            if (string.IsNullOrEmpty(friendId)) return Task.FromResult<string?>(null);
            if (_pbpChallenge is { Task.IsCompleted: false } live && _pbpChallengeFor == friendId) return live.Task;
            _pbpChallenge?.TrySetResult(null);

            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pbpChallenge = tcs;
            _pbpChallengeFor = friendId;
            if (!PbpIntend(PbpHostRules.ChallengeIntent(friendId))) { tcs.TrySetResult(null); return tcs.Task; }

            _ = Task.Delay(timeout).ContinueWith(_ => tcs.TrySetResult(null), TaskScheduler.Default);
            return tcs.Task;
        }

        /// <summary>The friend's side of a chess invite: open the board on that challenge.</summary>
        internal static void PbpJoinFriendChallenge(string challengeId)
        {
            if (!InviteDestination.IsChallengeId(challengeId)) return;
            PbpIntend(PbpHostRules.AcceptIntent(challengeId));
        }

        /// <summary>The Lobby's Join on a chess open table: open the board and sit at that <c>p_</c> table.</summary>
        internal static bool PbpJoinOpenTable(string target)
        {
            if (!PbpHostRules.IsTableId(target)) return false;
            return PbpIntend(PbpHostRules.JoinIntent(target));
        }

        /// <summary>The Lobby's "Host a chess table".</summary>
        internal static bool PbpHostOpenTable() => PbpIntend(PbpHostRules.HostIntent());

        /// <summary>WPF Intend: the intent waits for the identity frame on a fresh launch, and goes out at
        /// once to a board that is already up and talking. False = no board could be opened.</summary>
        private static bool PbpIntend(JObject intent)
        {
            bool fresh = LivePbp() == null;
            GameWindow? w = null;
            try { w = PbpLaunch(); } catch (Exception ex) { Log.Warning(ex, "PieceByPiece: launch for an intent failed"); }
            if (w == null || w.Spec.Id != PbpId) return false;
            w._pbpIntent = intent;
            if (!fresh && w._pbpIdentityPosted) w.PostPbpFriendIntent();
            return true;
        }

        private void PostPbpFriendIntent()
        {
            var intent = _pbpIntent;
            if (intent == null) return;
            _pbpIntent = null;
            try { Post(intent); }
            catch (Exception ex) { Log.Debug("PieceByPiece: friend intent post failed: {E}", ex.Message); }
        }

        private static void OnPbpFriendChallenge(JObject o)
        {
            var id = o["challengeId"]?.Type == JTokenType.String ? (string?)o["challengeId"] : null;
            _pbpChallenge?.TrySetResult(InviteDestination.IsChallengeId(id) ? id : null);
        }

        private void DropPbpFriendIntent()
        {
            _pbpIntent = null;
            _pbpChallenge?.TrySetResult(null);
            _pbpChallenge = null;
            _pbpChallengeFor = null;
        }
    }
}
