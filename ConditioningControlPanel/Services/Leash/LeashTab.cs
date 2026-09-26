using System;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>Where leash time meets Circe's tab. Tests hand in a fake; the app's one is
/// <see cref="ChasterLeashTab"/>.</summary>
public interface ILeashTab
{
    /// <summary>Book a Chaster punishment (+seconds on row <c>leash</c>). Returns what was applied.</summary>
    int BookPunish(int seconds);

    /// <summary>Book a credit reward (-seconds on row <c>leash_credit</c>). Returns what was applied (negative or 0).</summary>
    int BookCredit(int seconds);
}

/// <summary>
/// What a leash may ask of the tab, before the tab's own rules run (CONTRACT "The client").
/// A Chaster punishment books only at Strict (the server refuses it below that; the client refuses
/// it too) and only in the preset sizes; a credit only in the preset sizes. Everything after this
/// goes through <c>ChasterService.NoteSeconds</c>, which holds the player's OWN limits: the rows
/// are opt-in (nothing books unless the player switched <c>leash</c> / <c>leash_credit</c> on),
/// the daily limit and the backlog limit clamp, the safety hold after panic refuses, a Remote
/// session's share applies, and a credit never reaches past the floor. There is no separate leash
/// budget (owner, 2026-09-26). Pure.
/// </summary>
public static class LeashChasterRule
{
    public static int PunishSeconds(int size, LeashIntensity? intensity)
    {
        if (intensity != LeashIntensity.Strict) return 0;
        foreach (var s in LeashGrammar.PunishSizes(PunishKind.Chaster)) if (s == size) return size;
        return 0;
    }

    public static int CreditSeconds(int? size)
    {
        if (size is not int s) return 0;
        foreach (var c in LeashGrammar.CreditSizes) if (c == s) return s;
        return 0;
    }
}

/// <summary>The app's tab: <c>App.Chaster</c>, the same NoteSeconds path an Awareness trigger uses.
/// The row ids are literal on purpose (the price-row wiring test reads them).</summary>
public sealed class ChasterLeashTab : ILeashTab
{
    public int BookPunish(int seconds)
    {
        if (seconds <= 0) return 0;
        try { return App.Chaster?.NoteSeconds("leash", seconds).AppliedSeconds ?? 0; }
        catch (Exception ex) { App.Logger?.Debug("Leash tab punish failed: {E}", ex.Message); return 0; }
    }

    public int BookCredit(int seconds)
    {
        if (seconds <= 0) return 0;
        try { return App.Chaster?.NoteSeconds("leash_credit", -seconds).AppliedSeconds ?? 0; }
        catch (Exception ex) { App.Logger?.Debug("Leash tab credit failed: {E}", ex.Message); return 0; }
    }
}
