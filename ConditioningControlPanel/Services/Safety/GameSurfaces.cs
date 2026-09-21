using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Safety;

/// <summary>
/// The one list of full-screen game/feed surfaces the panic key has to know about: which of them
/// owns the screen right now, and how to take each one down.
///
/// <para><b>Why it exists.</b> The panic ladder kept that list twice - once in MainWindow's
/// <c>AnyGameSurfaceOwnsTheScreen</c> probe and once in <c>PanicStopEverySurface</c>'s stop pass -
/// and both copies had drifted behind the launcher. Racing Thoughts, the Goon Game and the Graded
/// Intake window were in neither, so a panic press inside one of them stopped nothing it could see
/// AND advanced the double-press exit counter: two Escapes inside Racing Thoughts (where Escape is
/// the BRAKE) quit the whole app. That is the Sep 19 2026 support report "why does pressing Esc 2-3
/// times exit the app? Whilst am ingame?".</para>
///
/// <para>Panic itself is unchanged and stays absolute: the press still stops everything. The rule
/// this list restores is the one <see cref="PanicPolicy.AdvancesExitLadder(PanicPolicy.Rung, bool)"/>
/// already wrote down - a press that takes a game down closes the GAME, and only a further press
/// with the game already gone can exit the app.</para>
///
/// <para>Kept out of MainWindow, and out of WPF, so the membership can be tested against
/// <c>LauncherCatalogue</c> without opening a window. <see cref="All"/> is settable for exactly
/// that reason; production never assigns it.</para>
/// </summary>
internal static class GameSurfaces
{
    /// <summary>One surface. <paramref name="Id"/> matches the launcher's game id where there is
    /// one, so the drift guard can compare the two lists.
    ///
    /// <para>One id does not mean what the launcher tile means: <c>intake</c> here is
    /// <see cref="Quiz.IntakeHostService"/>'s own WebView2 window (the Lab / remote route), NOT the
    /// launcher tile of that name, which opens the Graded Intake as a PANEL TAB and reports
    /// <c>IsActive</c> false forever. The panic key has to close the window; the tab goes down with
    /// the panel's own stop.</para></summary>
    internal sealed record Surface(string Id, Func<bool> IsActive, Action Close);

    /// <summary>
    /// Every surface, in the order the stop pass takes them down. Order is not load-bearing: each
    /// close is idempotent and independent.
    /// </summary>
    internal static IReadOnlyList<Surface> All { get; set; } = Build();

    private static List<Surface> Build() => new()
    {
        // The Rabbit Hole descent runs inside the app rather than in a window of its own, but it
        // owns the screen exactly as hard as the rest.
        new("chaos", () => App.Chaos?.IsDescending == true, () => App.Chaos?.ForceShutdown()),
        new("dtrh", () => Chaos.DtrhHostService.IsActive, Chaos.DtrhHostService.CloseActive),
        new("arcademy", () => Arcademy.ArcademyHostService.IsActive, Arcademy.ArcademyHostService.CloseActive),
        new("backroom", () => BackRoom.BackRoomHostService.IsActive, () => BackRoom.BackRoomHostService.CloseActive("panic")),
        new("fyp", () => Fyp.FypHostService.IsActive, Fyp.FypHostService.Close),
        new("justdrop", () => JustDrop.JustDropHostService.IsActive, JustDrop.JustDropHostService.CloseActive),
        // The four the two copies had missed. Racing Thoughts is the reported one; the other three
        // are the same shape (a WebView2 game window the player is sitting in).
        new("race", () => Chaos.CaucusHostService.IsActive, Chaos.CaucusHostService.CloseActive),
        new("goon", () => GoonGame.GoonHostService.IsActive, GoonGame.GoonHostService.CloseActive),
        new("piecebypiece", () => PieceByPiece.PieceByPieceHostService.IsActive, PieceByPiece.PieceByPieceHostService.Close),
        new("intake", () => Quiz.IntakeHostService.IsActive, Quiz.IntakeHostService.CloseActive),
    };

    /// <summary>True while any of them is on screen. Read BEFORE the stop pass closes them. Never
    /// throws: a dead host service must not be able to eat a panic press.</summary>
    internal static bool AnyOwnsTheScreen() => ActiveIds().Count > 0;

    /// <summary>The ids that are up right now, for the diagnostic line. A probe that throws is
    /// logged and counted as closed.</summary>
    internal static IReadOnlyList<string> ActiveIds()
    {
        var live = new List<string>();
        foreach (var s in All)
        {
            try { if (s.IsActive()) live.Add(s.Id); }
            catch (Exception ex)
            {
                try { App.Logger?.Warning("PANIC: {Id} active-probe failed: {Error}", s.Id, ex.Message); } catch { }
            }
        }
        return live;
    }

    /// <summary>
    /// Close every one of them. <paramref name="step"/> is the caller's guarded runner (the stop
    /// pass's own <c>Step</c>), so one host that throws cannot starve the rest.
    ///
    /// <para>The stop pass wants this shape: closing is idempotent on every host, so it does not
    /// ask who is up first and cannot be wrong about it either.</para>
    /// </summary>
    internal static void CloseAll(Action<string, Action> step)
    {
        foreach (var s in All) step(s.Id, s.Close);
    }

    /// <summary>
    /// Close only the named surfaces. For a caller that has already probed - the legacy ladder's
    /// rung closes the games that ARE up, and must not reach past them into a Rabbit Hole descent
    /// that its own rung above had already declined to touch.
    /// </summary>
    internal static void CloseAll(IEnumerable<string> ids, Action<string, Action> step)
    {
        var wanted = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        foreach (var s in All)
            if (wanted.Contains(s.Id)) step(s.Id, s.Close);
    }

    /// <summary>The ids in the registry, for the drift guard against the launcher catalogue.</summary>
    internal static IReadOnlyList<string> Ids() => All.Select(s => s.Id).ToList();
}
