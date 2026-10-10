// PORTED from WPF 7.1.5 Services/Race/RacingAccess.cs + CaucusHostService.Launch: the race's door.
// The page (Resources/web/dtrh/race.html) runs in the shared GameWindow (panic surface "games"); its
// host frames are the GameWindow partials RaceWindow.Host.cs (boot, payout, watchdog, grants),
// RaceWindow.Tracks.cs (the player's own tracks) and RaceWindow.Cloud.cs (BambiCloud levels). The
// ownership rules themselves are Core Services/Race/RacingAccess.
using System;
using System.Linq;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Race;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal static class RaceWindow
    {
        /// <summary>WPF PrizeGrants.RacingTrackMax: the original tracks are rt.original.00..10.</summary>
        internal const int RacingTrackMax = RacingAccess.RacingTrackMax;

        /// <summary>WPF PrizeGrants.RacingTrack(n).</summary>
        internal static string RacingTrack(int n) => RacingAccess.RacingTrack(n);

        /// <summary>Points Core RacingAccess (the cloud gate reads it) at this head's ownership store.</summary>
        static RaceWindow() => RacingAccess.IsGrantedProvider = id => PrizeOwnership.IsGranted(id);

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

        /// <summary>WPF BackRoomHostService.OnRoomMessage: why the room's race door stays shut, or null.
        /// "locked" = no account or no racing purchase (the Gate), "busy" = a race is already up.</summary>
        internal static string? Refusal(Func<string, bool>? owns = null, bool? signedIn = null)
        {
            bool open;
            try { open = (signedIn ?? CoreAccount.IsLoggedIn) && CanLaunch(owns); }
            catch (Exception ex) { Log.Debug(ex, "[Race] door probe threw"); open = false; }
            return !open ? "locked" : _live != null ? "busy" : null;
        }

        /// <summary>WPF CaucusHostService.Launch: one race at a time, focused rather than relaunched.
        /// Returns null when the gate refused.</summary>
        internal static GameWindow? Launch()
        {
            if (_live != null) { _live.Activate(); return _live; }
            try { if (!Gate()) return null; }
            catch (Exception ex) { Log.Warning(ex, "[Race] gate threw; refused"); return null; }
            var window = new GameWindow(Spec);
            window.StartRace();
            window.Closed += (_, _) => { if (ReferenceEquals(_live, window)) _live = null; };
            _live = window;
            window.Load(WebAssetServer.Shared);
            window.Show();
            return window;
        }
    }
}
