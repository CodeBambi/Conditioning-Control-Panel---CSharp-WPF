using System;
using System.Linq;
using Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The Back Room's race door, both ways (WPF BackRoomHostService.OnRoomClosed :458 / ReturnToRoom
    /// :474 and CaucusHostService.DisposeAll :714). The room winds down, the race opens, and when the
    /// race ends the room comes back on <c>?raceReturn=1</c>.
    ///
    /// <para>WPF hands the race the room's own window; this head has one window per game, so the race
    /// gets a window of its own and the room is opened again after it. What ends the trip without a
    /// return is what ends it in WPF: the title-bar X (there it shuts the shared window), panic, and
    /// the app going down.</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        // ---- seams (tests swap them) ---------------------------------------------------------------

        /// <summary>Why the room's race door stays shut ("locked" / "busy"), or null.</summary>
        internal static Func<string?> RoomRaceRefusal = DefaultRoomRaceRefusal;
        /// <summary>Open the race for the room. Null = it did not open.</summary>
        internal static Func<GameWindow?> RoomRaceLaunch = DefaultRoomRaceLaunch;
        /// <summary>Put the room back after the race.</summary>
        internal static Action RoomReturn = DefaultRoomReturn;

        internal const string RaceReturnQuery = "raceReturn=1";

        /// <summary>Bumped by every panic press: a trip that began before it never returns.</summary>
        private static int _roomTripEpoch;
        private bool _raceFromRoom, _roomReturnOwed;
        private int _roomTripAt;

        /// <summary>The race was opened from the room (WPF <c>returnToCasino</c> in the race's init).</summary>
        internal bool RaceFromRoom => _raceFromRoom;

        internal static string? DefaultRoomRaceRefusal() => RaceWindow.Refusal();
        internal static GameWindow? DefaultRoomRaceLaunch() => RaceWindow.Launch(fromRoom: true);

        internal static void DefaultRoomReturn()
        {
            if (!Games.TryGetValue("backroom", out var room)) return;
            GameWindow? live;
            lock (Open) live = Open.FirstOrDefault(w => w.Spec.Id == room.Id);
            if (live != null) { live.Activate(); return; }
            try { if (room.Gate != null && !room.Gate()) return; }   // signed out during the race: no room to go back to
            catch (Exception ex) { Log.Warning(ex, "[BackRoom] return gate threw; refused"); return; }
            var window = new GameWindow(room with { Query = RaceReturnQuery });
            window.Load(Platform.WebAssetServer.Shared);
            window.Show();
        }

        /// <summary>Panic: every trip in flight ends where it is. One line in CloseAllForPanic calls this.</summary>
        internal static void CancelRoomReturns() => System.Threading.Interlocked.Increment(ref _roomTripEpoch);

        /// <summary>The room has closed for the race (WPF OnRoomClosed). Revalidated after the room's own
        /// asynchronous exit: a door that shut meanwhile puts the room straight back.</summary>
        private void OpenRaceFromRoom(int tripAt, bool byHost)
        {
            if (tripAt != _roomTripEpoch || !byHost) return;   // panic, the X, or the app going down: no race, no return
            try
            {
                if (RoomRaceRefusal() != null) { RoomReturn(); return; }
                var race = RoomRaceLaunch();
                if (race == null) { RoomReturn(); return; }
                race.ArmRoomReturn(tripAt);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[BackRoom] race handoff failed");
                try { RoomReturn(); } catch (Exception ex2) { Log.Warning(ex2, "[BackRoom] return failed"); }
            }
        }

        /// <summary>Called on the room, at the door: remember the trip and how the room ends up closing.</summary>
        private void ArmRaceHandoff()
        {
            int tripAt = _roomTripEpoch;
            bool byHost = false;
            Closing += (_, e) => byHost = e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing;
            Closed += (_, _) => OpenRaceFromRoom(tripAt, byHost);
        }

        /// <summary>Called on the race: when it ends by itself (the page's exit, the host's own close, the
        /// watchdog), the room comes back. Idempotent.</summary>
        internal void ArmRoomReturn(int tripAt)
        {
            _raceFromRoom = true;
            if (_roomReturnOwed) return;
            _roomReturnOwed = true;
            _roomTripAt = tripAt;
            if (IsClosedOrClosing) { FinishRoomTrip(true); return; }
            bool byHost = false;
            Closing += (_, e) => byHost = e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing;
            Closed += (_, _) => FinishRoomTrip(byHost);
        }

        private void FinishRoomTrip(bool byHost)
        {
            if (!_roomReturnOwed) return;
            _roomReturnOwed = false;
            if (!byHost || _roomTripAt != _roomTripEpoch) return;
            try { RoomReturn(); }
            catch (Exception ex) { Log.Warning(ex, "[BackRoom] return failed"); }
        }

        internal void MarkRaceFromRoom() => _raceFromRoom = true;

        internal static int RoomTripEpoch => _roomTripEpoch;
    }
}
