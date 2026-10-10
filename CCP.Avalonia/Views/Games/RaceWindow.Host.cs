// PORTED from WPF 7.1.5 Services/Chaos/CaucusHostService.cs (the Racing Thoughts host): the boot
// frames (init with the race's own settings block, manifest, loom-list), run-started / run-ended with
// the one-payout latch and the chaos_meta payout, the grants watcher, the ping-then-close heartbeat
// watchdog and the teardown funnel. The race page runs in a GameWindow, so this is a GameWindow
// partial (the lane's "GameWindow.Race.cs"); the track and cloud halves are RaceWindow.Tracks.cs and
// RaceWindow.Cloud.cs. Shell frames the race shares (fullscreen-set, boot-error, exit-done) stay in
// GameWindow.HandleMessage.
using System;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games.Dtrh;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Race;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        private const int RaceProtocol = 1;
        private DtrhMetaBridge? _raceMeta;
        private readonly RaceRunLifecycle _raceRun = new();
        private int[] _raceOwned = Array.Empty<int>();
        private DispatcherTimer? _raceHeartbeatWatch;
        private DateTime _raceLastHeartbeatUtc;
        private bool _racePinged, _raceExiting, _raceLoomHooked, _raceStarted, _raceDisposed;
        /// <summary>WPF _sessionGeneration: bumped at teardown so a queued post knows it is late.</summary>
        private int _raceSession;

        /// <summary>Test seams: the owned tracks and the cloud gate (the real ones read PrizeOwnership).</summary>
        internal Func<int[]> RaceOwnedTracks = () => RaceWindow.OwnedTracks();
        internal Func<bool> RaceCanLaunch = () => RaceWindow.CanLaunch();
        internal Func<string?, bool> RaceCanOpenCloud = url => RacingAccess.CanOpenCloud(url);

        internal bool RaceRunActive => _raceRun.IsActive;

        /// <summary>WPF CaucusHostService.Launch, after the gate: the session state a race window needs
        /// before its page says ready. Idempotent; RaceWindow.Launch calls it before Show.</summary>
        internal void StartRace()
        {
            if (_raceStarted) return;
            _raceStarted = true;
            ++_raceSession;
            _raceOwned = RaceOwnedTracks();
            Platform.PrizeOwnership.Changed += OnRaceGrantsChanged;
            _raceExiting = false;
            _raceRun.Reset();
            _racePinged = false;
            // Real banking, never the cloned test state: the race pays Sparks into the same
            // chaos_meta.json the descent banks into.
            _raceMeta = new DtrhMetaBridge(testMode: false, Post);
            // The chart's decoder (WPF: NAudio) is this head's LibVLC; Core only reads the WAV it writes.
            TrackDecoder.ToWavProvider ??= RaceTrackPlayer.TranscodeToWav;
            Closed += (_, _) => DisposeRace();
            StartRaceHeartbeatWatch();
            HookRaceVideoFromApp();   // WPF HookVideoEvents(true): a mandatory video pauses the race
            try { Platform.FriendsHead.Service?.EnterActivity(PresenceActivity.Race); }
            catch (Exception ex) { Log.Debug("RaceHost: presence enter: {E}", ex.Message); }
            Log.Information("CaucusHostService: launched");
        }

        /// <summary>WPF CaucusHostService.OnPageMessage. True = claimed; false falls through to the
        /// shell frames (fullscreen-set, boot-error, exit's 1200 ms watchdog, exit-done).</summary>
        private bool HandleRace(JObject o)
        {
            if (!_raceStarted) StartRace();
            switch ((string?)o["type"])
            {
                case "ready":
                    IsReady = true;
                    OnRacePageReady();
                    return true;
                case "heartbeat":
                case "pong":
                    _raceLastHeartbeatUtc = DateTime.UtcNow;
                    _racePinged = false;
                    return true;
                case "sfx":
                    // WPF CaucusHostService: the descent's native sfx bank.
                    Chaos.ChaosSfx.PlayFrame((string?)o["name"], (float?)o["scale"]);
                    return true;
                case "fire-payload":
                    RaceFirePayload(o);
                    return true;
                case "run-started":
                    if (!_raceRun.TryStart()) return true;
                    global::ConditioningControlPanel.Services.SeasonFeatureTracker.TrackFeature(global::ConditioningControlPanel.Models.SeasonFeatureKeys.Race);   // WPF CaucusHostService:338
                    Log.Information("RaceHost: run started (seed={Seed})", (string?)o["seed"]);
                    return true;
                case "run-ended":
                    OnRaceRunEnded(o);
                    return true;
                case "report-bug":
                    OpenRaceBugReport();
                    return true;
                case "track-pick":
                    RacePickTrack();
                    return true;
                case "track-play":
                    RaceTrackPlay();
                    return true;
                case "track-pause":
                    RaceTrackPause((bool?)o["on"] ?? false);
                    return true;
                case "track-stop":
                    RaceStopTrack();
                    return true;
                case "track-cancel":
                    RaceCancelAnalysis(postCancelled: true);
                    return true;
                case "cloud-open":
                    // `url` is optional (the site's front door without one); `front` is the page's
                    // fallback when a play press did not start their player.
                    RaceOpenCloud((string?)o["url"], background: (bool?)o["front"] != true);
                    return true;
                case "cloud-start":
                    RaceStartCloudTrack();
                    return true;
                case "exit":
                    // Page-initiated: it winds itself down, then exit-done. The shell arms the 1200 ms close.
                    _raceExiting = true;
                    RaceStopTrack();
                    return false;
                default:
                    return false;
            }
        }

        // ============================ boot ============================

        /// <summary>WPF OnPageReady: init (the race's settings block), manifest, favorites, loom-list.</summary>
        private void OnRacePageReady()
        {
            try
            {
                _raceLastHeartbeatUtc = DateTime.UtcNow;
                _racePinged = false;
                try { Platform.WebAssetServer.Shared.ModRoot ??= Dtrh.DtrhModContent.ModDtrhRoot; } catch (Exception ex) { _ = ex; }   // WPF :179 ccp.mod
                Post(RaceInitMessage());
                var raceMedia = GameMediaManifest.BuildLive();
                Dtrh.DtrhModContent.MergeMedia(raceMedia);   // WPF :288: creator mods mix / replace media, as the descent
                Post(raceMedia.Frame());
                try
                {
                    var favorites = DtrhAssetStatsStore.TopAssets(12);
                    if (favorites.Count > 0) Post(new { type = "favorites", names = favorites });
                }
                catch (Exception ex) { Log.Debug("RaceHost favorites post failed: {E}", ex.Message); }
                PostRaceLoomList();
                if (!_raceLoomHooked) { DtrhLoomStore.Changed += OnRaceLoomChanged; _raceLoomHooked = true; }
            }
            catch (Exception ex) { Log.Warning("CaucusHostService.OnPageReady: {E}", ex.Message); }
        }

        internal object RaceInitMessage() => new
        {
            type = "init",
            protocol = RaceProtocol,
            settings = new
            {
                masterVolume = RaceMasterVolume(),
                // Reduced and Off both read as reduced motion on the page: it has no third state.
                reducedMotion = RaceReducedMotion(),
                // The menu's `levels` panel: only a host that owns the browser window offers it.
                cloud = RaceCloudAvailable,
                racingTracks = RaceOwnedTracks(),
                // WPF sets this when the race came from the Back Room's shared host. Here the room
                // closes first and the race opens in a window of its own (RaceWindow.Refusal), so
                // there is no room to go back to.
                returnToCasino = false,
            },
            modId = RaceActiveModId(),
            // Creator mods' own DTRH content (drift pools, portrait, tint, drone) on the ccp.mod route.
            modContent = Dtrh.DtrhModContent.BuildInitPayload(),
        };

        private static int RaceMasterVolume()
        {
            try { return CoreSettings.Current?.MasterVolume ?? 100; }
            catch { return 100; }
        }

        private static bool RaceReducedMotion()
        {
            try { return CoreSettings.Current.MotionLevel != MotionLevel.Full; }
            catch { return false; }
        }

        private static string RaceActiveModId()
        {
            try { return CoreMods.ActiveModId ?? "builtin-sissyhypno"; }
            catch { return "builtin-sissyhypno"; }
        }

        // ============================ page messages ============================

        /// <summary>fire-payload: audio only. Every visual effect is in-world on the page, and the video
        /// kind is dark at both ends (the page never spawns one, this refuses it if one ever asks).</summary>
        private void RaceFirePayload(JObject o)
        {
            try
            {
                var kind = (string?)o["kind"];
                if (string.IsNullOrWhiteSpace(kind)) return;
                if (string.Equals(kind, "video", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Information("RaceHost: video payload refused, video bubbles are dark");
                    return;
                }
                if (!string.Equals(kind, "audio", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warning("RaceHost: payload kind '{K}' is in-world - ignored", kind);
                    return;
                }
                bool fired = Chaos.DtrhPayloadBridge.Fire(kind, (int?)o["strength"], (double?)o["durationMult"]);
                Log.Information("RaceHost: native payload {K} {R}", kind, fired ? "fired" : "had nothing to fire");
            }
            catch (Exception ex) { Log.Warning("RaceHost.FirePayload: {E}", ex.Message); }
        }

        /// <summary>WPF OnRunEnded's numbers, pure: the descent's formula with the score divided by 5
        /// first (the race's multiplier ladder makes its scores run about 5x a descent's), under the
        /// same 250 XP a minute ceiling.</summary>
        internal static (int BaseXp, int FinalXp, double DurationSec, double SparkScore) RacePayout(double score, double durationSec, double skillMult)
        {
            score = Math.Max(0, score);
            durationSec = Math.Max(1, durationSec);
            double capBase = 250.0 * (durationSec / 60.0);
            int baseXp = (int)Math.Min(score / 5.0, capBase);
            return (baseXp, (int)Math.Round(baseXp * skillMult), durationSec, score / 5.0);
        }

        /// <summary>run-ended -> XP payout + Spark banking, answered with payout-result. One payout per
        /// run: a run-ended with no run-started before it (or a second one) pays nothing.</summary>
        private void OnRaceRunEnded(JObject o)
        {
            if (!_raceRun.TryEnd()) return;
            // The file is the clock, so the end of the run is the end of the audio either way.
            RaceStopTrack();
            try
            {
                double skillMult = ChaosRunState.SkillMultProvider?.Invoke() ?? 1.0;
                var pay = RacePayout((double?)o["score"] ?? 0, (double?)o["durationSec"] ?? 60, skillMult);
                int bestCombo = (int?)o["bestCombo"] ?? 0;
                int popped = (int?)o["popped"] ?? 0;
                int effects = (int?)o["effects"] ?? 0;

                long previousBest = ChaosMeta.State.BestScore;

                int sparksEarned = 0;
                if (_raceMeta != null)
                {
                    // Score is scaled the same way for the Spark formula (it is sqrt-shaped, so an
                    // unscaled race score would out-bank every descent).
                    sparksEarned = _raceMeta.AwardRun(new ChaosMeta.ChaosRunRewardInput(
                        RunDurationSec: pay.DurationSec,
                        DifficultyMult: 1.0,
                        SparkGainMult: 1.0,
                        Score: pay.SparkScore,
                        TrickleDrops: 0,
                        DripFeedMaxed: false,
                        BestCombo: bestCombo,
                        Defused: effects,
                        ElapsedSec: pay.DurationSec));
                }

                try { CoreProgression.AddXP(pay.BaseXp, "Chaos"); }
                catch (Exception ex) { Log.Debug("RaceHost payout AddXP: {E}", ex.Message); }
                // Popped bubbles feed the GLOBAL bubble count and its sparkle-point milestones,
                // the same sink the descent and the native chaos mode credit.
                try { if (popped > 0) global::ConditioningControlPanel.Avalonia.App.Achievements?.TrackBubblesPopped(popped); }
                catch (Exception ex) { Log.Debug("RaceHost bubble credit: {E}", ex.Message); }

                Post(new
                {
                    type = "payout-result",
                    baseXp = pay.BaseXp,
                    skillMult,
                    finalXp = pay.FinalXp,
                    sparksEarned,
                    previousBest,
                    dryRun = false,
                });
                Log.Information(
                    "RaceHost: run complete: score {Score:0} over {Dur:0}s ({Laps} laps) -> base {Base} x skill {Mult:0.0} = {Final} XP, {Sparks} sparks",
                    (double?)o["score"] ?? 0, pay.DurationSec, (int?)o["laps"] ?? 0, pay.BaseXp, skillMult, pay.FinalXp, sparksEarned);
            }
            catch (Exception ex) { Log.Warning("RaceHost.OnRunEnded: {E}", ex.Message); }
        }

        /// <summary>The page's in-game bug button: the same window the rest of the app uses, owned by
        /// the race window so it sits on top even in fullscreen.</summary>
        private void OpenRaceBugReport()
        {
            RaceQueue(() =>
            {
                try { _ = new Windows.BugReportWindow().ShowDialogSafe(this); }
                catch (Exception ex) { Log.Warning("RaceHost.report-bug: {E}", ex.Message); }
            });
        }

        /// <summary>WPF QueueSession: run on the UI thread, only while this race session is still up.</summary>
        private void RaceQueue(Action action)
        {
            int session = _raceSession;
            Dispatcher.UIThread.Post(() =>
            {
                if (session == _raceSession && !_raceDisposed && !IsClosedOrClosing) action();
            });
        }

        // ============================ ownership ============================

        /// <summary>WPF OnRaceGrantsChanged: a track taken away (or the last one gone) closes the race;
        /// a track gained reaches the open page as race-ownership.</summary>
        private void OnRaceGrantsChanged() => Dispatcher.UIThread.Post(ApplyRaceGrants);

        internal void ApplyRaceGrants()
        {
            if (_raceDisposed || IsClosedOrClosing) return;
            var owned = RaceOwnedTracks();
            if (!RaceCanLaunch() || _raceOwned.Except(owned).Any()) { CloseRaceGracefully(); return; }
            if (_raceOwned.SequenceEqual(owned)) return;
            _raceOwned = owned;
            Post(new { type = "race-ownership", tracks = owned });
        }

        /// <summary>WPF CloseActive: ask the page to wind down (it answers exit / exit-done), force
        /// after 1200 ms. A page that never came up is simply closed.</summary>
        internal void CloseRaceGracefully()
        {
            if (IsReady && !_raceExiting)
            {
                _raceExiting = true;
                Post(new { type = "exit-request" });
                DispatcherTimer.RunOnce(() => { if (!IsClosedOrClosing) Close(); }, TimeSpan.FromMilliseconds(1200));
            }
            else if (!_raceExiting) Close();
        }

        // ============================ the loom ============================

        /// <summary>THE LOOM: the player's own woven spirals, slug + url + the params sidecar. The page
        /// draws an entry that kept its params live.
        /// The gif comes off the asset server's ccp.spirals route (WPF https://ccp.spirals/), so a spiral
        /// saved without params plays too.</summary>
        private void PostRaceLoomList()
        {
            try
            {
                Post(new
                {
                    type = "loom-list",
                    spirals = DtrhLoomStore.List()
                        .Select(s => new
                        {
                            slug = s.Slug,
                            url = RaceLoomUrl(s.Slug),
                            @params = TryParseLoomParams(s.ParamsJson),
                        }),
                });
            }
            catch (Exception ex) { Log.Debug("RaceHost.PostLoomList: {E}", ex.Message); }
        }

        internal static string RaceLoomUrl(string slug)
        {
            var server = Platform.WebAssetServer.Shared;
            server.Hosts.TryAdd(ArcSpiralsHost, () => DtrhLoomStore.SpiralsFolder);
            return server.HostUrl(ArcSpiralsHost, "loom_" + slug + ".gif");
        }

        private static JObject? TryParseLoomParams(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JObject.Parse(json); } catch { return null; }
        }

        /// <summary>A save or delete in the Loom: re-post, on the UI thread, only while the page is up.</summary>
        private void OnRaceLoomChanged() => RaceQueue(PostRaceLoomList);

        // ============================ watchdog ============================

        /// <summary>WPF StartHeartbeatWatch: a page silent past the limit gets one ping, and if it stays
        /// silent through the next tick the window is closed (no relaunch ladder: a race is short).</summary>
        private void StartRaceHeartbeatWatch()
        {
            StopRaceHeartbeatWatch();
            _raceLastHeartbeatUtc = DateTime.UtcNow;
            _raceHeartbeatWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _raceHeartbeatWatch.Tick += (_, _) => CheckRaceHeartbeat(DateTime.UtcNow);
            _raceHeartbeatWatch.Start();
        }

        private void StopRaceHeartbeatWatch()
        {
            try { _raceHeartbeatWatch?.Stop(); } catch { }
            _raceHeartbeatWatch = null;
        }

        /// <summary>One watchdog tick; returns what it did, for tests. Guarded on IsReady: the page
        /// only starts beating after boot, so a still-loading page cannot false-trip. 10 s mid-run,
        /// 20 s on the menu.</summary>
        internal string CheckRaceHeartbeat(DateTime nowUtc)
        {
            if (!IsReady || _raceExiting || IsClosedOrClosing) return "idle";
            double silent = (nowUtc - _raceLastHeartbeatUtc).TotalSeconds;
            double limit = _raceRun.IsActive ? 10 : 20;
            if (silent <= limit) return "ok";
            if (!_racePinged)
            {
                _racePinged = true;
                Post(new { type = "ping" });
                return "pinged";
            }
            Log.Warning("RaceHost: page heartbeat silent >{Limit}s and no pong - closing", limit);
            Close();
            return "closed";
        }

        // ============================ teardown ============================

        /// <summary>WPF DisposeAll: the one funnel every exit reaches (the window's Closed). Idempotent.</summary>
        private void DisposeRace()
        {
            if (_raceDisposed) return;
            _raceDisposed = true;
            Platform.PrizeOwnership.Changed -= OnRaceGrantsChanged;
            ++_raceSession;
            StopRaceHeartbeatWatch();
            UnhookRaceVideo();
            try { DisposeRaceCloud(); } catch (Exception ex) { Log.Debug("RaceHost: cloud dispose: {E}", ex.Message); }
            try { DisposeRaceTracks(); } catch (Exception ex) { Log.Debug("RaceHost: track dispose: {E}", ex.Message); }
            try { _raceMeta?.FlushSave(); } catch (Exception ex) { Log.Debug("RaceHost: meta flush: {E}", ex.Message); }
            _raceRun.Reset();
            if (_raceLoomHooked) { try { DtrhLoomStore.Changed -= OnRaceLoomChanged; } catch { } _raceLoomHooked = false; }
            _raceMeta = null;
            _raceExiting = false;
            Log.Information("CaucusHostService: closed");
            try { Platform.FriendsHead.Service?.LeaveActivity(PresenceActivity.Race); }
            catch (Exception ex) { Log.Debug("RaceHost: presence leave: {E}", ex.Message); }
        }
    }
}
