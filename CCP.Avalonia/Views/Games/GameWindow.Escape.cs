// PORTED from ConditioningControlPanel/MainWindow/MainWindow.xaml.cs TryRacePauseOnEscape (7.1.5) +
// BackRoomHostService / CaucusHostService / PieceByPieceHostService .IsInFront / .IsReady / .PostKeptEscape.
// Escape in front of Racing Thoughts, the chess board or Breakout is that game's pause, not a panic
// (owner 2026-09-25, 2026-09-29; tester report 2026-09-30). Every condition is Core
// PanicPolicy.GameClaimsEscapeAsPause: Escape is the panic key, a game window is in front, no engine
// session runs, no Lock Card is open, and the previous claim was not inside 2 s (a quick second Escape
// is a full panic). The kept press is handed to the page as well (PanicPolicy.KeptEscapeGoesTo):
// the page drops it when the real key reached it too (shared/host-escape.js, piecebypiece/ui/host-escape.js,
// dtrh/race/hostEscape.js), so a focused game still handles the press once.

using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        /// <summary>One open game window as the Escape rule sees it.</summary>
        internal readonly record struct EscapeCandidate(string Id, bool InFront, bool Ready, Action<object> Post);

        /// <summary>When a game last kept an Escape as its pause (WPF _lastRaceEscapeClaimUtc).</summary>
        private static DateTime? _lastEscapeClaimUtc;

        internal static void ResetEscapeClaimForTest() => _lastEscapeClaimUtc = null;

        internal static bool IsRaceId(string id) => id.Equals("race", StringComparison.OrdinalIgnoreCase);
        internal static bool IsBoardId(string id) => id.Equals("piecebypiece", StringComparison.OrdinalIgnoreCase);

        /// <summary>The frame each page listens for (CaucusHostService / BackRoomHostService "kept-escape",
        /// PieceByPieceHostService "pbp:escape").</summary>
        internal static string KeptEscapeFrame(string id) => IsBoardId(id) ? "pbp:escape" : "kept-escape";

        /// <summary>The live candidates: every open game window, in front = the active window.</summary>
        private static List<EscapeCandidate> LiveCandidates()
        {
            GameWindow[] all;
            lock (Open) all = Open.ToArray();
            return all.Where(w => !w.IsClosedOrClosing)
                .Select(w => new EscapeCandidate(w.Spec.Id, w.IsActive, w.IsReady, w.Post))
                .ToList();
        }

        /// <summary>UI thread, first thing in the panic handler (after the Lockdown / capture refusals,
        /// as WPF's OnGlobalKeyPressed filters those before HandlePanicKeyPress). True = the press was a
        /// game's pause and nothing else may run for it. A probe that throws never eats a panic press.</summary>
        internal static bool TryKeepEscapeAsPause(string? panicKey, bool engineRunning, bool lockCardOpen, DateTime nowUtc)
        {
            try { return TryKeepEscapeAsPause(LiveCandidates(), panicKey, engineRunning, lockCardOpen, nowUtc); }
            catch (Exception ex)
            {
                Log.Warning("PANIC: game pause probe failed: {Error}", ex.Message);
                return false;
            }
        }

        /// <summary>The rule over explicit candidates (the seam the tests drive).</summary>
        internal static bool TryKeepEscapeAsPause(IReadOnlyList<EscapeCandidate> games, string? panicKey,
            bool engineRunning, bool lockCardOpen, DateTime nowUtc)
        {
            var race = games.FirstOrDefault(g => g.InFront && IsRaceId(g.Id));
            var board = games.FirstOrDefault(g => g.InFront && IsBoardId(g.Id));
            var breakout = games.FirstOrDefault(g => g.InFront && IsBreakoutPage(g.Id));
            bool raceInFront = race.Id != null, boardInFront = board.Id != null, breakoutInFront = breakout.Id != null;
            bool claim = PanicPolicy.GameClaimsEscapeAsPause(panicKey,
                gameInFront: raceInFront || boardInFront || breakoutInFront,
                engineRunning: engineRunning,
                lockCardOpen: lockCardOpen,
                lastClaimUtc: _lastEscapeClaimUtc,
                nowUtc: nowUtc);
            if (!claim) { _lastEscapeClaimUtc = null; return false; }
            _lastEscapeClaimUtc = nowUtc;
            Log.Information("PANIC: Escape kept by {Game} as its pause (again within 2 s = full panic)",
                raceInFront ? "Racing Thoughts" : boardInFront ? "Piece by Piece" : "Breakout");
            var target = PanicPolicy.KeptEscapeGoesTo(claim,
                raceInFront, race.Ready, boardInFront, board.Ready, breakoutInFront, breakout.Ready) switch
            {
                PanicPolicy.KeptEscapePage.Race => race,
                PanicPolicy.KeptEscapePage.Board => board,
                PanicPolicy.KeptEscapePage.Breakout => breakout,
                _ => default,
            };
            if (target.Id != null)
            {
                try { target.Post(new { type = KeptEscapeFrame(target.Id) }); }
                catch (Exception ex) { Log.Debug("PANIC: kept Escape post failed: {E}", ex.Message); }
            }
            return true;
        }
    }
}
