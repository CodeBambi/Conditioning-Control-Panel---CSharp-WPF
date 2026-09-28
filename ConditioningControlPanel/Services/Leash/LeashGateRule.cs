using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>What the leash gate needs to know about the world, read fresh at every check. The
/// same busy shape as the Homework gate, kept in its own file on purpose.</summary>
public readonly record struct LeashGateInputs(
    bool Due,
    bool SessionRunning,
    bool GameUp,
    bool LockCardOpen,
    bool FullscreenEffect,
    bool ModalUp,
    bool PanelAway,
    bool Watching);

/// <summary>Which gate stands over the panel when more than one is due.</summary>
public enum GateChoice { None, Leash, Homework }

/// <summary>
/// When the leash gate may stand over the panel (CONTRACT "The gate"). Due = a pending
/// punishment that needs the gate. Everything else is "busy": the card never lands on a busy
/// user and steps aside the moment something starts. While the gate's own task runs (a session,
/// a lock card, the watch) the gate is busy by the same inputs, so it never covers its own task.
/// Pure.
/// </summary>
public static class LeashGateRule
{
    public static bool Busy(in LeashGateInputs w) =>
        w.SessionRunning || w.GameUp || w.LockCardOpen || w.FullscreenEffect || w.ModalUp || w.PanelAway || w.Watching;

    public static bool ShouldShow(in LeashGateInputs w) => w.Due && !Busy(w);

    /// <summary>The leash gate goes first when both are due.</summary>
    public static GateChoice Pick(bool leashDue, bool homeworkDue) =>
        leashDue ? GateChoice.Leash : homeworkDue ? GateChoice.Homework : GateChoice.None;

    /// <summary>The punishment the gate shows: the oldest one that still needs the gate. A
    /// Chaster punishment never does (it books itself), nor does an expired one, nor one this
    /// client already completed and is waiting for the server to drop.</summary>
    public static Punishment? Due(IReadOnlyList<Punishment>? pending, DateTimeOffset now, ISet<string>? completedLocally = null)
    {
        if (pending == null) return null;
        Punishment? best = null;
        foreach (var p in pending)
        {
            if (p.Kind == PunishKind.Chaster) continue;
            if (p.ExpiresAt <= now) continue;
            if (completedLocally != null && completedLocally.Contains(p.Pid)) continue;
            if (best == null || p.At < best.At) best = p;
        }
        return best;
    }
}
