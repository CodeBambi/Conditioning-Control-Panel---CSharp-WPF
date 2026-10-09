// PORTED (core slice) from WPF 7.1.5 Services/Chaos/CaucusHostService.cs (Racing Thoughts host, 1530
// lines) + Services/Race/RacingAccess.cs. The race page (Resources/web/dtrh/race.html) runs in the
// shared GameWindow (panic surface "games", the init + manifest + exit frames); this file adds the
// race's own door and the frames that let it boot on owned tracks.
// ponytail: not ported yet - track-pick/play/pause/stop (TrackPlayer, TrackAnalyzer, TrackWordSpotter:
// the player's own audio files), cloud-open/cloud-start (RaceCloudWindow), run-started/run-ended
// payout into chaos_meta (DtrhMetaBridge), favorites, loom-list, heartbeat watchdog, fullscreen-set.
using System;
using System.Linq;
using ConditioningControlPanel.Avalonia.Platform;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal static class RaceWindow
    {
        /// <summary>WPF PrizeGrants.RacingTrackMax: the original tracks are rt.original.00..10.</summary>
        internal const int RacingTrackMax = 10;

        /// <summary>WPF PrizeGrants.RacingTrack(n).</summary>
        internal static string RacingTrack(int n) =>
            "rt.original." + n.ToString("00", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>WPF RacingAccess.OwnedTracks (purchase door armed): the tracks the page may pick from.</summary>
        internal static int[] OwnedTracks(Func<string, bool>? owns = null)
        {
            owns ??= PrizeOwnership.IsGranted;
            return Enumerable.Range(0, RacingTrackMax + 1).Where(n => owns(RacingTrack(n))).ToArray();
        }

        /// <summary>WPF RacingAccess.CanLaunch: any owned original track opens the door.</summary>
        internal static bool CanLaunch(Func<string, bool>? owns = null)
        {
            try { return OwnedTracks(owns).Length > 0; }
            catch (Exception ex) { Log.Debug(ex, "[Race] ownership probe threw"); return false; }
        }

        /// <summary>The race as a GameWindow spec: an account (WPF LauncherCatalogue default) and an
        /// owned track (CaucusHostService.Launch refuses on RacingAccess.CanLaunch).</summary>
        internal static readonly GameWindow.Game Spec = new("race", "launcher_game_race_title", "dtrh/race.html", Gate);

        private static bool Gate()
        {
            if (!CoreAccount.IsLoggedIn) { Log.Information("[Race] refused: no account"); return false; }
            if (!CanLaunch()) { Log.Information("[Race] launch refused, no racing purchase"); return false; }
            return true;
        }

        private static GameWindow? _live;

        /// <summary>WPF CaucusHostService.Launch: one race at a time, focused rather than relaunched.
        /// Returns null when the gate refused.</summary>
        internal static GameWindow? Launch()
        {
            if (_live != null) { _live.Activate(); return _live; }
            try { if (!Gate()) return null; }
            catch (Exception ex) { Log.Warning(ex, "[Race] gate threw; refused"); return null; }
            var window = new GameWindow(Spec);
            window.Web.WebMessage += json => OnRaceMessage(window, json);
            window.Closed += (_, _) => { if (ReferenceEquals(_live, window)) _live = null; };
            _live = window;
            window.Load(WebAssetServer.Shared);
            window.Show();
            return window;
        }

        /// <summary>The race's own frames, after GameWindow's shared ones (it subscribed first, so the
        /// generic init lands before the ownership frame).</summary>
        internal static void OnRaceMessage(GameWindow window, string json)
        {
            if (window.PageUrl == null || window.Web.CurrentUrl == null || !GameWindow.SameOrigin(window.Web.CurrentUrl, window.PageUrl)) return;
            JObject o;
            try { o = JObject.Parse(json); }
            catch { return; }
            switch ((string?)o["type"])
            {
                case "ready":
                    // raceBoot.js 'race-ownership': the owned levels; an empty list hides every built-in one.
                    window.Post(new { type = "race-ownership", tracks = OwnedTracks() });
                    break;
                case "track-pick":
                    window.Post(new { type = "track-error", message = "your own tracks are not on this build yet" });
                    break;
                case "track-play":
                case "track-pause":
                case "track-stop":
                case "track-cancel":
                case "cloud-open":
                case "cloud-start":
                case "run-started":
                case "run-ended":
                    Log.Debug("[Race] {Type}: not handled on this head yet", (string?)o["type"]);
                    break;
            }
        }
    }
}
