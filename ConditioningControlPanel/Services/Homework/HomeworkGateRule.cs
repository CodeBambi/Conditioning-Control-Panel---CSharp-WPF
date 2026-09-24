namespace ConditioningControlPanel.Services.Homework;

/// <summary>What the gate needs to know about the world, read fresh at every check.</summary>
public readonly record struct HomeworkGateInputs(
    bool Due,
    bool SessionRunning,
    bool GameUp,
    bool LockCardOpen,
    bool FullscreenEffect,
    bool ModalUp,
    bool PanelAway,
    bool Watching);

/// <summary>
/// When the "Homework due" card may stand over the panel. Due is the server's word (enabled, opted
/// in, a pick exists, not handed in). Everything else is "the user is busy", and the card never
/// lands on a busy user: it waits for the next idle moment instead, and a card already up steps
/// aside the moment something starts (a scheduled session, a remote command, a game).
/// </summary>
public static class HomeworkGateRule
{
    public static bool Busy(in HomeworkGateInputs w) =>
        w.SessionRunning || w.GameUp || w.LockCardOpen || w.FullscreenEffect || w.ModalUp || w.PanelAway || w.Watching;

    public static bool ShouldShow(in HomeworkGateInputs w) => w.Due && !Busy(w);
}
