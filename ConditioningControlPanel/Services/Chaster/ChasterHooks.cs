using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>
/// Where CCP's events meet the price table. Everything that already raises an event is
/// subscribed to here, once, so the services that own those events never hear the word Chaster.
/// The few sites with no event (a finished lock card, a missed attention check, a finished
/// session, a video played to its end, a broken mantra streak, a remote picture) call
/// <c>App.Chaster?.Note(...)</c> themselves, one line each.
///
/// <para>Every call is inert until the tab is on, an account is linked AND the player switched
/// that row on, so attaching costs nothing for everyone else. A handler never throws: a price
/// must not be able to break the feature it is priced on.</para>
/// </summary>
public static class ChasterHooks
{
    /// <summary>Which Lockdown tripwires are "trying to leave". Close, Stop and a wrong phrase
    /// are. The emergency exit NEVER is. Minimize is allowed, a system key is usually a
    /// reflex, a greyed toggle is curiosity and a starved dose is not an exit at all.</summary>
    public static bool EscapeCosts(string? kind) =>
        kind is EscapeKinds.Close or EscapeKinds.Stop or EscapeKinds.WrongPhrase;

    public static string QuestRow(QuestType type) => type == QuestType.Weekly ? "quest_weekly" : "quest";

    /// <summary>The Back Room slot (CONTRACT 10.24). The bridge hands over the SERVER'S line for an
    /// outcome that really landed, read off the tape the host relayed, so a free demo spin and the
    /// <c>fx.melt</c> / <c>fx.jackpot</c> it fires can never reach here. The melt line books the melt
    /// row; the jackpot line (<c>emi3</c>) wipes the tab. Every other line books nothing.</summary>
    public static string? SlotLineRow(string? line) => line switch
    {
        "melt" => "melt",
        "emi3" => CircesTab.JackpotEventId,
        _ => null,
    };

    public static void SlotLanded(string? line)
    {
        if (App.Chaster is not { } chaster) return;
        switch (SlotLineRow(line))
        {
            case "melt": Safe(() => chaster.Note("melt")); break;
            case CircesTab.JackpotEventId: Safe(() => chaster.Wipe()); break;
        }
    }

    private static bool _attached;

    /// <summary>Call once, after the services below exist.</summary>
    public static void Attach(ChasterService chaster)
    {
        if (_attached) return;
        _attached = true;
        try
        {
            if (App.Quests != null) App.Quests.QuestCompleted += (_, e) => Safe(() => chaster.Note(QuestRow(e.QuestType)));
            if (App.Progression != null) App.Progression.LevelUp += (_, _) => Safe(() => chaster.Note("levelup"));
            if (App.Programs != null)
            {
                App.Programs.DayCompleted += (_, _) => Safe(() => chaster.Note("program_done"));
                App.Programs.DayMissed += (_, _) => Safe(() => chaster.Note("program_skipped"));
            }
            if (App.Lockdown != null)
                App.Lockdown.EscapeAttempted += attempt => Safe(() => { if (EscapeCosts(attempt.Kind)) chaster.Note("escape"); });
        }
        catch (Exception ex) { Diag.Swallowed(ex, "chaster hooks attach"); }
    }

    private static void Safe(Action book)
    {
        try { book(); }
        catch (Exception ex) { Diag.Swallowed(ex, "chaster hook"); }
    }
}
