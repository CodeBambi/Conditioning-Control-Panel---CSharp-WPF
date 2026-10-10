using System;
using Avalonia.Controls;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The host side every game window shares beyond the shell frames: the per-game dispatch seam
    /// (each game's own partial claims its frames first), the media manifest on ready, the heartbeat
    /// watchdog (WPF DtrhHostService.StartHeartbeatWatch / ArcademyHostService: 5 s tick, recover
    /// once, then close), and page-driven fullscreen (WPF ApplyHostFullscreen: C# owns the borderless
    /// toggle and echoes the state back).
    /// </summary>
    internal sealed partial class GameWindow
    {
        internal bool IsReady { get; private set; }
        internal bool IsClosedOrClosing { get; private set; }

        /// <summary>WPF DtrhHostService: a live run gets the fast ladder (10 s), the hub a lazier one (20 s).
        /// ArcademyHostService: 12 s mid-class, 20 s otherwise.</summary>
        internal const double HubSilenceLimitSeconds = 20;
        internal static double RunSilenceLimitSeconds(string id) => id == "arcademy" ? 12 : 10;

        private DispatcherTimer? _heartbeatWatch;
        private DateTime _lastHeartbeatUtc = DateTime.UtcNow;
        private bool _beating, _recoveredOnce;

        /// <summary>True while a descent (DtRH) or class (Arcademy) runs: the watchdog's fast ladder.</summary>
        internal bool InRun { get; set; }

        private void OnGameOpened()
        {
            if (Spec.Id is "backroom" or "breakout" or "breakoutdemo") OpenBackRoom();
            if (Spec.Id is "dtrh" or "arcademy") StartHeartbeatWatch();
            if (Spec.Id == PbpId) OpenPbp();   // chess host (GameWindow.Pbp.cs); it unhooks itself on Closed
            if (Spec.Id == "arcademy") OpenArcademy();
            if (Spec.Id == "dtrh") OpenDtrhHost();
            if (Spec.Id == LoomId) OpenLoom();
        }

        private void OnGameClosed()
        {
            StopHeartbeatWatch();
            try { CloseBackRoom(); } catch (Exception ex) { Log.Debug("[Game] backroom close: {E}", ex.Message); }
            try { CloseDtrh(); } catch (Exception ex) { Log.Debug("[Game] dtrh close: {E}", ex.Message); }
            CloseLoom();
        }

        /// <summary>Each game's own frames (WPF *HostService.OnPageMessage). True = claimed.</summary>
        private bool HandleGameMessage(JObject o)
        {
            if (_backRoom != null && HandleBackRoom(o)) return true;
            if (Spec.Id == "dtrh" && HandleDtrh(o)) return true;
            if (Spec.Id == "race" && HandleRace(o)) return true;   // RaceWindow.Host.cs
            if (Spec.Id == PbpId && HandlePbp(o)) return true;
            if (Spec.Id == "goon" && HandleGoon(o)) return true;
            if (Spec.Id == "arcademy" && HandleArcademy(o)) return true;
            if (Spec.Id == FypId && HandleFyp(o)) return true;
            if (Spec.Id == LoomId && HandleLoom(o)) return true;
            if (Spec.Id == JustDropId) return HandleJustDrop(o);
            return false;
        }

        /// <summary>WPF each host's OnPageReady: init, then the manifest (DtRH, Arcademy, Goon, chess read it).</summary>
        private void OnPageReady()
        {
            if (Spec.Id == "dtrh")
            {
                // WPF DtrhHostService.OnPageReady: init carries the SAVED run setup for the hub's Descent tab.
                var init = JObject.FromObject(InitMessage());
                init["runSetup"] = JToken.FromObject(BuildRunSetup());
                DtrhInitExtras(init);
                Post(init);
                PostDtrhReady();
                // WPF :243: the mod's own descent media mixed into (or replacing) the library's.
                var m = GameMediaManifest.BuildLive();
                Dtrh.DtrhModContent.MergeMedia(m);
                Post(m.Frame());
                PostDtrhAfterManifest();
                return;
            }
            Post(InitMessage());
            Post(GameMediaManifest.BuildLive().Frame());
        }

        // ---- heartbeat ------------------------------------------------------------------------

        internal void NoteHeartbeat()
        {
            _beating = true;
            _lastHeartbeatUtc = DateTime.UtcNow;
        }

        private void StartHeartbeatWatch()
        {
            StopHeartbeatWatch();
            _lastHeartbeatUtc = DateTime.UtcNow;
            _heartbeatWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _heartbeatWatch.Tick += (_, _) => CheckHeartbeat(DateTime.UtcNow);
            _heartbeatWatch.Start();
        }

        private void StopHeartbeatWatch()
        {
            try { _heartbeatWatch?.Stop(); } catch { }
            _heartbeatWatch = null;
        }

        /// <summary>One watchdog tick. Guarded on ready AND a first beat, so a still-loading page (or one
        /// that never beats on this engine) cannot false-trip. Returns what it did, for tests.</summary>
        internal string CheckHeartbeat(DateTime nowUtc)
        {
            if (!IsReady || !_beating || IsClosedOrClosing) return "idle";
            double silent = (nowUtc - _lastHeartbeatUtc).TotalSeconds;
            double limit = InRun ? RunSilenceLimitSeconds(Spec.Id) : HubSilenceLimitSeconds;
            if (silent <= limit) return "ok";
            Log.Warning("[Game] {Id}: page heartbeat silent >{Limit}s ({Where}) - recovering", Spec.Id, limit, InRun ? "mid-run" : "hub");
            if (_recoveredOnce) { Close(); return "closed"; }
            // WPF Recover: relaunch once (the page boots again into its hub; a run in flight is banked first).
            _recoveredOnce = true;
            OnRecover();
            IsReady = false; _beating = false; InRun = false;
            _lastHeartbeatUtc = nowUtc;
            if (PageUrl != null) Web.Navigate(PageUrl);
            return "recovered";
        }

        private void OnRecover()
        {
            if (Spec.Id == "dtrh") BankDtrhRunOnTeardown("heartbeat-silent");
        }

        // ---- fullscreen -----------------------------------------------------------------------

        internal bool IsHostFullscreen => WindowState == WindowState.FullScreen;

        internal void SetHostFullscreen(bool on)
        {
            try { WindowState = on ? WindowState.FullScreen : WindowState.Normal; }
            catch (Exception ex) { Log.Debug("[Game] {Id}: fullscreen failed: {E}", Spec.Id, ex.Message); }
            Post(new { type = "fullscreen", on = IsHostFullscreen });
        }
    }
}
